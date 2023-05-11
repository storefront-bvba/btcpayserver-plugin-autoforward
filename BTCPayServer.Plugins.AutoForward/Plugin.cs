using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Plugins.AutoForward.Data;
using BTCPayServer.Plugins.AutoForward.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.AutoForward;

public class Plugin : BaseBTCPayServerPlugin
{
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } = new[]
    {
        new IBTCPayServerPlugin.PluginDependency { Identifier = nameof(BTCPayServer), Condition = ">=1.9.0" }
    };

    public override void Execute(IServiceCollection services)
    {
        services.AddSingleton<IUIExtension>(new UIExtension("TemplatePluginHeaderNav", "header-nav"));
        services.AddHostedService<ApplicationPartsLogger>();
        services.AddHostedService<PluginMigrationRunner>();
        services.AddSingleton<AutoForwardDbContextFactory>();
        services.AddSingleton<AutoForwardInvoiceHelper>();
        services.AddSingleton<AutoForwardDestinationRepository>();
        services.AddHostedService<InvoiceWatcher>();
        services.AddHostedService<BlockWatcher>();
        services.AddDbContext<AutoForwardDbContext>((provider, o) =>
        {
            AutoForwardDbContextFactory factory = provider.GetRequiredService<AutoForwardDbContextFactory>();
            factory.ConfigureBuilder(o);
        });
    }
}
