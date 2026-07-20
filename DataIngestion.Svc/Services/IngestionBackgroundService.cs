using DataIngestion.Model.Data;
using DataIngestion.Model.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DataIngestion.Svc.Services;

public class IngestionBackgroundService : BackgroundService
{
    private readonly IIngestionQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IngestionBackgroundService> _logger;

    public IngestionBackgroundService(
        IIngestionQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<IngestionBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (jobId, zipUrl) in _queue.ReadAllAsync(stoppingToken))
        {
            await ProcessJobAsync(jobId, zipUrl, stoppingToken);
        }
    }

    private async Task ProcessJobAsync(Guid jobId, string zipUrl, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ingestionService = scope.ServiceProvider.GetRequiredService<IIngestionService>();

        var job = await db.IngestionJobs.FindAsync(new object[] { jobId }, ct);
        if (job == null) return;

        job.Status = JobStatus.Running;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Processing async job {JobId} for {ZipUrl}", jobId, zipUrl);

        try
        {
            var result = await ingestionService.IngestAsync(zipUrl);
            job.Status = JobStatus.Completed;
            job.RunId = result.RunId;
            job.ClientsProcessed = result.ClientsProcessed;
            job.AccountsProcessed = result.AccountsProcessed;
            job.HoldingsProcessed = result.HoldingsProcessed;
            job.CompletedAt = DateTimeOffset.UtcNow;
            _logger.LogInformation("Job {JobId} completed: Run #{RunId}", jobId, result.RunId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed", jobId);
            job.Status = JobStatus.Failed;
            job.Error = ex.Message;
            job.CompletedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }
}
