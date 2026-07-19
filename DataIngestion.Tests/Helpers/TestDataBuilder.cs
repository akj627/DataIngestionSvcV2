using System.IO.Compression;
using System.Text;

namespace DataIngestion.Tests.Helpers;

internal static class TestDataBuilder
{
    public static byte[] CreateZip(Dictionary<string, string> entries)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var entryStream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                entryStream.Write(bytes);
            }
        }
        return ms.ToArray();
    }

    public static string ClientJson(
        string clientId = "CLT-TEST-001",
        string firstName = "Test",
        string lastName = "User",
        string email = "test@example.com",
        string advisorId = "ADV-0001",
        int accountCount = 1,
        int holdingsPerAccount = 1)
    {
        var accounts = Enumerable.Range(1, accountCount)
            .Select(i => AccountJson($"ACC-{clientId}-{i:D3}", holdingsPerAccount));

        return $$"""
        {
            "client_id": "{{clientId}}",
            "first_name": "{{firstName}}",
            "last_name": "{{lastName}}",
            "email": "{{email}}",
            "advisor_id": "{{advisorId}}",
            "last_updated": "2025-01-01T00:00:00Z",
            "accounts": [{{string.Join(",", accounts)}}]
        }
        """;
    }

    private static string AccountJson(string accountId, int holdingsCount)
    {
        var holdings = Enumerable.Range(1, holdingsCount)
            .Select(i => $$"""
            {
                "ticker": "TK{{i}}",
                "cusip": "12345678{{i}}",
                "description": "Test Holding {{i}}",
                "quantity": {{i * 10}}.0,
                "market_value": {{i * 1000}}.00,
                "cost_basis": {{i * 900}}.00,
                "price": {{i * 100}}.00,
                "asset_class": "US_EQUITY"
            }
            """);

        return $$"""
        {
            "account_id": "{{accountId}}",
            "account_type": "INDIVIDUAL",
            "custodian": "Test Custodian",
            "opened_date": "2023-01-01",
            "status": "ACTIVE",
            "cash_balance": 500.00,
            "total_value": 5000.00,
            "holdings": [{{string.Join(",", holdings)}}]
        }
        """;
    }
}
