namespace DataIngestion.Svc.Services;

public interface IIngestionQueue
{
    ValueTask EnqueueAsync(Guid jobId, string zipUrl, CancellationToken ct = default);
    IAsyncEnumerable<(Guid JobId, string ZipUrl)> ReadAllAsync(CancellationToken ct);
}
