namespace HnswLite.Test.XUnit
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    using HnswLite.Test.Shared;
    using Touchstone.Core;
    using Touchstone.XunitAdapter;
    using Xunit;

    /// <summary>
    /// xUnit host for HnswLite Touchstone tests. Each shared TestCaseDescriptor
    /// is surfaced as its own xUnit theory row, so individual failures are
    /// reported one-per-test in IDE and CI output.
    /// </summary>
    public sealed class HnswTheoryTests
    {
        /// <summary>
        /// Provides every non-skipped TestCaseDescriptor from the shared suites as xUnit theory data.
        /// </summary>
        public static TouchstoneTheoryData TestCases
        {
            get { return new TouchstoneTheoryData(HnswSuites.All); }
        }

        /// <summary>
        /// Executes a single Touchstone test case under xUnit.
        /// </summary>
        /// <param name="testCase">The test case to execute.</param>
        /// <returns>Task representing the test run.</returns>
        [Theory]
        [MemberData(nameof(TestCases))]
        public async Task RunTouchstoneCase(TestCaseDescriptor testCase)
        {
            await testCase.ExecuteAsync(CancellationToken.None);
        }
    }
}
