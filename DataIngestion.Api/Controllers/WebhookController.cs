using DataIngestion.Model.Data;
using DataIngestion.Model.DTOs;
using DataIngestion.Model.Models;
using DataIngestion.Svc.Services;
using Microsoft.AspNetCore.Mvc;

namespace DataIngestion.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WebhookController : ControllerBase
{
    private readonly IIngestionService _ingestionService;
    private readonly IIngestionQueue _queue;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        IIngestionService ingestionService,
        IIngestionQueue queue,
        AppDbContext dbContext,
        ILogger<WebhookController> logger)
    {
        _ingestionService = ingestionService;
        _queue = queue;
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] WebhookRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
            return BadRequest("url is required");

        _logger.LogInformation("Webhook received for URL: {Url}", request.Url);

        try
        {
            var result = await _ingestionService.IngestAsync(request.Url);
            return Ok(result);
        }
        catch (DuplicateIngestionException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            return BadRequest(new { error = $"Could not download ZIP: {ex.Message}" });
        }
    }

    [HttpPost("async")]
    public async Task<IActionResult> PostAsync([FromBody] WebhookRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
            return BadRequest("url is required");

        var job = new IngestionJob
        {
            JobId = Guid.NewGuid(),
            ZipUrl = request.Url,
            Status = JobStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.IngestionJobs.Add(job);
        await _dbContext.SaveChangesAsync();
        await _queue.EnqueueAsync(job.JobId, request.Url);

        _logger.LogInformation("Async job {JobId} queued for URL: {Url}", job.JobId, request.Url);

        return Accepted(new { jobId = job.JobId });
    }
}
