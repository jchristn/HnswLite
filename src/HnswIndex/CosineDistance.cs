namespace Hnsw
{
    using System;
    using System.Collections.Generic;
    using System.Numerics;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Calculates cosine distance between vectors.
    /// Thread-safe implementation. Uses SIMD-accelerated paths when hardware support is available.
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

            ReadOnlySpan<float> spanA = CollectionsMarshal.AsSpan(a);
            ReadOnlySpan<float> spanB = CollectionsMarshal.AsSpan(b);

            float dotProduct = 0f;
            float normA = 0f;
            float normB = 0f;
            int i = 0;

            if (Vector.IsHardwareAccelerated)
            {
                int width = Vector<float>.Count;
                Vector<float> dotAcc = Vector<float>.Zero;
                Vector<float> normAAcc = Vector<float>.Zero;
                Vector<float> normBAcc = Vector<float>.Zero;
                int limit = spanA.Length - width;
                for (; i <= limit; i += width)
                {
                    Vector<float> va = new Vector<float>(spanA.Slice(i, width));
                    Vector<float> vb = new Vector<float>(spanB.Slice(i, width));
                    dotAcc += va * vb;
                    normAAcc += va * va;
                    normBAcc += vb * vb;
                }
                dotProduct = Vector.Dot(dotAcc, Vector<float>.One);
                normA = Vector.Dot(normAAcc, Vector<float>.One);
                normB = Vector.Dot(normBAcc, Vector<float>.One);
            }

            for (; i < spanA.Length; i++)
            {
                float av = spanA[i];
                float bv = spanB[i];
                dotProduct += av * bv;
                normA += av * av;
                normB += bv * bv;
            }

            if (normA == 0f || normB == 0f)
            {
                return 1f;
            }

            float cosineSimilarity = dotProduct / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
            return 1f - cosineSimilarity;
        }

        #endregion
    }
}
