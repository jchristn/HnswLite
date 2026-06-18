namespace HnswLite.Test.Shared
{
    using System;
    using Npgsql;

    /// <summary>
    /// Shared storage override configuration for the console and adapter test runners.
    /// </summary>
    public sealed class TestStorageConfiguration
    {
        public const string StorageEnvVar = "HNSWLITE_TEST_STORAGE";
        public const string SqliteFilenameEnvVar = "HNSWLITE_TEST_SQLITE_FILENAME";
        public const string PostgresqlConnectionEnvVar = "HNSWLITE_TEST_POSTGRES_CONNECTION";
        public const string LegacyPostgresqlConnectionEnvVar = "HNSWLITE_POSTGRES_TEST_CONNECTION";
        public const string PostgresqlHostEnvVar = "HNSWLITE_TEST_POSTGRES_HOST";
        public const string PostgresqlPortEnvVar = "HNSWLITE_TEST_POSTGRES_PORT";
        public const string PostgresqlUserEnvVar = "HNSWLITE_TEST_POSTGRES_USER";
        public const string PostgresqlPasswordEnvVar = "HNSWLITE_TEST_POSTGRES_PASSWORD";
        public const string PostgresqlDatabaseEnvVar = "HNSWLITE_TEST_POSTGRES_DATABASE";
        public const string PostgresqlSchemaEnvVar = "HNSWLITE_TEST_POSTGRES_SCHEMA";

        private const string _LegacyPostgresqlHostEnvVar = "HNSWLITE_POSTGRES_TEST_HOST";
        private const string _LegacyPostgresqlPortEnvVar = "HNSWLITE_POSTGRES_TEST_PORT";
        private const string _LegacyPostgresqlUserEnvVar = "HNSWLITE_POSTGRES_TEST_USER";
        private const string _LegacyPostgresqlPasswordEnvVar = "HNSWLITE_POSTGRES_TEST_PASSWORD";
        private const string _LegacyPostgresqlDatabaseEnvVar = "HNSWLITE_POSTGRES_TEST_DATABASE";
        private const string _LegacyPostgresqlSchemaEnvVar = "HNSWLITE_POSTGRES_TEST_SCHEMA";

        private TestStorageConfiguration(
            TestStorageKind? storage,
            string? sqliteFilename,
            string? postgresqlConnectionString)
        {
            Storage = storage;
            SqliteFilename = sqliteFilename;
            PostgresqlConnectionString = postgresqlConnectionString;
        }

        /// <summary>
        /// Gets the configured storage override, if any.
        /// </summary>
        public TestStorageKind? Storage { get; }

        /// <summary>
        /// Gets the configured SQLite filename, if any.
        /// </summary>
        public string? SqliteFilename { get; }

        /// <summary>
        /// Gets the configured PostgreSQL connection string, if any.
        /// </summary>
        public string? PostgresqlConnectionString { get; }

        /// <summary>
        /// Gets whether a storage override is configured.
        /// </summary>
        public bool HasStorageOverride => Storage.HasValue;

        /// <summary>
        /// Reads the shared storage configuration from environment variables.
        /// </summary>
        public static TestStorageConfiguration FromEnvironment()
        {
            TestStorageKind? storage = ParseStorage(Environment.GetEnvironmentVariable(StorageEnvVar));
            string? sqliteFilename = EmptyToNull(Environment.GetEnvironmentVariable(SqliteFilenameEnvVar));
            string? postgresqlConnectionString = ResolvePostgresqlConnectionString();

            return new TestStorageConfiguration(storage, sqliteFilename, postgresqlConnectionString);
        }

        /// <summary>
        /// Applies a storage override to the process environment before the shared suites are enumerated.
        /// </summary>
        public static void ApplyToEnvironment(
            string? storage,
            string? sqliteFilename,
            string? postgresqlConnectionString,
            string? postgresqlHost,
            string? postgresqlPort,
            string? postgresqlUser,
            string? postgresqlPassword,
            string? postgresqlDatabase,
            string? postgresqlSchema)
        {
            SetEnvironment(StorageEnvVar, storage);
            SetEnvironment(SqliteFilenameEnvVar, sqliteFilename);
            SetEnvironment(PostgresqlConnectionEnvVar, postgresqlConnectionString);
            SetEnvironment(PostgresqlHostEnvVar, postgresqlHost);
            SetEnvironment(PostgresqlPortEnvVar, postgresqlPort);
            SetEnvironment(PostgresqlUserEnvVar, postgresqlUser);
            SetEnvironment(PostgresqlPasswordEnvVar, postgresqlPassword);
            SetEnvironment(PostgresqlDatabaseEnvVar, postgresqlDatabase);
            SetEnvironment(PostgresqlSchemaEnvVar, postgresqlSchema);
        }

        /// <summary>
        /// Returns the configured PostgreSQL connection string, or null when no PostgreSQL configuration exists.
        /// </summary>
        public string? GetPostgresqlConnectionStringOrNull()
        {
            return PostgresqlConnectionString;
        }

        /// <summary>
        /// Returns the configured PostgreSQL connection string or throws a configuration error.
        /// </summary>
        public string GetRequiredPostgresqlConnectionString()
        {
            if (!string.IsNullOrWhiteSpace(PostgresqlConnectionString))
            {
                return PostgresqlConnectionString;
            }

            throw new InvalidOperationException(
                "PostgreSQL storage tests require either HNSWLITE_TEST_POSTGRES_CONNECTION, " +
                "HNSWLITE_POSTGRES_TEST_CONNECTION, or component environment variables " +
                "HNSWLITE_TEST_POSTGRES_HOST/HNSWLITE_TEST_POSTGRES_USER/" +
                "HNSWLITE_TEST_POSTGRES_PASSWORD/HNSWLITE_TEST_POSTGRES_DATABASE.");
        }

        private static TestStorageKind? ParseStorage(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim().ToLowerInvariant() switch
            {
                "ram" or "memory" => TestStorageKind.Ram,
                "sqlite" or "sqlite3" => TestStorageKind.Sqlite,
                "postgresql" or "postgres" or "pg" => TestStorageKind.Postgresql,
                _ => throw new ArgumentException($"Unsupported storage override '{value}'. Use ram, sqlite, or postgresql."),
            };
        }

        private static string? ResolvePostgresqlConnectionString()
        {
            string? explicitConnectionString = EmptyToNull(Environment.GetEnvironmentVariable(PostgresqlConnectionEnvVar))
                                               ?? EmptyToNull(Environment.GetEnvironmentVariable(LegacyPostgresqlConnectionEnvVar));
            if (!string.IsNullOrWhiteSpace(explicitConnectionString))
            {
                return explicitConnectionString;
            }

            string? host = EmptyToNull(Environment.GetEnvironmentVariable(PostgresqlHostEnvVar))
                           ?? EmptyToNull(Environment.GetEnvironmentVariable(_LegacyPostgresqlHostEnvVar));
            string? user = EmptyToNull(Environment.GetEnvironmentVariable(PostgresqlUserEnvVar))
                           ?? EmptyToNull(Environment.GetEnvironmentVariable(_LegacyPostgresqlUserEnvVar));
            string? password = EmptyToNull(Environment.GetEnvironmentVariable(PostgresqlPasswordEnvVar))
                               ?? EmptyToNull(Environment.GetEnvironmentVariable(_LegacyPostgresqlPasswordEnvVar));
            string? database = EmptyToNull(Environment.GetEnvironmentVariable(PostgresqlDatabaseEnvVar))
                               ?? EmptyToNull(Environment.GetEnvironmentVariable(_LegacyPostgresqlDatabaseEnvVar));
            string? port = EmptyToNull(Environment.GetEnvironmentVariable(PostgresqlPortEnvVar))
                           ?? EmptyToNull(Environment.GetEnvironmentVariable(_LegacyPostgresqlPortEnvVar));
            string? schema = EmptyToNull(Environment.GetEnvironmentVariable(PostgresqlSchemaEnvVar))
                             ?? EmptyToNull(Environment.GetEnvironmentVariable(_LegacyPostgresqlSchemaEnvVar));

            if (string.IsNullOrWhiteSpace(host)
                && string.IsNullOrWhiteSpace(user)
                && string.IsNullOrWhiteSpace(password)
                && string.IsNullOrWhiteSpace(database)
                && string.IsNullOrWhiteSpace(port)
                && string.IsNullOrWhiteSpace(schema))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(host)
                || string.IsNullOrWhiteSpace(user)
                || string.IsNullOrWhiteSpace(password)
                || string.IsNullOrWhiteSpace(database))
            {
                return null;
            }

            NpgsqlConnectionStringBuilder builder = new NpgsqlConnectionStringBuilder
            {
                Host = host,
                Username = user,
                Password = password,
                Database = database,
            };

            if (int.TryParse(port, out int parsedPort))
            {
                builder.Port = parsedPort;
            }

            if (!string.IsNullOrWhiteSpace(schema))
            {
                builder.SearchPath = schema;
            }

            return builder.ConnectionString;
        }

        private static string? EmptyToNull(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static void SetEnvironment(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                Environment.SetEnvironmentVariable(name, value.Trim());
            }
        }
    }
}
