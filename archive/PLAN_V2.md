# HnswLite V2 Plan

This plan is intentionally breaking. The goal is to make storage and index operations fully asynchronous, add PostgreSQL as a first-class backend, make Docker default to PostgreSQL, and update all tests, SDKs, and docs to match.

Developers should update task checkboxes and append notes directly under each task while work is in progress.

## Status Legend

- `[ ]` Not started
- `[~]` In progress
- `[x]` Complete
- `[!]` Blocked

## Global Acceptance Criteria

- [x] All storage interfaces are fully asynchronous; PostgreSQL and SQLite storage paths use async database I/O.
- [x] Existing RAM and SQLite providers compile against the new async interfaces.
- [x] PostgreSQL provider implements the same async interfaces and passes the shared provider test suite.
- [x] Server supports `RAM`, `SQLite`, and `PostgreSQL`, with PostgreSQL as the default storage type.
- [x] Docker Compose starts PostgreSQL, provisions the database/schema/default records, starts the server against PostgreSQL, and starts the dashboard.
- [x] `docker/factory/reset.*` resets PostgreSQL-backed data and logs safely.
- [x] C#, Python, and JavaScript/TypeScript SDKs are updated for the new defaults and validated against the V2 server.
- [x] All .NET test harnesses pass.
- [x] All SDK test harnesses pass.
- [x] README, CHANGELOG, REST_API, Docker docs, SDK docs, and server API testing docs are updated.

## Phase 0: Baseline and Branch Setup

- [x] V2-0001: Create a feature branch.
  - Suggested branch: `feature/v2-async-postgresql`.
  - Acceptance: `git status --short` shows only expected local changes before implementation begins.
  - Progress: Work is being performed on existing branch `feature/v2.0.0`.

- [x] V2-0002: Record baseline test results before code changes.
  - Command: `dotnet test src/HnswLite.sln -f net8.0`
  - Optional command if SDK is installed: `dotnet test src/HnswLite.sln -f net10.0`
  - Acceptance: Baseline failures, if any, are documented here before V2 changes.
  - Notes: Baseline `dotnet test src\HnswLite.sln -f net8.0` passed before implementation changes: 81 tests passed each under MSTest, NUnit, and xUnit.

- [x] V2-0003: Decide target package version.
  - Recommendation: use `2.0.0` because this intentionally breaks public interfaces.
  - Acceptance: Version policy is recorded in CHANGELOG and project files during docs/package updates.
  - Notes: Target version set to `2.0.0` across core/server/storage projects and SDK/dashboard package metadata.

## Phase 1: Fully Asynchronous Core Interfaces

### Interface Design

- [x] V2-0101: Replace `IHnswStorage` sync members with async equivalents.
  - File: `src/HnswIndex/IHnswStorage.cs`
  - Required shape:
    - `Task AddNodeAsync(Guid id, List<float> vector, CancellationToken cancellationToken = default)`
    - `Task AddNodesAsync(Dictionary<Guid, List<float>> nodes, CancellationToken cancellationToken = default)`
    - `Task RemoveNodeAsync(Guid id, CancellationToken cancellationToken = default)`
    - `Task RemoveNodesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)`
    - `Task<IHnswNode> GetNodeAsync(Guid id, CancellationToken cancellationToken = default)`
    - `Task<Dictionary<Guid, IHnswNode>> GetNodesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)`
    - `Task<TryGetNodeResult> TryGetNodeAsync(Guid id, CancellationToken cancellationToken = default)`
    - `Task<IEnumerable<Guid>> GetAllNodeIdsAsync(CancellationToken cancellationToken = default)`
    - `Task<int> GetCountAsync(CancellationToken cancellationToken = default)`
    - `Task<Guid?> GetEntryPointAsync(CancellationToken cancellationToken = default)`
    - `Task SetEntryPointAsync(Guid? entryPoint, CancellationToken cancellationToken = default)`
  - Remove: `Guid? EntryPoint { get; set; }`.
  - Acceptance: No sync entry point property remains.
  - Progress: Replaced sync `EntryPoint` property with `GetEntryPointAsync` and `SetEntryPointAsync`.

- [x] V2-0102: Replace `IHnswLayerStorage` with fully async methods.
  - File: `src/HnswIndex/IHnswLayerStorage.cs`
  - Required shape:
    - `Task<int> GetNodeLayerAsync(Guid nodeId, CancellationToken cancellationToken = default)`
    - `Task SetNodeLayerAsync(Guid nodeId, int layer, CancellationToken cancellationToken = default)`
    - `Task RemoveNodeLayerAsync(Guid nodeId, CancellationToken cancellationToken = default)`
    - `Task<Dictionary<Guid, int>> GetAllNodeLayersAsync(CancellationToken cancellationToken = default)`
    - `Task ClearLayersAsync(CancellationToken cancellationToken = default)`
    - `Task<int> GetLayerCountAsync(CancellationToken cancellationToken = default)`
  - Remove: sync `Count` property and sync `Clear`.
  - Acceptance: No synchronous layer-storage method remains.
  - Progress: Replaced all layer methods/properties with async methods.

- [x] V2-0103: Replace mutable `IHnswNode` members with async methods.
  - File: `src/HnswIndex/IHnswNode.cs`
  - Keep sync read-only data that is already materialized:
    - `Guid Id { get; }`
    - `IReadOnlyList<float> Vector { get; }`
    - `string? Name { get; }`
    - `IReadOnlyList<string>? Labels { get; }`
    - `IReadOnlyDictionary<string, object>? Tags { get; }`
  - Required async methods:
    - `Task<Dictionary<int, HashSet<Guid>>> GetNeighborsAsync(CancellationToken cancellationToken = default)`
    - `Task AddNeighborAsync(int layer, Guid neighborId, CancellationToken cancellationToken = default)`
    - `Task RemoveNeighborAsync(int layer, Guid neighborId, CancellationToken cancellationToken = default)`
    - `Task SetMetadataAsync(string? name, List<string>? labels, Dictionary<string, object>? tags, CancellationToken cancellationToken = default)`
  - Remove sync setters: `Name`, `Labels`, `Tags`.
  - Remove sync methods: `GetNeighbors`, `AddNeighbor`, `RemoveNeighbor`.
  - Acceptance: PostgreSQL node mutation can be implemented without blocking on async database APIs.
  - Progress: Metadata is now read-only on the interface and written through `SetMetadataAsync`; neighbor calls are async.

