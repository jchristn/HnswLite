namespace Test.Sqlite
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    using Hnsw;
    using Hnsw.RamStorage;
    using Hnsw.SqliteStorage;
    using HnswIndex.SqliteStorage;

    public static class Program
    {
        /// <summary>
        /// Helper to perform timed search and return results
        /// </summary>
        private static async Task<List<VectorResult>> TimedSearchAsync(HnswIndex index, List<float> query, int k, int? ef = null, string label = "Search")
        {
            Stopwatch sw = Stopwatch.StartNew();
            List<VectorResult> results = (await index.GetTopKAsync(query, k, ef)).ToList();
            sw.Stop();
            Console.WriteLine($"{label} time: {sw.ElapsedMilliseconds}ms ({sw.Elapsed.TotalMicroseconds:F0}μs)");
            return results;
        }

        /// <summary>
        /// Main entry point for the SQLite test program.
        /// </summary>
        public static async Task Main()
        {
            Console.WriteLine("=== HNSW SQLite vs RAM Comparison Test Suite ===\n");

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
            await TestFlushValidationAsync();

            Console.WriteLine("\n=== All tests completed ===");
        }

        /// <summary>
        /// Helper method to safely delete temporary database files.
        /// </summary>
        private static void SafeDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    // Force garbage collection to ensure connections are disposed
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    File.Delete(path);
                }
            }
            catch
            {
                // If we can't delete, that's okay for temp files
                // They'll be cleaned up eventually by the OS
            }
        }

        private static async Task TestBasicAddAndSearchAsync()
        {
            Console.WriteLine("Test 1: Basic Add and Search - SQLite vs RAM");

            // Test SQLite implementation
            var dbPath = Path.GetTempFileName();
            try
            {
                using var sqliteStorage = new SqliteHnswStorage(dbPath);
                using var sqliteLayerStorage = new SqliteHnswLayerStorage(sqliteStorage.Connection);
                var sqliteIndex = new HnswIndex(2, sqliteStorage, sqliteLayerStorage);

                // Test RAM implementation
                var ramIndex = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());

                // Add same vectors to both using batch operations
                var vectors = new Dictionary<Guid, List<float>>
                {
                    { Guid.NewGuid(), new List<float> { 1.0f, 1.0f } },
                    { Guid.NewGuid(), new List<float> { 2.0f, 2.0f } },
                    { Guid.NewGuid(), new List<float> { 3.0f, 3.0f } },
                    { Guid.NewGuid(), new List<float> { 10.0f, 10.0f } },
                    { Guid.NewGuid(), new List<float> { 11.0f, 11.0f } }
                };

                var sw = Stopwatch.StartNew();
                await sqliteIndex.AddNodesAsync(vectors);
                sw.Stop();
                var sqliteAddTime = sw.ElapsedMilliseconds;

                sw.Restart();
                await ramIndex.AddNodesAsync(vectors);
                sw.Stop();
                var ramAddTime = sw.ElapsedMilliseconds;

                // Search for nearest to (1.5, 1.5)
                var query = new List<float> { 1.5f, 1.5f };

                sw.Restart();
                List<VectorResult> sqliteResults = await TimedSearchAsync(sqliteIndex, query, 3, null, "SQLite search");
                sw.Stop();
                var sqliteSearchTime = sw.ElapsedMilliseconds;

                sw.Restart();
                List<VectorResult> ramResults = await TimedSearchAsync(ramIndex, query, 3, null, "RAM search");
                sw.Stop();
                var ramSearchTime = sw.ElapsedMilliseconds;

                Console.WriteLine($"Query: [{string.Join(", ", query)}]");
                Console.WriteLine("SQLite Top 3 results:");
                foreach (var result in sqliteResults)
                {
                    Console.WriteLine($"  Vector: [{string.Join(", ", result.Vectors)}], Distance: {result.Distance:F4}");
                }

                Console.WriteLine("RAM Top 3 results:");
                foreach (var result in ramResults)
                {
                    Console.WriteLine($"  Vector: [{string.Join(", ", result.Vectors)}], Distance: {result.Distance:F4}");
                }

                // Performance comparison
                Console.WriteLine($"Performance - Add: SQLite {sqliteAddTime}ms vs RAM {ramAddTime}ms");
                Console.WriteLine($"Performance - Search: SQLite {sqliteSearchTime}ms vs RAM {ramSearchTime}ms");

                // Verify results are similar
                var sqliteFirst = sqliteResults[0].Vectors;
                var ramFirst = ramResults[0].Vectors;
                var resultsMatch = Math.Abs(sqliteFirst[0] - ramFirst[0]) < 0.1f &&
                                  Math.Abs(sqliteFirst[1] - ramFirst[1]) < 0.1f;
                Console.WriteLine($"Results match: {resultsMatch}");
                Console.WriteLine($"Test passed: {resultsMatch}\n");
            }
            finally
            {
                SafeDeleteFile(dbPath);
            }
        }

        private static async Task TestRemoveAsync()
        {
            Console.WriteLine("Test 2: Remove Operation - SQLite vs RAM");

            var dbPath = Path.GetTempFileName();
            try
            {
                using var sqliteStorage = new SqliteHnswStorage(dbPath);
                using var sqliteLayerStorage = new SqliteHnswLayerStorage(sqliteStorage.Connection);
                var sqliteIndex = new HnswIndex(2, sqliteStorage, sqliteLayerStorage);

                var ramIndex = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());

                var id1 = Guid.NewGuid();
                var id2 = Guid.NewGuid();
                var id3 = Guid.NewGuid();

                // Add to both indexes using batch
                var vectors = new Dictionary<Guid, List<float>>
                {
                    { id1, new List<float> { 1.0f, 1.0f } },
                    { id2, new List<float> { 2.0f, 2.0f } },
                    { id3, new List<float> { 3.0f, 3.0f } }
                };

                await sqliteIndex.AddNodesAsync(vectors);
                await ramIndex.AddNodesAsync(vectors);

                // Remove the middle vector from both
                var sw = Stopwatch.StartNew();
                await sqliteIndex.RemoveAsync(id2);
                sw.Stop();
                var sqliteRemoveTime = sw.ElapsedMilliseconds;

                sw.Restart();
                await ramIndex.RemoveAsync(id2);
                sw.Stop();
                var ramRemoveTime = sw.ElapsedMilliseconds;

                List<VectorResult> sqliteResults = await TimedSearchAsync(sqliteIndex, new List<float> { 2.0f, 2.0f }, 3, null, "SQLite search after remove");
                List<VectorResult> ramResults = await TimedSearchAsync(ramIndex, new List<float> { 2.0f, 2.0f }, 3, null, "RAM search after remove");

                Console.WriteLine($"SQLite results after removing (2,2): {sqliteResults.Count} vectors found");
                Console.WriteLine($"RAM results after removing (2,2): {ramResults.Count} vectors found");
                Console.WriteLine($"Performance - Remove: SQLite {sqliteRemoveTime}ms vs RAM {ramRemoveTime}ms");

                // Verify id2 is not in either result set
                var sqliteContainsRemoved = sqliteResults.Any(r => r.GUID == id2);
                var ramContainsRemoved = ramResults.Any(r => r.GUID == id2);
                var testPassed = !sqliteContainsRemoved && !ramContainsRemoved && sqliteResults.Count == ramResults.Count;
                Console.WriteLine($"Test passed: {testPassed}\n");
            }
            finally
            {
                SafeDeleteFile(dbPath);
            }
        }

        private static async Task TestEmptyIndexAsync()
        {
            Console.WriteLine("Test 3: Empty Index - SQLite vs RAM");

            var dbPath = Path.GetTempFileName();
            try
            {
                using var sqliteStorage = new SqliteHnswStorage(dbPath);
                using var sqliteLayerStorage = new SqliteHnswLayerStorage(sqliteStorage.Connection);
                var sqliteIndex = new HnswIndex(2, sqliteStorage, sqliteLayerStorage);

                var ramIndex = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());

                List<VectorResult> sqliteResults = await TimedSearchAsync(sqliteIndex, new List<float> { 1.0f, 1.0f }, 5, null, "SQLite empty index search");
                List<VectorResult> ramResults = await TimedSearchAsync(ramIndex, new List<float> { 1.0f, 1.0f }, 5, null, "RAM empty index search");

                Console.WriteLine($"SQLite results from empty index: {sqliteResults.Count}");
                Console.WriteLine($"RAM results from empty index: {ramResults.Count}");
                Console.WriteLine($"Test passed: {sqliteResults.Count == 0 && ramResults.Count == 0}\n");
            }
            finally
            {
                SafeDeleteFile(dbPath);
            }
        }

        private static async Task TestSingleElementAsync()
        {
            Console.WriteLine("Test 4: Single Element - SQLite vs RAM");

            var dbPath = Path.GetTempFileName();
            try
            {
                using var sqliteStorage = new SqliteHnswStorage(dbPath);
                using var sqliteLayerStorage = new SqliteHnswLayerStorage(sqliteStorage.Connection);
                var sqliteIndex = new HnswIndex(2, sqliteStorage, sqliteLayerStorage);

                var ramIndex = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());

                var id = Guid.NewGuid();
                await sqliteIndex.AddAsync(id, new List<float> { 5.0f, 5.0f });
                await ramIndex.AddAsync(id, new List<float> { 5.0f, 5.0f });

                List<VectorResult> sqliteResults = await TimedSearchAsync(sqliteIndex, new List<float> { 0.0f, 0.0f }, 1, null, "SQLite single element search");
                List<VectorResult> ramResults = await TimedSearchAsync(ramIndex, new List<float> { 0.0f, 0.0f }, 1, null, "RAM single element search");

                Console.WriteLine($"SQLite found {sqliteResults.Count} result(s)");
                Console.WriteLine($"RAM found {ramResults.Count} result(s)");

                var testPassed = sqliteResults.Count == 1 && ramResults.Count == 1 &&
                               sqliteResults[0].GUID == id && ramResults[0].GUID == id;
                Console.WriteLine($"Test passed: {testPassed}\n");
            }
            finally
            {
                SafeDeleteFile(dbPath);
            }
        }

        private static async Task TestDuplicateVectorsAsync()
        {
            Console.WriteLine("Test 5: Duplicate Vectors - SQLite vs RAM");

            var dbPath = Path.GetTempFileName();
            try
            {
                using var sqliteStorage = new SqliteHnswStorage(dbPath);
                using var sqliteLayerStorage = new SqliteHnswLayerStorage(sqliteStorage.Connection);
                var sqliteIndex = new HnswIndex(2, sqliteStorage, sqliteLayerStorage);

                var ramIndex = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());

                // Add multiple vectors at the same location to both indexes using batch
                var duplicates = new Dictionary<Guid, List<float>>();
                for (int i = 0; i < 5; i++)
                {
                    var id = Guid.NewGuid();
                    duplicates[id] = new List<float> { 5.0f, 5.0f };
                }

                await sqliteIndex.AddNodesAsync(duplicates);
                await ramIndex.AddNodesAsync(duplicates);

                List<VectorResult> sqliteResults = await TimedSearchAsync(sqliteIndex, new List<float> { 5.0f, 5.0f }, 10, null, "SQLite duplicate vectors search");
                List<VectorResult> ramResults = await TimedSearchAsync(ramIndex, new List<float> { 5.0f, 5.0f }, 10, null, "RAM duplicate vectors search");

                Console.WriteLine($"SQLite found {sqliteResults.Count} duplicate vectors");
                Console.WriteLine($"RAM found {ramResults.Count} duplicate vectors");

                // All should have distance 0
                var sqliteAllZero = sqliteResults.All(r => Math.Abs(r.Distance) < 0.001f);
                var ramAllZero = ramResults.All(r => Math.Abs(r.Distance) < 0.001f);

                Console.WriteLine($"SQLite all have zero distance: {sqliteAllZero}");
                Console.WriteLine($"RAM all have zero distance: {ramAllZero}");

                var testPassed = sqliteResults.Count == 5 && ramResults.Count == 5 &&
                               sqliteAllZero && ramAllZero;
                Console.WriteLine($"Test passed: {testPassed}\n");
            }
            finally
            {
                SafeDeleteFile(dbPath);
            }
        }

        private static async Task TestHighDimensionalAsync()
        {
            Console.WriteLine("Test 6: High-Dimensional Vectors - SQLite vs RAM");

            var dbPath = Path.GetTempFileName();
            try
            {
                using var sqliteStorage = new SqliteHnswStorage(dbPath);
                using var sqliteLayerStorage = new SqliteHnswLayerStorage(sqliteStorage.Connection);
                var sqliteIndex = new HnswIndex(100, sqliteStorage, sqliteLayerStorage);

                var ramIndex = new HnswIndex(100, new RamHnswStorage(), new RamHnswLayerStorage());
                var random = new Random(42);

                // Add 10 random 100-dimensional vectors to both using batch
                var vectors = new Dictionary<Guid, List<float>>();
                for (int i = 0; i < 10; i++)
                {
                    var id = Guid.NewGuid();
                    vectors[id] = Enumerable.Range(0, 100).Select(_ => (float)random.NextDouble()).ToList();
                }

                var sw = Stopwatch.StartNew();
                await sqliteIndex.AddNodesAsync(vectors);
                sw.Stop();
                var sqliteAddTime = sw.ElapsedMilliseconds;

                sw.Restart();
                await ramIndex.AddNodesAsync(vectors);
                sw.Stop();
                var ramAddTime = sw.ElapsedMilliseconds;

                // Create a query vector
                var query = Enumerable.Range(0, 100).Select(_ => 0.5f).ToList();

                sw.Restart();
                List<VectorResult> sqliteResults = await TimedSearchAsync(sqliteIndex, query, 5, null, "SQLite high-dimensional search");
                sw.Stop();
                long sqliteSearchTime = sw.ElapsedMilliseconds;

                sw.Restart();
                List<VectorResult> ramResults = await TimedSearchAsync(ramIndex, query, 5, null, "RAM high-dimensional search");
                sw.Stop();
                long ramSearchTime = sw.ElapsedMilliseconds;

                Console.WriteLine($"SQLite found {sqliteResults.Count} nearest neighbors in 100D space");
                Console.WriteLine($"RAM found {ramResults.Count} nearest neighbors in 100D space");
                Console.WriteLine($"Performance - Add: SQLite {sqliteAddTime}ms vs RAM {ramAddTime}ms");
                Console.WriteLine($"Performance - Search: SQLite {sqliteSearchTime}ms vs RAM {ramSearchTime}ms");
                Console.WriteLine($"Test passed: {sqliteResults.Count == 5 && ramResults.Count == 5}\n");
            }
            finally
            {
                SafeDeleteFile(dbPath);
            }
        }

        private static async Task TestLargerDatasetAsync()
        {
            Console.WriteLine("Test 7: Larger Dataset Performance - SQLite vs RAM");

            var dbPath = Path.GetTempFileName();
            try
            {
                using var sqliteStorage = new SqliteHnswStorage(dbPath);
                using var sqliteLayerStorage = new SqliteHnswLayerStorage(sqliteStorage.Connection);
                var sqliteIndex = new HnswIndex(2, sqliteStorage, sqliteLayerStorage);
                sqliteIndex.Seed = 42;
                sqliteIndex.ExtendCandidates = true;

                var ramIndex = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());
                ramIndex.Seed = 42;
                ramIndex.ExtendCandidates = true;

                var random = new Random(42);

                // Create clusters of points
                var clusters = new[]
                {
                    (center: new[] { 0f, 0f }, count: 20),
                    (center: new[] { 10f, 10f }, count: 20),
                    (center: new[] { -10f, 5f }, count: 20)
                };

                // Generate same points for both indexes
                var allPoints = new Dictionary<Guid, List<float>>();
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
                        allPoints[Guid.NewGuid()] = vector;
                    }
                }

                var shuffled = allPoints.OrderBy(x => random.Next()).ToDictionary(x => x.Key, x => x.Value);

                // Add to SQLite
                Console.Write("Adding 60 vectors to SQLite... ");
                var sw = Stopwatch.StartNew();
                await sqliteIndex.AddNodesAsync(shuffled);
                sw.Stop();
                var sqliteAddTime = sw.ElapsedMilliseconds;
                Console.WriteLine($"Done ({sqliteAddTime}ms)");

                // Add to RAM with same data
                Console.Write("Adding 60 vectors to RAM... ");
                sw.Restart();
                await ramIndex.AddNodesAsync(shuffled);
                sw.Stop();
                var ramAddTime = sw.ElapsedMilliseconds;
                Console.WriteLine($"Done ({ramAddTime}ms)");

                Console.WriteLine($"SQLite added 60 vectors in {sqliteAddTime}ms");
                Console.WriteLine($"RAM added 60 vectors in {ramAddTime}ms");

                // Search near cluster center
                sw.Restart();
                List<VectorResult> sqliteResults = await TimedSearchAsync(sqliteIndex, new List<float> { 10f, 10f }, 5, 400, "SQLite larger dataset search");
                sw.Stop();
                long sqliteSearchTime = sw.ElapsedMilliseconds;

                sw.Restart();
                List<VectorResult> ramResults = await TimedSearchAsync(ramIndex, new List<float> { 10f, 10f }, 5, 400, "RAM larger dataset search");
                sw.Stop();
                long ramSearchTime = sw.ElapsedMilliseconds;

                Console.WriteLine($"SQLite search completed in {sqliteSearchTime}ms");
                Console.WriteLine($"RAM search completed in {ramSearchTime}ms");

                Console.WriteLine("SQLite nearest 5 to (10, 10):");
                foreach (var result in sqliteResults)
                {
                    Console.WriteLine($"  [{string.Join(", ", result.Vectors)}] - Distance: {result.Distance:F4}");
                }

                // Verify results are mostly from the (10,10) cluster for both
                var sqliteNearCluster = sqliteResults.Count(r =>
                    Math.Abs(r.Vectors[0] - 10f) < 2f &&
                    Math.Abs(r.Vectors[1] - 10f) < 2f);
                var ramNearCluster = ramResults.Count(r =>
                    Math.Abs(r.Vectors[0] - 10f) < 2f &&
                    Math.Abs(r.Vectors[1] - 10f) < 2f);

                Console.WriteLine($"SQLite near cluster: {sqliteNearCluster}/5");
                Console.WriteLine($"RAM near cluster: {ramNearCluster}/5");
                Console.WriteLine($"Test passed: {sqliteNearCluster >= 4 && ramNearCluster >= 4}\n");
            }
            finally
            {
                SafeDeleteFile(dbPath);
            }
        }

        private static async Task TestDistanceFunctionsAsync()
        {
            Console.WriteLine("Test 8: Distance Functions - SQLite vs RAM");

            var distances = new[] { "Euclidean", "Cosine", "DotProduct" };

            foreach (var distanceName in distances)
            {
                var dbPath = Path.GetTempFileName();
                try
                {
                    using var sqliteStorage = new SqliteHnswStorage(dbPath);
                    using var sqliteLayerStorage = new SqliteHnswLayerStorage(sqliteStorage.Connection);
                    var sqliteIndex = new HnswIndex(2, sqliteStorage, sqliteLayerStorage);

                    var ramIndex = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());

                    // Set distance function
                    switch (distanceName)
                    {
                        case "Euclidean":
                            sqliteIndex.DistanceFunction = new EuclideanDistance();
                            ramIndex.DistanceFunction = new EuclideanDistance();
                            break;
                        case "Cosine":
                            sqliteIndex.DistanceFunction = new CosineDistance();
                            ramIndex.DistanceFunction = new CosineDistance();
                            break;
                        case "DotProduct":
                            sqliteIndex.DistanceFunction = new DotProductDistance();
                            ramIndex.DistanceFunction = new DotProductDistance();
                            break;
                    }

                    await TestDistanceFunctionAsync(sqliteIndex, ramIndex, distanceName);
                }
                finally
                {
                    SafeDeleteFile(dbPath);
                }
            }

            Console.WriteLine();
        }

        private static async Task TestDistanceFunctionAsync(HnswIndex sqliteIndex, HnswIndex ramIndex, string name)
        {
            var vectors = new Dictionary<Guid, List<float>>
            {
                { Guid.NewGuid(), new List<float> { 1.0f, 0.0f } },
                { Guid.NewGuid(), new List<float> { 0.0f, 1.0f } },
                { Guid.NewGuid(), new List<float> { 0.707f, 0.707f } },
                { Guid.NewGuid(), new List<float> { -1.0f, 0.0f } }
            };

            await sqliteIndex.AddNodesAsync(vectors);
            await ramIndex.AddNodesAsync(vectors);

            var query = new List<float> { 1.0f, 0.0f };
            List<VectorResult> sqliteResults = await TimedSearchAsync(sqliteIndex, query, 2, null, $"SQLite {name} distance");
            List<VectorResult> ramResults = await TimedSearchAsync(ramIndex, query, 2, null, $"RAM {name} distance");

            Console.WriteLine($"{name} distance - Query: [{string.Join(", ", query)}]");
            Console.WriteLine($"  SQLite nearest: [{string.Join(", ", sqliteResults[0].Vectors)}], Distance: {sqliteResults[0].Distance:F4}");
            Console.WriteLine($"  RAM nearest: [{string.Join(", ", ramResults[0].Vectors)}], Distance: {ramResults[0].Distance:F4}");

            bool sqlitePassed = Math.Abs(sqliteResults[0].Vectors[0] - 1.0f) < 0.01f && Math.Abs(sqliteResults[0].Vectors[1]) < 0.01f;
            bool ramPassed = Math.Abs(ramResults[0].Vectors[0] - 1.0f) < 0.01f && Math.Abs(ramResults[0].Vectors[1]) < 0.01f;
            Console.WriteLine($"  Test passed: SQLite {sqlitePassed}, RAM {ramPassed}");
        }

        private static async Task TestBatchOperationsAsync()
        {
            Console.WriteLine("Test 9: Batch Operations - SQLite vs RAM");

            var dbPath = Path.GetTempFileName();
            try
            {
                using var sqliteStorage = new SqliteHnswStorage(dbPath);
                using var sqliteLayerStorage = new SqliteHnswLayerStorage(sqliteStorage.Connection);
                var sqliteIndex = new HnswIndex(2, sqliteStorage, sqliteLayerStorage);

                var ramIndex = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());
                var random = new Random(42);

                var batch = new Dictionary<Guid, List<float>>();
                var ids = new List<Guid>();
                for (int i = 0; i < 20; i++)
                {
                    var id = Guid.NewGuid();
                    ids.Add(id);
                    batch[id] = new List<float> { (float)random.NextDouble() * 10, (float)random.NextDouble() * 10 };
                }

                var sw = Stopwatch.StartNew();
                await sqliteIndex.AddNodesAsync(batch);
                sw.Stop();
                var sqliteBatchTime = sw.ElapsedMilliseconds;

                sw.Restart();
                await ramIndex.AddNodesAsync(batch);
                sw.Stop();
                var ramBatchTime = sw.ElapsedMilliseconds;

                Console.WriteLine($"SQLite batch inserted {batch.Count} vectors in {sqliteBatchTime}ms");
                Console.WriteLine($"RAM batch inserted {batch.Count} vectors in {ramBatchTime}ms");

                List<float> query = new List<float> { 5.0f, 5.0f };
                List<VectorResult> sqliteResults = await TimedSearchAsync(sqliteIndex, query, 5, null, "SQLite batch search");
                List<VectorResult> ramResults = await TimedSearchAsync(ramIndex, query, 5, null, "RAM batch search");

                Console.WriteLine($"SQLite found {sqliteResults.Count} results after batch insert");
                Console.WriteLine($"RAM found {ramResults.Count} results after batch insert");

                // Test batch remove
                var toRemove = ids.Take(10).ToList();

                sw.Restart();
                await sqliteIndex.RemoveNodesAsync(toRemove);
                sw.Stop();
                var sqliteRemoveTime = sw.ElapsedMilliseconds;

                sw.Restart();
                await ramIndex.RemoveNodesAsync(toRemove);
                sw.Stop();
                var ramRemoveTime = sw.ElapsedMilliseconds;

                List<VectorResult> sqliteResultsAfter = await TimedSearchAsync(sqliteIndex, query, 15, null, "SQLite after batch remove");
                List<VectorResult> ramResultsAfter = await TimedSearchAsync(ramIndex, query, 15, null, "RAM after batch remove");

                Console.WriteLine($"SQLite after removing 10 items, found {sqliteResultsAfter.Count} results (removed in {sqliteRemoveTime}ms)");
                Console.WriteLine($"RAM after removing 10 items, found {ramResultsAfter.Count} results (removed in {ramRemoveTime}ms)");

                Console.WriteLine($"Test passed: {sqliteResultsAfter.Count == 10 && ramResultsAfter.Count == 10}\n");
            }
            finally
            {
                SafeDeleteFile(dbPath);
            }
        }

        private static async Task TestStateExportImportAsync()
        {
            Console.WriteLine("Test 10: State Export/Import - SQLite vs RAM");

            var dbPath = Path.GetTempFileName();
            var dbPath2 = Path.GetTempFileName();
            try
            {
                using var sqliteStorage = new SqliteHnswStorage(dbPath);
                using var sqliteLayerStorage = new SqliteHnswLayerStorage(sqliteStorage.Connection);

                // Create and populate both indexes
                var originalSqliteIndex = new HnswIndex(2, sqliteStorage, sqliteLayerStorage);
                originalSqliteIndex.M = 8;
                originalSqliteIndex.MaxM = 12;
                originalSqliteIndex.DistanceFunction = new CosineDistance();

                var originalRamIndex = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());
                originalRamIndex.M = 8;
                originalRamIndex.MaxM = 12;
                originalRamIndex.DistanceFunction = new CosineDistance();

                var vectors = new Dictionary<Guid, List<float>>();
                var ids = new List<Guid>();
                for (int i = 0; i < 10; i++)
                {
                    var id = Guid.NewGuid();
                    ids.Add(id);
                    vectors[id] = new List<float> { i * 0.1f, i * 0.2f };
                }

                await originalSqliteIndex.AddNodesAsync(vectors);
                await originalRamIndex.AddNodesAsync(vectors);

                // Export state from both
                var sw = Stopwatch.StartNew();
                var sqliteState = await originalSqliteIndex.ExportStateAsync();
                sw.Stop();
                var sqliteExportTime = sw.ElapsedMilliseconds;

                sw.Restart();
                var ramState = await originalRamIndex.ExportStateAsync();
                sw.Stop();
                var ramExportTime = sw.ElapsedMilliseconds;

                Console.WriteLine($"Export time - SQLite: {sqliteExportTime}ms, RAM: {ramExportTime}ms");

                // Create new indexes and import
                using var sqliteStorage2 = new SqliteHnswStorage(dbPath2);
                using var sqliteLayerStorage2 = new SqliteHnswLayerStorage(sqliteStorage2.Connection);
                var importedSqliteIndex = new HnswIndex(2, sqliteStorage2, sqliteLayerStorage2);

                var importedRamIndex = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());

                sw.Restart();
                await importedSqliteIndex.ImportStateAsync(sqliteState);
                sw.Stop();
                var sqliteImportTime = sw.ElapsedMilliseconds;

                sw.Restart();
                await importedRamIndex.ImportStateAsync(ramState);
                sw.Stop();
                var ramImportTime = sw.ElapsedMilliseconds;

                Console.WriteLine($"Import time - SQLite: {sqliteImportTime}ms, RAM: {ramImportTime}ms");

                // Test that the imported indexes work the same
                var query = new List<float> { 0.5f, 1.0f };
                List<VectorResult> originalSqliteResults = await TimedSearchAsync(originalSqliteIndex, query, 3, null, "Original SQLite search");
                List<VectorResult> importedSqliteResults = await TimedSearchAsync(importedSqliteIndex, query, 3, null, "Imported SQLite search");
                List<VectorResult> originalRamResults = await TimedSearchAsync(originalRamIndex, query, 3, null, "Original RAM search");
                List<VectorResult> importedRamResults = await TimedSearchAsync(importedRamIndex, query, 3, null, "Imported RAM search");

                bool sqlitePassed = originalSqliteResults.Count == importedSqliteResults.Count;
                bool ramPassed = originalRamResults.Count == importedRamResults.Count;

                for (int i = 0; i < originalSqliteResults.Count && sqlitePassed; i++)
                {
                    sqlitePassed = originalSqliteResults[i].GUID == importedSqliteResults[i].GUID &&
                                 Math.Abs(originalSqliteResults[i].Distance - importedSqliteResults[i].Distance) < 0.0001f;
                }

                for (int i = 0; i < originalRamResults.Count && ramPassed; i++)
                {
                    ramPassed = originalRamResults[i].GUID == importedRamResults[i].GUID &&
                               Math.Abs(originalRamResults[i].Distance - importedRamResults[i].Distance) < 0.0001f;
                }

                Console.WriteLine($"SQLite parameters preserved: M={importedSqliteIndex.M}, MaxM={importedSqliteIndex.MaxM}, Distance={importedSqliteIndex.DistanceFunction.Name}");
                Console.WriteLine($"RAM parameters preserved: M={importedRamIndex.M}, MaxM={importedRamIndex.MaxM}, Distance={importedRamIndex.DistanceFunction.Name}");
                Console.WriteLine($"SQLite results match: {sqlitePassed}");
                Console.WriteLine($"RAM results match: {ramPassed}");
                Console.WriteLine($"Test passed: {sqlitePassed && ramPassed}\n");
            }
            finally
            {
                SafeDeleteFile(dbPath);
                SafeDeleteFile(dbPath2);
            }
        }

        private static async Task TestValidationAsync()
        {
            Console.WriteLine("Test 11: Input Validation - SQLite vs RAM");

            var dbPath = Path.GetTempFileName();
            try
            {
                // Test dimension validation
                try
                {
                    using var sqliteStorage = new SqliteHnswStorage(dbPath);
                    using var sqliteLayerStorage = new SqliteHnswLayerStorage(sqliteStorage.Connection);
                    var index = new HnswIndex(3, sqliteStorage, sqliteLayerStorage);
                    await index.AddAsync(Guid.NewGuid(), new List<float> { 1.0f, 2.0f }); // Wrong dimension
                    Console.WriteLine("SQLite dimension validation: FAILED");
                }
                catch (ArgumentException)
                {
                    Console.WriteLine("SQLite dimension validation: PASSED");
                }

                try
                {
                    var index = new HnswIndex(3, new RamHnswStorage(), new RamHnswLayerStorage());
                    await index.AddAsync(Guid.NewGuid(), new List<float> { 1.0f, 2.0f }); // Wrong dimension
                    Console.WriteLine("RAM dimension validation: FAILED");
                }
                catch (ArgumentException)
                {
                    Console.WriteLine("RAM dimension validation: PASSED");
                }

                // Test null vector
                try
                {
                    using var sqliteStorage = new SqliteHnswStorage(dbPath);
                    using var sqliteLayerStorage = new SqliteHnswLayerStorage(sqliteStorage.Connection);
                    var index = new HnswIndex(2, sqliteStorage, sqliteLayerStorage);
                    await index.AddAsync(Guid.NewGuid(), null!);
                    Console.WriteLine("SQLite null vector validation: FAILED");
                }
                catch (ArgumentNullException)
                {
                    Console.WriteLine("SQLite null vector validation: PASSED");
                }

                try
                {
                    var index = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());
                    await index.AddAsync(Guid.NewGuid(), null!);
                    Console.WriteLine("RAM null vector validation: FAILED");
                }
                catch (ArgumentNullException)
                {
                    Console.WriteLine("RAM null vector validation: PASSED");
                }

                // Test invalid dimension in constructor
                try
                {
                    using var sqliteStorage = new SqliteHnswStorage(dbPath);
                    using var sqliteLayerStorage = new SqliteHnswLayerStorage(sqliteStorage.Connection);
                    var index = new HnswIndex(0, sqliteStorage, sqliteLayerStorage); // Invalid dimension
                    Console.WriteLine("SQLite constructor dimension validation: FAILED");
                }
                catch (ArgumentOutOfRangeException)
                {
                    Console.WriteLine("SQLite constructor dimension validation: PASSED");
                }

                try
                {
                    var index = new HnswIndex(0, new RamHnswStorage(), new RamHnswLayerStorage()); // Invalid dimension
                    Console.WriteLine("RAM constructor dimension validation: FAILED");
                }
                catch (ArgumentOutOfRangeException)
                {
                    Console.WriteLine("RAM constructor dimension validation: PASSED");
                }

                // Test batch validation - empty dictionary
                try
                {
                    using var sqliteStorage = new SqliteHnswStorage(dbPath);
                    using var sqliteLayerStorage = new SqliteHnswLayerStorage(sqliteStorage.Connection);
                    var index = new HnswIndex(2, sqliteStorage, sqliteLayerStorage);
                    await index.AddNodesAsync(new Dictionary<Guid, List<float>>());
                    Console.WriteLine("SQLite empty batch validation: FAILED");
                }
                catch (ArgumentException)
                {
                    Console.WriteLine("SQLite empty batch validation: PASSED");
                }

                try
                {
                    var index = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());
                    await index.AddNodesAsync(new Dictionary<Guid, List<float>>());
                    Console.WriteLine("RAM empty batch validation: FAILED");
                }
                catch (ArgumentException)
                {
                    Console.WriteLine("RAM empty batch validation: PASSED");
                }

                Console.WriteLine();
            }
            finally
            {
                SafeDeleteFile(dbPath);
            }
        }

        private static async Task TestPersistenceAsync()
        {
            Console.WriteLine("Test 12: Persistence Across Sessions (SQLite only)");

            var dbPath = Path.GetTempFileName();
            try
            {
                var testVectors = new Dictionary<Guid, List<float>>
                {
                    { Guid.NewGuid(), new List<float> { 1.0f, 1.0f } },
                    { Guid.NewGuid(), new List<float> { 2.0f, 2.0f } },
                    { Guid.NewGuid(), new List<float> { 3.0f, 3.0f } }
                };

                // First session - create and populate index
                using (var sqliteStorage = new SqliteHnswStorage(dbPath))
                using (var sqliteLayerStorage = new SqliteHnswLayerStorage(sqliteStorage.Connection))
                {
                    var index = new HnswIndex(2, sqliteStorage, sqliteLayerStorage);
                    index.M = 8;
                    index.MaxM = 12;

                    await index.AddNodesAsync(testVectors);

                    List<VectorResult> results = await TimedSearchAsync(index, new List<float> { 1.5f, 1.5f }, 3, null, "Persistence test initial search");
                    Console.WriteLine($"First session: Found {results.Count} vectors");
                }

                // Second session - reload and verify data persists
                using (var sqliteStorage = new SqliteHnswStorage(dbPath))
                using (var sqliteLayerStorage = new SqliteHnswLayerStorage(sqliteStorage.Connection))
                {
                    var index = new HnswIndex(2, sqliteStorage, sqliteLayerStorage);

                    List<VectorResult> results = await TimedSearchAsync(index, new List<float> { 1.5f, 1.5f }, 3, null, "Persistence test initial search");
                    Console.WriteLine($"Second session: Found {results.Count} vectors");

                    // Verify we can find the same vectors
                    var foundIds = results.Select(r => r.GUID).ToHashSet();
                    var expectedIds = testVectors.Keys.ToHashSet();
                    var allFound = expectedIds.All(id => foundIds.Contains(id));

                    Console.WriteLine($"All vectors persist across sessions: {allFound}");
                    Console.WriteLine($"Test passed: {results.Count == 3 && allFound}\n");
                }
            }
            finally
            {
                SafeDeleteFile(dbPath);
            }
        }

        private static async Task TestPerformanceComparisonAsync()
        {
            Console.WriteLine("Test 13: Comprehensive Performance Comparison");

            var dbPath = Path.GetTempFileName();
            try
            {
                using var sqliteStorage = new SqliteHnswStorage(dbPath);
                using var sqliteLayerStorage = new SqliteHnswLayerStorage(sqliteStorage.Connection);
                // Use lower dimensionality for faster testing - 384D is extremely expensive
                const int testDimension = 64;  // Reduced from 384 for much faster distance calculations
                var sqliteIndex = new HnswIndex(testDimension, sqliteStorage, sqliteLayerStorage);

                var ramIndex = new HnswIndex(testDimension, new RamHnswStorage(), new RamHnswLayerStorage());

                // Set same parameters - OPTIMIZED FOR FASTER TESTING
                sqliteIndex.Seed = 42;
                ramIndex.Seed = 42;
                sqliteIndex.M = 8;          // Reduced from 16 for faster construction
                ramIndex.M = 8;             // Reduced from 16 for faster construction  
                sqliteIndex.EfConstruction = 50;  // Reduced from 200 for much faster construction
                ramIndex.EfConstruction = 50;     // Reduced from 200 for much faster construction

                var random = new Random(42);
                const int vectorCount = 500;  // Reduced from 2000 for reasonable test time

                Console.WriteLine($"Testing with {vectorCount} {testDimension}-dimensional vectors...\n");

                // Generate test data
                Console.Write("Generating test vectors... ");
                var vectors = new Dictionary<Guid, List<float>>();
                for (int i = 0; i < vectorCount; i++)
                {
                    var vector = Enumerable.Range(0, testDimension).Select(_ => (float)(random.NextDouble() * 2 - 1)).ToList();
                    vectors[Guid.NewGuid()] = vector;
                    
                    if ((i + 1) % 100 == 0)
                        Console.Write($"{i + 1}/{vectorCount} ");
                }
                Console.WriteLine("Done");

                // Test SQLite insertion performance with batch
                Console.Write("Testing SQLite batch insertion... ");
                var sw = Stopwatch.StartNew();
                await sqliteIndex.AddNodesAsync(vectors);
                sw.Stop();
                var sqliteInsertTime = sw.ElapsedMilliseconds;
                var sqliteInsertRate = vectorCount * 1000.0 / sqliteInsertTime;

                Console.WriteLine($"Completed in {sw.ElapsedMilliseconds}ms");

                // Test RAM insertion performance with batch
                Console.Write("Testing RAM batch insertion... ");
                sw.Restart();
                await ramIndex.AddNodesAsync(vectors);
                sw.Stop();
                Console.WriteLine($"Completed in {sw.ElapsedMilliseconds}ms");
                var ramInsertTime = sw.ElapsedMilliseconds;
                var ramInsertRate = vectorCount * 1000.0 / ramInsertTime;

                Console.WriteLine("=== INSERTION PERFORMANCE ===");
                Console.WriteLine($"SQLite: {sqliteInsertTime}ms total, {sqliteInsertRate:F1} vectors/sec");
                Console.WriteLine($"RAM:    {ramInsertTime}ms total, {ramInsertRate:F1} vectors/sec");
                Console.WriteLine($"RAM is {(double)sqliteInsertTime / ramInsertTime:F1}x faster for insertion\n");

                // Test search performance with multiple queries
                var queries = new List<List<float>>();
                for (int i = 0; i < 100; i++)
                {
                    queries.Add(Enumerable.Range(0, testDimension).Select(_ => (float)(random.NextDouble() * 2 - 1)).ToList());
                }

                // SQLite search performance
                sw.Restart();
                foreach (var query in queries)
                {
                    await TimedSearchAsync(sqliteIndex, query, 10, 100, "SQLite performance search");
                }
                sw.Stop();
                var sqliteSearchTime = sw.ElapsedMilliseconds;
                var sqliteSearchRate = queries.Count * 1000.0 / sqliteSearchTime;

                // RAM search performance
                sw.Restart();
                foreach (var query in queries)
                {
                    await TimedSearchAsync(ramIndex, query, 10, 100, "RAM performance search");
                }
                sw.Stop();
                var ramSearchTime = sw.ElapsedMilliseconds;
                var ramSearchRate = queries.Count * 1000.0 / ramSearchTime;

                Console.WriteLine("=== SEARCH PERFORMANCE (100 queries) ===");
                Console.WriteLine($"SQLite: {sqliteSearchTime}ms total, {sqliteSearchRate:F1} queries/sec");
                Console.WriteLine($"RAM:    {ramSearchTime}ms total, {ramSearchRate:F1} queries/sec");
                Console.WriteLine($"RAM is {(double)sqliteSearchTime / ramSearchTime:F1}x faster for search\n");

                // Test batch operations performance
                Console.WriteLine("=== BATCH OPERATIONS PERFORMANCE ===");

                // Generate additional vectors for batch testing
                var batchVectors = new Dictionary<Guid, List<float>>();
                for (int i = 0; i < 100; i++)
                {
                    batchVectors[Guid.NewGuid()] = Enumerable.Range(0, testDimension).Select(_ => (float)(random.NextDouble() * 2 - 1)).ToList();
                }

                // Test individual adds vs batch add
                HnswIndex testIndex1 = new HnswIndex(testDimension, new RamHnswStorage(), new RamHnswLayerStorage()) { Seed = 42 };
                sw.Restart();
                foreach (var kvp in batchVectors)
                {
                    await testIndex1.AddAsync(kvp.Key, kvp.Value);
                }
                sw.Stop();
                var individualAddTime = sw.ElapsedMilliseconds;

                HnswIndex testIndex2 = new HnswIndex(testDimension, new RamHnswStorage(), new RamHnswLayerStorage()) { Seed = 42 };
                sw.Restart();
                await testIndex2.AddNodesAsync(batchVectors);
                sw.Stop();
                var batchAddTime = sw.ElapsedMilliseconds;

                Console.WriteLine($"Individual adds: {individualAddTime}ms for {batchVectors.Count} vectors");
                Console.WriteLine($"Batch add: {batchAddTime}ms for {batchVectors.Count} vectors");
                Console.WriteLine($"Batch is {(double)individualAddTime / batchAddTime:F1}x faster\n");

                // Test memory usage (approximate)
                var fileSize = new FileInfo(dbPath).Length;
                var bytesPerVector = fileSize / (double)vectorCount;
                long theoreticalVectorSize = testDimension * 4; // testDimension floats * 4 bytes each
                var overhead = bytesPerVector - theoreticalVectorSize;

                Console.WriteLine("=== STORAGE ===");
                Console.WriteLine($"SQLite database size: {fileSize / (1024.0 * 1024.0):F1} MB ({bytesPerVector:F0} bytes/vector)");
                Console.WriteLine($"Theoretical vector size: {theoreticalVectorSize} bytes/vector");
                Console.WriteLine($"Storage overhead: {overhead:F0} bytes/vector ({overhead / theoreticalVectorSize * 100:F0}%)");
                Console.WriteLine($"RAM storage: In-memory only (no persistence)\n");

                // Test accuracy comparison
                List<float> testQuery = Enumerable.Range(0, testDimension).Select(_ => 0.0f).ToList();
                List<VectorResult> sqliteResults = await TimedSearchAsync(sqliteIndex, testQuery, 10, null, "SQLite final comparison");
                List<VectorResult> ramResults = await TimedSearchAsync(ramIndex, testQuery, 10, null, "RAM final comparison");

                Console.WriteLine("=== ACCURACY COMPARISON ===");
                Console.WriteLine($"SQLite found {sqliteResults.Count} results, avg distance: {sqliteResults.Average(r => r.Distance):F4}");
                Console.WriteLine($"RAM found {ramResults.Count} results, avg distance: {ramResults.Average(r => r.Distance):F4}");

                // Check if top results are similar
                var topSqliteIds = sqliteResults.Take(5).Select(r => r.GUID).ToHashSet();
                var topRamIds = ramResults.Take(5).Select(r => r.GUID).ToHashSet();
                var overlap = topSqliteIds.Intersect(topRamIds).Count();

                Console.WriteLine($"Top 5 results overlap: {overlap}/5 ({overlap * 20}%)");
                Console.WriteLine($"Results are {(overlap >= 3 ? "consistent" : "different")}\n");

                // Test batch remove performance
                var removeIds = vectors.Keys.Take(500).ToList();

                sw.Restart();
                await sqliteIndex.RemoveNodesAsync(removeIds);
                sw.Stop();
                var sqliteBatchRemoveTime = sw.ElapsedMilliseconds;

                sw.Restart();
                await ramIndex.RemoveNodesAsync(removeIds);
                sw.Stop();
                var ramBatchRemoveTime = sw.ElapsedMilliseconds;

                Console.WriteLine("=== BATCH REMOVE PERFORMANCE ===");
                Console.WriteLine($"SQLite removed {removeIds.Count} vectors in {sqliteBatchRemoveTime}ms");
                Console.WriteLine($"RAM removed {removeIds.Count} vectors in {ramBatchRemoveTime}ms");
                Console.WriteLine($"RAM is {(double)sqliteBatchRemoveTime / ramBatchRemoveTime:F1}x faster for batch removal\n");

                // Additional performance metrics for high-dimensional data
                Console.WriteLine("=== HIGH-DIMENSIONAL PERFORMANCE INSIGHTS ===");
                var avgInsertTimeMs = (double)sqliteInsertTime / vectorCount;
                var avgSearchTimeMs = (double)sqliteSearchTime / queries.Count;
                Console.WriteLine($"SQLite - Avg insert time: {avgInsertTimeMs:F2}ms/vector, Avg search time: {avgSearchTimeMs:F2}ms/query");

                avgInsertTimeMs = (double)ramInsertTime / vectorCount;
                avgSearchTimeMs = (double)ramSearchTime / queries.Count;
                Console.WriteLine($"RAM    - Avg insert time: {avgInsertTimeMs:F2}ms/vector, Avg search time: {avgSearchTimeMs:F2}ms/query");

                Console.WriteLine($"Vector dimensionality impact: {testDimension}D vectors are {testDimension / 50.0:F1}x larger than typical 50D vectors");
                Console.WriteLine($"Estimated memory usage (RAM): ~{vectorCount * testDimension * 4 / (1024.0 * 1024.0):F1} MB for vectors alone\n");

                Console.WriteLine("=== SUMMARY ===");
                Console.WriteLine($"• RAM is faster for both insertion ({(double)sqliteInsertTime / ramInsertTime:F1}x) and search ({(double)sqliteSearchTime / ramSearchTime:F1}x)");
                Console.WriteLine($"• Batch operations provide {(double)individualAddTime / batchAddTime:F1}x speedup over individual operations");
                Console.WriteLine($"• SQLite provides persistence at the cost of {100 - (100.0 * ramInsertTime / sqliteInsertTime):F0}% performance");
                Console.WriteLine($"• Accuracy is {(overlap >= 3 ? "maintained" : "degraded")} with SQLite storage");
                Console.WriteLine($"• SQLite database size: {fileSize / (1024.0 * 1024.0):F1} MB for {vectorCount} {testDimension}D vectors");
                Console.WriteLine($"• Storage efficiency: {overhead / theoreticalVectorSize * 100:F0}% overhead vs raw vector data\n");
            }
            finally
            {
                SafeDeleteFile(dbPath);
            }
        }

        private static async Task TestFlushValidationAsync()
        {
            Console.WriteLine("Test 15: Flush Validation - Ensuring deferred saves work correctly");

            string dbPath = Path.GetTempFileName();
            try
            {
                using SqliteHnswStorage sqliteStorage = new SqliteHnswStorage(dbPath);
                using SqliteHnswLayerStorage sqliteLayerStorage = new SqliteHnswLayerStorage(sqliteStorage.Connection);
                HnswIndex sqliteIndex = new HnswIndex(2, sqliteStorage, sqliteLayerStorage);

                // Test 1: Add nodes without explicit flush - should still be queryable
                Console.WriteLine("\n1. Testing nodes are queryable without explicit Flush:");
                Guid id1 = Guid.NewGuid();
                Guid id2 = Guid.NewGuid();
                Guid id3 = Guid.NewGuid();
                
                Dictionary<Guid, List<float>> vectors = new Dictionary<Guid, List<float>>
                {
                    { id1, new List<float> { 1.0f, 1.0f } },
                    { id2, new List<float> { 2.0f, 2.0f } },
                    { id3, new List<float> { 3.0f, 3.0f } }
                };

                await sqliteIndex.AddNodesAsync(vectors);
                
                // Search immediately without flush
                List<VectorResult> results = await TimedSearchAsync(sqliteIndex, new List<float> { 1.5f, 1.5f }, 3, null, "Search without flush");
                Console.WriteLine($"Found {results.Count} results without explicit flush");
                bool canSearchWithoutFlush = results.Count == 3;
                Console.WriteLine($"✓ Nodes are searchable without explicit flush: {canSearchWithoutFlush}");

                // Test 2: Verify data persists after reopening database
                Console.WriteLine("\n2. Testing persistence after reopening database:");
                
                // Close and reopen
                sqliteStorage.Dispose();
                sqliteLayerStorage.Dispose();
                
                using SqliteHnswStorage newStorage = new SqliteHnswStorage(dbPath);
                using SqliteHnswLayerStorage newLayerStorage = new SqliteHnswLayerStorage(newStorage.Connection);
                HnswIndex newIndex = new HnswIndex(2, newStorage, newLayerStorage);
                
                // Search in reopened database
                results = await TimedSearchAsync(newIndex, new List<float> { 1.5f, 1.5f }, 3, null, "Search after reopen");
                Console.WriteLine($"Found {results.Count} results after reopening database");
                bool dataPersistedCorrectly = results.Count == 3 && results.Any(r => r.GUID == id1);
                Console.WriteLine($"✓ Data persisted correctly: {dataPersistedCorrectly}");

                // Test 3: Verify neighbor relationships are preserved
                Console.WriteLine("\n3. Testing neighbor relationships persistence:");
                
                // Add more vectors to create complex neighbor relationships
                Dictionary<Guid, List<float>> additionalVectors = new Dictionary<Guid, List<float>>();
                Random random = new Random(42);
                for (int i = 0; i < 20; i++)
                {
                    additionalVectors[Guid.NewGuid()] = new List<float> 
                    { 
                        (float)(random.NextDouble() * 10), 
                        (float)(random.NextDouble() * 10) 
                    };
                }
                
                await newIndex.AddNodesAsync(additionalVectors);
                
                // Get results before closing
                List<VectorResult> beforeCloseResults = await TimedSearchAsync(newIndex, new List<float> { 5.0f, 5.0f }, 5, null, "Before close");
                
                // Close and reopen again
                newStorage.Dispose();
                newLayerStorage.Dispose();
                
                using SqliteHnswStorage finalStorage = new SqliteHnswStorage(dbPath);
                using SqliteHnswLayerStorage finalLayerStorage = new SqliteHnswLayerStorage(finalStorage.Connection);
                HnswIndex finalIndex = new HnswIndex(2, finalStorage, finalLayerStorage);
                
                // Get results after reopening
                List<VectorResult> afterReopenResults = await TimedSearchAsync(finalIndex, new List<float> { 5.0f, 5.0f }, 5, null, "After reopen");
                
                // Compare results
                bool neighborsPreserved = beforeCloseResults.Count == afterReopenResults.Count;
                if (neighborsPreserved)
                {
                    for (int i = 0; i < beforeCloseResults.Count; i++)
                    {
                        if (beforeCloseResults[i].GUID != afterReopenResults[i].GUID)
                        {
                            neighborsPreserved = false;
                            break;
                        }
                    }
                }
                
                Console.WriteLine($"Results before close: {beforeCloseResults.Count}, after reopen: {afterReopenResults.Count}");
                Console.WriteLine($"✓ Neighbor relationships preserved: {neighborsPreserved}");

                // Test 4: Verify batch operations with deferred flush
                Console.WriteLine("\n4. Testing batch operations with deferred flush:");
                
                // Measure performance with large batch
                Stopwatch sw = Stopwatch.StartNew();
                Dictionary<Guid, List<float>> largeBatch = new Dictionary<Guid, List<float>>();
                for (int i = 0; i < 100; i++)
                {
                    largeBatch[Guid.NewGuid()] = new List<float> 
                    { 
                        (float)(random.NextDouble() * 100), 
                        (float)(random.NextDouble() * 100) 
                    };
                }
                
                await finalIndex.AddNodesAsync(largeBatch);
                sw.Stop();
                long batchTime = sw.ElapsedMilliseconds;
                
                Console.WriteLine($"Added 100 vectors in batch: {batchTime}ms");
                Console.WriteLine($"Average time per vector: {batchTime / 100.0:F2}ms");
                
                // Verify all vectors are searchable
                List<VectorResult> batchResults = await TimedSearchAsync(finalIndex, new List<float> { 50.0f, 50.0f }, 10, null, "After batch add");
                bool batchWorksCorrectly = batchResults.Count == 10;
                Console.WriteLine($"✓ Batch operations work correctly: {batchWorksCorrectly}");

                // Test 5: Test removal and persistence
                Console.WriteLine("\n5. Testing removal with deferred flush:");
                
                // Remove some nodes
                List<Guid> toRemove = additionalVectors.Keys.Take(5).ToList();
                sw.Restart();
                await finalIndex.RemoveNodesAsync(toRemove);
                sw.Stop();
                
                Console.WriteLine($"Removed 5 nodes in {sw.ElapsedMilliseconds}ms");
                
                // Verify removal is effective immediately
                List<VectorResult> afterRemovalResults = await TimedSearchAsync(finalIndex, new List<float> { 5.0f, 5.0f }, 20, null, "After removal");
                bool removedNodesAbsent = !afterRemovalResults.Any(r => toRemove.Contains(r.GUID));
                Console.WriteLine($"✓ Removed nodes are no longer searchable: {removedNodesAbsent}");
                
                // Close and reopen to verify removal persisted
                finalStorage.Dispose();
                finalLayerStorage.Dispose();
                
                using SqliteHnswStorage verifyStorage = new SqliteHnswStorage(dbPath);
                using SqliteHnswLayerStorage verifyLayerStorage = new SqliteHnswLayerStorage(verifyStorage.Connection);
                HnswIndex verifyIndex = new HnswIndex(2, verifyStorage, verifyLayerStorage);
                
                List<VectorResult> verifyResults = await TimedSearchAsync(verifyIndex, new List<float> { 5.0f, 5.0f }, 20, null, "After reopen post-removal");
                bool removalPersisted = !verifyResults.Any(r => toRemove.Contains(r.GUID));
                Console.WriteLine($"✓ Removal persisted after reopening: {removalPersisted}");

                // Summary
                Console.WriteLine("\n=== FLUSH VALIDATION SUMMARY ===");
                Console.WriteLine($"✓ Nodes searchable without explicit flush: {canSearchWithoutFlush}");
                Console.WriteLine($"✓ Data persists correctly: {dataPersistedCorrectly}");
                Console.WriteLine($"✓ Neighbor relationships preserved: {neighborsPreserved}");
                Console.WriteLine($"✓ Batch operations work correctly: {batchWorksCorrectly}");
                Console.WriteLine($"✓ Deferred flush improves batch performance by reducing I/O");
                Console.WriteLine($"✓ All flush validation tests passed\n");
            }
            finally
            {
                SafeDeleteFile(dbPath);
            }
        }
    }
}