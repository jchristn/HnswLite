namespace Hnsw
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

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
        IReadOnlyList<float> Vector { get; }

        /// <summary>
        /// Optional human-readable name for this vector.
        /// </summary>
        string? Name { get; }

        /// <summary>
        /// Optional classification labels.
        /// </summary>
        IReadOnlyList<string>? Labels { get; }

        /// <summary>
        /// Optional arbitrary key/value tags.
        /// </summary>
        IReadOnlyDictionary<string, object>? Tags { get; }

        /// <summary>
        /// Gets a copy of the node's neighbors organized by layer.
        /// </summary>
        Task<Dictionary<int, HashSet<Guid>>> GetNeighborsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds a neighbor connection at the specified layer.
        /// </summary>
        Task AddNeighborAsync(int layer, Guid neighborGuid, CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a neighbor connection at the specified layer.
        /// </summary>
        Task RemoveNeighborAsync(int layer, Guid neighborGuid, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets the optional metadata for this vector.
        /// </summary>
        Task SetMetadataAsync(
            string? name,
            List<string>? labels,
            Dictionary<string, object>? tags,
            CancellationToken cancellationToken = default);
    }
}
