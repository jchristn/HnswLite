namespace Hnsw
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Calculates dot product distance between vectors (negated for use as distance metric).
    /// Thread-safe implementation.
    /// </summary>
    public class DotProductDistance : IDistanceFunction
    {
        #region Public-Members

        /// <summary>
        /// Gets the name of the distance function.
        /// </summary>
        public string Name => "DotProduct";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Calculates the negative dot product between two vectors.
        /// Higher dot product results in lower distance (negated).
        /// </summary>
        /// <param name="a">First vector. Cannot be null.</param>
        /// <param name="b">Second vector. Cannot be null.</param>
        /// <returns>The negative dot product between the vectors.</returns>
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

            float dotProduct = 0;
            for (int i = 0; i < a.Count; i++)
            {
                dotProduct += a[i] * b[i];
            }
            
            // Negate so that higher dot product = lower distance
            return -dotProduct;
        }

        #endregion
    }
}