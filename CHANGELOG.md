# Change Log

## v2.2.0

### Observability (metrics and traces)

- **Library telemetry (`HnswLite`, `HnswLite.SqliteStorage`, `HnswLite.PostgresqlStorage`).** New public `HnswTelemetry` and `HnswTelemetryNames` emit on the `HnswLite` meter and activity source using only the .NET base class library: no new package dependencies, near-zero cost with no listener, and instrumentation never throws.
  - Every `HnswIndex` operation (`add`, `add_batch`, `remove`, `remove_batch`, `search`, `export`, `import`) records a duration histogram and an outcome counter with `error.type`, opens an `hnsw.<operation>` span, and records each stage as a `stage:<name>` child span plus a per-stage histogram. Write operations include a `queued` stage for time spent waiting on the per-index write lock, and `hnswlite.index.lock.waiting` / `hnswlite.index.lock.held` show contention.
  - Search records results returned, nodes evaluated, and `SearchContext` cache hits and misses. `SearchContext` gains `CacheHits`, `CacheMisses`, and `NodesEvaluated`.
  - SQLite and PostgreSQL providers record per-operation storage latency and outcomes (`hnswlite.storage.*`), PostgreSQL transaction outcomes, and `<provider> <operation>` client spans for coarse operations (open, schema, batch writes, transactions, flush).
- **Server telemetry (`HnswIndex.Server`).** One Radiant 0.1.2 host at the composition root subscribes to `Watson`, `HnswLite`, `HnswLite.Server`, and `Npgsql`, exports traces over OTLP, and serves a Prometheus endpoint (port 9464). The new `HnswLite.Server` meter and source cover every service operation and its stages, search results and filtering, API key decisions, API error codes, the startup index-reload job (job and stage spans, counters, last-success gauge), index/vector inventory gauges, and build/config info. Watson's built-in HTTP telemetry is enabled explicitly and not duplicated. A telemetry start failure logs a warning with the root cause and the server keeps running.
- **Configuration.** New `Telemetry` block in `hnswindex.json`: `Enable`, `ServiceName`, `OtlpEnable`, `OtlpEndpoint`, `OtlpProtocol`, `PrometheusEnable`, `PrometheusHostname`, `PrometheusPort`, `TraceSamplingRatio`, `IncludeRuntimeMetrics`, `TraceDatabaseCommands`. Loopback defaults use `127.0.0.1`.
- **Docker stack.** `docker/compose.yaml` adds Prometheus `v3.5.4`, Tempo `2.6.1`, and Grafana OSS `13.0.2` with healthchecks and health-gated startup. Grafana is provisioned as code: datasources with stable UIDs (`prometheus`, `tempo`) and six dashboards in the `HnswLite` folder (Overview, HTTP, Index Operations, Search, Storage & Integrations, Runtime & Lifecycle) from `assets/grafana/`. The Grafana admin password is overridable with `GRAFANA_ADMIN_PASSWORD`. The server healthcheck now probes `127.0.0.1` with `retries: 2`. Factory reset also removes the observability volumes, and `docker/update.bat` / `update.sh` pull and recreate the stack.
- **Image builds.** Added `src/.dockerignore` and `dashboard/.dockerignore` (build outputs and local data were being sent as build context, which grew past 600 MB and broke cloud builds) and `build-all.sh` / `build-server.sh` / `build-dashboard.sh` equivalents of the `.bat` build scripts.
- **Dashboard.** The home page has an External services card with Grafana, Prometheus, and Tempo URLs, default credentials, copy buttons, and a reachability check.
- **Documentation.** New [TELEMETRY.md](TELEMETRY.md) with the metrics and spans catalogs, configuration, subscription examples, dashboard map, recommended PromQL alerts, and troubleshooting.

### Tests

- New `TelemetrySuites` (12 cases) prove emission with in-memory BCL listeners across library operations and stages, storage providers (including PostgreSQL), service operations, the reload job, gauges, auth and API errors, the Radiant host (including a live Prometheus scrape), failure paths, and the no-listener path.
- `run-tests.sh` now passes `--framework net8.0` to the console runner (it previously failed on the multi-targeted project) and is executable.

### Package versions

