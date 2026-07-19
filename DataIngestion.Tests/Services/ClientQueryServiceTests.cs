using DataIngestion.Model.Data;
using DataIngestion.Model.Models;
using DataIngestion.Svc.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DataIngestion.Tests.Services;

public class ClientQueryServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _dbContext;
    private readonly ClientQueryService _service;

    public ClientQueryServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new AppDbContext(options);
        _dbContext.Database.EnsureCreated();
        _service = new ClientQueryService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    private async Task<IngestionRun> SeedRunAsync(DateTimeOffset? knowledgeDate = null, int clientCount = 2)
    {
        var run = new IngestionRun
        {
            KnowledgeDate = knowledgeDate ?? DateTimeOffset.UtcNow,
            ZipUrl = "http://example.com/data.zip",
            ZipHash = Guid.NewGuid().ToString("N")
        };

        for (var i = 1; i <= clientCount; i++)
        {
            var client = new Client
            {
                ClientId = $"CLT-{i:000}",
                FirstName = $"First{i}",
                LastName = $"Last{i}",
                Email = $"client{i}@example.com",
                AdvisorId = "ADV-001",
                LastUpdated = DateTimeOffset.UtcNow,
                IngestionRun = run
            };

            var account = new Account
            {
                AccountId = $"ACC-{i:000}",
                AccountType = "IRA",
                Custodian = "Custodian A",
                OpenedDate = new DateOnly(2020, 1, 1),
                Status = "active",
                CashBalance = 1000m,
                TotalValue = 5000m,
                Client = client,
                Holdings =
                [
                    new Holding
                    {
                        Ticker = "AAPL", Cusip = "037833100", Description = "Apple Inc",
                        Quantity = 10, MarketValue = 1500m, CostBasis = 1200m,
                        Price = 150m, AssetClass = "equity"
                    }
                ]
            };

            client.Accounts.Add(account);
            run.Clients.Add(client);
        }

        _dbContext.IngestionRuns.Add(run);
        await _dbContext.SaveChangesAsync();
        return run;
    }

    // ── GetRuns ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRunsAsync_ReturnsAllRunsNewestFirst()
    {
        var run1 = await SeedRunAsync(DateTimeOffset.UtcNow.AddHours(-1));
        var run2 = await SeedRunAsync(DateTimeOffset.UtcNow);

        var runs = await _service.GetRunsAsync();

        Assert.Equal(2, runs.Count);
        Assert.Equal(run2.Id, runs[0].RunId); // newest first
        Assert.Equal(run1.Id, runs[1].RunId);
    }

    // ── GetClients ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetClientsAsync_NoRuns_ReturnsEmptyResult()
    {
        var result = await _service.GetClientsAsync(1, 20);

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetClientsAsync_ReturnsClientsFromLatestRunOnly()
    {
        await SeedRunAsync(DateTimeOffset.UtcNow.AddHours(-1), clientCount: 3);
        await SeedRunAsync(DateTimeOffset.UtcNow, clientCount: 2); // latest

        var result = await _service.GetClientsAsync(1, 20);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task GetClientsAsync_PagingReturnsCorrectPage()
    {
        await SeedRunAsync(clientCount: 5);

        var page1 = await _service.GetClientsAsync(1, 2);
        var page2 = await _service.GetClientsAsync(2, 2);
        var page3 = await _service.GetClientsAsync(3, 2);

        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(3, page1.TotalPages);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(2, page2.Items.Count);
        Assert.Single(page3.Items);
    }

    [Fact]
    public async Task GetClientsAsync_ItemsIncludeKnowledgeDate()
    {
        var run = await SeedRunAsync(clientCount: 1);

        var result = await _service.GetClientsAsync(1, 20);

        Assert.Equal(run.KnowledgeDate, result.Items[0].KnowledgeDate);
    }

    // ── GetAccounts ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAccountsAsync_KnownClient_ReturnsAccounts()
    {
        await SeedRunAsync(clientCount: 2);

        var accounts = await _service.GetAccountsAsync("CLT-001");

        Assert.NotNull(accounts);
        Assert.Single(accounts);
        Assert.Equal("ACC-001", accounts[0].AccountId);
    }

    [Fact]
    public async Task GetAccountsAsync_UnknownClient_ReturnsNull()
    {
        await SeedRunAsync(clientCount: 1);

        var accounts = await _service.GetAccountsAsync("CLT-DOES-NOT-EXIST");

        Assert.Null(accounts);
    }

    [Fact]
    public async Task GetAccountsAsync_ClientInOlderRunOnly_ReturnsNull()
    {
        // CLT-001 is only in the old run; latest run has a different client
        await SeedRunAsync(DateTimeOffset.UtcNow.AddHours(-1), clientCount: 1); // has CLT-001
        // Seed a second run with a different client
        var latestRun = new IngestionRun { KnowledgeDate = DateTimeOffset.UtcNow, ZipUrl = "x" };
        latestRun.Clients.Add(new Client
        {
            ClientId = "CLT-999", FirstName = "X", LastName = "Y",
            Email = "x@y.com", AdvisorId = "", LastUpdated = DateTimeOffset.UtcNow,
            IngestionRun = latestRun
        });
        _dbContext.IngestionRuns.Add(latestRun);
        await _dbContext.SaveChangesAsync();

        var accounts = await _service.GetAccountsAsync("CLT-001");

        Assert.Null(accounts);
    }

    // ── GetHoldings ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetHoldingsAsync_KnownClientAndAccount_ReturnsHoldings()
    {
        await SeedRunAsync(clientCount: 1);

        var holdings = await _service.GetHoldingsAsync("CLT-001", "ACC-001");

        Assert.NotNull(holdings);
        Assert.Single(holdings);
        Assert.Equal("AAPL", holdings[0].Ticker);
    }

    [Fact]
    public async Task GetHoldingsAsync_UnknownAccount_ReturnsNull()
    {
        await SeedRunAsync(clientCount: 1);

        var holdings = await _service.GetHoldingsAsync("CLT-001", "ACC-DOES-NOT-EXIST");

        Assert.Null(holdings);
    }

    [Fact]
    public async Task GetHoldingsAsync_UnknownClient_ReturnsNull()
    {
        await SeedRunAsync(clientCount: 1);

        var holdings = await _service.GetHoldingsAsync("CLT-DOES-NOT-EXIST", "ACC-001");

        Assert.Null(holdings);
    }

    // ── AsOf queries ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetClientsAsync_AsOfBetweenRuns_ReturnsFirstRunData()
    {
        var t1 = DateTimeOffset.UtcNow.AddHours(-2);
        var t2 = DateTimeOffset.UtcNow;

        await SeedRunAsync(t1, clientCount: 3); // run 1
        await SeedRunAsync(t2, clientCount: 1); // run 2 (latest)

        // ask for state halfway between the two runs
        var asOf = t1.AddHours(1);
        var result = await _service.GetClientsAsync(1, 20, asOf);

        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    public async Task GetClientsAsync_AsOfAfterAllRuns_ReturnsLatestRunData()
    {
        var t1 = DateTimeOffset.UtcNow.AddHours(-2);
        var t2 = DateTimeOffset.UtcNow.AddHours(-1);

        await SeedRunAsync(t1, clientCount: 3);
        await SeedRunAsync(t2, clientCount: 1); // latest

        var asOf = DateTimeOffset.UtcNow; // after both runs
        var result = await _service.GetClientsAsync(1, 20, asOf);

        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task GetClientsAsync_AsOfBeforeAllRuns_ReturnsEmpty()
    {
        await SeedRunAsync(DateTimeOffset.UtcNow, clientCount: 2);

        var asOf = DateTimeOffset.UtcNow.AddDays(-1); // before any run
        var result = await _service.GetClientsAsync(1, 20, asOf);

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }
}
