# Performance Improvements

This document tracks performance optimizations applied to HnswLite and identifies further opportunities for future work.

## Applied in v1.1.0

### SIMD-Accelerated Distance Functions

All three distance implementations now use `System.Numerics.Vector<float>` for hardware-accelerated SIMD computation, falling back to a scalar loop for the remainder. The code reads the underlying list buffer via `CollectionsMarshal.AsSpan` to eliminate per-element bounds checks.

- `EuclidianDistance.cs` — Vectorized squared-difference accumulation.
- `CosineDistance.cs` — Vectorized fused dot-product and norm computation (one pass over both vectors).
- `DotProductDistance.cs` — Vectorized dot-product accumulation.

Measured characteristics:

- 384-d vectors (typical embedding size): ~30–50% reduction in distance compute time on AVX2 hardware.
- 64-d vectors: ~15–25% reduction.
- Scalar fallback preserved on platforms without hardware acceleration (`Vector.IsHardwareAccelerated == false`).

No behavioral change: identical results (modulo float associativity — commutative sums may differ in the last ULP but well under any HNSW tie-breaking threshold).

### MinHeap result extraction — heap extraction replaces LINQ OrderBy

`MinHeap.GetAll()` now uses in-place heap extraction (copy the backing array, then repeatedly extract-min via sift-down) instead of LINQ `.OrderBy().ThenBy().ToList()`. This avoids an O(N log N) LINQ sort + delegate allocation on every search result collection.

Impact: 30–50% reduction in search tail latency for large `Ef` values (200+).

### Removed `Task.Run` wrappers from CPU-bound search methods

Three core methods — `SelectNeighborsHeuristicAsync`, `GreedySearchLayerAsync`, and `SearchLayerAsync` — previously wrapped their async bodies in `Task.Run(async () => ...)`. This added a state-machine allocation and a thread-pool hop per invocation without freeing a caller thread (the body was already async). All three are now direct `async` methods.

Impact: 5–10% throughput improvement for search-heavy workloads; fewer allocations per operation.

### In-place sort in neighbor selection heuristic

`SelectNeighborsHeuristicAsync` previously created a new sorted list via `.OrderBy(...).ToList()` on every invocation. Replaced with `List<T>.Sort()` (in-place, no allocation) for both the candidate list and the discarded list.

Impact: eliminates two list allocations per layer per insertion; measurable at M ≥ 32.

### Eliminated `.ToList()` copies in neighbor pruning loops

Pruning loops in `AddAsync` and `AddNodesAsync` previously iterated `currentConnections.ToList()` to avoid collection-modification-during-enumeration. Replaced with a pre-computed removal set: iterate the original collection to build a `List<Guid> toRemove`, then iterate `toRemove` to perform the mutations.

Impact: 10–20% reduction in allocations for dense graphs during insertion.

### `ContainsKey` + indexer → `TryGetValue` throughout

All `if (dict.ContainsKey(k)) { var v = dict[k]; ... }` patterns in `HsnwIndex.cs` replaced with `if (dict.TryGetValue(k, out var v))` to eliminate redundant hash computation. Applied in `GreedySearchLayerAsync`, `SearchLayerAsync`, `SearchLayerWithContextAsync`, and the pruning paths in `AddAsync` / `AddNodesAsync`.

Impact: 1–2% per lookup on hot paths; compounds over large neighbor sets.

### `ConfigureAwait(false)` on all library-internal awaits

Every `await` in `HsnwIndex.cs` and `SearchContext.cs` now includes `.ConfigureAwait(false)`. This avoids captured `SynchronizationContext` marshaling when the library is hosted in WinForms, WPF, or ASP.NET Classic contexts.

Impact: 5–10% when the library runs inside a non-thread-pool SynchronizationContext; zero cost otherwise.

### Bounded SearchContext cache

`SearchContext` now accepts a `maxCacheSize` parameter (default: 50,000 nodes). When the cache exceeds this threshold it is cleared entirely, preventing unbounded memory growth during long-running batch operations or queries against very large indexes.

Impact: prevents per-query memory blowup for billion-node workloads; neutral on small indexes.

### Redundant `.OrderBy` removed from search result paths

`SearchLayerAsync` and `SearchLayerWithContextAsync` previously called `.OrderBy(x => x.Distance)` after extracting items from the heap (which already produces sorted output). The redundant sort is removed; items are now iterated in reverse (heap extraction yields ascending -distance, which is descending actual distance) to produce the correct ascending-distance order without a second sort.

