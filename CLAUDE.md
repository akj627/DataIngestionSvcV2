# DataIngestion — Project Context

## What this is
A data ingestion pipeline service that receives a webhook with a ZIP URL, downloads and parses JSON client files, and persists the data to SQLite via EF Core. Includes a Razor Pages UI for triggering ingestion and browsing client/account/holding data with historical as-of queries.

## Stack
- **Framework:** ASP.NET Core Web API + Razor Pages (.NET 10)
- **ORM:** Entity Framework Core with SQLite (`dataingestion.db` created on first run)
- **DI:** ASP.NET Core built-in (`Microsoft.Extensions.DependencyInjection`)
- **UI:** Razor Pages + Bootstrap 5 + DataTables (CDN)
- **Tests:** xUnit + Moq (37 tests)
- **API Docs:** Swagger at `/swagger`

## Project structure
```
DataIngestion.Api/
├── Controllers/
│   ├── WebhookController.cs      POST /api/webhook
│   └── ClientsController.cs      GET /api/clients, /accounts, /holdings
├── DTOs/
│   └── PartnerDtos.cs            JSON shapes from partner + request/response types
├── Models/
│   ├── IngestionRun.cs           EF Core entity — envelope for one delivery
│   ├── Client.cs                 EF Core entity (FK → IngestionRun)
│   ├── Account.cs                EF Core entity
│   └── Holding.cs                EF Core entity
├── Data/
│   └── AppDbContext.cs           EF Core DbContext, DB created via EnsureCreated()
├── Services/
│   ├── IIngestionService.cs      Interface — two overloads (URL and bytes)
│   ├── IngestionService.cs       Downloads ZIP, parses JSON, inserts fresh per run
│   ├── IClientQueryService.cs    Interface — all queries accept optional asOf
│   └── ClientQueryService.cs     Scopes all queries to latest (or as-of) run
├── Pages/
│   ├── Index.cshtml / .cs        Dashboard — ingestion form + clients grid
│   └── Clients/
│       ├── Accounts.cshtml / .cs Accounts for a client
│       └── Holdings.cshtml / .cs Holdings for an account
├── wwwroot/
│   ├── test-data-v1.zip          5 clients (original snapshot)
│   └── test-data-v2.zip          5 clients (Emily Wilson → Alex Thompson, Jane Smith updated)
└── Program.cs                    DI registration, middleware config

DataIngestion.Tests/
├── Controllers/
│   ├── WebhookControllerTests.cs
│   └── ClientsControllerTests.cs
├── Services/
│   ├── IngestionServiceTests.cs
│   └── ClientQueryServiceTests.cs
├── Helpers/
│   ├── MockHttpMessageHandler.cs
│   └── TestDataBuilder.cs
└── DataIngestion.Tests.csproj
```

## How to run
```bash
cd DataIngestion.Api
dotnet run
# App starts on http://localhost:5141 (or check Properties/launchSettings.json)
# UI:      http://localhost:5141
# Swagger: http://localhost:5141/swagger
# DB file: dataingestion.db at repo root (created automatically)
```

## How to run tests
```bash
dotnet test
```

## How to test ingestion
Once running, POST to the webhook with the locally served test ZIP:
```bash
curl -X POST http://localhost:5141/api/webhook \
  -H "Content-Type: application/json" \
  -d '{"url": "http://localhost:5141/test-data-v1.zip"}'
```

## Data flow
```
POST /api/webhook
  → WebhookController (validates, delegates)
  → IngestionService.IngestAsync(url)
      → HttpClient downloads ZIP bytes
      → ZipArchive extracts .json entries
      → JsonSerializer deserializes → ClientDto[]
      → Creates IngestionRun (KnowledgeDate = UtcNow)
      → For each client: insert fresh rows (append-only, no upsert)
      → DbContext.SaveChangesAsync()
  → Returns IngestionResult (counts, RunId, KnowledgeDate)
```

## Append-only ingestion strategy
- Each `POST /api/webhook` creates a new `IngestionRun` row
- All clients/accounts/holdings are inserted fresh — nothing is updated or deleted
- "Current" data = rows scoped to `MAX(IngestionRun.Id)`
- "Historical" data = rows scoped to the latest run on or before a given date
- Composite unique index on `(ClientId, IngestionRunId)` — same client can appear in multiple runs

## As-of / historical queries
`IClientQueryService` methods accept `DateTimeOffset? asOf = null`:
- `null` → latest run (`MAX(Id)`)
- date provided → latest run where `KnowledgeDate <= asOf` (resolved client-side because SQLite cannot translate `DateTimeOffset` comparisons to SQL)

The UI exposes this via a `?asOf=` query parameter that flows through Index → Accounts → Holdings pages, so the breadcrumb navigation preserves the historical context.

## Key decisions made
- **Awaited (not fire-and-forget):** ~1000 clients is fast enough; avoids background job complexity
- **Append-only not upsert:** Preserves full snapshot history; enables as-of queries with no extra work
- **EnsureCreated() not Migrate():** Simpler for demo; swap to Migrate() for production
- **SQLite:** Zero external dependencies, file-based. To switch to Postgres: change connection string + `UseNpgsql(...)`, regenerate migrations
- **DTOs separate from models:** Partner JSON shape doesn't dictate DB schema
- **HttpClient via DI:** Registered with `AddHttpClient<IIngestionService, IngestionService>()` for proper lifecycle management
- **File upload via `Request.Form.Files`:** `[BindProperty] IFormFile` was unreliable; raw `Request.Form.Files.GetFile("ZipFile")` is used instead
- **`ShowInputError` boolean:** Used instead of `!ModelState.IsValid` to avoid false positives on the ingestion form

## Test data files (wwwroot/)

