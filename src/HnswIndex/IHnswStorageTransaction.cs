namespace Hnsw
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Represents an asynchronous storage transaction for graph mutations.
    /// </summary>
    public interface IHnswStorageTransaction : IAsyncDisposable
    {
        /// <summary>
        /// Commits the transaction.
        /// </summary>
        Task CommitAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Rolls the transaction back.
        /// </summary>
        Task RollbackAsync(CancellationToken cancellationToken = default);
    }
}
