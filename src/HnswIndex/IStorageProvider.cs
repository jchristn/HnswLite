namespace Hnsw
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Unified storage provider that combines vector node storage, layer assignment
    /// storage, and lifecycle management into a single interface.
    /// Implementations must be thread-safe.
    /// </summary>
    public interface IStorageProvider : IHnswStorage, IHnswLayerStorage, IAsyncDisposable
    {
        /// <summary>
        /// Begins a transaction for a graph mutation operation.
        /// Providers that do not need transactions may return a no-op transaction.
        /// </summary>
        Task<IHnswStorageTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Flushes any buffered graph or metadata changes.
        /// </summary>
        Task FlushAsync(CancellationToken cancellationToken = default);
    }
}
