namespace Test.Ram
{
    using System;

    using Hnsw;
    using Hnsw.RamStorage;

    public static class Program
    {
        /// <summary>
        /// Main entry point for the test program.
        /// </summary>
        public static async Task Main()
        {
            Console.WriteLine("=== HNSW Index Test Suite ===\n");

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

            Console.WriteLine("\n=== All tests completed ===");
        }

        private static async Task TestBasicAddAndSearchAsync()
        {
            Console.WriteLine("Test 1: Basic Add and Search");
            var index = new HNSWIndex(2, new RamHnswStorage());

            // Add some 2D vectors
            var vectors = new List<(Guid, List<float>)>
            {
                (Guid.NewGuid(), new List<float> { 1.0f, 1.0f }),
                (Guid.NewGuid(), new List<float> { 2.0f, 2.0f }),
                (Guid.NewGuid(), new List<float> { 3.0f, 3.0f }),
                (Guid.NewGuid(), new List<float> { 10.0f, 10.0f }),
                (Guid.NewGuid(), new List<float> { 11.0f, 11.0f })
            };

            foreach (var (id, vector) in vectors)
            {
                await index.AddAsync(id, vector);
            }

            // Search for nearest to (1.5, 1.5)
            var query = new List<float> { 1.5f, 1.5f };
            var results = (await index.GetTopKAsync(query, 3)).ToList();

            Console.WriteLine($"Query: [{string.Join(", ", query)}]");
            Console.WriteLine("Top 3 results:");
            foreach (var result in results)
            {
                Console.WriteLine($"  Vector: [{string.Join(", ", result.Vectors)}], Distance: {result.Distance:F4}");
            }

            // Verify the closest vectors are (1,1) and (2,2)
            var firstVector = results[0].Vectors;
            var isCorrect = (Math.Abs(firstVector[0] - 1.0f) < 0.1f || Math.Abs(firstVector[0] - 2.0f) < 0.1f);
            Console.WriteLine($"Test passed: {isCorrect}\n");
        }

        private static async Task TestRemoveAsync()
        {
            Console.WriteLine("Test 2: Remove Operation");
            var index = new HNSWIndex(2, new Hnsw.RamStorage.RamHnswStorage());

            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            var id3 = Guid.NewGuid();

            await index.AddAsync(id1, new List<float> { 1.0f, 1.0f });
            await index.AddAsync(id2, new List<float> { 2.0f, 2.0f });
            await index.AddAsync(id3, new List<float> { 3.0f, 3.0f });

            // Remove the middle vector
            await index.RemoveAsync(id2);

            var results = (await index.GetTopKAsync(new List<float> { 2.0f, 2.0f }, 3)).ToList();
            Console.WriteLine($"Results after removing (2,2): {results.Count} vectors found");

            // Verify id2 is not in results
            var containsRemoved = results.Any(r => r.GUID == id2);
            Console.WriteLine($"Test passed: {!containsRemoved}\n");
        }

        private static async Task TestEmptyIndexAsync()
        {
            Console.WriteLine("Test 3: Empty Index");
            var index = new HNSWIndex(2, new RamHnswStorage());

            var results = (await index.GetTopKAsync(new List<float> { 1.0f, 1.0f }, 5)).ToList();
            Console.WriteLine($"Results from empty index: {results.Count}");
            Console.WriteLine($"Test passed: {results.Count == 0}\n");
        }

        private static async Task TestSingleElementAsync()
        {
            Console.WriteLine("Test 4: Single Element");
            var index = new HNSWIndex(2, new RamHnswStorage());

            var id = Guid.NewGuid();
            await index.AddAsync(id, new List<float> { 5.0f, 5.0f });

            var results = (await index.GetTopKAsync(new List<float> { 0.0f, 0.0f }, 1)).ToList();
            Console.WriteLine($"Found {results.Count} result(s)");
            Console.WriteLine($"Test passed: {results.Count == 1 && results[0].GUID == id}\n");
        }

        private static async Task TestDuplicateVectorsAsync()
        {
            Console.WriteLine("Test 5: Duplicate Vectors");
            var index = new HNSWIndex(2, new RamHnswStorage());

            // Add multiple vectors at the same location
            for (int i = 0; i < 5; i++)
            {
                await index.AddAsync(Guid.NewGuid(), new List<float> { 5.0f, 5.0f });
            }

            var results = (await index.GetTopKAsync(new List<float> { 5.0f, 5.0f }, 10)).ToList();
            Console.WriteLine($"Found {results.Count} duplicate vectors");

            // All should have distance 0
            var allZeroDistance = results.All(r => Math.Abs(r.Distance) < 0.001f);
            Console.WriteLine($"All have zero distance: {allZeroDistance}");
            Console.WriteLine($"Test passed: {results.Count == 5 && allZeroDistance}\n");
        }