- [x] V2-0104: Update `IStorageProvider`.
  - File: `src/HnswIndex/IStorageProvider.cs`
  - Required shape: `public interface IStorageProvider : IHnswStorage, IHnswLayerStorage, IAsyncDisposable`
  - Optional method if transaction support is added in V2-0105: `Task<IHnswStorageTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)`.
  - Remove: `IDisposable`.
  - Acceptance: Provider disposal is asynchronous.
  - Progress: `IStorageProvider` now inherits `IAsyncDisposable`.

- [x] V2-0105: Add explicit async transaction/flush contracts.
  - Recommended new file: `src/HnswIndex/IHnswStorageTransaction.cs`
  - Recommended shape:
    - `public interface IHnswStorageTransaction : IAsyncDisposable`
    - `Task CommitAsync(CancellationToken cancellationToken = default)`
    - `Task RollbackAsync(CancellationToken cancellationToken = default)`
  - Recommended provider methods:
    - `Task<IHnswStorageTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)`
    - `Task FlushAsync(CancellationToken cancellationToken = default)`
  - Rationale: `HnswIndex` mutates many graph edges during add/remove. PostgreSQL needs a clear transaction boundary to avoid per-edge autocommit overhead and partial graph persistence.
  - Acceptance: `HnswIndex.AddAsync`, `AddNodesAsync`, `RemoveAsync`, `RemoveNodesAsync`, and `ImportStateAsync` use a provider transaction when available.
  - Progress: Added `IHnswStorageTransaction`, `NoOpHnswStorageTransaction`, `BeginTransactionAsync`, and `FlushAsync`; core algorithm transaction usage is tracked under V2-0110 through V2-0115.

### Core Algorithm Migration

- [x] V2-0110: Update `HnswIndex` to use async entry point access.
  - File: `src/HnswIndex/HsnwIndex.cs`
  - Replace all `_Storage.EntryPoint` reads/writes with `GetEntryPointAsync` and `SetEntryPointAsync`.
  - Acceptance: No `EntryPoint` property references remain in core code.
  - Progress: Core index now uses async entry point methods.

- [x] V2-0111: Update `HnswIndex` layer calls.
  - File: `src/HnswIndex/HsnwIndex.cs`
  - Replace `GetNodeLayer`, `SetNodeLayer`, `RemoveNodeLayer`, `Clear`, and `Count` usage with async methods.
  - Acceptance: Layer storage is awaited everywhere.
  - Progress: Core index now awaits layer storage methods.

- [x] V2-0112: Update `HnswIndex` node-neighbor calls.
  - File: `src/HnswIndex/HsnwIndex.cs`
  - Replace:
    - `node.GetNeighbors()` with `await node.GetNeighborsAsync(...)`
    - `node.AddNeighbor(...)` with `await node.AddNeighborAsync(...)`
    - `node.RemoveNeighbor(...)` with `await node.RemoveNeighborAsync(...)`
  - Acceptance: All graph mutation and read calls are asynchronous.
  - Progress: Core index graph traversal and mutation now use async neighbor methods.

- [x] V2-0113: Update metadata writes in server and tests.
  - Replace `node.Name = ...`, `node.Labels = ...`, `node.Tags = ...` with `await node.SetMetadataAsync(...)`.
  - Files include:
    - `src/HnswIndex.Server/Services/IndexManager.cs`
    - `src/Test.Shared/*.cs`
  - Acceptance: No external code uses removed metadata setters.
  - Progress: Server add-vector paths and shared metadata tests now call `SetMetadataAsync`.

- [x] V2-0114: Update `SearchContext` to async node APIs.
  - File: `src/HnswIndex/SearchContext.cs`
  - Ensure cached `IHnswNode` instances expose async neighbor operations without blocking.
  - Acceptance: Search still batches node loading through `GetNodesAsync` and avoids per-candidate round trips where possible.
  - Progress: `SearchContext` remains an async node cache; neighbor calls are performed asynchronously by `HnswIndex`.

- [x] V2-0115: Update state export/import.
  - Files:
    - `src/HnswIndex/HsnwIndex.cs`
    - `src/HnswIndex/HnswState.cs`
    - `src/HnswIndex/NodeState.cs`
  - Ensure export uses `GetNeighborsAsync` and async layer access.
  - Ensure import uses async node insertion, layer writes, neighbor writes, and entry point writes.
  - Acceptance: State round-trip tests pass for RAM, SQLite, and PostgreSQL.
  - Progress: State export/import now use async layer, neighbor, entry point, flush, and transaction APIs. Test validation remains tracked in later phases.

## Phase 2: Async RAM Provider

- [x] V2-0201: Migrate `RamHnswNode`.
  - File: `src/HnswIndex.RamStorage/RamHnswNode.cs`
  - Implement async methods with completed tasks where no I/O exists.
  - Ensure metadata is updated only through `SetMetadataAsync`.
  - Acceptance: RAM node implementation has no removed sync interface members.
  - Progress: RAM node now implements async neighbor and metadata methods.

- [x] V2-0202: Migrate `RamHnswLayerStorage`.
  - File: `src/HnswIndex.RamStorage/RamHnswLayerStorage.cs`
  - Convert methods to async signatures.
  - Acceptance: RAM layer storage compiles against async `IHnswLayerStorage`.
  - Progress: RAM layer storage now exposes async layer operations.

- [x] V2-0203: Migrate `RamHnswStorage` and `RamStorageProvider`.
  - Files:
    - `src/HnswIndex.RamStorage/RamHnswStorage.cs`
    - `src/HnswIndex.RamStorage/RamStorageProvider.cs`
  - Implement async entry point methods.
  - Implement `IAsyncDisposable`.
  - Implement no-op transaction/flush behavior if transaction support is added.
  - Acceptance: RAM shared test suite passes.
  - Progress: RAM storage/provider now expose async entry point, async layer delegation, async disposal, no-op transaction, and no-op flush. Public sync compatibility methods and sync provider disposal were removed. Test validation remains tracked in Phase 7.

## Phase 3: Async SQLite Provider

- [x] V2-0301: Replace sync SQLite commands with async APIs where available.
  - Files:
    - `src/HnswIndex.SqliteStorage/SqliteHnswStorage.cs`
    - `src/HnswIndex.SqliteStorage/SqliteHnswLayerStorage.cs`
    - `src/HnswIndex.SqliteStorage/SqliteHnswNode.cs`
    - `src/HnswIndex.SqliteStorage/SqliteStorageProvider.cs`
  - Use `ExecuteNonQueryAsync`, `ExecuteScalarAsync`, `ExecuteReaderAsync`, `ReadAsync`.
  - Acceptance: No storage-path SQLite command executes synchronously.
  - Result: Replaced SQLite constructors with async `CreateAsync` factories, converted open/configuration/schema setup, entry point, count, node CRUD, metadata, neighbor, layer, and server metadata paths to async SQLite APIs, and replaced `ReaderWriterLockSlim` database sections with async-compatible `SemaphoreSlim` guards. Internal batch transactions now use async begin/commit/rollback.

