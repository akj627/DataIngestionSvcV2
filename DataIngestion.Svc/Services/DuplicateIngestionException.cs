namespace DataIngestion.Svc.Services;

public class DuplicateIngestionException : Exception
{
    public int ExistingRunId { get; }

    public DuplicateIngestionException(int existingRunId, DateTimeOffset knowledgeDate)
        : base($"ZIP already ingested as Run #{existingRunId} on {knowledgeDate.LocalDateTime:yyyy-MM-dd HH:mm}")
    {
        ExistingRunId = existingRunId;
    }
}
