namespace HnswIndex.Server.Classes
{
    using System;

    /// <summary>
    /// Thrown when a supplied vector's length does not match the dimension of the target index.
    /// Derives from <see cref="ArgumentException"/> so existing callers that catch argument errors still handle it.
    /// The REST API maps it to 400 InvalidDimension.
    /// </summary>
    public class VectorDimensionMismatchException : ArgumentException
    {
        #region Public-Members

        /// <summary>
        /// Length of the supplied vector.
        /// </summary>
        public int ActualDimension { get; }

        /// <summary>
        /// Dimension of the index.
        /// </summary>
        public int ExpectedDimension { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the VectorDimensionMismatchException class.
        /// </summary>
        /// <param name="message">Error message.</param>
        /// <param name="actualDimension">Length of the supplied vector.</param>
        /// <param name="expectedDimension">Dimension of the index.</param>
        public VectorDimensionMismatchException(string message, int actualDimension, int expectedDimension)
            : base(message)
        {
            ActualDimension = actualDimension;
            ExpectedDimension = expectedDimension;
        }

        #endregion
    }
}