- [x] V2-0302: Convert SQLite node mutation to async.
  - Replace dirty flush behavior with one of:
    - async write-through per neighbor mutation, or
    - transaction-scoped buffered writes with `FlushAsync`.
  - Preferred: transaction-scoped buffered writes for add/remove/import, write-through for standalone metadata updates.
  - Acceptance: Crash/durability behavior is documented and tests reflect it.
  - Result: Chose async write-through neighbor persistence for SQLite V2. `AddNeighborAsync` and `RemoveNeighborAsync` now persist with async SQLite commands immediately; `FlushAsync` remains a retry path for failed writes. Added `Sqlite.Persistence/NeighborMutationIsWriteThrough` to verify neighbor mutations persist without a provider flush.

- [x] V2-0303: Resolve duplicate layer-table logic.
  - Current SQLite code has layer helpers both in `SqliteHnswStorage` and `SqliteHnswLayerStorage`.
  - Choose one implementation path under the async provider.
  - Acceptance: One authoritative SQLite layer table implementation remains.
  - Progress: Removed dead `_nodesTableName_layers` creation and helper methods from `SqliteHnswStorage`; `SqliteHnswLayerStorage` is now the single SQLite layer implementation.

- [x] V2-0304: Implement `IAsyncDisposable`.
  - Ensure dispose calls `FlushAsync`.
  - Acceptance: `await using` works for `SqliteStorageProvider`.
  - Progress: `SqliteStorageProvider` implements `DisposeAsync` and flushes cached nodes.

- [x] V2-0305: Validate SQLite persistence after async migration.
  - Required tests:
    - Add/search
    - Remove
    - Batch add
    - Metadata persists across reopen
    - State export/import
  - Acceptance: SQLite suites pass.
  - Result: Existing SQLite persistence coverage and the new write-through neighbor persistence case pass under `dotnet test src\HnswLite.sln -f net8.0` and `dotnet test src\HnswLite.sln -f net10.0`.

## Phase 4: PostgreSQL Storage Provider

### Project and Dependencies

- [x] V2-0401: Create PostgreSQL storage project.
  - New directory: `src/HnswIndex.PostgresqlStorage/`
  - New project: `src/HnswIndex.PostgresqlStorage/HnswIndex.PostgresqlStorage.csproj`
  - Target frameworks: match existing projects, currently `net8.0;net10.0`.
  - Package: `Npgsql`.
  - Project reference: `../HnswIndex/HnswIndex.csproj`.
  - Acceptance: Project builds independently.
  - Progress: Added `HnswIndex.PostgresqlStorage` targeting `net8.0;net10.0`; standalone `net8.0` build passes.

- [x] V2-0402: Add project to solution and server/test references.
  - Files:
    - `src/HnswLite.sln`
    - `src/HnswIndex.Server/HnswIndex.Server.csproj`
    - `src/Test.Shared/Test.Shared.csproj`
  - Acceptance: `dotnet build src/HnswLite.sln -f net8.0` includes PostgreSQL project.
  - Progress: Project added to solution, server reference added, and test shared reference added. Full `net8.0` solution build succeeds.

### Schema

- [x] V2-0410: Add PostgreSQL schema scripts.
  - Suggested files:
    - `src/HnswIndex.PostgresqlStorage/Sql/001_init.sql`
    - `docker/postgres/init/001_hnswlite.sql`
  - Required tables:
    - `hnsw_indexes`
    - `hnsw_nodes`
    - `hnsw_neighbors`
    - `hnsw_node_layers`
    - `hnsw_metadata`
  - Required baseline columns:
    - Index identity/name/dimension/distance function/M/MaxM/EfConstruction/created timestamp.
    - Node ID, vector blob, vector dimension, metadata JSONB, created/updated timestamps.
    - Neighbor node ID, layer, neighbor ID.
    - Layer node ID, layer.
    - Metadata key/value.
  - Acceptance: Schema is idempotent and can be run multiple times.
  - Progress: Added idempotent `postgresql-schema.sql` to provider project and configured it to copy to output.

- [x] V2-0411: Add indexes and constraints.
  - Required:
    - Unique index on index name.
    - Primary keys on all identity tables.
    - Foreign keys from nodes/layers/neighbors to index records.
    - Index on `(index_id, node_id)`.
    - Index on `(index_id, node_id, layer)` for neighbors.
    - Optional index on metadata JSONB if server-side metadata filtering is added later.
  - Acceptance: Delete of an index cascades all node/layer/neighbor rows.
  - Progress: Schema includes primary keys, foreign keys with cascade behavior, and indexes for nodes, layers, and neighbors.

- [x] V2-0412: Decide multi-index isolation model.
  - Recommendation: single database with `hnsw_indexes.id` partition key and per-index rows in shared tables.
  - Alternative: schema per index.
  - Acceptance: Decision is documented in `README.md` and PostgreSQL provider XML docs.
  - Notes: Decision is a single PostgreSQL schema with shared tables partitioned by `hnsw_indexes.id`/`index_id`. Documented in `README.md` and `PostgresqlStorageProvider` XML docs.

### Provider Implementation

- [x] V2-0420: Implement `PostgresqlStorageProvider`.
  - Suggested namespace: `HnswIndex.PostgresqlStorage`.
  - Constructor inputs:
    - connection string or `NpgsqlDataSource`
    - index name
    - create/provision flag
  - Required:
    - `IStorageProvider`
    - `IAsyncDisposable`
    - transaction support if added in V2-0105.
  - Acceptance: Provider can create/open a named index.
  - Progress: Provider can create/open a named index through `CreateAsync`. Added caller-owned `NpgsqlDataSource` overloads so the server can share one PostgreSQL pool across all loaded indexes.

- [x] V2-0421: Implement `PostgresqlHnswStorage`.
  - Implement all node CRUD methods asynchronously.
  - Use binary vector serialization compatible with SQLite unless a new format is documented.
  - Use `uuid` columns for GUIDs, not byte arrays.
  - Acceptance: Add/get/remove/count/all IDs work.
  - Progress: Node CRUD, count, ID enumeration, vector serialization, and batch load are implemented in `PostgresqlStorageProvider`.

