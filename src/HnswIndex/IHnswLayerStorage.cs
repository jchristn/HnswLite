namespace Hnsw
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Interface for storing and retrieving HNSW node layer assignments.
    /// Implementations can use in-memory storage, database storage, or other persistence mechanisms.
    /// </summary>
    public interface IHnswLayerStorage
    {
        /// <summary>
        /// Gets the layer assignment for a specific node.
        /// Thread-safe operation.
        /// </summary>
        /// <param name="nodeId">Node identifier.</param>
        /// <returns>The layer number for the node, or 0 if not found.</returns>
        int GetNodeLayer(Guid nodeId);

        /// <summary>
        /// Sets the layer assignment for a specific node.
        /// Thread-safe operation.
        /// </summary>
        /// <param name="nodeId">Node identifier. Cannot be Guid.Empty.</param>
        /// <param name="layer">Layer number. Minimum: 0, Maximum: 63.</param>
        /// <exception cref="ArgumentException">Thrown when nodeId is Guid.Empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when layer is outside valid range.</exception>
        void SetNodeLayer(Guid nodeId, int layer);

        /// <summary>
        /// Removes the layer assignment for a specific node.
        /// Thread-safe operation.
        /// No effect if the node doesn't exist.
        /// </summary>
        /// <param name="nodeId">Node identifier.</param>
        void RemoveNodeLayer(Guid nodeId);

        /// <summary>
        /// Gets all node layer assignments.
        /// Thread-safe operation.
        /// Returns a copy to prevent external modification.
        /// </summary>
        /// <returns>Dictionary mapping node IDs to layer numbers.</returns>
        Dictionary<Guid, int> GetAllNodeLayers();

        /// <summary>
        /// Removes all layer assignments.
        /// Thread-safe operation.
        /// </summary>
        void Clear();

        /// <summary>
        /// Gets the number of nodes with layer assignments.
        /// Thread-safe operation.
        /// </summary>
        /// <returns>The count of nodes with layer assignments.</returns>
        int Count { get; }
    }
}