using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers.Greenfield;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.AutoForward.Data;
using BTCPayServer.Services.Invoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin;
using PayoutData = BTCPayServer.Client.Models.PayoutData;

namespace BTCPayServer.Plugins.AutoForward.Services;

public class AutoForwardInvoiceHelper
{
    private readonly ApplicationDbContextFactory _applicationDbContextFactory;
    private readonly InvoiceRepository _invoiceRepository;
    private readonly IBTCPayServerClientFactory _btcPayServerClientFactory;
    private readonly ILogger _logger;


    public AutoForwardInvoiceHelper(ApplicationDbContextFactory applicationDbContextFactory,
        InvoiceRepository invoiceRepository, IBTCPayServerClientFactory btcPayServerClientFactory,
        ILoggerFactory loggerFactory)
    {
        _applicationDbContextFactory = applicationDbContextFactory;
        _invoiceRepository = invoiceRepository;
        _btcPayServerClientFactory = btcPayServerClientFactory;
        _logger = loggerFactory.CreateLogger("AutoForward");
    }


    public static Money GetAmountReceived(InvoiceEntity invoice, string paymentMethod)
    {
        Money total = new((long)0);

        // TODO should we loop to include Lightning? Not supported for now...
        var paymentMethodObj = invoice.GetPaymentMethod(PaymentMethodId.Parse(paymentMethod));

        // TODO don't hard code for BTC-Onchain
        var data = paymentMethodObj.Calculate();
        total += data.CryptoPaid;

        return total;
    }

    public async Task<InvoiceEntity[]> GetAutoForwardableInvoices()
    {
        // TODO this method does not scale and will be very slow if the invoice list is long
        return await GetInvoicesBySql(
            $"select * FROM \"Invoices\" where \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardToAddress' is not null and \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardPercentage' is not null");
    }

    public async Task<InvoiceEntity[]> GetAutoForwardableInvoicesNotPaidOut()
    {
        // TODO this method does not scale and will be very slow if the invoice list is long
        return await GetInvoicesBySql(
            $"select * FROM \"Invoices\" where \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardToAddress' is not null and \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardPercentage' is not null and \"Blob2\"::jsonb -> 'metadata' -> 'AutoForwardCompletedPayoutId' is null and \"Status\" = 'complete'");
    }

    public async Task<InvoiceEntity[]> GetUnprocessedInvoicesLinkedToDestination(string destination, string storeId)
    {
        // TODO this method does not scale and will be very slow if the invoice list is long
        // TODO switch to SQL prepared statements
        string sql =
            $"select * FROM \"Invoices\" where \"Blob2\"::jsonb -> 'metadata' ->> 'autoForwardToAddress' = '{destination}' and \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardPercentage' is not null and \"Blob2\"::jsonb -> 'metadata' -> 'AutoForwardCompletedPayoutId' is null and \"Status\" = 'complete' and \"StoreDataId\" = '{storeId}'";
        return await GetInvoicesBySql(sql);
    }

    private async Task<InvoiceEntity[]> GetInvoicesBySql(string sql)
    {
        using var context = _applicationDbContextFactory.CreateContext();
        IQueryable<BTCPayServer.Data.InvoiceData> query =
            context
                .Invoices.FromSqlRaw(sql).OrderByDescending(invoice => invoice.Created)
                .Include(o => o.Payments)
                .Include(o => o.Refunds).ThenInclude(refundData => refundData.PullPaymentData);

        return (await query.ToListAsync()).Select(o => _invoiceRepository.ToEntity(o)).ToArray();
    }

    public string[] GetCompletedPayoutIds()
    {
        // TODO this method does not scale and will be very slow if the invoice list is long
        string sql =
            "select distinct \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardCompletedPayoutId' as payoutId FROM \"Invoices\" where \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardToAddress' is not null and \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardPercentage' is not null and \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardCompletedPayoutId' is not null";
        return null;
    }

    // public async Task CreatePayoutForAllSettledInvoices(CancellationToken cancellationToken)
    // {
    //     var invoicesWithoutPayout = await GetAutoForwardableInvoicesMissingAPayout();
    //     foreach (var invoice in invoicesWithoutPayout)
    //     {
    //         CreatePayoutForInvoice(invoice, cancellationToken);
    //     }
    // }

    private async Task<BTCPayServerClient> GetClient(string storeId)
    {
        return await _btcPayServerClientFactory.Create(null, storeId);
    }


