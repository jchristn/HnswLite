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

            // Use FULL synchronous for complete durability
            var syncCommand = _connection.CreateCommand();
            syncCommand.CommandText = "PRAGMA synchronous=FULL";
            syncCommand.ExecuteNonQuery();

            // Optimize cache and memory settings for better performance
            var cacheCommand = _connection.CreateCommand();
            cacheCommand.CommandText = "PRAGMA cache_size=10000";
            cacheCommand.ExecuteNonQuery();

            var tempStoreCommand = _connection.CreateCommand();
            tempStoreCommand.CommandText = "PRAGMA temp_store=MEMORY";
            tempStoreCommand.ExecuteNonQuery();

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

                    // Save vector to database using binary format for better performance
                    var vectorBlob = SerializeVector(vector);
                    var command = _connection.CreateCommand();
                    command.CommandText = $@"
                        INSERT OR REPLACE INTO {_nodesTableName} (id, vector_blob, vector_dimension, updated_at) 
                        VALUES (@id, @vectorBlob, @dimension, CURRENT_TIMESTAMP)";
                    command.Parameters.AddWithValue("@id", id.ToByteArray());
                    command.Parameters.AddWithValue("@vectorBlob", vectorBlob);
                    command.Parameters.AddWithValue("@dimension", vector.Count);
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

                // Validate all nodes first
                foreach (var kvp in nodes)
                {
                    if (kvp.Key == Guid.Empty)
                        throw new ArgumentException($"Node ID cannot be Guid.Empty.", nameof(nodes));
                    if (kvp.Value == null)
                        throw new ArgumentNullException(nameof(nodes), $"Vector for node {kvp.Key} is null.");
                    if (kvp.Value.Count == 0)
                        throw new ArgumentException($"Vector for node {kvp.Key} cannot be empty.", nameof(nodes));
                }

                _storageLock.EnterWriteLock();
                try
                {
                    // Use a transaction for batch operation
                    using var transaction = _connection.BeginTransaction();
                    try
                    {
                        bool wasEmpty = false;

                        // Check if this is the first batch
                        if (!_entryPointLoaded)
                        {
                            LoadEntryPointFromDatabase();
                            _entryPointLoaded = true;
                            wasEmpty = _entryPoint == null;
                        }
                        else
                        {
                            wasEmpty = _entryPoint == null;
                        }

                        // Prepare the insert command
                        var command = _connection.CreateCommand();
                        command.Transaction = transaction;
                        command.CommandText = $@"
                            INSERT OR REPLACE INTO {_nodesTableName} (id, vector_blob, vector_dimension, updated_at) 
                            VALUES (@id, @vectorBlob, @dimension, CURRENT_TIMESTAMP)";

                        // Add all nodes
                        Guid? firstNodeId = null;
                        foreach (var kvp in nodes)
                        {
                            if (firstNodeId == null)
                                firstNodeId = kvp.Key;

                            // Remove from cache if exists
                            if (_nodeCache.TryGetValue(kvp.Key, out var existingNode))
                            {
                                existingNode.Dispose();
                                _nodeCache.Remove(kvp.Key);
                            }

                            // Save vector to database using binary format for better performance
                            var vectorBlob = SerializeVector(kvp.Value);
                            command.Parameters.Clear();
                            command.Parameters.AddWithValue("@id", kvp.Key.ToByteArray());
                            command.Parameters.AddWithValue("@vectorBlob", vectorBlob);
                            command.Parameters.AddWithValue("@dimension", kvp.Value.Count);
                            command.ExecuteNonQuery();

                            // Create node and add to cache
                            var node = new SqliteHnswNode(kvp.Key, kvp.Value, _connection, _neighborsTableName);
                            _nodeCache[kvp.Key] = node;
                        }

                        // Set entry point if this was the first batch
                        if (wasEmpty && firstNodeId.HasValue)
                        {
                            _entryPoint = firstNodeId.Value;
                            SaveEntryPointToDatabase();
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
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
                    deleteNodeCommand.Parameters.AddWithValue("@id", id.ToByteArray());
                    deleteNodeCommand.ExecuteNonQuery();

                    var deleteNeighborsCommand = _connection.CreateCommand();
                    deleteNeighborsCommand.CommandText = $"DELETE FROM {_neighborsTableName} WHERE node_id = @id";
                    deleteNeighborsCommand.Parameters.AddWithValue("@id", id.ToByteArray());
                    deleteNeighborsCommand.ExecuteNonQuery();

                    // Remove layer assignment
                    var deleteLayerCommand = _connection.CreateCommand();
                    deleteLayerCommand.CommandText = $"DELETE FROM {_nodesTableName}_layers WHERE node_id = @id";
                    deleteLayerCommand.Parameters.AddWithValue("@id", id.ToByteArray());
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
                    // Use a transaction for batch operation
                    using var transaction = _connection.BeginTransaction();
                    try
                    {
                        bool entryPointRemoved = false;

                        // Prepare delete commands
                        var deleteNodeCommand = _connection.CreateCommand();
                        deleteNodeCommand.Transaction = transaction;
                        deleteNodeCommand.CommandText = $"DELETE FROM {_nodesTableName} WHERE id = @id";

                        var deleteNeighborsCommand = _connection.CreateCommand();
                        deleteNeighborsCommand.Transaction = transaction;
                        deleteNeighborsCommand.CommandText = $"DELETE FROM {_neighborsTableName} WHERE node_id = @id";

                        var deleteLayerCommand = _connection.CreateCommand();
                        deleteLayerCommand.Transaction = transaction;
                        deleteLayerCommand.CommandText = $"DELETE FROM {_nodesTableName}_layers WHERE node_id = @id";

                        foreach (var id in ids)
                        {
                            // Remove from cache
                            if (_nodeCache.TryGetValue(id, out var node))
                            {
                                node.Dispose();
                                _nodeCache.Remove(id);
                            }

                            // Remove from database
                            deleteNodeCommand.Parameters.Clear();
                            deleteNodeCommand.Parameters.AddWithValue("@id", id.ToByteArray());
                            deleteNodeCommand.ExecuteNonQuery();

                            deleteNeighborsCommand.Parameters.Clear();
                            deleteNeighborsCommand.Parameters.AddWithValue("@id", id.ToByteArray());
                            deleteNeighborsCommand.ExecuteNonQuery();

                            deleteLayerCommand.Parameters.Clear();
                            deleteLayerCommand.Parameters.AddWithValue("@id", id.ToByteArray());
                            deleteLayerCommand.ExecuteNonQuery();

                            if (_entryPoint == id)
                            {
                                entryPointRemoved = true;
                            }
                        }

                        // Update entry point if necessary
                        if (entryPointRemoved)
                        {
                            var newEntryPointCommand = _connection.CreateCommand();
                            newEntryPointCommand.Transaction = transaction;
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

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
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
                    command.CommandText = $"SELECT vector_blob FROM {_nodesTableName} WHERE id = @id";
                    command.Parameters.AddWithValue("@id", id.ToByteArray());

                    var result = command.ExecuteScalar();
                    if (result == null || result == DBNull.Value)
                        throw new KeyNotFoundException($"Node with ID {id} not found in storage.");

                    var vectorBlob = (byte[])result;
                    var vector = DeserializeVector(vectorBlob);

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
        /// Gets multiple nodes by their IDs in a batch operation.
        /// Thread-safe operation.
        /// More efficient than calling GetNodeAsync multiple times.
        /// Uses caching for improved performance.
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

                var result = new Dictionary<Guid, IHnswNode>();
                var idsToLoad = new List<Guid>();

                // First pass: get cached nodes
                _storageLock.EnterReadLock();
                try
                {
                    foreach (var id in ids)
                    {
                        if (_nodeCache.TryGetValue(id, out var cachedNode))
                        {
                            result[id] = cachedNode;
                        }
                        else
                        {
                            idsToLoad.Add(id);
                        }
                    }
                }
                finally
                {
                    _storageLock.ExitReadLock();
                }

                // Second pass: load missing nodes from database
                if (idsToLoad.Count > 0)
                {
                    _storageLock.EnterWriteLock();
                    try
                    {
                        // Build a query to get all nodes in one go
                        var placeholders = string.Join(",", idsToLoad.Select((_, i) => $"@id{i}"));
                        var command = _connection.CreateCommand();
                        command.CommandText = $"SELECT id, vector_blob FROM {_nodesTableName} WHERE id IN ({placeholders})";

                        for (int i = 0; i < idsToLoad.Count; i++)
                        {
                            command.Parameters.AddWithValue($"@id{i}", idsToLoad[i].ToByteArray());
                        }

                        using var reader = command.ExecuteReader();
                        while (reader.Read())
                        {
                            var idBytes = (byte[])reader[0];
                            var nodeId = new Guid(idBytes);
                            var vectorBlob = (byte[])reader[1];
                            var vector = DeserializeVector(vectorBlob);

                            var node = new SqliteHnswNode(nodeId, vector, _connection, _neighborsTableName);
                            _nodeCache[nodeId] = node;
                            result[nodeId] = node;
                        }
                    }
                    finally
                    {
                        _storageLock.ExitWriteLock();
                    }
                }

                return result;
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
        public async Task<TryGetNodeResult> TryGetNodeAsync(Guid id, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var node = GetNodeAsync(id, cancellationToken).Result;
                    return TryGetNodeResult.Found(node);
                }
                catch (KeyNotFoundException)
                {
                    return TryGetNodeResult.NotFound();
                }
                catch (AggregateException ex) when (ex.InnerException is KeyNotFoundException)
                {
                    return TryGetNodeResult.NotFound();
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
                        var idBytes = (byte[])reader[0];
                        var nodeId = new Guid(idBytes);
                        nodeIds.Add(nodeId);
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
            // Create nodes table with binary storage for better performance
            var createNodesTableCommand = _connection.CreateCommand();
            createNodesTableCommand.CommandText = $@"
                CREATE TABLE IF NOT EXISTS {_nodesTableName} (
                    id BLOB PRIMARY KEY,
                    vector_blob BLOB NOT NULL,
                    vector_dimension INTEGER NOT NULL,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
                )";
            createNodesTableCommand.ExecuteNonQuery();

            // Create neighbors table
            var createNeighborsTableCommand = _connection.CreateCommand();
            createNeighborsTableCommand.CommandText = $@"
                CREATE TABLE IF NOT EXISTS {_neighborsTableName} (
                    node_id BLOB PRIMARY KEY,
                    neighbors_blob BLOB,
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
                    node_id BLOB PRIMARY KEY,
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
                command.Parameters.AddWithValue("@nodeId", nodeId.ToByteArray());
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
                command.Parameters.AddWithValue("@nodeId", nodeId.ToByteArray());

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
                    var idBytes = (byte[])reader[0];
                    var nodeId = new Guid(idBytes);
                    layers[nodeId] = reader.GetInt32(1);
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
            command.Parameters.AddWithValue("@id", id.ToByteArray());
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

        // Binary serialization methods for better performance
        private byte[] SerializeVector(List<float> vector)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            writer.Write(vector.Count);
            foreach (var f in vector)
            {
                writer.Write(f);
            }
            return ms.ToArray();
        }

        private List<float> DeserializeVector(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var reader = new BinaryReader(ms);
            
            int count = reader.ReadInt32();
            var vector = new List<float>(count);
            for (int i = 0; i < count; i++)
            {
                vector.Add(reader.ReadSingle());
            }
            return vector;
        }

        private byte[] SerializeNeighbors(Dictionary<int, HashSet<Guid>> neighbors)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            writer.Write(neighbors.Count);
            foreach (var kvp in neighbors)
            {
                writer.Write(kvp.Key); // layer
                writer.Write(kvp.Value.Count); // neighbor count
                foreach (var neighborId in kvp.Value)
                {
                    writer.Write(neighborId.ToByteArray());
                }
            }
            return ms.ToArray();
        }

        private Dictionary<int, HashSet<Guid>> DeserializeNeighbors(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return new Dictionary<int, HashSet<Guid>>();
                
            using var ms = new MemoryStream(bytes);
            using var reader = new BinaryReader(ms);
            
            var neighbors = new Dictionary<int, HashSet<Guid>>();
            int layerCount = reader.ReadInt32();
            
            for (int i = 0; i < layerCount; i++)
            {
                int layer = reader.ReadInt32();
                int neighborCount = reader.ReadInt32();
                var layerNeighbors = new HashSet<Guid>();
                
                for (int j = 0; j < neighborCount; j++)
                {
                    var guidBytes = reader.ReadBytes(16);
                    layerNeighbors.Add(new Guid(guidBytes));
                }
                
                neighbors[layer] = layerNeighbors;
            }
            
            return neighbors;
        }

#pragma warning restore CS8619 // Nullability of reference types in value doesn't match target type.
    }
}