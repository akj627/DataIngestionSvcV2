using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace DataIngestion.Model.Models;

public class Account
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string AccountId { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string AccountType { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Custodian { get; set; } = string.Empty;

    public DateOnly OpenedDate { get; set; }

    [Required, MaxLength(20)]
    public string Status { get; set; } = string.Empty;

    [Precision(18, 4)]
    public decimal CashBalance { get; set; }

    [Precision(18, 4)]
    public decimal TotalValue { get; set; }

    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    public ICollection<Holding> Holdings { get; set; } = new List<Holding>();
}
