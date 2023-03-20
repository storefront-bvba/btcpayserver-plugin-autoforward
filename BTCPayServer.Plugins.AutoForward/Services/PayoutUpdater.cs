using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.AutoForward.Services;

public class PayoutUpdater // : BaseAsyncService
{
    // public PayoutUpdater(Logs logs) : base(logs)
    // {
    // }
    //
    // public PayoutUpdater(ILogger logger) : base(logger)
    // {
    // }
    //
    // internal override Task[] InitializeTasks()
    // {
    //     return new[]
    //     {
    //         CreateLoopTask(UpdatePayouts)
    //     };
    // }
    //
    // private readonly TimeSpan Period = TimeSpan.FromSeconds(2);
    //
    // async Task UpdatePayouts()
    // {
    //     using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation))
    //     {
    //         Logs.PayServer.LogWarning($"Updating payouts...");
    //
    //         // var settings = await SettingsRepository.GetSettingAsync<DynamicDnsSettings>() ?? new DynamicDnsSettings();
    //         // foreach (var service in settings.Services)
    //         // {
    //         //     if (service?.Enabled is true && (service.LastUpdated is null ||
    //         //                              (DateTimeOffset.UtcNow - service.LastUpdated) > Period))
    //         //     {
    //         //         timeout.CancelAfter(TimeSpan.FromSeconds(20.0));
    //         //         try
    //         //         {
    //         //             var errorMessage = await service.SendUpdateRequest(HttpClientFactory.CreateClient());
    //         //             if (errorMessage == null)
    //         //             {
    //         //                 Logs.PayServer.LogInformation("Dynamic DNS service successfully refresh the DNS record");
    //         //                 service.LastUpdated = DateTimeOffset.UtcNow;
    //         //                 await SettingsRepository.UpdateSetting(settings);
    //         //             }
    //         //             else
    //         //             {
    //         //                 Logs.PayServer.LogWarning($"Dynamic DNS service is enabled but the request to the provider failed: {errorMessage}");
    //         //             }
    //         //         }
    //         //         catch (OperationCanceledException) when (timeout.IsCancellationRequested)
    //         //         {
    //         //         }
    //         //     }
    //         // }
    //     }
    //
    //     using var delayCancel = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
    //     var delay = Task.Delay(Period, delayCancel.Token);
    //     // var changed = SettingsRepository.WaitSettingsChanged<DynamicDnsSettings>(Cancellation);
    //     // await Task.WhenAny(delay, changed);
    //     delayCancel.Cancel();
    // }
}