namespace HnswLite.Test.Automated
{
    using System;
    using System.Threading.Tasks;

    using HnswLite.Test.Shared;
    using Touchstone.Cli;

    /// <summary>
    /// Console runner for HnswLite Touchstone tests.
    /// Exit code 0 = all passed; exit code 1 = at least one failure.
    /// Optional argument: <c>--results &lt;path&gt;</c> to write a JSON results file.
    /// </summary>
    public static class Program
    {
        #region Entrypoint

        /// <summary>
        /// Main entry point.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Process exit code.</returns>
        public static async Task<int> Main(string[] args)
        {
            RunnerOptions options = RunnerOptions.Parse(args);
            TestStorageConfiguration.ApplyToEnvironment(
                options.Storage,
                options.SqliteFilename,
                options.PostgresqlConnectionString,
                options.PostgresqlHost,
                options.PostgresqlPort,
                options.PostgresqlUser,
                options.PostgresqlPassword,
                options.PostgresqlDatabase,
                options.PostgresqlSchema);

            if (!string.IsNullOrWhiteSpace(options.Storage))
            {
                Console.WriteLine("Storage override: " + options.GetStorageDisplayName());
            }

            string? resultsPath = options.ResultsPath;
            int exitCode = await ConsoleRunner.RunAsync(HnswSuites.All, null, resultsPath).ConfigureAwait(false);
            return exitCode;
        }

        #endregion

        #region Private-Methods

        private sealed class RunnerOptions
        {
            public string? ResultsPath { get; private set; }
            public string? Storage { get; private set; }
            public string? SqliteFilename { get; private set; }
            public string? PostgresqlConnectionString { get; private set; }
            public string? PostgresqlHost { get; private set; }
            public string? PostgresqlPort { get; private set; }
            public string? PostgresqlUser { get; private set; }
            public string? PostgresqlPassword { get; private set; }
            public string? PostgresqlDatabase { get; private set; }
            public string? PostgresqlSchema { get; private set; }

            public string GetStorageDisplayName()
            {
                if (string.IsNullOrWhiteSpace(Storage))
                {
                    return string.Empty;
                }

                string storage = Storage.Trim();
                switch (storage.ToLowerInvariant())
                {
                    case "sqlite":
                    case "sqlite3":
                        return string.IsNullOrWhiteSpace(SqliteFilename)
                            ? "sqlite"
                            : "sqlite://" + SqliteFilename.Trim();
                    case "postgresql":
                    case "postgres":
                    case "pg":
                        return GetPostgresqlDisplayName();
                    default:
                        return storage;
                }
            }

            public static RunnerOptions Parse(string[]? args)
            {
                RunnerOptions options = new RunnerOptions();
                if (args == null) return options;

                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i];
                    string? value = null;
                    int equalsIndex = arg.IndexOf('=');
                    if (equalsIndex > 0)
                    {
                        value = arg.Substring(equalsIndex + 1);
                        arg = arg.Substring(0, equalsIndex);
                    }
                    else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        value = args[++i];
                    }

                    switch (arg.ToLowerInvariant())
                    {
                        case "--results":
                            options.ResultsPath = value;
                            break;
                        case "--storage":
                            options.Storage = value;
                            break;
                        case "--filename":
                        case "--file":
                        case "--sqlite-filename":
                            options.SqliteFilename = value;
                            break;
                        case "--connection-string":
                        case "--connectionstring":
                            options.PostgresqlConnectionString = value;
                            break;
                        case "--host":
                            options.PostgresqlHost = value;
                            break;
                        case "--port":
                            options.PostgresqlPort = value;
                            break;
                        case "--user":
                        case "--username":
                            options.PostgresqlUser = value;
                            break;
                        case "--pass":
                        case "--password":
                            options.PostgresqlPassword = value;
                            break;
                        case "--database":
                        case "--databasename":
                        case "--dbname":
                            options.PostgresqlDatabase = value;
                            break;
                        case "--schema":
                            options.PostgresqlSchema = value;
                            break;
                    }
                }

                return options;
            }

            private string GetPostgresqlDisplayName()
            {
                if (!string.IsNullOrWhiteSpace(PostgresqlConnectionString))
                {
                    return "postgresql://connection-string";
                }

                string host = string.IsNullOrWhiteSpace(PostgresqlHost) ? "localhost" : PostgresqlHost.Trim();
                string? port = string.IsNullOrWhiteSpace(PostgresqlPort) ? null : PostgresqlPort.Trim();
                string? database = string.IsNullOrWhiteSpace(PostgresqlDatabase) ? null : PostgresqlDatabase.Trim();
                string? schema = string.IsNullOrWhiteSpace(PostgresqlSchema) ? null : PostgresqlSchema.Trim();

                string display = string.IsNullOrWhiteSpace(port)
                    ? "postgresql://" + host
                    : "postgresql://" + host + ":" + port;

                if (!string.IsNullOrWhiteSpace(database))
                {
                    display += "/" + database;
                }

                if (!string.IsNullOrWhiteSpace(schema))
                {
                    display += "?schema=" + schema;
                }

                return display;
            }
        }

        #endregion
    }
}
