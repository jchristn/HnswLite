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
    using HnswIndex.SqliteStorage;
    using Hsnw;
    using Microsoft.Data.Sqlite;
    using Touchstone.Core;

    /// <summary>
    /// Extended Touchstone test suites — concurrency, cross-storage parity, edge cases,
    /// large-scale searches, parameter sensitivity, and topology preservation.
    /// </summary>
    public static class HnswExtendedSuites
    {
        #region Public-Members

        /// <summary>
        /// All extended suites surfaced through the runner.
        /// </summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get
            {
                return new List<TestSuiteDescriptor>
                {
                    EdgeCasesSuite(),
                    LargeDatasetSuite(),
                    ParameterSensitivitySuite(),
                    BatchOperationsSuite(),
                    ConcurrencySuite(),
                    CrossStorageParitySuite(),
                    DistanceCoverageSuite(),
                    HighDimensionalSuite(),
                    StateRoundtripSuite(),
                    SqliteAdvancedSuite(),
                    SqliteStateSuite(),
                };
            }
        }

        #endregion

        #region Private-Members

        private const int _Dimension2D = 2;
        private const int _Dimension64 = 64;
        private const int _Dimension384 = 384;
        private const int _Dimension768 = 768;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Edge-case scenarios: re-add, search-then-remove cycles, removing non-existent.
        /// </summary>
        /// <returns>Edge-case test suite.</returns>
        public static TestSuiteDescriptor EdgeCasesSuite()
        {
            return new TestSuiteDescriptor(
                suiteId: "Ram.EdgeCases",
                displayName: "RAM Storage - Edge Cases",
                cases: new List<TestCaseDescriptor>
                {
                    Case("Ram.EdgeCases", "RemoveNonExistentNoThrow",
                        "Removing a non-existent GUID is a no-op",
                        async ct =>
                        {
                            HnswIndex index = NewRam(_Dimension2D);
                            await index.AddAsync(Guid.NewGuid(), new List<float> { 1f, 1f }, ct).ConfigureAwait(false);
                            // Removing an unknown id should not throw
                            await index.RemoveAsync(Guid.NewGuid(), ct).ConfigureAwait(false);
                        }),

                    Case("Ram.EdgeCases", "RemoveAllThenSearch",
                        "After removing every vector, search returns empty results",
                        async ct =>
                        {
                            HnswIndex index = NewRam(_Dimension2D);
                            List<Guid> ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
                            await index.AddNodesAsync(new Dictionary<Guid, List<float>>
                            {
                                { ids[0], new List<float> { 1f, 1f } },
                                { ids[1], new List<float> { 2f, 2f } },
                                { ids[2], new List<float> { 3f, 3f } },
                            }, ct).ConfigureAwait(false);

                            await index.RemoveNodesAsync(ids, ct).ConfigureAwait(false);

                            List<VectorResult> results = (await index.GetTopKAsync(
                                new List<float> { 0f, 0f }, 5, cancellationToken: ct).ConfigureAwait(false)).ToList();
                            TestAssert.Equal(0, results.Count, "Empty after RemoveAll");
                        }),

                    Case("Ram.EdgeCases", "InsertRemoveInsertCycle",
                        "Insert/remove/insert preserves consistency",
                        async ct =>
                        {
                            HnswIndex index = NewRam(_Dimension2D);
                            Guid a = Guid.NewGuid();
                            Guid b = Guid.NewGuid();

                            await index.AddAsync(a, new List<float> { 1f, 1f }, ct).ConfigureAwait(false);
                            await index.AddAsync(b, new List<float> { 5f, 5f }, ct).ConfigureAwait(false);
                            await index.RemoveAsync(a, ct).ConfigureAwait(false);
                            await index.AddAsync(a, new List<float> { 1f, 1f }, ct).ConfigureAwait(false);

                            List<VectorResult> results = (await index.GetTopKAsync(
                                new List<float> { 1f, 1f }, 1, cancellationToken: ct).ConfigureAwait(false)).ToList();
                            TestAssert.Equal(1, results.Count, "Top-1 count after re-insert");
                            TestAssert.Equal(a, results[0].GUID, "Re-inserted GUID retrieved");
                        }),

                    Case("Ram.EdgeCases", "K_GreaterThan_Available",
                        "Requesting K larger than vector count returns all available",
                        async ct =>
                        {
                            HnswIndex index = NewRam(_Dimension2D);
                            await index.AddNodesAsync(new Dictionary<Guid, List<float>>
                            {
                                { Guid.NewGuid(), new List<float> { 1f, 1f } },
                                { Guid.NewGuid(), new List<float> { 2f, 2f } },
                            }, ct).ConfigureAwait(false);

                            List<VectorResult> results = (await index.GetTopKAsync(
                                new List<float> { 1f, 1f }, 100, cancellationToken: ct).ConfigureAwait(false)).ToList();
                            TestAssert.Equal(2, results.Count, "Returned all available when K > N");
                        }),

                    Case("Ram.EdgeCases", "K_EqualsOne",
                        "K=1 returns the single best result",
                        async ct =>
                        {
                            HnswIndex index = NewRam(_Dimension2D);
                            Guid expected = Guid.NewGuid();
                            await index.AddNodesAsync(new Dictionary<Guid, List<float>>
                            {
                                { expected, new List<float> { 0f, 0f } },
                                { Guid.NewGuid(), new List<float> { 5f, 5f } },
                                { Guid.NewGuid(), new List<float> { 10f, 10f } },
                            }, ct).ConfigureAwait(false);

                            List<VectorResult> results = (await index.GetTopKAsync(
                                new List<float> { 0f, 0f }, 1, cancellationToken: ct).ConfigureAwait(false)).ToList();
                            TestAssert.Equal(1, results.Count, "K=1 size");
                            TestAssert.Equal(expected, results[0].GUID, "K=1 picks closest");
                        }),

                    Case("Ram.EdgeCases", "ResultsAreSortedByDistance",
                        "Returned results are sorted ascending by distance",
                        async ct =>
                        {
                            HnswIndex index = NewRam(_Dimension2D);
                            await index.AddNodesAsync(new Dictionary<Guid, List<float>>
                            {
                                { Guid.NewGuid(), new List<float> { 0f, 0f } },
                                { Guid.NewGuid(), new List<float> { 1f, 1f } },
                                { Guid.NewGuid(), new List<float> { 5f, 5f } },
                                { Guid.NewGuid(), new List<float> { 20f, 20f } },
                                { Guid.NewGuid(), new List<float> { 100f, 100f } },
                            }, ct).ConfigureAwait(false);

                            List<VectorResult> results = (await index.GetTopKAsync(
                                new List<float> { 0f, 0f }, 5, cancellationToken: ct).ConfigureAwait(false)).ToList();
                            for (int i = 1; i < results.Count; i++)
                            {
                                TestAssert.True(results[i].Distance >= results[i - 1].Distance,
                                    $"Result[{i}].Distance ({results[i].Distance}) >= Result[{i - 1}].Distance ({results[i - 1].Distance})");
                            }
                        }),
                });
        }

        /// <summary>
        /// Larger-scale dataset tests for stability and recall.
        /// </summary>
        /// <returns>Large-dataset test suite.</returns>
        public static TestSuiteDescriptor LargeDatasetSuite()
        {
            return new TestSuiteDescriptor(
                suiteId: "Ram.LargeDataset",
                displayName: "RAM Storage - Large Dataset",
                cases: new List<TestCaseDescriptor>
                {
                    Case("Ram.LargeDataset", "ThousandClustered",
                        "1000 clustered vectors return cluster members for clustered query",
                        async ct =>
                        {
                            HnswIndex index = NewRam(_Dimension64, seed: 42);
                            Random rng = new Random(42);
                            const int total = 1000;

                            Dictionary<Guid, List<float>> vectors = new Dictionary<Guid, List<float>>(total);
                            // Two clusters: one centered at 0, one centered at 100.
                            for (int i = 0; i < total / 2; i++)
                                vectors[Guid.NewGuid()] = RandomVector(_Dimension64, rng, center: 0f, scale: 1f);
                            for (int i = 0; i < total / 2; i++)
                                vectors[Guid.NewGuid()] = RandomVector(_Dimension64, rng, center: 100f, scale: 1f);

                            await index.AddNodesAsync(vectors, ct).ConfigureAwait(false);

                            // Query near the second cluster — at least 8/10 hits should be cluster-2 members.
                            List<float> query = RandomVector(_Dimension64, rng, center: 100f, scale: 1f);
                            List<VectorResult> results = (await index.GetTopKAsync(query, 10, cancellationToken: ct).ConfigureAwait(false)).ToList();

                            int near = 0;
                            foreach (VectorResult r in results)
                            {
                                if (r.Vectors[0] > 50f) near++;
                            }
                            TestAssert.True(near >= 8, $"Cluster recall: expected >=8/10, got {near}/10");
                        }),

                    Case("Ram.LargeDataset", "TenClustersDistinct",
                        "10 well-separated clusters: nearest results match the queried cluster",
                        async ct =>
                        {
                            HnswIndex index = NewRam(_Dimension2D, seed: 7);
                            Random rng = new Random(7);
                            int perCluster = 50;

                            Dictionary<Guid, List<float>> vectors = new Dictionary<Guid, List<float>>();
                            for (int c = 0; c < 10; c++)
                            {
                                float cx = c * 100f;
                                float cy = c * 100f;
                                for (int i = 0; i < perCluster; i++)
                                {
                                    vectors[Guid.NewGuid()] = new List<float>
                                    {
                                        cx + (float)(rng.NextDouble() - 0.5),
                                        cy + (float)(rng.NextDouble() - 0.5),
                                    };
                                }
                            }
                            await index.AddNodesAsync(vectors, ct).ConfigureAwait(false);

                            // Query near cluster 5 — every top-5 should be near the cluster center.
                            List<float> query = new List<float> { 500f, 500f };
                            List<VectorResult> results = (await index.GetTopKAsync(query, 5, cancellationToken: ct).ConfigureAwait(false)).ToList();
                            int near = 0;
                            foreach (VectorResult r in results)
                            {
                                if (Math.Abs(r.Vectors[0] - 500f) < 5f && Math.Abs(r.Vectors[1] - 500f) < 5f) near++;
                            }
                            TestAssert.True(near >= 4, $"Expected ≥4/5 from cluster-5, got {near}");
                        }),
                });
        }

        /// <summary>
        /// Parameter-sensitivity tests: M, EfConstruction, ef.
        /// </summary>
        /// <returns>Parameter-sensitivity test suite.</returns>
        public static TestSuiteDescriptor ParameterSensitivitySuite()
        {
            return new TestSuiteDescriptor(
                suiteId: "Ram.Parameters",
                displayName: "RAM Storage - Parameter Sensitivity",
                cases: new List<TestCaseDescriptor>
                {
                    Case("Ram.Parameters", "LowM_StillFunctional",
                        "Index with M=2 still produces correct nearest-neighbor results",
                        async ct =>
                        {
                            HnswIndex index = NewRam(_Dimension2D, seed: 1);
                            index.M = 2;
                            index.MaxM = 4;
                            await index.AddNodesAsync(MakeGrid(side: 5), ct).ConfigureAwait(false);

                            List<VectorResult> results = (await index.GetTopKAsync(
                                new List<float> { 0.1f, 0.1f }, 1, cancellationToken: ct).ConfigureAwait(false)).ToList();
                            TestAssert.Equal(1, results.Count, "Top-1 with low M");
                            TestAssert.NearEqual(0f, results[0].Vectors[0], 0.5f, "Closest x");
                            TestAssert.NearEqual(0f, results[0].Vectors[1], 0.5f, "Closest y");
                        }),

                    Case("Ram.Parameters", "HighM_StillFunctional",
                        "Index with M=64 builds and searches without error",
                        async ct =>
                        {
                            HnswIndex index = NewRam(_Dimension2D, seed: 1);
                            index.M = 64;
                            index.MaxM = 100;
                            await index.AddNodesAsync(MakeGrid(side: 8), ct).ConfigureAwait(false);

                            List<VectorResult> results = (await index.GetTopKAsync(
                                new List<float> { 3f, 3f }, 5, cancellationToken: ct).ConfigureAwait(false)).ToList();
                            TestAssert.True(results.Count > 0, "High-M search returns results");
                        }),

                    Case("Ram.Parameters", "EfOverridesAtSearchTime",
                        "Custom ef parameter at search time changes recall behavior without error",
                        async ct =>
                        {
                            HnswIndex index = NewRam(_Dimension2D, seed: 1);
                            await index.AddNodesAsync(MakeGrid(side: 6), ct).ConfigureAwait(false);

                            // Both calls succeed; we don't assert recall difference (small dataset),
                            // only that the ef parameter is honored end-to-end without error.
                            List<VectorResult> low = (await index.GetTopKAsync(
                                new List<float> { 2f, 2f }, 5, ef: 10, cancellationToken: ct).ConfigureAwait(false)).ToList();
                            List<VectorResult> high = (await index.GetTopKAsync(
                                new List<float> { 2f, 2f }, 5, ef: 200, cancellationToken: ct).ConfigureAwait(false)).ToList();

                            TestAssert.Equal(5, low.Count, "Low-ef result count");
                            TestAssert.Equal(5, high.Count, "High-ef result count");
                        }),

                    Case("Ram.Parameters", "DeterministicSeedReproducible",
                        "Same seed + same insertions produce identical search ordering",
                        async ct =>
                        {
                            HnswIndex a = NewRam(_Dimension2D, seed: 99);
                            HnswIndex b = NewRam(_Dimension2D, seed: 99);

                            Dictionary<Guid, List<float>> grid = MakeGrid(side: 5);
                            await a.AddNodesAsync(grid, ct).ConfigureAwait(false);
                            await b.AddNodesAsync(grid, ct).ConfigureAwait(false);

                            List<float> q = new List<float> { 2f, 2f };
                            List<VectorResult> ra = (await a.GetTopKAsync(q, 10, cancellationToken: ct).ConfigureAwait(false)).ToList();
                            List<VectorResult> rb = (await b.GetTopKAsync(q, 10, cancellationToken: ct).ConfigureAwait(false)).ToList();

                            TestAssert.Equal(ra.Count, rb.Count, "Same result count");
                            for (int i = 0; i < ra.Count; i++)
                            {
                                TestAssert.Equal(ra[i].GUID, rb[i].GUID, $"Same GUID at position {i}");
                            }
                        }),

                    Case("Ram.Parameters", "ExtendCandidatesIsHonored",
                        "ExtendCandidates flag does not break insertion or search",
                        async ct =>
                        {
                            HnswIndex index = NewRam(_Dimension2D, seed: 5);
                            index.ExtendCandidates = true;
                            await index.AddNodesAsync(MakeGrid(side: 6), ct).ConfigureAwait(false);

                            List<VectorResult> results = (await index.GetTopKAsync(
                                new List<float> { 2f, 2f }, 3, cancellationToken: ct).ConfigureAwait(false)).ToList();
                            TestAssert.Equal(3, results.Count, "Top-3 with ExtendCandidates");
                        }),
                });
        }

        /// <summary>
        /// Batch insertion / removal correctness.
        /// </summary>
        /// <returns>Batch-operations test suite.</returns>
        public static TestSuiteDescriptor BatchOperationsSuite()
        {
            return new TestSuiteDescriptor(
                suiteId: "Ram.Batch",
                displayName: "RAM Storage - Batch Operations",
                cases: new List<TestCaseDescriptor>
                {
                    Case("Ram.Batch", "MultipleAddBatchesAccumulate",
                        "Adding three batches accumulates totals correctly",
                        async ct =>
                        {
                            HnswIndex index = NewRam(_Dimension2D, seed: 1);
                            Random rng = new Random(1);

                            for (int batch = 0; batch < 3; batch++)
                            {
                                Dictionary<Guid, List<float>> b = new Dictionary<Guid, List<float>>();
                                for (int i = 0; i < 10; i++)
                                    b[Guid.NewGuid()] = new List<float> { (float)rng.NextDouble() * 10f, (float)rng.NextDouble() * 10f };
                                await index.AddNodesAsync(b, ct).ConfigureAwait(false);
                            }

                            List<VectorResult> results = (await index.GetTopKAsync(
                                new List<float> { 5f, 5f }, 30, cancellationToken: ct).ConfigureAwait(false)).ToList();
                            TestAssert.Equal(30, results.Count, "All 30 retrievable after 3 batches");
                        }),

                    Case("Ram.Batch", "RemoveNodesAsync_BatchRemoval",
                        "RemoveNodesAsync removes every supplied id and leaves remainder intact",
                        async ct =>
                        {
                            HnswIndex index = NewRam(_Dimension2D, seed: 1);
                            List<Guid> all = new List<Guid>();
                            Dictionary<Guid, List<float>> vectors = new Dictionary<Guid, List<float>>();
                            for (int i = 0; i < 30; i++)
                            {
                                Guid id = Guid.NewGuid();
                                all.Add(id);
                                vectors[id] = new List<float> { (float)i, (float)i };
                            }
                            await index.AddNodesAsync(vectors, ct).ConfigureAwait(false);

                            List<Guid> toRemove = all.Take(10).ToList();
                            await index.RemoveNodesAsync(toRemove, ct).ConfigureAwait(false);

                            List<VectorResult> results = (await index.GetTopKAsync(
                                new List<float> { 0f, 0f }, 30, cancellationToken: ct).ConfigureAwait(false)).ToList();

                            HashSet<Guid> removed = new HashSet<Guid>(toRemove);
                            foreach (VectorResult r in results)
                            {
                                TestAssert.False(removed.Contains(r.GUID), $"Removed GUID {r.GUID} should not appear");
                            }
                            TestAssert.Equal(20, results.Count, "Remaining count");
                        }),

                    Case("Ram.Batch", "EmptyBatch_Add_Throws",
                        "AddNodesAsync rejects an empty dictionary",
                        async ct =>
                        {
                            HnswIndex index = NewRam(_Dimension2D);
                            await TestAssert.ThrowsAsync<ArgumentException>(
                                () => index.AddNodesAsync(new Dictionary<Guid, List<float>>(), ct),
                                "Empty AddNodesAsync").ConfigureAwait(false);
                        }),

                    Case("Ram.Batch", "EmptyBatch_Remove_Throws",
                        "RemoveNodesAsync rejects an empty list",
                        async ct =>
                        {
                            HnswIndex index = NewRam(_Dimension2D);
                            await index.AddAsync(Guid.NewGuid(), new List<float> { 1f, 1f }, ct).ConfigureAwait(false);
                            await TestAssert.ThrowsAsync<ArgumentException>(
                                () => index.RemoveNodesAsync(new List<Guid>(), ct),
                                "Empty RemoveNodesAsync").ConfigureAwait(false);
                        }),
                });
        }

        /// <summary>
        /// Concurrency tests for parallel reads and reads-during-writes.
        /// </summary>
        /// <returns>Concurrency test suite.</returns>
        public static TestSuiteDescriptor ConcurrencySuite()
        {
            return new TestSuiteDescriptor(
                suiteId: "Ram.Concurrency",
                displayName: "RAM Storage - Concurrency",
                cases: new List<TestCaseDescriptor>
                {
                    Case("Ram.Concurrency", "ParallelReads",
                        "100 concurrent searches all return results without error",
                        async ct =>
                        {
                            HnswIndex index = NewRam(_Dimension64, seed: 1);
                            Random rng = new Random(1);
                            Dictionary<Guid, List<float>> vectors = new Dictionary<Guid, List<float>>();
                            for (int i = 0; i < 200; i++)
                                vectors[Guid.NewGuid()] = RandomVector(_Dimension64, rng);
                            await index.AddNodesAsync(vectors, ct).ConfigureAwait(false);

                            List<Task<List<VectorResult>>> tasks = new List<Task<List<VectorResult>>>();
                            for (int i = 0; i < 100; i++)
                            {
                                List<float> q = RandomVector(_Dimension64, rng);
                                tasks.Add(Task.Run(async () =>
                                    (await index.GetTopKAsync(q, 5, cancellationToken: ct).ConfigureAwait(false)).ToList(), ct));
                            }
                            List<VectorResult>[] results = await Task.WhenAll(tasks).ConfigureAwait(false);

                            TestAssert.Equal(100, results.Length, "All concurrent searches completed");
                            foreach (List<VectorResult> r in results)
                                TestAssert.True(r.Count > 0, "Each concurrent search returned results");
                        }),

                    Case("Ram.Concurrency", "InterleavedAddAndSearch",
                        "Interleaved adds and searches do not deadlock or throw",
                        async ct =>
                        {
                            HnswIndex index = NewRam(_Dimension2D, seed: 1);
                            await index.AddAsync(Guid.NewGuid(), new List<float> { 0f, 0f }, ct).ConfigureAwait(false);

                            List<Task> tasks = new List<Task>();
                            for (int i = 0; i < 50; i++)
                            {
                                int local = i;
                                tasks.Add(Task.Run(async () =>
                                {
                                    await index.AddAsync(Guid.NewGuid(), new List<float> { local, local }, ct).ConfigureAwait(false);
                                }, ct));
                            }
                            for (int i = 0; i < 50; i++)
                            {
                                tasks.Add(Task.Run(async () =>
                                {
                                    await index.GetTopKAsync(new List<float> { 25f, 25f }, 3, cancellationToken: ct).ConfigureAwait(false);
                                }, ct));
                            }
                            await Task.WhenAll(tasks).ConfigureAwait(false);
                        }),
                });
        }

        /// <summary>
        /// Validates that RAM and SQLite produce equivalent topology / search results
        /// for the same fixed inputs.
        /// </summary>
        /// <returns>Cross-storage parity test suite.</returns>
        public static TestSuiteDescriptor CrossStorageParitySuite()
        {
            return new TestSuiteDescriptor(
                suiteId: "Parity.RamVsSqlite",
                displayName: "Parity - RAM vs SQLite",
                cases: new List<TestCaseDescriptor>
                {
                    Case("Parity.RamVsSqlite", "SameSeed_SameTopK",
                        "Identical seed + insertions produce equivalent top-K on both storages",
                        async ct =>
                        {
                            string path = NewTempDb();
                            try
                            {
                                Dictionary<Guid, List<float>> vectors = MakeGrid(side: 6);

                                HnswIndex ram = NewRam(_Dimension2D, seed: 12345);
                                await ram.AddNodesAsync(vectors, ct).ConfigureAwait(false);

                                SqliteStorageProvider sqlProv = new SqliteStorageProvider(path);
                                try
                                {
                                    HnswIndex sql = new HnswIndex(_Dimension2D, sqlProv, seed: 12345);
                                    await sql.AddNodesAsync(vectors, ct).ConfigureAwait(false);

                                    List<float> q = new List<float> { 2f, 2f };
                                    HashSet<Guid> ramSet = new HashSet<Guid>((await ram.GetTopKAsync(q, 5, cancellationToken: ct).ConfigureAwait(false)).Select(r => r.GUID));
                                    HashSet<Guid> sqlSet = new HashSet<Guid>((await sql.GetTopKAsync(q, 5, cancellationToken: ct).ConfigureAwait(false)).Select(r => r.GUID));

                                    int overlap = ramSet.Intersect(sqlSet).Count();
                                    TestAssert.True(overlap >= 4,
                                        $"At least 4/5 of top-5 should match between RAM and SQLite (got {overlap})");
                                }
                                finally { sqlProv.Dispose(); }
                            }
                            finally
                            {
                                SqliteConnection.ClearAllPools();
                                TryDelete(path);
                            }
                        }),
                });
        }

        /// <summary>
        /// Distance-function coverage: Cosine and DotProduct on RAM with topology checks.
        /// </summary>
        /// <returns>Distance-coverage test suite.</returns>
        public static TestSuiteDescriptor DistanceCoverageSuite()
        {
            return new TestSuiteDescriptor(
                suiteId: "Distance.Coverage",
                displayName: "Distance Functions - End-to-End",
                cases: new List<TestCaseDescriptor>
                {
                    Case("Distance.Coverage", "CosineWithUnitVectors",
                        "Cosine search across unit vectors picks closest direction",
                        async ct =>
                        {
                            HnswIndex index = NewRam(_Dimension2D);
                            index.DistanceFunction = new CosineDistance();
                            Guid east = Guid.NewGuid();
                            Guid northeast = Guid.NewGuid();
                            Guid north = Guid.NewGuid();
                            Guid west = Guid.NewGuid();

                            await index.AddNodesAsync(new Dictionary<Guid, List<float>>
                            {
                                { east, new List<float> { 1f, 0f } },
                                { northeast, new List<float> { 0.707f, 0.707f } },
                                { north, new List<float> { 0f, 1f } },
                                { west, new List<float> { -1f, 0f } },
                            }, ct).ConfigureAwait(false);

                            List<VectorResult> results = (await index.GetTopKAsync(
                                new List<float> { 0.99f, 0.01f }, 1, cancellationToken: ct).ConfigureAwait(false)).ToList();
                            TestAssert.Equal(east, results[0].GUID, "Closest by cosine to nearly-east");
                        }),

                    Case("Distance.Coverage", "DotProductPicksLargestAlignment",
                        "DotProduct distance picks the vector with highest dot-product alignment",
                        async ct =>
                        {
                            HnswIndex index = NewRam(_Dimension2D);
                            index.DistanceFunction = new DotProductDistance();
                            Guid huge = Guid.NewGuid();

                            await index.AddNodesAsync(new Dictionary<Guid, List<float>>
                            {
                                { Guid.NewGuid(), new List<float> { 1f, 0f } },
                                { huge, new List<float> { 100f, 0f } },
                                { Guid.NewGuid(), new List<float> { 0f, 100f } },
                            }, ct).ConfigureAwait(false);

                            List<VectorResult> results = (await index.GetTopKAsync(
                                new List<float> { 1f, 0f }, 1, cancellationToken: ct).ConfigureAwait(false)).ToList();
                            TestAssert.Equal(huge, results[0].GUID, "Highest dot product wins");
                        }),
                });
        }

        /// <summary>
        /// Higher-dimensional sanity: 384-d and 768-d (typical embedding sizes).
        /// </summary>
        /// <returns>High-dimensional test suite.</returns>
        public static TestSuiteDescriptor HighDimensionalSuite()
        {
            return new TestSuiteDescriptor(
                suiteId: "Ram.HighDim",
                displayName: "RAM Storage - High-Dimensional Vectors",
                cases: new List<TestCaseDescriptor>
                {
                    Case("Ram.HighDim", "Dim384",
                        "384-d (sentence-transformers size) returns top-K without error",
                        async ct =>
                        {
                            HnswIndex index = NewRam(_Dimension384, seed: 1);
                            Random rng = new Random(1);
                            Dictionary<Guid, List<float>> vectors = new Dictionary<Guid, List<float>>();
                            for (int i = 0; i < 50; i++)
                                vectors[Guid.NewGuid()] = RandomVector(_Dimension384, rng);
                            await index.AddNodesAsync(vectors, ct).ConfigureAwait(false);

                            List<float> query = RandomVector(_Dimension384, rng);
                            List<VectorResult> results = (await index.GetTopKAsync(query, 10, cancellationToken: ct).ConfigureAwait(false)).ToList();
                            TestAssert.Equal(10, results.Count, "Top-10 in 384-d");
                        }),

                    Case("Ram.HighDim", "Dim768",
                        "768-d (BERT size) returns top-K without error",
                        async ct =>
                        {
                            HnswIndex index = NewRam(_Dimension768, seed: 1);
                            Random rng = new Random(1);
                            Dictionary<Guid, List<float>> vectors = new Dictionary<Guid, List<float>>();
                            for (int i = 0; i < 30; i++)
                                vectors[Guid.NewGuid()] = RandomVector(_Dimension768, rng);
                            await index.AddNodesAsync(vectors, ct).ConfigureAwait(false);

                            List<float> query = RandomVector(_Dimension768, rng);
                            List<VectorResult> results = (await index.GetTopKAsync(query, 5, cancellationToken: ct).ConfigureAwait(false)).ToList();
                            TestAssert.Equal(5, results.Count, "Top-5 in 768-d");
                        }),
                });
        }

        /// <summary>
        /// State export/import round-trip with non-trivial content.
        /// </summary>
        /// <returns>State round-trip test suite.</returns>
        public static TestSuiteDescriptor StateRoundtripSuite()
        {
            return new TestSuiteDescriptor(
                suiteId: "Ram.StateRoundtrip",
                displayName: "RAM Storage - State Round-Trip",
                cases: new List<TestCaseDescriptor>
                {
                    Case("Ram.StateRoundtrip", "EveryDistanceFunctionPersists",
                        "Distance function name survives ExportState/ImportState for all three functions",
                        async ct =>
                        {
                            string[] names = { "Euclidean", "Cosine", "DotProduct" };
                            foreach (string fn in names)
                            {
                                HnswIndex original = NewRam(_Dimension2D);
                                original.DistanceFunction = fn switch
                                {
                                    "Euclidean" => new EuclideanDistance(),
                                    "Cosine" => new CosineDistance(),
                                    _ => new DotProductDistance(),
                                };
                                await original.AddAsync(Guid.NewGuid(), new List<float> { 1f, 1f }, ct).ConfigureAwait(false);

                                HnswState state = await original.ExportStateAsync(ct).ConfigureAwait(false);
                                HnswIndex restored = NewRam(_Dimension2D);
                                await restored.ImportStateAsync(state, ct).ConfigureAwait(false);

                                TestAssert.Equal(fn, restored.DistanceFunction.Name, $"{fn} persisted");
                            }
                        }),

                    Case("Ram.StateRoundtrip", "DimensionMismatchRejected",
                        "Importing into an index with a different dimension throws",
                        async ct =>
                        {
                            HnswIndex source = NewRam(2);
                            await source.AddAsync(Guid.NewGuid(), new List<float> { 1f, 2f }, ct).ConfigureAwait(false);
                            HnswState state = await source.ExportStateAsync(ct).ConfigureAwait(false);

                            HnswIndex target = NewRam(3);
                            await TestAssert.ThrowsAsync<ArgumentException>(
                                () => target.ImportStateAsync(state, ct),
                                "Mismatched dimension import").ConfigureAwait(false);
                        }),
                });
        }

        /// <summary>
        /// Advanced SQLite tests: batch operations, larger datasets, distance functions.
        /// </summary>
        /// <returns>SQLite advanced test suite.</returns>
        public static TestSuiteDescriptor SqliteAdvancedSuite()
        {
            return new TestSuiteDescriptor(
                suiteId: "Sqlite.Advanced",
                displayName: "SQLite Storage - Advanced",
                cases: new List<TestCaseDescriptor>
                {
                    Case("Sqlite.Advanced", "BatchAdd100",
                        "Batch-add of 100 vectors then search returns results",
                        async ct =>
                        {
                            string path = NewTempDb();
                            try
                            {
                                SqliteStorageProvider provider = new SqliteStorageProvider(path);
                                try
                                {
                                    HnswIndex index = new HnswIndex(_Dimension2D, provider, seed: 1);
                                    Random rng = new Random(1);

                                    Dictionary<Guid, List<float>> vectors = new Dictionary<Guid, List<float>>();
                                    for (int i = 0; i < 100; i++)
                                        vectors[Guid.NewGuid()] = new List<float> { (float)rng.NextDouble() * 100f, (float)rng.NextDouble() * 100f };
                                    await index.AddNodesAsync(vectors, ct).ConfigureAwait(false);

                                    List<VectorResult> results = (await index.GetTopKAsync(
                                        new List<float> { 50f, 50f }, 10, cancellationToken: ct).ConfigureAwait(false)).ToList();
                                    TestAssert.Equal(10, results.Count, "Top-10 from 100 vectors");
                                }
                                finally { provider.Dispose(); }
                            }
                            finally
                            {
                                SqliteConnection.ClearAllPools();
                                TryDelete(path);
                            }
                        }),

                    Case("Sqlite.Advanced", "CosineDistance",
                        "SQLite + CosineDistance: nearest unit vector is by cosine angle",
                        async ct =>
                        {
                            string path = NewTempDb();
                            try
                            {
                                SqliteStorageProvider provider = new SqliteStorageProvider(path);
                                try
                                {
                                    HnswIndex index = new HnswIndex(_Dimension2D, provider);
                                    index.DistanceFunction = new CosineDistance();

                                    Guid east = Guid.NewGuid();
                                    await index.AddNodesAsync(new Dictionary<Guid, List<float>>
                                    {
                                        { east, new List<float> { 1f, 0f } },
                                        { Guid.NewGuid(), new List<float> { 0f, 1f } },
                                    }, ct).ConfigureAwait(false);

                                    List<VectorResult> results = (await index.GetTopKAsync(
                                        new List<float> { 0.99f, 0.01f }, 1, cancellationToken: ct).ConfigureAwait(false)).ToList();
                                    TestAssert.Equal(east, results[0].GUID, "Cosine picks east");
                                }
                                finally { provider.Dispose(); }
                            }
                            finally
                            {
                                SqliteConnection.ClearAllPools();
                                TryDelete(path);
                            }
                        }),

                    Case("Sqlite.Advanced", "RemoveBatch",
                        "RemoveNodesAsync over SQLite excludes every removed GUID",
                        async ct =>
                        {
                            string path = NewTempDb();
                            try
                            {
                                SqliteStorageProvider provider = new SqliteStorageProvider(path);
                                try
                                {
                                    HnswIndex index = new HnswIndex(_Dimension2D, provider, seed: 1);
                                    List<Guid> ids = new List<Guid>();
                                    Dictionary<Guid, List<float>> vectors = new Dictionary<Guid, List<float>>();
                                    for (int i = 0; i < 20; i++)
                                    {
                                        Guid id = Guid.NewGuid();
                                        ids.Add(id);
                                        vectors[id] = new List<float> { (float)i, (float)i };
                                    }
                                    await index.AddNodesAsync(vectors, ct).ConfigureAwait(false);

                                    List<Guid> toRemove = ids.Take(7).ToList();
                                    await index.RemoveNodesAsync(toRemove, ct).ConfigureAwait(false);

                                    List<VectorResult> results = (await index.GetTopKAsync(
                                        new List<float> { 5f, 5f }, 20, cancellationToken: ct).ConfigureAwait(false)).ToList();
                                    HashSet<Guid> removedSet = new HashSet<Guid>(toRemove);
                                    foreach (VectorResult r in results)
                                        TestAssert.False(removedSet.Contains(r.GUID), $"Removed id {r.GUID}");
                                }
                                finally { provider.Dispose(); }
                            }
                            finally
                            {
                                SqliteConnection.ClearAllPools();
                                TryDelete(path);
                            }
                        }),
                });
        }

        /// <summary>
        /// SQLite state export/import round-trip.
        /// </summary>
        /// <returns>SQLite state suite.</returns>
        public static TestSuiteDescriptor SqliteStateSuite()
        {
            return new TestSuiteDescriptor(
                suiteId: "Sqlite.State",
                displayName: "SQLite Storage - State Export/Import",
                cases: new List<TestCaseDescriptor>
                {
                    Case("Sqlite.State", "ExportFromSqliteImportToRam",
                        "State exported from SQLite imports cleanly into a RAM index",
                        async ct =>
                        {
                            string path = NewTempDb();
                            try
                            {
                                HnswState state;
                                Guid markerId = Guid.NewGuid();

                                SqliteStorageProvider sqlProv = new SqliteStorageProvider(path);
                                try
                                {
                                    HnswIndex sql = new HnswIndex(_Dimension2D, sqlProv);
                                    await sql.AddNodesAsync(new Dictionary<Guid, List<float>>
                                    {
                                        { markerId, new List<float> { 1f, 1f } },
                                        { Guid.NewGuid(), new List<float> { 9f, 9f } },
                                    }, ct).ConfigureAwait(false);

                                    state = await sql.ExportStateAsync(ct).ConfigureAwait(false);
                                }
                                finally { sqlProv.Dispose(); }

                                HnswIndex ram = NewRam(_Dimension2D);
                                await ram.ImportStateAsync(state, ct).ConfigureAwait(false);

                                List<VectorResult> results = (await ram.GetTopKAsync(
                                    new List<float> { 1f, 1f }, 1, cancellationToken: ct).ConfigureAwait(false)).ToList();
                                TestAssert.Equal(1, results.Count, "Top-1 imported into RAM");
                                TestAssert.Equal(markerId, results[0].GUID, "Imported GUID");
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

        private static TestCaseDescriptor Case(string suiteId, string caseId, string display, Func<CancellationToken, Task> exec)
        {
            return new TestCaseDescriptor(suiteId: suiteId, caseId: caseId, displayName: display, executeAsync: exec);
        }

        private static HnswIndex NewRam(int dimension, int? seed = null)
        {
            RamStorageProvider provider = new RamStorageProvider();
            HnswIndex idx = new HnswIndex(dimension, provider);
            if (seed.HasValue) idx.Seed = seed.Value;
            return idx;
        }

        private static List<float> RandomVector(int dimension, Random rng, float center = 0f, float scale = 1f)
        {
            List<float> v = new List<float>(dimension);
            for (int i = 0; i < dimension; i++)
                v.Add(center + (float)(rng.NextDouble() * 2.0 - 1.0) * scale);
            return v;
        }

        private static Dictionary<Guid, List<float>> MakeGrid(int side)
        {
            Dictionary<Guid, List<float>> grid = new Dictionary<Guid, List<float>>(side * side);
            for (int x = 0; x < side; x++)
            {
                for (int y = 0; y < side; y++)
                {
                    grid[Guid.NewGuid()] = new List<float> { x, y };
                }
            }
            return grid;
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
                // best-effort
            }
        }

        #endregion
    }
}
