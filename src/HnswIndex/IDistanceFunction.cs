namespace Hnsw
{
    using System.Collections.Generic;

    /// <summary>
    /// Interface for distance calculation between vectors.
    /// </summary>
    public interface IDistanceFunction
    {
        /// <summary>
        /// Calculates the distance between two vectors.
        /// </summary>
        /// <param name="a">First vector.</param>
        /// <param name="b">Second vector.</param>
        /// <returns>Distance between the vectors.</returns>
        float Distance(List<float> a, List<float> b);

        /// <summary>
        /// Gets the name of the distance function.
        /// </summary>
        string Name { get; }
    }
}
