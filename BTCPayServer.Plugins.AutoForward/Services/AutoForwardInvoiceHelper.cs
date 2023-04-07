using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.AutoForward.Data;
using BTCPayServer.Services.Invoices;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using PayoutData = BTCPayServer.Client.Models.PayoutData;

namespace BTCPayServer.Plugins.AutoForward.Services;

public class AutoForwardInvoiceHelper
{
    private readonly ApplicationDbContextFactory _applicationDbContextFactory;
    private readonly InvoiceRepository _invoiceRepository;
    private readonly IBTCPayServerClientFactory _btcPayServerClientFactory;
    private readonly PullPaymentHostedService _pullPaymentService;

    public AutoForwardInvoiceHelper(ApplicationDbContextFactory applicationDbContextFactory, InvoiceRepository invoiceRepository, IBTCPayServerClientFactory btcPayServerClientFactory)
    {
        _applicationDbContextFactory = applicationDbContextFactory;
        _invoiceRepository = invoiceRepository;
        _btcPayServerClientFactory = btcPayServerClientFactory;
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
        return await GetInvoicesBySql($"select * FROM \"Invoices\" where \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardToAddress' is not null and \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardPercentage' is not null order by \"Created\" desc");
    }

    public async Task<InvoiceEntity[]> GetAutoForwardableInvoicesNotPaidOut()
    {
        // TODO this method does not scale and will be very slow if the invoice list is long
        return await GetInvoicesBySql($"select * FROM \"Invoices\" where \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardToAddress' is not null and \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardPercentage' is not null and \"Blob2\"::jsonb -> 'metadata' -> 'AutoForwardCompletedPayoutId' is null and status = \"settled\" order by \"Created\" desc");
    }

    public async Task<InvoiceEntity[]> GetUnprocessedInvoicesLinkedToDestination(string destination, string storeId)
    {
        // TODO this method does not scale and will be very slow if the invoice list is long
        return await GetInvoicesBySql($"select * FROM \"Invoices\" where \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardToAddress' = \"{destination}\" and \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardPercentage' is not null and \"Blob2\"::jsonb -> 'metadata' -> 'AutoForwardCompletedPayoutId' is null and status = \"settled\" and StoreDataId = \"{storeId}\" order by \"Created\" desc");
    }

    private async Task<InvoiceEntity[]> GetInvoicesBySql(FormattableString sql)
    {
        using var context = _applicationDbContextFactory.CreateContext();
        IQueryable<BTCPayServer.Data.InvoiceData> query =
            context
                .Invoices.FromSqlInterpolated(sql)
                .Include(o => o.Payments)
                .Include(o => o.Refunds).ThenInclude(refundData => refundData.PullPaymentData);

        return (await query.ToListAsync()).Select(o => _invoiceRepository.ToEntity(o)).ToArray();
    }