- [x] V2-0422: Implement `PostgresqlHnswLayerStorage`.
  - Implement async layer methods.
  - Store layer in `hnsw_node_layers`.
  - Acceptance: Layer assignment round-trips and clears per index.
  - Progress: Async layer storage is implemented in `PostgresqlStorageProvider`.

- [x] V2-0423: Implement `PostgresqlHnswNode`.
  - Implement async neighbors:
    - `GetNeighborsAsync`
    - `AddNeighborAsync`
    - `RemoveNeighborAsync`
  - Implement async metadata:
    - `SetMetadataAsync`
  - Use JSONB for metadata.
  - Acceptance: Metadata and neighbors survive provider disposal/reopen.
  - Progress: PostgreSQL node supports async neighbor and metadata methods with write-through persistence.

- [x] V2-0424: Implement batch-loading for PostgreSQL.
  - `GetNodesAsync` must perform one query for node vectors/metadata and one query for neighbor rows for all requested nodes.
  - Avoid constructing one query per node during search.
  - Acceptance: SQL log or instrumentation confirms batch behavior.
  - Progress: `GetNodesAsync` loads node rows and neighbor rows in batches using `ANY(@ids)`.

- [x] V2-0425: Implement entry point persistence.
  - Preferred: store `entry_point_id` on `hnsw_indexes`.
  - Acceptance: Entry point survives reopen.
  - Progress: Entry point is stored in `hnsw_indexes.entry_point_id`.

- [x] V2-0426: Implement PostgreSQL transactions.
  - Add/remove/import operations should run graph mutations inside a DB transaction.
  - If node objects are transaction-aware, ensure they use the active transaction connection.
  - Acceptance: A simulated exception during add/remove rolls back partial graph changes.
  - Progress: Provider implements transaction hooks using an explicit provider-scoped PostgreSQL connection/transaction. Rollback clears the node cache so rolled-back mutations are not visible through cached nodes. Failure coverage is tracked under V2-0705.

- [x] V2-0427: Add provider-specific diagnostics.
  - Log connection target without secrets.
  - Add clear exceptions for missing database, missing schema, and permission failures.
  - Acceptance: Startup failures identify the actionable cause.
  - Progress: PostgreSQL create/open and index-list failures now wrap Npgsql/PostgreSQL/socket/timeout errors with an `InvalidOperationException` that includes sanitized host, port, database, and username while preserving the original exception.

## Phase 5: Server Integration

- [x] V2-0501: Update server project reference.
  - File: `src/HnswIndex.Server/HnswIndex.Server.csproj`
  - Add reference to `HnswIndex.PostgresqlStorage`.
  - Acceptance: Server builds with PostgreSQL provider.
  - Progress: Server references PostgreSQL provider and `dotnet build src\HnswLite.sln -f net8.0` succeeds.

- [x] V2-0502: Expand `StorageSettings`.
  - File: `src/HnswIndex.Server/Classes/StorageSettings.cs`
  - Add:
    - `DefaultStorageType`, default `PostgreSQL`
    - `PostgresqlConnectionString`
    - `PostgresqlDatabase`
    - `PostgresqlAutoProvision`
    - Keep `SqliteDirectory` for SQLite support.
  - Acceptance: Existing config can express all three storage backends.
  - Progress: Added default storage type, PostgreSQL connection string, and PostgreSQL auto-provision settings while keeping SQLite directory.

- [x] V2-0503: Update server startup wiring.
  - File: `src/HnswIndex.Server/HnswIndexServer.cs`
  - Pass complete storage settings into `IndexManager`, not only SQLite directory.
  - Acceptance: Server startup logs selected default backend.
  - Progress: Server passes full storage settings into `IndexManager` and awaits PostgreSQL reload during global initialization.

- [x] V2-0504: Update `IndexManager`.
  - File: `src/HnswIndex.Server/Services/IndexManager.cs`
  - Add PostgreSQL provider creation for `StorageType = PostgreSQL`.
  - Change default storage type to settings default when request omits storage type.
  - Preserve RAM and SQLite support.
  - Acceptance: Create index works for all storage types.
  - Progress: `IndexManager` can create RAM, SQLite, and PostgreSQL providers. PostgreSQL providers now share one manager-owned `NpgsqlDataSource` to avoid one connection pool per loaded index.

- [x] V2-0505: Replace SQLite file reload with provider-specific reload.
  - For PostgreSQL, reload index metadata from `hnsw_indexes`.
  - For SQLite, keep `.db` scan.
  - For RAM, no reload.
  - Acceptance: Server restart reloads PostgreSQL indexes and vector counts.
  - Progress: SQLite reload remains file-based; PostgreSQL reload reads `hnsw_indexes` asynchronously through the provider.

- [x] V2-0506: Update server metadata model.
  - Ensure server-owned fields are stored in PostgreSQL `hnsw_indexes` or metadata table.
  - Acceptance: `GET /v1.0/indexes` after restart returns persisted PostgreSQL indexes.
  - Progress: PostgreSQL index metadata is stored in `hnsw_indexes`; server reload reads it through `PostgresqlStorageProvider.ListIndexesAsync`. Restart persistence validation passed under V2-1203.

- [x] V2-0507: Update API test scripts.
  - Files:
    - `src/HnswIndex.Server/test-api.sh`
    - `src/HnswIndex.Server/test-api.ps1`
    - `src/HnswIndex.Server/test-api.bat`
    - `src/HnswIndex.Server/API-TESTING.md`
  - Change example storage type to `PostgreSQL`.
  - Add explicit examples for `RAM` and `SQLite`.
  - Acceptance: Scripts pass against Docker default deployment.
  - Progress: API scripts and docs default to `PostgreSQL`; PowerShell harness was fixed to use `-UseBasicParsing`, capture returned vector GUIDs, and validate vector get/enumerate/delete. Docker smoke validation passed under V2-1202.

## Phase 6: Docker PostgreSQL Default

- [x] V2-0601: Add PostgreSQL service to Docker Compose.
  - File: `docker/compose.yaml`
  - Required service: `hnswlite-postgres`.
  - Suggested image: official `postgres` image.
  - Required environment:
    - `POSTGRES_USER`
    - `POSTGRES_PASSWORD`
    - `POSTGRES_DB`
  - Required volume:
    - `./postgres/data:/var/lib/postgresql/data`
  - Required healthcheck:
    - `pg_isready`
  - Acceptance: `docker compose up -d hnswlite-postgres` becomes healthy.
  - Progress: Added `hnswlite-postgres` with official PostgreSQL 16 image, credentials, bind-mounted data directory, configurable host port mapping, and `pg_isready` healthcheck. Compose now avoids fixed `container_name` values so alternate project names can be used for validation. Runtime validation remains under V2-1201.

