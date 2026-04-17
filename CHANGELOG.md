# Change Log

## v1.1.2 (current)

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