    private async Task<PayoutData> CreatePayout(string destinationAddress, decimal amount, string paymentMethod,
        string storeId, bool subtractFromAmount, CancellationToken cancellationToken)
    {
        var client = await GetClient(storeId);
        CreateOnChainTransactionRequest createOnChainTransactionRequest = new CreateOnChainTransactionRequest
        {
            ProceedWithBroadcast = false
        };
        var destination = new CreateOnChainTransactionRequest.CreateOnChainTransactionRequestDestination
        {
            Destination = destinationAddress, Amount = amount, SubtractFromAmount = subtractFromAmount
        };
        createOnChainTransactionRequest.Destinations =
            new List<CreateOnChainTransactionRequest.CreateOnChainTransactionRequestDestination>();
        createOnChainTransactionRequest.Destinations.Add(destination);

        CreatePayoutThroughStoreRequest createPayoutThroughStoreRequest = new()
            { Amount = amount, Destination = destinationAddress, PaymentMethod = paymentMethod };

        var payout = await client.CreatePayout(storeId, createPayoutThroughStoreRequest, cancellationToken);
        return payout;
    }


    /**
     * Make sure get given invoice is given a payout
     */
    public async void SyncPayoutForInvoice(InvoiceEntity invoice, CancellationToken cancellationToken)
    {
        if (!CanInvoiceBePaidOut(invoice))
        {
            throw new System.Exception(
                $"Invoice ID {invoice.Id} should be completed. Cannot sync payout for this invoice.");
        }

        var metaJson = invoice.Metadata.ToJObject();
        AutoForwardInvoiceMetadata newMeta = AutoForwardInvoiceMetadata.FromJObject(metaJson);
        var paymentMethod = "BTC-OnChain"; // TODO make dynamic

        if (!String.IsNullOrEmpty(newMeta.AutoForwardPayoutId))
        {
            // The invoice is already linked to a payout.
            var payout = await GetPayoutById(newMeta.AutoForwardPayoutId, invoice.StoreId, cancellationToken);
            if (payout != null)
            {
                // It is possible the payout was just completed, but the invoice doesn't know about it.
                if (payout.State == PayoutState.Completed)
                {
                    newMeta.AutoForwardCompleted = true;
                    invoice.Metadata = newMeta;
                    await _invoiceRepository.UpdateInvoiceMetadata(invoice.Id, invoice.StoreId, newMeta.ToJObject());
                }
            }
        }

        if (!newMeta.AutoForwardCompleted)
        {
            // This invoice was not paid out yet
            var destination = newMeta.AutoForwardToAddress;
            bool subtractFromAmount = newMeta.AutoForwardSubtractFeeFromAmount;
            await CreateOrUpdatePayout(paymentMethod, destination, invoice.StoreId, subtractFromAmount,
                cancellationToken);
        }
    }

    public async Task<PayoutData> GetPayoutForDestination(string cryptoCode, string destination, string storeId,
        CancellationToken cancellationToken)
    {
        var client = await GetClient(storeId);
        var payouts = await client.GetStorePayouts(storeId, false, cancellationToken);
        foreach (var onePayout in payouts)
        {
            if (onePayout.Destination.Equals(destination) && onePayout.CryptoCode.Equals(cryptoCode) &&
                onePayout.State != PayoutState.Completed)
            {
                return onePayout;
            }
        }

        return null;
    }

