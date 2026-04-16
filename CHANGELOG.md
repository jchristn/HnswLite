# Change Log

## v1.1.0 (current)

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

See [`PERFORMANCE_IMPROVEMENTS.md`](PERFORMANCE_IMPROVEMENTS.md) for details and remaining future work (multi-connection SQLite reader pool, cross-insert parallel index build).

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
