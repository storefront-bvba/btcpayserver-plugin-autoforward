using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.AutoForward.Data;
using BTCPayServer.Plugins.AutoForward.Data.Client;
using BTCPayServer.Plugins.AutoForward.Exception;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.AutoForward.Services;

public class AutoForwardDestinationRepository
{
    private readonly AutoForwardDbContextFactory _autoForwardDbContextFactory;

    public AutoForwardDestinationRepository(AutoForwardDbContextFactory autoForwardDbContextFactory)
    {
        _autoForwardDbContextFactory = autoForwardDbContextFactory;
    }

    // public async Task<List<AutoForwardDestination>> ListDestinations()
    // {
    //     await using var context = _autoForwardDbContextFactory.CreateContext();
    //     return await context.AutoForwardDestination.ToListAsync();
    // }


    public async Task<AutoForwardDestination[]> FindByStoreId(string storeId,
        CancellationToken cancellationToken = default)
    {
        // await using var context = _autoForwardDbContextFactory.CreateContext();
        // return await context.AutoForwardDestination.ToListAsync();

        if (storeId is null)
            throw new ArgumentNullException(nameof(storeId));
        await using var context = _autoForwardDbContextFactory.CreateContext();
        IQueryable<AutoForwardDestination> query = context.AutoForwardDestination
            .Where(x => x.StoreId == storeId);

        var data = await query.ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return data;
    }

    public async Task<AutoForwardDestination> Create(AutoForwardDestination autoForwardDestination)
    {
        await using var context = _autoForwardDbContextFactory.CreateContext();

        autoForwardDestination.Id = Guid.NewGuid().ToString();
        await context.AutoForwardDestination.AddAsync(autoForwardDestination);

        await context.SaveChangesAsync();
        return autoForwardDestination;
    }

    public async Task<AutoForwardDestination> FindById(string storeId, string destinationId)
    {
        await using var context = _autoForwardDbContextFactory.CreateContext();
        IQueryable<AutoForwardDestination> query = context.AutoForwardDestination
            .Where(ca => ca.StoreId == storeId && ca.Id == destinationId);

        return (await query.ToListAsync()).FirstOrDefault();
    }

    public async Task<AutoForwardDestination> UpdatePayoutsAllowed(string storeId, string destinationId, bool allowed)
    {
        var entity = await FindById(storeId, destinationId);
        if (entity == null)
        {
            throw new RecordNotFoundException();
        }

        entity.PayoutsAllowed = allowed;

        await using var context = _autoForwardDbContextFactory.CreateContext();
        context.AutoForwardDestination.Update(entity);

        await context.SaveChangesAsync();
        return entity;
    }
}