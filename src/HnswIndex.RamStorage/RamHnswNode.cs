namespace Hnsw.RamStorage
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;

    /// <summary>
    /// In-memory implementation of HNSW node with thread-safe operations.
    /// </summary>
    public class RamHnswNode : IHnswNode
    {
        // Private members
        private readonly Guid _id;
        private readonly List<float> _vector;
        private readonly Dictionary<int, HashSet<Guid>> _neighbors = new Dictionary<int, HashSet<Guid>>();
        private readonly ReaderWriterLockSlim _nodeLock = new ReaderWriterLockSlim();
        private bool _disposed = false;

        // Public properties
        /// <summary>
        /// Gets the unique identifier of the node.
        /// Cannot be Guid.Empty.
        /// </summary>
        public Guid Id => _id;

        /// <summary>
        /// Gets the vector associated with the node.
        /// Never null. Vector dimension typically ranges from 1 to 4096.
        /// All values must be finite (not NaN or Infinity).
        /// </summary>
        public List<float> Vector => _vector;

        /// <summary>
        /// Gets whether this node has been disposed.
        /// </summary>
        public bool IsDisposed => _disposed;

        // Constructors
        /// <summary>
        /// Initializes a new instance of the RamHnswNode class.
        /// </summary>
        /// <param name="id">Node identifier. Cannot be Guid.Empty.</param>
        /// <param name="vector">Vector data. Cannot be null or empty. All values must be finite.</param>
        /// <exception cref="ArgumentException">Thrown when id is Guid.Empty or vector contains invalid values.</exception>
        /// <exception cref="ArgumentNullException">Thrown when vector is null.</exception>
        public RamHnswNode(Guid id, List<float> vector)
        {
            if (id == Guid.Empty)
                throw new ArgumentException("Id cannot be Guid.Empty.", nameof(id));
            if (vector == null)
                throw new ArgumentNullException(nameof(vector));
            if (vector.Count == 0)
                throw new ArgumentException("Vector cannot be empty.", nameof(vector));

            // Validate vector values
            for (int i = 0; i < vector.Count; i++)
            {
                if (float.IsNaN(vector[i]) || float.IsInfinity(vector[i]))
                    throw new ArgumentException($"Vector contains invalid value at index {i}. All values must be finite.", nameof(vector));
            }

            _id = id;
            _vector = new List<float>(vector); // Create defensive copy
        }

        // Public methods
        /// <summary>
        /// Gets a copy of the node's neighbors organized by layer.
        /// Thread-safe operation.
        /// Returns a dictionary where keys are layer numbers (0 to MaxLayers-1) and values are sets of neighbor IDs.
        /// Never returns null. Returns an empty dictionary if no neighbors exist.
        /// </summary>
        /// <returns>A copy of the neighbors dictionary.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the node has been disposed.</exception>
        public Dictionary<int, HashSet<Guid>> GetNeighbors()
        {
            ThrowIfDisposed();

            _nodeLock.EnterReadLock();
            try
            {
                var result = new Dictionary<int, HashSet<Guid>>();
                foreach (var kvp in _neighbors)
                {
                    result[kvp.Key] = new HashSet<Guid>(kvp.Value);
                }
                return result;
            }
            finally
            {
                _nodeLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Adds a neighbor connection at the specified layer.
        /// Thread-safe operation. Idempotent - adding the same neighbor multiple times has no additional effect.
        /// </summary>
        /// <param name="layer">The layer number. Minimum: 0, Maximum: 63.</param>
        /// <param name="NeighborGUID">The ID of the neighbor to add. Cannot be Guid.Empty or equal to this node's ID.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when layer is negative or exceeds maximum.</exception>
        /// <exception cref="ArgumentException">Thrown when NeighborGUID is invalid.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the node has been disposed.</exception>
        public void AddNeighbor(int layer, Guid NeighborGUID)
        {
            ThrowIfDisposed();

            if (layer < 0)
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot be negative.");
            if (layer > 63)
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot exceed 63.");
            if (NeighborGUID == Guid.Empty)
                throw new ArgumentException("NeighborId cannot be Guid.Empty.", nameof(NeighborGUID));
            if (NeighborGUID == _id)
                throw new ArgumentException("Node cannot be its own neighbor.", nameof(NeighborGUID));

            _nodeLock.EnterWriteLock();
            try
            {
                if (!_neighbors.ContainsKey(layer))
                    _neighbors[layer] = new HashSet<Guid>();
                _neighbors[layer].Add(NeighborGUID);
            }
            finally
            {
                _nodeLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Removes a neighbor connection at the specified layer.
        /// Thread-safe operation. No effect if the neighbor doesn't exist.
        /// Removes the layer entry if it becomes empty after neighbor removal.
        /// </summary>
        /// <param name="layer">The layer number. Minimum: 0, Maximum: 63.</param>
        /// <param name="NeighborGUID">The ID of the neighbor to remove.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when layer is negative or exceeds maximum.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the node has been disposed.</exception>
        public void RemoveNeighbor(int layer, Guid NeighborGUID)
        {
            ThrowIfDisposed();

            if (layer < 0)
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot be negative.");
            if (layer > 63)
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot exceed 63.");

            _nodeLock.EnterWriteLock();
            try
            {
                if (_neighbors.ContainsKey(layer))
                {
                    _neighbors[layer].Remove(NeighborGUID);
                    if (_neighbors[layer].Count == 0)
                        _neighbors.Remove(layer);
                }
            }
            finally
            {
                _nodeLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Gets the number of neighbors at a specific layer.
        /// Thread-safe operation.
        /// </summary>
        /// <param name="layer">The layer number. Minimum: 0, Maximum: 63.</param>
        /// <returns>The number of neighbors at the specified layer, or 0 if the layer has no neighbors.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when layer is negative or exceeds maximum.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the node has been disposed.</exception>
        public int GetNeighborCount(int layer)
        {
            ThrowIfDisposed();

            if (layer < 0)
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot be negative.");
            if (layer > 63)
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot exceed 63.");

            _nodeLock.EnterReadLock();
            try
            {
                return _neighbors.TryGetValue(layer, out var layerNeighbors) ? layerNeighbors.Count : 0;
            }
            finally
            {
                _nodeLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Gets the total number of neighbors across all layers.
        /// Thread-safe operation.
        /// </summary>
        /// <returns>The total number of neighbors.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the node has been disposed.</exception>
        public int GetTotalNeighborCount()
        {
            ThrowIfDisposed();

            _nodeLock.EnterReadLock();
            try
            {
                return _neighbors.Values.Sum(set => set.Count);
            }
            finally
            {
                _nodeLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Checks if a specific neighbor exists at the given layer.
        /// Thread-safe operation.
        /// </summary>
        /// <param name="layer">The layer number. Minimum: 0, Maximum: 63.</param>
        /// <param name="NeighborGUID">The neighbor ID to check.</param>
        /// <returns>true if the neighbor exists at the specified layer; otherwise, false.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when layer is negative or exceeds maximum.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the node has been disposed.</exception>
        public bool HasNeighbor(int layer, Guid NeighborGUID)
        {
            ThrowIfDisposed();

            if (layer < 0)
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot be negative.");
            if (layer > 63)
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot exceed 63.");

            _nodeLock.EnterReadLock();
            try
            {
                return _neighbors.TryGetValue(layer, out var layerNeighbors) && layerNeighbors.Contains(NeighborGUID);
            }
            finally
            {
                _nodeLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Disposes of the node's resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        // Protected methods
        /// <summary>
        /// Disposes of the node's resources.
        /// </summary>
        /// <param name="disposing">true if disposing managed resources; otherwise, false.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _nodeLock?.Dispose();
                }
                _disposed = true;
            }
        }

        // Private methods
        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RamHnswNode));
        }
    }
}