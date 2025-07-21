namespace Hnsw
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Interface for HNSW graph nodes.
    /// </summary>
    public interface IHnswNode
    {
        /// <summary>
        /// Gets the unique identifier of the node.
        /// </summary>
        Guid Id { get; }

        /// <summary>
        /// Gets the vector associated with the node.
        /// </summary>
        List<float> Vector { get; }

        /// <summary>
        /// Gets a copy of the node's neighbors organized by layer.
        /// </summary>
        Dictionary<int, HashSet<Guid>> GetNeighbors();

        /// <summary>
        /// Adds a neighbor connection at the specified layer.
        /// </summary>
        void AddNeighbor(int layer, Guid neighborId);

        /// <summary>
        /// Removes a neighbor connection at the specified layer.
        /// </summary>
        void RemoveNeighbor(int layer, Guid neighborId);
    }
}
