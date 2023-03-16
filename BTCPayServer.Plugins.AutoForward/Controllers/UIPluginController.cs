using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Plugins.Template.Data;
using BTCPayServer.Plugins.Template.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.Template;

[Route("~/plugins/autoforward")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanViewProfile)]
public class UIPluginController : Controller
{
    private readonly MyPluginService _PluginService;
    private readonly UserManager<ApplicationUser> _UserManager;
    private readonly InvoiceRepository _InvoiceRepository;

    public UIPluginController(MyPluginService PluginService, UserManager<ApplicationUser> userManager, InvoiceRepository invoiceRepository)
    {
        _PluginService = PluginService;
        _UserManager = userManager;
        _InvoiceRepository = invoiceRepository;
    }

    private string GetUserId() => _UserManager.GetUserId(User);

    // GET
    public async Task<IActionResult> Index()
    {
        var model = new PluginPageViewModel { Data = await _PluginService.Get() };


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

        // invoiceQuery.StoreId = model.StoreIds;
        invoiceQuery.Take = model.Count;
        invoiceQuery.Skip = model.Skip;
        invoiceQuery.IncludeRefunds = true;

        var list = await _InvoiceRepository.GetInvoices(invoiceQuery);

        foreach (var invoice in list)
        {
            var state = invoice.GetInvoiceState();
            model.Invoices.Add(new BTCPayServer.Models.InvoicingModels.InvoiceModel()
            {
                Status = state,
                ShowCheckout = invoice.Status == InvoiceStatusLegacy.New,
                Date = invoice.InvoiceTime,
                InvoiceId = invoice.Id,
                OrderId = invoice.Metadata.OrderId ?? string.Empty,
                RedirectUrl = invoice.RedirectURL?.AbsoluteUri ?? string.Empty,
                Amount = invoice.Price,
                Currency = invoice.Currency,
                CanMarkInvalid = state.CanMarkInvalid(),
                CanMarkSettled = state.CanMarkComplete(),
                Details = InvoicePopulatePayments(invoice),
                HasRefund = invoice.Refunds.Any(data => !data.PullPaymentData.Archived)
            });
        }


        return View(model);
    }
}

public class PluginPageViewModel : BasePagingViewModel
{
    public List<PluginData> Data { get; set; }
    public List<InvoiceModel> Invoices { get; set; } = new List<InvoiceModel>();
    public override int CurrentPageCount => Invoices.Count;
}