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
| `src/HnswIndex.Server/` | Standalone REST server (Watson 7) |
| `src/Test.Shared/` + `src/Test.{Automated,XUnit,NUnit,MSTest}/` | Touchstone-driven test suites |
| `dashboard/` | React 19 + Vite dashboard |
| `sdk/csharp/`, `sdk/python/`, `sdk/js/` | Client SDKs with 100% endpoint coverage |
| `docker/` | `compose.yaml` for server + dashboard, plus factory-reset scripts |

### Key features

- **Pure C# implementation** — no native dependencies.
- **Thread-safe**, async/await with cancellation tokens throughout.
- **Unified `IStorageProvider` interface** — build-your-own backend by implementing one interface.
- **Multiple distance metrics** — Euclidean, Cosine, Dot Product, with SIMD acceleration via `System.Numerics.Vector<float>`.
- **Batch operations** — efficient bulk insert and remove.
- **Persistence by default** — the SQLite storage provider writes a self-describing `.db` file; the REST server reloads every index on startup.
- **Paginated enumeration contract** across every GET collection endpoint (`EnumerationQuery` / `EnumerationResult<T>`).
- **OPTIONS preflight + CORS** out of the box in the REST server.

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

- **`IStorageProvider`** — a single interface that combines `IHnswStorage`, `IHnswLayerStorage`, and `IDisposable`. `HnswIndex` accepts it via a new constructor.
- **`RamStorageProvider`** and **`SqliteStorageProvider`** consolidate the previous pair-of-objects setup into one lifecycle-managed instance.

### Server persistence

- **Default `StorageType` is now `SQLite`**. The old default silently created RAM-only indexes that vanished on restart.
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

- Unified Touchstone test suite: tests are defined once in `Test.Shared` and executed by **four** runners (`Test.Automated` console, `Test.XUnit`, `Test.NUnit`, `Test.MSTest`). Coverage grew from 23 to **53 cases** across 11 suites including concurrency, cross-storage parity, and cluster-recall scenarios.

### Dashboard

- React 19 + Vite 6 + TypeScript dashboard at `dashboard/` with pages for **Indices**, **Vectors** (browse / edit / add / delete with an index dropdown and Add-vector modal), **Search**, **Request History** (30-day browser-local capture with hour / day / week / month ranges), **API Explorer**, **Server Info**, **Settings**, plus a login flow.
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
- Prefer `SqliteStorageProvider` for persistence; `RamStorageProvider` for ephemeral in-memory indexes.
- For high-dimensional embeddings use `CosineDistance`.

## Simple example (embedded)

```csharp
using Hnsw;
using Hnsw.RamStorage;
using HnswIndex.SqliteStorage;

// RAM
using RamStorageProvider ram = new RamStorageProvider();
HnswIndex index = new HnswIndex(128, ram);

// Or SQLite (persistent)
using SqliteStorageProvider sqlite = new SqliteStorageProvider("my-index.db");
HnswIndex persistentIndex = new HnswIndex(128, sqlite);

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
HnswIndex restored = new HnswIndex(128, new RamStorageProvider());
await restored.ImportStateAsync(state);
```

### Best practices

1. **Resource management.** `IStorageProvider` is `IDisposable` — use `using` to guarantee flush on scope exit (important for SQLite).
2. **Prefer batches.** Calling `AddNodesAsync` is substantially faster than a loop of `AddAsync` because it acquires the write lock once.
3. **Tune `Ef` at search time.**
   ```csharp
   IEnumerable<VectorResult> quick  = await index.GetTopKAsync(query, 10, ef: 50);   // fast, lower recall
   IEnumerable<VectorResult> better = await index.GetTopKAsync(query, 10, ef: 400);  // slower, higher recall
   ```

### Custom storage backend

Implement `IStorageProvider` (which aggregates `IHnswStorage`, `IHnswLayerStorage`, and `IDisposable`). See `RamStorageProvider` and `SqliteStorageProvider` as reference implementations. The server and dashboard are completely provider-agnostic.

## REST server

```bash
cd src/HnswIndex.Server
dotnet run -- --setup      # writes hnswindex.json with a generated admin API key
dotnet run
```

The server listens on `http://localhost:8080` by default. Authentication uses the `x-api-key` header (configurable via `Server.AdminApiKeyHeader`). OPTIONS pre-flight is unauthenticated and served by Watson's preflight hook; CORS headers are emitted on every response and configured under the `Cors` block in `hnswindex.json`.

Full endpoint reference: [REST_API.md](REST_API.md). Interactive reference: [HNSW Index.postman_collection.json](HNSW%20Index.postman_collection.json).

## Dashboard

React 19 + Vite 6 + TypeScript dashboard at `dashboard/`. Pages include **Indices**, **Vectors** (browse / edit / add / delete), **Search**, **Request History** with an activity chart, **API Explorer**, **Server Info**, **Settings**, plus a login flow.

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
docker compose up -d
```

- Server:    `http://localhost:8080/`
- Dashboard: `http://localhost:8081/dashboard/`

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
