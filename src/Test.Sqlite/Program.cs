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

                // Add same vectors to both
                var vectors = new List<(Guid, List<float>)>
                {
                    (Guid.NewGuid(), new List<float> { 1.0f, 1.0f }),
                    (Guid.NewGuid(), new List<float> { 2.0f, 2.0f }),
                    (Guid.NewGuid(), new List<float> { 3.0f, 3.0f }),
                    (Guid.NewGuid(), new List<float> { 10.0f, 10.0f }),
                    (Guid.NewGuid(), new List<float> { 11.0f, 11.0f })
                };

                var sw = Stopwatch.StartNew();
                foreach (var (id, vector) in vectors)
                {
                    await sqliteIndex.AddAsync(id, vector);
                }
                sw.Stop();
                var sqliteAddTime = sw.ElapsedMilliseconds;

                sw.Restart();
                foreach (var (id, vector) in vectors)
                {
                    await ramIndex.AddAsync(id, vector);
                }
                sw.Stop();
                var ramAddTime = sw.ElapsedMilliseconds;

                // Search for nearest to (1.5, 1.5)
                var query = new List<float> { 1.5f, 1.5f };

                sw.Restart();
                var sqliteResults = (await sqliteIndex.GetTopKAsync(query, 3)).ToList();
                sw.Stop();
                var sqliteSearchTime = sw.ElapsedMilliseconds;

                sw.Restart();
                var ramResults = (await ramIndex.GetTopKAsync(query, 3)).ToList();
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

                // Add to both indexes
                var vectors = new List<(Guid, List<float>)>
                {
                    (id1, new List<float> { 1.0f, 1.0f }),
                    (id2, new List<float> { 2.0f, 2.0f }),
                    (id3, new List<float> { 3.0f, 3.0f })
                };

                foreach (var (id, vector) in vectors)
                {
                    await sqliteIndex.AddAsync(id, vector);
                    await ramIndex.AddAsync(id, vector);
                }

                // Remove the middle vector from both
                var sw = Stopwatch.StartNew();
                await sqliteIndex.RemoveAsync(id2);
                sw.Stop();
                var sqliteRemoveTime = sw.ElapsedMilliseconds;

                sw.Restart();
                await ramIndex.RemoveAsync(id2);
                sw.Stop();
                var ramRemoveTime = sw.ElapsedMilliseconds;

                var sqliteResults = (await sqliteIndex.GetTopKAsync(new List<float> { 2.0f, 2.0f }, 3)).ToList();
                var ramResults = (await ramIndex.GetTopKAsync(new List<float> { 2.0f, 2.0f }, 3)).ToList();

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

                var sqliteResults = (await sqliteIndex.GetTopKAsync(new List<float> { 1.0f, 1.0f }, 5)).ToList();
                var ramResults = (await ramIndex.GetTopKAsync(new List<float> { 1.0f, 1.0f }, 5)).ToList();

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

                var sqliteResults = (await sqliteIndex.GetTopKAsync(new List<float> { 0.0f, 0.0f }, 1)).ToList();
                var ramResults = (await ramIndex.GetTopKAsync(new List<float> { 0.0f, 0.0f }, 1)).ToList();

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

                // Add multiple vectors at the same location to both indexes
                for (int i = 0; i < 5; i++)
                {
                    var id = Guid.NewGuid();
                    await sqliteIndex.AddAsync(id, new List<float> { 5.0f, 5.0f });
                    await ramIndex.AddAsync(id, new List<float> { 5.0f, 5.0f });
                }

                var sqliteResults = (await sqliteIndex.GetTopKAsync(new List<float> { 5.0f, 5.0f }, 10)).ToList();
                var ramResults = (await ramIndex.GetTopKAsync(new List<float> { 5.0f, 5.0f }, 10)).ToList();

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

                // Add 10 random 100-dimensional vectors to both
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 10; i++)
                {
                    var id = Guid.NewGuid();
                    var vector = Enumerable.Range(0, 100).Select(_ => (float)random.NextDouble()).ToList();
                    await sqliteIndex.AddAsync(id, vector);
                }
                sw.Stop();
                var sqliteAddTime = sw.ElapsedMilliseconds;

                random = new Random(42); // Reset for same vectors
                sw.Restart();
                for (int i = 0; i < 10; i++)
                {
                    var id = Guid.NewGuid();
                    var vector = Enumerable.Range(0, 100).Select(_ => (float)random.NextDouble()).ToList();
                    await ramIndex.AddAsync(id, vector);
                }
                sw.Stop();
                var ramAddTime = sw.ElapsedMilliseconds;

                // Create a query vector
                var query = Enumerable.Range(0, 100).Select(_ => 0.5f).ToList();

                sw.Restart();
                var sqliteResults = (await sqliteIndex.GetTopKAsync(query, 5)).ToList();
                sw.Stop();
                var sqliteSearchTime = sw.ElapsedMilliseconds;

                sw.Restart();
                var ramResults = (await ramIndex.GetTopKAsync(query, 5)).ToList();
                sw.Stop();
                var ramSearchTime = sw.ElapsedMilliseconds;

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

                allPoints = allPoints.OrderBy(x => random.Next()).ToList();

                // Add to SQLite
                var sw = Stopwatch.StartNew();
                foreach (var point in allPoints)
                {
                    await sqliteIndex.AddAsync(point.id, point.vector);
                }
                sw.Stop();
                var sqliteAddTime = sw.ElapsedMilliseconds;

                // Add to RAM with same IDs
                sw.Restart();
                foreach (var point in allPoints)
                {
                    await ramIndex.AddAsync(point.id, point.vector);
                }
                sw.Stop();
                var ramAddTime = sw.ElapsedMilliseconds;

                Console.WriteLine($"SQLite added 60 vectors in {sqliteAddTime}ms");
                Console.WriteLine($"RAM added 60 vectors in {ramAddTime}ms");

                // Search near cluster center
                sw.Restart();
                var sqliteResults = (await sqliteIndex.GetTopKAsync(new List<float> { 10f, 10f }, 5, ef: 400)).ToList();
                sw.Stop();
                var sqliteSearchTime = sw.ElapsedMilliseconds;

                sw.Restart();
                var ramResults = (await ramIndex.GetTopKAsync(new List<float> { 10f, 10f }, 5, ef: 400)).ToList();
                sw.Stop();
                var ramSearchTime = sw.ElapsedMilliseconds;

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
            var vectors = new List<(Guid, List<float>)>
            {
                (Guid.NewGuid(), new List<float> { 1.0f, 0.0f }),
                (Guid.NewGuid(), new List<float> { 0.0f, 1.0f }),
                (Guid.NewGuid(), new List<float> { 0.707f, 0.707f }),
                (Guid.NewGuid(), new List<float> { -1.0f, 0.0f })
            };

            foreach (var (id, vector) in vectors)
            {
                await sqliteIndex.AddAsync(id, vector);
                await ramIndex.AddAsync(id, vector);
            }

            var query = new List<float> { 1.0f, 0.0f };
            var sqliteResults = (await sqliteIndex.GetTopKAsync(query, 2)).ToList();
            var ramResults = (await ramIndex.GetTopKAsync(query, 2)).ToList();

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

                var batch = new List<(Guid, List<float>)>();
                for (int i = 0; i < 20; i++)
                {
                    var id = Guid.NewGuid();
                    batch.Add((id, new List<float> { (float)random.NextDouble() * 10, (float)random.NextDouble() * 10 }));
                }

                var sw = Stopwatch.StartNew();
                await sqliteIndex.AddBatchAsync(batch);
                sw.Stop();
                var sqliteBatchTime = sw.ElapsedMilliseconds;

                sw.Restart();
                await ramIndex.AddBatchAsync(batch);
                sw.Stop();
                var ramBatchTime = sw.ElapsedMilliseconds;

                Console.WriteLine($"SQLite batch inserted {batch.Count} vectors in {sqliteBatchTime}ms");
                Console.WriteLine($"RAM batch inserted {batch.Count} vectors in {ramBatchTime}ms");

                var query = new List<float> { 5.0f, 5.0f };
                var sqliteResults = (await sqliteIndex.GetTopKAsync(query, 5)).ToList();
                var ramResults = (await ramIndex.GetTopKAsync(query, 5)).ToList();

                Console.WriteLine($"SQLite found {sqliteResults.Count} results after batch insert");
                Console.WriteLine($"RAM found {ramResults.Count} results after batch insert");

                // Test batch remove
                var toRemove = batch.Take(10).Select(x => x.Item1).ToList();

                sw.Restart();
                await sqliteIndex.RemoveBatchAsync(toRemove);
                sw.Stop();
                var sqliteRemoveTime = sw.ElapsedMilliseconds;

                sw.Restart();
                await ramIndex.RemoveBatchAsync(toRemove);
                sw.Stop();
                var ramRemoveTime = sw.ElapsedMilliseconds;

                var sqliteResultsAfter = (await sqliteIndex.GetTopKAsync(query, 15)).ToList();
                var ramResultsAfter = (await ramIndex.GetTopKAsync(query, 15)).ToList();

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

                var ids = new List<Guid>();
                for (int i = 0; i < 10; i++)
                {
                    var id = Guid.NewGuid();
                    ids.Add(id);
                    var vector = new List<float> { i * 0.1f, i * 0.2f };
                    await originalSqliteIndex.AddAsync(id, vector);
                    await originalRamIndex.AddAsync(id, vector);
                }

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
                var originalSqliteResults = (await originalSqliteIndex.GetTopKAsync(query, 3)).ToList();
                var importedSqliteResults = (await importedSqliteIndex.GetTopKAsync(query, 3)).ToList();
                var originalRamResults = (await originalRamIndex.GetTopKAsync(query, 3)).ToList();
                var importedRamResults = (await importedRamIndex.GetTopKAsync(query, 3)).ToList();

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
                    await index.AddAsync(Guid.NewGuid(), null);
                    Console.WriteLine("SQLite null vector validation: FAILED");
                }
                catch (ArgumentNullException)
                {
                    Console.WriteLine("SQLite null vector validation: PASSED");
                }

                try
                {
                    var index = new HnswIndex(2, new RamHnswStorage(), new RamHnswLayerStorage());
                    await index.AddAsync(Guid.NewGuid(), null);
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
                var testVectors = new List<(Guid, List<float>)>
            {
                (Guid.NewGuid(), new List<float> { 1.0f, 1.0f }),
                (Guid.NewGuid(), new List<float> { 2.0f, 2.0f }),
                (Guid.NewGuid(), new List<float> { 3.0f, 3.0f })
            };

                // First session - create and populate index
                using (var sqliteStorage = new SqliteHnswStorage(dbPath))
                using (var sqliteLayerStorage = new SqliteHnswLayerStorage(sqliteStorage.Connection))
                {
                    var index = new HnswIndex(2, sqliteStorage, sqliteLayerStorage);
                    index.M = 8;
                    index.MaxM = 12;

                    foreach (var (id, vector) in testVectors)
                    {
                        await index.AddAsync(id, vector);
                    }

                    var results = (await index.GetTopKAsync(new List<float> { 1.5f, 1.5f }, 3)).ToList();
                    Console.WriteLine($"First session: Found {results.Count} vectors");
                }

                // Second session - reload and verify data persists
                using (var sqliteStorage = new SqliteHnswStorage(dbPath))
                using (var sqliteLayerStorage = new SqliteHnswLayerStorage(sqliteStorage.Connection))
                {
                    var index = new HnswIndex(2, sqliteStorage, sqliteLayerStorage);

                    var results = (await index.GetTopKAsync(new List<float> { 1.5f, 1.5f }, 3)).ToList();
                    Console.WriteLine($"Second session: Found {results.Count} vectors");

                    // Verify we can find the same vectors
                    var foundIds = results.Select(r => r.GUID).ToHashSet();
                    var expectedIds = testVectors.Select(tv => tv.Item1).ToHashSet();
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
                var sqliteIndex = new HnswIndex(50, sqliteStorage, sqliteLayerStorage);

                var ramIndex = new HnswIndex(50, new RamHnswStorage(), new RamHnswLayerStorage());

                // Set same parameters
                sqliteIndex.Seed = 42;
                ramIndex.Seed = 42;
                sqliteIndex.M = 16;
                ramIndex.M = 16;
                sqliteIndex.EfConstruction = 200;
                ramIndex.EfConstruction = 200;

                var random = new Random(42);
                const int vectorCount = 1000;

                Console.WriteLine($"Testing with {vectorCount} 50-dimensional vectors...\n");

                // Generate test data
                var vectors = new List<(Guid, List<float>)>();
                for (int i = 0; i < vectorCount; i++)
                {
                    var vector = Enumerable.Range(0, 50).Select(_ => (float)(random.NextDouble() * 10 - 5)).ToList();
                    vectors.Add((Guid.NewGuid(), vector));
                }

                // Test SQLite insertion performance
                var sw = Stopwatch.StartNew();
                foreach (var (id, vector) in vectors)
                {
                    await sqliteIndex.AddAsync(id, vector);
                }
                sw.Stop();
                var sqliteInsertTime = sw.ElapsedMilliseconds;
                var sqliteInsertRate = vectorCount * 1000.0 / sqliteInsertTime;

                // Test RAM insertion performance
                sw.Restart();
                foreach (var (id, vector) in vectors)
                {
                    await ramIndex.AddAsync(id, vector);
                }
                sw.Stop();
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
                    queries.Add(Enumerable.Range(0, 50).Select(_ => (float)(random.NextDouble() * 10 - 5)).ToList());
                }

                // SQLite search performance
                sw.Restart();
                foreach (var query in queries)
                {
                    await sqliteIndex.GetTopKAsync(query, 10, ef: 100);
                }
                sw.Stop();
                var sqliteSearchTime = sw.ElapsedMilliseconds;
                var sqliteSearchRate = queries.Count * 1000.0 / sqliteSearchTime;

                // RAM search performance
                sw.Restart();
                foreach (var query in queries)
                {
                    await ramIndex.GetTopKAsync(query, 10, ef: 100);
                }
                sw.Stop();
                var ramSearchTime = sw.ElapsedMilliseconds;
                var ramSearchRate = queries.Count * 1000.0 / ramSearchTime;

                Console.WriteLine("=== SEARCH PERFORMANCE (100 queries) ===");
                Console.WriteLine($"SQLite: {sqliteSearchTime}ms total, {sqliteSearchRate:F1} queries/sec");
                Console.WriteLine($"RAM:    {ramSearchTime}ms total, {ramSearchRate:F1} queries/sec");
                Console.WriteLine($"RAM is {(double)sqliteSearchTime / ramSearchTime:F1}x faster for search\n");

                // Test memory usage (approximate)
                var fileSize = new FileInfo(dbPath).Length;
                Console.WriteLine("=== STORAGE ===");
                Console.WriteLine($"SQLite database size: {fileSize / 1024.0:F1} KB ({fileSize / (double)vectorCount:F1} bytes/vector)");
                Console.WriteLine($"RAM storage: In-memory only (no persistence)\n");

                // Test accuracy comparison
                var testQuery = Enumerable.Range(0, 50).Select(_ => 0.0f).ToList();
                var sqliteResults = (await sqliteIndex.GetTopKAsync(testQuery, 10)).ToList();
                var ramResults = (await ramIndex.GetTopKAsync(testQuery, 10)).ToList();

                Console.WriteLine("=== ACCURACY COMPARISON ===");
                Console.WriteLine($"SQLite found {sqliteResults.Count} results, avg distance: {sqliteResults.Average(r => r.Distance):F4}");
                Console.WriteLine($"RAM found {ramResults.Count} results, avg distance: {ramResults.Average(r => r.Distance):F4}");

                // Check if top results are similar
                var topSqliteIds = sqliteResults.Take(5).Select(r => r.GUID).ToHashSet();
                var topRamIds = ramResults.Take(5).Select(r => r.GUID).ToHashSet();
                var overlap = topSqliteIds.Intersect(topRamIds).Count();

                Console.WriteLine($"Top 5 results overlap: {overlap}/5 ({overlap * 20}%)");
                Console.WriteLine($"Results are {(overlap >= 3 ? "consistent" : "different")}\n");

                Console.WriteLine("=== SUMMARY ===");
                Console.WriteLine($"• RAM is faster for both insertion ({(double)sqliteInsertTime / ramInsertTime:F1}x) and search ({(double)sqliteSearchTime / ramSearchTime:F1}x)");
                Console.WriteLine($"• SQLite provides persistence at the cost of {100 - (100.0 * ramInsertTime / sqliteInsertTime):F0}% performance");
                Console.WriteLine($"• Accuracy is {(overlap >= 3 ? "maintained" : "degraded")} with SQLite storage");
                Console.WriteLine($"• SQLite database size: {fileSize / 1024.0:F1} KB for {vectorCount} vectors\n");
            }
            finally
            {
                SafeDeleteFile(dbPath);
            }
        }
    }
}