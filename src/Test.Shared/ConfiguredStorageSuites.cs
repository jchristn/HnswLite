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
    using HnswIndex.PostgresqlStorage;
    using HnswIndex.SqliteStorage;
    using Hsnw;
    using Touchstone.Core;

    /// <summary>
    /// Shared suite that runs against the storage backend selected by CLI or environment.
    /// </summary>
    public static class ConfiguredStorageSuites
    {
        private const int _Dimension = 2;

        /// <summary>
        /// Builds the configured-storage suite.
        /// </summary>
        public static TestSuiteDescriptor ConfiguredStorageSuite(TestStorageConfiguration configuration)
        {
            if (configuration.Storage == null)
            {
                throw new ArgumentException("Storage override is required.", nameof(configuration));
            }

            string storageName = configuration.Storage.Value.ToString();
            return new TestSuiteDescriptor(
                suiteId: "ConfiguredStorage",
                displayName: $"Configured Storage - {storageName}",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        suiteId: "ConfiguredStorage",
                        caseId: "Lifecycle",
                        displayName: $"Configured {storageName} storage supports add/search/remove/metadata",
                        executeAsync: ct => RunLifecycleAsync(configuration, ct)),
                });
        }

        private static async Task RunLifecycleAsync(TestStorageConfiguration configuration, CancellationToken cancellationToken)
        {
            string logicalName = "configured_" + Guid.NewGuid().ToString("N");
            string? sqlitePath = configuration.Storage == TestStorageKind.Sqlite
                ? configuration.SqliteFilename ?? NewTempDb()
                : null;
            bool deleteSqlitePath = configuration.Storage == TestStorageKind.Sqlite
                                    && configuration.SqliteFilename == null
                                    && sqlitePath != null;
            Guid expected = Guid.NewGuid();
            Guid removed = Guid.NewGuid();

            await using ConfiguredStorageHandle handle = await CreateStorageAsync(
                configuration,
                logicalName,
                sqlitePath,
                deleteSqliteOnDispose: false,
                createIfNotExists: true,
                cancellationToken).ConfigureAwait(false);

            HnswIndex index = new HnswIndex(_Dimension, handle.Provider, seed: 42);
            await index.AddNodesAsync(new Dictionary<Guid, List<float>>
            {
                { expected, new List<float> { 1f, 1f } },
                { removed, new List<float> { 2f, 2f } },
                { Guid.NewGuid(), new List<float> { 10f, 10f } },
            }, cancellationToken).ConfigureAwait(false);

            IHnswNode node = await handle.Provider.GetNodeAsync(expected, cancellationToken).ConfigureAwait(false);
            await node.SetMetadataAsync(
                "configured-vector",
                new List<string> { "configured", configuration.Storage!.Value.ToString().ToLowerInvariant() },
                new Dictionary<string, object> { { "backend", configuration.Storage.Value.ToString() } },
                cancellationToken).ConfigureAwait(false);

            List<VectorResult> results = (await index.GetTopKAsync(
                new List<float> { 1f, 1f },
                1,
                cancellationToken: cancellationToken).ConfigureAwait(false)).ToList();

            TestAssert.Equal(1, results.Count, "Configured storage top-1 count");
            TestAssert.Equal(expected, results[0].GUID, "Configured storage nearest GUID");

            await index.RemoveAsync(removed, cancellationToken).ConfigureAwait(false);
            List<VectorResult> afterRemove = (await index.GetTopKAsync(
                new List<float> { 2f, 2f },
                10,
                cancellationToken: cancellationToken).ConfigureAwait(false)).ToList();
            TestAssert.False(afterRemove.Any(r => r.GUID == removed), "Configured storage removed GUID absent");

            if (configuration.Storage.Value is TestStorageKind.Sqlite or TestStorageKind.Postgresql)
            {
                await handle.Provider.FlushAsync(cancellationToken).ConfigureAwait(false);
                await handle.Provider.DisposeAsync().ConfigureAwait(false);

                await using ConfiguredStorageHandle reopened = await CreateStorageAsync(
                    configuration,
                    logicalName,
                    sqlitePath,
                    deleteSqliteOnDispose: deleteSqlitePath,
                    createIfNotExists: false,
                    cancellationToken).ConfigureAwait(false);

                IHnswNode persisted = await reopened.Provider.GetNodeAsync(expected, cancellationToken).ConfigureAwait(false);
                TestAssert.Equal("configured-vector", persisted.Name, "Configured storage metadata persisted");
                TestAssert.True(persisted.Labels!.Contains("configured"), "Configured storage label persisted");
            }
        }

        private static async Task<ConfiguredStorageHandle> CreateStorageAsync(
            TestStorageConfiguration configuration,
            string logicalName,
            string? sqlitePath,
            bool deleteSqliteOnDispose,
            bool createIfNotExists,
            CancellationToken cancellationToken)
        {
            return configuration.Storage switch
            {
                TestStorageKind.Ram => new ConfiguredStorageHandle(new RamStorageProvider(), null),
                TestStorageKind.Sqlite => await CreateSqliteAsync(sqlitePath, deleteSqliteOnDispose, createIfNotExists, cancellationToken).ConfigureAwait(false),
                TestStorageKind.Postgresql => await CreatePostgresqlAsync(configuration, logicalName, createIfNotExists, cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException("Storage override is not configured."),
            };
        }

        private static async Task<ConfiguredStorageHandle> CreateSqliteAsync(
            string? sqlitePath,
            bool deleteSqliteOnDispose,
            bool createIfNotExists,
            CancellationToken cancellationToken)
        {
            string path = sqlitePath ?? NewTempDb();
            SqliteStorageProvider provider = await SqliteStorageProvider.CreateAsync(
                path,
                createIfNotExists,
                cancellationToken).ConfigureAwait(false);

            return new ConfiguredStorageHandle(provider, deleteSqliteOnDispose ? path : null);
        }

        private static async Task<ConfiguredStorageHandle> CreatePostgresqlAsync(
            TestStorageConfiguration configuration,
            string logicalName,
            bool createIfNotExists,
            CancellationToken cancellationToken)
        {
            PostgresqlStorageProvider provider = await PostgresqlStorageProvider.CreateAsync(
                configuration.GetRequiredPostgresqlConnectionString(),
                logicalName,
                _Dimension,
                "Euclidean",
                16,
                32,
                200,
                createIfNotExists,
                cancellationToken).ConfigureAwait(false);

            return new ConfiguredStorageHandle(provider, null);
        }

        private static string NewTempDb()
        {
            string dir = Path.Combine(Path.GetTempPath(), "hnswlite-configured-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "configured.db");
        }

        private sealed class ConfiguredStorageHandle : IAsyncDisposable
        {
            public ConfiguredStorageHandle(IStorageProvider provider, string? sqlitePathToDelete)
            {
                Provider = provider;
                SqlitePathToDelete = sqlitePathToDelete;
            }

            public IStorageProvider Provider { get; }

            private string? SqlitePathToDelete { get; }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    await Provider.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    if (!string.IsNullOrWhiteSpace(SqlitePathToDelete))
                    {
                        TryDelete(SqlitePathToDelete);
                    }
                }
            }

            private static void TryDelete(string path)
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                    string? dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir)) Directory.Delete(dir, true);
                }
                catch
                {
                    // Best-effort cleanup only.
                }
            }
        }
    }
}
