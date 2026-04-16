namespace HnswLite.Test.Shared
{
    using System;

    /// <summary>
    /// Minimal assertion helpers used by shared Touchstone test cases.
    /// Tests throw on failure; these helpers wrap the common comparisons.
    /// </summary>
    public static class TestAssert
    {
        #region Public-Methods

        /// <summary>
        /// Throws if the condition is false.
        /// </summary>
        /// <param name="condition">Condition that must be true.</param>
        /// <param name="message">Failure message when condition is false.</param>
        /// <exception cref="InvalidOperationException">Thrown when condition is false.</exception>
        public static void True(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
        }

        /// <summary>
        /// Throws if the condition is true.
        /// </summary>
        /// <param name="condition">Condition that must be false.</param>
        /// <param name="message">Failure message when condition is true.</param>
        /// <exception cref="InvalidOperationException">Thrown when condition is true.</exception>
        public static void False(bool condition, string message)
        {
            if (condition) throw new InvalidOperationException("Assertion failed: " + message);
        }

        /// <summary>
        /// Throws if expected and actual are not equal.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="expected">Expected value.</param>
        /// <param name="actual">Actual value.</param>
        /// <param name="message">Failure message.</param>
        /// <exception cref="InvalidOperationException">Thrown when values differ.</exception>
        public static void Equal<T>(T expected, T actual, string message)
        {
            if (!object.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"Assertion failed: {message}. Expected={expected}, Actual={actual}");
            }
        }

        /// <summary>
        /// Throws if the two floats differ by more than tolerance.
        /// </summary>
        /// <param name="expected">Expected value.</param>
        /// <param name="actual">Actual value.</param>
        /// <param name="tolerance">Maximum allowed absolute difference.</param>
        /// <param name="message">Failure message.</param>
        /// <exception cref="InvalidOperationException">Thrown when values differ by more than tolerance.</exception>
        public static void NearEqual(float expected, float actual, float tolerance, string message)
        {
            if (Math.Abs(expected - actual) > tolerance)
            {
                throw new InvalidOperationException(
                    $"Assertion failed: {message}. Expected={expected}, Actual={actual}, Tolerance={tolerance}");
            }
        }

        /// <summary>
        /// Executes the action and verifies it throws an exception assignable to TException.
        /// </summary>
        /// <typeparam name="TException">Expected exception type.</typeparam>
        /// <param name="action">Action expected to throw.</param>
        /// <param name="message">Failure message if no exception or wrong type.</param>
        /// <exception cref="InvalidOperationException">Thrown when the expected exception does not occur.</exception>
        public static void Throws<TException>(Action action, string message) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Assertion failed: {message}. Expected {typeof(TException).Name}, got {ex.GetType().Name}: {ex.Message}");
            }

            throw new InvalidOperationException(
                $"Assertion failed: {message}. Expected {typeof(TException).Name} but no exception was thrown.");
        }

        /// <summary>
        /// Executes the async function and verifies it throws an exception assignable to TException.
        /// </summary>
        /// <typeparam name="TException">Expected exception type.</typeparam>
        /// <param name="func">Async function expected to throw.</param>
        /// <param name="message">Failure message if no exception or wrong type.</param>
        /// <returns>Task that completes once assertion is checked.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the expected exception does not occur.</exception>
        public static async System.Threading.Tasks.Task ThrowsAsync<TException>(Func<System.Threading.Tasks.Task> func, string message) where TException : Exception
        {
            try
            {
                await func().ConfigureAwait(false);
            }
            catch (TException)
            {
                return;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Assertion failed: {message}. Expected {typeof(TException).Name}, got {ex.GetType().Name}: {ex.Message}");
            }

            throw new InvalidOperationException(
                $"Assertion failed: {message}. Expected {typeof(TException).Name} but no exception was thrown.");
        }

        #endregion
    }
}
