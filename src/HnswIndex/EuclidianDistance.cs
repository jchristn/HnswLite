namespace Hnsw
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Calculates Euclidean distance between vectors.
    /// Thread-safe implementation.
    /// </summary>
    public class EuclideanDistance : IDistanceFunction
    {
        #region Public-Members

        /// <summary>
        /// Gets the name of the distance function.
        /// </summary>
        public string Name => "Euclidean";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Calculates the Euclidean distance between two vectors.
        /// </summary>
        /// <param name="a">First vector. Cannot be null.</param>
        /// <param name="b">Second vector. Cannot be null.</param>
        /// <returns>The Euclidean distance between the vectors.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a or b is null.</exception>
        /// <exception cref="ArgumentException">Thrown when vectors have different dimensions.</exception>
        public float Distance(List<float> a, List<float> b)
        {
            ArgumentNullException.ThrowIfNull(a, nameof(a));
            ArgumentNullException.ThrowIfNull(b, nameof(b));
            
            if (a.Count != b.Count)
            {
                throw new ArgumentException($"Vectors must have the same dimension. Vector a has {a.Count} dimensions, vector b has {b.Count} dimensions.", nameof(b));
            }

            float sum = 0;
            for (int i = 0; i < a.Count; i++)
            {
                float diff = a[i] - b[i];
                sum += diff * diff;
            }
            
            return MathF.Sqrt(sum);
        }

        #endregion
    }
}