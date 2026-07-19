# DataIngestion

A data ingestion pipeline that receives a webhook with a ZIP URL, downloads and parses JSON client files, and persists the data to SQLite via EF Core. Includes a Razor Pages UI for triggering ingestion and browsing client/account/holding data with historical as-of queries.

## Stack

- **Framework:** ASP.NET Core Web API + Razor Pages (.NET 10)
- **ORM:** Entity Framework Core with SQLite (`dataingestion.db` created on first run)
- **UI:** Razor Pages + Bootstrap 5 + DataTables
- **Tests:** xUnit + Moq
- **API Docs:** Swagger at `/swagger`

## Project structure

```
DataIngestion.Model/          EF Core entities, DTOs, DbContext
DataIngestion.Svc/            Ingestion and query services
DataIngestion.Api/            Web API controllers + Razor Pages UI
DataIngestion.Tests/          xUnit test suite
```

```
DataIngestion.Api/
├── Controllers/
│   ├── WebhookController.cs      POST /api/webhook
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
```

## Getting started

```bash
cd DataIngestion.Api
dotnet run
```

- UI: `http://localhost:5141`
- Swagger: `http://localhost:5141/swagger`
- DB: `dataingestion.db` at the repo root (created automatically on first run)

> If you've run the app before and the schema has changed, delete `dataingestion.db` before restarting.

## Running tests

```bash
dotnet test
```

## Testing ingestion via curl

```bash
curl -X POST http://localhost:5141/api/webhook \
  -H "Content-Type: application/json" \
  -d '{"url": "http://localhost:5141/test-data-v1.zip"}'
```

## How it works

### Data flow

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

### Append-only ingestion

- Each `POST /api/webhook` creates a new `IngestionRun` row
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

Four ZIPs in `wwwroot/` covering a progression of portfolio changes, useful for demonstrating the append-only and as-of features.

| Version | Clients | Accounts | Holdings |
|---------|---------|----------|----------|
| v1      | 3       | 5        | 11       |
| v2      | 5       | 9        | 18       |
| v3      | 4       | 8        | 21       |
| v4      | 6       | 12       | 30       |

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

1. Ingest v1 → 3 clients, Emily Wilson visible
2. Ingest v2 → 5 clients, Emily gone, 3 new clients, Jane expanded to 2 accounts
3. Ingest v3 → 4 clients, David + Alex gone, Maria Garcia in, Sarah drops to 1 account
4. Ingest v4 → 6 clients, Robert Chen + Lisa Park in, Jane adds SEP_IRA, Sarah back to 3 accounts
5. Set the as-of date picker between any two runs → grid snaps to that snapshot
6. Click a client → Accounts/Holdings pages preserve the as-of context
7. Try ingesting any ZIP again → duplicate warning

## Design decisions

| Decision | Rationale |
|----------|-----------|
| Append-only, not upsert | Preserves full snapshot history; enables as-of queries with no extra work |
| Awaited ingestion (not fire-and-forget) | Simple and fast enough for the expected data volume |
| `EnsureCreated()` not `Migrate()` | Simpler for local dev; swap to `Migrate()` for production |
| SQLite | Zero external dependencies, file-based; switch to Postgres by changing the connection string and calling `UseNpgsql(...)` |
| DTOs separate from EF models | Input shape from upstream doesn't dictate the DB schema |
| URL-based tabs | Tab clicks are GET requests, so data is always fresh without client-side state management |

## Known limitation

SQLite via EF Core cannot translate `DateTimeOffset` comparisons to SQL. `RunIdAsOfAsync` works around this by loading `(Id, KnowledgeDate)` tuples client-side and filtering in memory — acceptable since the number of ingestion runs stays small.
