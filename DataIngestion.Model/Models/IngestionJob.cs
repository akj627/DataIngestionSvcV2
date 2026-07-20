using System.ComponentModel.DataAnnotations;

namespace DataIngestion.Model.Models;

public enum JobStatus { Pending, Running, Completed, Failed }

public class IngestionJob
{
    [Key]
    public Guid JobId { get; set; }
    public JobStatus Status { get; set; }
    public string ZipUrl { get; set; } = string.Empty;
    public int? RunId { get; set; }
    public string? Error { get; set; }
    public int? ClientsProcessed { get; set; }
    public int? AccountsProcessed { get; set; }
    public int? HoldingsProcessed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
