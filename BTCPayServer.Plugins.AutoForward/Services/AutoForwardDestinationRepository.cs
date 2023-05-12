using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.AutoForward.Controllers;
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

    public async Task<AutoForwardDestination> EnsureDestinationExists(string destination, string storeId,
        string paymentMethod)
    {
        var entity = await FindByDestination(destination, storeId, paymentMethod);
        if (entity == null)
        {
            // Create
            entity = await Create(new AutoForwardDestination()
                { Destination = destination, StoreId = storeId, PaymentMethod = paymentMethod });
        }

        return entity;
    }

    public async Task<AutoForwardDestination> FindByDestination(string destination, string storeId,
        string paymentMethod,
        CancellationToken cancellationToken = default)
    {
        await using var context = _autoForwardDbContextFactory.CreateContext();
        IQueryable<AutoForwardDestination> query = context.AutoForwardDestination
            .Where(ca => ca.StoreId == storeId && ca.Destination == destination && ca.PaymentMethod == paymentMethod);

        return (await query.ToListAsync()).FirstOrDefault();
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

    public async Task<AutoForwardDestination> Update(AutoForwardDestination entity)
    {
        await using var context = _autoForwardDbContextFactory.CreateContext();
        context.AutoForwardDestination.Update(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    public async Task<AutoForwardDestination[]> FindAll(CancellationToken cancellationToken)
    {
        await using var context = _autoForwardDbContextFactory.CreateContext();
        IQueryable<AutoForwardDestination> query = context.AutoForwardDestination.OrderBy(o => o.Destination);
        return await query.ToArrayAsync(cancellationToken).ConfigureAwait(false);
    }
}