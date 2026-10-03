namespace HnswLite.Test.XUnit
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    using HnswLite.Test.Shared;
    using Touchstone.Core;
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
        public static TheoryData<TestCaseDescriptor> TestCases
        {
            get
            {
                // Touchstone.XunitAdapter 0.2.0's TouchstoneTheoryData yields skipped cases too, so filter here.
                TheoryData<TestCaseDescriptor> data = new TheoryData<TestCaseDescriptor>();
                foreach (TestSuiteDescriptor suite in HnswSuites.All)
                {
                    foreach (TestCaseDescriptor testCase in suite.Cases)
                    {
                        if (!testCase.Skip) data.Add(testCase);
                    }
                }

                return data;
            }
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
            await TestExecutor.ExecuteCaseAsync(testCase, CancellationToken.None);
        }
    }
}
