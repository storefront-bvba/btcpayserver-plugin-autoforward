using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.AutoForward.Data;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Logging;
using NBitcoin;

namespace BTCPayServer.Plugins.AutoForward.Services;

public class InvoiceWatcher : EventHostedServiceBase
{
    private readonly InvoiceRepository _invoiceRepository;
    private readonly PullPaymentHostedService _pullPaymentHostedService;

    public InvoiceWatcher(EventAggregator eventAggregator, Logs logs, InvoiceRepository invoiceRepository, PullPaymentHostedService pullPaymentHostedService) : base(eventAggregator, logs)
    {
        _invoiceRepository = invoiceRepository;
        _pullPaymentHostedService = pullPaymentHostedService;
    }

    public InvoiceWatcher(EventAggregator eventAggregator, ILogger logger) : base(eventAggregator, logger)
    {
    }

    protected override void SubscribeToEvents()
    {
        Subscribe<InvoiceEvent>();
    }

    protected async override Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is InvoiceEvent invoiceEvent)
        {
            InvoiceEntity invoice = invoiceEvent.Invoice;
            string prefix = "Auto-Forwarding: ";
            InvoiceLogs logs = new InvoiceLogs();

            if (invoice.Status.ToModernStatus() == InvoiceStatus.Settled)
            {
                // TODO prevent double payout creation
                var metaJson = invoice.Metadata.ToJObject();
                AutoForwardInvoiceMetadata newMeta = AutoForwardInvoiceMetadata.FromJObject(metaJson);


                if (string.IsNullOrEmpty(newMeta.AutoForwardPayoutId))
                {
                    var totalPaid = AutoForwardInvoiceHelper.getAmountReceived(invoice);
                    var payoutId = createPayout(newMeta.AutoForwardToAddress, newMeta.AutoForwardPercentage, totalPaid, invoice);

                    newMeta.AutoForwardPayoutId = "xxx";

                    // Assign the new metadata to the invoice so future logic can use it too...
                    invoice.Metadata = newMeta;

                    // TODO is this really needed?
                    await _invoiceRepository.UpdateInvoiceMetadata(invoice.Id, invoice.StoreId, newMeta.ToJObject());


                    logs.Write($"{prefix}Invoice is {invoice.Status.ToModernStatus()}. Created payout ID xxx.", InvoiceEventData.EventSeverity.Info);
                }
                else
                {
                    logs.Write($"{prefix}Invoice is settled. A payout was already created. Found ID {newMeta.AutoForwardPayoutId}.", InvoiceEventData.EventSeverity.Info);
                }

                await _invoiceRepository.AddInvoiceLogs(invoice.Id, logs);
            }
        }
    }

    private object createPayout(string newMetaAutoForwardToAddress, decimal newMetaAutoForwardPercentage, decimal totalPaid, InvoiceEntity invoice)
    {
        
        var pmi = new PaymentMethodId(walletId.CryptoCode, BitcoinPaymentType.Instance);
        var claimRequest = new ClaimRequest()
        {
            Destination = new AddressClaimDestination(
                BitcoinAddress.Create(output.DestinationAddress, network.NBitcoinNetwork)),
            Value = output.Amount,
            PaymentMethodId = pmi,
            StoreId = walletId.StoreId,
            PreApprove = true,
        };


        var response = await _pullPaymentHostedService.Claim(claimRequest);
        result.Add(claimRequest, response.Result);
        if (response.Result == ClaimRequest.ClaimResult.Ok)
        {
            if (message is null)
            {
                message = "Payouts scheduled:<br/>";
            }

            message += $"{claimRequest.Value} to {claimRequest.Destination.ToString()}<br/>";
        }
        else
        {
            someFailed = true;
            if (errorMessage is null)
            {
                errorMessage = "Payouts failed to be scheduled:<br/>";
            }

            switch (response.Result)
            {
                case ClaimRequest.ClaimResult.Duplicate:
                    errorMessage += $"{claimRequest.Value} to {claimRequest.Destination.ToString()} - address reuse<br/>";
                    break;
                case ClaimRequest.ClaimResult.AmountTooLow:
                    errorMessage += $"{claimRequest.Value} to {claimRequest.Destination.ToString()} - amount too low<br/>";
                    break;
            }
        }


        if (message is not null && errorMessage is not null)
        {
            message += $"<br/><br/>{errorMessage}";
        }
        else if (message is null && errorMessage is not null)
        {
            message = errorMessage;
        }

        return response.

        // TempData.SetStatusMessageModel(new StatusMessageModel()
        // {
        //     Severity = someFailed ? StatusMessageModel.StatusSeverity.Warning :
        //         StatusMessageModel.StatusSeverity.Success,
        //     Html = message
        // });
        // return RedirectToAction("Payouts", "UIStorePullPayments",
        //     new
        //     {
        //         storeId = walletId.StoreId,
        //         PaymentMethodId = pmi.ToString(),
        //         payoutState = PayoutState.AwaitingPayment,
        //     });
    }
}