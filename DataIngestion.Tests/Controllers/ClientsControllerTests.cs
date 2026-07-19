using DataIngestion.Api.Controllers;
using DataIngestion.Model.DTOs;
using DataIngestion.Svc.Services;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace DataIngestion.Tests.Controllers;

public class ClientsControllerTests
{
    private readonly Mock<IClientQueryService> _mockService;
    private readonly ClientsController _controller;

    public ClientsControllerTests()
    {
        _mockService = new Mock<IClientQueryService>();
        _controller = new ClientsController(_mockService.Object);
    }

    // ── GET /api/clients ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetClients_ReturnsOkWithPagedResult()
    {
        var expected = new PagedResult<ClientSummaryDto>
        {
            Items = [new ClientSummaryDto { ClientId = "CLT-001" }],
            TotalCount = 1, Page = 1, PageSize = 20, TotalPages = 1
        };
        _mockService.Setup(s => s.GetClientsAsync(1, 20)).ReturnsAsync(expected);

        var result = await _controller.GetClients(1, 20);

        var ok = Assert.IsType<OkObjectResult>(result);
        var paged = Assert.IsType<PagedResult<ClientSummaryDto>>(ok.Value);
        Assert.Equal(1, paged.TotalCount);
    }

    [Theory]
    [InlineData(0, 20, 1, 20)]   // page clamped to 1
    [InlineData(1, 0, 1, 20)]    // pageSize clamped to 20
    [InlineData(1, 200, 1, 20)]  // pageSize clamped to 20
    public async Task GetClients_ClampsInvalidPageParams(int page, int pageSize, int expectedPage, int expectedPageSize)
    {
        _mockService
            .Setup(s => s.GetClientsAsync(expectedPage, expectedPageSize))
            .ReturnsAsync(new PagedResult<ClientSummaryDto>());

        await _controller.GetClients(page, pageSize);

        _mockService.Verify(s => s.GetClientsAsync(expectedPage, expectedPageSize), Times.Once);
    }

    // ── GET /api/clients/{clientId}/accounts ─────────────────────────────────

    [Fact]
    public async Task GetAccounts_KnownClient_ReturnsOk()
    {
        _mockService
            .Setup(s => s.GetAccountsAsync("CLT-001"))
            .ReturnsAsync([new AccountSummaryDto { AccountId = "ACC-001" }]);

        var result = await _controller.GetAccounts("CLT-001");

        var ok = Assert.IsType<OkObjectResult>(result);
        var accounts = Assert.IsType<List<AccountSummaryDto>>(ok.Value);
        Assert.Single(accounts);
    }

    [Fact]
    public async Task GetAccounts_UnknownClient_ReturnsNotFound()
    {
        _mockService
            .Setup(s => s.GetAccountsAsync("CLT-UNKNOWN"))
            .ReturnsAsync((List<AccountSummaryDto>?)null);

        var result = await _controller.GetAccounts("CLT-UNKNOWN");

        Assert.IsType<NotFoundResult>(result);
    }

    // ── GET /api/clients/{clientId}/accounts/{accountId}/holdings ────────────

    [Fact]
    public async Task GetHoldings_KnownClientAndAccount_ReturnsOk()
    {
        _mockService
            .Setup(s => s.GetHoldingsAsync("CLT-001", "ACC-001"))
            .ReturnsAsync([new HoldingSummaryDto { Ticker = "AAPL" }]);

        var result = await _controller.GetHoldings("CLT-001", "ACC-001");

        var ok = Assert.IsType<OkObjectResult>(result);
        var holdings = Assert.IsType<List<HoldingSummaryDto>>(ok.Value);
        Assert.Single(holdings);
    }

    [Fact]
    public async Task GetHoldings_UnknownClientOrAccount_ReturnsNotFound()
    {
        _mockService
            .Setup(s => s.GetHoldingsAsync("CLT-001", "ACC-UNKNOWN"))
            .ReturnsAsync((List<HoldingSummaryDto>?)null);

        var result = await _controller.GetHoldings("CLT-001", "ACC-UNKNOWN");

        Assert.IsType<NotFoundResult>(result);
    }
}
