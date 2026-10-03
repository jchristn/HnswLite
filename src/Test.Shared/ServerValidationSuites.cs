namespace HnswLite.Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    using HnswIndex.Server.Classes;
    using HnswIndex.Server.Services;
    using Touchstone.Core;

    /// <summary>
    /// Request validation in <see cref="IndexManager"/>. A vector whose length does not match the index
    /// dimension must raise <see cref="VectorDimensionMismatchException"/> (mapped to 400 InvalidDimension by the
    /// REST API), never the <see cref="InvalidOperationException"/> reserved for a missing index.
    /// </summary>
    public static class ServerValidationSuites
    {
        #region Public-Members

        /// <summary>
        /// All server-validation suites surfaced through the runner.
        /// </summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get
            {
                return new List<TestSuiteDescriptor>
                {
                    DimensionValidationSuite(),
                };
            }
        }

        #endregion

        #region Private-Members

        private const string _SuiteId = "Server.Validation";
        private const int _Dimension = 3;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dimension-mismatch and missing-index cases for add, batch add, and search.
        /// </summary>
        /// <returns>Dimension validation test suite.</returns>
        public static TestSuiteDescriptor DimensionValidationSuite()
        {
            return new TestSuiteDescriptor(
                suiteId: _SuiteId,
                displayName: "Server - Dimension Validation",
                cases: new List<TestCaseDescriptor>
                {
                    Case("AddVectorDimensionMismatch",
                        "AddVector with the wrong dimension throws VectorDimensionMismatchException",
                        async (manager, indexName, ct) =>
                        {
                            VectorDimensionMismatchException ex = await CaptureMismatchAsync(
                                () => manager.AddVectorAsync(indexName, new AddVectorRequest
                                {
                                    GUID = Guid.NewGuid(),
                                    Vector = new List<float> { 1f, 2f }
                                }, ct)).ConfigureAwait(false);

                            TestAssert.Equal(2, ex.ActualDimension, "actual dimension reported");
                            TestAssert.Equal(_Dimension, ex.ExpectedDimension, "expected dimension reported");
                        }),

                    Case("AddVectorsDimensionMismatch",
                        "AddVectors with one wrong-dimension entry throws and adds nothing",
                        async (manager, indexName, ct) =>
                        {
                            VectorDimensionMismatchException ex = await CaptureMismatchAsync(
                                () => manager.AddVectorsAsync(indexName, new AddVectorsRequest
                                {
                                    Vectors = new List<AddVectorRequest>
                                    {
                                        new AddVectorRequest { GUID = Guid.NewGuid(), Vector = new List<float> { 1f, 2f, 3f } },
                                        new AddVectorRequest { GUID = Guid.NewGuid(), Vector = new List<float> { 1f, 2f, 3f, 4f } },
                                    }
                                }, ct)).ConfigureAwait(false);

                            TestAssert.Equal(4, ex.ActualDimension, "actual dimension reported");

                            IndexResponse? index = manager.GetIndex(indexName);
                            TestAssert.True(index != null, "index still exists");
                            TestAssert.Equal(0, index!.VectorCount, "no vectors added from a rejected batch");
                        }),

                    Case("SearchDimensionMismatch",
                        "Search with the wrong query dimension throws VectorDimensionMismatchException",
                        async (manager, indexName, ct) =>
                        {
                            VectorDimensionMismatchException ex = await CaptureMismatchAsync(
                                () => manager.SearchAsync(indexName, new SearchRequest
                                {
                                    Vector = new List<float> { 1f },
                                    K = 1
                                }, ct)).ConfigureAwait(false);

                            TestAssert.Equal(1, ex.ActualDimension, "actual dimension reported");
                        }),

                    Case("MissingIndexStillInvalidOperation",
                        "A missing index still throws InvalidOperationException, not a dimension error",
                        async (manager, indexName, ct) =>
                        {
                            await TestAssert.ThrowsAsync<InvalidOperationException>(
                                () => manager.AddVectorAsync("does-not-exist", new AddVectorRequest
                                {
                                    GUID = Guid.NewGuid(),
                                    Vector = new List<float> { 1f, 2f }
                                }, ct), "missing index on add").ConfigureAwait(false);

                            await TestAssert.ThrowsAsync<InvalidOperationException>(
                                () => manager.SearchAsync("does-not-exist", new SearchRequest
                                {
                                    Vector = new List<float> { 1f, 2f, 3f },
                                    K = 1
                                }, ct), "missing index on search").ConfigureAwait(false);
                        }),
                });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string display, Func<IndexManager, string, CancellationToken, Task> exec)
        {
            return new TestCaseDescriptor(
                suiteId: _SuiteId,
                caseId: caseId,
                displayName: display,
                executeAsync: async ct =>
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), "hnswlite-validation-tests-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);
                    IndexManager manager = new IndexManager(tempDir);
                    try
                    {
                        string indexName = "idx-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                        await manager.CreateIndexAsync(new CreateIndexRequest
                        {
                            Name = indexName,
                            Dimension = _Dimension,
                            StorageType = "RAM",
                            DistanceFunction = "Euclidean",
                        }, ct).ConfigureAwait(false);

                        await exec(manager, indexName, ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        try { await manager.DisposeAsync().ConfigureAwait(false); } catch { }
                        try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
                    }
                });
        }

        private static async Task<VectorDimensionMismatchException> CaptureMismatchAsync(Func<Task> func)
        {
            try
            {
                await func().ConfigureAwait(false);
            }
            catch (VectorDimensionMismatchException ex)
            {
                return ex;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Assertion failed: expected VectorDimensionMismatchException, got {ex.GetType().Name}: {ex.Message}");
            }

            throw new InvalidOperationException(
                "Assertion failed: expected VectorDimensionMismatchException but no exception was thrown.");
        }

        #endregion
    }
}
