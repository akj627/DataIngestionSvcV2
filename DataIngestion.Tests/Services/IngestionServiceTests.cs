using System.Net;
using DataIngestion.Model.Data;
using DataIngestion.Svc.Services;
using DataIngestion.Tests.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataIngestion.Tests.Services;

public class IngestionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _dbContext;

    public IngestionServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new AppDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    private IngestionService CreateService(byte[] zipContent, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var httpClient = new HttpClient(new MockHttpMessageHandler(zipContent, statusCode));
        return new IngestionService(httpClient, _dbContext, NullLogger<IngestionService>.Instance);
    }

    // ── Happy path ──────────────────────────────────────────────────────────

    [Fact]
    public async Task IngestAsync_ValidZipWithMultipleClients_PersistsAllClients()
    {
        var zip = TestDataBuilder.CreateZip(new Dictionary<string, string>
        {
            ["CLT-001.json"] = TestDataBuilder.ClientJson("CLT-001"),
            ["CLT-002.json"] = TestDataBuilder.ClientJson("CLT-002"),
            ["CLT-003.json"] = TestDataBuilder.ClientJson("CLT-003"),
        });

        await CreateService(zip).IngestAsync("http://example.com/data.zip");

        var count = await _dbContext.Clients.AsNoTracking().CountAsync();
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task IngestAsync_ValidZip_ReturnsCorrectCounts()
    {
        // 2 clients, 2 accounts each, 3 holdings per account = 12 holdings total
        var zip = TestDataBuilder.CreateZip(new Dictionary<string, string>
        {
            ["CLT-001.json"] = TestDataBuilder.ClientJson("CLT-001", accountCount: 2, holdingsPerAccount: 3),
            ["CLT-002.json"] = TestDataBuilder.ClientJson("CLT-002", accountCount: 2, holdingsPerAccount: 3),
        });

        var result = await CreateService(zip).IngestAsync("http://example.com/data.zip");

        Assert.Equal(2, result.ClientsProcessed);
        Assert.Equal(4, result.AccountsProcessed);
        Assert.Equal(12, result.HoldingsProcessed);
    }

    [Fact]
    public async Task IngestAsync_ReturnsRunIdAndKnowledgeDate()
    {
        var zip = TestDataBuilder.CreateZip(new Dictionary<string, string>
        {
            ["CLT-001.json"] = TestDataBuilder.ClientJson("CLT-001"),
        });

        var before = DateTimeOffset.UtcNow;
        var result = await CreateService(zip).IngestAsync("http://example.com/data.zip");
        var after = DateTimeOffset.UtcNow;

        Assert.True(result.RunId > 0);
        Assert.True(result.KnowledgeDate >= before);
        Assert.True(result.KnowledgeDate <= after);
    }

    // ── Append behaviour (each run is a new snapshot) ───────────────────────

    [Fact]
    public async Task IngestAsync_SameZipIngestedTwice_ThrowsDuplicateOnSecondCall()
    {
        var zip = TestDataBuilder.CreateZip(new Dictionary<string, string>
        {
            ["CLT-001.json"] = TestDataBuilder.ClientJson("CLT-001"),
        });

        var service = CreateService(zip);
        await service.IngestAsync("http://example.com/data.zip");

        await Assert.ThrowsAsync<DuplicateIngestionException>(
            () => service.IngestAsync("http://example.com/data.zip"));

        // only one run should exist
        Assert.Equal(1, await _dbContext.IngestionRuns.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task IngestAsync_SecondIngestionWithUpdatedEmail_LatestRunHasNewEmail()
    {
        var zip1 = TestDataBuilder.CreateZip(new Dictionary<string, string>
        {
            ["CLT-001.json"] = TestDataBuilder.ClientJson("CLT-001", email: "old@example.com"),
        });
        var zip2 = TestDataBuilder.CreateZip(new Dictionary<string, string>
        {
            ["CLT-001.json"] = TestDataBuilder.ClientJson("CLT-001", email: "new@example.com"),
        });

        await CreateService(zip1).IngestAsync("http://example.com/data.zip");
        var result2 = await CreateService(zip2).IngestAsync("http://example.com/data.zip");

        var latestClient = await _dbContext.Clients.AsNoTracking()
            .SingleAsync(c => c.ClientId == "CLT-001" && c.IngestionRunId == result2.RunId);
        Assert.Equal("new@example.com", latestClient.Email);
    }

    [Fact]
    public async Task IngestAsync_SecondIngestionWithFewerAccounts_LatestRunHasFewerAccounts()
    {
        var zip1 = TestDataBuilder.CreateZip(new Dictionary<string, string>
        {
            ["CLT-001.json"] = TestDataBuilder.ClientJson("CLT-001", accountCount: 3),
        });
        var zip2 = TestDataBuilder.CreateZip(new Dictionary<string, string>
        {
            ["CLT-001.json"] = TestDataBuilder.ClientJson("CLT-001", accountCount: 1),
        });

        await CreateService(zip1).IngestAsync("http://example.com/data.zip");
        var result2 = await CreateService(zip2).IngestAsync("http://example.com/data.zip");

        // All-time total: 3 from run 1 + 1 from run 2
        var totalAccounts = await _dbContext.Accounts.AsNoTracking().CountAsync();
        Assert.Equal(4, totalAccounts);

        // Latest run has 1 account
        Assert.Equal(1, result2.AccountsProcessed);
    }

    // ── Duplicate detection ──────────────────────────────────────────────────

    [Fact]
    public async Task IngestAsync_SameZipContentTwice_ThrowsDuplicateIngestionException()
    {
        var zip = TestDataBuilder.CreateZip(new Dictionary<string, string>
        {
            ["CLT-001.json"] = TestDataBuilder.ClientJson("CLT-001"),
        });

        var service = CreateService(zip);
        await service.IngestAsync("http://example.com/data.zip");

        await Assert.ThrowsAsync<DuplicateIngestionException>(
            () => service.IngestAsync("http://example.com/different-name.zip"));
    }

    [Fact]
    public async Task IngestAsync_DifferentZipContent_CreatesNewRun()
    {
        var zip1 = TestDataBuilder.CreateZip(new Dictionary<string, string>
        {
            ["CLT-001.json"] = TestDataBuilder.ClientJson("CLT-001"),
        });
        var zip2 = TestDataBuilder.CreateZip(new Dictionary<string, string>
        {
            ["CLT-002.json"] = TestDataBuilder.ClientJson("CLT-002"),
        });

        await CreateService(zip1).IngestAsync("http://example.com/v1.zip");
        await CreateService(zip2).IngestAsync("http://example.com/v2.zip");

        Assert.Equal(2, await _dbContext.IngestionRuns.CountAsync());
    }

    // ── Edge cases: empty / non-JSON content ────────────────────────────────

    [Fact]
    public async Task IngestAsync_EmptyZip_ReturnsZeroCountsAndCreatesEmptyRun()
    {
        var zip = TestDataBuilder.CreateZip(new Dictionary<string, string>());

        var result = await CreateService(zip).IngestAsync("http://example.com/data.zip");

        Assert.Equal(0, result.ClientsProcessed);
        Assert.Equal(0, result.AccountsProcessed);
        Assert.Equal(0, result.HoldingsProcessed);
        Assert.True(result.RunId > 0);
        Assert.Equal(0, await _dbContext.Clients.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task IngestAsync_ZipContainsOnlyNonJsonFiles_IgnoresFilesAndPersistsNothing()
    {
        var zip = TestDataBuilder.CreateZip(new Dictionary<string, string>
        {
            ["README.txt"] = "this is not json",
            ["data.csv"]   = "col1,col2\n1,2",
        });

        var result = await CreateService(zip).IngestAsync("http://example.com/data.zip");

        Assert.Equal(0, result.ClientsProcessed);
        Assert.Equal(0, await _dbContext.Clients.AsNoTracking().CountAsync());
    }

    // ── Edge cases: bad data ─────────────────────────────────────────────────

    [Fact]
    public async Task IngestAsync_OneFileMalformed_SkipsBadFileAndProcessesRest()
    {
        var zip = TestDataBuilder.CreateZip(new Dictionary<string, string>
        {
            ["CLT-001.json"] = TestDataBuilder.ClientJson("CLT-001"),
            ["bad.json"]     = "{ this is not valid json !!!",
        });

        var result = await CreateService(zip).IngestAsync("http://example.com/data.zip");

        Assert.Equal(1, result.ClientsProcessed);
        Assert.Equal(1, await _dbContext.Clients.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task IngestAsync_ClientWithNoAccounts_PersistsClientWithEmptyAccountList()
    {
        var zip = TestDataBuilder.CreateZip(new Dictionary<string, string>
        {
            ["CLT-001.json"] = TestDataBuilder.ClientJson("CLT-001", accountCount: 0),
        });

        var result = await CreateService(zip).IngestAsync("http://example.com/data.zip");

        Assert.Equal(1, result.ClientsProcessed);
        Assert.Equal(0, result.AccountsProcessed);
        Assert.Equal(0, await _dbContext.Accounts.AsNoTracking().CountAsync());
    }

    // ── Edge cases: HTTP failures ────────────────────────────────────────────

    [Fact]
    public async Task IngestAsync_HttpReturns500_ThrowsHttpRequestException()
    {
        var service = CreateService(Array.Empty<byte>(), HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.IngestAsync("http://example.com/data.zip"));
    }
}
