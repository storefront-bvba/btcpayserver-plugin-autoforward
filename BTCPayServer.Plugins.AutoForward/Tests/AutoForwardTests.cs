using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Tests;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.AutoForward.Tests;

[Collection(nameof(NonParallelizableCollectionDefinition))]
public class AutoForwardTests : UnitTestBase
{
    public const int TestTimeout = TestUtils.TestTimeout;

    public AutoForwardTests(ITestOutputHelper helper) : base(helper)
    {
    }

    [Fact(Timeout = TestTimeout)]
    [Trait("Integration", "Integration")]
    public async Task CreateAutoForwardableInvoice()
    {
        using var tester = CreateServerTester();
        tester.ActivateLightning();
        await tester.StartAsync();
        var user = tester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();

        var factory = tester.PayTester.GetService<IBTCPayServerClientFactory>();
        Assert.NotNull(factory);
        var client = await factory.Create(user.UserId, user.StoreId);
        var u = await client.GetCurrentUser();
        var s = await client.GetStores();
        var store = await client.GetStore(user.StoreId);
        Assert.NotNull(store);

        await client.GenerateOnChainWallet(store.Id, "BTC", new GenerateOnChainWalletRequest() { });

        var invoice = await CreateInvoiceToAutoForward(client, store.Id, 100, "EUR", "bcrt1q9q26gunpl7e0l45unnqqw9k3dzlsqeqlny3gpv", (decimal)0.99, true);

        Assert.Equal(invoice.Metadata.GetValue("autoForwardToAddress"), "bcrt1q9q26gunpl7e0l45unnqqw9k3dzlsqeqlny3gpv");
        Assert.Equal(invoice.Metadata.GetValue("autoForwardPercentage"), 0.99);

        Assert.NotNull(invoice);
        Assert.NotEmpty(invoice.Id);
    }

    private async Task<InvoiceData> CreateInvoiceToAutoForward(BTCPayServerClient client, string storeId, decimal amount, string currencyCode, string destination, decimal percentToForward, bool subtractFeeFromAmount)
    {
        JObject meta = new();
        meta.Add("autoForwardToAddress", destination);
        meta.Add("autoForwardPercentage", percentToForward);
        meta.Add("autoForwardSubtractFeeFromAmount", subtractFeeFromAmount);

        var invoice = await client.CreateInvoice(storeId, new CreateInvoiceRequest() { Currency = currencyCode, Amount = amount, Metadata = meta });

        return invoice;
    }
    
    // TODO Test creating 1 auto-forwardable invoice, pay it, but only with 1 confirmation. Nothing should happen.
    
    // TODO Test creating 1 auto-forwardable invoice, pay it and payout should be created with correct amount and destination using subtractFees
    // TODO Test creating 1 auto-forwardable invoice, pay it and payout should be created with correct amount and destination NOT using subtractFees
    
    // TODO Test creating 2 auto-forwardable invoices to same destination, pay both and 1 payout should be created with correct amount and destination using subtractFees + the invoices should be updated correctly
    // TODO Test creating 2 auto-forwardable invoices to same destination, pay both and 1 payout should be created with correct amount and destination NOT using subtractFees + the invoices should be updated correctly
    
    // TODO Test creating 2 auto-forwardable invoices to different destinations, pay both and 2 payouts should be created with correct amount and destination using subtractFees + the invoices should be updated correctly
    // TODO Test creating 2 auto-forwardable invoices to different destinations, pay both and 2 payouts should be created with correct amount and destination NOT using subtractFees + the invoices should be updated correctly
    
    // TODO Test creating 1 auto-forwardable invoice, pay it and payout should be created with correct amount and destination using subtractFees. Process the payout and create another invoice. The 2nd payout should be for the 2nd invoice and not for the 1st.  + the invoices should be updated correctly
    // TODO Test creating 1 auto-forwardable invoice, pay it and payout should be created with correct amount and destination NOT using subtractFees. Process the payout and create another invoice. The 2nd payout should be for the 2nd invoice and not for the 1st.  + the invoices should be updated correctly
    
    // TODO Create a payout that is not part of an auto-forward. This payout should never be affected in any of the tests.
    
    
    
}
