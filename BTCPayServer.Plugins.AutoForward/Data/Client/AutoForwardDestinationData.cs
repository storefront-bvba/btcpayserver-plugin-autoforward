namespace BTCPayServer.Plugins.AutoForward.Data.Client;

public class AutoForwardDestinationData
{
    
    public string Id { get; set; }
    public string Destination { get; set; }
    public string StoreId { get; set; }
    public string PaymentMethod { get; set; }
    public decimal Balance { get; set; } = 0;
    public bool PayoutsAllowed { get; set; } = false;
}