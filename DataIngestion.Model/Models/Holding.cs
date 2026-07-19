using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace DataIngestion.Model.Models;

public class Holding
{
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string Ticker { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Cusip { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Description { get; set; } = string.Empty;

    [Precision(18, 6)]
    public decimal Quantity { get; set; }

    [Precision(18, 4)]
    public decimal MarketValue { get; set; }

    [Precision(18, 4)]
    public decimal CostBasis { get; set; }

    [Precision(18, 4)]
    public decimal Price { get; set; }

    [MaxLength(50)]
    public string AssetClass { get; set; } = string.Empty;

    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;
}
