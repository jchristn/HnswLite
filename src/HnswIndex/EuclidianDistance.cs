namespace Hnsw
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// Calculates Euclidean distance between vectors.
    /// </summary>
    public class EuclideanDistance : IDistanceFunction
    {
        /// <summary>
        /// Gets the name of the distance function.
        /// </summary>
        public string Name => "Euclidean";

        /// <summary>
        /// Calculates the Euclidean distance between two vectors.
        /// </summary>
        public float Distance(List<float> a, List<float> b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (a.Count != b.Count) throw new ArgumentException("Vectors must have the same dimension");

            float sum = 0;
            for (int i = 0; i < a.Count; i++)
            {
                float diff = a[i] - b[i];
                sum += diff * diff;
            }
            return (float)Math.Sqrt(sum);
        }
    }
}
