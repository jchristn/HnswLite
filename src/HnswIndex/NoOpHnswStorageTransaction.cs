namespace Hnsw
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// No-op transaction used by providers that do not require transactional resources.
    /// </summary>
    public sealed class NoOpHnswStorageTransaction : IHnswStorageTransaction
    {
        /// <inheritdoc />
        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
