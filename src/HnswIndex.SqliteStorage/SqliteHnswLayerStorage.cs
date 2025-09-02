namespace Hnsw.SqliteStorage
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using Hnsw;
    using Microsoft.Data.Sqlite;

    /// <summary>
    /// SQLite-based implementation of HNSW layer storage with thread-safe operations.
    /// Provides persistent layer assignment storage in a SQLite database.
    /// </summary>
    public class SqliteHnswLayerStorage : IHnswLayerStorage, IDisposable
    {
        // Private members
        private readonly SqliteConnection _connection;
        private readonly Dictionary<Guid, int> _layerCache = new Dictionary<Guid, int>();
        private readonly ReaderWriterLockSlim _layersLock = new ReaderWriterLockSlim();
        private readonly string _tableName;
        private bool _disposed = false;
        private bool _cacheLoaded = false;

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
                EnsureCacheLoaded();

                _layersLock.EnterReadLock();
                try
                {
                    return _layerCache.Count;
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

        /// <summary>
        /// Gets the database table name used for layer storage.
        /// </summary>
        public string TableName => _tableName;

        // Constructors
        /// <summary>
        /// Initializes a new instance of the SqliteHnswLayerStorage class.
        /// </summary>
        /// <param name="connection">SQLite database connection. Cannot be null.</param>
        /// <param name="tableName">Table name for storing layer assignments. Cannot be null or empty.</param>
        /// <exception cref="ArgumentNullException">Thrown when connection or tableName is null.</exception>
        /// <exception cref="ArgumentException">Thrown when tableName is empty or whitespace.</exception>
        public SqliteHnswLayerStorage(SqliteConnection connection, string tableName = "hnsw_node_layers")
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            if (string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentException("Table name cannot be null or empty.", nameof(tableName));

            _connection = connection;
            _tableName = tableName;

            InitializeTable();
        }

        // Public methods
        /// <summary>
        /// Gets the layer assignment for a specific node.
        /// Thread-safe operation.
        /// Uses caching for improved performance.
        /// </summary>
        /// <param name="nodeId">Node identifier.</param>
        /// <returns>The layer number for the node, or 0 if not found.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the storage has been disposed.</exception>
        public int GetNodeLayer(Guid nodeId)
        {
            ThrowIfDisposed();
            EnsureCacheLoaded();

            _layersLock.EnterReadLock();
            try
            {
                return _layerCache.TryGetValue(nodeId, out int layer) ? layer : 0;
            }
            finally
            {
                _layersLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Sets the layer assignment for a specific node.
        /// Thread-safe operation.
        /// Immediately persists to database and updates cache.
        /// </summary>
        /// <param name="nodeId">Node identifier. Cannot be Guid.Empty.</param>
        /// <param name="layer">Layer number. Minimum: 0, Maximum: 63.</param>
        /// <exception cref="ArgumentException">Thrown when nodeId is Guid.Empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when layer is outside valid range.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the storage has been disposed.</exception>
        public void SetNodeLayer(Guid nodeId, int layer)
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
                // Update database
                SqliteCommand command = _connection.CreateCommand();
                command.CommandText = $@"
                    INSERT OR REPLACE INTO {_tableName} (node_id, layer, updated_at) 
                    VALUES (@nodeId, @layer, CURRENT_TIMESTAMP)";
                command.Parameters.AddWithValue("@nodeId", nodeId.ToString());
                command.Parameters.AddWithValue("@layer", layer);
                command.ExecuteNonQuery();

                // Update cache
                EnsureCacheLoadedUnsafe();
                _layerCache[nodeId] = layer;
            }
            finally
            {
                _layersLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Removes the layer assignment for a specific node.
        /// Thread-safe operation.
        /// Immediately removes from database and cache.
        /// No effect if the node doesn't exist.
        /// </summary>
        /// <param name="nodeId">Node identifier.</param>
        /// <exception cref="ObjectDisposedException">Thrown when the storage has been disposed.</exception>
        public void RemoveNodeLayer(Guid nodeId)
        {
            ThrowIfDisposed();

            _layersLock.EnterWriteLock();
            try
            {
                // Remove from database
                SqliteCommand command = _connection.CreateCommand();
                command.CommandText = $"DELETE FROM {_tableName} WHERE node_id = @nodeId";
                command.Parameters.AddWithValue("@nodeId", nodeId.ToString());
                command.ExecuteNonQuery();

                // Remove from cache
                EnsureCacheLoadedUnsafe();
                _layerCache.Remove(nodeId);
            }
            finally
            {
                _layersLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Gets all node layer assignments.
        /// Thread-safe operation.
        /// Returns a copy to prevent external modification.
        /// </summary>
        /// <returns>Dictionary mapping node IDs to layer numbers.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the storage has been disposed.</exception>
        public Dictionary<Guid, int> GetAllNodeLayers()
        {
            ThrowIfDisposed();
            EnsureCacheLoaded();

            _layersLock.EnterReadLock();
            try
            {
                return new Dictionary<Guid, int>(_layerCache);
            }
            finally
            {
                _layersLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Removes all layer assignments.
        /// Thread-safe operation.
        /// Clears both database and cache.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown when the storage has been disposed.</exception>
        public void Clear()
        {
            ThrowIfDisposed();

            _layersLock.EnterWriteLock();
            try
            {
                // Clear database
                SqliteCommand command = _connection.CreateCommand();
                command.CommandText = $"DELETE FROM {_tableName}";
                command.ExecuteNonQuery();

                // Clear cache
                _layerCache.Clear();
                _cacheLoaded = true; // Cache is now in sync (empty)
            }
            finally
            {
                _layersLock.ExitWriteLock();
            }
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
            EnsureCacheLoaded();

            _layersLock.EnterReadLock();
            try
            {
                return _layerCache.ContainsKey(nodeId);
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
            EnsureCacheLoaded();

            _layersLock.EnterReadLock();
            try
            {
                return _layerCache.Keys.ToList();
            }
            finally
            {
                _layersLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Forces a reload of the cache from the database.
        /// Thread-safe operation.
        /// Useful for synchronizing with external database changes.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown when the storage has been disposed.</exception>
        public void RefreshCache()
        {
            ThrowIfDisposed();

            _layersLock.EnterWriteLock();
            try
            {
                _cacheLoaded = false;
                EnsureCacheLoadedUnsafe();
            }
            finally
            {
                _layersLock.ExitWriteLock();
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
                        _layerCache.Clear();
                    }
                    finally
                    {
                        _layersLock.ExitWriteLock();
                    }

                    _layersLock?.Dispose();
                    // Note: We don't dispose the connection as it's owned by the caller
                }
                _disposed = true;
            }
        }

        // Private methods
        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SqliteHnswLayerStorage));
        }

        private void InitializeTable()
        {
            SqliteCommand command = _connection.CreateCommand();
            command.CommandText = $@"
                CREATE TABLE IF NOT EXISTS {_tableName} (
                    node_id TEXT PRIMARY KEY,
                    layer INTEGER NOT NULL,
                    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
                )";
            command.ExecuteNonQuery();

            // Create index for performance
            SqliteCommand indexCommand = _connection.CreateCommand();
            indexCommand.CommandText = $"CREATE INDEX IF NOT EXISTS idx_{_tableName}_node_id ON {_tableName}(node_id)";
            indexCommand.ExecuteNonQuery();
        }

        private void EnsureCacheLoaded()
        {
            if (_cacheLoaded)
                return;

            _layersLock.EnterWriteLock();
            try
            {
                EnsureCacheLoadedUnsafe();
            }
            finally
            {
                _layersLock.ExitWriteLock();
            }
        }

        private void EnsureCacheLoadedUnsafe()
        {
            if (_cacheLoaded)
                return;

            _layerCache.Clear();

            SqliteCommand command = _connection.CreateCommand();
            command.CommandText = $"SELECT node_id, layer FROM {_tableName}";

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (Guid.TryParse(reader.GetString(0), out Guid nodeId))
                {
                    int layer = reader.GetInt32(1);
                    _layerCache[nodeId] = layer;
                }
            }

            _cacheLoaded = true;
        }
    }
}