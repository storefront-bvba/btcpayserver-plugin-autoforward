using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Plugins.AutoForward.Data;
using BTCPayServer.Plugins.AutoForward.Services;
using BTCPayServer.Plugins.Template.Data;
using BTCPayServer.Plugins.Template.Services;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;

namespace BTCPayServer.Plugins.Template;

[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanViewProfile)]
public class UIAutoForwardController : Controller
{
    private readonly MyPluginService _PluginService;
    private readonly UserManager<ApplicationUser> _UserManager;
    private readonly InvoiceRepository _InvoiceRepository;
    private readonly DisplayFormatter _DisplayFormatter;
    private readonly AutoForwardInvoiceHelper _helper;

    public UIAutoForwardController(MyPluginService PluginService, UserManager<ApplicationUser> userManager, InvoiceRepository invoiceRepository, DisplayFormatter displayFormatter, AutoForwardInvoiceHelper helper)
    {
        _PluginService = PluginService;
        _UserManager = userManager;
        _InvoiceRepository = invoiceRepository;
        _DisplayFormatter = displayFormatter;
        _helper = helper;
    }

    private string GetUserId() => _UserManager.GetUserId(User);


    // This method is copy/pasted from BTCPayServer/Controllers/UIInvoiceController.UI.cs because it is private there
    // private InvoiceDetailsModel InvoicePopulatePayments(InvoiceEntity invoice)
    // {
    //     // TODO Cleanup. DO we need this method?
    //     var overpaid = false;
    //     var model = new InvoiceDetailsModel
    //     {
    //         Archived = invoice.Archived,
    //         Payments = invoice.GetPayments(false),
    //         Overpaid = true,
    //         CryptoPayments = invoice.GetPaymentMethods().Select(
    //             data =>
    //             {
    //                 var accounting = data.Calculate();
    //                 var paymentMethodId = data.GetId();
    //                 var overpaidAmount = accounting.OverpaidHelper.ToDecimal(MoneyUnit.BTC);
    //
    //                 if (overpaidAmount > 0)
    //                 {
    //                     overpaid = true;
    //                 }
    //
    //                 return new InvoiceDetailsModel.CryptoPayment
    //                 {
    //                     PaymentMethodId = paymentMethodId,
    //                     PaymentMethod = paymentMethodId.ToPrettyString(),
    //                     Due = _DisplayFormatter.Currency(accounting.Due.ToDecimal(MoneyUnit.BTC), paymentMethodId.CryptoCode),
    //                     Paid = _DisplayFormatter.Currency(accounting.CryptoPaid.ToDecimal(MoneyUnit.BTC), paymentMethodId.CryptoCode),
    //                     Overpaid = _DisplayFormatter.Currency(overpaidAmount, paymentMethodId.CryptoCode),
    //                     // Address = data.GetPaymentMethodDetails().GetPaymentDestination(), // TODO: Commented this out because of error
    //                     // Rate = ExchangeRate(data.GetId().CryptoCode, data), // TODO: Commented this out because of error
    //                     PaymentMethodRaw = data
    //                 };
    //             }).ToList()
    //     };
    //     model.Overpaid = overpaid;
    //
    //     return model;
    // }


    [Route("~/plugins/autoforward")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        var model = new PluginPageViewModel { Data = await _PluginService.Get() };

// TODO Cleanup
        // var storeIds = new HashSet<string>();
        // if (fs.GetFilterArray("storeid") is string[] l)
        // {
        //     foreach (var i in l)
        //         storeIds.Add(i);
        // }
        // if (storeId is not null)
        // {
        //     storeIds.Add(storeId);
        //     model.StoreId = storeId;
        // }
        // model.StoreIds = storeIds.ToArray();
        string storeId = null;

        var invoiceQuery = new InvoiceQuery()
        {
            UserId = GetUserId(),
            //StoreId = storeId,
        };

        string paymentMethod = "BTC-OnChain"; // TODO make dynamic

        // invoiceQuery.StoreId = model.StoreIds;
        invoiceQuery.Take = model.Count;
        invoiceQuery.Skip = model.Skip;
        invoiceQuery.IncludeRefunds = true;

        //var list = await _InvoiceRepository.GetInvoices(invoiceQuery);
        var list = await _helper.GetAutoForwardableInvoices();

        foreach (var invoice in list)
        {
            AutoForwardInvoiceMetadata meta = AutoForwardInvoiceMetadata.FromJObject(invoice.Metadata.ToJObject());
            var state = invoice.GetInvoiceState();
            var pm = "BTC-OnChain"; // TODO make dynamic

            var amountReceived = AutoForwardInvoiceHelper.GetAmountReceived(invoice, pm);
            var payments = invoice.GetPayments(false);
            var hasRefund = invoice.Refunds.Any(data => !data.PullPaymentData.Archived);
            Client.Models.PayoutData payout;
            
            if (meta.AutoForwardCompletedPayoutId != null)
            {
                payout = await _helper.GetPayoutById(meta.AutoForwardCompletedPayoutId, invoice.StoreId, cancellationToken);
            }
            else
            {
                payout = await _helper.GetPayoutForDestination(paymentMethod, meta.AutoForwardToAddress, invoice.StoreId, cancellationToken);
            }
            
            model.Invoices.Add(new AutoForwardableInvoiceModel()
            {
                Status = state,
                Date = invoice.InvoiceTime,
                InvoiceId = invoice.Id,
                OrderId = invoice.Metadata.OrderId ?? string.Empty,
                Amount = invoice.Price,
                Currency = invoice.Currency,
                HasRefund = hasRefund, // TODO do something with refund info?
                Payments = payments,
                AutoForwardToAddress = meta.AutoForwardToAddress,
                AutoForwardPayout = payout,
                AutoForwardPercentage = meta.AutoForwardPercentage,
                AmountReceived = amountReceived,
                AmountReceivedCryptoCode = "BTC" // TODO make dynamic
            });
        }


        return View(model);
    }

    [Route("~/plugins/autoforward/payouts")]
    public async Task<IActionResult> Payouts()
    {
        var model = new PluginPageViewModel { Data = await _PluginService.Get() };
        return View(model);
    }

}

public class PluginPageViewModel : BasePagingViewModel
{
    public List<PluginData> Data { get; set; }
    public List<AutoForwardableInvoiceModel> Invoices { get; set; } = new();
    public override int CurrentPageCount => Invoices.Count;
}

public class AutoForwardableInvoiceModel
{
    public DateTimeOffset Date { get; set; }

    public string OrderId { get; set; }
    public string InvoiceId { get; set; }
    
    public string AutoForwardToAddress { get; set; }
    public decimal AutoForwardPercentage { get; set; }
    public Client.Models.PayoutData AutoForwardPayout { get; set; }

    public InvoiceState Status { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; }
    
    public bool HasRefund { get; set; }
    public List<PaymentEntity> Payments { get; set; }
    public Money AmountReceived { get; set; }
    public string AmountReceivedCryptoCode { get; set; }
}