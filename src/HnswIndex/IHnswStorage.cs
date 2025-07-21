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
        /// Removes a node from storage.
        /// </summary>
        Task RemoveNodeAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a node by ID.
        /// </summary>
        Task<IHnswNode> GetNodeAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Tries to get a node by ID.
        /// </summary>
        Task<(bool success, IHnswNode node)> TryGetNodeAsync(Guid id, CancellationToken cancellationToken = default);

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
