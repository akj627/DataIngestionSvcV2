using System.ComponentModel.DataAnnotations;

namespace DataIngestion.Model.Models;

public class Client
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string ClientId { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(50)]
    public string AdvisorId { get; set; } = string.Empty;

    public DateTimeOffset LastUpdated { get; set; }

    public int IngestionRunId { get; set; }
    public IngestionRun IngestionRun { get; set; } = null!;

    public ICollection<Account> Accounts { get; set; } = new List<Account>();
}
