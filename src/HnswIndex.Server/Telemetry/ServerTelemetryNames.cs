namespace HnswIndex.Server.Telemetry
{
    /// <summary>
    /// Stable telemetry names emitted by the HnswLite server: meter and activity source names, instrument names,
    /// label keys, and bounded label values. These strings are consumed by the Grafana dashboards in
    /// assets/grafana. The library-level names live in <see cref="Hnsw.HnswTelemetryNames"/>.
    /// Thread safety: all members are constants.
    /// </summary>
    public static class ServerTelemetryNames
    {
        #region Sources

        /// <summary>
        /// Name of the server meter.
        /// </summary>
        public const string MeterName = "HnswLite.Server";

        /// <summary>
        /// Name of the server activity source.
        /// </summary>
        public const string ActivitySourceName = "HnswLite.Server";

        /// <summary>
        /// Name of Watson's meter and activity source (HTTP layer).
        /// </summary>
        public const string WatsonSourceName = "Watson";

        /// <summary>
        /// Name of the Npgsql driver's meter and activity source (PostgreSQL connection pool and commands).
        /// </summary>
        public const string NpgsqlSourceName = "Npgsql";

        #endregion

        #region Instruments

        /// <summary>
        /// Histogram (seconds): duration of a server operation behind an API route.
        /// Labels: hnswlite.operation, hnswlite.storage.type, hnswlite.outcome, error.type (failures only).
        /// </summary>
        public const string OperationDuration = "hnswlite.server.operation.duration";

        /// <summary>
        /// Counter ({operation}): server operations by outcome.
        /// Labels: hnswlite.operation, hnswlite.storage.type, hnswlite.outcome, error.type (failures only).
        /// </summary>
        public const string Operations = "hnswlite.server.operations";

        /// <summary>
        /// Histogram (seconds): duration of each stage of a server operation.
        /// Labels: hnswlite.operation, hnswlite.stage, hnswlite.outcome.
        /// </summary>
        public const string StageDuration = "hnswlite.server.stage.duration";

        /// <summary>
        /// Histogram ({result}): results returned to the caller by a search, after metadata filtering.
        /// </summary>
        public const string SearchResults = "hnswlite.server.search.results";

        /// <summary>
        /// Counter ({result}): search candidates removed by label/tag metadata filters.
        /// </summary>
        public const string SearchFiltered = "hnswlite.server.search.filtered";

        /// <summary>
        /// Observable gauge ({index}): indexes currently loaded. Labels: hnswlite.storage.type.
        /// </summary>
        public const string Indexes = "hnswlite.server.indexes";

        /// <summary>
        /// Observable gauge ({vector}): vectors held by loaded indexes. Labels: hnswlite.storage.type.
        /// </summary>
        public const string Vectors = "hnswlite.server.vectors";

        /// <summary>
        /// Counter ({request}): API key authentication decisions. Labels: hnswlite.auth.result.
        /// </summary>
        public const string AuthRequests = "hnswlite.server.auth.requests";

        /// <summary>
        /// Counter ({error}): API error responses by error code. Labels: hnswlite.api.error.
        /// </summary>
        public const string ApiErrors = "hnswlite.server.api.errors";

        /// <summary>
        /// Counter ({job}): startup index-reload jobs by outcome. Labels: hnswlite.outcome.
        /// </summary>
        public const string ReloadJobs = "hnswlite.server.reload.jobs";

        /// <summary>
        /// Histogram (seconds): duration of the startup index-reload job. Labels: hnswlite.outcome.
        /// </summary>
        public const string ReloadDuration = "hnswlite.server.reload.duration";

        /// <summary>
        /// Histogram (seconds): duration of each reload stage. Labels: hnswlite.stage, hnswlite.outcome.
        /// </summary>
        public const string ReloadStageDuration = "hnswlite.server.reload.stage.duration";

        /// <summary>
        /// Counter ({stage}): reload stage executions by outcome. Labels: hnswlite.stage, hnswlite.outcome.
        /// </summary>
        public const string ReloadStages = "hnswlite.server.reload.stages";

        /// <summary>
        /// Counter ({index}): indexes processed by the reload job. Labels: hnswlite.storage.type, hnswlite.reload.result.
        /// </summary>
        public const string ReloadIndexes = "hnswlite.server.reload.indexes";

        /// <summary>
        /// Observable gauge (seconds): Unix time of the last successful reload job, 0 when none has succeeded.
        /// </summary>
        public const string ReloadLastSuccess = "hnswlite.server.reload.last_success";

        /// <summary>
        /// Observable gauge ({info}): constant 1 carrying build information. Labels: hnswlite.version, dotnet.runtime.
        /// </summary>
        public const string BuildInfo = "hnswlite.server.build.info";

        /// <summary>
        /// Observable gauge ({info}): constant 1 carrying safe (non-secret) configuration values.
        /// Labels: hnswlite.config.default_storage_type, hnswlite.config.require_authentication,
        /// hnswlite.config.cors_enabled, hnswlite.config.otlp_enabled, hnswlite.config.prometheus_enabled.
        /// </summary>
        public const string ConfigInfo = "hnswlite.server.config.info";

        #endregion

        #region Label-Keys

        /// <summary>
        /// Label key: server operation (see the Operation* constants).
        /// </summary>
        public const string LabelOperation = "hnswlite.operation";

        /// <summary>
        /// Label key: stage name.
        /// </summary>
        public const string LabelStage = "hnswlite.stage";

        /// <summary>
        /// Label key: outcome (success, error, cancelled).
        /// </summary>
        public const string LabelOutcome = "hnswlite.outcome";

        /// <summary>
        /// Label key: OpenTelemetry error type (full exception type name).
        /// </summary>
        public const string LabelErrorType = "error.type";

        /// <summary>
        /// Label key: normalized storage type (postgresql, sqlite, ram, unknown).
        /// </summary>
        public const string LabelStorageType = "hnswlite.storage.type";

        /// <summary>
        /// Label key: authentication result (see the Auth* constants).
        /// </summary>
        public const string LabelAuthResult = "hnswlite.auth.result";

        /// <summary>
        /// Label key: API error code (an ApiErrorEnum name).
        /// </summary>
        public const string LabelApiError = "hnswlite.api.error";

        /// <summary>
        /// Label key: per-index reload result (loaded, skipped, failed).
        /// </summary>
        public const string LabelReloadResult = "hnswlite.reload.result";

        /// <summary>
        /// Label key: server version.
        /// </summary>
        public const string LabelVersion = "hnswlite.version";

        /// <summary>
        /// Label key: .NET runtime description.
        /// </summary>
        public const string LabelRuntime = "dotnet.runtime";

        /// <summary>
        /// Label key: configured default storage type.
        /// </summary>
        public const string LabelConfigDefaultStorageType = "hnswlite.config.default_storage_type";

        /// <summary>
        /// Label key: whether API key authentication is required.
        /// </summary>
        public const string LabelConfigRequireAuthentication = "hnswlite.config.require_authentication";

        /// <summary>
        /// Label key: whether CORS headers are enabled.
        /// </summary>
        public const string LabelConfigCorsEnabled = "hnswlite.config.cors_enabled";

        /// <summary>
        /// Label key: whether OTLP export is enabled.
        /// </summary>
        public const string LabelConfigOtlpEnabled = "hnswlite.config.otlp_enabled";

        /// <summary>
        /// Label key: whether the Prometheus scrape endpoint is enabled.
        /// </summary>
        public const string LabelConfigPrometheusEnabled = "hnswlite.config.prometheus_enabled";

        #endregion

        #region Span-Attributes

        /// <summary>
        /// Span attribute: index name (free text, spans only, never a metric label).
        /// </summary>
        public const string AttributeIndexName = "hnswlite.index.name";

        /// <summary>
        /// Span attribute: vector identifier (spans only).
        /// </summary>
        public const string AttributeVectorId = "hnswlite.vector.id";

        /// <summary>
        /// Span attribute: number of items in a batch request.
        /// </summary>
        public const string AttributeBatchSize = "hnswlite.batch.size";

        /// <summary>
        /// Span attribute: number of results returned.
        /// </summary>
        public const string AttributeResultCount = "hnswlite.result.count";

        /// <summary>
        /// Span attribute: number of results removed by metadata filters.
        /// </summary>
        public const string AttributeFilteredCount = "hnswlite.filtered.count";

        /// <summary>
        /// Span attribute: number of indexes loaded by a reload stage.
        /// </summary>
        public const string AttributeLoadedCount = "hnswlite.reload.loaded";

        #endregion

        #region Operations

        /// <summary>
        /// Operation: create an index.
        /// </summary>
        public const string OperationIndexCreate = "index.create";

        /// <summary>
        /// Operation: read index metadata.
        /// </summary>
        public const string OperationIndexGet = "index.get";

        /// <summary>
        /// Operation: enumerate indexes.
        /// </summary>
        public const string OperationIndexList = "index.list";

        /// <summary>
        /// Operation: delete an index.
        /// </summary>
        public const string OperationIndexDelete = "index.delete";

        /// <summary>
        /// Operation: add one vector.
        /// </summary>
        public const string OperationVectorAdd = "vector.add";

        /// <summary>
        /// Operation: add a batch of vectors.
        /// </summary>
        public const string OperationVectorAddBatch = "vector.add_batch";

        /// <summary>
        /// Operation: read one vector.
        /// </summary>
        public const string OperationVectorGet = "vector.get";

        /// <summary>
        /// Operation: enumerate vectors.
        /// </summary>
        public const string OperationVectorList = "vector.list";

        /// <summary>
        /// Operation: remove one vector.
        /// </summary>
        public const string OperationVectorRemove = "vector.remove";

        /// <summary>
        /// Operation: nearest-neighbor search.
        /// </summary>
        public const string OperationSearch = "search";

        #endregion

        #region Stages

        /// <summary>
        /// Stage: opening or creating the index's storage provider.
        /// </summary>
        public const string StageStorageOpen = "storage_open";

        /// <summary>
        /// Stage: persisting server metadata into the index store.
        /// </summary>
        public const string StageMetadataPersist = "metadata_persist";

        /// <summary>
        /// Stage: releasing an index's storage resources.
        /// </summary>
        public const string StageDispose = "dispose";

        /// <summary>
        /// Stage: inserting vectors into the HNSW graph (library call).
        /// </summary>
        public const string StageInsert = "insert";

        /// <summary>
        /// Stage: writing vector metadata (name, labels, tags).
        /// </summary>
        public const string StageMetadataWrite = "metadata_write";

        /// <summary>
        /// Stage: removing a vector from the HNSW graph (library call).
        /// </summary>
        public const string StageRemove = "remove";

        /// <summary>
        /// Stage: top-K graph search (library call).
        /// </summary>
        public const string StageTopK = "topk";

        /// <summary>
        /// Stage: fetching nodes from storage to populate metadata.
        /// </summary>
        public const string StageMetadataFetch = "metadata_fetch";

        /// <summary>
        /// Stage: applying label/tag filters and shaping results.
        /// </summary>
        public const string StageFilter = "filter";

        /// <summary>
        /// Stage: listing vector identifiers from storage.
        /// </summary>
        public const string StageEnumerateIds = "enumerate_ids";

        /// <summary>
        /// Stage: reading a single node from storage.
        /// </summary>
        public const string StageNodeFetch = "node_fetch";

        /// <summary>
        /// Reload stage: SQLite index files.
        /// </summary>
        public const string StageReloadSqlite = "sqlite";

        /// <summary>
        /// Reload stage: PostgreSQL indexes.
        /// </summary>
        public const string StageReloadPostgresql = "postgresql";

        #endregion

        #region Values

        /// <summary>
        /// Storage type: PostgreSQL.
        /// </summary>
        public const string StoragePostgresql = "postgresql";

        /// <summary>
        /// Storage type: SQLite.
        /// </summary>
        public const string StorageSqlite = "sqlite";

        /// <summary>
        /// Storage type: in-memory.
        /// </summary>
        public const string StorageRam = "ram";

        /// <summary>
        /// Storage type: unknown or not applicable.
        /// </summary>
        public const string StorageUnknown = "unknown";

        /// <summary>
        /// Auth result: authentication is disabled in settings.
        /// </summary>
        public const string AuthDisabled = "disabled";

        /// <summary>
        /// Auth result: a valid API key was presented.
        /// </summary>
        public const string AuthSuccess = "success";

        /// <summary>
        /// Auth result: no API key header was presented.
        /// </summary>
        public const string AuthMissingKey = "missing_key";

        /// <summary>
        /// Auth result: an API key was presented but did not match.
        /// </summary>
        public const string AuthInvalidKey = "invalid_key";

        /// <summary>
        /// Reload result: the index was loaded.
        /// </summary>
        public const string ReloadLoaded = "loaded";

        /// <summary>
        /// Reload result: the index was skipped (missing or invalid metadata).
        /// </summary>
        public const string ReloadSkipped = "skipped";

        /// <summary>
        /// Reload result: the index failed to load.
        /// </summary>
        public const string ReloadFailed = "failed";

        /// <summary>
        /// Root span name of the startup index-reload job.
        /// </summary>
        public const string ReloadJobSpanName = "job:index_reload";

        #endregion
    }
}
