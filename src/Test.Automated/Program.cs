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
            string? resultsPath = ParseResultsPath(args);
            int exitCode = await ConsoleRunner.RunAsync(HnswSuites.All, null, resultsPath).ConfigureAwait(false);
            return exitCode;
        }

        #endregion

        #region Private-Methods

        private static string? ParseResultsPath(string[] args)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "--results", StringComparison.Ordinal))
                    return args[i + 1];
            }
            return null;
        }

        #endregion
    }
}
