namespace Hnsw
{
    using System;
    using System.Collections.Generic;
    using System.Numerics;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Calculates dot product distance between vectors (negated for use as distance metric).
    /// Thread-safe implementation. Uses SIMD-accelerated paths when hardware support is available.
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
        public float Distance(IReadOnlyList<float> a, IReadOnlyList<float> b)
        {
            ArgumentNullException.ThrowIfNull(a, nameof(a));
            ArgumentNullException.ThrowIfNull(b, nameof(b));

            if (a.Count != b.Count)
            {
                throw new ArgumentException($"Vectors must have the same dimension. Vector a has {a.Count} dimensions, vector b has {b.Count} dimensions.", nameof(b));
            }

            if (a is not List<float> listA || b is not List<float> listB)
            {
                float fallbackDot = 0f;
                for (int j = 0; j < a.Count; j++)
                {
                    fallbackDot += a[j] * b[j];
                }
                return -fallbackDot;
            }

            ReadOnlySpan<float> spanA = CollectionsMarshal.AsSpan(listA);
            ReadOnlySpan<float> spanB = CollectionsMarshal.AsSpan(listB);

            float dotProduct = 0f;
            int i = 0;

            if (Vector.IsHardwareAccelerated)
            {
                int width = Vector<float>.Count;
                Vector<float> acc = Vector<float>.Zero;
                int limit = spanA.Length - width;
                for (; i <= limit; i += width)
                {
                    Vector<float> va = new Vector<float>(spanA.Slice(i, width));
                    Vector<float> vb = new Vector<float>(spanB.Slice(i, width));
                    acc += va * vb;
                }
                dotProduct = Vector.Dot(acc, Vector<float>.One);
            }

            for (; i < spanA.Length; i++)
            {
                dotProduct += spanA[i] * spanB[i];
            }

            return -dotProduct;
        }

        #endregion
    }
}
