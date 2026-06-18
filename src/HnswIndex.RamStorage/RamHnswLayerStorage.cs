namespace Hnsw.RamStorage
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// In-memory implementation of HNSW layer storage with thread-safe operations.
    /// Provides high-performance layer assignment storage in RAM.
    /// </summary>
    public class RamHnswLayerStorage : IHnswLayerStorage, IDisposable
    {
        // Private members
        private readonly Dictionary<Guid, int> _nodeLayers = new Dictionary<Guid, int>();
        private readonly ReaderWriterLockSlim _layersLock = new ReaderWriterLockSlim();
        private bool _disposed = false;

        // Public properties
        /// <summary>
        /// Gets the number of nodes with layer assignments.
        /// Thread-safe operation.
        /// </summary>
        public int Count
        {
            get
            {
                ThrowIfDisposed();
                _layersLock.EnterReadLock();
                try
                {
                    return _nodeLayers.Count;
                }
                finally
                {
                    _layersLock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// Gets whether the storage has been disposed.
        /// </summary>
        public bool IsDisposed => _disposed;

        // Constructors
        /// <summary>
        /// Initializes a new instance of the RamHnswLayerStorage class.
        /// </summary>
        public RamHnswLayerStorage()
        {
            // All fields are already initialized
        }

        // Public methods
        // Synchronous helper behind the async public API.
        private int GetNodeLayer(Guid nodeId)
        {
            ThrowIfDisposed();

            _layersLock.EnterReadLock();
            try
            {
                return _nodeLayers.TryGetValue(nodeId, out int layer) ? layer : 0;
            }
            finally
            {
                _layersLock.ExitReadLock();
            }
        }

        /// <inheritdoc />
        public Task<int> GetNodeLayerAsync(Guid nodeId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(GetNodeLayer(nodeId));
        }

        // Synchronous helper behind the async public API.
        private void SetNodeLayer(Guid nodeId, int layer)
        {
            ThrowIfDisposed();

            if (nodeId == Guid.Empty)
                throw new ArgumentException("NodeId cannot be Guid.Empty.", nameof(nodeId));
            if (layer < 0)
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot be negative.");
            if (layer > 63)
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot exceed 63.");

            _layersLock.EnterWriteLock();
            try
            {
                _nodeLayers[nodeId] = layer;
            }
            finally
            {
                _layersLock.ExitWriteLock();
            }
        }

        /// <inheritdoc />
        public Task SetNodeLayerAsync(Guid nodeId, int layer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetNodeLayer(nodeId, layer);
            return Task.CompletedTask;
        }

        // Synchronous helper behind the async public API.
        private void RemoveNodeLayer(Guid nodeId)
        {
            ThrowIfDisposed();

            _layersLock.EnterWriteLock();
            try
            {
                _nodeLayers.Remove(nodeId);
            }
            finally
            {
                _layersLock.ExitWriteLock();
            }
        }

        /// <inheritdoc />
        public Task RemoveNodeLayerAsync(Guid nodeId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RemoveNodeLayer(nodeId);
            return Task.CompletedTask;
        }

        // Synchronous helper behind the async public API.
        private Dictionary<Guid, int> GetAllNodeLayers()
        {
            ThrowIfDisposed();

            _layersLock.EnterReadLock();
            try
            {
                return new Dictionary<Guid, int>(_nodeLayers);
            }
            finally
            {
                _layersLock.ExitReadLock();
            }
        }

        /// <inheritdoc />
        public Task<Dictionary<Guid, int>> GetAllNodeLayersAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(GetAllNodeLayers());
        }

        // Synchronous helper behind the async public API.
        private void Clear()
        {
            ThrowIfDisposed();

            _layersLock.EnterWriteLock();
            try
            {
                _nodeLayers.Clear();
            }
            finally
            {
                _layersLock.ExitWriteLock();
            }
        }

        /// <inheritdoc />
        public Task ClearLayersAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Clear();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<int> GetLayerCountAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Count);
        }

        /// <summary>
        /// Checks if a layer assignment exists for the specified node.
        /// Thread-safe operation.
        /// </summary>
        /// <param name="nodeId">Node identifier to check.</param>
        /// <returns>true if a layer assignment exists; otherwise, false.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the storage has been disposed.</exception>
        public bool ContainsNode(Guid nodeId)
        {
            ThrowIfDisposed();

            _layersLock.EnterReadLock();
            try
            {
                return _nodeLayers.ContainsKey(nodeId);
            }
            finally
            {
                _layersLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Gets all node IDs that have layer assignments.
        /// Thread-safe operation.
        /// Returns a copy to prevent external modification.
        /// </summary>
        /// <returns>Collection of node IDs with layer assignments.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the storage has been disposed.</exception>
        public IEnumerable<Guid> GetAllNodeIds()
        {
            ThrowIfDisposed();

            _layersLock.EnterReadLock();
            try
            {
                return _nodeLayers.Keys.ToList();
            }
            finally
            {
                _layersLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Disposes of the storage resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        // Protected methods
        /// <summary>
        /// Disposes of the storage resources.
        /// </summary>
        /// <param name="disposing">true if disposing managed resources; otherwise, false.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _layersLock.EnterWriteLock();
                    try
                    {
                        _nodeLayers.Clear();
                    }
                    finally
                    {
                        _layersLock.ExitWriteLock();
                    }

                    _layersLock?.Dispose();
                }
                _disposed = true;
            }
        }

        // Private methods
        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RamHnswLayerStorage));
        }
    }
}
