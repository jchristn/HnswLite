namespace Hnsw
{
    /// <summary>
    /// Stable telemetry names emitted by the HnswLite libraries: the meter and activity source names, every
    /// instrument name, every label (tag) key, and the bounded set of label values.
    /// These strings are public contract consumed by collectors and Grafana dashboards. Do not rename them
    /// without a major version change.
    /// Thread safety: all members are constants and safe to read from any thread.
    /// </summary>
    public static class HnswTelemetryNames
    {
        #region Sources

        /// <summary>
        /// Name of the <see cref="System.Diagnostics.Metrics.Meter"/> used by the HnswLite libraries.
        /// Subscribe to it from a host (for example Radiant's settings.Sources.AddMeter) to collect metrics.
        /// </summary>
        public const string MeterName = "HnswLite";

        /// <summary>
        /// Name of the <see cref="System.Diagnostics.ActivitySource"/> used by the HnswLite libraries.
        /// Subscribe to it from a host (for example Radiant's settings.Sources.AddActivitySource) to collect traces.
        /// </summary>
        public const string ActivitySourceName = "HnswLite";

        #endregion

        #region Instruments

        /// <summary>
        /// Histogram (seconds): end-to-end duration of an index operation.
        /// Labels: hnswlite.operation, hnswlite.outcome, error.type (failures only).
        /// </summary>
        public const string IndexOperationDuration = "hnswlite.index.operation.duration";

        /// <summary>
        /// Counter ({operation}): index operations by outcome.
        /// Labels: hnswlite.operation, hnswlite.outcome, error.type (failures only).
        /// </summary>
        public const string IndexOperations = "hnswlite.index.operations";

        /// <summary>
        /// Histogram (seconds): duration of each stage of an index operation, including the queued stage
        /// spent waiting for the index write lock.
        /// Labels: hnswlite.operation, hnswlite.stage, hnswlite.outcome.
        /// </summary>
        public const string IndexStageDuration = "hnswlite.index.stage.duration";

        /// <summary>
        /// Counter ({vector}): vectors successfully added, removed, or imported.
        /// Labels: hnswlite.operation.
        /// </summary>
        public const string IndexVectors = "hnswlite.index.vectors";

        /// <summary>
        /// UpDownCounter ({operation}): write operations currently waiting for the index write lock.
        /// Labels: hnswlite.operation.
        /// </summary>
        public const string IndexLockWaiting = "hnswlite.index.lock.waiting";

        /// <summary>
        /// UpDownCounter ({operation}): write operations currently holding the index write lock.
        /// The lock has a capacity of one per index.
        /// Labels: hnswlite.operation.
        /// </summary>
        public const string IndexLockHeld = "hnswlite.index.lock.held";

        /// <summary>
        /// Histogram ({result}): number of results returned by a top-K search.
        /// </summary>
        public const string SearchResults = "hnswlite.search.results";

        /// <summary>
        /// Histogram ({node}): number of graph nodes whose distance to the query was evaluated by a search.
        /// </summary>
        public const string SearchNodesEvaluated = "hnswlite.search.nodes.evaluated";

        /// <summary>
        /// Counter ({request}): node lookups served by the per-operation search cache.
        /// Labels: hnswlite.cache.result (hit or miss).
        /// </summary>
        public const string SearchCacheRequests = "hnswlite.search.cache.requests";

        /// <summary>
        /// Histogram (seconds): duration of a storage provider operation.
        /// Labels: hnswlite.storage.provider, hnswlite.storage.operation, hnswlite.outcome, error.type (failures only).
        /// </summary>
        public const string StorageOperationDuration = "hnswlite.storage.operation.duration";

        /// <summary>
        /// Counter ({operation}): storage provider operations by outcome.
        /// Labels: hnswlite.storage.provider, hnswlite.storage.operation, hnswlite.outcome, error.type (failures only).
        /// </summary>
        public const string StorageOperations = "hnswlite.storage.operations";

        /// <summary>
        /// Counter ({transaction}): storage transactions by outcome.
        /// Labels: hnswlite.storage.provider, hnswlite.transaction.outcome.
        /// </summary>
        public const string StorageTransactions = "hnswlite.storage.transactions";

        #endregion

        #region Label-Keys

        /// <summary>
        /// Label key: the index operation (see the Operation* constants).
        /// </summary>
        public const string LabelOperation = "hnswlite.operation";

        /// <summary>
        /// Label key: the operation outcome (see the Outcome* constants).
        /// </summary>
        public const string LabelOutcome = "hnswlite.outcome";

        /// <summary>
        /// Label key: the operation stage (see the Stage* constants).
        /// </summary>
        public const string LabelStage = "hnswlite.stage";

        /// <summary>
        /// Label key: the OpenTelemetry error type, the full type name of the exception that failed the operation.
        /// </summary>
        public const string LabelErrorType = "error.type";

        /// <summary>
        /// Label key: search cache lookup result (hit or miss).
        /// </summary>
        public const string LabelCacheResult = "hnswlite.cache.result";

        /// <summary>
        /// Label key: the storage provider (see the Provider* constants).
        /// </summary>
        public const string LabelStorageProvider = "hnswlite.storage.provider";

        /// <summary>
        /// Label key: the storage operation, a provider method name such as GetNodes or AddNodes.
        /// </summary>
        public const string LabelStorageOperation = "hnswlite.storage.operation";

        /// <summary>
        /// Label key: the storage transaction outcome (see the Transaction* constants).
        /// </summary>
        public const string LabelTransactionOutcome = "hnswlite.transaction.outcome";

        #endregion

        #region Span-Attributes

        /// <summary>
        /// Span attribute: index vector dimension.
        /// </summary>
        public const string AttributeVectorDimension = "hnswlite.vector.dimension";

        /// <summary>
        /// Span attribute: number of vectors in a batch operation.
        /// </summary>
        public const string AttributeBatchSize = "hnswlite.batch.size";

        /// <summary>
        /// Span attribute: requested number of neighbors (K) for a search.
        /// </summary>
        public const string AttributeSearchK = "hnswlite.search.k";

        /// <summary>
        /// Span attribute: effective ef (dynamic candidate list size) for a search.
        /// </summary>
        public const string AttributeSearchEf = "hnswlite.search.ef";

        /// <summary>
        /// Span attribute: number of results returned by a search.
        /// </summary>
        public const string AttributeSearchResults = "hnswlite.search.results";

        /// <summary>
        /// Span attribute: number of nodes evaluated by a search.
        /// </summary>
        public const string AttributeSearchNodesEvaluated = "hnswlite.search.nodes_evaluated";

        /// <summary>
        /// Span attribute: distance function name.
        /// </summary>
        public const string AttributeDistanceFunction = "hnswlite.distance_function";

        /// <summary>
        /// Span attribute: layer assigned to a newly inserted node.
        /// </summary>
        public const string AttributeNodeLayer = "hnswlite.node.layer";

        /// <summary>
        /// Span attribute: OpenTelemetry database system name.
        /// </summary>
        public const string AttributeDbSystemName = "db.system.name";

        /// <summary>
        /// Span attribute: OpenTelemetry database operation name.
        /// </summary>
        public const string AttributeDbOperationName = "db.operation.name";

        #endregion

        #region Operations

        /// <summary>
        /// Operation: add a single vector.
        /// </summary>
        public const string OperationAdd = "add";

        /// <summary>
        /// Operation: add a batch of vectors.
        /// </summary>
        public const string OperationAddBatch = "add_batch";

        /// <summary>
        /// Operation: remove a single vector.
        /// </summary>
        public const string OperationRemove = "remove";

        /// <summary>
        /// Operation: remove a batch of vectors.
        /// </summary>
        public const string OperationRemoveBatch = "remove_batch";

        /// <summary>
        /// Operation: top-K nearest neighbor search.
        /// </summary>
        public const string OperationSearch = "search";

        /// <summary>
        /// Operation: export index state.
        /// </summary>
        public const string OperationExport = "export";

        /// <summary>
        /// Operation: import index state.
        /// </summary>
        public const string OperationImport = "import";

        #endregion

        #region Stages

        /// <summary>
        /// Stage: waiting for the index write lock (a concurrency slot).
        /// </summary>
        public const string StageQueued = "queued";

        /// <summary>
        /// Stage: opening a storage transaction.
        /// </summary>
        public const string StageTransactionBegin = "transaction_begin";

        /// <summary>
        /// Stage: writing node rows and layer assignments to storage.
        /// </summary>
        public const string StageStorageWrite = "storage_write";

        /// <summary>
        /// Stage: linking new nodes into the graph (greedy descent, candidate search, neighbor selection, pruning).
        /// </summary>
        public const string StageGraphInsert = "graph_insert";

        /// <summary>
        /// Stage: unlinking removed nodes from their neighbors and deleting them from storage.
        /// </summary>
        public const string StageGraphUnlink = "graph_unlink";

        /// <summary>
        /// Stage: re-electing the graph entry point after a removal.
        /// </summary>
        public const string StageEntryPointUpdate = "entry_point_update";

        /// <summary>
        /// Stage: repairing neighbor connectivity after a batch removal.
        /// </summary>
        public const string StageGraphRepair = "graph_repair";

        /// <summary>
        /// Stage: clearing existing index contents before an import.
        /// </summary>
        public const string StageClear = "clear";

        /// <summary>
        /// Stage: reading nodes from storage (export).
        /// </summary>
        public const string StageStorageRead = "storage_read";

        /// <summary>
        /// Stage: flushing buffered storage writes.
        /// </summary>
        public const string StageFlush = "flush";

        /// <summary>
        /// Stage: committing the storage transaction.
        /// </summary>
        public const string StageCommit = "commit";

        /// <summary>
        /// Stage: rolling back the storage transaction after a failure.
        /// </summary>
        public const string StageRollback = "rollback";

        /// <summary>
        /// Stage: loading the entry point and pre-fetching its neighbors (search).
        /// </summary>
        public const string StageEntryPoint = "entry_point";

        /// <summary>
        /// Stage: greedy descent through the upper layers (search).
        /// </summary>
        public const string StageGreedyDescent = "greedy_descent";

        /// <summary>
        /// Stage: ef-bounded best-first search on layer 0 (search).
        /// </summary>
        public const string StageLayerZeroSearch = "layer0_search";

        /// <summary>
        /// Stage: materializing result vectors (search).
        /// </summary>
        public const string StageResultBuild = "result_build";

        #endregion

        #region Outcomes

        /// <summary>
        /// Outcome: the operation completed successfully.
        /// </summary>
        public const string OutcomeSuccess = "success";

        /// <summary>
        /// Outcome: the operation failed with an exception.
        /// </summary>
        public const string OutcomeError = "error";

        /// <summary>
        /// Outcome: the operation was cancelled.
        /// </summary>
        public const string OutcomeCancelled = "cancelled";

        /// <summary>
        /// Cache result: the node was served from the search cache.
        /// </summary>
        public const string CacheHit = "hit";

        /// <summary>
        /// Cache result: the node had to be loaded from storage.
        /// </summary>
        public const string CacheMiss = "miss";

        /// <summary>
        /// Transaction outcome: committed.
        /// </summary>
        public const string TransactionCommitted = "committed";

        /// <summary>
        /// Transaction outcome: rolled back.
        /// </summary>
        public const string TransactionRolledBack = "rolled_back";

        /// <summary>
        /// Transaction outcome: commit or rollback failed.
        /// </summary>
        public const string TransactionFailed = "failed";

        #endregion

        #region Providers

        /// <summary>
        /// Storage provider: SQLite (HnswLite.SqliteStorage).
        /// </summary>
        public const string ProviderSqlite = "sqlite";

        /// <summary>
        /// Storage provider: PostgreSQL (HnswLite.PostgresqlStorage).
        /// </summary>
        public const string ProviderPostgresql = "postgresql";

        #endregion
    }
}
