using BTCPayServer.Services.Invoices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.AutoForward.Data;

public class AutoForwardInvoiceMetadata: InvoiceMetadata
{
    [JsonIgnore]
    public bool AutoForwardCompleted
    {
        get => this.GetAdditionalData<bool>("autoForwardCompleted");
        set => this.SetAdditionalData("autoForwardCompleted", value);
    }
    
    [JsonIgnore]
    public bool AutoForwardSubtractFeeFromAmount
    {
        get => this.GetAdditionalData<bool>("autoForwardSubtractFeeFromAmount");
        set => this.SetAdditionalData("autoForwardSubtractFeeFromAmount", value);
    }
    
    [JsonIgnore]
    public string AutoForwardPayoutId
    {
        get => this.GetAdditionalData<string>("autoForwardPayoutId");
        set => this.SetAdditionalData("autoForwardPayoutId", value);
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