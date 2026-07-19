# Interview Discussion Guide

## 1. Walkthrough (~15 min)

### What to demo
1. Show the Razor Pages UI at `http://localhost:5141`
2. Run ingestion with `http://localhost:5141/test-data-v1.zip` — show the result banner (Run #, clients/accounts/holdings counts)
3. Run ingestion again with `http://localhost:5141/test-data-v2.zip` — show a new run is created, grid refreshes
4. Click a client row — show accounts page
5. Click an account row — show holdings page
6. Optionally: POST directly via curl or Swagger to show the API layer

### Design decisions to articulate

**Why append-only instead of upsert?**
> Each ingestion run is a full snapshot from the partner. By inserting fresh rows per run rather than overwriting, we preserve the complete history of every client's portfolio at every point in time. This lets us answer "what did Jane Smith's portfolio look like on March 2nd?" without any extra work.

**Why `IngestionRun`?**
> It acts as the envelope for one delivery. Every client, account, and holding row has a foreign key back to the run that created it. Queries for "current" data just find the latest run ID and scope everything to it. Queries for historical data scope to any run ID.

**Why SQLite?**
> Zero external dependencies — the DB is a single file, created automatically on first run. For production, you swap the connection string and call `UseNpgsql()` instead of `UseSqlite()`. The rest of the code doesn't change.

**Why `EnsureCreated()` not `Migrate()`?**
> `EnsureCreated()` is simpler for a demo — it creates the schema once and never touches it again. The trade-off is that schema changes require deleting the DB file and recreating. In production you'd use `dotnet ef migrations add` and `Migrate()` so schema changes are tracked and applied safely.

**Why awaited (synchronous) not fire-and-forget?**
> At ~1,000 clients, ingestion completes in under a second. A background job would add complexity (status polling, error surfacing) with no real benefit at this scale. The webhook caller gets the result immediately in the response body.

**Why DTOs separate from EF models?**
> The partner's JSON shape (`client_id`, `advisor_id`, snake_case) doesn't have to dictate the DB schema. DTOs absorb the partner's naming conventions; EF models use our conventions. If the partner changes their payload structure, we update the DTO and the mapping — the DB schema stays stable.

**Why `HttpClient` via DI (`AddHttpClient<>`) not `new HttpClient()`?**
> `new HttpClient()` created inside a service causes socket exhaustion under load — it doesn't reuse connections. `IHttpClientFactory` (registered via `AddHttpClient<>`) manages pooling and lifetime correctly.

---

## 2. Live Extension (~30 min)

These are the most likely things the interviewer will ask you to add live. Know the approach for each.

### Historical query — "show portfolio as of a specific date"

The data is already there — every row has `IngestionRunId` and every run has `KnowledgeDate`. Just need an endpoint that finds the latest run *on or before* the requested date.

```csharp
// GET /api/clients/{clientId}/accounts?asOf=2025-03-01
var run = await _dbContext.IngestionRuns
    .Where(r => r.KnowledgeDate <= asOfDate)
    .OrderByDescending(r => r.Id)
    .FirstOrDefaultAsync();
```

Then scope the client/account query to that `run.Id` instead of `MaxAsync(r => r.Id)`.

### Diff between runs — "what changed from run N-1 to N?"

Compare holdings between two consecutive runs for the same client.

```csharp
// GET /api/clients/{clientId}/diff?runA=1&runB=2
var holdingsA = await GetHoldingsForRun(clientId, runA);
var holdingsB = await GetHoldingsForRun(clientId, runB);

var added   = holdingsB.Where(h => !holdingsA.Any(x => x.Ticker == h.Ticker));
var removed = holdingsA.Where(h => !holdingsB.Any(x => x.Ticker == h.Ticker));
var changed = holdingsB.Where(h => holdingsA.Any(x => x.Ticker == h.Ticker && x.Quantity != h.Quantity));
```

### Cross-client analytics — "total AUM by advisor"

One LINQ aggregation scoped to the latest run.

```csharp
// GET /api/advisors/summary
var latestRunId = await LatestRunIdAsync();
var summary = await _dbContext.Clients
    .Where(c => c.IngestionRunId == latestRunId)
    .GroupBy(c => c.AdvisorId)
    .Select(g => new {
        AdvisorId = g.Key,
        ClientCount = g.Count(),
        TotalAUM = g.SelectMany(c => c.Accounts).Sum(a => a.TotalValue)
    })
    .ToListAsync();
```

### Holdings search — "find all clients holding AAPL"

```csharp
// GET /api/holdings/search?ticker=AAPL
var latestRunId = await LatestRunIdAsync();
var results = await _dbContext.Holdings
    .Where(h => h.Ticker == ticker && h.Account.Client.IngestionRunId == latestRunId)
    .Include(h => h.Account).ThenInclude(a => a.Client)
    .Select(h => new { h.Account.Client.ClientId, h.Account.AccountId, h.Quantity, h.MarketValue })
    .ToListAsync();
```

### Partial failure handling — "what if JSON #500 of 1000 is malformed?"

Current behavior: `ParseZip()` already logs a warning and skips bad entries — the run still completes with whatever parsed successfully.

Better behavior for production: wrap the entire `ProcessAsync` in a try/catch, add a `Status` field to `IngestionRun` (`Queued / Processing / Complete / PartialFailure / Failed`), and record which files failed.

```csharp
public enum IngestionStatus { Processing, Complete, PartialFailure, Failed }

// On IngestionRun:
public IngestionStatus Status { get; set; }
public string? ErrorDetails { get; set; }
```

### Webhook authentication — "how do you verify this came from the partner?"

HMAC signature validation on the incoming request:

```csharp
// Partner sends: X-Webhook-Signature: sha256=<hmac of body with shared secret>
var body = await Request.Body.ReadToEndAsync();
var expected = ComputeHmac(body, _sharedSecret);
var received = Request.Headers["X-Webhook-Signature"];
if (!CryptographicOperations.FixedTimeEquals(expected, received))
    return Unauthorized();
```

Use `FixedTimeEquals` (not `==`) to prevent timing attacks.

---

## 3. Design / Scaling Discussion (~15 min)

### "How would you scale this to 100,000 clients or 50 simultaneous partners?"

**Problem with current design at scale:**
- Webhook handler blocks until ingestion completes — times out under load
- SQLite has file-level write locks — concurrent ingestion runs will serialize or fail
- Single process — no horizontal scaling

**Distributed approach:**

```
Partner → POST /api/webhook → 202 Accepted immediately
                ↓
         Message Queue (AWS SQS / Azure Service Bus / RabbitMQ)
                ↓
         Worker Service (1..N instances, horizontally scaled)
              → downloads ZIP
              → parses + persists
              → marks run Complete
```

**What changes in code:**
- Webhook returns `202 Accepted` with a `runId` — caller polls `GET /api/runs/{runId}/status`
- `IngestionRun` gets a `Status` enum (`Queued / Processing / Complete / Failed`)
- SQLite → Postgres (concurrent writers, connection pooling)
- Worker calls the same `IIngestionService.IngestAsync()` — no logic changes

**Idempotency:** If the same ZIP URL is delivered twice (at-least-once delivery from the queue), deduplicate by hashing the ZIP bytes and checking for an existing run with that hash before processing.

**Dead-letter queue:** After N failed retries, move the message to a DLQ and alert on-call. Don't silently drop failures.

### "What would you change for production?"

| Current (demo) | Production |
|---|---|
| `EnsureCreated()` | EF Core migrations (`dotnet ef migrations add`) |
| SQLite | Postgres |
| Synchronous ingestion | Queue-based async worker |
| No auth on webhook | HMAC signature verification |
| `IngestionRun` has no status | Add `Status` + `ErrorDetails` fields |
| No observability | Structured logging to a sink (Datadog / Seq), metrics on run duration and failure rate |

### "What breaks if the same ZIP is sent twice?"

Currently: two identical `IngestionRun` rows are created and all data is duplicated. The UI shows the latest run so reads are correct, but the DB accumulates redundant data.

Fix: hash the ZIP bytes before processing, store the hash on `IngestionRun`, and return the existing run if a match is found:

```csharp
var hash = Convert.ToHexString(SHA256.HashData(zipBytes));
var existing = await _dbContext.IngestionRuns.FirstOrDefaultAsync(r => r.ZipHash == hash);
if (existing != null) return BuildResultFrom(existing); // idempotent
```
