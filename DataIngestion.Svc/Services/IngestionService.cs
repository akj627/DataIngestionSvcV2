using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using DataIngestion.Model.Data;
using DataIngestion.Model.DTOs;
using DataIngestion.Model.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DataIngestion.Svc.Services;

public class IngestionService : IIngestionService
{
    private readonly HttpClient _httpClient;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<IngestionService> _logger;

    public IngestionService(HttpClient httpClient, AppDbContext dbContext, ILogger<IngestionService> logger)
    {
        _httpClient = httpClient;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<IngestionResult> IngestAsync(string zipUrl)
    {
        var zipBytes = await DownloadZipAsync(zipUrl);
        return await ProcessAsync(zipBytes, zipUrl);
    }

    public Task<IngestionResult> IngestAsync(byte[] zipBytes, string sourceLabel) =>
        ProcessAsync(zipBytes, sourceLabel);

    private async Task<IngestionResult> ProcessAsync(byte[] zipBytes, string sourceLabel)
    {
        var hash = Convert.ToHexString(SHA256.HashData(zipBytes));

        var existing = await _dbContext.IngestionRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ZipHash == hash);
        if (existing != null)
            throw new DuplicateIngestionException(existing.Id, existing.KnowledgeDate);

        var clientDtos = ParseZip(zipBytes);

        var run = new IngestionRun
        {
            KnowledgeDate = DateTimeOffset.UtcNow,
            ZipUrl = sourceLabel,
            ZipHash = hash
        };
        _dbContext.IngestionRuns.Add(run);

        var result = new IngestionResult();

        foreach (var dto in clientDtos)
        {
            var client = new Client { ClientId = dto.ClientId, IngestionRun = run };
            MapClientFields(dto, client);

            foreach (var accountDto in dto.Accounts)
            {
                var account = MapAccount(accountDto);
                client.Accounts.Add(account);
                result.AccountsProcessed++;
                result.HoldingsProcessed += account.Holdings.Count;
            }

            _dbContext.Clients.Add(client);
            result.ClientsProcessed++;
        }

        run.ClientsProcessed = result.ClientsProcessed;
        run.AccountsProcessed = result.AccountsProcessed;
        run.HoldingsProcessed = result.HoldingsProcessed;

        await _dbContext.SaveChangesAsync();

        result.RunId = run.Id;
        result.KnowledgeDate = run.KnowledgeDate;

        _logger.LogInformation(
            "Run {RunId} ({KnowledgeDate}): {Clients} clients, {Accounts} accounts, {Holdings} holdings",
            run.Id, run.KnowledgeDate, result.ClientsProcessed, result.AccountsProcessed, result.HoldingsProcessed);

        return result;
    }

    private async Task<byte[]> DownloadZipAsync(string url)
    {
        _logger.LogInformation("Downloading ZIP from {Url}", url);
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    private List<ClientDto> ParseZip(byte[] zipBytes)
    {
        var clients = new List<ClientDto>();

        using var stream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            if (!entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                using var entryStream = entry.Open();
                var dto = JsonSerializer.Deserialize<ClientDto>(entryStream);
                if (dto != null)
                    clients.Add(dto);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse entry {Entry}, skipping", entry.Name);
            }
        }

        return clients;
    }

    private static void MapClientFields(ClientDto dto, Client client)
    {
        client.FirstName = dto.FirstName;
        client.LastName = dto.LastName;
        client.Email = dto.Email;
        client.AdvisorId = dto.AdvisorId;
        client.LastUpdated = dto.LastUpdated;
    }

    private static Account MapAccount(AccountDto dto)
    {
        return new Account
        {
            AccountId = dto.AccountId,
            AccountType = dto.AccountType,
            Custodian = dto.Custodian,
            OpenedDate = DateOnly.Parse(dto.OpenedDate),
            Status = dto.Status,
            CashBalance = dto.CashBalance,
            TotalValue = dto.TotalValue,
            Holdings = dto.Holdings.Select(h => new Holding
            {
                Ticker = h.Ticker,
                Cusip = h.Cusip,
                Description = h.Description,
                Quantity = h.Quantity,
                MarketValue = h.MarketValue,
                CostBasis = h.CostBasis,
                Price = h.Price,
                AssetClass = h.AssetClass
            }).ToList()
        };
    }
}