        private static async Task TestHighDimensionalAsync()
        {
            Console.WriteLine("Test 6: High-Dimensional Vectors");
            var index = new HNSWIndex(100, new RamHnswStorage());
            var random = new Random(42);

            // Add 10 random 100-dimensional vectors
            var ids = new List<Guid>();
            for (int i = 0; i < 10; i++)
            {
                var id = Guid.NewGuid();
                ids.Add(id);
                var vector = Enumerable.Range(0, 100).Select(_ => (float)random.NextDouble()).ToList();
                await index.AddAsync(id, vector);
            }

            // Create a query vector
            var query = Enumerable.Range(0, 100).Select(_ => (float)random.NextDouble()).ToList();

            var results = (await index.GetTopKAsync(query, 5)).ToList();
            Console.WriteLine($"Found {results.Count} nearest neighbors in 100D space");
            Console.WriteLine($"Test passed: {results.Count == 5}\n");
        }

        private static async Task TestLargerDatasetAsync()
        {
            Console.WriteLine("Test 7: Larger Dataset Performance");
            var index = new HNSWIndex(2, new RamHnswStorage());
            index.Seed = 42; // Set seed for reproducibility
            index.ExtendCandidates = true; // Enable extended candidates for better connectivity
            var random = new Random(42);

            // Create clusters of points
            var clusters = new[]
            {
                (center: new[] { 0f, 0f }, count: 20),
                (center: new[] { 10f, 10f }, count: 20),
                (center: new[] { -10f, 5f }, count: 20)
            };

            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Add points in a mixed order to ensure better connectivity
            var allPoints = new List<(Guid id, List<float> vector, int cluster)>();

            for (int clusterIdx = 0; clusterIdx < clusters.Length; clusterIdx++)
            {
                var cluster = clusters[clusterIdx];
                for (int i = 0; i < cluster.count; i++)
                {
                    var vector = new List<float>
                    {
                        cluster.center[0] + (float)(random.NextDouble() - 0.5),
                        cluster.center[1] + (float)(random.NextDouble() - 0.5)
                    };
                    allPoints.Add((Guid.NewGuid(), vector, clusterIdx));
                }
            }

            // Shuffle points to ensure clusters are mixed during insertion
            allPoints = allPoints.OrderBy(x => random.Next()).ToList();

            foreach (var point in allPoints)
            {
                await index.AddAsync(point.id, point.vector);
            }

            sw.Stop();
            Console.WriteLine($"Added 60 vectors in {sw.ElapsedMilliseconds}ms");

            // Search near one of the cluster centers with higher ef
            sw.Restart();
            var results = (await index.GetTopKAsync(new List<float> { 10f, 10f }, 5, ef: 400)).ToList();
            sw.Stop();

            Console.WriteLine($"Search completed in {sw.ElapsedMilliseconds}ms");
            Console.WriteLine("Nearest 5 to (10, 10):");
            foreach (var result in results)
            {
                Console.WriteLine($"  [{string.Join(", ", result.Vectors)}] - Distance: {result.Distance:F4}");
            }

            // Verify results are mostly from the (10,10) cluster
            var nearCluster = results.Count(r =>
                Math.Abs(r.Vectors[0] - 10f) < 2f &&
                Math.Abs(r.Vectors[1] - 10f) < 2f);

            Console.WriteLine($"Test passed: {nearCluster >= 4}\n");
        }

        private static async Task TestDistanceFunctionsAsync()
        {
            Console.WriteLine("Test 8: Distance Functions");

            // Test Euclidean
            var euclideanIndex = new HNSWIndex(2, new RamHnswStorage());
            euclideanIndex.DistanceFunction = new EuclideanDistance();
            await TestDistanceFunctionAsync(euclideanIndex, "Euclidean");

            // Test Cosine
            var cosineIndex = new HNSWIndex(2, new RamHnswStorage());
            cosineIndex.DistanceFunction = new CosineDistance();
            await TestDistanceFunctionAsync(cosineIndex, "Cosine");

            // Test Dot Product
            var dotIndex = new HNSWIndex(2, new RamHnswStorage());
            dotIndex.DistanceFunction = new DotProductDistance();
            await TestDistanceFunctionAsync(dotIndex, "Dot Product");

            Console.WriteLine();
        }

        private static async Task TestDistanceFunctionAsync(HNSWIndex index, string name)
        {
            var vectors = new List<(Guid, List<float>)>
            {
                (Guid.NewGuid(), new List<float> { 1.0f, 0.0f }),
                (Guid.NewGuid(), new List<float> { 0.0f, 1.0f }),
                (Guid.NewGuid(), new List<float> { 0.707f, 0.707f }),
                (Guid.NewGuid(), new List<float> { -1.0f, 0.0f })
            };

            foreach (var (id, vector) in vectors)
            {
                await index.AddAsync(id, vector);
            }

            var query = new List<float> { 1.0f, 0.0f };
            var results = (await index.GetTopKAsync(query, 2)).ToList();

            Console.WriteLine($"{name} distance - Query: [{string.Join(", ", query)}]");
            Console.WriteLine($"  Nearest: [{string.Join(", ", results[0].Vectors)}], Distance: {results[0].Distance:F4}");

            bool passed = Math.Abs(results[0].Vectors[0] - 1.0f) < 0.01f && Math.Abs(results[0].Vectors[1]) < 0.01f;
            Console.WriteLine($"  Test passed: {passed}");
        }

