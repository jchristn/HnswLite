namespace Hnsw
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// Calculates cosine distance between vectors.
    /// </summary>
    public class CosineDistance : IDistanceFunction
    {
        /// <summary>
        /// Gets the name of the distance function.
        /// </summary>
        public string Name => "Cosine";

        /// <summary>
        /// Calculates the cosine distance between two vectors.
        /// </summary>
        public float Distance(List<float> a, List<float> b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (a.Count != b.Count) throw new ArgumentException("Vectors must have the same dimension");

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
                return 1f; // Maximum distance

            return 1f - (dotProduct / (float)(Math.Sqrt(normA) * Math.Sqrt(normB)));
        }
    }
}
