using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using Microsoft.EntityFrameworkCore;
using NBitcoin;

namespace BTCPayServer.Plugins.AutoForward.Services;

public class AutoForwardInvoiceHelper
{
    private readonly ApplicationDbContextFactory _applicationDbContextFactory;
    private readonly InvoiceRepository _invoiceRepository;

    public AutoForwardInvoiceHelper(ApplicationDbContextFactory applicationDbContextFactory, InvoiceRepository invoiceRepository)
    {
        _applicationDbContextFactory = applicationDbContextFactory;
        _invoiceRepository = invoiceRepository;
    }


    public static Money getAmountReceived(InvoiceEntity invoice, string paymentMethod)
    {
        Money total = new((long)0);

        // TODO should we loop to include Lightning? Not supported for now...
        var paymentMethodObj = invoice.GetPaymentMethod(PaymentMethodId.Parse(paymentMethod));

        // TODO don't hard code for BTC-Onchain
        var data = paymentMethodObj.Calculate();
        total += data.CryptoPaid;

        return total;
    }

    public async Task<InvoiceEntity[]> getAutoForwardableInvoices()
    {
        // TODO this method does not scale and will be very slow if the invoice list is long
        return await getInvoicesBySql($"select * FROM \"Invoices\" where \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardToAddress' is not null and \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardPercentage' is not null order by \"Created\" desc");
    }

    public async Task<InvoiceEntity[]> getAutoForwardableInvoicesMissingAPayout()
    {
        // TODO this method does not scale and will be very slow if the invoice list is long
        return await getInvoicesBySql($"select * FROM \"Invoices\" where \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardToAddress' is not null and \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardPercentage' is not null and \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardPayoutId' is null and status = \"settled\" order by \"Created\" desc");
    }

    private async Task<InvoiceEntity[]> getInvoicesBySql(FormattableString sql)
    {
        using var context = _applicationDbContextFactory.CreateContext();
        IQueryable<BTCPayServer.Data.InvoiceData> query =
            context
                .Invoices.FromSqlInterpolated(sql)
                .Include(o => o.Payments)
                .Include(o => o.Refunds).ThenInclude(refundData => refundData.PullPaymentData);

        return (await query.ToListAsync()).Select(o => _invoiceRepository.ToEntity(o)).ToArray();
    }

    public string[] getPayoutIds()
    {
        // TODO this method does not scale and will be very slow if the invoice list is long
        string sql = "select distinct \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardPayoutId' as payoutId FROM \"Invoices\" where \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardToAddress' is not null and \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardPercentage' is not null and \"Blob2\"::jsonb -> 'metadata' -> 'autoForwardPayoutId' is not null order by \"Created\" desc ";
        return null;
    }
}