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
        /// Gets or sets the entry point node ID.
        /// </summary>
        public Guid? EntryPoint
        {
            get { return _Storage.EntryPoint; }
            set { _Storage.EntryPoint = value; }
        }

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
        public int GetNodeLayer(Guid nodeId)
        {
            return _LayerStorage.GetNodeLayer(nodeId);
        }

        /// <inheritdoc />
        public void SetNodeLayer(Guid nodeId, int layer)
        {
            _LayerStorage.SetNodeLayer(nodeId, layer);
        }

        /// <inheritdoc />
        public void RemoveNodeLayer(Guid nodeId)
        {
            _LayerStorage.RemoveNodeLayer(nodeId);
        }

        /// <inheritdoc />
        public Dictionary<Guid, int> GetAllNodeLayers()
        {
            return _LayerStorage.GetAllNodeLayers();
        }

        /// <inheritdoc />
        public void Clear()
        {
            _Storage.Clear();
            _LayerStorage.Clear();
        }

        /// <summary>
        /// Disposes both the node storage and layer storage.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Disposes managed resources.
        /// </summary>
        /// <param name="disposing">True when called from Dispose().</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                _Storage.Dispose();
                _LayerStorage.Dispose();
            }
            _Disposed = true;
        }

        #endregion
    }
}