- `HnswLite` **2.1.0**, `HnswLite.SqliteStorage` **2.2.0**, `HnswLite.PostgresqlStorage` **2.2.0**: additive public API (telemetry types and `SearchContext` counters) and instrumentation.
- `HnswLite.RamStorage` (2.0.1) and `HnswLite.Sdk` (2.1.0) are unchanged.

---

## v2.1.0

### Dependency updates

- **Microsoft.Data.Sqlite** `9.0.7` → `10.0.11` (`HnswLite.SqliteStorage`). Pulls the current patched SQLitePCLRaw native bundle, clearing advisory **GHSA-2m69-gcr7-jv3q** (high severity) that affected the previous transitive `SQLitePCLRaw.lib.e_sqlite3 2.1.10` — the Release build now restores with zero `NU1903` warnings.
- **Npgsql** `9.0.3` → `10.0.3` (`HnswLite.PostgresqlStorage`).
- **System.Text.Json** `9.0.4` → `10.0.11` (`HnswLite.Sdk`).
- Server and test tooling: SyslogLogging `2.0.8` → `2.2.1`, Watson `7.0.11` → `7.1.0`, Microsoft.NET.Test.Sdk `17.14.1` → `18.9.0`, xunit.runner.visualstudio `3.1.4` → `3.1.5`, NUnit `4.3.2` → `4.6.1`, NUnit3TestAdapter `5.0.0` → `6.2.0`, MSTest.TestAdapter/TestFramework `4.0.2` → `4.3.3`.

No public API changed; the dependency updates required no source changes.

### Package versions

- `HnswLite.SqliteStorage`, `HnswLite.PostgresqlStorage`, and `HnswLite.Sdk` move to **2.1.0** — their published NuGet dependency requirements changed to new major versions of Microsoft.Data.Sqlite / Npgsql / System.Text.Json, a consumer-visible surface change even though no HnswLite type or signature changed.
- `HnswLite` (core) and `HnswLite.RamStorage` remain **2.0.1** — no dependency or API surface change.

### Tests

- Added distance-function coverage: Cosine/DotProduct dimension-mismatch and null-argument negative cases, Cosine zero-magnitude-vector behavior (returns distance `1`, no divide-by-zero), and a SIMD-vs-scalar parity check over 67-d vectors that guards the `System.Numerics` accelerated paths across the runtime update.
- Full shared suite (97 cases) passes on RAM and SQLite via the console runner and via the xUnit, NUnit, and MSTest adapters, on both `net8.0` and `net10.0`.

---

## v2.0.0

### Breaking async storage API

- `IHnswStorage`, `IHnswLayerStorage`, `IHnswNode`, and `IStorageProvider` are now async-only.
- `IHnswNode` metadata is read-only through properties and updated with `SetMetadataAsync(...)`.
- `IStorageProvider` now includes transaction/flush hooks and implements `IAsyncDisposable`.
- Backward compatibility with the previous sync storage interface was intentionally removed.

### PostgreSQL backend and server default

- Added `HnswLite.PostgresqlStorage`, backed by Npgsql and PostgreSQL tables for indexes, nodes, node layers, neighbors, per-index metadata, and system metadata.
- New server-created indexes default to `PostgreSQL`; callers can still explicitly request `SQLite` or `RAM`.
- Server startup reloads PostgreSQL-backed index metadata from the database.

### Docker deployment

- Docker Compose now starts PostgreSQL 16, waits for health, runs a one-shot provisioner, and then starts the server/dashboard.
- Docker server configuration points to PostgreSQL by default.
- Factory reset scripts now remove PostgreSQL data, SQLite fallback data, and logs while preserving configuration.

### Tests, SDKs, and docs

- Shared .NET tests were migrated for async storage APIs and include opt-in PostgreSQL integration suites behind `HNSWLITE_POSTGRES_TEST_CONNECTION`.
- C#, Python, and JS/TS SDK examples and harnesses now default to PostgreSQL/Docker settings.
- README, REST API docs, Docker docs, API test docs, and Postman examples were updated for V2 defaults.
- NuGet publish tooling packs all five packages and pushes `.snupkg` symbol packages exactly once.

---

## v1.2.0

### Metadata filters on search and enumeration

