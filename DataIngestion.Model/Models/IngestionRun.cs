using System.ComponentModel.DataAnnotations;

namespace DataIngestion.Model.Models;

public class IngestionRun
{
    public int Id { get; set; }
    public DateTimeOffset KnowledgeDate { get; set; }
    [MaxLength(2000)] public string ZipUrl { get; set; } = string.Empty;
    [MaxLength(64)] public string ZipHash { get; set; } = string.Empty;
    public int ClientsProcessed { get; set; }
    public int AccountsProcessed { get; set; }
    public int HoldingsProcessed { get; set; }
    public ICollection<Client> Clients { get; set; } = new List<Client>();
}
