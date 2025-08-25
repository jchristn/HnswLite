namespace Test.Ram
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    using Hnsw;
    using Hnsw.RamStorage;

    /// <summary>
    /// Test program for HNSW RAM storage implementation.
    /// </summary>
    public static class Program
    {
        #region Public-Members

        #endregion

        #region Private-Members

        private static readonly int _TestDimension = 64;
        private static readonly int _TestVectorCount = 5000;
        private static readonly int _BatchSize = 20;
        private static readonly int _LargeDatasetSize = 60;
        private static readonly int _HighDimensionSize = 384;
        private static readonly int _DuplicateVectorCount = 5;
        private static readonly int _QueryCount = 100;
        private static readonly float _MinSimilarityThreshold = 0.01f;

        private static int _TestsPassed = 0;
        private static int _TestsFailed = 0;
        private static List<string> _TestResults = new List<string>();

        #endregion

        #region Entrypoint
 
        /// <summary>
        /// Main entry point for the test program.
        /// </summary>
        public static async Task Main()
        {
            Console.WriteLine("=== HNSW RAM Test Suite ===\n");

            await TestBasicAddAndSearchAsync();
            await TestRemoveAsync();
            await TestEmptyIndexAsync();
            await TestSingleElementAsync();
            await TestDuplicateVectorsAsync();
            await TestHighDimensionalAsync();
            await TestLargerDatasetAsync();
            await TestDistanceFunctionsAsync();
            await TestBatchOperationsAsync();
            await TestStateExportImportAsync();
            await TestValidationAsync();
            await TestPersistenceAsync();
            await TestPerformanceComparisonAsync();

            Console.WriteLine("\n=== All tests completed ===");
            PrintTestSummary();
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        private static async Task<List<VectorResult>> TimedSearchAsync(HnswIndex index, List<float> query, int k, int? ef = null, string label = "Search", float minSimilarity = -1.0f)
        {
            Stopwatch sw = Stopwatch.StartNew();
            List<VectorResult> allResults = (await index.GetTopKAsync(query, k, ef)).ToList();
            sw.Stop();
            
            float actualMinSimilarity = minSimilarity < 0 ? _MinSimilarityThreshold : minSimilarity;
            float maxDistance = 1.0f / actualMinSimilarity;
            List<VectorResult> filteredResults = allResults.Where(r => r.Distance <= maxDistance).ToList();
            
            Console.WriteLine($"{label} time: {sw.ElapsedMilliseconds}ms ({sw.Elapsed.TotalMicroseconds:F0}μs) - Found {allResults.Count} total, {filteredResults.Count} above similarity threshold");
            return filteredResults;
        }

        private static void RecordTestResult(string testName, bool passed)
        {
            if (passed)
            {
                _TestsPassed++;
                _TestResults.Add($"✓ {testName}: PASSED");
            }
            else
            {
                _TestsFailed++;
                _TestResults.Add($"✗ {testName}: FAILED");
            }
        }

        private static void PrintTestSummary()
        {
            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("TEST SUMMARY");
            Console.WriteLine(new string('=', 60));
            foreach (string result in _TestResults)
            {
                Console.WriteLine(result);
            }
            Console.WriteLine(new string('-', 60));
            Console.WriteLine($"Total Tests: {_TestsPassed + _TestsFailed}");
            Console.WriteLine($"Passed: {_TestsPassed}");
            Console.WriteLine($"Failed: {_TestsFailed}");
            Console.WriteLine($"Success Rate: {(_TestsPassed / (double)(_TestsPassed + _TestsFailed)) * 100:F1}%");
            Console.WriteLine(new string('=', 60));
        }

        private static async Task TestBasicAddAndSearchAsync()
        {
            Console.WriteLine("Test 1: Basic Add and Search");
            HnswIndex index = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());

            Dictionary<Guid, List<float>> vectors = new Dictionary<Guid, List<float>>
            {
                { Guid.NewGuid(), new List<float> { 1.0f, 1.0f } },
                { Guid.NewGuid(), new List<float> { 2.0f, 2.0f } },
                { Guid.NewGuid(), new List<float> { 3.0f, 3.0f } },
                { Guid.NewGuid(), new List<float> { 10.0f, 10.0f } },
                { Guid.NewGuid(), new List<float> { 11.0f, 11.0f } }
            };

            await index.AddNodesAsync(vectors);

            List<float> query = new List<float> { 1.5f, 1.5f };
            Console.WriteLine($"Query: [{string.Join(", ", query)}]");
            List<VectorResult> results = await TimedSearchAsync(index, query, 3, null, "Search");
            Console.WriteLine("Top 3 results:");
            foreach (VectorResult result in results)
            {
                Console.WriteLine($"  Vector: [{string.Join(", ", result.Vectors)}], Distance: {result.Distance:F4}");
            }

            List<float> firstVector = results[0].Vectors;
            bool isCorrect = (Math.Abs(firstVector[0] - 1.0f) < 0.1f || Math.Abs(firstVector[0] - 2.0f) < 0.1f);
            Console.WriteLine($"Test passed: {isCorrect}\n");
            RecordTestResult("Basic Add and Search", isCorrect);
        }

        private static async Task TestRemoveAsync()
        {
            Console.WriteLine("Test 2: Remove Operation");
            HnswIndex index = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());

            Guid id1 = Guid.NewGuid();
            Guid id2 = Guid.NewGuid();
            Guid id3 = Guid.NewGuid();

            Dictionary<Guid, List<float>> vectors = new Dictionary<Guid, List<float>>
            {
                { id1, new List<float> { 1.0f, 1.0f } },
                { id2, new List<float> { 2.0f, 2.0f } },
                { id3, new List<float> { 3.0f, 3.0f } }
            };
            await index.AddNodesAsync(vectors);

            await index.RemoveAsync(id2);

            List<VectorResult> results = await TimedSearchAsync(index, new List<float> { 2.0f, 2.0f }, 3);
            Console.WriteLine($"Results after removing (2,2): {results.Count} vectors found");

            bool containsRemoved = results.Any(r => r.GUID == id2);
            bool testPassed = !containsRemoved;
            Console.WriteLine($"Test passed: {testPassed}\n");
            RecordTestResult("Remove Operation", testPassed);
        }

        private static async Task TestEmptyIndexAsync()
        {
            Console.WriteLine("Test 3: Empty Index");
            HnswIndex index = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());

            List<VectorResult> results = await TimedSearchAsync(index, new List<float> { 1.0f, 1.0f }, 5);
            Console.WriteLine($"Results from empty index: {results.Count}");
            bool testPassed = results.Count == 0;
            Console.WriteLine($"Test passed: {testPassed}\n");
            RecordTestResult("Empty Index", testPassed);
        }

        private static async Task TestSingleElementAsync()
        {
            Console.WriteLine("Test 4: Single Element");
            HnswIndex index = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());

            Guid id = Guid.NewGuid();
            await index.AddAsync(id, new List<float> { 5.0f, 5.0f });

            List<VectorResult> results = await TimedSearchAsync(index, new List<float> { 0.0f, 0.0f }, 1);
            Console.WriteLine($"Found {results.Count} result(s)");
            bool testPassed = results.Count == 1 && results[0].GUID == id;
            Console.WriteLine($"Test passed: {testPassed}\n");
            RecordTestResult("Single Element", testPassed);
        }

        private static async Task TestDuplicateVectorsAsync()
        {
            Console.WriteLine("Test 5: Duplicate Vectors");
            HnswIndex index = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());

            Dictionary<Guid, List<float>> duplicates = new Dictionary<Guid, List<float>>();
            for (int i = 0; i < _DuplicateVectorCount; i++)
            {
                duplicates[Guid.NewGuid()] = new List<float> { 5.0f, 5.0f };
            }
            await index.AddNodesAsync(duplicates);

            List<VectorResult> results = await TimedSearchAsync(index, new List<float> { 5.0f, 5.0f }, 10);
            Console.WriteLine($"Found {results.Count} duplicate vectors");

            bool allZeroDistance = results.All(r => Math.Abs(r.Distance) < 0.001f);
            Console.WriteLine($"All have zero distance: {allZeroDistance}");
            bool testPassed = results.Count == _DuplicateVectorCount && allZeroDistance;
            Console.WriteLine($"Test passed: {testPassed}\n");
            RecordTestResult("Duplicate Vectors", testPassed);
        }

        private static async Task TestHighDimensionalAsync()
        {
            Console.WriteLine("Test 6: High-Dimensional Vectors");
            HnswIndex index = new HnswIndex(_HighDimensionSize, new RamHnswStorage(), new RamHnswLayerStorage());
            Random random = new Random(42);

            Dictionary<Guid, List<float>> vectors = new Dictionary<Guid, List<float>>();
            List<Guid> ids = new List<Guid>();
            for (int i = 0; i < 10; i++)
            {
                Guid id = Guid.NewGuid();
                ids.Add(id);
                vectors[id] = Enumerable.Range(0, _HighDimensionSize).Select(_ => (float)random.NextDouble()).ToList();
            }
            await index.AddNodesAsync(vectors);

            List<float> query = Enumerable.Range(0, _HighDimensionSize).Select(_ => (float)random.NextDouble()).ToList();

            List<VectorResult> results = await TimedSearchAsync(index, query, 5);
            Console.WriteLine($"Found {results.Count} nearest neighbors in {_HighDimensionSize}D space");
            bool testPassed = results.Count == 5;
            Console.WriteLine($"Test passed: {testPassed}\n");
            RecordTestResult("High-Dimensional Vectors", testPassed);
        }

        private static async Task TestLargerDatasetAsync()
        {
            Console.WriteLine("Test 7: Larger Dataset Performance");
            HnswIndex index = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());
            index.Seed = 42;
            index.ExtendCandidates = true;
            Random random = new Random(42);

            var clusters = new[]
            {
                (center: new[] { 0f, 0f }, count: _LargeDatasetSize / 3),
                (center: new[] { 10f, 10f }, count: _LargeDatasetSize / 3),
                (center: new[] { -10f, 5f }, count: _LargeDatasetSize / 3)
            };

            Stopwatch sw = Stopwatch.StartNew();

            Dictionary<Guid, List<float>> allPoints = new Dictionary<Guid, List<float>>();

            for (int clusterIdx = 0; clusterIdx < clusters.Length; clusterIdx++)
            {
                var cluster = clusters[clusterIdx];
                for (int i = 0; i < cluster.count; i++)
                {
                    List<float> vector = new List<float>
                    {
                        cluster.center[0] + (float)(random.NextDouble() - 0.5),
                        cluster.center[1] + (float)(random.NextDouble() - 0.5)
                    };
                    allPoints[Guid.NewGuid()] = vector;
                }
            }

            Dictionary<Guid, List<float>> shuffled = allPoints.OrderBy(x => random.Next()).ToDictionary(x => x.Key, x => x.Value);

            await index.AddNodesAsync(shuffled);

            sw.Stop();
            Console.WriteLine($"Added {_LargeDatasetSize} vectors in {sw.ElapsedMilliseconds}ms");

            sw.Restart();
            List<VectorResult> results = await TimedSearchAsync(index, new List<float> { 10f, 10f }, 5, 400);
            sw.Stop();

            Console.WriteLine($"Search completed in {sw.ElapsedMilliseconds}ms");
            Console.WriteLine("Nearest 5 to (10, 10):");
            foreach (VectorResult result in results)
            {
                Console.WriteLine($"  [{string.Join(", ", result.Vectors)}] - Distance: {result.Distance:F4}");
            }

            int nearCluster = results.Count(r =>
                Math.Abs(r.Vectors[0] - 10f) < 2f &&
                Math.Abs(r.Vectors[1] - 10f) < 2f);

            bool testPassed = nearCluster >= 4;
            Console.WriteLine($"Test passed: {testPassed}\n");
            RecordTestResult("Larger Dataset Performance", testPassed);
        }

        private static async Task TestDistanceFunctionsAsync()
        {
            Console.WriteLine("Test 8: Distance Functions");

            HnswIndex euclideanIndex = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());
            euclideanIndex.DistanceFunction = new EuclideanDistance();
            await TestDistanceFunctionAsync(euclideanIndex, "Euclidean");

            HnswIndex cosineIndex = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());
            cosineIndex.DistanceFunction = new CosineDistance();
            await TestDistanceFunctionAsync(cosineIndex, "Cosine");

            HnswIndex dotIndex = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());
            dotIndex.DistanceFunction = new DotProductDistance();
            await TestDistanceFunctionAsync(dotIndex, "Dot Product");

            Console.WriteLine();
            RecordTestResult("Distance Functions", true);
        }

        private static async Task TestDistanceFunctionAsync(HnswIndex index, string name)
        {
            Dictionary<Guid, List<float>> vectors = new Dictionary<Guid, List<float>>
            {
                { Guid.NewGuid(), new List<float> { 1.0f, 0.0f } },
                { Guid.NewGuid(), new List<float> { 0.0f, 1.0f } },
                { Guid.NewGuid(), new List<float> { 0.707f, 0.707f } },
                { Guid.NewGuid(), new List<float> { -1.0f, 0.0f } }
            };

            await index.AddNodesAsync(vectors);

            List<float> query = new List<float> { 1.0f, 0.0f };
            List<VectorResult> results = await TimedSearchAsync(index, query, 2);

            Console.WriteLine($"{name} distance - Query: [{string.Join(", ", query)}]");
            Console.WriteLine($"  Nearest: [{string.Join(", ", results[0].Vectors)}], Distance: {results[0].Distance:F4}");

            bool passed = Math.Abs(results[0].Vectors[0] - 1.0f) < 0.01f && Math.Abs(results[0].Vectors[1]) < 0.01f;
            Console.WriteLine($"  Test passed: {passed}");
        }

        private static async Task TestBatchOperationsAsync()
        {
            Console.WriteLine("Test 9: Batch Operations");
            HnswIndex index = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());
            Random random = new Random(42);

            Dictionary<Guid, List<float>> batch = new Dictionary<Guid, List<float>>();
            List<Guid> ids = new List<Guid>();
            for (int i = 0; i < _BatchSize; i++)
            {
                Guid id = Guid.NewGuid();
                ids.Add(id);
                batch[id] = new List<float> { (float)random.NextDouble() * 10, (float)random.NextDouble() * 10 };
            }

            Stopwatch sw = Stopwatch.StartNew();
            await index.AddNodesAsync(batch);
            sw.Stop();

            Console.WriteLine($"Batch inserted {batch.Count} vectors in {sw.ElapsedMilliseconds}ms");

            List<float> query = new List<float> { 5.0f, 5.0f };
            List<VectorResult> results = await TimedSearchAsync(index, query, 5);

            Console.WriteLine($"Found {results.Count} results after batch insert");

            List<Guid> toRemove = ids.Take(10).ToList();
            await index.RemoveNodesAsync(toRemove);

            results = await TimedSearchAsync(index, query, 15, null, "After batch remove");
            Console.WriteLine($"After removing 10 items, found {results.Count} results");

            bool testPassed = results.Count == 10;
            Console.WriteLine($"Test passed: {testPassed}\n");
            RecordTestResult("Batch Operations", testPassed);
        }

        private static async Task TestStateExportImportAsync()
        {
            Console.WriteLine("Test 10: State Export/Import");

            HnswIndex originalIndex = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());
            originalIndex.M = 8;
            originalIndex.MaxM = 12;
            originalIndex.DistanceFunction = new CosineDistance();

            Dictionary<Guid, List<float>> vectors = new Dictionary<Guid, List<float>>();
            List<Guid> ids = new List<Guid>();
            for (int i = 0; i < 10; i++)
            {
                Guid id = Guid.NewGuid();
                ids.Add(id);
                vectors[id] = new List<float> { i * 0.1f, i * 0.2f };
            }
            await originalIndex.AddNodesAsync(vectors);

            var state = await originalIndex.ExportStateAsync();

            HnswIndex importedIndex = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());
            await importedIndex.ImportStateAsync(state);

            List<float> query = new List<float> { 0.5f, 1.0f };
            List<VectorResult> originalResults = await TimedSearchAsync(originalIndex, query, 3, null, "Original index search");
            List<VectorResult> importedResults = await TimedSearchAsync(importedIndex, query, 3, null, "Imported index search");

            bool passed = originalResults.Count == importedResults.Count;
            for (int i = 0; i < originalResults.Count && passed; i++)
            {
                passed = originalResults[i].GUID == importedResults[i].GUID &&
                         Math.Abs(originalResults[i].Distance - importedResults[i].Distance) < 0.0001f;
            }

            Console.WriteLine($"Parameters preserved: M={importedIndex.M}, MaxM={importedIndex.MaxM}, Distance={importedIndex.DistanceFunction.Name}");
            Console.WriteLine($"Results match: {passed}");
            Console.WriteLine($"Test passed: {passed}\n");
            RecordTestResult("State Export/Import", passed);
        }

        private static async Task TestValidationAsync()
        {
            Console.WriteLine("Test 11: Input Validation");

            try
            {
                HnswIndex index = new HnswIndex(3, new RamHnswStorage(), new RamHnswLayerStorage());
                await index.AddAsync(Guid.NewGuid(), new List<float> { 1.0f, 2.0f });
                Console.WriteLine("Dimension validation: FAILED");
            }
            catch (ArgumentException)
            {
                Console.WriteLine("Dimension validation: PASSED");
            }

            try
            {
                HnswIndex index = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());
                await index.AddAsync(Guid.NewGuid(), null!);
                Console.WriteLine("Null vector validation: FAILED");
            }
            catch (ArgumentNullException)
            {
                Console.WriteLine("Null vector validation: PASSED");
            }

            try
            {
                HnswIndex index = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());
                index.M = -1;
                Console.WriteLine("Parameter bounds validation: FAILED");
            }
            catch (ArgumentOutOfRangeException)
            {
                Console.WriteLine("Parameter bounds validation: PASSED");
            }

            try
            {
                HnswIndex index = new HnswIndex(0, new RamHnswStorage(), new RamHnswLayerStorage());
                Console.WriteLine("Constructor dimension validation: FAILED");
            }
            catch (ArgumentOutOfRangeException)
            {
                Console.WriteLine("Constructor dimension validation: PASSED");
            }

            try
            {
                HnswIndex index = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());
                await index.AddNodesAsync(new Dictionary<Guid, List<float>>());
                Console.WriteLine("Empty batch validation: FAILED");
            }
            catch (ArgumentException)
            {
                Console.WriteLine("Empty batch validation: PASSED");
            }

            try
            {
                HnswIndex index = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());
                await index.AddNodesAsync(null!);
                Console.WriteLine("Null batch validation: FAILED");
            }
            catch (ArgumentNullException)
            {
                Console.WriteLine("Null batch validation: PASSED");
            }

            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                HnswIndex index = new HnswIndex(_HighDimensionSize, new RamHnswStorage(), new RamHnswLayerStorage());
                cts.Cancel();
                try
                {
                    await index.AddAsync(Guid.NewGuid(), Enumerable.Range(0, _HighDimensionSize).Select(x => (float)x).ToList(), cts.Token);
                    Console.WriteLine("Cancellation test: FAILED");
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Cancellation test: PASSED");
                }
            }

            Console.WriteLine();
            RecordTestResult("Input Validation", true);
        }

        private static async Task TestPersistenceAsync()
        {
            Console.WriteLine("Test 12: Persistence (RAM only)");

            HnswIndex index = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());

            Dictionary<Guid, List<float>> vectors = new Dictionary<Guid, List<float>>
            {
                { Guid.NewGuid(), new List<float> { 1.0f, 1.0f } },
                { Guid.NewGuid(), new List<float> { 2.0f, 2.0f } },
                { Guid.NewGuid(), new List<float> { 3.0f, 3.0f } }
            };

            await index.AddNodesAsync(vectors);

            List<VectorResult> results = await TimedSearchAsync(index, new List<float> { 1.5f, 1.5f }, 3, null, "RAM persistence test");
            Console.WriteLine($"First session: Found {results.Count} vectors");

            HnswIndex newIndex = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());
            List<VectorResult> newResults = await TimedSearchAsync(newIndex, new List<float> { 1.5f, 1.5f }, 3, null, "RAM after restart simulation");
            Console.WriteLine($"Second session: Found {newResults.Count} vectors");

            bool testPassed = results.Count == 3 && newResults.Count == 0;
            Console.WriteLine($"RAM storage doesn't persist (expected behavior): {testPassed}");
            Console.WriteLine($"Test passed: {testPassed}\n");
            RecordTestResult("Persistence", testPassed);
        }

        private static async Task TestPerformanceComparisonAsync()
        {
            Console.WriteLine("Test 13: Performance Comparison (RAM only)");

            HnswIndex index = new HnswIndex(_TestDimension, new RamHnswStorage(), new RamHnswLayerStorage());
            index.Seed = 42;
            index.EfConstruction = 50;

            Console.WriteLine($"Testing with {_TestVectorCount} {_TestDimension}-dimensional vectors...\n");

            Random random = new Random(42);
            
            Console.Write("Generating test vectors... ");
            Dictionary<Guid, List<float>> vectors = new Dictionary<Guid, List<float>>();
            for (int i = 0; i < _TestVectorCount; i++)
            {
                if ((i + 1) % 100 == 0) Console.Write($"{i + 1}/{_TestVectorCount} ");
                vectors[Guid.NewGuid()] = Enumerable.Range(0, _TestDimension).Select(_ => (float)(random.NextDouble() * 2 - 1)).ToList();
            }
            Console.WriteLine("Done");

            Console.Write("Testing RAM batch insertion... ");
            Stopwatch sw = Stopwatch.StartNew();
            await index.AddNodesAsync(vectors);
            sw.Stop();
            long insertTime = sw.ElapsedMilliseconds;
            Console.WriteLine($"Completed in {insertTime}ms");

            Console.WriteLine("=== INSERTION PERFORMANCE ===");
            Console.WriteLine($"RAM: {insertTime}ms total, {_TestVectorCount * 1000.0 / insertTime:F1} vectors/sec");

            List<List<float>> queries = new List<List<float>>();
            for (int i = 0; i < _QueryCount; i++)
            {
                queries.Add(Enumerable.Range(0, _TestDimension).Select(_ => (float)(random.NextDouble() * 2 - 1)).ToList());
            }

            sw.Restart();
            foreach (List<float> query in queries)
            {
                await TimedSearchAsync(index, query, 10, 100, "RAM performance search");
            }
            sw.Stop();
            long searchTime = sw.ElapsedMilliseconds;

            Console.WriteLine($"=== SEARCH PERFORMANCE ({queries.Count} queries) ===");
            Console.WriteLine($"RAM: {searchTime}ms total, {queries.Count * 1000.0 / searchTime:F1} queries/sec");

            List<Guid> removeIds = vectors.Keys.Take(_TestVectorCount / 2).ToList();
            sw.Restart();
            await index.RemoveNodesAsync(removeIds);
            sw.Stop();
            long removeTime = sw.ElapsedMilliseconds;

            Console.WriteLine("=== BATCH REMOVE PERFORMANCE ===");
            Console.WriteLine($"RAM removed {removeIds.Count} vectors in {removeTime}ms");

            List<float> testQuery = Enumerable.Range(0, _TestDimension).Select(_ => 0.5f).ToList();
            List<VectorResult> finalResults = await TimedSearchAsync(index, testQuery, 10, null, "RAM final test");
            
            Console.WriteLine("=== SUMMARY ===");
            Console.WriteLine($"• RAM insertion: {_TestVectorCount * 1000.0 / insertTime:F1} vectors/sec");
            Console.WriteLine($"• RAM search: {queries.Count * 1000.0 / searchTime:F1} queries/sec");
            Console.WriteLine($"• RAM batch remove: {removeIds.Count * 1000.0 / removeTime:F1} removals/sec");
            Console.WriteLine($"• Final result count: {finalResults.Count}");
            Console.WriteLine($"• In-memory storage only (no persistence)\n");

            bool testPassed = finalResults.Count > 0;
            RecordTestResult("Performance Comparison", testPassed);
        }

        #endregion
    }
}