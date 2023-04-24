using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.AutoForward.Services;

public class InvoiceWatcher : EventHostedServiceBase
{
    private readonly AutoForwardInvoiceHelper _autoForwardInvoiceHelper;

    public InvoiceWatcher(EventAggregator eventAggregator, AutoForwardInvoiceHelper autoForwardInvoiceHelper, Logs logs)
        : base(eventAggregator, logs)
    {
        _autoForwardInvoiceHelper = autoForwardInvoiceHelper;
    }

    public InvoiceWatcher(EventAggregator eventAggregator, ILogger logger) : base(eventAggregator, logger)
    {
    }

    private readonly SemaphoreSlim _updateLock = new(1, 1);
    
    protected override void SubscribeToEvents()
    {
        base.SubscribeToEvents();
        Subscribe<InvoiceEvent>();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        try
        {
            await _updateLock.WaitAsync(cancellationToken);

            if (evt is InvoiceEvent invoiceEvent)
            {
                InvoiceEntity invoice = invoiceEvent.Invoice;
                if (_autoForwardInvoiceHelper.CanInvoiceBePaidOut(invoice))
                {
                    _autoForwardInvoiceHelper.WriteToLog("Need to sync payout for invoice " + invoice.Id);
                    _autoForwardInvoiceHelper.SyncPayoutForInvoice(invoice, cancellationToken);
                }
            }
        }
        catch (System.Exception e)
        {
            Logs.PayServer.LogWarning(e, "Error while processing auto-forward event");
        }
        finally
        {
            _updateLock.Release();
        }
    }
}