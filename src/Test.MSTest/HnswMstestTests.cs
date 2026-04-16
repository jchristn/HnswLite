namespace HnswLite.Test.MSTest
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    using HnswLite.Test.Shared;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Touchstone.Core;
    using Touchstone.MstestAdapter;

    /// <summary>
    /// MSTest host for HnswLite Touchstone tests. Each shared TestCaseDescriptor
    /// is surfaced as a distinct MSTest data row.
    /// </summary>
    [TestClass]
    public sealed class HnswMstestTests
    {
        /// <summary>
        /// Returns every non-skipped TestCaseDescriptor as MSTest DynamicData rows.
        /// </summary>
        /// <returns>MSTest data rows — one TestCaseDescriptor per row.</returns>
        public static IEnumerable<object[]> TestCases()
        {
            return TouchstoneDynamicData.FromSuites(HnswSuites.All);
        }

        /// <summary>
        /// Executes a single Touchstone test case under MSTest.
        /// </summary>
        /// <param name="testCase">The test case to execute.</param>
        /// <returns>Task representing the test run.</returns>
        [TestMethod]
        [DynamicData(nameof(TestCases))]
        public async Task RunTouchstoneCase(TestCaseDescriptor testCase)
        {
            await testCase.ExecuteAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
