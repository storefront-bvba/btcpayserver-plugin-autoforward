using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.AutoForward.Services;

public class InvoiceWatcher : EventHostedServiceBase
{

    private readonly AutoForwardInvoiceHelper _autoForwardInvoiceHelper;

    public InvoiceWatcher(EventAggregator eventAggregator, AutoForwardInvoiceHelper autoForwardInvoiceHelper, Logs logs) : base(eventAggregator, logs)
    {
        _autoForwardInvoiceHelper = autoForwardInvoiceHelper;
    }

    public InvoiceWatcher(EventAggregator eventAggregator, ILogger logger) : base(eventAggregator, logger)
    {
    }

    protected override void SubscribeToEvents()
    {
        Subscribe<InvoiceEvent>();
    }

    protected override Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is InvoiceEvent invoiceEvent)
        {
            InvoiceEntity invoice = invoiceEvent.Invoice;
            if (invoice.Status.ToModernStatus() == InvoiceStatus.Settled)
            {
                // An invoice was just settled
                _autoForwardInvoiceHelper.SyncPayoutForInvoice(invoice, cancellationToken);
            }
        }

        return Task.CompletedTask;
    }

}