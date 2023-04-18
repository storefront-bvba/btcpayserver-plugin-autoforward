using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.AutoForward.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins.AutoForward;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AutoForwardDbContext>
{
    public AutoForwardDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<AutoForwardDbContext> builder = new DbContextOptionsBuilder<AutoForwardDbContext>();

        builder.UseSqlite("Data Source=temp.db");

        return new AutoForwardDbContext(builder.Options, true);
    }
}

public class AutoForwardDbContextFactory : BaseDbContextFactory<AutoForwardDbContext>
{
    public AutoForwardDbContextFactory(IOptions<DatabaseOptions> options) : base(options, "BTCPayServer.Plugins.AutoForward")
    {
    }

    public override AutoForwardDbContext CreateContext()
    {
        DbContextOptionsBuilder<AutoForwardDbContext> builder = new DbContextOptionsBuilder<AutoForwardDbContext>();
        ConfigureBuilder(builder);
        return new AutoForwardDbContext(builder.Options);
    }
}
