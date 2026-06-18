namespace Hnsw.RamStorage
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Unified in-memory storage provider that combines vector node storage and
    /// layer assignment storage into a single <see cref="IStorageProvider"/>.
    /// Thread-safe. Disposes both backing stores on disposal.
    /// </summary>
    public class RamStorageProvider : IStorageProvider
    {
        #region Public-Members

        /// <summary>
        /// Number of nodes with layer assignments.
        /// </summary>
        public int Count => _LayerStorage.Count;

        #endregion

        #region Private-Members

        private readonly RamHnswStorage _Storage;
        private readonly RamHnswLayerStorage _LayerStorage;
        private bool _Disposed;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new RAM storage provider.
        /// </summary>
        public RamStorageProvider()
        {
            _Storage = new RamHnswStorage();
            _LayerStorage = new RamHnswLayerStorage();
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task AddNodeAsync(Guid id, List<float> vector, CancellationToken cancellationToken = default)
        {
            return _Storage.AddNodeAsync(id, vector, cancellationToken);
        }

        /// <inheritdoc />
        public Task AddNodesAsync(Dictionary<Guid, List<float>> nodes, CancellationToken cancellationToken = default)
        {
            return _Storage.AddNodesAsync(nodes, cancellationToken);
        }

        /// <inheritdoc />
        public Task RemoveNodeAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return _Storage.RemoveNodeAsync(id, cancellationToken);
        }

        /// <inheritdoc />
        public Task RemoveNodesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        {
            return _Storage.RemoveNodesAsync(ids, cancellationToken);
        }

        /// <inheritdoc />
        public Task<IHnswNode> GetNodeAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return _Storage.GetNodeAsync(id, cancellationToken);
        }

        /// <inheritdoc />
        public Task<Dictionary<Guid, IHnswNode>> GetNodesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        {
            return _Storage.GetNodesAsync(ids, cancellationToken);
        }

        /// <inheritdoc />
        public Task<TryGetNodeResult> TryGetNodeAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return _Storage.TryGetNodeAsync(id, cancellationToken);
        }

        /// <inheritdoc />
        public Task<IEnumerable<Guid>> GetAllNodeIdsAsync(CancellationToken cancellationToken = default)
        {
            return _Storage.GetAllNodeIdsAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task<int> GetCountAsync(CancellationToken cancellationToken = default)
        {
            return _Storage.GetCountAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task<Guid?> GetEntryPointAsync(CancellationToken cancellationToken = default)
        {
            return _Storage.GetEntryPointAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task SetEntryPointAsync(Guid? entryPoint, CancellationToken cancellationToken = default)
        {
            return _Storage.SetEntryPointAsync(entryPoint, cancellationToken);
        }

        /// <inheritdoc />
        public Task<int> GetNodeLayerAsync(Guid nodeId, CancellationToken cancellationToken = default)
        {
            return _LayerStorage.GetNodeLayerAsync(nodeId, cancellationToken);
        }

        /// <inheritdoc />
        public Task SetNodeLayerAsync(Guid nodeId, int layer, CancellationToken cancellationToken = default)
        {
            return _LayerStorage.SetNodeLayerAsync(nodeId, layer, cancellationToken);
        }

        /// <inheritdoc />
        public Task RemoveNodeLayerAsync(Guid nodeId, CancellationToken cancellationToken = default)
        {
            return _LayerStorage.RemoveNodeLayerAsync(nodeId, cancellationToken);
        }

        /// <inheritdoc />
        public Task<Dictionary<Guid, int>> GetAllNodeLayersAsync(CancellationToken cancellationToken = default)
        {
            return _LayerStorage.GetAllNodeLayersAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task ClearLayersAsync(CancellationToken cancellationToken = default)
        {
            return _LayerStorage.ClearLayersAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task<int> GetLayerCountAsync(CancellationToken cancellationToken = default)
        {
            return _LayerStorage.GetLayerCountAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task<IHnswStorageTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IHnswStorageTransaction>(new NoOpHnswStorageTransaction());
        }

        /// <inheritdoc />
        public Task FlushAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            DisposeCore();
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }

        #endregion

        #region Private-Methods

        // Synchronous helper behind DisposeAsync.
        private void DisposeCore()
        {
            if (_Disposed) return;
            _Storage.Dispose();
            _LayerStorage.Dispose();
            _Disposed = true;
        }

        #endregion
    }
}
