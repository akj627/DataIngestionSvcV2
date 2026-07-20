# DataIngestion

A data ingestion pipeline that receives a webhook with a ZIP URL, downloads and parses JSON client files, and persists the data to SQLite via EF Core. Supports both synchronous and asynchronous ingestion. Includes a Razor Pages UI for triggering ingestion and browsing client/account/holding data with historical as-of queries.

## Stack

- **Framework:** ASP.NET Core Web API + Razor Pages (.NET 10)
- **ORM:** Entity Framework Core with SQLite (`dataingestionv2.db` created on first run)
- **UI:** Razor Pages + Bootstrap 5 + DataTables
- **Tests:** xUnit + Moq (50 tests)
- **API Docs:** Swagger at `/swagger`

## Project structure

```
DataIngestion.Model/          EF Core entities, DTOs, DbContext
DataIngestion.Svc/            Ingestion, query, and queue services
DataIngestion.Api/            Web API controllers + Razor Pages UI
DataIngestion.Tests/          xUnit test suite
```

```
DataIngestion.Api/
├── Controllers/
│   ├── WebhookController.cs      POST /api/webhook (sync), POST /api/webhook/async
│   ├── JobsController.cs         GET /api/jobs/{jobId}
│   └── ClientsController.cs      GET /api/clients, /accounts, /holdings
├── Pages/
│   ├── Index.cshtml              Dashboard — ingestion form + run history + clients grid
│   └── Clients/
│       ├── Accounts.cshtml       Accounts for a client
│       └── Holdings.cshtml       Holdings for an account
└── wwwroot/
    ├── test-data-v1.zip
    ├── test-data-v2.zip
    ├── test-data-v3.zip
    └── test-data-v4.zip

DataIngestion.Svc/
├── Services/
│   ├── IngestionService.cs           Downloads ZIP, parses JSON, inserts rows
│   ├── IngestionBackgroundService.cs BackgroundService — single-threaded queue consumer
│   ├── IngestionChannel.cs           Channel<T> implementation of IIngestionQueue
│   ├── IIngestionQueue.cs            Queue abstraction
│   └── ClientQueryService.cs         As-of query logic
```

## Getting started

```bash
cd DataIngestion.Api
dotnet run
```

- UI: `http://localhost:5141`
- Swagger: `http://localhost:5141/swagger`
- DB: `dataingestionv2.db` at the repo root (created automatically on first run)

> If you've run the app before and the schema has changed, delete `dataingestionv2.db` before restarting.

## Running tests

```bash
dotnet test
```

## API endpoints

### Sync ingestion
```bash
curl -X POST http://localhost:5141/api/webhook \
  -H "Content-Type: application/json" \
  -d '{"url": "http://localhost:5141/test-data-v1.zip"}'
# → 200 OK { runId, clientsProcessed, accountsProcessed, holdingsProcessed, knowledgeDate }
```

### Async ingestion
```bash
curl -X POST http://localhost:5141/api/webhook/async \
  -H "Content-Type: application/json" \
  -d '{"url": "http://localhost:5141/test-data-v2.zip"}'
# → 202 Accepted { jobId }

curl http://localhost:5141/api/jobs/{jobId}
# → 200 OK { jobId, status, runId, clientsProcessed, ... }
```

## How it works

### Sync data flow

```
POST /api/webhook
  → WebhookController (validates, delegates)
  → IngestionService.IngestAsync(url)
      → HttpClient downloads ZIP bytes
      → SHA-256 hash checked — duplicate ZIP rejected (409)
      → ZipArchive extracts .json entries
      → JsonSerializer deserializes → ClientDto[]
      → Creates IngestionRun (KnowledgeDate = UtcNow)
      → For each client: insert fresh rows (append-only, no upsert)
      → DbContext.SaveChangesAsync()
  → Returns IngestionResult (counts, RunId, KnowledgeDate)
```

### Async data flow

```
POST /api/webhook/async
  → WebhookController creates IngestionJob (Status=Pending) in DB
  → Enqueues (jobId, url) onto Channel<T>
  → Returns 202 Accepted { jobId }

IngestionBackgroundService (single reader, sequential)
  → Dequeues next job
  → Updates Status → Running
  → Calls IngestionService.IngestAsync(url)  ← same service as sync path
  → Updates Status → Completed (or Failed) + stores counts

GET /api/jobs/{jobId}
  → Returns current JobStatusDto { status, runId, counts, error }
```

The UI's **Ingest (async)** button posts to `/api/webhook/async` and polls `GET /api/jobs/{jobId}` every 2 seconds until the job completes, then refreshes the runs grid.

### Why single-threaded consumer

The background service uses `Channel<T>` with `SingleReader = true` and processes jobs one at a time. SQLite does not support concurrent writes — a parallel consumer would cause lock contention. If switched to Postgres, the consumer count could be increased by bumping channel options and registering multiple hosted service instances.

### Append-only ingestion

