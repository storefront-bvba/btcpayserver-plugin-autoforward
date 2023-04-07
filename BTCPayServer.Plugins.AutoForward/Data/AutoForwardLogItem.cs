using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BTCPayServer.Plugins.AutoForward.Data;

public class AutoForwardLogItem
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string Id { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    [Required] 
    public string Message { get; set; }

    public string StoreId { get; set; }
    public string InvoiceId { get; set; }
    public string PayoutId { get; set; }
}