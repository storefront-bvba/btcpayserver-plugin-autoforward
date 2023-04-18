using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BTCPayServer.Plugins.AutoForward.Data;

public class AutoForwardDestination
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string Id { get; set; }

    [Required] public string Destination { get; set; }

    public string StoreId { get; set; }
    public string PaymentMethod { get; set; }
    public decimal Balance { get; set; } = 0;
    public bool PayoutsAllowed { get; set; } = false;
}