    public string[] GetCompletedPayoutIds()
    {
        // TODO this method does not scale and will be very slow if the invoice list is long
        string sql = "select distinct \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardCompletedPayoutId' as payoutId FROM \"Invoices\" where \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardToAddress' is not null and \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardPercentage' is not null and \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardCompletedPayoutId' is not null order by \"Created\" desc ";
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


    private async Task<PayoutData> CreatePayout(string destinationAddress, decimal amount, string paymentMethod, string storeId, CancellationToken cancellationToken)
    {
        var client = await _btcPayServerClientFactory.Create(null, new string[] { storeId });
        CreateOnChainTransactionRequest createOnChainTransactionRequest = new CreateOnChainTransactionRequest();
        createOnChainTransactionRequest.ProceedWithBroadcast = false;
        var destination = new CreateOnChainTransactionRequest.CreateOnChainTransactionRequestDestination
        {
            Destination = destinationAddress, Amount = amount, SubtractFromAmount = false
        };
        createOnChainTransactionRequest.Destinations = new List<CreateOnChainTransactionRequest.CreateOnChainTransactionRequestDestination>();
        createOnChainTransactionRequest.Destinations.Add(destination);

        CreatePayoutThroughStoreRequest createPayoutThroughStoreRequest = new() { Amount = amount, Destination = destinationAddress, PaymentMethod = paymentMethod };

        var payout = await client.CreatePayout(storeId, createPayoutThroughStoreRequest, cancellationToken);
        return payout;
    }


    /**
     * Make sure get given invoice is given a payout
     */
    public async void SyncPayoutForInvoice(InvoiceEntity invoice, CancellationToken cancellationToken)
    {
        if (invoice.Status.ToModernStatus() != InvoiceStatus.Settled)
        {
            throw new Exception($"Invoice ID {invoice.Id} should be settled. Cannot create or update payout for this invoice.");
        }

        var metaJson = invoice.Metadata.ToJObject();
        AutoForwardInvoiceMetadata newMeta = AutoForwardInvoiceMetadata.FromJObject(metaJson);
        var paymentMethod = "BTC-OnChain"; // TODO make dynamic

        if (string.IsNullOrEmpty(newMeta.AutoForwardCompletedPayoutId))
        {
            // This invoice was not paid out yet
            var destination = newMeta.AutoForwardToAddress;
            await CreateOrUpdatePayout(paymentMethod, destination, invoice.StoreId, cancellationToken);
        }
    }

    public async Task<PayoutData> GetPayoutForDestination(string paymentMethod, string destination, string storeId, CancellationToken cancellationToken)
    {
        var client = await _btcPayServerClientFactory.Create(null, storeId);
        var payouts = await client.GetStorePayouts(storeId, false, cancellationToken);
        foreach (var onePayout in payouts)
        {
            if (onePayout.Destination.Equals(destination) && onePayout.PaymentMethod.Equals(paymentMethod))
            {
                return onePayout;
            }
        }

        return null;
    }

    private async Task CreateOrUpdatePayout(string paymentMethod, string destination, string storeId, CancellationToken cancellationToken)
    {
        var client = await _btcPayServerClientFactory.Create(null, new string[] { storeId });
        PayoutData payout = await GetPayoutForDestination(paymentMethod, destination, storeId, cancellationToken);
        List<InvoiceEntity> invoicesIncludedInPayout = new();

        var invoices = await GetUnprocessedInvoicesLinkedToDestination(destination, storeId);
        decimal totalAmount = 0;

        foreach (var invoiceEntity in invoices)
        {
            var amountReceived = GetAmountReceived(invoiceEntity, paymentMethod);
            if (amountReceived > 0)
            {
                invoicesIncludedInPayout.Add(invoiceEntity);
                totalAmount += amountReceived.ToDecimal(MoneyUnit.BTC);
            }
        }

        if (payout != null)
        {
            if (payout.Amount.Equals(totalAmount))
            {
                // The existing payout is correct. No need to change anything.
                return;
            }

            // Cancel the previous payout
            await client.CancelPayout(storeId, payout.Id, cancellationToken);

            foreach (var invoice in invoices)
            {
                await WriteToLog(invoice.Id, $"Cancelled previous Payout ID {payout.Id} because the amount should be {totalAmount} instead of {payout.Amount}");
            }
        }

        // Create a new payout for the correct amount
        payout = await CreatePayout(destination, totalAmount, paymentMethod, storeId, cancellationToken);

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

        foreach (var invoice in invoices)
        {
            await WriteToLog(invoice.Id, $"Created new Payout ID {payout.Id} for {totalAmount} containing the payouts for invoices {invoiceText}.");
        }
    }

    private async Task WriteToLog(string message, string invoiceId)
    {
        WriteToLog($"Invoice {invoiceId}: {message}");

        InvoiceLogs logs = new InvoiceLogs();
        string prefix = "Auto-Forwarding: ";

        logs.Write($"{prefix}{message}", InvoiceEventData.EventSeverity.Info);
        await _invoiceRepository.AddInvoiceLogs(invoiceId, logs);
    }

    public async Task<PayoutData> GetPayoutById(string id, string storeId, CancellationToken cancellationToken)
    {
        var client = await _btcPayServerClientFactory.Create(null, storeId);
        var payout = await client.GetStorePayout(storeId, id, cancellationToken);
        return payout;
    }

    public void WriteToLog(string message)
    {
        // TODO write to log DB table
    }
}