        private static async Task TestBatchOperationsAsync()
        {
            Console.WriteLine("Test 9: Batch Operations");
            var index = new HNSWIndex(2, new RamHnswStorage());
            var random = new Random(42);

            var batch = new List<(Guid, List<float>)>();
            for (int i = 0; i < 20; i++)
            {
                batch.Add((Guid.NewGuid(), new List<float> { (float)random.NextDouble() * 10, (float)random.NextDouble() * 10 }));
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            await index.AddBatchAsync(batch);
            sw.Stop();

            Console.WriteLine($"Batch inserted {batch.Count} vectors in {sw.ElapsedMilliseconds}ms");

            var query = new List<float> { 5.0f, 5.0f };
            var results = (await index.GetTopKAsync(query, 5)).ToList();

            Console.WriteLine($"Found {results.Count} results after batch insert");

            // Test batch remove
            var toRemove = batch.Take(10).Select(x => x.Item1).ToList();
            await index.RemoveBatchAsync(toRemove);

            results = (await index.GetTopKAsync(query, 15)).ToList();
            Console.WriteLine($"After removing 10 items, found {results.Count} results");

            Console.WriteLine($"Test passed: {results.Count == 10}\n");
        }

        private static async Task TestStateExportImportAsync()
        {
            Console.WriteLine("Test 10: State Export/Import");

            // Create and populate an index
            var originalIndex = new HNSWIndex(2, new RamHnswStorage());
            originalIndex.M = 8;
            originalIndex.MaxM = 12;
            originalIndex.DistanceFunction = new CosineDistance();

            var ids = new List<Guid>();
            for (int i = 0; i < 10; i++)
            {
                var id = Guid.NewGuid();
                ids.Add(id);
                await originalIndex.AddAsync(id, new List<float> { i * 0.1f, i * 0.2f });
            }

            // Export state
            var state = await originalIndex.ExportStateAsync();

            // Create new index and import
            var importedIndex = new HNSWIndex(2, new RamHnswStorage());
            await importedIndex.ImportStateAsync(state);

            // Test that the imported index works the same
            var query = new List<float> { 0.5f, 1.0f };
            var originalResults = (await originalIndex.GetTopKAsync(query, 3)).ToList();
            var importedResults = (await importedIndex.GetTopKAsync(query, 3)).ToList();

            bool passed = originalResults.Count == importedResults.Count;
            for (int i = 0; i < originalResults.Count && passed; i++)
            {
                passed = originalResults[i].GUID == importedResults[i].GUID &&
                         Math.Abs(originalResults[i].Distance - importedResults[i].Distance) < 0.0001f;
            }

            Console.WriteLine($"Parameters preserved: M={importedIndex.M}, MaxM={importedIndex.MaxM}, Distance={importedIndex.DistanceFunction.Name}");
            Console.WriteLine($"Results match: {passed}");
            Console.WriteLine($"Test passed: {passed}\n");
        }

        private static async Task TestValidationAsync()
        {
            Console.WriteLine("Test 11: Input Validation");

            // Test dimension validation
            try
            {
                var index = new HNSWIndex(3, new RamHnswStorage());
                await index.AddAsync(Guid.NewGuid(), new List<float> { 1.0f, 2.0f }); // Wrong dimension
                Console.WriteLine("Dimension validation: FAILED");
            }
            catch (ArgumentException)
            {
                Console.WriteLine("Dimension validation: PASSED");
            }

            // Test null vector
            try
            {
                var index = new HNSWIndex(2, new RamHnswStorage());
                await index.AddAsync(Guid.NewGuid(), null);
                Console.WriteLine("Null vector validation: FAILED");
            }
            catch (ArgumentNullException)
            {
                Console.WriteLine("Null vector validation: PASSED");
            }

            // Test parameter bounds
            try
            {
                var index = new HNSWIndex(2, new RamHnswStorage());
                index.M = -1; // Invalid
                Console.WriteLine("Parameter bounds validation: FAILED");
            }
            catch (ArgumentOutOfRangeException)
            {
                Console.WriteLine("Parameter bounds validation: PASSED");
            }

            // Test invalid dimension in constructor
            try
            {
                var index = new HNSWIndex(0, new RamHnswStorage()); // Invalid dimension
                Console.WriteLine("Constructor dimension validation: FAILED");
            }
            catch (ArgumentOutOfRangeException)
            {
                Console.WriteLine("Constructor dimension validation: PASSED");
            }

            // Test cancellation
            using (var cts = new CancellationTokenSource())
            {
                var index = new HNSWIndex(100, new RamHnswStorage());
                cts.Cancel();
                try
                {
                    await index.AddAsync(Guid.NewGuid(), Enumerable.Range(0, 100).Select(x => (float)x).ToList(), cts.Token);
                    Console.WriteLine("Cancellation test: FAILED");
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Cancellation test: PASSED");
                }
            }

            Console.WriteLine();
        }
    }
}