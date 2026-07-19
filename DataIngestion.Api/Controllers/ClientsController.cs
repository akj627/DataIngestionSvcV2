using DataIngestion.Svc.Services;
using Microsoft.AspNetCore.Mvc;

namespace DataIngestion.Api.Controllers;

[ApiController]
[Route("api/clients")]
public class ClientsController : ControllerBase
{
    private readonly IClientQueryService _queryService;

    public ClientsController(IClientQueryService queryService)
    {
        _queryService = queryService;
    }

    [HttpGet]
    public async Task<IActionResult> GetClients([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var result = await _queryService.GetClientsAsync(page, pageSize);
        return Ok(result);
    }

    [HttpGet("{clientId}/accounts")]
    public async Task<IActionResult> GetAccounts(string clientId)
    {
        var accounts = await _queryService.GetAccountsAsync(clientId);
        return accounts == null ? NotFound() : Ok(accounts);
    }

    [HttpGet("{clientId}/accounts/{accountId}/holdings")]
    public async Task<IActionResult> GetHoldings(string clientId, string accountId)
    {
        var holdings = await _queryService.GetHoldingsAsync(clientId, accountId);
        return holdings == null ? NotFound() : Ok(holdings);
    }
}
