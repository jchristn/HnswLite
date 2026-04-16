namespace HnswLite.Test.NUnit
{
    using System.Collections;
    using System.Threading;
    using System.Threading.Tasks;

    using HnswLite.Test.Shared;
    using global::NUnit.Framework;
    using Touchstone.Core;
    using Touchstone.NunitAdapter;

    /// <summary>
    /// NUnit host for HnswLite Touchstone tests. Each shared TestCaseDescriptor
    /// is enumerated as a distinct NUnit test case.
    /// </summary>
    [TestFixture]
    public sealed class HnswNunitTests
    {
        /// <summary>
        /// Enumerator over all non-skipped Touchstone test cases.
        /// </summary>
        /// <returns>Enumerator of TestCaseDescriptor values.</returns>
        public static IEnumerable TestCases()
        {
            return new TouchstoneTestCaseSource(HnswSuites.All);
        }

        /// <summary>
        /// Executes a single Touchstone test case under NUnit.
        /// </summary>
        /// <param name="testCase">The test case to execute.</param>
        /// <returns>Task representing the test run.</returns>
        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task RunTouchstoneCase(TestCaseDescriptor testCase)
        {
            await testCase.ExecuteAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
