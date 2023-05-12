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

    public InvoiceWatcher(EventAggregator eventAggregator, AutoForwardInvoiceHelper autoForwardInvoiceHelper,
        AutoForwardDestinationRepository destinationRepository, Logs logs)
        : base(eventAggregator, logs)
    {
        _autoForwardInvoiceHelper = autoForwardInvoiceHelper;
        _destinationRepository = destinationRepository;
    }

    public InvoiceWatcher(EventAggregator eventAggregator, ILogger logger) : base(eventAggregator, logger)
    {
    }

    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private readonly AutoForwardDestinationRepository _destinationRepository;

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
                bool justCreated = invoiceEvent.EventCode == InvoiceEventCode.Created;
                bool justSettled = invoiceEvent.EventCode == InvoiceEventCode.Completed;
                
                if (justCreated || justSettled)
                {
                    try
                    {
                        // When new => The destination will be checked and auto-created
                        // When complete => Will create payout
                        
                        await _autoForwardInvoiceHelper.CheckInvoice(invoice,
                            justCreated, justSettled, cancellationToken);
                    }
                    catch (System.Exception e)
                    {
                        await _autoForwardInvoiceHelper.WriteToLog(e.ToString(), invoice.Id);
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            _autoForwardInvoiceHelper.WriteToLog(e);
        }
        finally
        {
            _updateLock.Release();
        }
    }
}