- **`POST /v1.0/indexes/{name}/search`** and **`GET /v1.0/indexes/{name}/vectors`** both accept new optional `Labels`, `Tags`, and `CaseInsensitive` parameters.
  - **AND semantics on both** — a record is kept only when every label in the filter is present on its `Labels`, AND every key in the `Tags` filter exists on the record's `Tags` with an equal stringified value.
  - **`CaseInsensitive`** (default `false`) toggles `StringComparison.OrdinalIgnoreCase` for labels, tag keys, and tag values.
  - Tag values on the stored side are stringified via `Convert.ToString(value, InvariantCulture)` before comparison, so `42L` equals `"42"` and `true` equals `"True"`.
  - Query-string form: `?labels=red,small&tags=env:prod,owner:alice&caseInsensitive=true`.
- **`FilteredCount` on responses** — both `SearchResponse` and `EnumerationResult<T>` now report how many candidates/records were dropped by the metadata filter. Zero when no filter was supplied. Search can return fewer than `K` results when the filter is restrictive; `FilteredCount` makes this visible instead of silent.
- **Shared `MetadataFilter` helper** (`HnswIndex.Server.Services.MetadataFilter`) — single predicate used by both endpoints; handles null filters as no-ops, case folding, and invariant-culture stringification.
- **Enumeration pagination correctness** — the metadata filter is applied before `Skip`/`MaxResults` so `TotalRecords` stays accurate.
- **23 new test cases** across four suites in `Test.Shared/MetadataFilterSuites.cs` (helper unit tests, search end-to-end, enumerate end-to-end, query-string parsing). Total: 81 passing cases.

### SDKs

- **C# SDK** — `SearchRequest` / `EnumerationQuery` gained `Labels` / `Tags` / `CaseInsensitive`; `SearchResponse` / `EnumerationResult` gained `FilteredCount`; `VectorSearchResult` now surfaces `Name` / `Labels` / `Tags` (previously dropped by the SDK model); `AddVectorRequest` gained the metadata triple so callers can set what they will later filter on. The enumeration query-string builder handles the new parameters with URL encoding.
- **Python SDK** — `SearchRequest`, new `EnumerationQuery` dataclass, `SearchResponse`, `EnumerationResult`, `VectorSearchResult`, `VectorEntry`, and `AddVectorRequest` all updated. `search()` / `enumerate_vectors()` / `add_vector()` client methods gained the matching keyword arguments.
- **JS/TS SDK** — mirror type additions; **bug fix:** `keysToPascal` / `keysToCamel` previously recursed into every object blindly, which would have mangled user-supplied tag keys (`{env: 'prod'}` → `{Env: 'prod'}`). An `OPAQUE_KEYS` set now leaves `tags` values verbatim.

### Dashboard

- **Search page** — collapsible "Metadata filters" section with Labels (comma-separated), Tags (JSON object), and Case-insensitive toggle. `FilteredCount` appears in the results header.
- **Vectors page** — same three filter inputs alongside the existing GUID-prefix control. `FilteredCount` appears above the pagination footer when non-zero.
- Type/client plumbing updated to match the server contract.

### Documentation

- `README.md` — new "New in v1.2.0" section and a "Filtering by labels and tags" subsection with cURL / JSON examples and the v1.2 limitations.
- `METADATA_FILTERS.md` — implementation plan kept in sync with execution.

---

## v1.1.2 (previous)

### Performance — `ConfigureAwait(false)` audit

- Completed the `ConfigureAwait(false)` sweep claimed for v1.1.0. All 66 library-internal awaits in `HsnwIndex.cs` now include `.ConfigureAwait(false)`; previously only 18 did, leaving most of `AddAsync`, `AddNodesAsync`, `RemoveAsync`, `RemoveNodesAsync`, `GetTopKAsync`, `ExportStateAsync`, `ImportStateAsync`, and the context-aware search paths still marshalling back to a captured `SynchronizationContext`.
- Effect: eliminates the hidden post-`await` context hop for consumers hosting the library inside WinForms, WPF, or ASP.NET Classic sync contexts. Zero cost on the thread-pool default context.

---

## v1.1.1

### Vector metadata

