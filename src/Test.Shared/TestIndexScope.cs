namespace HnswLite.Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Hnsw;
    using Hnsw.RamStorage;
    using HnswIndex.PostgresqlStorage;
    using HnswIndex.SqliteStorage;
    using Microsoft.Data.Sqlite;
    using Npgsql;

    /// <summary>
    /// Owns an HNSW index and its backing test storage for one test case.
    /// </summary>
    public sealed class TestIndexScope : IAsyncDisposable
    {
        private static readonly object _DetachedLock = new object();
        private static readonly List<TestIndexScope> _DetachedScopes = new List<TestIndexScope>();
        private static bool _ProcessExitRegistered;

        private readonly TestStorageLocation _Location;
        private readonly bool _CleanupLocationOnDispose;
        private bool _Disposed;

        private TestIndexScope(
            HnswIndex index,
            IStorageProvider provider,
            TestStorageLocation location,
            bool cleanupLocationOnDispose)
        {
            Index = index;
            Provider = provider;
            Kind = location.Kind;
            _Location = location;
            _CleanupLocationOnDispose = cleanupLocationOnDispose;
        }

        /// <summary>
        /// Gets the index under test.
        /// </summary>
        public HnswIndex Index { get; }

        /// <summary>
        /// Gets the storage provider under test.
        /// </summary>
        public IStorageProvider Provider { get; }

        /// <summary>
        /// Gets the storage backend kind.
        /// </summary>
        public TestStorageKind Kind { get; }

        /// <summary>
        /// Creates a new isolated test index using the configured override when present.
        /// </summary>
        public static Task<TestIndexScope> CreateAsync(
            int dimension,
            CancellationToken cancellationToken,
            int? seed = null,
            TestStorageKind defaultKind = TestStorageKind.Ram,
            bool cleanupLocationOnDispose = true)
        {
            TestStorageConfiguration configuration = TestStorageConfiguration.FromEnvironment();
            TestStorageKind kind = configuration.Storage ?? defaultKind;
            TestStorageLocation location = TestStorageLocation.Create(configuration, kind, dimension);
            return CreateAsync(location, cancellationToken, seed, createIfNotExists: true, cleanupLocationOnDispose);
        }

        /// <summary>
        /// Creates a configured index for legacy tests that do not own an async disposal scope.
        /// Detached scopes are cleaned up when the test process exits.
        /// </summary>
        public static HnswIndex CreateDetachedIndex(
            int dimension,
            int? seed = null,
            TestStorageKind defaultKind = TestStorageKind.Ram)
        {
            TestIndexScope scope = CreateAsync(
                dimension,
                CancellationToken.None,
                seed,
                defaultKind,
                cleanupLocationOnDispose: true).GetAwaiter().GetResult();

            lock (_DetachedLock)
            {
                if (!_ProcessExitRegistered)
                {
                    AppDomain.CurrentDomain.ProcessExit += (_, _) => DisposeDetachedScopes();
                    _ProcessExitRegistered = true;
                }

                _DetachedScopes.Add(scope);
            }

            return scope.Index;
        }

        /// <summary>
        /// Reopens the same durable test index. RAM cannot be reopened.
        /// </summary>
        public Task<TestIndexScope> ReopenAsync(CancellationToken cancellationToken, int? seed = null)
        {
            if (Kind == TestStorageKind.Ram)
            {
                throw new InvalidOperationException("RAM test storage cannot be reopened.");
            }

            return CreateAsync(_Location, cancellationToken, seed, createIfNotExists: false, cleanupLocationOnDispose: false);
        }

        /// <summary>
        /// Cleans durable test artifacts for this scope's location.
        /// </summary>
        public Task CleanupLocationAsync(CancellationToken cancellationToken = default)
        {
            return _Location.CleanupAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_Disposed) return;
            _Disposed = true;

            try
            {
                await Provider.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                if (_CleanupLocationOnDispose)
                {
                    await _Location.CleanupAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        private static async Task<TestIndexScope> CreateAsync(
            TestStorageLocation location,
            CancellationToken cancellationToken,
            int? seed,
            bool createIfNotExists,
            bool cleanupLocationOnDispose)
        {
            IStorageProvider provider = location.Kind switch
            {
                TestStorageKind.Ram => new RamStorageProvider(),
                TestStorageKind.Sqlite => await CreateSqliteProviderAsync(location, createIfNotExists, cancellationToken).ConfigureAwait(false),
                TestStorageKind.Postgresql => await CreatePostgresqlProviderAsync(location, createIfNotExists, cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException("Unsupported storage kind."),
            };

            HnswIndex index = new HnswIndex(location.Dimension, provider);
            if (seed.HasValue)
            {
                index.Seed = seed.Value;
            }

            return new TestIndexScope(index, provider, location, cleanupLocationOnDispose);
        }

        private static void DisposeDetachedScopes()
        {
            List<TestIndexScope> scopes;
            lock (_DetachedLock)
            {
                scopes = new List<TestIndexScope>(_DetachedScopes);
                _DetachedScopes.Clear();
            }

            foreach (TestIndexScope scope in scopes)
            {
                try
                {
                    scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch
                {
                    // Best-effort process-exit cleanup only.
                }
            }
        }

        private static Task<SqliteStorageProvider> CreateSqliteProviderAsync(
            TestStorageLocation location,
            bool createIfNotExists,
            CancellationToken cancellationToken)
        {
            return SqliteStorageProvider.CreateAsync(
                location.SqlitePath!,
                location.SqliteNodesTable!,
                location.SqliteNeighborsTable!,
                location.SqliteMetadataTable!,
                location.SqliteLayersTable!,
                createIfNotExists,
                cancellationToken);
        }

        private static Task<PostgresqlStorageProvider> CreatePostgresqlProviderAsync(
            TestStorageLocation location,
            bool createIfNotExists,
            CancellationToken cancellationToken)
        {
            return PostgresqlStorageProvider.CreateAsync(
                location.PostgresqlConnectionString!,
                location.PostgresqlIndexName!,
                location.Dimension,
                "Euclidean",
                16,
                32,
                200,
                createIfNotExists,
                cancellationToken);
        }

        private sealed class TestStorageLocation
        {
            private TestStorageLocation(TestStorageKind kind, int dimension)
            {
                Kind = kind;
                Dimension = dimension;
            }

            public TestStorageKind Kind { get; }
            public int Dimension { get; }
            public string? SqlitePath { get; private set; }
            public string? SqliteNodesTable { get; private set; }
            public string? SqliteNeighborsTable { get; private set; }
            public string? SqliteMetadataTable { get; private set; }
            public string? SqliteLayersTable { get; private set; }
            public bool DeleteSqliteFileOnCleanup { get; private set; }
            public string? PostgresqlConnectionString { get; private set; }
            public string? PostgresqlIndexName { get; private set; }

            public static TestStorageLocation Create(
                TestStorageConfiguration configuration,
                TestStorageKind kind,
                int dimension)
            {
                TestStorageLocation location = new TestStorageLocation(kind, dimension);
                switch (kind)
                {
                    case TestStorageKind.Ram:
                        return location;

                    case TestStorageKind.Sqlite:
                        string path = ResolveSqlitePath(configuration);
                        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
                        if (!string.IsNullOrWhiteSpace(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }

                        string prefix = "hnsw_test_" + Guid.NewGuid().ToString("N");
                        location.SqlitePath = path;
                        location.SqliteNodesTable = prefix + "_nodes";
                        location.SqliteNeighborsTable = prefix + "_neighbors";
                        location.SqliteMetadataTable = prefix + "_metadata";
                        location.SqliteLayersTable = prefix + "_layers";
                        location.DeleteSqliteFileOnCleanup = string.IsNullOrWhiteSpace(configuration.SqliteFilename);
                        return location;

                    case TestStorageKind.Postgresql:
                        location.PostgresqlConnectionString = configuration.GetRequiredPostgresqlConnectionString();
                        location.PostgresqlIndexName = "test_full_" + Guid.NewGuid().ToString("N");
                        return location;

                    default:
                        throw new InvalidOperationException("Unsupported storage kind.");
                }
            }

            public async Task CleanupAsync(CancellationToken cancellationToken)
            {
                switch (Kind)
                {
                    case TestStorageKind.Sqlite:
                        await CleanupSqliteAsync(cancellationToken).ConfigureAwait(false);
                        break;
                    case TestStorageKind.Postgresql:
                        await CleanupPostgresqlAsync(cancellationToken).ConfigureAwait(false);
                        break;
                }
            }

            private async Task CleanupSqliteAsync(CancellationToken cancellationToken)
            {
                if (string.IsNullOrWhiteSpace(SqlitePath))
                {
                    return;
                }

                try
                {
                    await using SqliteConnection conn = new SqliteConnection("Data Source=" + SqlitePath);
                    await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
                    await using SqliteCommand cmd = conn.CreateCommand();
                    cmd.CommandText = string.Join(
                        Environment.NewLine,
                        new[]
                        {
                            $"DROP TABLE IF EXISTS {SqliteNeighborsTable};",
                            $"DROP TABLE IF EXISTS {SqliteLayersTable};",
                            $"DROP TABLE IF EXISTS {SqliteNodesTable};",
                            $"DROP TABLE IF EXISTS {SqliteMetadataTable};",
                        });
                    await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort cleanup only.
                }
                finally
                {
                    SqliteConnection.ClearAllPools();
                }

                if (!DeleteSqliteFileOnCleanup)
                {
                    return;
                }

                TryDelete(SqlitePath);
            }

            private async Task CleanupPostgresqlAsync(CancellationToken cancellationToken)
            {
                if (string.IsNullOrWhiteSpace(PostgresqlConnectionString)
                    || string.IsNullOrWhiteSpace(PostgresqlIndexName))
                {
                    return;
                }

                try
                {
                    await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(PostgresqlConnectionString);
                    await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                    await using NpgsqlCommand cmd = conn.CreateCommand();
                    cmd.CommandText = "DELETE FROM hnsw_indexes WHERE name = @name;";
                    cmd.Parameters.AddWithValue("name", PostgresqlIndexName);
                    await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort cleanup only.
                }
            }

            private static string ResolveSqlitePath(TestStorageConfiguration configuration)
            {
                if (!string.IsNullOrWhiteSpace(configuration.SqliteFilename))
                {
                    return configuration.SqliteFilename;
                }

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
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    {
                        Directory.Delete(dir, recursive: true);
                    }
                }
                catch
                {
                    // Best-effort cleanup only.
                }
            }
        }
    }
}
