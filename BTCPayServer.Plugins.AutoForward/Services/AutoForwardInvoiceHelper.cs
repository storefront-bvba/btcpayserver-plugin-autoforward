using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.AutoForward.Data;
using BTCPayServer.Plugins.AutoForward.Exception;
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
    private readonly AutoForwardDestinationRepository _autoForwardDestinationRepository;
    private readonly AutoForwardDbContextFactory _autoForwardDbContextFactory;
    const string LogPrefix = "Auto-Forwarding: ";


    public AutoForwardInvoiceHelper(ApplicationDbContextFactory applicationDbContextFactory,
        InvoiceRepository invoiceRepository, IBTCPayServerClientFactory btcPayServerClientFactory,
        ILoggerFactory loggerFactory, AutoForwardDestinationRepository autoForwardDestinationRepository,
        AutoForwardDbContextFactory autoForwardDbContextFactory)
    {
        _applicationDbContextFactory = applicationDbContextFactory;
        _invoiceRepository = invoiceRepository;
        _btcPayServerClientFactory = btcPayServerClientFactory;
        _logger = loggerFactory.CreateLogger("AutoForward");
        _autoForwardDestinationRepository = autoForwardDestinationRepository;
        _autoForwardDbContextFactory = autoForwardDbContextFactory;
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
            $"select * FROM \"Invoices\" where \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardToAddress' is not null and \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardPercentage' is not null and \"Archived\" = 'false'");
    }

    public async Task<InvoiceEntity[]> GetAutoForwardableInvoicesNotPaidOut()
    {
        // TODO this method does not scale and will be very slow if the invoice list is long
        return await GetInvoicesBySql(
            $"select * FROM \"Invoices\" where \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardToAddress' is not null and \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardPercentage' is not null and \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardCompleted' is null and \"Status\" = 'complete' and \"Archived\" = 'false'");
    }

    public async Task<InvoiceEntity[]> GetUnprocessedInvoicesLinkedToDestination(string destination, string storeId)
    {
        // TODO this method does not scale and will be very slow if the invoice list is long
        // TODO switch to SQL prepared statements
        string sql =
            $"select * FROM \"Invoices\" where \"Blob2\"::jsonb -> 'metadata' ->> 'autoForwardToAddress' = '{destination}' and \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardPercentage' is not null and \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardCompleted' is null and \"Status\" = 'complete' and \"StoreDataId\" = '{storeId}' and \"Archived\" = 'false'";
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

    // public string[] GetCompletedPayoutIds()
    // {
    //     // TODO this method does not scale and will be very slow if the invoice list is long
    //     string sql =
    //         "select distinct \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardPayoutId' as payoutId FROM \"Invoices\" where \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardToAddress' is not null and \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardPercentage' is not null and \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardCompleted' = 'true'";
    //     return null;
    // }

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

        // TODO approvind payouts is a manual process for now
        //bool isApproved = IsDestinationApproved(destinationAddress, amount, paymentMethod, storeId);
        bool isApproved = false;

        CreatePayoutThroughStoreRequest createPayoutThroughStoreRequest = new()
            { Amount = amount, Destination = destinationAddress, PaymentMethod = paymentMethod, Approved = isApproved };

        var payout = await client.CreatePayout(storeId, createPayoutThroughStoreRequest, cancellationToken);
        return payout;
    }

    private async Task<bool> IsDestinationAllowed(string destinationAddress, string storeId, string paymentMethod,
        CancellationToken cancellationToken)
    {
        var destination =
            await _autoForwardDestinationRepository.FindByDestination(destinationAddress, storeId, paymentMethod,
                cancellationToken);
        return destination.PayoutsAllowed;

        // TODO check amount/balance allowed to pay out
    }


    /**
     * Make sure get given invoice is handled for the current state it's in.
     */
    public async Task CheckInvoice(InvoiceEntity invoice, bool justCreated, bool justSettled,
        CancellationToken cancellationToken)
    {
        if (!IsValidAutoForwardableInvoice(invoice, false))
        {
            return;
        }

        AutoForwardInvoiceMetadata newMeta = GetMetaForInvoice(invoice);
        var paymentMethod = "BTC-OnChain"; // TODO make dynamic

        await _autoForwardDestinationRepository.EnsureDestinationExists(newMeta.AutoForwardToAddress, invoice.StoreId,
            paymentMethod);

        if (justCreated)
        {
            bool isDestinationAllowed = await IsDestinationAllowed(newMeta.AutoForwardToAddress, invoice.StoreId,
                paymentMethod, cancellationToken);

            if (isDestinationAllowed)
            {
                await WriteToLog($"Destination {newMeta.AutoForwardToAddress} is allowed.", invoice.Id);
            }
            else
            {
                await WriteToLog($"Destination {newMeta.AutoForwardToAddress} is blocked.", invoice.Id);
            }
        }

        if (justSettled)
        {
            // Increase the balance for the destination, which should only be done once.
            await IncreaseBalance(invoice, paymentMethod, cancellationToken);
        }

        if (!String.IsNullOrEmpty(newMeta.AutoForwardPayoutId))
        {
            // The invoice is already linked to a payout.
            var payout = await GetPayoutById(newMeta.AutoForwardPayoutId, invoice.StoreId, cancellationToken);
            if (payout != null)
            {
                // It is possible the payout was just completed, but the invoice doesn't know about it.
                if (payout.State == PayoutState.Completed)
                {
                    await HandleCompletedPayout(invoice);
                    return;
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

    private async Task IncreaseBalance(InvoiceEntity invoice, string paymentMethod,
        CancellationToken cancellationToken)
    {
        var amountReceived = GetAmountReceived(invoice, paymentMethod).ToDecimal(MoneyUnit.BTC);
        if (amountReceived > 0)
        {
            var cryptoCode = paymentMethod.Split("-")[0];
            string destination = GetMetaForInvoice(invoice).AutoForwardToAddress;
            var entity =
                await _autoForwardDestinationRepository.FindByDestination(destination, invoice.StoreId, paymentMethod,
                    cancellationToken);
            decimal oldBalance = entity.Balance;
            entity.Balance += amountReceived;
            await _autoForwardDestinationRepository.Update(entity);

            await WriteToLog($"Increased balance for {destination} from {oldBalance} to {entity.Balance} {cryptoCode}",
                invoice.Id);
        }
    }

    private AutoForwardInvoiceMetadata GetMetaForInvoice(InvoiceEntity invoice)
    {
        var metaJson = invoice.Metadata.ToJObject();
        AutoForwardInvoiceMetadata newMeta = AutoForwardInvoiceMetadata.FromJObject(metaJson);
        invoice.Metadata = newMeta;
        return newMeta;
    }

    private async Task HandleCompletedPayout(InvoiceEntity invoice)
    {
        AutoForwardInvoiceMetadata newMeta = GetMetaForInvoice(invoice);
        newMeta.AutoForwardCompleted = true;

        await _invoiceRepository.UpdateInvoiceMetadata(invoice.Id, invoice.StoreId, newMeta.ToJObject());
        await WriteToLog("Payout completed", invoice.Id);
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
        decimal totalAmountToForward = 0;

        foreach (var invoiceEntity in invoices)
        {
            var payoutForInvoice = await GetPayoutForInvoice(invoiceEntity, cancellationToken);
            if (payoutForInvoice == null || payoutForInvoice.State != PayoutState.Completed)
            {
                var amountReceived = GetAmountReceived(invoiceEntity, paymentMethod).ToDecimal(MoneyUnit.BTC);
                if (amountReceived > 0)
                {
                    var metaJson = invoiceEntity.Metadata.ToJObject();
                    AutoForwardInvoiceMetadata newMeta = AutoForwardInvoiceMetadata.FromJObject(metaJson);

                    decimal amountToForward = amountReceived * newMeta.AutoForwardPercentage;
                    totalAmountToForward += amountToForward;

                    invoicesIncludedInPayout.Add(invoiceEntity);
                }
            }
        }

        bool isDestinationAllowed = await IsDestinationAllowed(destination, storeId, paymentMethod, cancellationToken);

        if (payout != null)
        {
            // TODO maybe "subtractFromAmount" is different now, and we should still cancel? Where is this stored in the payout?

            if (payout.Amount.Equals(totalAmountToForward) && isDestinationAllowed)
            {
                // The existing payout is correct. No need to change anything.
                return;
            }

            // Cancel the previous payout
            await client.CancelPayout(storeId, payout.Id, cancellationToken);

            foreach (var invoice in invoices)
            {
                await WriteToLog(
                    $"Cancelled previous Payout ID {payout.Id} because the amount should be {totalAmountToForward} {cryptoCode} instead of {payout.Amount}",
                    invoice.Id);
            }
        }

        if (totalAmountToForward > 0 && isDestinationAllowed)
        {
            WriteToLog($"Creating payout to {destination} for {totalAmountToForward} {paymentMethod}...");

            try
            {
                // Create a new payout for the correct amount
                payout = await CreatePayout(destination, totalAmountToForward, paymentMethod, storeId,
                    subtractFromAmount,
                    cancellationToken);

                WriteToLog($"Payout created!");


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

                if (invoicesIncludedInPayout.Count() == 1)
                {
                    WriteToLog(
                        $"Found 1 invoice to include in payout to {destination}");
                }
                else
                {
                    WriteToLog(
                        $"Found {invoicesIncludedInPayout.Count()} invoices to include in payout to {destination}");
                }


                foreach (var invoice in invoicesIncludedInPayout)
                {
                    await WriteToLog(
                        $"Created new Payout ID {payout.Id} for {totalAmountToForward} {cryptoCode} containing the payouts for invoices: {invoiceText}.",
                        invoice.Id);

                    // Link the invoice to the payout
                    AutoForwardInvoiceMetadata newMeta = GetMetaForInvoice(invoice);
                    newMeta.AutoForwardPayoutId = payout.Id;

                    await _invoiceRepository.UpdateInvoiceMetadata(invoice.Id, invoice.StoreId, newMeta.ToJObject());
                }
            }
            catch (GreenfieldAPIException e)
            {
                if (e.APIError.Code.Equals("duplicate-destination"))
                {
                    var existingPayout =
                        await GetPayoutForDestination(cryptoCode, destination, storeId, cancellationToken);

                    if (existingPayout == null)
                    {
                        // TODO can we solve this better? Payouts would need refactoring so they are properly store scoped...
                        // The payout exists in another store, so we're screwed.

                        WriteToLog(
                            "A payout already exists to {destination} in another store. Find the store and cancel that payout, so it can be created in this store");
                    }
                    else
                    {
                        if (existingPayout.Amount.Equals(totalAmountToForward))
                        {
                            // A payout with this destination already exists and the amount is accurate. Do nothing.
                            WriteToLog(
                                $"A payout already exists to {destination} with the correct amount ({totalAmountToForward} {cryptoCode})");
                        }
                        else
                        {
                            // The amount does not match. This is serious!
                            WriteToLog(
                                $"Could not create payout to {destination} for {totalAmountToForward} {cryptoCode}. One already exists, but this was not expected");

                            // TODO should we throw?
                            //throw;
                        }
                    }
                }
                else
                {
                    WriteToLog("Error creating payout: " + e);
                    throw;
                }
            }
        }
    }

    private async Task<PayoutData> GetPayoutForInvoice(InvoiceEntity invoiceEntity, CancellationToken cancellationToken)
    {
        var metaJson = invoiceEntity.Metadata.ToJObject();
        AutoForwardInvoiceMetadata newMeta = AutoForwardInvoiceMetadata.FromJObject(metaJson);

        var payout = await GetPayoutById(newMeta.AutoForwardPayoutId, invoiceEntity.StoreId, cancellationToken);
        return payout;
    }

    public void WriteToLog(string message)
    {
        _logger.LogInformation("{Prefix}{Message}", LogPrefix, message);
    }

    public void WriteToLog(System.Exception exception)
    {
        _logger.LogCritical(exception.ToString());
    }

    public async Task WriteToLog(string message, string invoiceId)
    {
        WriteToLog($"Invoice {invoiceId}: {message}");

        // Write to invoice
        InvoiceLogs logs = new InvoiceLogs();
        logs.Write($"{LogPrefix}{message}", InvoiceEventData.EventSeverity.Info);
        await _invoiceRepository.AddInvoiceLogs(invoiceId, logs);
    }

    public async Task<PayoutData> GetPayoutById(string id, string storeId, CancellationToken cancellationToken)
    {
        var client = await GetClient(storeId);
        try
        {
            var payout = await client.GetStorePayout(storeId, id, cancellationToken);
            return payout;
        }
        catch (GreenfieldAPIException e)
        {
            if (e.APIError.Code.Equals("payout-not-found"))
            {
                return null;
            }

            throw;
        }
    }


    public bool IsValidAutoForwardableInvoice(InvoiceEntity invoice, bool mustBeIncomplete)
    {
        AutoForwardInvoiceMetadata newMeta = GetMetaForInvoice(invoice);
        if (!String.IsNullOrEmpty(newMeta.AutoForwardToAddress) &&
            newMeta.AutoForwardPercentage is > 0 and < 1)
        {
            if (mustBeIncomplete && newMeta.AutoForwardCompleted)
            {
                return false;
            }

            return true;
        }

        return false;
    }

    public async Task UpdatePayoutToDestination(AutoForwardDestination destination, string oldDestination,
        bool? oldAllowed,
        CancellationToken cancellationToken = default)
    {
        if (oldDestination != null &&
            !oldDestination.Equals(destination.Destination, StringComparison.InvariantCulture))
        {
            // The payout destination has been altered

            // 1. Cancel the payout to the old destination. If something goes wrong, it can be re-created easily.
            var client = await GetClient(destination.StoreId);
            string cryptoCode = destination.PaymentMethod.Split()[0];
            var payoutsToOldDestination = await GetPayoutForDestination(cryptoCode, destination.Destination,
                destination.StoreId,
                cancellationToken);

            await client.CancelPayout(destination.StoreId, payoutsToOldDestination.Id);


            // 2. Update the destination in the invoices
            var invoicesToOldDestination =
                await GetUnprocessedInvoicesLinkedToDestination(oldDestination, destination.StoreId);
            foreach (var invoice in invoicesToOldDestination)
            {
                await WriteToLog($"Cancelled payout to old destination {oldDestination}",
                    invoice.Id);

                AutoForwardInvoiceMetadata newMeta = GetMetaForInvoice(invoice);
                newMeta.AutoForwardToAddress = destination.Destination;

                await _invoiceRepository.UpdateInvoiceMetadata(invoice.Id, invoice.StoreId, newMeta.ToJObject());
                await WriteToLog($"Changed payout destination from {oldDestination} to {destination.Destination}",
                    invoice.Id);
            }
        }

        var invoices = await GetUnprocessedInvoicesLinkedToDestination(destination.Destination, destination.StoreId);
        foreach (var invoice in invoices)
        {
            if (oldAllowed != null && oldAllowed != destination.PayoutsAllowed)
            {
                // Allowed has changed
                var newMeta = GetMetaForInvoice(invoice);

                if (destination.PayoutsAllowed)
                {
                    await WriteToLog($"Destination {newMeta.AutoForwardToAddress} is now allowed.", invoice.Id);
                }
                else
                {
                    await WriteToLog($"Destination {newMeta.AutoForwardToAddress} is now blocked.", invoice.Id);
                }
            }

            await CheckInvoice(invoice, false, false, cancellationToken);
        }
    }


    public async Task UpdateEverything(CancellationToken cancellationToken = default)
    {
        var openInvoices = await GetAutoForwardableInvoicesNotPaidOut();
        foreach (var invoice in openInvoices)
        {
            try
            {
                await CheckInvoice(invoice, false, false, cancellationToken);
            }
            catch (System.Exception e)
            {
                WriteToLog(e);
            }
        }
    }

    public async Task<PayoutData[]> GetPayoutsToDestination(AutoForwardDestination destination, bool isComplete,
        CancellationToken cancellationToken)
    {
        var client = await GetClient(destination.StoreId);
        var allPayouts = await client.GetStorePayouts(destination.StoreId, false, cancellationToken);

        return allPayouts.Where(p => (isComplete && p.State == PayoutState.Completed) || (!isComplete && p.State != PayoutState.Completed)).ToArray();
    }
}