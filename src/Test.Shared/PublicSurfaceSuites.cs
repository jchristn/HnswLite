namespace HnswLite.Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hnsw;
    using Hnsw.RamStorage;
    using Hsnw;
    using Touchstone.Core;

    /// <summary>
    /// Focused tests for public models, helper types, and direct storage contracts.
    /// </summary>
    public static class PublicSurfaceSuites
    {
        /// <summary>
        /// All public-surface suites.
        /// </summary>
        public static IReadOnlyList<TestSuiteDescriptor> All =>
            new List<TestSuiteDescriptor>
            {
                ModelValidationSuite(),
                UtilitySuite(),
                IndexContractSuite(),
                StorageProviderContractSuite(),
            };

        /// <summary>
        /// Validates public model objects used for import/export and API results.
        /// </summary>
        public static TestSuiteDescriptor ModelValidationSuite()
        {
            return new TestSuiteDescriptor(
                suiteId: "PublicSurface.Models",
                displayName: "Public Surface - Models",
                cases: new List<TestCaseDescriptor>
                {
                    Case("PublicSurface.Models", "HnswParametersValidation",
                        "HnswParameters accepts valid boundaries and rejects inconsistent values",
                        ct =>
                        {
                            HnswParameters p = new HnswParameters
                            {
                                M = 2,
                                MaxM = 2,
                                EfConstruction = 2,
                                MaxLayers = 64,
                                LevelMultiplier = 2.0,
                                ExtendCandidates = true,
                                KeepPrunedConnections = true,
                                DistanceFunctionName = "Euclidean",
                            };
                            p.Validate();

                            TestAssert.Throws<ArgumentOutOfRangeException>(() => p.M = 1, "M lower bound");
                            TestAssert.Throws<ArgumentOutOfRangeException>(() => p.M = 101, "M upper bound");
                            TestAssert.Throws<ArgumentOutOfRangeException>(() => p.MaxM = 0, "MaxM lower bound");
                            TestAssert.Throws<ArgumentOutOfRangeException>(() => p.EfConstruction = 0, "EfConstruction lower bound");
                            TestAssert.Throws<ArgumentOutOfRangeException>(() => p.MaxLayers = 65, "MaxLayers upper bound");
                            TestAssert.Throws<ArgumentException>(() => p.LevelMultiplier = double.NaN, "LevelMultiplier NaN");
                            TestAssert.Throws<ArgumentException>(() => p.DistanceFunctionName = " ", "DistanceFunctionName blank");

                            HnswParameters inconsistentMax = new HnswParameters { M = 16, MaxM = 8 };
                            TestAssert.Throws<InvalidOperationException>(() => inconsistentMax.Validate(), "MaxM must be >= M");

                            HnswParameters inconsistentEf = new HnswParameters { M = 16, EfConstruction = 8 };
                            TestAssert.Throws<InvalidOperationException>(() => inconsistentEf.Validate(), "EfConstruction must be >= M");
                            return Task.CompletedTask;
                        }),

                    Case("PublicSurface.Models", "NodeStateValidationAndClone",
                        "NodeState validates IDs, vectors, layers, neighbors, and clone isolation",
                        ct =>
                        {
                            Guid id = Guid.NewGuid();
                            Guid neighbor = Guid.NewGuid();
                            NodeState node = new NodeState(
                                id,
                                new List<float> { 1f, 2f },
                                1,
                                new Dictionary<int, List<Guid>> { { 0, new List<Guid> { neighbor } } });
                            node.Validate();

                            List<Guid> neighbors = node.GetNeighborsAtLayer(0);
                            neighbors.Clear();
                            TestAssert.Equal(1, node.GetNeighborsAtLayer(0).Count, "GetNeighborsAtLayer returns a copy");

                            NodeState clone = node.Clone();
                            clone.Vector[0] = 99f;
                            clone.Neighbors[0].Clear();
                            TestAssert.Equal(1f, node.Vector[0], "Clone vector is independent");
                            TestAssert.Equal(1, node.Neighbors[0].Count, "Clone neighbors are independent");

                            TestAssert.Throws<ArgumentException>(() => new NodeState(Guid.Empty, new List<float> { 1f }), "Guid.Empty rejected");
                            TestAssert.Throws<ArgumentException>(() => node.Vector = new List<float> { float.NaN }, "NaN vector rejected");
                            TestAssert.Throws<ArgumentOutOfRangeException>(() => node.Layer = -1, "Negative layer rejected");
                            TestAssert.Throws<ArgumentException>(
                                () => node.Neighbors = new Dictionary<int, List<Guid>> { { 0, new List<Guid> { id } } },
                                "Self neighbor rejected");

                            NodeState invalid = new NodeState();
                            TestAssert.Throws<InvalidOperationException>(() => invalid.Validate(), "Default node is invalid until initialized");
                            return Task.CompletedTask;
                        }),

                    Case("PublicSurface.Models", "HnswStateValidation",
                        "HnswState validates entry point, vector dimensions, null defaults, and node lists",
                        ct =>
                        {
                            Guid id = Guid.NewGuid();
                            HnswState state = new HnswState(2)
                            {
                                Nodes = new List<NodeState> { new NodeState(id, new List<float> { 1f, 2f }) },
                                EntryPointId = id,
                            };
                            state.Parameters = null!;
                            state.Validate();
                            TestAssert.Equal(1, state.NodeCount, "NodeCount");
                            TestAssert.False(state.IsEmpty, "State is not empty");

                            state.Nodes = null!;
                            TestAssert.Equal(0, state.NodeCount, "Null Nodes becomes empty list");

                            TestAssert.Throws<ArgumentException>(
                                () => state.Nodes = new List<NodeState> { null! },
                                "Nodes list cannot contain null");

                            HnswState missingEntry = new HnswState(2)
                            {
                                Nodes = new List<NodeState> { new NodeState(Guid.NewGuid(), new List<float> { 1f, 2f }) },
                            };
                            TestAssert.Throws<InvalidOperationException>(() => missingEntry.Validate(), "Nodes require entry point");

                            HnswState wrongEntry = new HnswState(2)
                            {
                                Nodes = new List<NodeState> { new NodeState(Guid.NewGuid(), new List<float> { 1f, 2f }) },
                                EntryPointId = Guid.NewGuid(),
                            };
                            TestAssert.Throws<InvalidOperationException>(() => wrongEntry.Validate(), "Entry point must exist");

                            HnswState wrongDimension = new HnswState(3)
                            {
                                Nodes = new List<NodeState> { new NodeState(id, new List<float> { 1f, 2f }) },
                                EntryPointId = id,
                            };
                            TestAssert.Throws<InvalidOperationException>(() => wrongDimension.Validate(), "Node dimension must match state");
                            return Task.CompletedTask;
                        }),

                    Case("PublicSurface.Models", "VectorResultValidationCloneEquality",
                        "VectorResult validates values, clones deeply, and compares by value",
                        ct =>
                        {
                            Guid id = Guid.NewGuid();
                            VectorResult result = new VectorResult(id, 1.25f, new List<float> { 1f, 2f, 3f });
                            result.Validate();
                            TestAssert.Equal(3, result.Dimension, "Dimension");

                            VectorResult clone = result.Clone();
                            TestAssert.True(result.Equals(clone), "Clone equals original");
                            clone.Vectors[0] = 42f;
                            TestAssert.Equal(1f, result.Vectors[0], "Clone vector is independent");
                            TestAssert.False(result.Equals(clone), "Modified clone no longer equals");
                            TestAssert.True(result.ToString().Contains("Dimension = 3", StringComparison.Ordinal), "ToString includes dimension");

                            TestAssert.Throws<ArgumentException>(() => result.GUID = Guid.Empty, "Guid.Empty rejected");
                            TestAssert.Throws<ArgumentOutOfRangeException>(() => result.Distance = -0.1f, "Negative distance rejected");
                            TestAssert.Throws<ArgumentException>(() => result.Distance = float.PositiveInfinity, "Infinite distance rejected");
                            TestAssert.Throws<ArgumentException>(() => result.Vectors = new List<float> { float.NaN }, "NaN vector rejected");

                            VectorResult incomplete = new VectorResult();
                            TestAssert.Throws<InvalidOperationException>(() => incomplete.Validate(), "Default result is incomplete");
                            return Task.CompletedTask;
                        }),
                });
        }

        /// <summary>
        /// Validates public helper utility contracts.
        /// </summary>
        public static TestSuiteDescriptor UtilitySuite()
        {
            return new TestSuiteDescriptor(
                suiteId: "PublicSurface.Utilities",
                displayName: "Public Surface - Utilities",
                cases: new List<TestCaseDescriptor>
                {
                    Case("PublicSurface.Utilities", "MinHeapOrderingAndValidation",
                        "MinHeap orders by priority, preserves Count during snapshots, and rejects invalid use",
                        ct =>
                        {
                            MinHeap<string> heap = new MinHeap<string>(capacity: 2);
                            heap.Push(2f, "b");
                            heap.Push(1f, "c");
                            heap.Push(1f, "a");

                            TestAssert.Equal(3, heap.Count, "Count after pushes");
                            TestAssert.Equal("a", heap.Peek().item, "Tie-breaker uses item comparer");
                            TestAssert.True(heap.Contains("b"), "Contains finds existing item");

                            List<(float priority, string item)> sorted = heap.GetAll();
                            TestAssert.Equal(3, heap.Count, "GetAll does not mutate heap");
                            TestAssert.Equal("a", sorted[0].item, "First sorted item");
                            TestAssert.Equal("c", sorted[1].item, "Second sorted item");
                            TestAssert.Equal("b", sorted[2].item, "Third sorted item");

                            TestAssert.Equal("a", heap.Pop().item, "First pop");
                            heap.Capacity = 1;
                            TestAssert.True(heap.Capacity >= heap.Count, "Capacity cannot shrink below Count");
                            heap.Clear();
                            TestAssert.True(heap.IsEmpty, "Clear empties heap");

                            TestAssert.Throws<InvalidOperationException>(() => heap.Pop(), "Pop empty heap");
                            TestAssert.Throws<InvalidOperationException>(() => heap.Peek(), "Peek empty heap");
                            TestAssert.Throws<ArgumentException>(() => heap.Push(float.NaN, "x"), "NaN priority rejected");
                            TestAssert.Throws<ArgumentOutOfRangeException>(() => heap.Capacity = -1, "Negative capacity rejected");
                            return Task.CompletedTask;
                        }),

                    Case("PublicSurface.Utilities", "PriorityItemAndTryGetNodeResult",
                        "PriorityItem equality and TryGetNodeResult factories behave predictably",
                        ct =>
                        {
                            PriorityItem<string> a = new PriorityItem<string>(1f, "alpha");
                            PriorityItem<string> b = new PriorityItem<string>(1f, "alpha");
                            PriorityItem<string> c = new PriorityItem<string>(2f, "alpha");

                            TestAssert.True(a.Equals(b), "Equal priority items compare equal");
                            TestAssert.Equal(a.GetHashCode(), b.GetHashCode(), "Equal priority items share hash");
                            TestAssert.False(a.Equals(c), "Different priority item is not equal");
                            TestAssert.True(a.ToString().Contains("alpha", StringComparison.Ordinal), "ToString includes item");
                            TestAssert.Throws<ArgumentException>(() => new PriorityItem<string>(float.NaN, "bad"), "NaN priority rejected");

                            RamHnswNode node = new RamHnswNode(Guid.NewGuid(), new List<float> { 1f });
                            TryGetNodeResult found = TryGetNodeResult.Found(node);
                            TestAssert.True(found.Success, "Found success");
                            TestAssert.Equal(node.Id, found.Node!.Id, "Found node");

                            TryGetNodeResult missing = TryGetNodeResult.NotFound();
                            TestAssert.False(missing.Success, "NotFound success false");
                            TestAssert.True(missing.Node == null, "NotFound node null");
                            TestAssert.Throws<ArgumentNullException>(() => TryGetNodeResult.Found(null!), "Found rejects null");
                            node.Dispose();
                            return Task.CompletedTask;
                        }),

                    Case("PublicSurface.Utilities", "SearchContextCachesAndValidatesInputs",
                        "SearchContext caches single and batch node lookups and honors cancellation",
                        async ct =>
                        {
                            await using RamStorageProvider provider = new RamStorageProvider();
                            Guid a = Guid.NewGuid();
                            Guid b = Guid.NewGuid();
                            await provider.AddNodesAsync(new Dictionary<Guid, List<float>>
                            {
                                { a, new List<float> { 1f } },
                                { b, new List<float> { 2f } },
                            }, ct).ConfigureAwait(false);

                            SearchContext context = new SearchContext(provider, ct);
                            IHnswNode first = await context.GetNodeAsync(a).ConfigureAwait(false);
                            IHnswNode second = await context.GetNodeAsync(a).ConfigureAwait(false);
                            TestAssert.True(object.ReferenceEquals(first, second), "Cached single lookup returns same node instance");
                            TestAssert.Equal(1, context.CachedNodeCount, "Single lookup cache count");

                            Dictionary<Guid, IHnswNode> batch = await context.GetNodesAsync(new[] { a, b }).ConfigureAwait(false);
                            TestAssert.Equal(2, batch.Count, "Batch lookup count");
                            TestAssert.Equal(2, context.CachedNodeCount, "Batch lookup cache count");

                            context.ClearCache();
                            TestAssert.Equal(0, context.CachedNodeCount, "ClearCache");
                            TryGetNodeResult missing = await context.TryGetNodeAsync(Guid.NewGuid()).ConfigureAwait(false);
                            TestAssert.False(missing.Success, "TryGet missing");

                            TestAssert.Throws<ArgumentNullException>(() => new SearchContext(null!), "Null storage rejected");
                            await TestAssert.ThrowsAsync<ArgumentNullException>(() => context.GetNodesAsync(null!), "Null ids rejected");

                            using CancellationTokenSource cts = new CancellationTokenSource();
                            cts.Cancel();
                            SearchContext cancelled = new SearchContext(provider, cts.Token);
                            await TestAssert.ThrowsAsync<OperationCanceledException>(() => cancelled.GetNodeAsync(a), "Cancelled context throws");
                        }),
                });
        }

        /// <summary>
        /// Validates HnswIndex public contract and invalid-import safety.
        /// </summary>
        public static TestSuiteDescriptor IndexContractSuite()
        {
            return new TestSuiteDescriptor(
                suiteId: "PublicSurface.Index",
                displayName: "Public Surface - HnswIndex Contract",
                cases: new List<TestCaseDescriptor>
                {
                    Case("PublicSurface.Index", "ConstructorAndPropertyValidation",
                        "HnswIndex validates constructor arguments, parameter setters, seed, and distance fallback",
                        async ct =>
                        {
                            TestAssert.Throws<ArgumentOutOfRangeException>(
                                () => new HnswIndex(4097, new RamStorageProvider()),
                                "Dimension upper bound");
                            TestAssert.Throws<ArgumentNullException>(
                                () => new HnswIndex(2, null!, new RamHnswLayerStorage()),
                                "Null storage rejected");
                            TestAssert.Throws<ArgumentNullException>(
                                () => new HnswIndex(2, new RamHnswStorage(), null!),
                                "Null layer storage rejected");

                            await using TestIndexScope scope = await TestIndexScope.CreateAsync(2, ct).ConfigureAwait(false);
                            HnswIndex index = scope.Index;
                            TestAssert.Equal(2, index.VectorDimension, "VectorDimension");

                            index.DistanceFunction = null!;
                            TestAssert.Equal("Euclidean", index.DistanceFunction.Name, "Null distance function falls back to Euclidean");

                            index.Seed = -1;
                            TestAssert.Equal(-1, index.Seed, "Seed -1 means random");
                            index.Seed = 123;
                            TestAssert.Equal(123, index.Seed, "Seed stored");
                            TestAssert.Throws<ArgumentOutOfRangeException>(() => index.Seed = -2, "Seed lower bound");

                            TestAssert.Throws<ArgumentOutOfRangeException>(() => index.MaxM = 0, "MaxM lower bound");
                            TestAssert.Throws<ArgumentOutOfRangeException>(() => index.EfConstruction = 0, "EfConstruction lower bound");
                            TestAssert.Throws<ArgumentOutOfRangeException>(() => index.MaxLayers = 0, "MaxLayers lower bound");
                            TestAssert.Throws<ArgumentException>(() => index.LevelMultiplier = double.PositiveInfinity, "LevelMultiplier finite");
                        }),

                    Case("PublicSurface.Index", "GetTopKArgumentValidation",
                        "GetTopKAsync rejects invalid vectors, counts, and ef values before searching",
                        async ct =>
                        {
                            await using TestIndexScope scope = await TestIndexScope.CreateAsync(2, ct).ConfigureAwait(false);
                            HnswIndex index = scope.Index;

                            await TestAssert.ThrowsAsync<ArgumentNullException>(() => index.GetTopKAsync(null!, 1), "Null query rejected");
                            await TestAssert.ThrowsAsync<ArgumentException>(() => index.GetTopKAsync(new List<float> { 1f }, 1), "Dimension mismatch rejected");
                            await TestAssert.ThrowsAsync<ArgumentOutOfRangeException>(() => index.GetTopKAsync(new List<float> { 1f, 2f }, 0), "Count lower bound");
                            await TestAssert.ThrowsAsync<ArgumentOutOfRangeException>(() => index.GetTopKAsync(new List<float> { 1f, 2f }, 10001), "Count upper bound");
                            await TestAssert.ThrowsAsync<ArgumentOutOfRangeException>(() => index.GetTopKAsync(new List<float> { 1f, 2f }, 1, ef: 0), "EF lower bound");
                            await TestAssert.ThrowsAsync<ArgumentOutOfRangeException>(() => index.GetTopKAsync(new List<float> { 1f, 2f }, 1, ef: 10001), "EF upper bound");
                        }),

                    Case("PublicSurface.Index", "InvalidImportIsRejectedWithoutDataLoss",
                        "ImportStateAsync validates state before clearing existing index data",
                        async ct =>
                        {
                            await using TestIndexScope scope = await TestIndexScope.CreateAsync(2, ct).ConfigureAwait(false);
                            HnswIndex index = scope.Index;
                            Guid existing = Guid.NewGuid();
                            await index.AddAsync(existing, new List<float> { 1f, 1f }, ct).ConfigureAwait(false);

                            HnswState invalid = new HnswState(2)
                            {
                                Nodes = new List<NodeState>
                                {
                                    new NodeState(Guid.NewGuid(), new List<float> { 2f, 2f }),
                                },
                                EntryPointId = null,
                            };

                            await TestAssert.ThrowsAsync<InvalidOperationException>(
                                () => index.ImportStateAsync(invalid, ct),
                                "Invalid state rejected");

                            List<VectorResult> results = (await index.GetTopKAsync(
                                new List<float> { 1f, 1f },
                                1,
                                cancellationToken: ct).ConfigureAwait(false)).ToList();
                            TestAssert.Equal(1, results.Count, "Existing data still searchable");
                            TestAssert.Equal(existing, results[0].GUID, "Existing GUID remains");
                        }),
                });
        }

        /// <summary>
        /// Validates direct storage-provider behavior outside HnswIndex.
        /// </summary>
        public static TestSuiteDescriptor StorageProviderContractSuite()
        {
            return new TestSuiteDescriptor(
                suiteId: "PublicSurface.StorageProvider",
                displayName: "Public Surface - Storage Provider Contract",
                cases: new List<TestCaseDescriptor>
                {
                    Case("PublicSurface.StorageProvider", "DirectNodeLayerAndEntryPointLifecycle",
                        "IStorageProvider supports direct node, batch, layer, and entry-point operations",
                        async ct =>
                        {
                            await using TestIndexScope scope = await TestIndexScope.CreateAsync(3, ct).ConfigureAwait(false);
                            IStorageProvider provider = scope.Provider;
                            Guid a = Guid.NewGuid();
                            Guid b = Guid.NewGuid();
                            Guid missing = Guid.NewGuid();

                            await provider.AddNodeAsync(a, new List<float> { 1f, 2f, 3f }, ct).ConfigureAwait(false);
                            await provider.AddNodesAsync(new Dictionary<Guid, List<float>>
                            {
                                { b, new List<float> { 4f, 5f, 6f } },
                            }, ct).ConfigureAwait(false);

                            TestAssert.Equal(2, await provider.GetCountAsync(ct).ConfigureAwait(false), "Provider count");
                            TestAssert.Equal(a, await provider.GetEntryPointAsync(ct).ConfigureAwait(false), "First node becomes entry point");

                            TryGetNodeResult found = await provider.TryGetNodeAsync(a, ct).ConfigureAwait(false);
                            TestAssert.True(found.Success, "TryGet existing");
                            TryGetNodeResult notFound = await provider.TryGetNodeAsync(missing, ct).ConfigureAwait(false);
                            TestAssert.False(notFound.Success, "TryGet missing");

                            Dictionary<Guid, IHnswNode> batch = await provider.GetNodesAsync(new[] { a, b, missing }, ct).ConfigureAwait(false);
                            TestAssert.Equal(2, batch.Count, "GetNodes only returns existing nodes");

                            await provider.SetNodeLayerAsync(a, 2, ct).ConfigureAwait(false);
                            TestAssert.Equal(2, await provider.GetNodeLayerAsync(a, ct).ConfigureAwait(false), "Layer stored");
                            Dictionary<Guid, int> layers = await provider.GetAllNodeLayersAsync(ct).ConfigureAwait(false);
                            TestAssert.True(layers.ContainsKey(a), "All layers includes node");

                            await provider.RemoveNodeLayerAsync(a, ct).ConfigureAwait(false);
                            TestAssert.Equal(0, await provider.GetNodeLayerAsync(a, ct).ConfigureAwait(false), "Removed layer reads as default");

                            await provider.SetEntryPointAsync(b, ct).ConfigureAwait(false);
                            TestAssert.Equal(b, await provider.GetEntryPointAsync(ct).ConfigureAwait(false), "Entry point updated");

                            await provider.ClearLayersAsync(ct).ConfigureAwait(false);
                            TestAssert.Equal(0, await provider.GetLayerCountAsync(ct).ConfigureAwait(false), "Layer clear");
                            await provider.FlushAsync(ct).ConfigureAwait(false);
                        }),

                    Case("PublicSurface.StorageProvider", "DirectStorageRejectsInvalidInputsAndDispose",
                        "IStorageProvider rejects invalid direct operations and throws after disposal",
                        async ct =>
                        {
                            TestIndexScope scope = await TestIndexScope.CreateAsync(2, ct).ConfigureAwait(false);
                            IStorageProvider provider = scope.Provider;

                            await TestAssert.ThrowsAsync<ArgumentException>(
                                () => provider.AddNodeAsync(Guid.Empty, new List<float> { 1f, 2f }, ct),
                                "Guid.Empty add rejected");
                            await TestAssert.ThrowsAsync<ArgumentNullException>(
                                () => provider.AddNodeAsync(Guid.NewGuid(), null!, ct),
                                "Null vector add rejected");
                            await TestAssert.ThrowsAsync<ArgumentException>(
                                () => provider.AddNodeAsync(Guid.NewGuid(), new List<float>(), ct),
                                "Empty vector add rejected");
                            await TestAssert.ThrowsAsync<ArgumentException>(
                                () => provider.AddNodeAsync(Guid.NewGuid(), new List<float> { 1f, float.NaN }, ct),
                                "NaN vector add rejected");
                            await TestAssert.ThrowsAsync<KeyNotFoundException>(
                                () => provider.GetNodeAsync(Guid.NewGuid(), ct),
                                "Missing GetNode rejected");
                            await TestAssert.ThrowsAsync<ArgumentException>(
                                () => provider.SetNodeLayerAsync(Guid.Empty, 0, ct),
                                "Guid.Empty layer rejected");
                            await TestAssert.ThrowsAsync<ArgumentOutOfRangeException>(
                                () => provider.SetNodeLayerAsync(Guid.NewGuid(), -1, ct),
                                "Negative layer rejected");
                            await TestAssert.ThrowsAsync<ArgumentOutOfRangeException>(
                                () => provider.SetNodeLayerAsync(Guid.NewGuid(), 64, ct),
                                "Layer upper bound rejected");

                            await scope.DisposeAsync().ConfigureAwait(false);
                            await TestAssert.ThrowsAsync<ObjectDisposedException>(
                                () => provider.GetCountAsync(ct),
                                "Disposed provider rejected");
                        }),
                });
        }

        private static TestCaseDescriptor Case(string suiteId, string caseId, string display, Func<CancellationToken, Task> exec)
        {
            return new TestCaseDescriptor(suiteId: suiteId, caseId: caseId, displayName: display, executeAsync: exec);
        }
    }
}
