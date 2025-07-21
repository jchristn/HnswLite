namespace HnswIndex.SqliteStorage
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using System.Text.Json;
    using System.IO;
    using Hnsw.SqliteStorage;
    using Hnsw;

    /// <summary>
    /// SQLite-based implementation of HNSW storage with thread-safe operations.
    /// Provides persistent storage for HNSW nodes in a SQLite database.
    /// </summary>
    public class SqliteHnswStorage : IHnswStorage, IDisposable
    {
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type.

        // Private members
        private readonly SqliteConnection _connection;
        private readonly Dictionary<Guid, SqliteHnswNode> _nodeCache = new Dictionary<Guid, SqliteHnswNode>();
        private readonly ReaderWriterLockSlim _storageLock = new ReaderWriterLockSlim();
        private readonly string _databasePath;
        private readonly string _nodesTableName;
        private readonly string _neighborsTableName;
        private readonly string _metadataTableName;
        private Guid? _entryPoint = null;
        private bool _disposed = false;
        private bool _entryPointLoaded = false;

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
                    if (!_entryPointLoaded)
                    {
                        _storageLock.ExitReadLock();
                        _storageLock.EnterWriteLock();
                        try
                        {
                            if (!_entryPointLoaded)
                            {
                                LoadEntryPointFromDatabase();
                                _entryPointLoaded = true;
                            }
                        }
                        finally
                        {
                            _storageLock.ExitWriteLock();
                            _storageLock.EnterReadLock();
                        }
                    }
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
                    if (value.HasValue && !NodeExistsInDatabase(value.Value))
                    {
                        throw new ArgumentException($"Entry point node {value.Value} does not exist in storage.", nameof(value));
                    }
                    _entryPoint = value;
                    SaveEntryPointToDatabase();
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
                    var command = _connection.CreateCommand();
                    command.CommandText = $"SELECT COUNT(*) FROM {_nodesTableName}";
                    var count = Convert.ToInt32(command.ExecuteScalar());
                    return count == 0;
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

        /// <summary>
        /// Gets the database file path.
        /// </summary>
        public string DatabasePath => _databasePath;

        /// <summary>
        /// Gets the SQLite database connection.
        /// </summary>
        public SqliteConnection Connection => _connection;

        // Constructors
        /// <summary>
        /// Initializes a new instance of the SqliteHnswStorage class.
        /// </summary>
        /// <param name="databasePath">Path to the SQLite database file. Cannot be null or empty.</param>
        /// <param name="createIfNotExists">Whether to create the database if it doesn't exist. Default: true.</param>
        /// <exception cref="ArgumentNullException">Thrown when databasePath is null or empty.</exception>
        /// <exception cref="FileNotFoundException">Thrown when database doesn't exist and createIfNotExists is false.</exception>
        public SqliteHnswStorage(string databasePath, bool createIfNotExists = true)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentNullException(nameof(databasePath));

            _databasePath = databasePath;
            _nodesTableName = "hnsw_nodes";
            _neighborsTableName = "hnsw_neighbors";
            _metadataTableName = "hnsw_metadata";

            if (!createIfNotExists && !File.Exists(databasePath))
                throw new FileNotFoundException($"Database file not found: {databasePath}");

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ToString();

            _connection = new SqliteConnection(connectionString);
            _connection.Open();

            // Enable WAL mode for better crash recovery
            var walCommand = _connection.CreateCommand();
            walCommand.CommandText = "PRAGMA journal_mode=WAL";
            walCommand.ExecuteNonQuery();

            // Enable synchronous=NORMAL for better performance with WAL
            var syncCommand = _connection.CreateCommand();
            syncCommand.CommandText = "PRAGMA synchronous=NORMAL";
            syncCommand.ExecuteNonQuery();

            InitializeDatabase();
        }

        /// <summary>
        /// Initializes a new instance of the SqliteHnswStorage class with custom table names.
        /// </summary>
        /// <param name="databasePath">Path to the SQLite database file. Cannot be null or empty.</param>
        /// <param name="nodesTableName">Name for the nodes table. Cannot be null or empty.</param>
        /// <param name="neighborsTableName">Name for the neighbors table. Cannot be null or empty.</param>
        /// <param name="metadataTableName">Name for the metadata table. Cannot be null or empty.</param>
        /// <param name="createIfNotExists">Whether to create the database if it doesn't exist. Default: true.</param>
        /// <exception cref="ArgumentNullException">Thrown when any parameter is null or empty.</exception>
        public SqliteHnswStorage(string databasePath, string nodesTableName, string neighborsTableName, string metadataTableName, bool createIfNotExists = true)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentNullException(nameof(databasePath));
            if (string.IsNullOrWhiteSpace(nodesTableName))
                throw new ArgumentNullException(nameof(nodesTableName));
            if (string.IsNullOrWhiteSpace(neighborsTableName))
                throw new ArgumentNullException(nameof(neighborsTableName));
            if (string.IsNullOrWhiteSpace(metadataTableName))
                throw new ArgumentNullException(nameof(metadataTableName));

            _databasePath = databasePath;
            _nodesTableName = nodesTableName;
            _neighborsTableName = neighborsTableName;
            _metadataTableName = metadataTableName;

            if (!createIfNotExists && !File.Exists(databasePath))
                throw new FileNotFoundException($"Database file not found: {databasePath}");

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ToString();

            _connection = new SqliteConnection(connectionString);
            _connection.Open();

            InitializeDatabase();
        }

        // Public methods
        /// <summary>
        /// Gets the number of nodes in storage.
        /// Thread-safe operation.
        /// Minimum: 0, Maximum: int.MaxValue (limited by available disk space).
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
                    var command = _connection.CreateCommand();
                    command.CommandText = $"SELECT COUNT(*) FROM {_nodesTableName}";
                    return Convert.ToInt32(command.ExecuteScalar());
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

                _storageLock.EnterWriteLock();
                try
                {
                    // Remove from cache if exists
                    if (_nodeCache.TryGetValue(id, out var existingNode))
                    {
                        existingNode.Dispose();
                        _nodeCache.Remove(id);
                    }

                    // Save vector to database
                    var vectorJson = JsonSerializer.Serialize(vector);
                    var command = _connection.CreateCommand();
                    command.CommandText = $@"
                        INSERT OR REPLACE INTO {_nodesTableName} (id, vector_json) 
                        VALUES (@id, @vectorJson)";
                    command.Parameters.AddWithValue("@id", id.ToString());
                    command.Parameters.AddWithValue("@vectorJson", vectorJson);
                    command.ExecuteNonQuery();

                    // Create node and add to cache
                    var node = new SqliteHnswNode(id, vector, _connection, _neighborsTableName);
                    _nodeCache[id] = node;

                    // Set as entry point if this is the first node
                    if (!_entryPointLoaded)
                    {
                        LoadEntryPointFromDatabase();
                        _entryPointLoaded = true;
                    }

                    if (_entryPoint == null)
                    {
                        _entryPoint = id;
                        SaveEntryPointToDatabase();
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
                    // Remove from cache
                    if (_nodeCache.TryGetValue(id, out var node))
                    {
                        node.Dispose();
                        _nodeCache.Remove(id);
                    }

                    // Remove from database
                    var deleteNodeCommand = _connection.CreateCommand();
                    deleteNodeCommand.CommandText = $"DELETE FROM {_nodesTableName} WHERE id = @id";
                    deleteNodeCommand.Parameters.AddWithValue("@id", id.ToString());
                    deleteNodeCommand.ExecuteNonQuery();

                    var deleteNeighborsCommand = _connection.CreateCommand();
                    deleteNeighborsCommand.CommandText = $"DELETE FROM {_neighborsTableName} WHERE node_id = @id";
                    deleteNeighborsCommand.Parameters.AddWithValue("@id", id.ToString());
                    deleteNeighborsCommand.ExecuteNonQuery();

                    // Remove layer assignment
                    var deleteLayerCommand = _connection.CreateCommand();
                    deleteLayerCommand.CommandText = $"DELETE FROM {_nodesTableName}_layers WHERE node_id = @id";
                    deleteLayerCommand.Parameters.AddWithValue("@id", id.ToString());
                    deleteLayerCommand.ExecuteNonQuery();

                    // Update entry point if necessary
                    if (_entryPoint == id)
                    {
                        var newEntryPointCommand = _connection.CreateCommand();
                        newEntryPointCommand.CommandText = $"SELECT id FROM {_nodesTableName} LIMIT 1";
                        var result = newEntryPointCommand.ExecuteScalar();

                        if (result != null && result != DBNull.Value)
                        {
                            if (Guid.TryParse(result.ToString(), out Guid newEntryPoint))
                                _entryPoint = newEntryPoint;
                            else
                                _entryPoint = null;
                        }
                        else
                        {
                            _entryPoint = null;
                        }

                        SaveEntryPointToDatabase();
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
        /// Uses caching for improved performance.
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
                    // Check cache first
                    if (_nodeCache.TryGetValue(id, out var cachedNode))
                        return cachedNode;
                }
                finally
                {
                    _storageLock.ExitReadLock();
                }

                // Not in cache, load from database
                _storageLock.EnterWriteLock();
                try
                {
                    // Double-check cache after acquiring write lock
                    if (_nodeCache.TryGetValue(id, out var cachedNode))
                        return cachedNode;

                    // Load from database
                    var command = _connection.CreateCommand();
                    command.CommandText = $"SELECT vector_json FROM {_nodesTableName} WHERE id = @id";
                    command.Parameters.AddWithValue("@id", id.ToString());

                    var result = command.ExecuteScalar();
                    if (result == null || result == DBNull.Value)
                        throw new KeyNotFoundException($"Node with ID {id} not found in storage.");

                    var vectorJson = result.ToString();
                    var vector = JsonSerializer.Deserialize<List<float>>(vectorJson);

                    var node = new SqliteHnswNode(id, vector, _connection, _neighborsTableName);
                    _nodeCache[id] = node;
                    return node;
                }
                finally
                {
                    _storageLock.ExitWriteLock();
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Tries to get a node by ID.
        /// Thread-safe operation.
        /// Uses caching for improved performance.
        /// </summary>
        /// <param name="id">Node identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A tuple indicating success and the node if found.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the storage has been disposed.</exception>
        public async Task<(bool success, IHnswNode node)> TryGetNodeAsync(Guid id, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var node = GetNodeAsync(id, cancellationToken).Result;
                    return (true, node);
                }
                catch (KeyNotFoundException)
                {
                    return (false, null);
                }
                catch (AggregateException ex) when (ex.InnerException is KeyNotFoundException)
                {
                    return (false, null);
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
                    var nodeIds = new List<Guid>();
                    var command = _connection.CreateCommand();
                    command.CommandText = $"SELECT id FROM {_nodesTableName}";

                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        if (Guid.TryParse(reader.GetString(0), out Guid nodeId))
                        {
                            nodeIds.Add(nodeId);
                        }
                    }

                    return nodeIds;
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
                // Check cache first
                if (_nodeCache.ContainsKey(id))
                    return true;

                // Check database
                return NodeExistsInDatabase(id);
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
                // Dispose all cached nodes
                foreach (var node in _nodeCache.Values)
                {
                    node.Dispose();
                }
                _nodeCache.Clear();

                // Clear database tables
                var clearNodesCommand = _connection.CreateCommand();
                clearNodesCommand.CommandText = $"DELETE FROM {_nodesTableName}";
                clearNodesCommand.ExecuteNonQuery();

                var clearNeighborsCommand = _connection.CreateCommand();
                clearNeighborsCommand.CommandText = $"DELETE FROM {_neighborsTableName}";
                clearNeighborsCommand.ExecuteNonQuery();

                var clearLayersCommand = _connection.CreateCommand();
                clearLayersCommand.CommandText = $"DELETE FROM {_nodesTableName}_layers";
                clearLayersCommand.ExecuteNonQuery();

                _entryPoint = null;
                SaveEntryPointToDatabase();
            }
            finally
            {
                _storageLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Forces a flush of all cached data to the database.
        /// Thread-safe operation.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown when the storage has been disposed.</exception>
        public void Flush()
        {
            ThrowIfDisposed();

            _storageLock.EnterReadLock();
            try
            {
                foreach (var node in _nodeCache.Values)
                {
                    node.Flush();
                }
            }
            finally
            {
                _storageLock.ExitReadLock();
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
                        // Flush and dispose all cached nodes
                        foreach (var node in _nodeCache.Values)
                        {
                            try
                            {
                                node.Flush();
                                node.Dispose();
                            }
                            catch
                            {
                                // Ignore errors during disposal
                            }
                        }
                        _nodeCache.Clear();
                    }
                    finally
                    {
                        _storageLock.ExitWriteLock();
                    }

                    _storageLock?.Dispose();
                    _connection?.Dispose();
                }
                _disposed = true;
            }
        }

        // Private methods
        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SqliteHnswStorage));
        }

        private void InitializeDatabase()
        {
            // Create nodes table
            var createNodesTableCommand = _connection.CreateCommand();
            createNodesTableCommand.CommandText = $@"
                CREATE TABLE IF NOT EXISTS {_nodesTableName} (
                    id TEXT PRIMARY KEY,
                    vector_json TEXT NOT NULL,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP
                )";
            createNodesTableCommand.ExecuteNonQuery();

            // Create neighbors table
            var createNeighborsTableCommand = _connection.CreateCommand();
            createNeighborsTableCommand.CommandText = $@"
                CREATE TABLE IF NOT EXISTS {_neighborsTableName} (
                    node_id TEXT PRIMARY KEY,
                    neighbors_json TEXT,
                    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
                )";
            createNeighborsTableCommand.ExecuteNonQuery();

            // Create metadata table
            var createMetadataTableCommand = _connection.CreateCommand();
            createMetadataTableCommand.CommandText = $@"
                CREATE TABLE IF NOT EXISTS {_metadataTableName} (
                    key TEXT PRIMARY KEY,
                    value TEXT,
                    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
                )";
            createMetadataTableCommand.ExecuteNonQuery();

            // Create node layers table
            var createNodeLayersTableCommand = _connection.CreateCommand();
            createNodeLayersTableCommand.CommandText = $@"
                CREATE TABLE IF NOT EXISTS {_nodesTableName}_layers (
                    node_id TEXT PRIMARY KEY,
                    layer INTEGER NOT NULL,
                    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (node_id) REFERENCES {_nodesTableName}(id) ON DELETE CASCADE
                )";
            createNodeLayersTableCommand.ExecuteNonQuery();

            // Create indexes for performance
            var createNodeIndexCommand = _connection.CreateCommand();
            createNodeIndexCommand.CommandText = $"CREATE INDEX IF NOT EXISTS idx_{_nodesTableName}_id ON {_nodesTableName}(id)";
            createNodeIndexCommand.ExecuteNonQuery();

            var createNeighborIndexCommand = _connection.CreateCommand();
            createNeighborIndexCommand.CommandText = $"CREATE INDEX IF NOT EXISTS idx_{_neighborsTableName}_node_id ON {_neighborsTableName}(node_id)";
            createNeighborIndexCommand.ExecuteNonQuery();

            var createLayersIndexCommand = _connection.CreateCommand();
            createLayersIndexCommand.CommandText = $"CREATE INDEX IF NOT EXISTS idx_{_nodesTableName}_layers_node_id ON {_nodesTableName}_layers(node_id)";
            createLayersIndexCommand.ExecuteNonQuery();
        }

        /// <summary>
        /// Sets the layer for a node in the database.
        /// Thread-safe operation.
        /// </summary>
        /// <param name="nodeId">Node identifier.</param>
        /// <param name="layer">Layer number.</param>
        /// <exception cref="ObjectDisposedException">Thrown when the storage has been disposed.</exception>
        public void SetNodeLayer(Guid nodeId, int layer)
        {
            ThrowIfDisposed();

            _storageLock.EnterWriteLock();
            try
            {
                var command = _connection.CreateCommand();
                command.CommandText = $@"
                    INSERT OR REPLACE INTO {_nodesTableName}_layers (node_id, layer, updated_at) 
                    VALUES (@nodeId, @layer, CURRENT_TIMESTAMP)";
                command.Parameters.AddWithValue("@nodeId", nodeId.ToString());
                command.Parameters.AddWithValue("@layer", layer);
                command.ExecuteNonQuery();
            }
            finally
            {
                _storageLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Gets the layer for a node from the database.
        /// Thread-safe operation.
        /// </summary>
        /// <param name="nodeId">Node identifier.</param>
        /// <returns>The layer number, or 0 if not found.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the storage has been disposed.</exception>
        public int GetNodeLayer(Guid nodeId)
        {
            ThrowIfDisposed();

            _storageLock.EnterReadLock();
            try
            {
                var command = _connection.CreateCommand();
                command.CommandText = $"SELECT layer FROM {_nodesTableName}_layers WHERE node_id = @nodeId";
                command.Parameters.AddWithValue("@nodeId", nodeId.ToString());

                var result = command.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                {
                    return Convert.ToInt32(result);
                }
                return 0; // Default layer
            }
            finally
            {
                _storageLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Gets all node layer assignments from the database.
        /// Thread-safe operation.
        /// </summary>
        /// <returns>Dictionary mapping node IDs to layer numbers.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the storage has been disposed.</exception>
        public Dictionary<Guid, int> GetAllNodeLayers()
        {
            ThrowIfDisposed();

            _storageLock.EnterReadLock();
            try
            {
                var layers = new Dictionary<Guid, int>();
                var command = _connection.CreateCommand();
                command.CommandText = $"SELECT node_id, layer FROM {_nodesTableName}_layers";

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (Guid.TryParse(reader.GetString(0), out Guid nodeId))
                    {
                        layers[nodeId] = reader.GetInt32(1);
                    }
                }

                return layers;
            }
            finally
            {
                _storageLock.ExitReadLock();
            }
        }

        private bool NodeExistsInDatabase(Guid id)
        {
            var command = _connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {_nodesTableName} WHERE id = @id";
            command.Parameters.AddWithValue("@id", id.ToString());
            var count = Convert.ToInt32(command.ExecuteScalar());
            return count > 0;
        }

        private void LoadEntryPointFromDatabase()
        {
            var command = _connection.CreateCommand();
            command.CommandText = $"SELECT value FROM {_metadataTableName} WHERE key = 'entry_point'";
            var result = command.ExecuteScalar();

            if (result != null && result != DBNull.Value)
            {
                if (Guid.TryParse(result.ToString(), out Guid entryPoint))
                    _entryPoint = entryPoint;
                else
                    _entryPoint = null;
            }
            else
            {
                _entryPoint = null;
            }
        }

        private void SaveEntryPointToDatabase()
        {
            var command = _connection.CreateCommand();
            command.CommandText = $@"
                INSERT OR REPLACE INTO {_metadataTableName} (key, value, updated_at) 
                VALUES ('entry_point', @value, CURRENT_TIMESTAMP)";
            command.Parameters.AddWithValue("@value", _entryPoint?.ToString() ?? string.Empty);
            command.ExecuteNonQuery();
        }

#pragma warning restore CS8619 // Nullability of reference types in value doesn't match target type.
    }
}