using DataIngestion.Model.DTOs;

namespace DataIngestion.Svc.Services;

public interface IClientQueryService
{
    Task<List<IngestionRunSummaryDto>> GetRunsAsync();
    Task<PagedResult<ClientSummaryDto>> GetClientsAsync(int page, int pageSize, DateTimeOffset? asOf = null);
    Task<List<AccountSummaryDto>?> GetAccountsAsync(string clientId, DateTimeOffset? asOf = null);
    Task<List<HoldingSummaryDto>?> GetHoldingsAsync(string clientId, string accountId, DateTimeOffset? asOf = null);
}
