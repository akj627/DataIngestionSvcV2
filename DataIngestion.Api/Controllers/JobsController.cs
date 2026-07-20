using DataIngestion.Model.Data;
using DataIngestion.Model.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DataIngestion.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public JobsController(AppDbContext dbContext) => _dbContext = dbContext;

    [HttpGet("{jobId:guid}")]
    public async Task<IActionResult> Get(Guid jobId)
    {
        var job = await _dbContext.IngestionJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.JobId == jobId);

        if (job == null) return NotFound();

        return Ok(new JobStatusDto
        {
            JobId = job.JobId,
            Status = job.Status.ToString(),
            ZipUrl = job.ZipUrl,
            RunId = job.RunId,
            ClientsProcessed = job.ClientsProcessed,
            AccountsProcessed = job.AccountsProcessed,
            HoldingsProcessed = job.HoldingsProcessed,
            Error = job.Error,
            CreatedAt = job.CreatedAt,
            CompletedAt = job.CompletedAt
        });
    }
}
