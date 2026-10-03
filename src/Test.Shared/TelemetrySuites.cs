namespace HnswLite.Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Hnsw;
    using Hnsw.RamStorage;
    using HnswIndex.PostgresqlStorage;
    using HnswIndex.Server.Classes;
    using HnswIndex.Server.Services;
    using HnswIndex.Server.Telemetry;
    using HnswIndex.SqliteStorage;
    using Hsnw;
    using Radiant;
    using Touchstone.Core;
    using IndexType = Hnsw.HnswIndex;

    /// <summary>
    /// Proves that the HnswLite libraries and server emit the documented metrics and spans: index operations and
    /// their stages, storage providers, the server service layer, the startup reload job, gauges, authentication
    /// and API error counters, the Radiant telemetry host, failure paths, and the no-listener path.
    /// Uses in-memory BCL listeners (see <see cref="TelemetryCapture"/>); no collector is required.
    /// </summary>
    public static class TelemetrySuites
    {
        #region Public-Members

        /// <summary>
        /// All telemetry suites.
        /// </summary>
        public static IReadOnlyList<TestSuiteDescriptor> All =>
            new List<TestSuiteDescriptor>
            {
                LibrarySuite(),
                StorageSuite(),
                ServerSuite(),
            };

        #endregion

        #region Private-Members

        private const int _Dimension = 4;
        private static readonly string[] _AddStages = new string[]
        {
            HnswTelemetryNames.StageQueued,
            HnswTelemetryNames.StageTransactionBegin,
            HnswTelemetryNames.StageStorageWrite,
            HnswTelemetryNames.StageGraphInsert,
            HnswTelemetryNames.StageFlush,
            HnswTelemetryNames.StageCommit,
        };

        private static readonly string[] _SearchStages = new string[]
        {
            HnswTelemetryNames.StageEntryPoint,
            HnswTelemetryNames.StageGreedyDescent,
            HnswTelemetryNames.StageLayerZeroSearch,
            HnswTelemetryNames.StageResultBuild,
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Library-level telemetry: index operations, stages, lock, search, failure paths, no-listener path.
        /// </summary>
        /// <returns>The suite.</returns>
        public static TestSuiteDescriptor LibrarySuite()
        {
            const string suite = "Telemetry.Library";
            return new TestSuiteDescriptor(
                suiteId: suite,
                displayName: "Telemetry - Library",
                cases: new List<TestCaseDescriptor>
                {
                    Case(suite, "SourceNamesAreStable", "Meter and activity source names match the documented contract", ct =>
                    {
                        TestAssert.Equal("HnswLite", HnswTelemetry.Meter.Name, "meter name");
                        TestAssert.Equal("HnswLite", HnswTelemetry.ActivitySource.Name, "activity source name");
                        TestAssert.Equal("HnswLite.Server", ServerTelemetry.Meter.Name, "server meter name");
                        TestAssert.Equal("HnswLite.Server", ServerTelemetry.ActivitySource.Name, "server activity source name");
                        TestAssert.True(HnswTelemetry.DurationBuckets.Length > 5, "duration buckets defined");
                        return Task.CompletedTask;
                    }),

                    Case(suite, "NoListenerDoesNotThrow", "Operations and helpers run cleanly with no listener attached", async ct =>
                    {
                        RamStorageProvider provider = new RamStorageProvider();
                        IndexType index = new IndexType(_Dimension, provider, 7);
                        await index.AddAsync(Guid.NewGuid(), Vector(1), ct).ConfigureAwait(false);
                        await index.AddNodesAsync(Batch(5, 10), ct).ConfigureAwait(false);
                        List<VectorResult> results = (await index.GetTopKAsync(Vector(2), 3, null, ct).ConfigureAwait(false)).ToList();
                        TestAssert.True(results.Count > 0, "search works without a listener");

                        HnswTelemetry.RecordStorageOperation("sqlite", "GetNode", HnswTelemetry.GetTimestamp(), new InvalidOperationException("x"));
                        HnswTelemetry.RecordStorageTransaction("sqlite", HnswTelemetryNames.TransactionCommitted);
                        HnswTelemetry.CompleteActivity(null, new InvalidOperationException("x"));
                        if (!HnswTelemetry.ActivitySource.HasListeners())
                        {
                            TestAssert.True(HnswTelemetry.StartStorageActivity("sqlite", "AddNodes") == null, "no span without a listener");
                        }

                        ServerTelemetry.RecordAuth(ServerTelemetryNames.AuthSuccess);
                        ServerTelemetry.RecordApiError(ApiErrorEnum.BadRequest);
                        ServerTelemetry.RecordReloadJob(0.1, null);
                        using (ServerOperationScope scope = ServerOperationScope.Start(ServerTelemetryNames.OperationSearch, "idx"))
                        {
                            scope.BeginStage(ServerTelemetryNames.StageTopK);
                            scope.Fail(new InvalidOperationException("x"));
                        }

                        await provider.DisposeAsync().ConfigureAwait(false);
                    }),

                    Case(suite, "AddSearchRemoveEmitMetricsAndSpans", "Add, search, and remove emit operation, stage, vector, search, and lock telemetry with nested spans", async ct =>
                    {
                        using TelemetryCapture capture = new TelemetryCapture();
                        RamStorageProvider provider = new RamStorageProvider();
                        IndexType index = new IndexType(_Dimension, provider, 11);

                        List<Guid> ids = new List<Guid>();
                        for (int i = 0; i < 20; i++)
                        {
                            Guid id = Guid.NewGuid();
                            ids.Add(id);
                            await index.AddAsync(id, Vector(i), ct).ConfigureAwait(false);
                        }

                        List<VectorResult> results = (await index.GetTopKAsync(Vector(3), 5, null, ct).ConfigureAwait(false)).ToList();
                        await index.RemoveAsync(ids[0], ct).ConfigureAwait(false);

                        AssertOperation(capture, HnswTelemetryNames.OperationAdd, HnswTelemetryNames.OutcomeSuccess, 20);
                        AssertOperation(capture, HnswTelemetryNames.OperationSearch, HnswTelemetryNames.OutcomeSuccess, 1);
                        AssertOperation(capture, HnswTelemetryNames.OperationRemove, HnswTelemetryNames.OutcomeSuccess, 1);

                        foreach (string stage in _AddStages)
                        {
                            TestAssert.True(
                                capture.Measurements(HnswTelemetryNames.IndexStageDuration, m => m.Has(HnswTelemetryNames.LabelOperation, "add") && m.Has(HnswTelemetryNames.LabelStage, stage)).Any(),
                                "add stage recorded: " + stage);
                        }

                        foreach (string stage in _SearchStages)
                        {
                            TestAssert.True(
                                capture.Measurements(HnswTelemetryNames.IndexStageDuration, m => m.Has(HnswTelemetryNames.LabelOperation, "search") && m.Has(HnswTelemetryNames.LabelStage, stage)).Any(),
                                "search stage recorded: " + stage);
                        }

                        TestAssert.Equal(20.0, capture.Sum(HnswTelemetryNames.IndexVectors, m => m.Has(HnswTelemetryNames.LabelOperation, "add")), "vectors added");
                        TestAssert.Equal(1.0, capture.Sum(HnswTelemetryNames.IndexVectors, m => m.Has(HnswTelemetryNames.LabelOperation, "remove")), "vectors removed");
                        TestAssert.Equal(0.0, capture.Sum(HnswTelemetryNames.IndexLockHeld, m => true), "lock held returns to zero");
                        TestAssert.Equal(0.0, capture.Sum(HnswTelemetryNames.IndexLockWaiting, m => true), "lock waiting returns to zero");
                        TestAssert.True(capture.Sum(HnswTelemetryNames.IndexLockHeld, m => m.Value > 0) >= 21, "lock acquisitions counted");

                        List<CapturedMeasurement> searchResults = capture.Measurements(HnswTelemetryNames.SearchResults);
                        TestAssert.Equal(1, searchResults.Count, "one search results measurement");
                        TestAssert.Equal((double)results.Count, searchResults[0].Value, "search results value");
                        TestAssert.True(capture.Measurements(HnswTelemetryNames.SearchNodesEvaluated).Single().Value >= results.Count, "nodes evaluated >= results");
                        TestAssert.True(capture.Sum(HnswTelemetryNames.SearchCacheRequests, m => true) > 0, "cache requests counted");

                        List<Activity> addSpans = capture.Spans("hnsw.add");
                        TestAssert.Equal(20, addSpans.Count, "one span per add");
                        Activity search = capture.Spans("hnsw.search").Single();
                        TestAssert.Equal(ActivityStatusCode.Ok, search.Status, "search span status ok");
                        TestAssert.Equal(5, Convert.ToInt32(search.GetTagItem(HnswTelemetryNames.AttributeSearchK)), "search k attribute");
                        TestAssert.True(search.GetTagItem(HnswTelemetryNames.AttributeSearchNodesEvaluated) != null, "nodes evaluated attribute");
                        Activity layer0 = capture.Spans("stage:layer0_search").Single();
                        TestAssert.Equal(search.SpanId, layer0.ParentSpanId, "stage span is a child of the operation span");
                        TestAssert.True(capture.IsDescendant(search, capture.Root!), "operation span nests under the caller's span");
                        TestAssert.True(capture.Spans("stage:queued").Count >= 21, "queued stage spans for write operations");

                        await provider.DisposeAsync().ConfigureAwait(false);
                    }),

                    Case(suite, "BatchExportImportEmitTelemetry", "Batch add/remove, export, and import emit operation telemetry", async ct =>
                    {
                        using TelemetryCapture capture = new TelemetryCapture();
                        RamStorageProvider provider = new RamStorageProvider();
                        IndexType index = new IndexType(_Dimension, provider, 3);
                        Dictionary<Guid, List<float>> batch = Batch(30, 0);
                        await index.AddNodesAsync(batch, ct).ConfigureAwait(false);
                        await index.RemoveNodesAsync(batch.Keys.Take(5).ToList(), ct).ConfigureAwait(false);
                        HnswState state = await index.ExportStateAsync(ct).ConfigureAwait(false);

                        RamStorageProvider provider2 = new RamStorageProvider();
                        IndexType index2 = new IndexType(_Dimension, provider2, 3);
                        await index2.ImportStateAsync(state, ct).ConfigureAwait(false);

                        AssertOperation(capture, HnswTelemetryNames.OperationAddBatch, HnswTelemetryNames.OutcomeSuccess, 1);
                        AssertOperation(capture, HnswTelemetryNames.OperationRemoveBatch, HnswTelemetryNames.OutcomeSuccess, 1);
                        AssertOperation(capture, HnswTelemetryNames.OperationExport, HnswTelemetryNames.OutcomeSuccess, 1);
                        AssertOperation(capture, HnswTelemetryNames.OperationImport, HnswTelemetryNames.OutcomeSuccess, 1);
                        TestAssert.Equal(30.0, capture.Sum(HnswTelemetryNames.IndexVectors, m => m.Has(HnswTelemetryNames.LabelOperation, "add_batch")), "batch vectors");
                        TestAssert.Equal(5.0, capture.Sum(HnswTelemetryNames.IndexVectors, m => m.Has(HnswTelemetryNames.LabelOperation, "remove_batch")), "batch removed");
                        TestAssert.Equal(25.0, capture.Sum(HnswTelemetryNames.IndexVectors, m => m.Has(HnswTelemetryNames.LabelOperation, "import")), "imported");
                        TestAssert.True(capture.Measurements(HnswTelemetryNames.IndexStageDuration, m => m.Has(HnswTelemetryNames.LabelStage, HnswTelemetryNames.StageGraphRepair)).Any(), "graph repair stage");
                        TestAssert.True(capture.Measurements(HnswTelemetryNames.IndexStageDuration, m => m.Has(HnswTelemetryNames.LabelStage, HnswTelemetryNames.StageClear)).Any(), "import clear stage");
                        TestAssert.Equal(30, Convert.ToInt32(capture.Spans("hnsw.add_batch").Single().GetTagItem(HnswTelemetryNames.AttributeBatchSize)), "batch size attribute");

                        await provider.DisposeAsync().ConfigureAwait(false);
                        await provider2.DisposeAsync().ConfigureAwait(false);
                    }),

                    Case(suite, "FailurePathsRecordErrorTypeAndSpanStatus", "Failures record outcome, error.type, error span status, and an exception event", async ct =>
                    {
                        using TelemetryCapture capture = new TelemetryCapture();
                        RamStorageProvider provider = new RamStorageProvider();
                        IndexType index = new IndexType(_Dimension, provider, 5);

                        await TestAssert.ThrowsAsync<ArgumentException>(
                            () => index.AddAsync(Guid.NewGuid(), new List<float> { 1, 2 }, ct), "dimension mismatch").ConfigureAwait(false);
                        await TestAssert.ThrowsAsync<ArgumentOutOfRangeException>(
                            () => index.GetTopKAsync(Vector(1), 0, null, ct), "invalid k").ConfigureAwait(false);

                        using (CancellationTokenSource cts = new CancellationTokenSource())
                        {
                            cts.Cancel();
                            await TestAssert.ThrowsAsync<OperationCanceledException>(
                                () => index.AddAsync(Guid.NewGuid(), Vector(1), cts.Token), "cancelled add").ConfigureAwait(false);
                        }

                        List<CapturedMeasurement> errors = capture.Measurements(HnswTelemetryNames.IndexOperations,
                            m => m.Has(HnswTelemetryNames.LabelOutcome, HnswTelemetryNames.OutcomeError));
                        TestAssert.True(errors.Any(m => m.Has(HnswTelemetryNames.LabelOperation, "add") && m.Has("error.type", "System.ArgumentException")), "add error with error.type");
                        TestAssert.True(errors.Any(m => m.Has(HnswTelemetryNames.LabelOperation, "search") && m.Has("error.type", "System.ArgumentOutOfRangeException")), "search error with error.type");
                        TestAssert.True(capture.Measurements(HnswTelemetryNames.IndexOperations,
                            m => m.Has(HnswTelemetryNames.LabelOutcome, HnswTelemetryNames.OutcomeCancelled) && m.Has(HnswTelemetryNames.LabelOperation, "add")).Any(), "cancelled outcome");
                        TestAssert.True(capture.Measurements(HnswTelemetryNames.IndexOperationDuration,
                            m => m.Has(HnswTelemetryNames.LabelOutcome, HnswTelemetryNames.OutcomeError)).Any(), "error durations recorded");

                        Activity failed = capture.Spans("hnsw.add").First(a => a.Status == ActivityStatusCode.Error);
                        TestAssert.Equal("System.ArgumentException", failed.GetTagItem("error.type")?.ToString(), "span error.type");
                        TestAssert.True(failed.Events.Any(e => e.Name == "exception"), "exception event recorded");
                        TestAssert.Equal(0.0, capture.Sum(HnswTelemetryNames.IndexLockWaiting, m => true), "cancelled waiter released");

                        await provider.DisposeAsync().ConfigureAwait(false);
                    }),
                });
        }

        /// <summary>
        /// Storage provider telemetry: SQLite (always) and PostgreSQL (when configured).
        /// </summary>
        /// <returns>The suite.</returns>
        public static TestSuiteDescriptor StorageSuite()
        {
            const string suite = "Telemetry.Storage";
            TestStorageConfiguration configuration = TestStorageConfiguration.FromEnvironment();
            bool postgresConfigured = !string.IsNullOrWhiteSpace(configuration.PostgresqlConnectionString);

            return new TestSuiteDescriptor(
                suiteId: suite,
                displayName: "Telemetry - Storage",
                cases: new List<TestCaseDescriptor>
                {
                    Case(suite, "SqliteEmitsStorageMetricsAndClientSpans", "SQLite provider emits per-operation metrics and client spans for coarse operations", async ct =>
                    {
                        string dir = NewTempDir();
                        try
                        {
                            using TelemetryCapture capture = new TelemetryCapture();
                            await using (SqliteStorageProvider provider = await SqliteStorageProvider.CreateAsync(Path.Combine(dir, "t.db"), cancellationToken: ct).ConfigureAwait(false))
                            {
                                IndexType index = new IndexType(_Dimension, provider, 9);
                                await index.AddNodesAsync(Batch(10, 0), ct).ConfigureAwait(false);
                                await index.AddAsync(Guid.NewGuid(), Vector(50), ct).ConfigureAwait(false);
                                await index.GetTopKAsync(Vector(4), 3, null, ct).ConfigureAwait(false);
                            }

                            foreach (string op in new string[] { "Open", "AddNodes", "AddNode", "GetNode", "GetNodes", "SaveNeighbors", "Flush" })
                            {
                                TestAssert.True(capture.Measurements(HnswTelemetryNames.StorageOperations,
                                    m => m.Has(HnswTelemetryNames.LabelStorageProvider, "sqlite") && m.Has(HnswTelemetryNames.LabelStorageOperation, op) && m.Has(HnswTelemetryNames.LabelOutcome, "success")).Any(),
                                    "sqlite storage op counted: " + op);
                            }

                            TestAssert.True(capture.Measurements(HnswTelemetryNames.StorageOperationDuration, m => m.Has(HnswTelemetryNames.LabelStorageProvider, "sqlite")).All(m => m.Value >= 0), "durations non-negative");

                            Activity addNodes = capture.Spans("sqlite AddNodes").Single();
                            TestAssert.Equal(ActivityKind.Client, addNodes.Kind, "storage span is a client span");
                            TestAssert.Equal("sqlite", addNodes.GetTagItem("db.system.name")?.ToString(), "db.system.name");
                            Activity batch = capture.Spans("hnsw.add_batch").Single();
                            TestAssert.True(capture.IsDescendant(addNodes, batch), "storage span nests under the index operation");
                            TestAssert.Equal(0, capture.Spans("sqlite GetNode").Count, "per-node reads are metrics only (no span flood)");
                        }
                        finally
                        {
                            TryDeleteDir(dir);
                        }
                    }),

                    Case(suite, "SqliteOpenFailureIsRecorded", "A failed SQLite open records an error outcome and an error span", async ct =>
                    {
                        string dir = NewTempDir();
                        try
                        {
                            using TelemetryCapture capture = new TelemetryCapture();
                            string missing = Path.Combine(dir, "missing.db");
                            bool threw = false;
                            try
                            {
                                await using SqliteStorageProvider provider = await SqliteStorageProvider.CreateAsync(missing, createIfNotExists: false, cancellationToken: ct).ConfigureAwait(false);
                            }
                            catch (Exception)
                            {
                                threw = true;
                            }

                            TestAssert.True(threw, "opening a missing database without create throws");
                            CapturedMeasurement failed = capture.Measurements(HnswTelemetryNames.StorageOperations,
                                m => m.Has(HnswTelemetryNames.LabelStorageOperation, "Open") && m.Has(HnswTelemetryNames.LabelOutcome, "error")).Single();
                            TestAssert.True(failed.Tags.ContainsKey("error.type"), "error.type label present");
                            TestAssert.Equal(ActivityStatusCode.Error, capture.Spans("sqlite Open").Single().Status, "open span marked error");
                        }
                        finally
                        {
                            TryDeleteDir(dir);
                        }
                    }),

                    new TestCaseDescriptor(
                        suiteId: suite,
                        caseId: "PostgresqlEmitsStorageAndTransactionTelemetry",
                        displayName: "PostgreSQL provider emits storage, transaction, and schema telemetry",
                        executeAsync: async ct =>
                        {
                            using TelemetryCapture capture = new TelemetryCapture();
                            string name = "telemetry_" + Guid.NewGuid().ToString("N");
                            await using (PostgresqlStorageProvider provider = await PostgresqlStorageProvider.CreateAsync(
                                configuration.GetRequiredPostgresqlConnectionString(), name, _Dimension, "Euclidean", 8, 16, 50, true, ct).ConfigureAwait(false))
                            {
                                IndexType index = new IndexType(_Dimension, provider, 2);
                                await index.AddNodesAsync(Batch(8, 0), ct).ConfigureAwait(false);
                                await index.GetTopKAsync(Vector(2), 3, null, ct).ConfigureAwait(false);
                                await TestAssert.ThrowsAsync<ArgumentException>(() => index.AddAsync(Guid.NewGuid(), new List<float> { 1 }, ct), "bad dimension").ConfigureAwait(false);
                            }

                            // Reopen so reads miss the provider's node cache and go to the database.
                            await using (PostgresqlStorageProvider reopened = await PostgresqlStorageProvider.CreateAsync(
                                configuration.GetRequiredPostgresqlConnectionString(), name, _Dimension, "Euclidean", 8, 16, 50, false, ct).ConfigureAwait(false))
                            {
                                IndexType index = new IndexType(_Dimension, reopened, 2);
                                await index.GetTopKAsync(Vector(2), 3, null, ct).ConfigureAwait(false);
                                await reopened.ClearAsync(ct).ConfigureAwait(false);
                            }

                            foreach (string op in new string[] { "EnsureSchema", "EnsureIndex", "BeginTransaction", "AddNodes", "Commit", "GetNodes", "UpsertNeighbor" })
                            {
                                TestAssert.True(capture.Measurements(HnswTelemetryNames.StorageOperations,
                                    m => m.Has(HnswTelemetryNames.LabelStorageProvider, "postgresql") && m.Has(HnswTelemetryNames.LabelStorageOperation, op)).Any(),
                                    "postgresql storage op counted: " + op);
                            }

                            TestAssert.True(capture.Measurements(HnswTelemetryNames.StorageTransactions,
                                m => m.Has(HnswTelemetryNames.LabelTransactionOutcome, HnswTelemetryNames.TransactionCommitted)).Any(), "committed transaction counted");
                            TestAssert.True(capture.Spans("postgresql AddNodes").Any(), "postgresql AddNodes client span");
                            TestAssert.True(capture.Spans("postgresql BeginTransaction").Any(), "postgresql BeginTransaction client span");
                        },
                        tags: null,
                        skip: !postgresConfigured,
                        skipReason: postgresConfigured ? null : "Set " + TestStorageConfiguration.PostgresqlConnectionEnvVar + " to run PostgreSQL telemetry tests."),
                });
        }

        /// <summary>
        /// Server telemetry: service operations, stages, failures, reload job, gauges, auth, API errors, telemetry host.
        /// </summary>
        /// <returns>The suite.</returns>
        public static TestSuiteDescriptor ServerSuite()
        {
            const string suite = "Telemetry.Server";
            return new TestSuiteDescriptor(
                suiteId: suite,
                displayName: "Telemetry - Server",
                cases: new List<TestCaseDescriptor>
                {
                    Case(suite, "ServiceOperationsEmitMetricsStagesAndSpans", "IndexManager operations emit operation/stage metrics and spans that wrap the library spans", async ct =>
                    {
                        string dir = NewTempDir();
                        try
                        {
                            using TelemetryCapture capture = new TelemetryCapture();
                            await using IndexManager manager = new IndexManager(RamSettings(dir));
                            await manager.CreateIndexAsync(new CreateIndexRequest { Name = "idx", Dimension = _Dimension, StorageType = "RAM" }, ct).ConfigureAwait(false);
                            for (int i = 0; i < 10; i++)
                            {
                                await manager.AddVectorAsync("idx", new AddVectorRequest { Vector = Vector(i), Labels = i % 2 == 0 ? new List<string> { "even" } : null }, ct).ConfigureAwait(false);
                            }

                            AddVectorsRequest batch = new AddVectorsRequest();
                            for (int i = 10; i < 15; i++) batch.Vectors.Add(new AddVectorRequest { Vector = Vector(i) });
                            await manager.AddVectorsAsync("idx", batch, ct).ConfigureAwait(false);

                            SearchResponse response = await manager.SearchAsync("idx", new SearchRequest { Vector = Vector(2), K = 6, Labels = new List<string> { "even" } }, ct).ConfigureAwait(false);
                            manager.GetIndex("idx");
                            manager.EnumerateIndexes(new EnumerationQuery());
                            EnumerationResult<VectorEntryResponse> page = await manager.EnumerateVectorsAsync("idx", new EnumerationQuery(), false, ct).ConfigureAwait(false);
                            await manager.GetVectorAsync("idx", page.Objects![0].GUID, ct).ConfigureAwait(false);
                            await manager.RemoveVectorAsync("idx", page.Objects![0].GUID, ct).ConfigureAwait(false);
                            await manager.DeleteIndexAsync("idx").ConfigureAwait(false);

                            foreach (string op in new string[]
                            {
                                ServerTelemetryNames.OperationIndexCreate, ServerTelemetryNames.OperationVectorAdd, ServerTelemetryNames.OperationVectorAddBatch,
                                ServerTelemetryNames.OperationSearch, ServerTelemetryNames.OperationIndexGet, ServerTelemetryNames.OperationIndexList,
                                ServerTelemetryNames.OperationVectorList, ServerTelemetryNames.OperationVectorGet, ServerTelemetryNames.OperationVectorRemove,
                                ServerTelemetryNames.OperationIndexDelete,
                            })
                            {
                                TestAssert.True(capture.Measurements(ServerTelemetryNames.Operations,
                                    m => m.Has(ServerTelemetryNames.LabelOperation, op) && m.Has(ServerTelemetryNames.LabelOutcome, "success")
                                        && m.Has(ServerTelemetryNames.LabelStorageType, op == ServerTelemetryNames.OperationIndexList ? "unknown" : "ram")).Any(),
                                    "server operation counted: " + op);
                                TestAssert.True(capture.Measurements(ServerTelemetryNames.OperationDuration, m => m.Has(ServerTelemetryNames.LabelOperation, op)).Any(), "server duration: " + op);
                            }

                            foreach (string stage in new string[] { ServerTelemetryNames.StageTopK, ServerTelemetryNames.StageMetadataFetch, ServerTelemetryNames.StageFilter })
                            {
                                TestAssert.True(capture.Measurements(ServerTelemetryNames.StageDuration,
                                    m => m.Has(ServerTelemetryNames.LabelOperation, "search") && m.Has(ServerTelemetryNames.LabelStage, stage)).Any(), "search stage: " + stage);
                            }

                            TestAssert.True(capture.Measurements(ServerTelemetryNames.StageDuration,
                                m => m.Has(ServerTelemetryNames.LabelOperation, "vector.add") && m.Has(ServerTelemetryNames.LabelStage, ServerTelemetryNames.StageMetadataWrite)).Any(), "metadata_write stage");

                            TestAssert.Equal((double)response.Results.Count, capture.Measurements(ServerTelemetryNames.SearchResults).Single().Value, "server search results");
                            TestAssert.Equal((double)response.FilteredCount, capture.Sum(ServerTelemetryNames.SearchFiltered, m => true), "server filtered count");

                            Activity serverSearch = capture.Spans("hnswlite search").Single();
                            TestAssert.Equal("idx", serverSearch.GetTagItem(ServerTelemetryNames.AttributeIndexName)?.ToString(), "index name on span (not on metrics)");
                            Activity librarySearch = capture.Spans("hnsw.search").Single();
                            TestAssert.True(capture.IsDescendant(librarySearch, serverSearch), "library search span nests under the server span");
                            Activity topk = capture.Spans("stage:topk").Single();
                            TestAssert.Equal(topk.SpanId, librarySearch.ParentSpanId, "library span is a child of the topk stage");

                            TestAssert.False(capture.Measurements(ServerTelemetryNames.Operations).Any(m => m.Tags.ContainsKey(ServerTelemetryNames.AttributeIndexName)), "no index name on metric labels");
                        }
                        finally
                        {
                            TryDeleteDir(dir);
                        }
                    }),

                    Case(suite, "ServiceFailuresRecordErrors", "Missing index and bad input record error outcomes with error.type", async ct =>
                    {
                        string dir = NewTempDir();
                        try
                        {
                            using TelemetryCapture capture = new TelemetryCapture();
                            await using IndexManager manager = new IndexManager(RamSettings(dir));
                            await TestAssert.ThrowsAsync<InvalidOperationException>(
                                () => manager.SearchAsync("missing", new SearchRequest { Vector = Vector(1) }, ct), "missing index").ConfigureAwait(false);
                            await manager.CreateIndexAsync(new CreateIndexRequest { Name = "idx", Dimension = _Dimension, StorageType = "RAM" }, ct).ConfigureAwait(false);
                            await TestAssert.ThrowsAsync<VectorDimensionMismatchException>(
                                () => manager.AddVectorAsync("idx", new AddVectorRequest { Vector = new List<float> { 1 } }, ct), "bad dimension").ConfigureAwait(false);
                            await TestAssert.ThrowsAsync<ArgumentException>(
                                () => manager.CreateIndexAsync(new CreateIndexRequest { Name = "bad", Dimension = _Dimension, StorageType = "Nope" }, ct), "bad storage type").ConfigureAwait(false);

                            TestAssert.True(capture.Measurements(ServerTelemetryNames.Operations,
                                m => m.Has(ServerTelemetryNames.LabelOperation, "search") && m.Has(ServerTelemetryNames.LabelOutcome, "error") && m.Has("error.type", "System.InvalidOperationException")).Any(), "search error");
                            TestAssert.True(capture.Measurements(ServerTelemetryNames.Operations,
                                m => m.Has(ServerTelemetryNames.LabelOperation, "vector.add") && m.Has(ServerTelemetryNames.LabelOutcome, "error") && m.Has(ServerTelemetryNames.LabelStorageType, "ram") && m.Has("error.type", "HnswIndex.Server.Classes.VectorDimensionMismatchException")).Any(), "add error");
                            TestAssert.True(capture.Measurements(ServerTelemetryNames.Operations,
                                m => m.Has(ServerTelemetryNames.LabelOperation, "index.create") && m.Has(ServerTelemetryNames.LabelOutcome, "error") && m.Has(ServerTelemetryNames.LabelStorageType, "unknown")).Any(), "create error with bounded storage label");
                            TestAssert.True(capture.Measurements(ServerTelemetryNames.StageDuration,
                                m => m.Has(ServerTelemetryNames.LabelStage, ServerTelemetryNames.StageStorageOpen) && m.Has(ServerTelemetryNames.LabelOutcome, "error")).Any(), "failed stage recorded");
                            Activity failed = capture.Spans("hnswlite search").Single();
                            TestAssert.Equal(ActivityStatusCode.Error, failed.Status, "server span marked error");
                            TestAssert.True(failed.Events.Any(e => e.Name == "exception"), "exception event");
                        }
                        finally
                        {
                            TryDeleteDir(dir);
                        }
                    }),

                    Case(suite, "ReloadJobEmitsJobStageAndIndexTelemetry", "Startup reload emits a job span, stage spans, job/stage/index counters, and the last-success gauge", async ct =>
                    {
                        string dir = NewTempDir();
                        try
                        {
                            await using (IndexManager seed = new IndexManager(RamSettings(dir)))
                            {
                                await seed.CreateIndexAsync(new CreateIndexRequest { Name = "persisted", Dimension = _Dimension, StorageType = "SQLite" }, ct).ConfigureAwait(false);
                                await seed.AddVectorAsync("persisted", new AddVectorRequest { Vector = Vector(1) }, ct).ConfigureAwait(false);
                            }

                            File.WriteAllText(Path.Combine(dir, "corrupt.db"), "not a sqlite database");

                            using TelemetryCapture capture = new TelemetryCapture();
                            await using IndexManager manager = new IndexManager(RamSettings(dir));
                            await manager.ReloadPersistedIndexesAsync(ct).ConfigureAwait(false);
                            capture.CollectObservables();

                            TestAssert.True(capture.Measurements(ServerTelemetryNames.ReloadJobs, m => m.Has(ServerTelemetryNames.LabelOutcome, "success")).Any(), "reload job counted");
                            TestAssert.True(capture.Measurements(ServerTelemetryNames.ReloadDuration).Any(), "reload duration");
                            TestAssert.True(capture.Measurements(ServerTelemetryNames.ReloadStages, m => m.Has(ServerTelemetryNames.LabelStage, "sqlite")).Any(), "sqlite stage counted");
                            TestAssert.True(capture.Measurements(ServerTelemetryNames.ReloadStageDuration, m => m.Has(ServerTelemetryNames.LabelStage, "sqlite")).Any(), "sqlite stage duration");
                            TestAssert.True(capture.Measurements(ServerTelemetryNames.ReloadIndexes, m => m.Has(ServerTelemetryNames.LabelReloadResult, "loaded") && m.Has(ServerTelemetryNames.LabelStorageType, "sqlite")).Any(), "loaded index counted");
                            TestAssert.True(capture.Measurements(ServerTelemetryNames.ReloadIndexes, m => m.Has(ServerTelemetryNames.LabelReloadResult, "failed")).Any(), "corrupt index counted as failed");
                            TestAssert.True(capture.Measurements(ServerTelemetryNames.ReloadLastSuccess).Any(m => m.Value > 0), "last success timestamp set");

                            Activity job = capture.AllSpans(ServerTelemetryNames.ReloadJobSpanName).Last();
                            Activity stage = capture.AllSpans("stage:sqlite").Last();
                            TestAssert.Equal(job.SpanId, stage.ParentSpanId, "stage span is a child of the job span");
                            TestAssert.Equal(ActivityStatusCode.Ok, job.Status, "job span ok");
                        }
                        finally
                        {
                            TryDeleteDir(dir);
                        }
                    }),

                    Case(suite, "GaugesReportInventoryBuildAndConfig", "Inventory, build-info, and config-info gauges report current state without secrets", async ct =>
                    {
                        string dir = NewTempDir();
                        Func<IEnumerable<StorageInventory>>? previous = ServerTelemetry.InventoryProvider;
                        try
                        {
                            using TelemetryCapture capture = new TelemetryCapture();
                            await using IndexManager manager = new IndexManager(RamSettings(dir));
                            await manager.CreateIndexAsync(new CreateIndexRequest { Name = "a", Dimension = _Dimension, StorageType = "RAM" }, ct).ConfigureAwait(false);
                            await manager.AddVectorAsync("a", new AddVectorRequest { Vector = Vector(1) }, ct).ConfigureAwait(false);
                            await manager.AddVectorAsync("a", new AddVectorRequest { Vector = Vector(2) }, ct).ConfigureAwait(false);
                            ServerTelemetry.InventoryProvider = () => manager.GetInventory();

                            HnswIndexSettings settings = new HnswIndexSettings();
                            settings.Server.AdminApiKey = "secret-key-value";
                            settings.Storage.DefaultStorageType = "SQLite";
                            ServerTelemetry.SetConfiguration(settings);
                            capture.CollectObservables();

                            TestAssert.Equal(1.0, capture.Measurements(ServerTelemetryNames.Indexes, m => m.Has(ServerTelemetryNames.LabelStorageType, "ram")).Last().Value, "index gauge");
                            TestAssert.Equal(2.0, capture.Measurements(ServerTelemetryNames.Vectors, m => m.Has(ServerTelemetryNames.LabelStorageType, "ram")).Last().Value, "vector gauge");
                            CapturedMeasurement build = capture.Measurements(ServerTelemetryNames.BuildInfo).Last();
                            TestAssert.True(build.Tags.ContainsKey(ServerTelemetryNames.LabelVersion), "build version label");
                            CapturedMeasurement config = capture.Measurements(ServerTelemetryNames.ConfigInfo).Last();
                            TestAssert.True(config.Has(ServerTelemetryNames.LabelConfigDefaultStorageType, "sqlite"), "config default storage");
                            TestAssert.False(config.Tags.Values.Any(v => v != null && v.Contains("secret-key-value")), "no secrets in config labels");
                        }
                        finally
                        {
                            ServerTelemetry.InventoryProvider = previous;
                            TryDeleteDir(dir);
                        }
                    }),

                    Case(suite, "AuthAndApiErrorCounters", "Auth decisions and API error codes are counted with bounded labels", ct =>
                    {
                        using TelemetryCapture capture = new TelemetryCapture();
                        ServerTelemetry.RecordAuth(ServerTelemetryNames.AuthSuccess);
                        ServerTelemetry.RecordAuth(ServerTelemetryNames.AuthInvalidKey);
                        ServerTelemetry.RecordAuth(ServerTelemetryNames.AuthMissingKey);
                        ServerTelemetry.RecordApiError(ApiErrorEnum.IndexNotFound);
                        ServerTelemetry.RecordApiError(ApiErrorEnum.InternalServerError);

                        TestAssert.True(capture.Measurements(ServerTelemetryNames.AuthRequests, m => m.Has(ServerTelemetryNames.LabelAuthResult, "invalid_key")).Any(), "invalid key counted");
                        TestAssert.True(capture.Measurements(ServerTelemetryNames.AuthRequests, m => m.Has(ServerTelemetryNames.LabelAuthResult, "missing_key")).Any(), "missing key counted");
                        TestAssert.True(capture.Measurements(ServerTelemetryNames.ApiErrors, m => m.Has(ServerTelemetryNames.LabelApiError, "IndexNotFound")).Any(), "api error counted");
                        TestAssert.Equal("InternalServerError", capture.Root!.GetTagItem(ServerTelemetryNames.LabelApiError)?.ToString(), "api error tagged on the current span");
                        return Task.CompletedTask;
                    }),

                    Case(suite, "TelemetryHostSubscribesAndServesPrometheus", "The Radiant host subscribes to every source and serves HnswLite metrics on the scrape endpoint", async ct =>
                    {
                        TelemetrySettings settings = new TelemetrySettings
                        {
                            OtlpEnable = false,
                            PrometheusEnable = true,
                            PrometheusHostname = "127.0.0.1",
                            PrometheusPort = FreePort(),
                        };

                        RadiantSettings radiant = TelemetryHost.BuildRadiantSettings(settings);
                        foreach (string name in new string[] { "Watson", "HnswLite", "HnswLite.Server", "Npgsql" })
                        {
                            TestAssert.True(radiant.Sources.MeterNames.Contains(name), "meter subscribed: " + name);
                        }

                        foreach (string name in new string[] { "Watson", "HnswLite", "HnswLite.Server" })
                        {
                            TestAssert.True(radiant.Sources.ActivitySourceNames.Contains(name), "activity source subscribed: " + name);
                        }

                        TestAssert.False(radiant.Sources.ActivitySourceNames.Contains("Npgsql"), "per-command Npgsql spans off by default");
                        TestAssert.True(radiant.Metrics.Definitions.Any(d => d.Key == HnswTelemetryNames.IndexOperationDuration && d.Buckets != null && d.Buckets.Length > 5), "duration buckets registered");

                        using (TelemetryHost host = TelemetryHost.Start(settings))
                        {
                            TestAssert.True(host.IsEnabled, "host started");
                            RamStorageProvider provider = new RamStorageProvider();
                            IndexType index = new IndexType(_Dimension, provider, 1);
                            await index.AddAsync(Guid.NewGuid(), Vector(1), ct).ConfigureAwait(false);
                            ServerTelemetry.RecordAuth(ServerTelemetryNames.AuthSuccess);

                            using HttpClient client = new HttpClient();
                            string body = await client.GetStringAsync(host.PrometheusScrapeUrl, ct).ConfigureAwait(false);
                            TestAssert.True(body.Contains("hnswlite_index_operations_total"), "library counter exported");
                            TestAssert.True(body.Contains("hnswlite_index_operation_duration_seconds_bucket"), "library histogram exported");
                            TestAssert.True(body.Contains("hnswlite_server_auth_requests_total"), "server counter exported");
                            TestAssert.True(body.Contains("le=\"0.0001\""), "seconds-scale buckets applied");
                            await provider.DisposeAsync().ConfigureAwait(false);
                        }

                        using (TelemetryHost inert = TelemetryHost.Start(new TelemetrySettings { Enable = false }))
                        {
                            TestAssert.False(inert.IsEnabled, "disabled settings produce an inert host");
                        }

                        int busyPort = FreePort();
                        TcpListener blocker = new TcpListener(IPAddress.Loopback, busyPort);
                        blocker.Start();
                        try
                        {
                            // The port is taken: starting must not throw; the host is either inert or degraded.
                            using TelemetryHost failed = TelemetryHost.Start(new TelemetrySettings { OtlpEnable = false, PrometheusHostname = "127.0.0.1", PrometheusPort = busyPort });
                            TestAssert.True(failed != null, "start failure never throws");
                        }
                        finally
                        {
                            blocker.Stop();
                        }
                    }),
                });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string suiteId, string caseId, string display, Func<CancellationToken, Task> exec)
        {
            return new TestCaseDescriptor(suiteId: suiteId, caseId: caseId, displayName: display, executeAsync: exec);
        }

        private static void AssertOperation(TelemetryCapture capture, string operation, string outcome, int minimum)
        {
            double count = capture.Sum(HnswTelemetryNames.IndexOperations,
                m => m.Has(HnswTelemetryNames.LabelOperation, operation) && m.Has(HnswTelemetryNames.LabelOutcome, outcome));
            TestAssert.True(count >= minimum, "operation " + operation + "/" + outcome + " counted at least " + minimum + " (was " + count + ")");
            TestAssert.True(capture.Measurements(HnswTelemetryNames.IndexOperationDuration,
                m => m.Has(HnswTelemetryNames.LabelOperation, operation) && m.Has(HnswTelemetryNames.LabelOutcome, outcome)).Any(), "duration recorded for " + operation);
        }

        private static List<float> Vector(int seed)
        {
            Random random = new Random(seed * 7919 + 17);
            List<float> v = new List<float>(_Dimension);
            for (int i = 0; i < _Dimension; i++) v.Add((float)random.NextDouble());
            return v;
        }

        private static Dictionary<Guid, List<float>> Batch(int count, int offset)
        {
            Dictionary<Guid, List<float>> batch = new Dictionary<Guid, List<float>>();
            for (int i = 0; i < count; i++) batch[Guid.NewGuid()] = Vector(offset + i + 1000);
            return batch;
        }

        private static StorageSettings RamSettings(string sqliteDirectory)
        {
            return new StorageSettings
            {
                DefaultStorageType = "RAM",
                SqliteDirectory = sqliteDirectory,
                PostgresqlConnectionString = string.Empty,
                PostgresqlAutoProvision = false,
            };
        }

        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "hnswlite-telemetry-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void TryDeleteDir(string dir)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch (Exception)
            {
                // best-effort cleanup
            }
        }

        private static int FreePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        #endregion
    }
}
