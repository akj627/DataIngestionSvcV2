using DataIngestion.Model.DTOs;
using DataIngestion.Svc.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DataIngestion.Api.Pages;

public class IndexModel : PageModel
{
    private readonly IClientQueryService _queryService;
    private readonly IIngestionService _ingestionService;

    [BindProperty]
    public string ZipUrl { get; set; } = "http://localhost:5141/test-data-v1.zip";

    [BindProperty(SupportsGet = true)]
    public string? AsOf { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Tab { get; set; }

    public List<IngestionRunSummaryDto> Runs { get; set; } = new();
    public List<ClientSummaryDto> Clients { get; set; } = new();
    public IngestionResult? LastIngestionResult { get; set; }
    public bool ShowInputError { get; set; }
    public string? IngestionError { get; set; }

    public IndexModel(IClientQueryService queryService, IIngestionService ingestionService)
    {
        _queryService = queryService;
        _ingestionService = ingestionService;
    }

    public async Task OnGetAsync()
    {
        Runs = await _queryService.GetRunsAsync();
        var paged = await _queryService.GetClientsAsync(1, 10000, ParseAsOf());
        Clients = paged.Items;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var uploadedFile = Request.Form.Files.GetFile("ZipFile");

        try
        {
            if (uploadedFile != null && uploadedFile.Length > 0)
            {
                using var ms = new MemoryStream();
                await uploadedFile.CopyToAsync(ms);
                LastIngestionResult = await _ingestionService.IngestAsync(ms.ToArray(), uploadedFile.FileName);
            }
            else if (!string.IsNullOrWhiteSpace(ZipUrl))
            {
                LastIngestionResult = await _ingestionService.IngestAsync(ZipUrl);
            }
            else
            {
                ShowInputError = true;
            }
        }
        catch (DuplicateIngestionException ex)
        {
            IngestionError = ex.Message;
        }
        catch (HttpRequestException ex)
        {
            IngestionError = $"Could not download ZIP: {ex.Message}";
        }

        Runs = await _queryService.GetRunsAsync();
        var result = await _queryService.GetClientsAsync(1, 10000, ParseAsOf());
        Clients = result.Items;
        return Page();
    }

    private DateTimeOffset? ParseAsOf()
    {
        if (string.IsNullOrEmpty(AsOf)) return null;
        return DateTime.TryParse(AsOf, null, System.Globalization.DateTimeStyles.AssumeLocal, out var dt)
            ? new DateTimeOffset(dt).ToUniversalTime()
            : null;
    }
}
