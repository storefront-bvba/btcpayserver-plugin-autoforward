using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Plugins.AutoForward.Data;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.AutoForward.Services;

public class InvoiceWatcher : EventHostedServiceBase
{
    private readonly InvoiceRepository _invoiceRepository;

    public InvoiceWatcher(EventAggregator eventAggregator, Logs logs, InvoiceRepository _invoiceRepository) : base(eventAggregator, logs)
    {
        this._invoiceRepository = _invoiceRepository;
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
            string prefix = "Auto-Forwarding: ";
            InvoiceLogs logs = new InvoiceLogs();

            if (invoiceEvent.Invoice.Status.ToModernStatus() == InvoiceStatus.Settled)
            {
                // TODO prevent double payout creation
                var metaJson = invoiceEvent.Invoice.Metadata.ToJObject();
                AutoForwardInvoiceMetadata newMeta = BTCPayServer.Plugins.AutoForward.Data.AutoForwardInvoiceMetadata.FromJObject(metaJson);


                if (string.IsNullOrEmpty(newMeta.AutoForwardPayoutId))
                {
                    newMeta.AutoForwardPayoutId = "xxx";

                    // Assign the new metadata to the invoice so future logic can use it too...
                    invoiceEvent.Invoice.Metadata = newMeta;

                    // TODO is this really needed?
                    await _invoiceRepository.UpdateInvoiceMetadata(invoiceEvent.InvoiceId, invoiceEvent.Invoice.StoreId, newMeta.ToJObject());

                    logs.Write($"{prefix}Invoice is {invoiceEvent.Invoice.Status.ToModernStatus()}. Created payout ID xxx.", InvoiceEventData.EventSeverity.Info);
                }
                else
                {
                    logs.Write($"{prefix}Invoice is settled. A payout was already created. Found ID {newMeta.AutoForwardPayoutId}.", InvoiceEventData.EventSeverity.Info);                    
                }

                
                await _invoiceRepository.AddInvoiceLogs(invoiceEvent.InvoiceId, logs);
            }
        }
    }
}