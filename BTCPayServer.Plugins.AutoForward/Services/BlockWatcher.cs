using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.AutoForward.Services;

public class BlockWatcher : EventHostedServiceBase
{
    private readonly AutoForwardInvoiceHelper _autoForwardInvoiceHelper;

    public BlockWatcher(EventAggregator eventAggregator, AutoForwardInvoiceHelper autoForwardInvoiceHelper, Logs logs)
        : base(eventAggregator, logs)
    {
        _autoForwardInvoiceHelper = autoForwardInvoiceHelper;
    }

    public BlockWatcher(EventAggregator eventAggregator, ILogger logger) : base(eventAggregator, logger)
    {
    }

    private readonly SemaphoreSlim _updateLock = new(1, 1);
    
    protected override void SubscribeToEvents()
    {
        base.SubscribeToEvents();
        Subscribe<NewBlockEvent>();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        try
        {
            await _updateLock.WaitAsync(cancellationToken);

            if (evt is NewBlockEvent newBlockEvent)
            {
                // Wait a bit to make sure other invoice/payout processing is finished.
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                
                await _autoForwardInvoiceHelper.UpdateEverything(cancellationToken);
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