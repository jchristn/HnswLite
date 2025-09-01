﻿namespace Hnsw.RamStorage
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// In-memory implementation of HNSW storage with thread-safe operations.
    /// Provides high-performance storage for HNSW nodes in RAM.
    /// </summary>
    public class RamHnswStorage : IHnswStorage, IDisposable
    {
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type.

        // Private members
        private readonly Dictionary<Guid, RamHnswNode> _nodes = new Dictionary<Guid, RamHnswNode>();
        private readonly ReaderWriterLockSlim _storageLock = new ReaderWriterLockSlim();
        private Guid? _entryPoint = null;
        private bool _disposed = false;

        // Public properties
        /// <summary>
        /// Gets or sets the entry point node ID.
        /// Can be null when storage is empty.
        /// When setting, the value must either be null or correspond to an existing node ID.
        /// Thread-safe property.
        /// Default: null.
        /// </summary>
        public Guid? EntryPoint
        {
            get
            {
                ThrowIfDisposed();
                _storageLock.EnterReadLock();
                try
                {
                    return _entryPoint;
                }
                finally
                {
                    _storageLock.ExitReadLock();
                }
            }
            set
            {
                ThrowIfDisposed();
                _storageLock.EnterWriteLock();
                try
                {
                    if (value.HasValue && !_nodes.ContainsKey(value.Value))
                    {
                        throw new ArgumentException($"Entry point node {value.Value} does not exist in storage.", nameof(value));
                    }
                    _entryPoint = value;
                }
                finally
                {
                    _storageLock.ExitWriteLock();
                }
            }
        }

        /// <summary>
        /// Gets whether the storage is empty (contains no nodes).
        /// Thread-safe property.
        /// </summary>
        public bool IsEmpty
        {
            get
            {
                ThrowIfDisposed();
                _storageLock.EnterReadLock();
                try
                {
                    return _nodes.Count == 0;
                }
                finally
                {
                    _storageLock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// Gets whether the storage has been disposed.
        /// </summary>
        public bool IsDisposed => _disposed;

        // Constructors
        /// <summary>
        /// Initializes a new instance of the RamHnswStorage class.
        /// </summary>
        public RamHnswStorage()
        {
            // All fields are already initialized
        }

        // Public methods
        /// <summary>
        /// Gets the number of nodes in storage.
        /// Thread-safe operation.
        /// Minimum: 0, Maximum: int.MaxValue (limited by available memory).
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The number of nodes in storage.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the storage has been disposed.</exception>
        public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                _storageLock.EnterReadLock();
                try
                {
                    return _nodes.Count;
                }
                finally
                {
                    _storageLock.ExitReadLock();
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Adds a new node to storage.
        /// Thread-safe operation.
        /// If this is the first node and EntryPoint is null, it becomes the entry point.
        /// If a node with the same ID already exists, it will be replaced.
        /// </summary>
        /// <param name="id">Node identifier. Cannot be Guid.Empty.</param>
        /// <param name="vector">Vector data. Cannot be null or empty. All values must be finite.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <exception cref="ArgumentException">Thrown when id is Guid.Empty.</exception>
        /// <exception cref="ArgumentNullException">Thrown when vector is null.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the storage has been disposed.</exception>
        public async Task AddNodeAsync(Guid id, List<float> vector, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (id == Guid.Empty)
                throw new ArgumentException("Id cannot be Guid.Empty.", nameof(id));
            if (vector == null)
                throw new ArgumentNullException(nameof(vector));
            if (vector.Count == 0)
                throw new ArgumentException("Vector cannot be empty.", nameof(vector));

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Create node (validation happens in RamHnswNode constructor)
                RamHnswNode node = new RamHnswNode(id, vector);

                _storageLock.EnterWriteLock();
                try
                {
                    // Dispose of existing node if replacing
                    if (_nodes.TryGetValue(id, out RamHnswNode? existingNode))
                    {
                        existingNode.Dispose();
                    }

                    _nodes[id] = node;

                    // Set as entry point if this is the first node
                    if (_entryPoint == null)
                        _entryPoint = id;
                }
                finally
                {
                    _storageLock.ExitWriteLock();
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Adds multiple nodes to storage in a batch operation.
        /// Thread-safe operation.
        /// More efficient than calling AddNodeAsync multiple times.
        /// If this is the first batch and EntryPoint is null, the first node becomes the entry point.
        /// </summary>
        /// <param name="nodes">Dictionary mapping node IDs to their vector data. Cannot be null.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when nodes is null.</exception>
        /// <exception cref="ArgumentException">Thrown when any node ID is Guid.Empty or vector is invalid.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the storage has been disposed.</exception>
        public async Task AddNodesAsync(Dictionary<Guid, List<float>> nodes, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (nodes == null)
                throw new ArgumentNullException(nameof(nodes));

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Validate and create all nodes first (outside of lock)
                Dictionary<Guid, RamHnswNode> newNodes = new Dictionary<Guid, RamHnswNode>(nodes.Count);
                foreach (KeyValuePair<Guid, List<float>> kvp in nodes)
                {
                    if (kvp.Key == Guid.Empty)
                        throw new ArgumentException($"Node ID cannot be Guid.Empty.", nameof(nodes));
                    if (kvp.Value == null)
                        throw new ArgumentNullException(nameof(nodes), $"Vector for node {kvp.Key} is null.");
                    if (kvp.Value.Count == 0)
                        throw new ArgumentException($"Vector for node {kvp.Key} cannot be empty.", nameof(nodes));

                    newNodes[kvp.Key] = new RamHnswNode(kvp.Key, kvp.Value);
                }

                _storageLock.EnterWriteLock();
                try
                {
                    bool wasEmpty = _entryPoint == null;

                    // Add all nodes
                    foreach (KeyValuePair<Guid, RamHnswNode> kvp in newNodes)
                    {
                        // Dispose of existing node if replacing
                        if (_nodes.TryGetValue(kvp.Key, out RamHnswNode? existingNode))
                        {
                            existingNode.Dispose();
                        }

                        _nodes[kvp.Key] = kvp.Value;
                    }

                    // Set entry point if this was the first batch
                    if (wasEmpty && newNodes.Count > 0)
                    {
                        _entryPoint = newNodes.Keys.First();
                    }
                }
                finally
                {
                    _storageLock.ExitWriteLock();
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Removes a node from storage.
        /// Thread-safe operation.
        /// If the removed node was the entry point, a new entry point is automatically selected.
        /// No effect if the node doesn't exist.
        /// </summary>
        /// <param name="id">Node identifier to remove.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <exception cref="ObjectDisposedException">Thrown when the storage has been disposed.</exception>
        public async Task RemoveNodeAsync(Guid id, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                _storageLock.EnterWriteLock();
                try
                {
                    if (_nodes.TryGetValue(id, out RamHnswNode? node))
                    {
                        node.Dispose();
                        _nodes.Remove(id);

                        // Update entry point if necessary
                        if (_entryPoint == id)
                        {
                            _entryPoint = _nodes.Keys.FirstOrDefault();
                            if (_entryPoint.Value == Guid.Empty)
                                _entryPoint = null;
                        }
                    }
                }
                finally
                {
                    _storageLock.ExitWriteLock();
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Removes multiple nodes from storage in a batch operation.
        /// Thread-safe operation.
        /// More efficient than calling RemoveNodeAsync multiple times.
        /// If any removed node was the entry point, a new entry point is automatically selected.
        /// </summary>
        /// <param name="ids">Collection of node IDs to remove. Cannot be null.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when ids is null.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the storage has been disposed.</exception>
        public async Task RemoveNodesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (ids == null)
                throw new ArgumentNullException(nameof(ids));

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                _storageLock.EnterWriteLock();
                try
                {
                    bool entryPointRemoved = false;

                    foreach (Guid id in ids)
                    {
                        if (_nodes.TryGetValue(id, out RamHnswNode? node))
                        {
                            node.Dispose();
                            _nodes.Remove(id);

                            if (_entryPoint == id)
                            {
                                entryPointRemoved = true;
                            }
                        }
                    }

                    // Update entry point if necessary
                    if (entryPointRemoved)
                    {
                        _entryPoint = _nodes.Keys.FirstOrDefault();
                        if (_entryPoint.Value == Guid.Empty)
                            _entryPoint = null;
                    }
                }
                finally
                {
                    _storageLock.ExitWriteLock();
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Gets a node by ID.
        /// Thread-safe operation.
        /// </summary>
        /// <param name="id">Node identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The requested node.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the node doesn't exist.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the storage has been disposed.</exception>
        public async Task<IHnswNode> GetNodeAsync(Guid id, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                _storageLock.EnterReadLock();
                try
                {
                    if (!_nodes.TryGetValue(id, out RamHnswNode? node))
                        throw new KeyNotFoundException($"Node with ID {id} not found in storage.");
                    return node;
                }
                finally
                {
                    _storageLock.ExitReadLock();
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Gets multiple nodes by their IDs in a batch operation.
        /// Thread-safe operation.
        /// More efficient than calling GetNodeAsync multiple times.
        /// </summary>
        /// <param name="ids">Collection of node IDs to retrieve. Cannot be null.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Dictionary mapping node IDs to their corresponding nodes. Only includes nodes that exist.</returns>
        /// <exception cref="ArgumentNullException">Thrown when ids is null.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the storage has been disposed.</exception>
        public async Task<Dictionary<Guid, IHnswNode>> GetNodesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (ids == null)
                throw new ArgumentNullException(nameof(ids));

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                _storageLock.EnterReadLock();
                try
                {
                    Dictionary<Guid, IHnswNode> result = new Dictionary<Guid, IHnswNode>();
                    foreach (Guid id in ids)
                    {
                        if (_nodes.TryGetValue(id, out RamHnswNode? node))
                        {
                            result[id] = node;
                        }
                    }
                    return result;
                }
                finally
                {
                    _storageLock.ExitReadLock();
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Tries to get a node by ID.
        /// Thread-safe operation.
        /// </summary>
        /// <param name="id">Node identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A tuple indicating success and the node if found.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the storage has been disposed.</exception>
        public async Task<TryGetNodeResult> TryGetNodeAsync(Guid id, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                _storageLock.EnterReadLock();
                try
                {
                    if (_nodes.TryGetValue(id, out RamHnswNode? node))
                    {
                        return TryGetNodeResult.Found(node);
                    }
                    return TryGetNodeResult.NotFound();
                }
                finally
                {
                    _storageLock.ExitReadLock();
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Gets all node IDs in storage.
        /// Thread-safe operation.
        /// Returns a new list to prevent external modification.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A collection of all node IDs in storage.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the storage has been disposed.</exception>
        public async Task<IEnumerable<Guid>> GetAllNodeIdsAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                _storageLock.EnterReadLock();
                try
                {
                    return _nodes.Keys.ToList();
                }
                finally
                {
                    _storageLock.ExitReadLock();
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Checks if a node exists in storage.
        /// Thread-safe operation.
        /// </summary>
        /// <param name="id">Node identifier to check.</param>
        /// <returns>true if the node exists; otherwise, false.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the storage has been disposed.</exception>
        public bool ContainsNode(Guid id)
        {
            ThrowIfDisposed();

            _storageLock.EnterReadLock();
            try
            {
                return _nodes.ContainsKey(id);
            }
            finally
            {
                _storageLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Clears all nodes from storage.
        /// Thread-safe operation.
        /// Also clears the entry point.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown when the storage has been disposed.</exception>
        public void Clear()
        {
            ThrowIfDisposed();

            _storageLock.EnterWriteLock();
            try
            {
                // Dispose all nodes
                foreach (RamHnswNode node in _nodes.Values)
                {
                    node.Dispose();
                }

                _nodes.Clear();
                _entryPoint = null;
            }
            finally
            {
                _storageLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Disposes of the storage and all contained nodes.
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
                    _storageLock.EnterWriteLock();
                    try
                    {
                        // Dispose all nodes
                        foreach (RamHnswNode node in _nodes.Values)
                        {
                            node.Dispose();
                        }
                        _nodes.Clear();
                    }
                    finally
                    {
                        _storageLock.ExitWriteLock();
                    }

                    _storageLock?.Dispose();
                }
                _disposed = true;
            }
        }

        // Private methods
        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RamHnswStorage));
        }

#pragma warning restore CS8619 // Nullability of reference types in value doesn't match target type.
    }
}