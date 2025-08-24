namespace Hnsw
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Calculates cosine distance between vectors.
    /// Thread-safe implementation.
    /// </summary>
    public class CosineDistance : IDistanceFunction
    {
        #region Public-Members

        /// <summary>
        /// Gets the name of the distance function.
        /// </summary>
        public string Name => "Cosine";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Calculates the cosine distance between two vectors.
        /// Returns 0 for identical vectors and approaches 2 for opposite vectors.
        /// </summary>
        /// <param name="a">First vector. Cannot be null.</param>
        /// <param name="b">Second vector. Cannot be null.</param>
        /// <returns>The cosine distance between the vectors (1 - cosine similarity).</returns>
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
            float normA = 0;
            float normB = 0;

            for (int i = 0; i < a.Count; i++)
            {
                dotProduct += a[i] * b[i];
                normA += a[i] * a[i];
                normB += b[i] * b[i];
            }

            if (normA == 0 || normB == 0)
            {
                return 1f; // Maximum distance for zero vectors
            }

            float cosineSimilarity = dotProduct / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
            return 1f - cosineSimilarity;
        }

        #endregion
    }
}