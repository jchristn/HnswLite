namespace Hnsw
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using System.Threading;

    /// <summary>
    /// Interface for HNSW storage backend.
    /// </summary>
    public interface IHnswStorage
    {
        /// <summary>
        /// Adds a new node to storage.
        /// </summary>
        Task AddNodeAsync(Guid id, List<float> vector, CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds multiple nodes to storage in a batch operation.
        /// More efficient than calling AddNodeAsync multiple times.
        /// </summary>
        /// <param name="nodes">Dictionary mapping node IDs to their vector data. Cannot be null.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task representing the asynchronous operation.</returns>
        Task AddNodesAsync(Dictionary<Guid, List<float>> nodes, CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a node from storage.
        /// </summary>
        Task RemoveNodeAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes multiple nodes from storage in a batch operation.
        /// More efficient than calling RemoveNodeAsync multiple times.
        /// </summary>
        /// <param name="ids">Collection of node IDs to remove. Cannot be null.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task representing the asynchronous operation.</returns>
        Task RemoveNodesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a node by ID.
        /// </summary>
        Task<IHnswNode> GetNodeAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets multiple nodes by their IDs in a batch operation.
        /// More efficient than calling GetNodeAsync multiple times.
        /// </summary>
        /// <param name="ids">Collection of node IDs to retrieve. Cannot be null.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Dictionary mapping node IDs to their corresponding nodes. Only includes nodes that exist.</returns>
        Task<Dictionary<Guid, IHnswNode>> GetNodesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

        /// <summary>
        /// Tries to get a node by ID.
        /// </summary>
        Task<TryGetNodeResult> TryGetNodeAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all node IDs in storage.
        /// </summary>
        Task<IEnumerable<Guid>> GetAllNodeIdsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the number of nodes in storage.
        /// </summary>
        Task<int> GetCountAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets or sets the entry point node ID.
        /// </summary>
        Guid? EntryPoint { get; set; }
    }
}