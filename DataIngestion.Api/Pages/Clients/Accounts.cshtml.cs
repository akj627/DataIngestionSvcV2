using DataIngestion.Model.DTOs;
using DataIngestion.Svc.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DataIngestion.Api.Pages.Clients;

public class AccountsModel : PageModel
{
    private readonly IClientQueryService _queryService;

    public string ClientId { get; set; } = string.Empty;
    public List<AccountSummaryDto> Accounts { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? AsOf { get; set; }

    public AccountsModel(IClientQueryService queryService)
    {
        _queryService = queryService;
    }

    public async Task<IActionResult> OnGetAsync(string clientId)
    {
        ClientId = clientId;
        var accounts = await _queryService.GetAccountsAsync(clientId, ParseAsOf());
        if (accounts == null) return NotFound();
        Accounts = accounts;
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
