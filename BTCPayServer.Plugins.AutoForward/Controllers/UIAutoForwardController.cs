using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Plugins.AutoForward.Data;
using BTCPayServer.Plugins.AutoForward.Services;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using PayoutData = BTCPayServer.Client.Models.PayoutData;

namespace BTCPayServer.Plugins.AutoForward.Controllers;

[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanViewProfile)]
public class UIAutoForwardController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AutoForwardInvoiceHelper _helper;
    private readonly AutoForwardDestinationRepository _autoForwardDestinationRepository;

    public UIAutoForwardController(UserManager<ApplicationUser> userManager,
        AutoForwardDestinationRepository autoForwardDestinationRepository, DisplayFormatter displayFormatter,
        AutoForwardInvoiceHelper helper)
    {
        _userManager = userManager;
        _helper = helper;
        _autoForwardDestinationRepository = autoForwardDestinationRepository;
    }

    private string GetUserId() => _userManager.GetUserId(User);


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
        var model = new InvoicesViewModel { };

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

        var invoiceQuery = new InvoiceQuery()
        {
            UserId = GetUserId(),
            //StoreId = storeId,
        };

        string paymentMethod = "BTC-OnChain"; // TODO make dynamic
        string cryptoCode = paymentMethod.Split("-")[0];

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

            var amountReceived = _helper.GetAmountReceived(invoice, pm);
            var amountToForward = _helper.GetAmountToForward(invoice, pm);
            var payments = invoice.GetPayments(false);
            var hasRefund = invoice.Refunds.Any(data => !data.PullPaymentData.Archived);
            Client.Models.PayoutData payout = null;

            if (meta.AutoForwardPayoutId != null)
            {
                payout = await _helper.GetPayoutById(meta.AutoForwardPayoutId, invoice.StoreId, cancellationToken);
            }
            else if (_helper.IsValidAutoForwardableInvoice(invoice, false))
            {
                payout = await _helper.GetPayoutForDestination(cryptoCode, meta.AutoForwardToAddress, invoice.StoreId,
                    cancellationToken);
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
                Payout = payout,
                AutoForwardPercentage = meta.AutoForwardPercentage,
                AutoForwardCompleted = meta.AutoForwardCompleted,
                AutoForwardNotNeeded = invoice.Status == InvoiceStatusLegacy.Expired ||
                                       invoice.Status == InvoiceStatusLegacy.Invalid,
                AmountReceived = amountReceived,
                AmountToForward = amountToForward,
                AmountReceivedCryptoCode = "BTC" // TODO make dynamic
            });
        }


        return View(model);
    }

    [Route("~/plugins/autoforward/payouts")]
    public Task<IActionResult> Payouts()
    {
        var model = new InvoicesViewModel { };
        return Task.FromResult<IActionResult>(View(model));
    }

    [Route("~/plugins/autoforward/destinations")]
    public async Task<IActionResult> Destinations(CancellationToken cancellationToken)
    {
        var model = new DestinationsViewModel { };
        var destinations = await _autoForwardDestinationRepository.FindAll(cancellationToken);
        model.Destinations = new DestinationViewModel[destinations.Length];

        for (int i = 0; i < destinations.Length;i++)
        {
            var destination = destinations[i];
            var openInvoices =
                await _helper.GetUnprocessedInvoicesLinkedToDestination(destination.Destination, destination.StoreId);

            decimal openInvoiceAmount = 0m;
            foreach (var invoice in openInvoices)
            {
                decimal amountToForward = _helper.GetAmountToForward(invoice, destination.PaymentMethod);
                openInvoiceAmount += amountToForward;
            }
            
            model.Destinations[i] = new DestinationViewModel
            {
                AutoForwardDestination = destination,
                CompletedPayouts = await _helper.GetPayoutsToDestination(destination, true, cancellationToken),
                OpenPayouts = await _helper.GetPayoutsToDestination(destination, false ,cancellationToken),
                OpenInvoiceCount = openInvoices.Length,
                OpenInvoiceAmount = openInvoiceAmount
            };
        }
        
        return await Task.FromResult<IActionResult>(View(model));
    }

    [Route("~/plugins/autoforward/help")]
    public Task<IActionResult> Help()
    {
        return Task.FromResult<IActionResult>(View());
    }

    [Route("~/plugins/autoforward/logs")]
    public Task<IActionResult> Logs()
    {
        return Task.FromResult<IActionResult>(View());
    }

    [Route("~/plugins/autoforward/update")]
    public async Task<IActionResult> UpdateEverything(CancellationToken cancellationToken)
    {
        await _helper.UpdateEverything(cancellationToken);

        // TODO show message on top of page when done
        return RedirectToAction(nameof(Index));
    }
}

public class InvoicesViewModel : BasePagingViewModel
{
    public List<AutoForwardableInvoiceModel> Invoices { get; set; } = new();
    public override int CurrentPageCount => Invoices.Count;
}

public class DestinationsViewModel : BasePagingViewModel
{
    public DestinationViewModel[] Destinations { get; set; }
    public override int CurrentPageCount => Destinations.Length;
}

public class AutoForwardableInvoiceModel
{
    public DateTimeOffset Date { get; set; }

    public string OrderId { get; set; }
    public string InvoiceId { get; set; }

    public string AutoForwardToAddress { get; set; }
    public decimal AutoForwardPercentage { get; set; }
    public bool AutoForwardCompleted { get; set; }
    public bool AutoForwardNotNeeded { get; set; }
    public Client.Models.PayoutData Payout { get; set; }

    public InvoiceState Status { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; }

    public bool HasRefund { get; set; }
    public List<PaymentEntity> Payments { get; set; }
    public Money AmountReceived { get; set; }
    public string AmountReceivedCryptoCode { get; set; }
    public decimal AmountToForward { get; set; }
}

public class DestinationViewModel
{
    public AutoForwardDestination AutoForwardDestination;
    public PayoutData[] OpenPayouts;
    public PayoutData[] CompletedPayouts;
    public int OpenInvoiceCount { get; set; }
    public decimal OpenInvoiceAmount { get; set; }
}