    private async Task CreateOrUpdatePayout(string paymentMethod, string destination, string storeId,
        bool subtractFromAmount, CancellationToken cancellationToken)
    {
        var client = await GetClient(storeId);
        var cryptoCode = paymentMethod.Split("-")[0];
        PayoutData payout = await GetPayoutForDestination(cryptoCode, destination, storeId, cancellationToken);
        List<InvoiceEntity> invoicesIncludedInPayout = new();

        var invoices = await GetUnprocessedInvoicesLinkedToDestination(destination, storeId);
        decimal totalAmount = 0;

        foreach (var invoiceEntity in invoices)
        {
            var amountReceived = GetAmountReceived(invoiceEntity, paymentMethod).ToDecimal(MoneyUnit.BTC);
            if (amountReceived > 0)
            {
                invoicesIncludedInPayout.Add(invoiceEntity);
                totalAmount += amountReceived;
            }
        }

        if (payout != null)
        {
            // TODO maybe "subtractFromAmount" is different now, and we should still cancel? Where is this stored in the payout?
            if (payout.Amount.Equals(totalAmount))
            {
                // The existing payout is correct. No need to change anything.
                return;
            }

            // Cancel the previous payout
            await client.CancelPayout(storeId, payout.Id, cancellationToken);

            foreach (var invoice in invoices)
            {
                await WriteToLog(
                    $"Cancelled previous Payout ID {payout.Id} because the amount should be {totalAmount} {cryptoCode} instead of {payout.Amount}",
                    invoice.Id);
            }
        }

        try
        {
            // Create a new payout for the correct amount
            payout = await CreatePayout(destination, totalAmount, paymentMethod, storeId, subtractFromAmount,
                cancellationToken);

            string invoiceText = "";
            for (int i = 0; i < invoicesIncludedInPayout.Count; i++)
            {
                bool isFirst = i == 0;
                bool isLast = i == invoicesIncludedInPayout.Count - 1;

                if (!isFirst)
                {
                    if (isLast)
                    {
                        invoiceText += " and ";
                    }
                    else
                    {
                        invoiceText += ", ";
                    }
                }

                invoiceText += invoicesIncludedInPayout[i].Id;
            }

            foreach (var invoice in invoicesIncludedInPayout)
            {
                await WriteToLog(
                    $"Created new Payout ID {payout.Id} for {totalAmount} {cryptoCode} containing the payouts for invoices {invoiceText}.",
                    invoice.Id);

                // Link the invoice to the payout
                var metaJson = invoice.Metadata.ToJObject();
                AutoForwardInvoiceMetadata newMeta = AutoForwardInvoiceMetadata.FromJObject(metaJson);
                newMeta.AutoForwardPayoutId = payout.Id;

                await _invoiceRepository.UpdateInvoiceMetadata(invoice.Id, invoice.StoreId, newMeta.ToJObject());
            }
        }
        catch (GreenfieldAPIException e)
        {
            if (e.APIError.Code.Equals("duplicate-destination"))
            {
                var existingPayout = await GetPayoutForDestination(cryptoCode, destination, storeId, cancellationToken);

                if (existingPayout == null)
                {
                    // TODO can we solve this better? Payouts would need refactoring so they are properly store scoped...
                    // The payout exists in another store, so we're screwed.

                    _logger.LogInformation(
                        "A payout already exists to {Destination} in another store. Find the store and cancel that payout, so it can be created in this store.",
                        destination);
                }
                else
                {
                    if (existingPayout.Amount.Equals(totalAmount))
                    {
                        // A payout with this destination already exists and the amount is accurate. Do nothing.
                        _logger.LogInformation(
                            "A payout already exists to {Destination} with the correct amount ({TotalAmount} {CryptoCode}).",
                            destination, totalAmount, cryptoCode);
                    }
                    else
                    {
                        // The amount does not match. This is serious!
                        _logger.LogError(
                            "Could not create payout to {Destination} for {TotalAmount} {CryptoCode}. One already exists, but this was not expected.",
                            destination, totalAmount, cryptoCode);

                        // TODO should we throw?
                        //throw;
                    }
                }

                // TODO is this ok? This seams to happen a lot. Are we having race conditions?
            }
            else
            {
                throw;
            }
        }
    }

    private async Task WriteToLog(string message, string invoiceId)
    {
        _logger.LogInformation("Invoice {InvoiceId}: {Message}", invoiceId, message);

        InvoiceLogs logs = new InvoiceLogs();
        string prefix = "Auto-Forwarding: ";

        logs.Write($"{prefix}{message}", InvoiceEventData.EventSeverity.Info);
        await _invoiceRepository.AddInvoiceLogs(invoiceId, logs);
    }

    public async Task<PayoutData> GetPayoutById(string id, string storeId, CancellationToken cancellationToken)
    {
        var client = await GetClient(storeId);
        var payout = await client.GetStorePayout(storeId, id, cancellationToken);
        return payout;
    }


    public bool CanInvoiceBePaidOut(InvoiceEntity invoice)
    {
        if (invoice.Status == InvoiceStatusLegacy.Complete)
        {
            var metaJson = invoice.Metadata.ToJObject();
            AutoForwardInvoiceMetadata newMeta = AutoForwardInvoiceMetadata.FromJObject(metaJson);
            if (!String.IsNullOrEmpty(newMeta.AutoForwardToAddress) && newMeta.AutoForwardCompleted == false &&
                newMeta.AutoForwardPercentage is > 0 and < 1)
            {
                return true;
            }
        }

        return false;
    }
}