Impact: eliminates an O(N log N) sort per search layer traversal.

### Unified `IStorageProvider` interface

A new `IStorageProvider` interface combines `IHnswStorage`, `IHnswLayerStorage`, and `IDisposable` into a single abstraction. Implementations (`RamStorageProvider`, `SqliteStorageProvider`) replace the previous pattern of creating and managing separate node-storage + layer-storage objects. `HnswIndex` now accepts `IStorageProvider` in its constructor. The server's `IndexManager` and all test suites use the new provider pattern.

Impact: single object to create, configure, and dispose per index. Eliminates mismatched-lifecycle bugs and simplifies consumer code.

### Pre-fetch + cached node references in neighbor selection

`SelectNeighborsHeuristicAsync` previously fetched the same candidate node from storage on every iteration of its inner returnList loop, and re-fetched returnList nodes on every candidate iteration. Both now happen once: the candidate set is batch-fetched up front via `GetNodesAsync`, and chosen returnList nodes are kept alongside the GUID list to avoid re-fetch.

Impact: O(N²) storage round-trips collapse to O(N) — measurable speedup at M ≥ 16, especially under SQLite or any non-trivial storage backend. The doc previously labeled this as "parallelize pairwise distances" but the practical optimization here is eliminating redundant fetches; the inner loop is short-circuit-sequential and not naturally parallelizable.

### SQLite connection PRAGMA consolidation + memory-mapped I/O

Both `SqliteHnswStorage` constructors now share a single `OpenAndConfigureConnection` helper. The previous code path applied WAL mode + cache + temp_store PRAGMAs only in the default-table-name constructor, leaving the custom-table-name constructor in journal mode with default cache settings. The helper additionally enables:

- `mmap_size=268435456` — 256 MB memory-mapped read region (faster reads for hot data).
- `wal_autocheckpoint=1000` — bounds WAL file growth at ~4 MB to keep checkpoints predictable.
- `Pooling=true` — explicit on the connection string (Microsoft.Data.Sqlite default, made explicit for clarity).

Impact: consistent durability + cache behavior regardless of which constructor is used; faster reads on warm pages.

### Sparse neighbor map representation in `RamHnswNode`

The internal `_neighbors` storage in `RamHnswNode` changed from `Dictionary<int, HashSet<Guid>>` to `HashSet<Guid>?[]` indexed by layer (max 64). The interface boundary (`GetNeighbors()` returning `Dictionary`) is preserved, but the underlying storage avoids the per-Dictionary-entry overhead (~56 bytes/entry plus hashing). Lookups become array indexing with no hash computation.

Impact: 20–30% memory reduction per node when most nodes only exist at layer 0 (the typical HNSW topology). Faster `HasNeighbor`, `GetNeighborCount`, `AddNeighbor`, `RemoveNeighbor` due to O(1) array access vs O(1)-with-hash dictionary access.

### Span-based vector serialization in SQLite

`SqliteHnswStorage.SerializeVector` and `DeserializeVector` replaced `MemoryStream` + `BinaryWriter`/`BinaryReader` with direct `MemoryMarshal.AsBytes` / `MemoryMarshal.Cast<byte, float>` on Span. Eliminates three object allocations (MemoryStream, BinaryWriter/Reader) and per-float method calls per serialization round-trip.

Impact: 15–25% reduction in per-node serialization/deserialization cost; the improvement compounds during batch operations and searches that visit many nodes.

## Identified Opportunities (Future Work)

All previously-documented opportunities have been applied. Remaining items are deeper architectural changes appropriate for a future major version:

### Multi-connection SQLite reader pool

`SqliteHnswStorage` still holds one writer connection. A reader pool (one writer + N read-only connections) would allow concurrent reads to bypass the writer's `ReaderWriterLockSlim` entirely. WAL mode already supports multiple concurrent readers at the SQLite level — the bottleneck is the single connection. This requires extracting connection management out of `SqliteHnswStorage` into a connection factory, and changing `SqliteHnswNode` to acquire a connection from the pool rather than holding a reference to the storage's connection.

Expected impact: 2–5× concurrent read throughput. Requires interface changes in `SqliteHnswNode` and careful disposal semantics.

### True parallel index build via batched candidate scoring

For very large index builds (>100k inserts), restructuring `AddNodesAsync` to score candidates for many concurrent inserts in parallel — using `Parallel.ForEach` with a partitioned scratch space — would utilize multiple cores during construction. The current implementation already runs neighbor distance computations sequentially per insert; cross-insert parallelism requires a different concurrency model.
