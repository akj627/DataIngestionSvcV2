using DataIngestion.Api.Controllers;
using DataIngestion.Model.Data;
using DataIngestion.Model.DTOs;
using DataIngestion.Model.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DataIngestion.Tests.Controllers;

public class JobsControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _dbContext;
    private readonly JobsController _controller;

    public JobsControllerTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _dbContext = new AppDbContext(options);
        _dbContext.Database.EnsureCreated();
        _controller = new JobsController(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Get_UnknownJobId_ReturnsNotFound()
    {
        var result = await _controller.Get(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Get_PendingJob_ReturnsCorrectStatus()
    {
        var job = new IngestionJob { JobId = Guid.NewGuid(), ZipUrl = "http://example.com/data.zip", Status = JobStatus.Pending, CreatedAt = DateTimeOffset.UtcNow };
        _dbContext.IngestionJobs.Add(job);
        await _dbContext.SaveChangesAsync();

        var result = await _controller.Get(job.JobId);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<JobStatusDto>(ok.Value);
        Assert.Equal("Pending", dto.Status);
        Assert.Equal(job.JobId, dto.JobId);
    }

    [Fact]
    public async Task Get_CompletedJob_ReturnsRunIdAndCounts()
    {
        var job = new IngestionJob
        {
            JobId = Guid.NewGuid(),
            ZipUrl = "http://example.com/data.zip",
            Status = JobStatus.Completed,
            RunId = 7,
            ClientsProcessed = 3,
            AccountsProcessed = 5,
            HoldingsProcessed = 11,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        };
        _dbContext.IngestionJobs.Add(job);
        await _dbContext.SaveChangesAsync();

        var result = await _controller.Get(job.JobId);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<JobStatusDto>(ok.Value);
        Assert.Equal("Completed", dto.Status);
        Assert.Equal(7, dto.RunId);
        Assert.Equal(3, dto.ClientsProcessed);
    }

    [Fact]
    public async Task Get_FailedJob_ReturnsError()
    {
        var job = new IngestionJob
        {
            JobId = Guid.NewGuid(),
            ZipUrl = "http://example.com/bad.zip",
            Status = JobStatus.Failed,
            Error = "Could not download ZIP",
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        };
        _dbContext.IngestionJobs.Add(job);
        await _dbContext.SaveChangesAsync();

        var result = await _controller.Get(job.JobId);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<JobStatusDto>(ok.Value);
        Assert.Equal("Failed", dto.Status);
        Assert.Equal("Could not download ZIP", dto.Error);
    }
}
