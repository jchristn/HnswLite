namespace Hnsw
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Interface for storing and retrieving HNSW node layer assignments.
    /// Implementations can use in-memory storage, database storage, or other persistence mechanisms.
    /// </summary>
    public interface IHnswLayerStorage
    {
        /// <summary>
        /// Gets the layer assignment for a specific node.
        /// </summary>
        /// <param name="nodeId">Node identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The layer number for the node, or 0 if not found.</returns>
        Task<int> GetNodeLayerAsync(Guid nodeId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets the layer assignment for a specific node.
        /// </summary>
        /// <param name="nodeId">Node identifier. Cannot be Guid.Empty.</param>
        /// <param name="layer">Layer number. Minimum: 0, Maximum: 63.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task SetNodeLayerAsync(Guid nodeId, int layer, CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes the layer assignment for a specific node.
        /// </summary>
        /// <param name="nodeId">Node identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task RemoveNodeLayerAsync(Guid nodeId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all node layer assignments.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Dictionary mapping node IDs to layer numbers.</returns>
        Task<Dictionary<Guid, int>> GetAllNodeLayersAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes all layer assignments.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task ClearLayersAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the number of nodes with layer assignments.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The count of nodes with layer assignments.</returns>
        Task<int> GetLayerCountAsync(CancellationToken cancellationToken = default);
    }
}
