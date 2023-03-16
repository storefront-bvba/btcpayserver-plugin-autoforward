using BTCPayServer.Services.Invoices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.AutoForward.Data;

public class AutoForwardInvoiceMetadata: InvoiceMetadata
{
    [JsonIgnore]
    public string AutoForwardPayoutId
    {
        get => this.GetAdditionalData<string>("autoForwardPayoutId");
        set => this.SetAdditionalData("autoForwardPayoutId", value);
    }
    
    [JsonIgnore]
    public string AutoForwardTo
    {
        get => this.GetAdditionalData<string>("autoForwardTo");
        set => this.SetAdditionalData("autoForwardTo", value);
    }
    
    public static AutoForwardInvoiceMetadata FromJObject(JObject jObject)
    {
        return jObject.ToObject<AutoForwardInvoiceMetadata>(MetadataSerializer);
    }
}