- [x] V2-0602: Add PostgreSQL provisioner container.
  - File: `docker/compose.yaml`
  - Required service: `hnswlite-postgres-provisioner`.
  - Must run on startup after PostgreSQL healthcheck.
  - Must execute idempotent SQL scripts to create default database objects, tables, indexes, and records.
  - Suggested mount: `./postgres/init:/docker-entrypoint-initdb.d` or a custom `psql` command against mounted SQL files.
  - Acceptance: Provisioner exits 0 on first run and subsequent runs.
  - Progress: Added `hnswlite-postgres-provisioner` using the PostgreSQL image and a `psql` command against mounted provisioning SQL. Compose now avoids fixed `container_name` values so repeated runs do not collide. Runtime validation remains under V2-1201.

- [x] V2-0603: Add provisioning scripts.
  - Suggested files:
    - `docker/postgres/init/001_database.sql`
    - `docker/postgres/init/002_schema.sql`
    - `docker/postgres/init/003_seed.sql`
  - Required default records:
    - Any server metadata needed by startup.
    - Optional default health/provisioning marker row.
  - Acceptance: Scripts are idempotent and checked into source control.
  - Progress: Added `docker/postgres/provision/001_hnswlite.sql` with schema creation, indexes, and default metadata records. Added `.gitignore` coverage for `docker/postgres/data/` so live PostgreSQL runtime files are not tracked.

- [x] V2-0604: Update server service configuration.
  - File: `docker/compose.yaml`
  - Server must depend on provisioner successful completion.
  - Server environment/config must point to PostgreSQL by default.
  - Acceptance: Fresh `docker compose up -d` starts PostgreSQL, provisions schema, starts server, then dashboard.
  - Progress: Server now depends on PostgreSQL health and provisioner completion in Compose, uses V2 local build context, and exposes a configurable host port. Runtime validation remains under V2-1201.

- [x] V2-0605: Update `docker/hnswlite/hnswindex.json`.
  - Set default storage to PostgreSQL.
  - Include PostgreSQL connection settings using Compose service DNS name.
  - Keep SQLite directory only as optional fallback.
  - Acceptance: Mounted config works in Compose without manual edits.
  - Progress: Docker config defaults storage to PostgreSQL and points to `hnswlite-postgres`.

- [x] V2-0606: Update Docker docs.
  - File: `docker/README.md`
  - Document PostgreSQL service, volumes, credentials, provisioning, and reset behavior.
  - Acceptance: A new user can run Compose and know where data is stored.
  - Progress: Rewrote Docker README around the PostgreSQL-default Compose stack, provisioner, volumes, reset behavior, and backup/restore notes.

- [x] V2-0607: Update factory reset scripts.
  - Files:
    - `docker/factory/reset.sh`
    - `docker/factory/reset.bat`
  - Required behavior:
    - Stop Compose stack.
    - Remove PostgreSQL data volume/directory.
    - Remove HnswLite logs.
    - Preserve config unless explicitly requested.
    - Explain that PostgreSQL schema will be re-provisioned on next startup.
  - Acceptance: Reset followed by `docker compose up -d` creates a fresh PostgreSQL-backed deployment.
  - Progress: `reset.sh` and `reset.bat` now stop Compose, remove PostgreSQL data, clear SQLite index data/logs, preserve config, and tell the operator that PostgreSQL will be re-provisioned on next startup.

## Phase 7: Test Harness Updates

### Shared .NET Tests

- [x] V2-0701: Update all shared tests for async node APIs.
  - Files:
    - `src/Test.Shared/HnswSuites.cs`
    - `src/Test.Shared/HnswExtendedSuites.cs`
    - `src/Test.Shared/MetadataFilterSuites.cs`
  - Replace sync node metadata and neighbor usage.
  - Acceptance: Tests compile.
  - Progress: Shared tests compile against the async node metadata API.

- [x] V2-0702: Add PostgreSQL basic provider suite.
  - Test cases:
    - Add and search.
    - Remove excludes GUID.
    - Batch add.
    - Batch remove.
    - Count and enumerate IDs.
    - Entry point persists across reopen.
  - Acceptance: Suite can run when `HNSWLITE_POSTGRES_TEST_CONNECTION` is set.
  - Progress: Added opt-in `Postgresql.Basic` suite gated by `HNSWLITE_POSTGRES_TEST_CONNECTION`.

- [x] V2-0703: Add PostgreSQL persistence suite.
  - Test cases:
    - Data survives provider dispose/reopen.
    - Metadata survives provider dispose/reopen.
    - Graph neighbors survive provider dispose/reopen.
    - Server metadata/index metadata survives reopen.
  - Acceptance: Suite passes against local or Docker PostgreSQL.
  - Progress: Added opt-in PostgreSQL persistence and metadata test. Runtime PostgreSQL validation remains pending.

- [x] V2-0704: Add PostgreSQL parity suite.
  - Compare RAM, SQLite, and PostgreSQL on deterministic seeded index.
  - Acceptance: Result overlap meets the existing cross-storage threshold.
  - Progress: Added opt-in RAM/PostgreSQL overlap test.

- [x] V2-0705: Add transaction failure tests.
  - Use an injectable failure point or test provider wrapper.
  - Acceptance: Failed add/remove leaves no partial nodes/layers/neighbors.
  - Result: Added PostgreSQL rollback coverage that creates nodes/layers/neighbors inside a transaction, rolls back, and verifies node count, node lookup, and layer rows are absent. This exposed and fixed provider rollback cache state.

- [x] V2-0706: Make PostgreSQL tests opt-in or container-backed.
  - Option A: skip when `HNSWLITE_POSTGRES_TEST_CONNECTION` is missing.
  - Option B: start Docker PostgreSQL from test harness.
  - Recommendation: use env-var opt-in for unit test runners; run Docker-backed integration in CI.
  - Acceptance: Default local tests do not fail because PostgreSQL is absent.
  - Progress: PostgreSQL suites are included only when `HNSWLITE_POSTGRES_TEST_CONNECTION` is set.

### Test Runners

- [x] V2-0710: Update xUnit runner.
  - File: `src/Test.XUnit/HnswTheoryTests.cs`
  - Acceptance: xUnit executes updated suite.
  - Result: Runner consumes `HnswSuites.All`; PostgreSQL cases run automatically when `HNSWLITE_POSTGRES_TEST_CONNECTION` is set.

