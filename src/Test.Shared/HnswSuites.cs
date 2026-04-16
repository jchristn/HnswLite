namespace HnswLite.Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    using Hnsw;
    using Hnsw.RamStorage;
    using Hnsw.SqliteStorage;
    using HnswIndex.SqliteStorage;
    using Hsnw;
    using Microsoft.Data.Sqlite;

    // Alias the new unified providers
    using RamProvider = Hnsw.RamStorage.RamStorageProvider;
    using SqliteProvider = HnswIndex.SqliteStorage.SqliteStorageProvider;
    using Touchstone.Core;

    /// <summary>
    /// Shared Touchstone test suites for HnswLite. All test logic lives here and is executed
    /// identically by Test.Automated (console), Test.XUnit, Test.NUnit, and Test.MSTest.
    /// </summary>
    public static class HnswSuites
    {
        #region Public-Members

        /// <summary>
        /// All test suites. Adapters enumerate this property to expose tests to their runner.
        /// </summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get
            {
                List<TestSuiteDescriptor> all = new List<TestSuiteDescriptor>
                {
                    DistanceFunctionSuite(),
                    RamBasicSuite(),
                    RamAdvancedSuite(),
                    RamValidationSuite(),
                    RamStateSuite(),
                    SqliteBasicSuite(),
                    SqlitePersistenceSuite(),
                };
                all.AddRange(HnswExtendedSuites.All);
                return all;
            }
        }

        #endregion

        #region Private-Members

        private const int _Dimension = 2;
        private const int _HighDimension = 64;
        private const int _BatchSize = 20;
        private const int _DuplicateCount = 5;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Distance-function correctness tests. No storage involved.
        /// </summary>
        /// <returns>A suite of distance-function correctness tests.</returns>
        public static TestSuiteDescriptor DistanceFunctionSuite()
        {
            return new TestSuiteDescriptor(
                suiteId: "Distance",
                displayName: "Distance Functions",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        suiteId: "Distance",
                        caseId: "EuclideanIdenticalVectorsZero",
                        displayName: "Euclidean distance between identical vectors is zero",
                        executeAsync: ct =>
                        {
                            EuclideanDistance fn = new EuclideanDistance();
                            List<float> a = new List<float> { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f };
                            float d = fn.Distance(a, new List<float>(a));
                            TestAssert.NearEqual(0f, d, 1e-6f, "Identical Euclidean distance");
                            return Task.CompletedTask;
                        }),
                    new TestCaseDescriptor(
                        suiteId: "Distance",
                        caseId: "EuclideanKnownValue",
                        displayName: "Euclidean distance over 9-d differs from scalar reference by <1e-4",
                        executeAsync: ct =>
                        {
                            EuclideanDistance fn = new EuclideanDistance();
                            List<float> a = new List<float> { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f };
                            List<float> b = new List<float> { 9f, 8f, 7f, 6f, 5f, 4f, 3f, 2f, 1f };
                            float d = fn.Distance(a, b);

                            float expected = 0f;
                            for (int i = 0; i < a.Count; i++)
                            {
                                float diff = a[i] - b[i];
                                expected += diff * diff;
                            }
                            expected = (float)Math.Sqrt(expected);

                            TestAssert.NearEqual(expected, d, 1e-4f, "Euclidean result");
                            return Task.CompletedTask;
                        }),
                    new TestCaseDescriptor(
                        suiteId: "Distance",
                        caseId: "CosineOrthogonalIsOne",
                        displayName: "Cosine distance between orthogonal unit vectors is 1",
                        executeAsync: ct =>
                        {
                            CosineDistance fn = new CosineDistance();
                            float d = fn.Distance(
                                new List<float> { 1f, 0f, 0f },
                                new List<float> { 0f, 1f, 0f });
                            TestAssert.NearEqual(1f, d, 1e-5f, "Cosine orthogonal");
                            return Task.CompletedTask;
                        }),
                    new TestCaseDescriptor(
                        suiteId: "Distance",
                        caseId: "CosineIdenticalIsZero",
                        displayName: "Cosine distance between identical direction is 0",
                        executeAsync: ct =>
                        {
                            CosineDistance fn = new CosineDistance();
                            float d = fn.Distance(
                                new List<float> { 2f, 0f, 0f },
                                new List<float> { 1f, 0f, 0f });
                            TestAssert.NearEqual(0f, d, 1e-5f, "Cosine same direction");
                            return Task.CompletedTask;
                        }),
                    new TestCaseDescriptor(
                        suiteId: "Distance",
                        caseId: "DotProductSign",
                        displayName: "Dot-product distance negates the dot product",
                        executeAsync: ct =>
                        {
                            DotProductDistance fn = new DotProductDistance();
                            float d = fn.Distance(
                                new List<float> { 1f, 2f, 3f },
                                new List<float> { 4f, 5f, 6f });
                            TestAssert.NearEqual(-(1f * 4f + 2f * 5f + 3f * 6f), d, 1e-5f, "Dot product negated");
                            return Task.CompletedTask;
                        }),
                    new TestCaseDescriptor(
                        suiteId: "Distance",
                        caseId: "DimensionMismatchThrows",
                        displayName: "Distance functions throw ArgumentException on dimension mismatch",
                        executeAsync: ct =>
                        {
                            TestAssert.Throws<ArgumentException>(
                                () => new EuclideanDistance().Distance(
                                    new List<float> { 1f, 2f },
                                    new List<float> { 1f, 2f, 3f }),
                                "Euclidean dimension mismatch");
                            return Task.CompletedTask;
                        }),
                });
        }

        /// <summary>
        /// Core RAM storage functional tests.
        /// </summary>
        /// <returns>A suite of basic RAM storage tests.</returns>
        public static TestSuiteDescriptor RamBasicSuite()
        {
            return new TestSuiteDescriptor(
                suiteId: "Ram.Basic",
                displayName: "RAM Storage - Basic Operations",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        suiteId: "Ram.Basic",
                        caseId: "AddAndSearch",
                        displayName: "Add five vectors and retrieve nearest neighbors",
                        executeAsync: async ct =>
                        {
                            HnswIndex index = NewRamIndex(_Dimension);
                            Dictionary<Guid, List<float>> vectors = new Dictionary<Guid, List<float>>
                            {
                                { Guid.NewGuid(), new List<float> { 1f, 1f } },
                                { Guid.NewGuid(), new List<float> { 2f, 2f } },
                                { Guid.NewGuid(), new List<float> { 3f, 3f } },
                                { Guid.NewGuid(), new List<float> { 10f, 10f } },
                                { Guid.NewGuid(), new List<float> { 11f, 11f } }
                            };
                            await index.AddNodesAsync(vectors, ct).ConfigureAwait(false);

                            List<VectorResult> results = (await index.GetTopKAsync(
                                new List<float> { 1.5f, 1.5f }, 3, cancellationToken: ct).ConfigureAwait(false)).ToList();

                            TestAssert.Equal(3, results.Count, "Top-3 count");
                            List<float> nearest = results[0].Vectors;
                            TestAssert.True(
                                Math.Abs(nearest[0] - 1f) < 0.1f || Math.Abs(nearest[0] - 2f) < 0.1f,
                                "Nearest must be (1,1) or (2,2)");
                        }),

                    new TestCaseDescriptor(
                        suiteId: "Ram.Basic",
                        caseId: "Remove",
                        displayName: "Remove a vector and confirm it disappears from results",
                        executeAsync: async ct =>
                        {
                            HnswIndex index = NewRamIndex(_Dimension);
                            Guid id1 = Guid.NewGuid();
                            Guid id2 = Guid.NewGuid();
                            Guid id3 = Guid.NewGuid();
                            await index.AddNodesAsync(new Dictionary<Guid, List<float>>
                            {
                                { id1, new List<float> { 1f, 1f } },
                                { id2, new List<float> { 2f, 2f } },
                                { id3, new List<float> { 3f, 3f } },
                            }, ct).ConfigureAwait(false);

                            await index.RemoveAsync(id2, ct).ConfigureAwait(false);

                            List<VectorResult> results = (await index.GetTopKAsync(
                                new List<float> { 2f, 2f }, 3, cancellationToken: ct).ConfigureAwait(false)).ToList();

                            TestAssert.False(results.Any(r => r.GUID == id2), "Removed vector must not be returned");
                        }),

                    new TestCaseDescriptor(
                        suiteId: "Ram.Basic",
                        caseId: "EmptyIndex",
                        displayName: "Empty index returns no results",
                        executeAsync: async ct =>
                        {
                            HnswIndex index = NewRamIndex(_Dimension);
                            List<VectorResult> results = (await index.GetTopKAsync(
                                new List<float> { 1f, 1f }, 5, cancellationToken: ct).ConfigureAwait(false)).ToList();
                            TestAssert.Equal(0, results.Count, "Empty-index result count");
                        }),

                    new TestCaseDescriptor(
                        suiteId: "Ram.Basic",
                        caseId: "SingleElement",
                        displayName: "Single-element index returns that element",
                        executeAsync: async ct =>
                        {
                            HnswIndex index = NewRamIndex(_Dimension);
                            Guid id = Guid.NewGuid();
                            await index.AddAsync(id, new List<float> { 5f, 5f }, ct).ConfigureAwait(false);

                            List<VectorResult> results = (await index.GetTopKAsync(
                                new List<float> { 0f, 0f }, 1, cancellationToken: ct).ConfigureAwait(false)).ToList();

                            TestAssert.Equal(1, results.Count, "Single-element count");
                            TestAssert.Equal(id, results[0].GUID, "Returned GUID");
                        }),

                    new TestCaseDescriptor(
                        suiteId: "Ram.Basic",
                        caseId: "DuplicateVectors",
                        displayName: "Duplicate vectors all return with near-zero distance",
                        executeAsync: async ct =>
                        {
                            HnswIndex index = NewRamIndex(_Dimension);
                            Dictionary<Guid, List<float>> dupes = new Dictionary<Guid, List<float>>();
                            for (int i = 0; i < _DuplicateCount; i++)
                                dupes[Guid.NewGuid()] = new List<float> { 5f, 5f };
                            await index.AddNodesAsync(dupes, ct).ConfigureAwait(false);

                            List<VectorResult> results = (await index.GetTopKAsync(
                                new List<float> { 5f, 5f }, 10, cancellationToken: ct).ConfigureAwait(false)).ToList();

                            TestAssert.Equal(_DuplicateCount, results.Count, "Duplicate count returned");
                            TestAssert.True(results.All(r => Math.Abs(r.Distance) < 0.001f), "All distances ~0");
                        }),
                });
        }

        /// <summary>
        /// Advanced RAM storage tests: batch ops, high-dimensional, distance function selection.
        /// </summary>
        /// <returns>A suite of advanced RAM storage tests.</returns>
        public static TestSuiteDescriptor RamAdvancedSuite()
        {
            return new TestSuiteDescriptor(
                suiteId: "Ram.Advanced",
                displayName: "RAM Storage - Advanced",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        suiteId: "Ram.Advanced",
                        caseId: "HighDimensional",
                        displayName: "Index supports 64-dimensional vectors",
                        executeAsync: async ct =>
                        {
                            HnswIndex index = NewRamIndex(_HighDimension);
                            Random rng = new Random(42);
                            Dictionary<Guid, List<float>> vectors = new Dictionary<Guid, List<float>>();
                            for (int i = 0; i < 20; i++)
                                vectors[Guid.NewGuid()] = RandomVector(_HighDimension, rng);
                            await index.AddNodesAsync(vectors, ct).ConfigureAwait(false);

                            List<VectorResult> results = (await index.GetTopKAsync(
                                RandomVector(_HighDimension, rng), 5, cancellationToken: ct).ConfigureAwait(false)).ToList();

                            TestAssert.Equal(5, results.Count, "Top-5 in 64-d");
                        }),

                    new TestCaseDescriptor(
                        suiteId: "Ram.Advanced",
                        caseId: "BatchAddThenBatchRemove",
                        displayName: "Batch add 20 then remove 10 leaves 10 in results",
                        executeAsync: async ct =>
                        {
                            HnswIndex index = NewRamIndex(_Dimension);
                            Random rng = new Random(42);
                            List<Guid> ids = new List<Guid>();
                            Dictionary<Guid, List<float>> batch = new Dictionary<Guid, List<float>>();
                            for (int i = 0; i < _BatchSize; i++)
                            {
                                Guid id = Guid.NewGuid();
                                ids.Add(id);
                                batch[id] = new List<float>
                                {
                                    (float)rng.NextDouble() * 10f,
                                    (float)rng.NextDouble() * 10f,
                                };
                            }
                            await index.AddNodesAsync(batch, ct).ConfigureAwait(false);

                            await index.RemoveNodesAsync(ids.Take(10).ToList(), ct).ConfigureAwait(false);

                            List<VectorResult> results = (await index.GetTopKAsync(
                                new List<float> { 5f, 5f }, 15, cancellationToken: ct).ConfigureAwait(false)).ToList();

                            TestAssert.Equal(10, results.Count, "After removing 10 of 20");
                        }),

                    new TestCaseDescriptor(
                        suiteId: "Ram.Advanced",
                        caseId: "CosineDistanceMetricSelectable",
                        displayName: "Configuring CosineDistance returns nearest by cosine similarity",
                        executeAsync: async ct =>
                        {
                            HnswIndex index = NewRamIndex(_Dimension);
                            index.DistanceFunction = new CosineDistance();

                            await index.AddNodesAsync(new Dictionary<Guid, List<float>>
                            {
                                { Guid.NewGuid(), new List<float> { 1f, 0f } },
                                { Guid.NewGuid(), new List<float> { 0f, 1f } },
                                { Guid.NewGuid(), new List<float> { 0.707f, 0.707f } },
                                { Guid.NewGuid(), new List<float> { -1f, 0f } },
                            }, ct).ConfigureAwait(false);

                            List<VectorResult> results = (await index.GetTopKAsync(
                                new List<float> { 1f, 0f }, 1, cancellationToken: ct).ConfigureAwait(false)).ToList();

                            TestAssert.Equal(1, results.Count, "Cosine top-1 count");
                            TestAssert.NearEqual(1f, results[0].Vectors[0], 0.01f, "Nearest direction x");
                            TestAssert.NearEqual(0f, results[0].Vectors[1], 0.01f, "Nearest direction y");
                        }),
                });
        }

        /// <summary>
        /// Input validation tests (null, invalid dimensions, parameter bounds).
        /// </summary>
        /// <returns>A suite of input-validation tests.</returns>
        public static TestSuiteDescriptor RamValidationSuite()
        {
            return new TestSuiteDescriptor(
                suiteId: "Ram.Validation",
                displayName: "RAM Storage - Input Validation",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        suiteId: "Ram.Validation",
                        caseId: "WrongDimensionOnAdd",
                        displayName: "Add rejects vector with wrong dimension",
                        executeAsync: async ct =>
                        {
                            HnswIndex index = NewRamIndex(3);
                            await TestAssert.ThrowsAsync<ArgumentException>(
                                () => index.AddAsync(Guid.NewGuid(), new List<float> { 1f, 2f }, ct),
                                "Wrong-dimension Add").ConfigureAwait(false);
                        }),
                    new TestCaseDescriptor(
                        suiteId: "Ram.Validation",
                        caseId: "NullVectorRejected",
                        displayName: "Add rejects null vector",
                        executeAsync: async ct =>
                        {
                            HnswIndex index = NewRamIndex(_Dimension);
                            await TestAssert.ThrowsAsync<ArgumentNullException>(
                                () => index.AddAsync(Guid.NewGuid(), null!, ct),
                                "Null vector Add").ConfigureAwait(false);
                        }),
                    new TestCaseDescriptor(
                        suiteId: "Ram.Validation",
                        caseId: "NegativeMRejected",
                        displayName: "Setting M to a negative value throws",
                        executeAsync: ct =>
                        {
                            HnswIndex index = NewRamIndex(_Dimension);
                            TestAssert.Throws<ArgumentOutOfRangeException>(
                                () => index.M = -1,
                                "Negative M");
                            return Task.CompletedTask;
                        }),
                    new TestCaseDescriptor(
                        suiteId: "Ram.Validation",
                        caseId: "ZeroDimensionRejected",
                        displayName: "Constructing an index with dimension 0 throws",
                        executeAsync: ct =>
                        {
                            TestAssert.Throws<ArgumentOutOfRangeException>(
                                () => new HnswIndex(0, new RamHnswStorage(), new RamHnswLayerStorage()),
                                "Zero dimension");
                            return Task.CompletedTask;
                        }),
                    new TestCaseDescriptor(
                        suiteId: "Ram.Validation",
                        caseId: "CancelledTokenHonoured",
                        displayName: "Cancelled token causes OperationCanceledException",
                        executeAsync: async ct =>
                        {
                            HnswIndex index = NewRamIndex(_HighDimension);
                            using CancellationTokenSource cts = new CancellationTokenSource();
                            cts.Cancel();
                            await TestAssert.ThrowsAsync<OperationCanceledException>(
                                () => index.AddAsync(
                                    Guid.NewGuid(),
                                    Enumerable.Range(0, _HighDimension).Select(i => (float)i).ToList(),
                                    cts.Token),
                                "Cancelled Add").ConfigureAwait(false);
                        }),
                });
        }

        /// <summary>
        /// State export/import round-trip.
        /// </summary>
        /// <returns>A suite validating ExportState/ImportState fidelity.</returns>
        public static TestSuiteDescriptor RamStateSuite()
        {
            return new TestSuiteDescriptor(
                suiteId: "Ram.State",
                displayName: "RAM Storage - State Export/Import",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        suiteId: "Ram.State",
                        caseId: "RoundTrip",
                        displayName: "Export/import preserves results and parameters",
                        executeAsync: async ct =>
                        {
                            HnswIndex original = NewRamIndex(_Dimension);
                            original.M = 8;
                            original.MaxM = 12;
                            original.DistanceFunction = new CosineDistance();

                            Dictionary<Guid, List<float>> vectors = new Dictionary<Guid, List<float>>();
                            for (int i = 0; i < 10; i++)
                                vectors[Guid.NewGuid()] = new List<float> { i * 0.1f, i * 0.2f };
                            await original.AddNodesAsync(vectors, ct).ConfigureAwait(false);

                            HnswState state = await original.ExportStateAsync(ct).ConfigureAwait(false);
                            HnswIndex imported = NewRamIndex(_Dimension);
                            await imported.ImportStateAsync(state, ct).ConfigureAwait(false);

                            TestAssert.Equal(original.M, imported.M, "M preserved");
                            TestAssert.Equal(original.MaxM, imported.MaxM, "MaxM preserved");
                            TestAssert.Equal(original.DistanceFunction.Name, imported.DistanceFunction.Name, "Distance fn preserved");

                            List<float> query = new List<float> { 0.5f, 1f };
                            List<VectorResult> orig = (await original.GetTopKAsync(query, 3, cancellationToken: ct).ConfigureAwait(false)).ToList();
                            List<VectorResult> imp = (await imported.GetTopKAsync(query, 3, cancellationToken: ct).ConfigureAwait(false)).ToList();

                            TestAssert.Equal(orig.Count, imp.Count, "Result counts match");
                            for (int i = 0; i < orig.Count; i++)
                            {
                                TestAssert.Equal(orig[i].GUID, imp[i].GUID, $"GUID[{i}]");
                                TestAssert.NearEqual(orig[i].Distance, imp[i].Distance, 1e-4f, $"Distance[{i}]");
                            }
                        }),
                });
        }

        /// <summary>
        /// Basic SQLite storage functional test.
        /// </summary>
        /// <returns>A suite of basic SQLite storage tests.</returns>
        public static TestSuiteDescriptor SqliteBasicSuite()
        {
            return new TestSuiteDescriptor(
                suiteId: "Sqlite.Basic",
                displayName: "SQLite Storage - Basic Operations",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        suiteId: "Sqlite.Basic",
                        caseId: "AddAndSearch",
                        displayName: "SQLite index returns nearest neighbors",
                        executeAsync: async ct =>
                        {
                            string path = NewTempDb();
                            try
                            {
                                SqliteProvider provider = new SqliteProvider(path);
                                try
                                {
                                    HnswIndex index = new HnswIndex(_Dimension, provider);
                                    await index.AddNodesAsync(new Dictionary<Guid, List<float>>
                                    {
                                        { Guid.NewGuid(), new List<float> { 1f, 1f } },
                                        { Guid.NewGuid(), new List<float> { 2f, 2f } },
                                        { Guid.NewGuid(), new List<float> { 10f, 10f } },
                                    }, ct).ConfigureAwait(false);

                                    List<VectorResult> results = (await index.GetTopKAsync(
                                        new List<float> { 1f, 1f }, 2, cancellationToken: ct).ConfigureAwait(false)).ToList();

                                    TestAssert.Equal(2, results.Count, "SQLite top-2 count");
                                }
                                finally { provider.Dispose(); }
                            }
                            finally { TryDelete(path); }
                        }),

                    new TestCaseDescriptor(
                        suiteId: "Sqlite.Basic",
                        caseId: "RemoveVector",
                        displayName: "SQLite remove excludes the removed GUID from results",
                        executeAsync: async ct =>
                        {
                            string path = NewTempDb();
                            try
                            {
                                SqliteProvider provider = new SqliteProvider(path);
                                try
                                {
                                    HnswIndex index = new HnswIndex(_Dimension, provider);
                                    Guid keepA = Guid.NewGuid();
                                    Guid keepB = Guid.NewGuid();
                                    Guid drop = Guid.NewGuid();
                                    await index.AddNodesAsync(new Dictionary<Guid, List<float>>
                                    {
                                        { keepA, new List<float> { 1f, 1f } },
                                        { drop, new List<float> { 2f, 2f } },
                                        { keepB, new List<float> { 3f, 3f } },
                                    }, ct).ConfigureAwait(false);

                                    await index.RemoveAsync(drop, ct).ConfigureAwait(false);

                                    List<VectorResult> results = (await index.GetTopKAsync(
                                        new List<float> { 2f, 2f }, 5, cancellationToken: ct).ConfigureAwait(false)).ToList();

                                    TestAssert.False(results.Any(r => r.GUID == drop), "Dropped GUID absent");
                                }
                                finally { provider.Dispose(); }
                            }
                            finally { TryDelete(path); }
                        }),
                });
        }

        /// <summary>
        /// SQLite persistence test — data survives closing and reopening the database.
        /// </summary>
        /// <returns>A suite of SQLite persistence tests.</returns>
        public static TestSuiteDescriptor SqlitePersistenceSuite()
        {
            return new TestSuiteDescriptor(
                suiteId: "Sqlite.Persistence",
                displayName: "SQLite Storage - Persistence",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        suiteId: "Sqlite.Persistence",
                        caseId: "DataSurvivesClose",
                        displayName: "Data persists across close/reopen of the database",
                        executeAsync: async ct =>
                        {
                            string path = NewTempDb();
                            Guid expected = Guid.NewGuid();
                            try
                            {
                                SqliteProvider w = new SqliteProvider(path);
                                try
                                {
                                    HnswIndex index = new HnswIndex(_Dimension, w);
                                    await index.AddNodesAsync(new Dictionary<Guid, List<float>>
                                    {
                                        { expected, new List<float> { 1f, 1f } },
                                        { Guid.NewGuid(), new List<float> { 9f, 9f } },
                                    }, ct).ConfigureAwait(false);
                                }
                                finally { w.Dispose(); }

                                SqliteConnection.ClearAllPools();

                                SqliteProvider r = new SqliteProvider(path, createIfNotExists: false);
                                try
                                {
                                    HnswIndex index = new HnswIndex(_Dimension, r);
                                    List<VectorResult> results = (await index.GetTopKAsync(
                                        new List<float> { 1f, 1f }, 1, cancellationToken: ct).ConfigureAwait(false)).ToList();

                                    TestAssert.Equal(1, results.Count, "Persisted top-1 count");
                                    TestAssert.Equal(expected, results[0].GUID, "Persisted GUID");
                                }
                                finally { r.Dispose(); }
                            }
                            finally
                            {
                                SqliteConnection.ClearAllPools();
                                TryDelete(path);
                            }
                        }),
                });
        }

        #endregion

        #region Private-Methods

        private static HnswIndex NewRamIndex(int dimension)
        {
            return new HnswIndex(dimension, new RamProvider());
        }

        private static List<float> RandomVector(int dimension, Random rng)
        {
            List<float> v = new List<float>(dimension);
            for (int i = 0; i < dimension; i++)
                v.Add((float)(rng.NextDouble() * 2.0 - 1.0));
            return v;
        }

        private static string NewTempDb()
        {
            string dir = Path.Combine(Path.GetTempPath(), "hnswlite-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "index.db");
        }

        private static void TryDelete(string path)
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                if (File.Exists(path)) File.Delete(path);
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }

        #endregion
    }
}
