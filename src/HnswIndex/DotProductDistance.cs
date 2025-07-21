namespace Hnsw
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// Calculates dot product distance between vectors (negated for use as distance metric).
    /// </summary>
    public class DotProductDistance : IDistanceFunction
    {
        /// <summary>
        /// Gets the name of the distance function.
        /// </summary>
        public string Name => "DotProduct";

        /// <summary>
        /// Calculates the negative dot product between two vectors.
        /// </summary>
        public float Distance(List<float> a, List<float> b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (a.Count != b.Count) throw new ArgumentException("Vectors must have the same dimension");

            float dotProduct = 0;
            for (int i = 0; i < a.Count; i++)
            {
                dotProduct += a[i] * b[i];
            }
            // Negate so that higher dot product = lower distance
            return -dotProduct;
        }
    }
}