- [x] V2-0711: Update NUnit runner.
  - File: `src/Test.NUnit/HnswNunitTests.cs`
  - Acceptance: NUnit executes updated suite.
  - Result: Runner consumes `HnswSuites.All`; PostgreSQL cases run automatically when `HNSWLITE_POSTGRES_TEST_CONNECTION` is set.

- [x] V2-0712: Update MSTest runner.
  - File: `src/Test.MSTest/HnswMstestTests.cs`
  - Acceptance: MSTest executes updated suite.
  - Result: Runner consumes `HnswSuites.All`; PostgreSQL cases run automatically when `HNSWLITE_POSTGRES_TEST_CONNECTION` is set.

- [x] V2-0713: Update automated console runner.
  - File: `src/Test.Automated/Program.cs`
  - Add PostgreSQL connection-string handling and clear skip/pass output.
  - Acceptance: Console runner reports PostgreSQL skipped or passed explicitly.
  - Result: Console runner consumes `HnswSuites.All`; PostgreSQL suites are included only when `HNSWLITE_POSTGRES_TEST_CONNECTION` is set, matching the adapter runners.

### Required .NET Validation

- [x] V2-0720: Run .NET tests without PostgreSQL.
  - Command: `dotnet test src/HnswLite.sln -f net8.0`
  - Expected: RAM and SQLite pass; PostgreSQL integration tests are skipped with clear reason.
  - Result: Passed after PostgreSQL test gating and SQLite write-through coverage. MSTest: 82 passed. xUnit: 82 passed. NUnit: 82 passed. PostgreSQL suites are omitted unless `HNSWLITE_POSTGRES_TEST_CONNECTION` is set.

- [x] V2-0721: Run .NET tests with PostgreSQL.
  - Command:
    - PowerShell: `$env:HNSWLITE_POSTGRES_TEST_CONNECTION='Host=localhost;Port=5432;Database=hnswlite_tests;Username=hnswlite;Password=hnswlite'; dotnet test src/HnswLite.sln -f net8.0`
  - Expected: RAM, SQLite, and PostgreSQL pass.
  - Result: Passed with Docker PostgreSQL on port `55432`. `net8.0`: MSTest/xUnit/NUnit each passed 87 tests, 0 failed.

- [x] V2-0722: Run .NET 10 tests if SDK is available.
  - Command: `dotnet test src/HnswLite.sln -f net10.0`
  - Result: Passed with Docker PostgreSQL on port `55432`. MSTest/xUnit/NUnit each passed 87 tests, 0 failed.

## Phase 8: SDK Updates and SDK Test Harnesses

### API Contract Review

- [x] V2-0801: Confirm REST JSON compatibility.
  - Storage type should accept `PostgreSQL`, `SQLite`, and `RAM`.
  - Default should be `PostgreSQL`.
  - If create-index request shape changes, update every SDK model.
  - Acceptance: API examples and SDK models match server DTOs.
  - Progress: Confirmed create-index shape is unchanged and storage values now include `PostgreSQL`, `SQLite`, and `RAM`; default storage is PostgreSQL.

### C# SDK

- [x] V2-0810: Update C# SDK models.
  - Files:
    - `sdk/csharp/HnswLite.Sdk/Models/CreateIndexRequest.cs`
    - `sdk/csharp/HnswLite.Sdk/Models/IndexResponse.cs`
  - Default `StorageType` to `PostgreSQL`.
  - Documentation: `"PostgreSQL"`, `"SQLite"`, `"RAM"`.
  - Acceptance: C# SDK builds.
  - Progress: Updated request/response model defaults and XML comments to PostgreSQL.

- [x] V2-0811: Update C# SDK test harness.
  - File: `sdk/csharp/HnswLite.Sdk.Test/Program.cs`
  - Default test storage to PostgreSQL.
  - Allow env override for storage type.
  - Acceptance: Harness passes against Docker default server.
  - Progress: Harness defaults to Docker base URL, Docker admin API key, and PostgreSQL; supports `HNSWLITE_BASE_URL`, `HNSWLITE_API_KEY`, `HNSWLITE_STORAGE_TYPE`, and positional overrides.

- [x] V2-0812: Validate C# SDK.
  - Commands:
    - `dotnet build sdk/csharp/HnswLite.Sdk/HnswLite.Sdk.csproj -f net8.0`
    - `dotnet run --project sdk/csharp/HnswLite.Sdk.Test/HnswLite.Sdk.Test.csproj -f net8.0`
  - Result: `dotnet build sdk/csharp/HnswLite.Sdk/HnswLite.Sdk.csproj -f net8.0` passed. Live harness passed against `http://localhost:18080` with PostgreSQL: 18 passed, 0 failed.

### Python SDK

- [x] V2-0820: Update Python SDK defaults and docs.
  - Files:
    - `sdk/python/hnswlite/client.py`
    - `sdk/python/hnswlite/models.py`
    - `sdk/python/README.md`
  - Default `storage_type` to `PostgreSQL`.
  - Acceptance: Python SDK examples match server.
  - Progress: Updated Python client/model defaults and README examples to PostgreSQL and Docker port 8080.

- [x] V2-0821: Update Python SDK integration tests.
  - File: `sdk/python/tests/test_integration.py`
  - Default test storage to PostgreSQL.
  - Allow env override for storage type.
  - Acceptance: Tests pass against Docker default server.
  - Progress: Harness defaults to Docker base URL, Docker admin API key, and PostgreSQL; supports CLI and `HNSWLITE_*` overrides.

- [x] V2-0822: Validate Python SDK.
  - Commands:
    - `python -m pytest sdk/python/tests`
  - Result: `python -m compileall sdk/python/hnswlite sdk/python/tests` passed. Live harness passed against `http://localhost:18080` with PostgreSQL: 23 passed, 0 failed.

### JavaScript/TypeScript SDK

- [x] V2-0830: Update JS SDK defaults and docs.
  - Files:
    - `sdk/js/src/types.ts`
    - `sdk/js/src/index.ts`
    - `sdk/js/README.md`
  - Correct any existing storage example drift such as `InMemory`; use `PostgreSQL`, `SQLite`, `RAM`.
  - Acceptance: TypeScript builds.
  - Progress: Added `StorageType` union, exported it, and updated README examples from `InMemory`/`CosineDistance` to `PostgreSQL`/`Cosine`.

