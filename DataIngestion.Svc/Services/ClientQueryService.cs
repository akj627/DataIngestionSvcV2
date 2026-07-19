using DataIngestion.Model.Data;
using DataIngestion.Model.DTOs;
using Microsoft.EntityFrameworkCore;

namespace DataIngestion.Svc.Services;

public class ClientQueryService : IClientQueryService
{
    private readonly AppDbContext _dbContext;

    public ClientQueryService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<IngestionRunSummaryDto>> GetRunsAsync() =>
        await _dbContext.IngestionRuns
            .AsNoTracking()
            .OrderByDescending(r => r.Id)
            .Select(r => new IngestionRunSummaryDto
            {
                RunId = r.Id,
                KnowledgeDate = r.KnowledgeDate,
                ZipSource = r.ZipUrl,
                ClientsProcessed = r.ClientsProcessed,
                AccountsProcessed = r.AccountsProcessed,
                HoldingsProcessed = r.HoldingsProcessed
            })
            .ToListAsync();

    public async Task<PagedResult<ClientSummaryDto>> GetClientsAsync(int page, int pageSize, DateTimeOffset? asOf = null)
    {
        var latestRunId = await RunIdAsOfAsync(asOf);
        if (latestRunId == null)
            return new PagedResult<ClientSummaryDto> { Page = page, PageSize = pageSize };

        var query = _dbContext.Clients
            .AsNoTracking()
            .Where(c => c.IngestionRunId == latestRunId.Value);

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new ClientSummaryDto
            {
                ClientId = c.ClientId,
                FirstName = c.FirstName,
                LastName = c.LastName,
                Email = c.Email,
                AdvisorId = c.AdvisorId,
                LastUpdated = c.LastUpdated,
                KnowledgeDate = c.IngestionRun.KnowledgeDate
            })
            .ToListAsync();

        return new PagedResult<ClientSummaryDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        };
    }

    public async Task<List<AccountSummaryDto>?> GetAccountsAsync(string clientId, DateTimeOffset? asOf = null)
    {
        var latestRunId = await RunIdAsOfAsync(asOf);
        if (latestRunId == null) return null;

        var client = await _dbContext.Clients
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ClientId == clientId && c.IngestionRunId == latestRunId.Value);

        if (client == null) return null;

        return await _dbContext.Accounts
            .AsNoTracking()
            .Where(a => a.ClientId == client.Id)
            .OrderBy(a => a.AccountId)
            .Select(a => new AccountSummaryDto
            {
                AccountId = a.AccountId,
                AccountType = a.AccountType,
                Custodian = a.Custodian,
                OpenedDate = a.OpenedDate.ToString("yyyy-MM-dd"),
                Status = a.Status,
                CashBalance = a.CashBalance,
                TotalValue = a.TotalValue
            })
            .ToListAsync();
    }

    public async Task<List<HoldingSummaryDto>?> GetHoldingsAsync(string clientId, string accountId, DateTimeOffset? asOf = null)
    {
        var latestRunId = await RunIdAsOfAsync(asOf);
        if (latestRunId == null) return null;

        var client = await _dbContext.Clients
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ClientId == clientId && c.IngestionRunId == latestRunId.Value);

        if (client == null) return null;

        var account = await _dbContext.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.AccountId == accountId && a.ClientId == client.Id);

        if (account == null) return null;

        return await _dbContext.Holdings
            .AsNoTracking()
            .Where(h => h.AccountId == account.Id)
            .OrderBy(h => h.Ticker)
            .Select(h => new HoldingSummaryDto
            {
                Ticker = h.Ticker,
                Cusip = h.Cusip,
                Description = h.Description,
                Quantity = h.Quantity,
                MarketValue = h.MarketValue,
                CostBasis = h.CostBasis,
                Price = h.Price,
                AssetClass = h.AssetClass
            })
            .ToListAsync();
    }

    private async Task<int?> RunIdAsOfAsync(DateTimeOffset? asOf)
    {
        if (!asOf.HasValue)
            return await _dbContext.IngestionRuns.MaxAsync(r => (int?)r.Id);

        // SQLite cannot translate DateTimeOffset comparisons; load the small
        // runs index client-side (Id + KnowledgeDate only) and filter in memory.
        var runs = await _dbContext.IngestionRuns
            .AsNoTracking()
            .Select(r => new { r.Id, r.KnowledgeDate })
            .ToListAsync();

        return runs
            .Where(r => r.KnowledgeDate <= asOf.Value)
            .Select(r => (int?)r.Id)
            .DefaultIfEmpty(null)
            .Max();
    }
}