- **`IHnswNode`** now exposes `Name` (string), `Labels` (List&lt;string&gt;), and `Tags` (Dictionary&lt;string, object&gt;) as mutable properties. All three are optional and default to null.
- **`RamHnswNode`** stores metadata in-memory (auto-properties).
- **`SqliteHnswNode`** persists metadata as a JSON blob in a `metadata_json` column on the nodes table. **Writes are immediate** — every property setter executes an `UPDATE` so metadata is durable even on an unclean crash. Existing databases are migrated via `ALTER TABLE ADD COLUMN` on first open.
- **Server request/response models** (`AddVectorRequest`, `VectorEntryResponse`, `VectorSearchResult`) carry all three metadata fields. The `EnumerateVectors` endpoint always populates metadata; `Search` results include metadata alongside distance.
- **Dashboard** — the Vectors table shows Name and Labels columns; the Add-vector modal, the Edit-vector modal, and the Search results detail modal all display and (where applicable) edit Name, Labels, and Tags.
- **58 test cases** (up from 53) — five new metadata-specific tests cover RAM read-back, SQLite persistence across close/reopen, null defaults, overwrite semantics, and batch-add metadata survival.

### Graceful shutdown under Docker

- The server now handles `AppDomain.CurrentDomain.ProcessExit` (SIGTERM) in addition to `Console.CancelKeyPress` (SIGINT). Previously `docker compose down` sent SIGTERM which the server didn't catch, so dirty in-memory state was never flushed. With immediate-write metadata this is less critical, but the fix ensures the full disposal chain (Watson stop → IndexManager dispose → storage flush) runs on any graceful termination signal.

### NuGet packaging

- `HnswIndex.SqliteStorage` renamed to **`HnswLite.SqliteStorage`** (new `<PackageId>`) for consistency with `HnswLite`, `HnswLite.RamStorage`, and `HnswLite.Sdk`.
- `HnswLite.Sdk` now packs `README.md` and `LICENSE.md` (previously triggered a "Readme missing" warning on NuGet push).
- `PackageTags` in all library csprojs switched to semicolon-delimited.
- New `publish-nuget.bat` at the repo root: takes a NuGet API key, cleans stale packages, packs all four projects in Release, pushes `.nupkg` files (symbols auto-upload alongside).

### Other fixes

- Dockerfile build stage bumped from `sdk:8.0` → `sdk:10.0` (required for multi-target restore). Publish pinned to `-f net8.0`; final stage switched from `sdk:8.0` → `aspnet:8.0` (smaller runtime image).
- `docker/compose.yaml` server `start_period` reduced from 30 s → 5 s.
- Dashboard nginx `302 /dashboard/` redirect now uses `$scheme://$http_host` so the port (e.g. `:8081`) is preserved.
- Server `clean.bat` / `clean.sh` added to the build output for quick local reset (deletes `hnswindex.json`, `data/`, `logs/`; no confirmation prompt).

---

## v1.1.0

### Platform

- Multi-target `net8.0` and `net10.0` across the library, server, and all test projects.
- Upgraded Watson web server to `7.0.11`:
  - OPTIONS pre-flight served by Watson's native `Routes.Preflight` hook (bypasses authentication).
  - CORS response headers emitted from a new `Cors` block in `hnswindex.json` (allow-origin / methods / headers / expose / max-age / credentials) — applied on every response.
  - Authentication enforced via `Routes.AuthenticateRequest` hook between `PreAuthentication` (health only) and `PostAuthentication` (all v1.0 routes).

### Storage abstraction

- New **`IStorageProvider`** interface combining `IHnswStorage` + `IHnswLayerStorage` + `IDisposable`. `HnswIndex` accepts it via a new constructor; the original separate-interfaces constructor is preserved for backward compatibility.
- New `RamStorageProvider` (wraps `RamHnswStorage` + `RamHnswLayerStorage`).
- New `SqliteStorageProvider` (wraps `SqliteHnswStorage` + `SqliteHnswLayerStorage` over a shared connection).
- Server `IndexManager` and all test suites updated to use the providers.

### Server persistence

- **Default `StorageType` changed from `"RAM"` to `"SQLite"`.** Previously the server silently created RAM-only indexes that vanished on restart.
- Server-owned index metadata (GUID, dimension, storage type, distance function, M, MaxM, EfConstruction, created timestamp) is now persisted **inside each SQLite `.db` file** using the library's existing `hnsw_metadata` key/value table under a `server.*` key prefix. No manifest file — **the database IS the manifest**.
- `IndexManager` scans the SQLite directory on startup, opens each `.db`, reads the server metadata, rebuilds the `HnswIndex`, and re-registers the entry. Indexes now survive restarts.

### Enumeration contract

Every GET that returns a collection now follows a single contract: query-string parameters populate an `EnumerationQuery`, the response body is an `EnumerationResult<T>`. **There are no "get all" endpoints anymore.**