- Each ingestion (sync or async) creates a new `IngestionRun` row
- All clients/accounts/holdings are inserted fresh — nothing is updated or deleted
- "Current" data = rows scoped to `MAX(IngestionRun.Id)`
- "Historical" data = rows scoped to the latest run on or before a given date
- Composite unique index on `(ClientId, IngestionRunId)` — the same client can appear across multiple runs

### As-of / historical queries

`IClientQueryService` methods accept `DateTimeOffset? asOf = null`:
- `null` → latest run (`MAX(Id)`)
- date provided → latest run where `KnowledgeDate <= asOf`

The UI exposes this via an "as of" date picker on the Clients tab. The `?asOf=` query parameter is threaded through Index → Accounts → Holdings so breadcrumb navigation preserves the historical context.

### Duplicate detection

Each ZIP's SHA-256 hash is stored on `IngestionRun`. Attempting to ingest the same ZIP content again returns a warning (UI) or 409 Conflict (API), regardless of the URL used.

## Test data

Six ZIPs in `wwwroot/` for different scenarios.

### Progression snapshots (as-of demo)

| File | Clients | Accounts | Holdings | Purpose |
|------|---------|----------|----------|---------|
| test-data-v1.zip | 3 | 5 | 11 | Baseline snapshot |
| test-data-v2.zip | 5 | 9 | 18 | Clients added, holdings changed |
| test-data-v3.zip | 4 | 8 | 21 | Clients removed and added |
| test-data-v4.zip | 6 | 12 | 30 | Further changes |

### Load test

| File | Clients | Accounts | Holdings | Notes |
|------|---------|----------|----------|-------|
| test-data-v500.zip | 500 | 1000 | 3000 | Sync ingestion: ~7.5s · Async caller response: ~300ms |

### Partial failure

| File | Clients | Notes |
|------|---------|-------|
| test-data-with-error.zip | 10 entries (9 valid, 1 malformed) | CLT-50005 has invalid JSON — skipped with a warning, other 9 ingest cleanly |

### Client roster across versions

| Client | v1 | v2 | v3 | v4 |
|--------|----|----|----|----|
| CLT-29481 Jane Smith    | 1 acct / 3 holdings  | 2 accts / 5 holdings | 2 accts / 6 holdings | 3 accts / 8 holdings |
| CLT-30155 Sarah Johnson | 2 accts / 4 holdings | 2 accts / 4 holdings | 1 acct / 3 holdings  | 3 accts / 6 holdings |
| CLT-31500 Emily Wilson  | 2 accts / 3 holdings | removed              | —                    | —                    |
| CLT-30012 Michael Chen  | —                    | 2 accts / 4 holdings | 3 accts / 6 holdings | 2 accts / 6 holdings |
| CLT-31000 David Martinez| —                    | 2 accts / 3 holdings | removed              | —                    |
| CLT-32000 Alex Thompson | —                    | 1 acct / 2 holdings  | removed              | —                    |
| CLT-33000 Maria Garcia  | —                    | —                    | 2 accts / 6 holdings | 2 accts / 7 holdings |
| CLT-34000 Robert Chen   | —                    | —                    | —                    | 2 accts / 5 holdings |
| CLT-35000 Lisa Park     | —                    | —                    | —                    | 1 acct / 4 holdings  |

### Suggested walkthrough

1. Ingest v1 (sync) → 3 clients, Emily Wilson visible
2. Ingest v2 (async) → 202 returned immediately, status card polls to completion
3. Ingest v3 → 4 clients, David + Alex gone, Maria Garcia in
4. Ingest v4 → 6 clients, Robert Chen + Lisa Park in
5. Set the as-of date picker between any two runs → grid snaps to that snapshot
6. Click a client → Accounts/Holdings pages preserve the as-of context
7. Try ingesting any ZIP again → duplicate warning

## Design decisions

| Decision | Rationale |
|----------|-----------|
| Append-only, not upsert | Preserves full snapshot history; enables as-of queries with no extra work |
| `Channel<T>` not a broker | Zero external dependencies; interface (`IIngestionQueue`) can be swapped for RabbitMQ or Azure Service Bus without changing the consumer |
| Single-threaded consumer | SQLite doesn't support concurrent writes; bump to multiple consumers when switching to Postgres |
| Same `IngestionService` for sync and async | No duplication — the background service calls the exact same code path |
| `EnsureCreated()` not `Migrate()` | Simpler for local dev; swap to `Migrate()` for production |
| SQLite | Zero external dependencies, file-based; switch to Postgres by changing the connection string and calling `UseNpgsql(...)` |
| DTOs separate from EF models | Input shape from upstream doesn't dictate the DB schema |

## Known limitation

SQLite via EF Core cannot translate `DateTimeOffset` comparisons to SQL. `RunIdAsOfAsync` works around this by loading `(Id, KnowledgeDate)` tuples client-side and filtering in memory — acceptable since the number of ingestion runs stays small.
