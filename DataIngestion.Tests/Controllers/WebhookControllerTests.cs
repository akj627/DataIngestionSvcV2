using DataIngestion.Api.Controllers;
using DataIngestion.Model.Data;
using DataIngestion.Model.DTOs;
using DataIngestion.Model.Models;
using DataIngestion.Svc.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataIngestion.Tests.Controllers;

public class WebhookControllerTests : IDisposable
{
    private readonly Mock<IIngestionService> _mockService;
    private readonly Mock<IIngestionQueue> _mockQueue;
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _dbContext;
    private readonly WebhookController _controller;

    public WebhookControllerTests()
    {
        _mockService = new Mock<IIngestionService>();
        _mockQueue = new Mock<IIngestionQueue>();
        _mockQueue.Setup(q => q.EnqueueAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .Returns(ValueTask.CompletedTask);

        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _dbContext = new AppDbContext(options);
        _dbContext.Database.EnsureCreated();

        _controller = new WebhookController(
            _mockService.Object,
            _mockQueue.Object,
            _dbContext,
            NullLogger<WebhookController>.Instance);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    // ── Sync endpoint ────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_ValidUrl_ReturnsOkWithIngestionResult()
    {
        var expected = new IngestionResult { ClientsProcessed = 5, AccountsProcessed = 10, HoldingsProcessed = 30 };
        _mockService.Setup(s => s.IngestAsync(It.IsAny<string>())).ReturnsAsync(expected);

        var result = await _controller.Post(new WebhookRequest { Url = "http://example.com/data.zip" });

        var ok = Assert.IsType<OkObjectResult>(result);
        var ingestionResult = Assert.IsType<IngestionResult>(ok.Value);
        Assert.Equal(5, ingestionResult.ClientsProcessed);
        Assert.Equal(10, ingestionResult.AccountsProcessed);
        Assert.Equal(30, ingestionResult.HoldingsProcessed);
    }

    [Fact]
    public async Task Post_ValidUrl_CallsIngestionServiceWithCorrectUrl()
    {
        const string url = "http://example.com/data.zip";
        _mockService.Setup(s => s.IngestAsync(url)).ReturnsAsync(new IngestionResult());

        await _controller.Post(new WebhookRequest { Url = url });

        _mockService.Verify(s => s.IngestAsync(url), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Post_EmptyOrWhitespaceUrl_ReturnsBadRequest(string url)
    {
        var result = await _controller.Post(new WebhookRequest { Url = url });

        Assert.IsType<BadRequestObjectResult>(result);
        _mockService.Verify(s => s.IngestAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Post_HttpRequestException_ReturnsBadRequest()
    {
        _mockService.Setup(s => s.IngestAsync(It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("ZIP download failed"));

        var result = await _controller.Post(new WebhookRequest { Url = "http://example.com/data.zip" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Post_DuplicateZip_ReturnsConflict()
    {
        _mockService.Setup(s => s.IngestAsync(It.IsAny<string>()))
            .ThrowsAsync(new DuplicateIngestionException(3, DateTimeOffset.UtcNow));

        var result = await _controller.Post(new WebhookRequest { Url = "http://example.com/data.zip" });

        Assert.IsType<ConflictObjectResult>(result);
    }

    // ── Async endpoint ───────────────────────────────────────────────────────

    [Fact]
    public async Task PostAsync_ValidUrl_ReturnsAcceptedWithJobId()
    {
        var result = await _controller.PostAsync(new WebhookRequest { Url = "http://example.com/data.zip" });

        var accepted = Assert.IsType<AcceptedResult>(result);
        var value = accepted.Value!;
        var jobIdProp = value.GetType().GetProperty("jobId");
        Assert.NotNull(jobIdProp);
        var jobId = Assert.IsType<Guid>(jobIdProp!.GetValue(value));
        Assert.NotEqual(Guid.Empty, jobId);
    }

    [Fact]
    public async Task PostAsync_ValidUrl_PersistsJobAsPending()
    {
        await _controller.PostAsync(new WebhookRequest { Url = "http://example.com/data.zip" });

        var job = await _dbContext.IngestionJobs.SingleAsync();
        Assert.Equal(JobStatus.Pending, job.Status);
        Assert.Equal("http://example.com/data.zip", job.ZipUrl);
    }

    [Fact]
    public async Task PostAsync_ValidUrl_EnqueuesJob()
    {
        await _controller.PostAsync(new WebhookRequest { Url = "http://example.com/data.zip" });

        _mockQueue.Verify(q => q.EnqueueAsync(It.IsAny<Guid>(), "http://example.com/data.zip", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PostAsync_EmptyUrl_ReturnsBadRequest(string url)
    {
        var result = await _controller.PostAsync(new WebhookRequest { Url = url });

        Assert.IsType<BadRequestObjectResult>(result);
        _mockQueue.Verify(q => q.EnqueueAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