- `EnumerationQuery`: `maxResults` (1–1000, default 100), `skip`, `continuationToken`, `ordering` (`CreatedAscending` / `CreatedDescending` / `NameAscending` / `NameDescending`), `prefix`, `suffix`, `createdAfterUtc`, `createdBeforeUtc`.
- `EnumerationResult<T>`: `Success`, `MaxResults`, `Skip`, `ContinuationToken`, `EndOfResults`, `TotalRecords`, `RecordsRemaining`, `TimestampUtc`, `Objects[]`.

### New REST endpoints

- `GET /v1.0/indexes` — paginated index enumeration (previously returned the entire list).
- `GET /v1.0/indexes/{name}/vectors` — paginated vector enumeration. Supports every `EnumerationQuery` parameter plus `includeVectors=true|false` (default `false`) to choose whether vector payloads are inlined.
- `GET /v1.0/indexes/{name}/vectors/{guid}` — fetch a single vector by GUID, always including the `Vector` array. Returns `404 VectorNotFound` when absent.

### Performance

Applied in this release:

- **SIMD-accelerated distance functions** (`Euclidean`, `Cosine`, `DotProduct`) using `System.Numerics.Vector<float>` + `CollectionsMarshal.AsSpan`. Scalar fallback on non-accelerated platforms.
- **`MinHeap.GetAll()`** switched from LINQ `.OrderBy().ThenBy()` to in-place heap extraction.
- **Removed `Task.Run` wrappers** from `SelectNeighborsHeuristicAsync`, `GreedySearchLayerAsync`, and `SearchLayerAsync` — they wrapped async CPU work needlessly.
- **In-place sort** in neighbor selection (`List<T>.Sort()` replaces `.OrderBy().ToList()`).
- **Pre-fetch + cached node references** in `SelectNeighborsHeuristicAsync` — O(N²) storage round-trips collapsed to O(N).
- **Eliminated `.ToList()` copies** in pruning loops (use pre-computed removal sets).
- **`ContainsKey` + indexer → `TryGetValue`** across hot paths.
- **`ConfigureAwait(false)`** on every library-internal await.
- **Bounded `SearchContext` cache** (default 50,000 nodes; clears when exceeded) to prevent unbounded memory growth for very large indexes.
- **Span-based SQLite vector serialization** — `MemoryMarshal.AsBytes` / `MemoryMarshal.Cast<byte, float>` replace `BinaryWriter`/`BinaryReader`.
- **Sparse neighbor map** in `RamHnswNode` — `HashSet<Guid>?[]` indexed by layer (max 64) replaces `Dictionary<int, HashSet<Guid>>`. ~20–30% memory reduction per node + O(1) array-indexed lookups.
- **SQLite connection consolidation** — both constructors now share a single `OpenAndConfigureConnection` helper. Fixed a bug where the custom-table-name constructor did not apply WAL / synchronous / cache PRAGMAs. Added `mmap_size=256MB` for memory-mapped reads and `wal_autocheckpoint=1000` to bound WAL growth.

See [`archive/PERFORMANCE_IMPROVEMENTS.md`](archive/PERFORMANCE_IMPROVEMENTS.md) for details and remaining future work (multi-connection SQLite reader pool, cross-insert parallel index build).

### Testing

- Existing `Test.Ram` / `Test.Sqlite` console apps replaced by a unified **Touchstone** test suite. All tests are defined once in `Test.Shared` and executed by four runners: **Test.Automated** (console), **Test.XUnit**, **Test.NUnit**, **Test.MSTest**.
- Coverage grew from 23 to **53 test cases** across 11 suites: distance-function correctness, basic/advanced RAM, validation, state round-trip, SQLite basic/advanced/persistence/state, edge cases, large-dataset cluster recall, parameter sensitivity (M, Ef, seed determinism, `ExtendCandidates`), batch operations, concurrency (100 parallel reads, interleaved add/search), RAM↔SQLite parity, high-dimensional (384-d, 768-d), and cross-storage state migration.
- `run-tests.bat` / `run-tests.sh` at the repo root drive all four runners.

### Dashboard

New React 19 + Vite 6 + TypeScript dashboard at `dashboard/`. Pages:

- **Dashboard** — overview tiles + request activity chart (hour / day / week / month ranges).
- **Indices** — create / list / edit / delete. Paginated via `EnumerationQuery`; prefix filter + ordering selector.
- **Vectors** — browse / edit / delete vectors, with an index dropdown and an Add-vector modal (Single / Batch tabs).
- **Search** — top-level page with index dropdown; supports `?index=<name>` query param for deep-linking.
- **Request History** — 30-day browser-local capture of every dashboard API call, with filters and drill-down modal.
- **API Explorer** — per-endpoint request builder with status / duration / headers / body tabs.
- **Server Info**, **Settings**, plus a login flow with an API-key-only authentication form.

Also:

- Sidebar / top bar: logo + GitHub / theme-toggle / sign-out icon buttons in the upper right, version pinned to the bottom of the nav panel.
- Three-dot **ActionMenu** on every entity table row, portal-rendered with auto flip-above-trigger near the viewport bottom.
- Clipboard copy control works in both secure and insecure browser contexts (falls back to `document.execCommand('copy')`).
- Client-side request-history retention: 30 days, bounded to 5000 entries, hourly purge while the dashboard is open.

### SDKs

Three new SDKs at `sdk/csharp/`, `sdk/python/`, `sdk/js/` with 100% endpoint coverage. Each ships with a test harness that exercises every method against a live server and a README with method-by-method examples.

- **C#** (`HnswLite.Sdk` — `net8.0`/`net10.0`): `HnswLiteClient` with 12 async methods (`PingAsync`, `HeadPingAsync`, `EnumerateIndexesAsync`, `CreateIndexAsync`, `GetIndexAsync`, `DeleteIndexAsync`, `SearchAsync`, `EnumerateVectorsAsync`, `GetVectorAsync`, `AddVectorAsync`, `AddVectorsAsync`, `RemoveVectorAsync`) + typed models + `HnswLiteApiException`.
- **Python** (`hnswlite-sdk` — Python 3.9+, `requests`): mirror method set in snake_case.
- **JavaScript/TypeScript** (`hnswlite-sdk` — Node 18+, zero runtime deps, native `fetch`): mirror method set in camelCase with full type definitions and internal PascalCase↔camelCase conversion.

### Docker

- `docker/` at the repo root with `compose.yaml` running both `hnswlite-server:v1.1.0` and `hnswlite-dashboard:v1.1.0` containers. The dashboard's nginx proxies `/v1.0/` to the server and serves the favicon at the origin root.
- `build-server.bat` and `build-dashboard.bat` at the repo root.
- New `docker/factory/reset.bat` + `reset.sh` that return the deployment to factory state (type `RESET` to confirm; preserves `hnswindex.json`, deletes all `.db` files and logs).

### Documentation

- New `REST_API.md` at the repo root — full reference for the enumeration contract and every endpoint.
- Updated `API-TESTING.md` with the `EnumerationQuery` parameter table.
- Updated the Postman collection with the new vector routes and enumeration query-string parameters.

### Bug fixes

- **Dashboard key camelization** — was producing `gUID` (lower-casing only the first character). Now correctly handles SCREAMING_ACRONYMS: `GUID → guid`, `URL → url`, `URLPath → urlPath`; `MaxM → maxM` and `EfConstruction → efConstruction` are preserved. Affected every cell that read `.guid` on any API response.
- **Dashboard reachability** — `Server Info > Reachability` previously always reported "Unreachable" because it sent the root `GET /` through the JSON-parsing path and the HTML response failed `JSON.parse`. Now uses `HEAD /` directly.
- **Dashboard layout** — the sidebar / TOC previously expanded with workspace content. Outer layout now locks to `100vh`; the workspace owns the scroll and the sidebar scrolls independently.

---

## v1.0.x

- SQLite backend with binary vector serialization (4× faster than JSON).
- Deferred flush for batch operations (100× improvement for large insertions).
- SearchContext caching — reduces database round-trips by 90%+.
- WAL mode and optimized PRAGMA settings for SQLite.
- Standalone REST server (`HnswIndex.Server`) with Docker image and Postman collection.
- Core HNSW algorithm implementation.
- In-memory storage backend.
- SQLite storage backend.
- Async APIs with cancellation support.
- Three distance functions (Euclidean, Cosine, Dot Product).
- Batch add/remove operations.
- State export/import functionality.
- Thread-safety.
- Initial release.
