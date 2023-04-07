using BTCPayServer.Services.Invoices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.AutoForward.Data;

public class AutoForwardInvoiceMetadata: InvoiceMetadata
{
    [JsonIgnore]
    public string AutoForwardCompletedPayoutId
    {
        get => this.GetAdditionalData<string>("autoForwardCompletedPayoutId");
        set => this.SetAdditionalData("autoForwardCompletedPayoutId", value);
    }
    
    [JsonIgnore]
    public string AutoForwardToAddress
    {
        get => this.GetAdditionalData<string>("autoForwardToAddress");
        set => this.SetAdditionalData("autoForwardToAddress", value);
    }
    
    [JsonIgnore]
    public decimal AutoForwardPercentage
    {
        get => this.GetAdditionalData<decimal>("autoForwardPercentage");
        set => this.SetAdditionalData("autoForwardPercentage", value);
    }
    
    public new static AutoForwardInvoiceMetadata FromJObject(JObject jObject)
    {
        return jObject.ToObject<AutoForwardInvoiceMetadata>(MetadataSerializer);
    }
}