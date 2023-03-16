using BTCPayServer.Services.Invoices;
using NBitcoin;

namespace BTCPayServer.Plugins.AutoForward.Services;

public class AutoForwardInvoiceHelper
{
    
    
    
    public static decimal getAmountReceived(InvoiceEntity invoice)
    {
        decimal r = 0;
        
        // invoice.GetPaymentMethods().Select(
        //     data =>
        //     {
        //         var accounting = data.Calculate();
        //         var paymentMethodId = data.GetId();
        //         var overpaidAmount = accounting.OverpaidHelper.ToDecimal(MoneyUnit.BTC);
        //
        //         if (overpaidAmount > 0)
        //         {
        //             overpaid = true;
        //         }
        //
        //         return new InvoiceDetailsModel.CryptoPayment
        //         {
        //             PaymentMethodId = paymentMethodId,
        //             PaymentMethod = paymentMethodId.ToPrettyString(),
        //             Due = _DisplayFormatter.Currency(accounting.Due.ToDecimal(MoneyUnit.BTC), paymentMethodId.CryptoCode),
        //             Paid = _DisplayFormatter.Currency(accounting.CryptoPaid.ToDecimal(MoneyUnit.BTC), paymentMethodId.CryptoCode),
        //             Overpaid = _DisplayFormatter.Currency(overpaidAmount, paymentMethodId.CryptoCode),
        //             // Address = data.GetPaymentMethodDetails().GetPaymentDestination(), // TODO: Commented this out because of error
        //             // Rate = ExchangeRate(data.GetId().CryptoCode, data), // TODO: Commented this out because of error
        //             PaymentMethodRaw = data
        //         };
        //     })

        foreach (var paymentMethod in invoice.GetPaymentMethods())
        {
            var data = paymentMethod.Calculate();
            r += data.CryptoPaid.ToDecimal(MoneyUnit.BTC);
        }

        // foreach (var payment in invoice.GetPayments(false))
        // {
        //     // r += payment.
        // }

        return r;
    }
    
}