using DataIngestion.Model.DTOs;
using DataIngestion.Svc.Services;
using Microsoft.AspNetCore.Mvc;

namespace DataIngestion.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WebhookController : ControllerBase
{
    private readonly IIngestionService _ingestionService;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(IIngestionService ingestionService, ILogger<WebhookController> logger)
    {
        _ingestionService = ingestionService;
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
}
