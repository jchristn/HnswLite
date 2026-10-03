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
                    PublicSurfaceSuites.ModelValidationSuite(),
                    PublicSurfaceSuites.UtilitySuite(),
                    PublicSurfaceSuites.IndexContractSuite(),
                    PublicSurfaceSuites.StorageProviderContractSuite(),
                    RamBasicSuite(),
                    RamAdvancedSuite(),
                    RamValidationSuite(),
                    RamStateSuite(),
                    SqliteBasicSuite(),
                    SqlitePersistenceSuite(),
                };
                all.AddRange(HnswExtendedSuites.All);
                all.AddRange(MetadataFilterSuites.All);
                all.AddRange(TelemetrySuites.All);
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
                            TestAssert.Throws<ArgumentException>(
                                () => new CosineDistance().Distance(
                                    new List<float> { 1f, 2f },
                                    new List<float> { 1f, 2f, 3f }),
                                "Cosine dimension mismatch");
                            TestAssert.Throws<ArgumentException>(
                                () => new DotProductDistance().Distance(
                                    new List<float> { 1f, 2f },
                                    new List<float> { 1f, 2f, 3f }),
                                "DotProduct dimension mismatch");
                            return Task.CompletedTask;
                        }),
                    new TestCaseDescriptor(
                        suiteId: "Distance",
                        caseId: "DistanceFunctionsRejectNull",
                        displayName: "Distance functions throw ArgumentNullException on null input",
                        executeAsync: ct =>
                        {
                            List<float> valid = new List<float> { 1f, 2f };
                            TestAssert.Throws<ArgumentNullException>(
                                () => new EuclideanDistance().Distance(null!, valid), "Euclidean null a");
                            TestAssert.Throws<ArgumentNullException>(
                                () => new EuclideanDistance().Distance(valid, null!), "Euclidean null b");
                            TestAssert.Throws<ArgumentNullException>(
                                () => new CosineDistance().Distance(null!, valid), "Cosine null a");
                            TestAssert.Throws<ArgumentNullException>(
                                () => new DotProductDistance().Distance(valid, null!), "DotProduct null b");
                            return Task.CompletedTask;
                        }),
                    new TestCaseDescriptor(
                        suiteId: "Distance",
                        caseId: "CosineZeroVectorReturnsOne",
                        displayName: "Cosine distance with a zero-magnitude vector returns 1 (no divide-by-zero)",
                        executeAsync: ct =>
                        {
                            CosineDistance fn = new CosineDistance();
                            // Zero magnitude on either side is undefined cosine similarity; the
                            // implementation defines this as distance 1 rather than NaN.
                            float zeroLeft = fn.Distance(
                                new List<float> { 0f, 0f, 0f },
                                new List<float> { 1f, 2f, 3f });
                            float zeroRight = fn.Distance(
                                new List<float> { 1f, 2f, 3f },
                                new List<float> { 0f, 0f, 0f });
                            float bothZero = fn.Distance(
                                new List<float> { 0f, 0f, 0f },
                                new List<float> { 0f, 0f, 0f });
                            TestAssert.NearEqual(1f, zeroLeft, 1e-6f, "Zero left operand");
                            TestAssert.NearEqual(1f, zeroRight, 1e-6f, "Zero right operand");
                            TestAssert.NearEqual(1f, bothZero, 1e-6f, "Both operands zero");
                            TestAssert.False(float.IsNaN(zeroLeft), "Result must not be NaN");
                            return Task.CompletedTask;
                        }),
                    new TestCaseDescriptor(
                        suiteId: "Distance",
                        caseId: "SimdMatchesScalarHighDimensional",
                        displayName: "SIMD-accelerated paths agree with a scalar reference over 67-d vectors",
                        executeAsync: ct =>
                        {
                            // 67 is prime, so the vectorized loop plus the scalar remainder loop are
                            // both exercised regardless of the hardware SIMD width. This guards the
                            // System.Numerics paths across the runtime update.
                            const int dim = 67;
                            Random rng = new Random(20260815);
                            List<float> a = new List<float>(dim);
                            List<float> b = new List<float>(dim);
                            for (int i = 0; i < dim; i++)
                            {
                                a.Add((float)(rng.NextDouble() * 4.0 - 2.0));
                                b.Add((float)(rng.NextDouble() * 4.0 - 2.0));
                            }

                            float refDot = 0f;
                            float refNormA = 0f;
                            float refNormB = 0f;
                            float refSquared = 0f;
                            for (int i = 0; i < dim; i++)
                            {
                                float diff = a[i] - b[i];
                                refSquared += diff * diff;
                                refDot += a[i] * b[i];
                                refNormA += a[i] * a[i];
                                refNormB += b[i] * b[i];
                            }
                            float expectedEuclidean = (float)Math.Sqrt(refSquared);
                            float expectedCosine = 1f - refDot / ((float)Math.Sqrt(refNormA) * (float)Math.Sqrt(refNormB));
                            float expectedDot = -refDot;

                            TestAssert.NearEqual(expectedEuclidean, new EuclideanDistance().Distance(a, b), 1e-3f, "Euclidean SIMD vs scalar");
                            TestAssert.NearEqual(expectedCosine, new CosineDistance().Distance(a, b), 1e-4f, "Cosine SIMD vs scalar");
                            TestAssert.NearEqual(expectedDot, new DotProductDistance().Distance(a, b), 1e-3f, "DotProduct SIMD vs scalar");
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
                            await using TestIndexScope scope = await NewIndexAsync(_Dimension, ct).ConfigureAwait(false);
                            HnswIndex index = scope.Index;
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
                            await using TestIndexScope scope = await NewIndexAsync(_Dimension, ct).ConfigureAwait(false);
                            HnswIndex index = scope.Index;
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
                            await using TestIndexScope scope = await NewIndexAsync(_Dimension, ct).ConfigureAwait(false);
                            HnswIndex index = scope.Index;
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
                            await using TestIndexScope scope = await NewIndexAsync(_Dimension, ct).ConfigureAwait(false);
                            HnswIndex index = scope.Index;
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
                            await using TestIndexScope scope = await NewIndexAsync(_Dimension, ct).ConfigureAwait(false);
                            HnswIndex index = scope.Index;
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
                            await using TestIndexScope scope = await NewIndexAsync(_HighDimension, ct).ConfigureAwait(false);
                            HnswIndex index = scope.Index;
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
                            await using TestIndexScope scope = await NewIndexAsync(_Dimension, ct).ConfigureAwait(false);
                            HnswIndex index = scope.Index;
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
                            await using TestIndexScope scope = await NewIndexAsync(_Dimension, ct).ConfigureAwait(false);
                            HnswIndex index = scope.Index;
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
                            await using TestIndexScope scope = await NewIndexAsync(3, ct).ConfigureAwait(false);
                            HnswIndex index = scope.Index;
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
                            await using TestIndexScope scope = await NewIndexAsync(_Dimension, ct).ConfigureAwait(false);
                            HnswIndex index = scope.Index;
                            await TestAssert.ThrowsAsync<ArgumentNullException>(
                                () => index.AddAsync(Guid.NewGuid(), null!, ct),
                                "Null vector Add").ConfigureAwait(false);
                        }),
                    new TestCaseDescriptor(
                        suiteId: "Ram.Validation",
                        caseId: "NegativeMRejected",
                        displayName: "Setting M to a negative value throws",
                        executeAsync: async ct =>
                        {
                            await using TestIndexScope scope = await NewIndexAsync(_Dimension, ct).ConfigureAwait(false);
                            HnswIndex index = scope.Index;
                            TestAssert.Throws<ArgumentOutOfRangeException>(
                                () => index.M = -1,
                                "Negative M");
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
                            await using TestIndexScope scope = await NewIndexAsync(_HighDimension, ct).ConfigureAwait(false);
                            HnswIndex index = scope.Index;
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
                            await using TestIndexScope originalScope = await NewIndexAsync(_Dimension, ct).ConfigureAwait(false);
                            HnswIndex original = originalScope.Index;
                            original.M = 8;
                            original.MaxM = 12;
                            original.DistanceFunction = new CosineDistance();

                            Dictionary<Guid, List<float>> vectors = new Dictionary<Guid, List<float>>();
                            for (int i = 0; i < 10; i++)
                                vectors[Guid.NewGuid()] = new List<float> { i * 0.1f, i * 0.2f };
                            await original.AddNodesAsync(vectors, ct).ConfigureAwait(false);

                            HnswState state = await original.ExportStateAsync(ct).ConfigureAwait(false);
                            await using TestIndexScope importedScope = await NewIndexAsync(_Dimension, ct).ConfigureAwait(false);
                            HnswIndex imported = importedScope.Index;
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
                            await using TestIndexScope scope = await NewIndexAsync(_Dimension, ct, TestStorageKind.Sqlite).ConfigureAwait(false);
                            HnswIndex index = scope.Index;
                            await index.AddNodesAsync(new Dictionary<Guid, List<float>>
                            {
                                { Guid.NewGuid(), new List<float> { 1f, 1f } },
                                { Guid.NewGuid(), new List<float> { 2f, 2f } },
                                { Guid.NewGuid(), new List<float> { 10f, 10f } },
                            }, ct).ConfigureAwait(false);

                            List<VectorResult> results = (await index.GetTopKAsync(
                                new List<float> { 1f, 1f }, 2, cancellationToken: ct).ConfigureAwait(false)).ToList();

                            TestAssert.Equal(2, results.Count, "SQLite top-2 count");
                        }),

                    new TestCaseDescriptor(
                        suiteId: "Sqlite.Basic",
                        caseId: "RemoveVector",
                        displayName: "SQLite remove excludes the removed GUID from results",
                        executeAsync: async ct =>
                        {
                            await using TestIndexScope scope = await NewIndexAsync(_Dimension, ct, TestStorageKind.Sqlite).ConfigureAwait(false);
                            HnswIndex index = scope.Index;
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
                            TestIndexScope? writer = null;
                            Guid expected = Guid.NewGuid();
                            try
                            {
                                writer = await NewIndexAsync(_Dimension, ct, TestStorageKind.Sqlite, cleanupLocationOnDispose: false).ConfigureAwait(false);
                                HnswIndex index = writer.Index;
                                await index.AddNodesAsync(new Dictionary<Guid, List<float>>
                                {
                                    { expected, new List<float> { 1f, 1f } },
                                    { Guid.NewGuid(), new List<float> { 9f, 9f } },
                                }, ct).ConfigureAwait(false);

                                await writer.DisposeAsync().ConfigureAwait(false);

                                await using TestIndexScope reader = await writer.ReopenAsync(ct).ConfigureAwait(false);
                                HnswIndex reopened = reader.Index;
                                List<VectorResult> results = (await reopened.GetTopKAsync(
                                    new List<float> { 1f, 1f }, 1, cancellationToken: ct).ConfigureAwait(false)).ToList();

                                TestAssert.Equal(1, results.Count, "Persisted top-1 count");
                                TestAssert.Equal(expected, results[0].GUID, "Persisted GUID");
                            }
                            finally
                            {
                                if (writer != null)
                                {
                                    await writer.CleanupLocationAsync(ct).ConfigureAwait(false);
                                }
                            }
                        }),

                    new TestCaseDescriptor(
                        suiteId: "Sqlite.Persistence",
                        caseId: "NeighborMutationIsWriteThrough",
                        displayName: "SQLite neighbor mutations persist without a provider flush",
                        executeAsync: async ct =>
                        {
                            Guid a = Guid.NewGuid();
                            Guid b = Guid.NewGuid();
                            TestIndexScope? writer = null;
                            TestIndexScope? reader = null;
                            try
                            {
                                writer = await NewIndexAsync(_Dimension, ct, TestStorageKind.Sqlite, cleanupLocationOnDispose: false).ConfigureAwait(false);
                                await writer.Provider.AddNodesAsync(new Dictionary<Guid, List<float>>
                                {
                                    { a, new List<float> { 1f, 1f } },
                                    { b, new List<float> { 2f, 2f } },
                                }, ct).ConfigureAwait(false);

                                IHnswNode node = await writer.Provider.GetNodeAsync(a, ct).ConfigureAwait(false);
                                await node.AddNeighborAsync(0, b, ct).ConfigureAwait(false);

                                reader = await writer.ReopenAsync(ct).ConfigureAwait(false);
                                IHnswNode reloaded = await reader.Provider.GetNodeAsync(a, ct).ConfigureAwait(false);
                                Dictionary<int, HashSet<Guid>> neighbors = await reloaded.GetNeighborsAsync(ct).ConfigureAwait(false);

                                TestAssert.True(neighbors.TryGetValue(0, out HashSet<Guid>? layerZero), "Layer zero neighbors exist");
                                TestAssert.True(layerZero!.Contains(b), "Write-through neighbor persisted without FlushAsync");
                            }
                            finally
                            {
                                if (reader != null) await reader.DisposeAsync().ConfigureAwait(false);
                                if (writer != null) await writer.DisposeAsync().ConfigureAwait(false);
                                if (writer != null) await writer.CleanupLocationAsync(ct).ConfigureAwait(false);
                            }
                        }),
                });
        }

        #endregion

        #region Private-Methods

        private static Task<TestIndexScope> NewIndexAsync(
            int dimension,
            CancellationToken cancellationToken,
            TestStorageKind defaultKind = TestStorageKind.Ram,
            int? seed = null,
            bool cleanupLocationOnDispose = true)
        {
            return TestIndexScope.CreateAsync(
                dimension,
                cancellationToken,
                seed,
                defaultKind,
                cleanupLocationOnDispose);
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
