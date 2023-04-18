using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Plugins.AutoForward.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.Plugins.AutoForward;

public class PluginMigrationRunner : IHostedService
{
    private readonly AutoForwardDbContextFactory _pluginDbContextFactory;
    private readonly ISettingsRepository _settingsRepository;
    private readonly AutoForwardInvoiceHelper _autoForwardInvoiceHelper;

    public PluginMigrationRunner(
        ISettingsRepository settingsRepository,
        AutoForwardDbContextFactory pluginDbContextFactory,
        AutoForwardInvoiceHelper autoForwardInvoiceHelper)
    {
        _settingsRepository = settingsRepository;
        _pluginDbContextFactory = pluginDbContextFactory;
        _autoForwardInvoiceHelper = autoForwardInvoiceHelper;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        PluginDataMigrationHistory settings = await _settingsRepository.GetSettingAsync<PluginDataMigrationHistory>() ??
                                              new PluginDataMigrationHistory();
        await using var ctx = _pluginDbContextFactory.CreateContext();
        await ctx.Database.MigrateAsync(cancellationToken);

        // settings migrations
        if (!settings.UpdatedSomething)
        {
            settings.UpdatedSomething = true;
            await _settingsRepository.UpdateSetting(settings);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public class PluginDataMigrationHistory
    {
        public bool UpdatedSomething { get; set; }
    }
}