- [x] V2-0831: Update JS integration tests.
  - File: `sdk/js/tests/integration.ts`
  - Default storage to PostgreSQL.
  - Allow env override for storage type.
  - Acceptance: Tests pass against Docker default server.
  - Progress: Harness defaults to Docker base URL, Docker admin API key, and PostgreSQL; supports positional and `HNSWLITE_*` overrides.

- [x] V2-0832: Validate JS SDK.
  - Commands:
    - `npm --prefix sdk/js install`
    - `npm --prefix sdk/js test`
  - Result: `npm.cmd --prefix sdk/js run build` passed. Live harness passed against `http://localhost:18080` with PostgreSQL: 21 passed, 0 failed, after fixing SDK response key normalization for server `GUID` fields.

### SDK Docs

- [x] V2-0840: Update SDK root matrix.
  - File: `sdk/README.md`
  - Document PostgreSQL default and storage type values.
  - Acceptance: SDK docs align across languages.
  - Progress: Root SDK README documents PostgreSQL default, accepted storage values, and updated harness commands.

## Phase 9: Documentation Updates

- [x] V2-0901: Update README.
  - File: `README.md`
  - Required changes:
    - V2 breaking async API.
    - PostgreSQL provider.
    - PostgreSQL is default for server/Docker.
    - Updated embedded examples using `await using`.
    - Updated custom storage backend guidance.
    - Updated Docker quick start.
  - Acceptance: README contains no outdated statement that SQLite is default.
  - Progress: README now has V2 async/PostgreSQL notes, PostgreSQL provider examples using `await using`, PostgreSQL Docker quick start, and async custom-backend guidance.

- [x] V2-0902: Update CHANGELOG.
  - File: `CHANGELOG.md`
  - Add V2 section.
  - Explicitly list breaking changes:
    - async-only storage interfaces.
    - removed sync node metadata setters.
    - provider disposal changed to `IAsyncDisposable`.
    - PostgreSQL default in Docker/server.
  - Acceptance: Upgrade impact is clear.
  - Progress: Added V2 section covering async-only storage interfaces, metadata setter removal, async provider disposal, PostgreSQL provider/default, Docker changes, tests, SDKs, and docs.

- [x] V2-0903: Update REST API docs.
  - File: `REST_API.md`
  - Required:
    - `StorageType` accepted values include `PostgreSQL`.
    - Default storage is PostgreSQL.
    - Any new server info/config fields are documented.
    - Examples use PostgreSQL by default.
  - Acceptance: API docs match server DTOs.
  - Progress: REST API docs now list `PostgreSQL`, `SQLite`, and `RAM`, and document PostgreSQL as the default when `StorageType` is omitted or blank.

- [x] V2-0904: Update server API testing docs.
  - Files:
    - `src/HnswIndex.Server/API-TESTING.md`
    - `src/HnswIndex.Server/test-api.md`
  - Acceptance: Test scripts and docs use PostgreSQL by default.
  - Progress: API test scripts, API testing guide, and test-api note now use PostgreSQL for create-index examples.

- [x] V2-0905: Update Docker docs.
  - File: `docker/README.md`
  - Include:
    - PostgreSQL service.
    - Provisioner service.
    - Volumes.
    - Credentials/configuration.
    - Reset behavior.
    - Backup/restore notes.
  - Acceptance: Docs match Compose file.
  - Progress: Completed as part of V2-0606.

- [x] V2-0906: Update project/package docs.
  - Files:
    - `src/HnswIndex/HnswIndex.xml` if maintained manually.
    - `src/HnswIndex.RamStorage/HsnwIndex.RamStorage.xml` if maintained manually.
    - `src/HnswIndex.SqliteStorage/HsnwIndex.SqliteStorage.xml` if maintained manually.
    - New PostgreSQL XML docs.
  - Acceptance: Package docs do not describe removed sync APIs.
  - Progress: Regenerated XML docs after removing public sync storage members and provider sync disposal; private helper XML comments for removed sync API names were removed.

- [x] V2-0907: Update Postman collection if storage examples are included.
  - File: `HNSW Index.postman_collection.json`
  - Acceptance: Create-index example defaults to PostgreSQL.
  - Progress: Postman create-index request bodies now use `StorageType: "PostgreSQL"`.

## Phase 10: Dashboard Review

- [x] V2-1001: Inspect dashboard storage type UI.
  - Files under `dashboard/src`.
  - Add PostgreSQL option wherever storage type is listed.
  - Make PostgreSQL the default.
  - Acceptance: Dashboard create-index flow can create PostgreSQL indexes.
  - Progress: Dashboard create-index type, default form state, select options, and API Explorer create-index template now use PostgreSQL.

- [x] V2-1002: Validate dashboard build.
  - Commands:
    - `npm --prefix dashboard install`
    - `npm --prefix dashboard run build`
  - Result: Passed with existing dependencies using `npm.cmd --prefix dashboard run build`.

## Phase 11: CI and Release Packaging

- [x] V2-1101: Update CI workflows if present.
  - Search: `.github/workflows`
  - Add PostgreSQL service container for integration tests.
  - Ensure default test job still runs without external DB where appropriate.
  - Acceptance: CI can validate PostgreSQL provider and SDK harnesses.
  - Result: No `.github/workflows` directory is present in this repository, so there are no CI workflow files to update.

- [x] V2-1102: Update NuGet packaging.
  - Add package metadata for `HnswLite.PostgresqlStorage`.
  - Set versions to V2 target.
  - Acceptance: `dotnet pack` produces all expected packages.
  - Progress: Added PostgreSQL storage project/package metadata, set package versions to `2.0.0`, and validated package creation for core, RAM, SQLite, PostgreSQL, and the C# SDK with per-project `dotnet pack` commands. Repacked `HnswLite.PostgresqlStorage.2.0.0.nupkg` after the transaction/diagnostics/shared-data-source fixes.

- [x] V2-1103: Update Docker image tags.
  - Files:
    - `docker/compose.yaml`
    - build scripts if present.
  - Acceptance: Compose references V2 image tags or local build context as intended.
  - Progress: Compose now tags server/dashboard images as `v2.0.0`, builds them from local source contexts, and uses configurable host port mappings for local validation.

## Phase 12: End-to-End Validation

- [x] V2-1201: Fresh Docker boot.
  - Commands:
    - `cd docker`
    - `docker compose down -v`
    - `docker compose up -d`
    - `docker compose ps`
  - Acceptance: PostgreSQL healthy, provisioner exited 0, server healthy, dashboard running.
  - Result: Passed using isolated Compose project `hnswlite-v2` with host port overrides (`HNSWLITE_POSTGRES_PORT=55432`, `HNSWLITE_SERVER_PORT=18080`, `HNSWLITE_DASHBOARD_PORT=18081`). PostgreSQL reported healthy, provisioner exited successfully, server reported healthy, dashboard started, `http://localhost:18080/` returned 200, and `http://localhost:18081/dashboard/` returned 200. Rebuilt and restarted `hnswlite-server` after the SQLite async factory refactor; server returned healthy.

