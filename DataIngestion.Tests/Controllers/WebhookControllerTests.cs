using DataIngestion.Api.Controllers;
using DataIngestion.Model.DTOs;
using DataIngestion.Svc.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataIngestion.Tests.Controllers;

public class WebhookControllerTests
{
    private readonly Mock<IIngestionService> _mockService;
    private readonly WebhookController _controller;

    public WebhookControllerTests()
    {
        _mockService = new Mock<IIngestionService>();
        _controller = new WebhookController(_mockService.Object, NullLogger<WebhookController>.Instance);
    }

    [Fact]
    public async Task Post_ValidUrl_ReturnsOkWithIngestionResult()
    {
        var expected = new IngestionResult { ClientsProcessed = 5, AccountsProcessed = 10, HoldingsProcessed = 30 };
        _mockService
            .Setup(s => s.IngestAsync(It.IsAny<string>()))
            .ReturnsAsync(expected);

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
        _mockService
            .Setup(s => s.IngestAsync(url))
            .ReturnsAsync(new IngestionResult());

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
        _mockService
            .Setup(s => s.IngestAsync(It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("ZIP download failed"));

        var result = await _controller.Post(new WebhookRequest { Url = "http://example.com/data.zip" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Post_DuplicateZip_ReturnsConflict()
    {
        _mockService
            .Setup(s => s.IngestAsync(It.IsAny<string>()))
            .ThrowsAsync(new DuplicateIngestionException(3, DateTimeOffset.UtcNow));

        var result = await _controller.Post(new WebhookRequest { Url = "http://example.com/data.zip" });

        Assert.IsType<ConflictObjectResult>(result);
    }
}
