<img src="https://raw.githubusercontent.com/jchristn/HnswLite/main/assets/logo.png" width="256" height="256">

# HnswLite

A pure C# implementation of Hierarchical Navigable Small World (HNSW) graphs for approximate nearest neighbor search. HnswLite ships as an embeddable library, a REST server, a React dashboard, and SDKs in three languages.

> **Note**: This library is in its early stages of development. We welcome your patience, constructive feedback, and contributions! Please be kind and considerate when reporting issues or suggesting improvements. I am not an expert on this topic and relied heavily on available AI tools to build this library. Pull requests are greatly appreciated!

[![NuGet Version](https://img.shields.io/nuget/v/HnswLite.svg?style=flat)](https://www.nuget.org/packages/HnswLite/) [![NuGet](https://img.shields.io/nuget/dt/HnswLite.svg)](https://www.nuget.org/packages/HnswLite)

## Overview

HnswLite implements the Hierarchical Navigable Small World algorithm, which provides fast approximate nearest-neighbor search with excellent recall rates. The library is designed to be embeddable, extensible, and easy to use from any .NET application — or from Python / JavaScript / any HTTP client via the REST server.

### Repository layout

| Path | Purpose |
|---|---|
| `src/HnswIndex/` | Core library (`HnswLite` on NuGet) |
| `src/HnswIndex.RamStorage/` | In-memory storage provider |
| `src/HnswIndex.SqliteStorage/` | SQLite storage provider |
| `src/HnswIndex.PostgresqlStorage/` | PostgreSQL storage provider |
| `src/HnswIndex.Server/` | Standalone REST server (Watson 7) |
| `src/Test.Shared/` + `src/Test.{Automated,XUnit,NUnit,MSTest}/` | Touchstone-driven test suites |
| `dashboard/` | React 19 + Vite dashboard |
| `sdk/csharp/`, `sdk/python/`, `sdk/js/` | Client SDKs with 100% endpoint coverage |
| `docker/` | `compose.yaml` for server + dashboard + Prometheus/Tempo/Grafana, plus factory-reset and update scripts |
| `assets/grafana/` | Grafana dashboards (provisioned into the `HnswLite` folder) |

### Key features

- **Pure C# implementation** — no native dependencies.
- **Thread-safe**, async/await with cancellation tokens throughout.
- **Async-only `IStorageProvider` interface** - build your own backend by implementing one provider contract.
- **Storage backends** - PostgreSQL, SQLite, and RAM.
- **Multiple distance metrics** — Euclidean, Cosine, Dot Product, with SIMD acceleration via `System.Numerics.Vector<float>`.
- **Batch operations** — efficient bulk insert and remove.
- **Persistence by default** - the REST server and Docker deployment default to PostgreSQL; SQLite remains available for embedded and fallback deployments.
- **Paginated enumeration contract** across every GET collection endpoint (`EnumerationQuery` / `EnumerationResult<T>`).
- **OPTIONS preflight + CORS** out of the box in the REST server.
- **Observability built in.** Metrics and traces for HTTP, every service operation and its stages, graph operations, storage calls, and runtime health, with Prometheus, Tempo, and six Grafana dashboards in the compose stack. See [TELEMETRY.md](TELEMETRY.md).

## New in v2.2.1

- Dependency refresh: Microsoft.Data.Sqlite and System.Text.Json `10.0.12`, Watson `7.2.2`, SyslogLogging `2.3.1`, and current test tooling (Touchstone `0.2.0`, NUnit `5.0.0`, MSTest `4.4.1`). See [CHANGELOG.md](CHANGELOG.md).
- Package versions: `HnswLite.SqliteStorage` 2.2.1, `HnswLite.Sdk` 2.1.1.

## New in v2.2.0

- **Metrics and traces in the libraries.** `HnswLite`, `HnswLite.SqliteStorage`, and `HnswLite.PostgresqlStorage` emit through the .NET base class library on the `HnswLite` meter and activity source: per-operation and per-stage durations (including time queued for the index write lock), outcomes with `error.type`, lock waiters, search work and cache hit ratio, storage call latency and errors, and transaction outcomes. No new package dependency, and it costs nearly nothing when nobody subscribes.
- **Server observability.** The REST server hosts one Radiant telemetry pipeline that exports traces to Tempo over OTLP and serves a Prometheus endpoint for Watson's HTTP metrics, the library metrics, the server's own `HnswLite.Server` metrics (service operations and stages, auth decisions, API errors, the startup reload job, inventory, build and config info), the Npgsql connection pool, and the .NET runtime. Configure it under `Telemetry` in `hnswindex.json`.
- **Observability stack.** `docker/compose.yaml` adds Prometheus, Tempo, and Grafana with datasources and six domain dashboards (Overview, HTTP, Index Operations, Search, Storage & Integrations, Runtime & Lifecycle) provisioned as code.
- **External services card.** The dashboard home page links to Grafana, Prometheus, and Tempo with their URLs, default credentials, and reachability.
- Package versions: `HnswLite` 2.1.0, `HnswLite.SqliteStorage` 2.2.0, `HnswLite.PostgresqlStorage` 2.2.0.

## New in v2.0.0

- **Breaking async storage API.** `IHnswStorage`, `IHnswLayerStorage`, `IHnswNode`, and `IStorageProvider` are async-only. Node metadata is updated through `SetMetadataAsync(...)`, and providers implement `IAsyncDisposable`.
- **PostgreSQL provider.** `HnswLite.PostgresqlStorage` stores index metadata, vectors, graph layers, neighbors, and vector metadata in PostgreSQL using async Npgsql APIs.
- **PostgreSQL multi-index model.** One PostgreSQL schema stores all logical indexes in shared tables partitioned by `hnsw_indexes.id`/`index_id`.
- **PostgreSQL server default.** New server-created indexes default to `PostgreSQL` unless `StorageType` is explicitly set to `SQLite` or `RAM`.
- **Docker PostgreSQL deployment.** `docker/compose.yaml` starts PostgreSQL, runs a one-shot provisioner for schema/default records, then starts the server and dashboard.
- **SDK and test updates.** C#, Python, and JS/TS SDK examples and harnesses default to Docker's PostgreSQL-backed server.

## New in v1.2.0

- **Metadata filters.** Both `POST /v1.0/indexes/{name}/search` and `GET /v1.0/indexes/{name}/vectors` now accept optional `Labels`, `Tags`, and `CaseInsensitive` parameters. Filtering uses **AND** semantics on both — every label must be present and every tag key/value must match for a record to be kept. When `CaseInsensitive` is true, labels, tag keys, and tag values are all compared using `StringComparison.OrdinalIgnoreCase`.
- **`FilteredCount` on responses.** Both `SearchResponse` and `EnumerationResult<T>` now include a `FilteredCount` integer reporting how many candidates/records were dropped by the metadata filter — so callers can tell at a glance whether a restrictive filter is responsible for a short page.
- Full coverage across **C# / Python / JS SDKs** and the **dashboard** (Search and Vectors pages).

### Filtering by labels and tags

Request body (search):
```json
POST /v1.0/indexes/demo/search
{
  "Vector": [0.1, 0.2, 0.3, 0.4],
  "K": 10,
  "Labels": ["red", "small"],
  "Tags": { "env": "prod", "owner": "alice" },
  "CaseInsensitive": false
}
```

Query-string (enumerate):
```bash
curl -H "x-api-key: $API_KEY" \
  "http://localhost:8080/v1.0/indexes/demo/vectors?labels=red,small&tags=env:prod,owner:alice&caseInsensitive=true&includeVectors=false"
```

Both endpoints return a `FilteredCount` alongside the existing fields:
```json
{
  "Results": [ ... ],
  "SearchTimeMs": 2.41,
  "FilteredCount": 3
}
```

**Limitations (v1.2):**
- Labels passed via query string cannot contain `,`; tag keys cannot contain `:` or `,`; tag values cannot contain `,`. Use the JSON body form (`POST /search`) when filter tokens contain these characters.
- Tag values are compared as strings (via `Convert.ToString(value, InvariantCulture)` on the stored side). Numeric / boolean tag values stringify predictably (`42` → `"42"`, `true` → `"True"`).
- Search applies the filter **after** HNSW traversal, so restrictive filters can return fewer than K results — `FilteredCount` tells you how many were dropped.

## New in v1.1.x

See [CHANGELOG.md](CHANGELOG.md) for the full list. Highlights:

### Platform

- Multi-target `net8.0` + `net10.0` across the library, server, and tests.
- Watson web server upgraded to `7.0.11`. OPTIONS pre-flight is handled by Watson's native hook and bypasses authentication; CORS response headers are emitted on every response from a configurable `Cors` block in `hnswindex.json`.

### Vector metadata

- Every vector now carries optional **`Name`** (string), **`Labels`** (list of strings), and **`Tags`** (string → object dictionary) alongside its GUID and float array.
- Metadata is exposed as mutable properties on `IHnswNode`. SQLite writes are immediate — every setter commits an `UPDATE`, so metadata survives even an unclean process crash.
- The REST API accepts and returns metadata on every vector endpoint (add, batch-add, enumerate, get-single, search).
- The dashboard Vectors table shows Name and Labels; the Add / Edit / Search-result-detail modals all expose all three fields.

### Storage abstraction

- **`IStorageProvider`** - a single interface that combines `IHnswStorage`, `IHnswLayerStorage`, transaction/flush hooks, and `IAsyncDisposable`. `HnswIndex` accepts it via a provider constructor.
- **`RamStorageProvider`** and **`SqliteStorageProvider`** consolidate the previous pair-of-objects setup into one lifecycle-managed instance.

### Server persistence

- **Historical v1.1 default:** `StorageType` changed from `RAM` to `SQLite` in v1.1. In v2.0.0, the server and Docker default is `PostgreSQL`.
- Server-owned metadata (GUID / dimension / distance function / M / MaxM / EfConstruction / created timestamp) is persisted **inside each SQLite `.db` file** via the library's `hnsw_metadata` key/value table under a `server.*` key prefix. No manifest file — the database IS the manifest.
- `IndexManager` scans the SQLite directory on startup, opens every `.db`, and re-registers the index. Indexes survive restarts.

### Paginated enumeration across every GET

- `GET /v1.0/indexes` is paginated. Query-string parameters populate an `EnumerationQuery`; response is an `EnumerationResult<T>`. No more "get all" endpoints.
- New **`GET /v1.0/indexes/{name}/vectors`** — paginated vector enumeration with an `includeVectors=true|false` switch for whether vector bodies are inlined.
- New **`GET /v1.0/indexes/{name}/vectors/{guid}`** — fetch a single vector (always includes the `Vector` array).

### Performance

- SIMD-accelerated distance functions (`Euclidean`, `Cosine`, `DotProduct`) via `System.Numerics.Vector<float>` + `CollectionsMarshal.AsSpan`, with a scalar fallback.
- `Task.Run` wrappers removed from `SelectNeighborsHeuristicAsync`, `GreedySearchLayerAsync`, and `SearchLayerAsync` — async state-machine allocation eliminated on the search hot path.
- Pre-fetch + cached node references in neighbor selection — O(N²) storage round-trips collapsed to O(N).
- In-place sort in neighbor selection (no `.OrderBy().ToList()` allocations).
- `ContainsKey` + indexer → `TryGetValue` across hot paths.
- `ConfigureAwait(false)` on every library-internal await.
- Bounded `SearchContext` cache (default 50k nodes) to prevent unbounded memory growth on large searches.
- Span-based SQLite vector serialization (`MemoryMarshal.AsBytes` / `MemoryMarshal.Cast<byte, float>`).
- Sparse neighbor map in `RamHnswNode` — `HashSet<Guid>?[]` indexed by layer (max 64) replaces `Dictionary<int, HashSet<Guid>>`.
- `MinHeap.GetAll()` switched from LINQ `.OrderBy().ThenBy()` to in-place heap extraction.
- SQLite connection consolidation — both constructors now share a single helper that applies WAL + synchronous + cache + `mmap_size=256MB` + `wal_autocheckpoint=1000` PRAGMAs (previously only the default-table-name constructor was configured).

See [archive/PERFORMANCE_IMPROVEMENTS.md](archive/PERFORMANCE_IMPROVEMENTS.md) for details and remaining future work.

### Testing

- Unified Touchstone test suite: tests are defined once in `Test.Shared` and executed by **four** runners (`Test.Automated` console, `Test.XUnit`, `Test.NUnit`, `Test.MSTest`). Coverage now includes **97 cases** across shared suites including concurrency, cross-storage parity, and cluster-recall scenarios.

### Dashboard

- React 19 + Vite 8 + TypeScript dashboard at `dashboard/` with pages for **Indices**, **Vectors** (browse / edit / add / delete with an index dropdown and Add-vector modal), **Search**, **Request History** (30-day browser-local capture with hour / day / week / month ranges), **API Explorer**, **Server Info**, **Settings**, plus a login flow.
- Docker image `jchristn77/hnswlite-dashboard` with nginx serving the SPA and proxying `/v1.0/` to the server container.

### SDKs

Three new SDKs with 100% endpoint coverage + integration test harnesses:

- **C#** (`HnswLite.Sdk`) — `net8.0` / `net10.0`.
- **Python** (`hnswlite-sdk`) — Python 3.9+, `requests`.
- **JS / TS** (`hnswlite-sdk`) — Node 18+, zero runtime deps, native `fetch`.

### Docker

- `docker/compose.yaml` runs the server and dashboard together.
- `docker/factory/reset.bat` + `reset.sh` — factory-reset scripts.
- `clean.bat` + `clean.sh` in the server output directory — delete `hnswindex.json` / `data/` / `logs/` for a fresh start.

## Use cases

- **Semantic search** — find similar documents / sentences from embeddings.
- **Recommendation systems** — discover similar items / users / content.
- **Image similarity** — search on feature vectors.
- **Anomaly detection** — identify outliers by neighbour distance.
- **Clustering** — group similar items.
- **RAG** — retrieval-augmented generation for LLM applications.
- **Duplicate detection** — find near-duplicate content at scale.

## Performance & scalability

### Recommended limits

- **Vector dimensions**: 50–1000 (optimal: 128–768).
- **Dataset size**: up to 1–10M vectors depending on dimension and RAM.
- **Memory usage**: approximately `(vector_count × dimension × 4 bytes) + (vector_count × M × 32 bytes)`.

> These are estimates. The library has not been exhaustively load-tested.

### Parameters

- `M` — connections per vector (default 16). More connections → better recall, more memory. 16–32 works well for most cases.
- `EfConstruction` — construction search depth (default 200). Higher → better graph quality, slower builds. Drop to 50–100 for fast batch insertion.
- `Ef` — search depth (default 50–200). Higher → better recall, slower search.
- `Seed` — fix for reproducible builds.

### Tips

- Use `AddNodesAsync(...)` / `RemoveNodesAsync(...)` for batches — they acquire the write lock once.
- Prefer `PostgresqlStorageProvider` for server and Docker persistence, `SqliteStorageProvider` for local embedded persistence, and `RamStorageProvider` for ephemeral in-memory indexes.
- For high-dimensional embeddings use `CosineDistance`.

## Simple example (embedded)

```csharp
using Hnsw;
using Hnsw.RamStorage;
using HnswIndex.PostgresqlStorage;
using HnswIndex.SqliteStorage;

// RAM
await using RamStorageProvider ram = new RamStorageProvider();
HnswIndex index = new HnswIndex(128, ram);

// PostgreSQL (server/Docker default)
string connectionString = "Host=localhost;Port=5432;Database=hnswlite;Username=hnswlite;Password=hnswlite";
await using PostgresqlStorageProvider postgres = await PostgresqlStorageProvider.CreateAsync(
    connectionString,
    "my-index",
    dimension: 128);
HnswIndex persistentIndex = new HnswIndex(128, postgres);

// Or SQLite (local embedded persistence)
await using SqliteStorageProvider sqlite = await SqliteStorageProvider.CreateAsync("my-index.db");
HnswIndex sqliteIndex = new HnswIndex(128, sqlite);

// Configure
index.M = 16;
index.EfConstruction = 200;
index.DistanceFunction = new CosineDistance();

// Add a single vector
Guid id = Guid.NewGuid();
List<float> vector = new List<float>(128); // your 128-d embedding
await index.AddAsync(id, vector);

// Add a batch
Dictionary<Guid, List<float>> batch = new Dictionary<Guid, List<float>>();
for (int i = 0; i < 1000; i++) batch[Guid.NewGuid()] = GenerateRandomVector(128);
await index.AddNodesAsync(batch);

// Search
List<float> query = new List<float>(128);
IEnumerable<VectorResult> neighbors = await index.GetTopKAsync(query, count: 10);
foreach (VectorResult r in neighbors)
    Console.WriteLine($"id={r.GUID} distance={r.Distance:F4}");

// Export / import state
HnswState state = await index.ExportStateAsync();
await using RamStorageProvider restoredStorage = new RamStorageProvider();
HnswIndex restored = new HnswIndex(128, restoredStorage);
await restored.ImportStateAsync(state);
```

### Best practices

1. **Resource management.** `IStorageProvider` is `IAsyncDisposable` - use `await using` to guarantee flush on scope exit.
2. **Prefer batches.** Calling `AddNodesAsync` is substantially faster than a loop of `AddAsync` because it acquires the write lock once.
3. **Tune `Ef` at search time.**
   ```csharp
   IEnumerable<VectorResult> quick  = await index.GetTopKAsync(query, 10, ef: 50);   // fast, lower recall
   IEnumerable<VectorResult> better = await index.GetTopKAsync(query, 10, ef: 400);  // slower, higher recall
   ```

### Custom storage backend

Implement `IStorageProvider` (which aggregates `IHnswStorage`, `IHnswLayerStorage`, transactional hooks, flush, and `IAsyncDisposable`). See `RamStorageProvider`, `SqliteStorageProvider`, and `PostgresqlStorageProvider` as reference implementations. The server and dashboard are provider-agnostic.

## REST server

```bash
cd src/HnswIndex.Server
dotnet run -- --setup      # writes hnswindex.json with a generated admin API key
dotnet run
```

The server listens on `http://localhost:8080` by default. Authentication uses the `x-api-key` header (configurable via `Server.AdminApiKeyHeader`). OPTIONS pre-flight is unauthenticated and served by Watson's preflight hook; CORS headers are emitted on every response and configured under the `Cors` block in `hnswindex.json`.

Full endpoint reference: [REST_API.md](REST_API.md). Interactive reference: [HNSW Index.postman_collection.json](HNSW%20Index.postman_collection.json).

## Observability

Subscribe to the library's meter and activity source from any OpenTelemetry host. The names are constants in `HnswTelemetryNames`:

```csharp
RadiantSettings settings = new RadiantSettings("my-service");
settings.Sources.AddMeter(HnswTelemetryNames.MeterName);                  // "HnswLite"
settings.Sources.AddActivitySource(HnswTelemetryNames.ActivitySourceName); // "HnswLite"
using RadiantHost host = RadiantHost.Start(settings);
```

The REST server does this for you and adds Watson, server, Npgsql, and runtime telemetry. Under Docker, Grafana runs at `http://localhost:3000` (`admin` / `admin`), Prometheus at `http://localhost:9090`, and Tempo at `http://localhost:3200`. [TELEMETRY.md](TELEMETRY.md) has the full metrics and spans catalogs, configuration keys, dashboard map, and recommended alerts.

## Test runners

The shared Touchstone tests can be run through `Test.Automated`, xUnit, NUnit, or MSTest. `Test.Automated` accepts storage overrides directly:

```bash
dotnet run --project src/Test.Automated/Test.Automated.csproj -- --storage sqlite --filename test.db
dotnet run --project src/Test.Automated/Test.Automated.csproj -- --storage postgresql --host localhost --user hnsw --pass password --schema public --databasename hnswtest
```

The adapter projects use the same shared configuration through environment variables before `dotnet test`: `HNSWLITE_TEST_STORAGE`, `HNSWLITE_TEST_SQLITE_FILENAME`, `HNSWLITE_TEST_POSTGRES_CONNECTION`, or PostgreSQL components `HNSWLITE_TEST_POSTGRES_HOST`, `HNSWLITE_TEST_POSTGRES_PORT`, `HNSWLITE_TEST_POSTGRES_USER`, `HNSWLITE_TEST_POSTGRES_PASSWORD`, `HNSWLITE_TEST_POSTGRES_DATABASE`, and `HNSWLITE_TEST_POSTGRES_SCHEMA`.

## Dashboard

React 19 + Vite 8 + TypeScript dashboard at `dashboard/`. Pages include **Indices**, **Vectors** (browse / edit / add / delete), **Search**, **Request History** with an activity chart, **API Explorer**, **Server Info**, **Settings**, plus a login flow. The dashboard toolchain requires Node 20.19+ or Node 22.12+.

```bash
# Local development
cd dashboard
npm install
HNSWLITE_SERVER_URL=http://localhost:8080 npm run dev

# Production build (static assets in dashboard/dist)
npm run build
```

## SDKs

| Language | Directory | Package | Runtime |
|---|---|---|---|
| C# | `sdk/csharp/` | `HnswLite.Sdk` | .NET 8 or .NET 10 |
| Python | `sdk/python/` | `hnswlite-sdk` | Python 3.9+ |
| JavaScript / TypeScript | `sdk/js/` | `hnswlite-sdk` | Node 18+ (native `fetch`) |

Each SDK has 100% endpoint coverage and a test harness. See [sdk/README.md](sdk/README.md) for the method matrix and per-language READMEs.

## Docker

```bash
cd docker
docker compose up -d --build
```

- Server:     `http://localhost:8080/`
- Dashboard:  `http://localhost:8081/dashboard/`
- Grafana:    `http://localhost:3000/` (`admin` / `admin`; set `GRAFANA_ADMIN_PASSWORD` outside local development)
- Prometheus: `http://localhost:9090/`
- Tempo:      `http://localhost:3200/` (OTLP on 4317/4318)
- Storage:    PostgreSQL by default, provisioned by the Compose stack

Build and push both release images with one tag:

```cmd
build-all.bat v2.0.0
```

or, on macOS/Linux, `./build-all.sh v2.0.0`.

Factory reset (with `RESET` confirmation):

```bash
cd docker/factory
./reset.sh      # or reset.bat on Windows
```

See [docker/README.md](docker/README.md) for image tags and environment overrides.

## Bugs, feedback, or enhancement requests

- **Bug reports**: please [file an issue](https://github.com/jchristn/HnswLite/issues) with reproduction steps.
- **Feature requests**: open a [discussion](https://github.com/jchristn/HnswLite/discussions) or create an issue.
- **Questions**: use the discussions forum.
- **Contributions**: pull requests welcome.

## License

MIT. See [LICENSE.md](LICENSE.md).

## Acknowledgments

Based on [*Efficient and robust approximate nearest neighbor search using Hierarchical Navigable Small World graphs*](https://arxiv.org/abs/1603.09320) by Yu. A. Malkov and D. A. Yashunin.