Four ZIPs covering a progression of client portfolio changes for demoing append-only / as-of history.

### Counts per version

| Version | Clients | Accounts | Holdings | Snapshot date |
|---|---|---|---|---|
| v1 | 3 | 5 | 11 | 2025-03-02 |
| v2 | 5 | 9 | 18 | 2025-06-15 |
| v3 | 4 | 8 | 21 | 2025-09-05 |
| v4 | 6 | 12 | 30 | 2025-11-20 |

### Client roster across versions

| Client | v1 | v2 | v3 | v4 |
|---|---|---|---|---|
| CLT-29481 Jane Smith | ✓ 1 acct / 3 holdings | ✓ 2 accts / 5 holdings | ✓ 2 accts / 6 holdings | ✓ 3 accts / 8 holdings |
| CLT-30155 Sarah Johnson | ✓ 2 accts / 4 holdings | ✓ 2 accts / 4 holdings | ✓ 1 acct / 3 holdings | ✓ 3 accts / 6 holdings |
| CLT-31500 Emily Wilson | ✓ 2 accts / 3 holdings | removed | — | — |
| CLT-30012 Michael Chen | — | ✓ 2 accts / 4 holdings | ✓ 3 accts / 6 holdings | ✓ 2 accts / 6 holdings |
| CLT-31000 David Martinez | — | ✓ 2 accts / 3 holdings | removed | — |
| CLT-32000 Alex Thompson | — | ✓ 1 acct / 2 holdings | removed | — |
| CLT-33000 Maria Garcia | — | — | ✓ 2 accts / 6 holdings | ✓ 2 accts / 7 holdings |
| CLT-34000 Robert Chen | — | — | — | ✓ 2 accts / 5 holdings |
| CLT-35000 Lisa Park | — | — | — | ✓ 1 acct / 4 holdings |

### Changes per version

**v1 → v2** (3→5 clients, +7 holdings)
- CLT-31500 Emily Wilson **removed**
- CLT-30012 Michael Chen **added** (2 accts: INDIVIDUAL with AAPL/MSFT/GOOGL; TRADITIONAL_IRA with AGG)
- CLT-31000 David Martinez **added** (2 accts: INDIVIDUAL with SPY/EFA; SEP_IRA with BND)
- CLT-32000 Alex Thompson **added** (1 acct: INDIVIDUAL with QQQ/ARKK)
- CLT-29481 Jane Smith: ROTH_IRA added (VOO+AMZN); INDIVIDUAL BND removed, MSFT+VXUS added, VTI 150→175
- CLT-30155 Sarah Johnson: JOINT TLT removed, SPY added; QQQ 200→220

**v2 → v3** (5→4 clients, +3 holdings)
- CLT-31000 David Martinez **removed**
- CLT-32000 Alex Thompson **removed**
- CLT-33000 Maria Garcia **added** (2 accts: INDIVIDUAL with VOO/VTI/SPY/AMZN; ROTH_IRA with BND/AGG)
- CLT-29481 Jane Smith: INDIVIDUAL MSFT removed, AMZN+NVDA added, VTI 175→200; ROTH_IRA adds QQQ
- CLT-30012 Michael Chen: ROTH_IRA added (VOO); INDIVIDUAL GOOGL removed, NVDA added; TRADITIONAL_IRA adds BND
- CLT-30155 Sarah Johnson: ROTH_IRA **closed** (1 acct left); JOINT TLT-based holding → NVDA, QQQ 220→180, GLD 150→200

**v3 → v4** (4→6 clients, +9 holdings)
- CLT-34000 Robert Chen **added** (2 accts: INDIVIDUAL with AAPL/MSFT/NVDA; TRADITIONAL_IRA with VOO/BND)
- CLT-35000 Lisa Park **added** (1 acct: INDIVIDUAL with VTI/VXUS/BND/GLD)
- CLT-29481 Jane Smith: SEP_IRA added (BND+TLT); INDIVIDUAL NVDA removed, VTI/VXUS/AMZN up; ROTH_IRA adds MSFT, VOO up
- CLT-30012 Michael Chen: ROTH_IRA **closed**; INDIVIDUAL GOOGL re-added, NVDA up; TRADITIONAL_IRA AGG up
- CLT-30155 Sarah Johnson: ROTH_IRA re-opened (VTI+VOO) + INDIVIDUAL added (NVDA); JOINT SPY/GLD quantities updated
- CLT-33000 Maria Garcia: INDIVIDUAL NVDA added, VTI+SPY up; ROTH_IRA AGG up

### Demo sequence for as-of feature
1. Ingest v1 → 3 clients, Emily Wilson visible
2. Ingest v2 → 5 clients, Emily Wilson gone, 3 new clients, Jane Smith expanded to 2 accounts
3. Ingest v3 → 4 clients, David+Alex gone, Maria Garcia in, Sarah drops to 1 account, Michael adds ROTH_IRA
4. Ingest v4 → 6 clients, Robert Chen + Lisa Park in, Jane adds SEP_IRA, Sarah back to 3 accounts
5. Set date picker between any two runs → grid snaps to that snapshot
6. Click a client → Accounts/Holdings pages preserve the as-of context
7. Try ingesting any ZIP again → duplicate warning (same ZIP hash rejected)

## Known SQLite limitation
SQLite via EF Core cannot translate `DateTimeOffset` comparisons (`<=`, `>=`) to SQL. Workarounds used:
- `LatestRunIdAsync` uses `MaxAsync(r => (int?)r.Id)` (not `OrderByDescending(KnowledgeDate)`)
- `RunIdAsOfAsync(asOf)` loads `(Id, KnowledgeDate)` for all runs client-side, then filters in memory — acceptable since run count stays small