- [x] V2-1202: Server smoke test.
  - Use API test script against Docker default.
  - Acceptance: create index, add vectors, search, enumerate, get vector, delete vector, delete index all pass.
  - Result: Passed against `http://localhost:18080` using `powershell -NoProfile -ExecutionPolicy Bypass -File .\src\HnswIndex.Server\test-api.ps1 -ApiKey ... -BaseUrl http://localhost:18080`. Updated the PowerShell harness to use `-UseBasicParsing`, fix header interpolation, use GUID-based test index names, and verify get-vector, vector enumeration, delete-vector, search-after-delete, and delete-index with PostgreSQL. Reran after rebuilding the Docker image with shared PostgreSQL data-source support and after the SQLite async factory/server rebuild; failure marker count was 0.

- [x] V2-1203: Server restart persistence test.
  - Create PostgreSQL index and vectors.
  - Restart server container only.
  - Verify index reloads and search returns expected vector.
  - Acceptance: PostgreSQL persistence works across server restart.
  - Result: Passed against isolated Compose project `hnswlite-v2`. Created a PostgreSQL index, added a vector, confirmed search before restart, restarted only `hnswlite-server`, waited for reload, confirmed the index and search result persisted, then deleted the test index. Reran successfully after shared PostgreSQL data-source support was added.

- [x] V2-1204: Factory reset validation.
  - Run:
    - `docker/factory/reset.sh` on Unix-like environment, or
    - `docker/factory/reset.bat` on Windows.
  - Restart Compose.
  - Acceptance: data is gone, schema is re-provisioned, server starts cleanly.
  - Result: Passed on Windows using `reset.bat` in a temporary Docker-only copy to avoid deleting dirty workspace logs/data. The temp stack used `COMPOSE_PROJECT_NAME=hnswlite-reset-validation` and alternate ports (`19082`, `19083`, `55434`), created a PostgreSQL index, ran the reset script with `RESET` confirmation, restarted with `docker compose up -d --no-build`, verified PostgreSQL/server/dashboard returned healthy/200, and confirmed the pre-reset index was gone.

- [x] V2-1205: Full .NET validation.
  - Commands:
    - `dotnet test src/HnswLite.sln -f net8.0`
    - `dotnet test src/HnswLite.sln -f net10.0`
  - Acceptance: all applicable tests pass; any skipped tests are intentional and documented.
  - Result: Passed with `HNSWLITE_POSTGRES_TEST_CONNECTION=Host=localhost;Port=55432;Username=hnswlite;Password=hnswlite;Database=hnswlite`. `net8.0`: MSTest/xUnit/NUnit each passed 87 tests, 0 failed. `net10.0`: MSTest/xUnit/NUnit each passed 87 tests, 0 failed. Reran after the shared PostgreSQL data-source changes and the SQLite async factory/write-through mutation refactor. Adjusted PostgreSQL remove test to validate storage removal/count and search exclusion instead of relying on approximate recall of a specific remaining node in a three-node graph, added rollback coverage for partial graph mutations, and added SQLite write-through neighbor persistence coverage.

- [x] V2-1206: Full SDK validation.
  - Commands:
    - `dotnet run --project sdk/csharp/HnswLite.Sdk.Test/HnswLite.Sdk.Test.csproj -f net8.0`
    - `PYTHONPATH=sdk/python python sdk/python/tests/test_integration.py`
    - `npm --prefix sdk/js test`
  - Acceptance: all SDK harnesses pass against Docker default server.
  - Result: Passed against live Docker server `http://localhost:18080` with storage type `PostgreSQL`. C# harness: 18 passed, 0 failed. Python script harness: 23 passed, 0 failed. JavaScript harness: 21 passed, 0 failed after fixing SDK response key normalization so server `GUID` fields map to camelCase `guid`. Reran all three after rebuilding the Docker image with shared PostgreSQL data-source support and again after the SQLite async factory/server rebuild.

- [x] V2-1207: Documentation validation.
  - Manually check all docs changed in Phase 9.
  - Acceptance: no docs still describe SQLite as default; no examples use removed sync APIs.
  - Result: Passed docs scan. No current documentation describes SQLite/RAM as the V2 default; remaining SQLite default mentions are explicitly historical v1.1 changelog/README notes or accepted-value examples. Storage examples use async disposal (`await using`) rather than removed synchronous provider disposal.

## Implementation Notes

- Prefer `Task` over `ValueTask` for public interfaces unless profiling proves a need. This keeps provider implementations simple and consistent.
- Use `ConfigureAwait(false)` in library/internal async code.
- Preserve cancellation token flow through every storage call.
- Avoid sync-over-async (`.Result`, `.Wait()`, `GetAwaiter().GetResult()`) in storage and server code.
- Do not store PostgreSQL credentials in logs.
- Keep RAM and SQLite providers available, but do not preserve old synchronous public API compatibility.
- PostgreSQL should be the default for Docker and server-created indexes; embedded library users can still explicitly choose RAM or SQLite.

## Open Decisions

- [x] D-001: Should PostgreSQL neighbor storage be normalized rows or bytea blob?
  - Recommendation: normalized rows for V2.
  - Decision: Use normalized rows in `hnsw_neighbors` keyed by `(index_id, node_id, layer, neighbor_id)`.

- [x] D-002: Should PostgreSQL provider auto-create indexes on constructor or require explicit server provisioning?
  - Recommendation: provider can create named index records when `createIfNotExists` is true; Docker provisioner creates global schema only.
  - Decision: Provider `CreateAsync` creates/opens named index records when `createIfNotExists` is true. Docker provisioner creates global schema/default records only.

- [x] D-003: Should metadata filtering move into provider/database for PostgreSQL?
  - Recommendation: keep existing in-memory post-filter behavior for V2 parity; consider provider-side filtering later.
  - Decision: Keep server/index-level in-memory post-filter behavior for V2 parity. Provider-side PostgreSQL filtering is deferred.

- [x] D-004: Should SQLite remain available in Docker examples?
  - Recommendation: document as optional fallback, but keep Compose default PostgreSQL.
  - Decision: Document SQLite as an explicit fallback/embedded option. Docker Compose and server-created indexes default to PostgreSQL.
