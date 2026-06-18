namespace HnswIndex.SqliteStorage
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Hnsw;
    using Hnsw.SqliteStorage;
    using Microsoft.Data.Sqlite;

    /// <summary>
    /// SQLite-based implementation of HNSW node storage.
    /// </summary>
    public sealed class SqliteHnswStorage : IHnswStorage, IDisposable, IAsyncDisposable
    {
        private readonly SqliteConnection _Connection;
        private readonly SemaphoreSlim _DatabaseLock;
        private readonly bool _OwnsDatabaseLock;
        private readonly SemaphoreSlim _StorageLock = new SemaphoreSlim(1, 1);
        private readonly Dictionary<Guid, SqliteHnswNode> _NodeCache = new Dictionary<Guid, SqliteHnswNode>();
        private readonly string _DatabasePath;
        private readonly string _NodesTableName;
        private readonly string _NeighborsTableName;
        private readonly string _MetadataTableName;
        private Guid? _EntryPoint;
        private bool _Disposed;
        private bool _EntryPointLoaded;

        private SqliteHnswStorage(
            string databasePath,
            string nodesTableName,
            string neighborsTableName,
            string metadataTableName,
            SqliteConnection connection,
            SemaphoreSlim databaseLock,
            bool ownsDatabaseLock)
        {
            _DatabasePath = databasePath;
            _NodesTableName = nodesTableName;
            _NeighborsTableName = neighborsTableName;
            _MetadataTableName = metadataTableName;
            _Connection = connection;
            _DatabaseLock = databaseLock;
            _OwnsDatabaseLock = ownsDatabaseLock;
        }

        /// <summary>
        /// Gets whether the storage has been disposed.
        /// </summary>
        public bool IsDisposed => _Disposed;

        /// <summary>
        /// Gets the database file path.
        /// </summary>
        public string DatabasePath => _DatabasePath;

        /// <summary>
        /// Gets the SQLite database connection.
        /// </summary>
        public SqliteConnection Connection => _Connection;

        /// <summary>
        /// Creates a SQLite storage instance and initializes the backing schema asynchronously.
        /// </summary>
        public static Task<SqliteHnswStorage> CreateAsync(
            string databasePath,
            bool createIfNotExists = true,
            SemaphoreSlim? databaseLock = null,
            CancellationToken cancellationToken = default)
        {
            return CreateAsync(
                databasePath,
                "hnsw_nodes",
                "hnsw_neighbors",
                "hnsw_metadata",
                createIfNotExists,
                databaseLock,
                cancellationToken);
        }

        /// <summary>
        /// Creates a SQLite storage instance with custom table names and initializes the backing schema asynchronously.
        /// </summary>
        public static async Task<SqliteHnswStorage> CreateAsync(
            string databasePath,
            string nodesTableName,
            string neighborsTableName,
            string metadataTableName,
            bool createIfNotExists = true,
            SemaphoreSlim? databaseLock = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(databasePath)) throw new ArgumentNullException(nameof(databasePath));
            if (string.IsNullOrWhiteSpace(nodesTableName)) throw new ArgumentNullException(nameof(nodesTableName));
            if (string.IsNullOrWhiteSpace(neighborsTableName)) throw new ArgumentNullException(nameof(neighborsTableName));
            if (string.IsNullOrWhiteSpace(metadataTableName)) throw new ArgumentNullException(nameof(metadataTableName));
            if (!createIfNotExists && !File.Exists(databasePath))
            {
                throw new FileNotFoundException($"Database file not found: {databasePath}");
            }

            string? directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            bool ownsDatabaseLock = databaseLock == null;
            SemaphoreSlim sqliteLock = databaseLock ?? new SemaphoreSlim(1, 1);
            SqliteConnection connection = await OpenAndConfigureConnectionAsync(databasePath, sqliteLock, cancellationToken).ConfigureAwait(false);

            SqliteHnswStorage storage = new SqliteHnswStorage(
                databasePath,
                nodesTableName,
                neighborsTableName,
                metadataTableName,
                connection,
                sqliteLock,
                ownsDatabaseLock);

            try
            {
                await storage.InitializeDatabaseAsync(cancellationToken).ConfigureAwait(false);
                return storage;
            }
            catch
            {
                await storage.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task<Guid?> GetEntryPointAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _StorageLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await EnsureEntryPointLoadedUnsafeAsync(cancellationToken).ConfigureAwait(false);
                return _EntryPoint;
            }
            finally
            {
                _StorageLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task SetEntryPointAsync(Guid? entryPoint, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _StorageLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _DatabaseLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (entryPoint.HasValue && !await NodeExistsInDatabaseCoreAsync(entryPoint.Value, null, cancellationToken).ConfigureAwait(false))
                    {
                        throw new ArgumentException($"Entry point node {entryPoint.Value} does not exist in storage.", nameof(entryPoint));
                    }

                    _EntryPoint = entryPoint;
                    _EntryPointLoaded = true;
                    await SaveEntryPointToDatabaseCoreAsync(null, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _DatabaseLock.Release();
                }
            }
            finally
            {
                _StorageLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _DatabaseLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using SqliteCommand command = _Connection.CreateCommand();
                command.CommandText = $"SELECT COUNT(*) FROM {_NodesTableName}";
                object? count = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                return Convert.ToInt32(count);
            }
            finally
            {
                _DatabaseLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task AddNodeAsync(Guid id, List<float> vector, CancellationToken cancellationToken = default)
        {
            ValidateNode(id, vector);
            ThrowIfDisposed();

            await _StorageLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                RemoveCachedNodeUnsafe(id);

                await _DatabaseLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    using SqliteCommand command = _Connection.CreateCommand();
                    command.CommandText = $@"
                        INSERT OR REPLACE INTO {_NodesTableName} (id, vector_blob, vector_dimension, metadata_json, updated_at)
                        VALUES (@id, @vectorBlob, @dimension, NULL, CURRENT_TIMESTAMP)";
                    command.Parameters.AddWithValue("@id", id.ToByteArray());
                    command.Parameters.AddWithValue("@vectorBlob", SerializeVector(vector));
                    command.Parameters.AddWithValue("@dimension", vector.Count);
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                    await EnsureEntryPointLoadedCoreAsync(null, cancellationToken).ConfigureAwait(false);
                    if (_EntryPoint == null)
                    {
                        _EntryPoint = id;
                        await SaveEntryPointToDatabaseCoreAsync(null, cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _DatabaseLock.Release();
                }

                _NodeCache[id] = await CreateNodeAsync(id, vector, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _StorageLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task AddNodesAsync(Dictionary<Guid, List<float>> nodes, CancellationToken cancellationToken = default)
        {
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            foreach (KeyValuePair<Guid, List<float>> kvp in nodes)
            {
                ValidateNode(kvp.Key, kvp.Value);
            }

            ThrowIfDisposed();

            await _StorageLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (Guid id in nodes.Keys)
                {
                    RemoveCachedNodeUnsafe(id);
                }

                await _DatabaseLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await using SqliteTransaction transaction = (SqliteTransaction)await _Connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        await EnsureEntryPointLoadedCoreAsync(transaction, cancellationToken).ConfigureAwait(false);
                        bool wasEmpty = _EntryPoint == null;
                        Guid? firstNodeId = null;

                        using SqliteCommand command = _Connection.CreateCommand();
                        command.Transaction = transaction;
                        command.CommandText = $@"
                            INSERT OR REPLACE INTO {_NodesTableName} (id, vector_blob, vector_dimension, metadata_json, updated_at)
                            VALUES (@id, @vectorBlob, @dimension, NULL, CURRENT_TIMESTAMP)";

                        foreach (KeyValuePair<Guid, List<float>> kvp in nodes)
                        {
                            firstNodeId ??= kvp.Key;
                            command.Parameters.Clear();
                            command.Parameters.AddWithValue("@id", kvp.Key.ToByteArray());
                            command.Parameters.AddWithValue("@vectorBlob", SerializeVector(kvp.Value));
                            command.Parameters.AddWithValue("@dimension", kvp.Value.Count);
                            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                        }

                        if (wasEmpty && firstNodeId.HasValue)
                        {
                            _EntryPoint = firstNodeId.Value;
                            await SaveEntryPointToDatabaseCoreAsync(transaction, cancellationToken).ConfigureAwait(false);
                        }

                        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                        throw;
                    }
                }
                finally
                {
                    _DatabaseLock.Release();
                }

                foreach (KeyValuePair<Guid, List<float>> kvp in nodes)
                {
                    _NodeCache[kvp.Key] = await CreateNodeAsync(kvp.Key, kvp.Value, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _StorageLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task RemoveNodeAsync(Guid id, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _StorageLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                RemoveCachedNodeUnsafe(id);

                await _DatabaseLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    using SqliteCommand deleteNodeCommand = _Connection.CreateCommand();
                    deleteNodeCommand.CommandText = $"DELETE FROM {_NodesTableName} WHERE id = @id";
                    deleteNodeCommand.Parameters.AddWithValue("@id", id.ToByteArray());
                    await deleteNodeCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                    using SqliteCommand deleteNeighborsCommand = _Connection.CreateCommand();
                    deleteNeighborsCommand.CommandText = $"DELETE FROM {_NeighborsTableName} WHERE node_id = @id";
                    deleteNeighborsCommand.Parameters.AddWithValue("@id", id.ToByteArray());
                    await deleteNeighborsCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                    await EnsureEntryPointLoadedCoreAsync(null, cancellationToken).ConfigureAwait(false);
                    if (_EntryPoint == id)
                    {
                        _EntryPoint = await SelectFirstNodeIdCoreAsync(null, cancellationToken).ConfigureAwait(false);
                        await SaveEntryPointToDatabaseCoreAsync(null, cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _DatabaseLock.Release();
                }
            }
            finally
            {
                _StorageLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task RemoveNodesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        {
            if (ids == null) throw new ArgumentNullException(nameof(ids));
            List<Guid> idList = ids.Where(id => id != Guid.Empty).Distinct().ToList();
            ThrowIfDisposed();

            if (idList.Count == 0)
            {
                return;
            }

            await _StorageLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (Guid id in idList)
                {
                    RemoveCachedNodeUnsafe(id);
                }

                await _DatabaseLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await using SqliteTransaction transaction = (SqliteTransaction)await _Connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        using SqliteCommand deleteNodeCommand = _Connection.CreateCommand();
                        deleteNodeCommand.Transaction = transaction;
                        deleteNodeCommand.CommandText = $"DELETE FROM {_NodesTableName} WHERE id = @id";

                        using SqliteCommand deleteNeighborsCommand = _Connection.CreateCommand();
                        deleteNeighborsCommand.Transaction = transaction;
                        deleteNeighborsCommand.CommandText = $"DELETE FROM {_NeighborsTableName} WHERE node_id = @id";

                        foreach (Guid id in idList)
                        {
                            byte[] idBytes = id.ToByteArray();
                            deleteNodeCommand.Parameters.Clear();
                            deleteNodeCommand.Parameters.AddWithValue("@id", idBytes);
                            await deleteNodeCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                            deleteNeighborsCommand.Parameters.Clear();
                            deleteNeighborsCommand.Parameters.AddWithValue("@id", idBytes);
                            await deleteNeighborsCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                        }

                        await EnsureEntryPointLoadedCoreAsync(transaction, cancellationToken).ConfigureAwait(false);
                        if (_EntryPoint.HasValue && idList.Contains(_EntryPoint.Value))
                        {
                            _EntryPoint = await SelectFirstNodeIdCoreAsync(transaction, cancellationToken).ConfigureAwait(false);
                            await SaveEntryPointToDatabaseCoreAsync(transaction, cancellationToken).ConfigureAwait(false);
                        }

                        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                        throw;
                    }
                }
                finally
                {
                    _DatabaseLock.Release();
                }
            }
            finally
            {
                _StorageLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task<IHnswNode> GetNodeAsync(Guid id, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _StorageLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_NodeCache.TryGetValue(id, out SqliteHnswNode? cachedNode))
                {
                    return cachedNode;
                }

                List<float> vector = await LoadVectorAsync(id, cancellationToken).ConfigureAwait(false);
                SqliteHnswNode node = await CreateNodeAsync(id, vector, cancellationToken).ConfigureAwait(false);
                _NodeCache[id] = node;
                return node;
            }
            finally
            {
                _StorageLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task<Dictionary<Guid, IHnswNode>> GetNodesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        {
            if (ids == null) throw new ArgumentNullException(nameof(ids));
            ThrowIfDisposed();

            List<Guid> requestedIds = ids.Distinct().ToList();
            Dictionary<Guid, IHnswNode> result = new Dictionary<Guid, IHnswNode>();
            if (requestedIds.Count == 0)
            {
                return result;
            }

            await _StorageLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                List<Guid> idsToLoad = new List<Guid>();
                foreach (Guid id in requestedIds)
                {
                    if (_NodeCache.TryGetValue(id, out SqliteHnswNode? cachedNode))
                    {
                        result[id] = cachedNode;
                    }
                    else
                    {
                        idsToLoad.Add(id);
                    }
                }

                if (idsToLoad.Count == 0)
                {
                    return result;
                }

                Dictionary<Guid, List<float>> loadedVectors = await LoadVectorsAsync(idsToLoad, cancellationToken).ConfigureAwait(false);
                foreach (KeyValuePair<Guid, List<float>> kvp in loadedVectors)
                {
                    SqliteHnswNode node = await CreateNodeAsync(kvp.Key, kvp.Value, cancellationToken).ConfigureAwait(false);
                    _NodeCache[kvp.Key] = node;
                    result[kvp.Key] = node;
                }

                return result;
            }
            finally
            {
                _StorageLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task<TryGetNodeResult> TryGetNodeAsync(Guid id, CancellationToken cancellationToken = default)
        {
            try
            {
                IHnswNode node = await GetNodeAsync(id, cancellationToken).ConfigureAwait(false);
                return TryGetNodeResult.Found(node);
            }
            catch (KeyNotFoundException)
            {
                return TryGetNodeResult.NotFound();
            }
        }

        /// <inheritdoc />
        public async Task<IEnumerable<Guid>> GetAllNodeIdsAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            List<Guid> nodeIds = new List<Guid>();
            await _DatabaseLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using SqliteCommand command = _Connection.CreateCommand();
                command.CommandText = $"SELECT id FROM {_NodesTableName}";

                using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    nodeIds.Add(new Guid((byte[])reader[0]));
                }
            }
            finally
            {
                _DatabaseLock.Release();
            }

            return nodeIds;
        }

        /// <summary>
        /// Flushes any cached node changes.
        /// </summary>
        public async Task FlushAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            List<SqliteHnswNode> nodes;
            await _StorageLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                nodes = _NodeCache.Values.ToList();
            }
            finally
            {
                _StorageLock.Release();
            }

            foreach (SqliteHnswNode node in nodes)
            {
                await node.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_Disposed) return;
            try
            {
                await FlushAsync().ConfigureAwait(false);
            }
            catch
            {
                // Ignore dispose-time flush failures.
            }

            DisposeCore();
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            DisposeCore();
            GC.SuppressFinalize(this);
        }

        private static async Task<SqliteConnection> OpenAndConfigureConnectionAsync(
            string databasePath,
            SemaphoreSlim databaseLock,
            CancellationToken cancellationToken)
        {
            string connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = true,
            }.ToString();

            SqliteConnection connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await databaseLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ApplyPragmaAsync(connection, "PRAGMA journal_mode=WAL", cancellationToken).ConfigureAwait(false);
                await ApplyPragmaAsync(connection, "PRAGMA synchronous=FULL", cancellationToken).ConfigureAwait(false);
                await ApplyPragmaAsync(connection, "PRAGMA cache_size=10000", cancellationToken).ConfigureAwait(false);
                await ApplyPragmaAsync(connection, "PRAGMA temp_store=MEMORY", cancellationToken).ConfigureAwait(false);
                await ApplyPragmaAsync(connection, "PRAGMA mmap_size=268435456", cancellationToken).ConfigureAwait(false);
                await ApplyPragmaAsync(connection, "PRAGMA wal_autocheckpoint=1000", cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                databaseLock.Release();
            }

            return connection;
        }

        private static async Task ApplyPragmaAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task InitializeDatabaseAsync(CancellationToken cancellationToken)
        {
            await _DatabaseLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using SqliteCommand createNodesTableCommand = _Connection.CreateCommand();
                createNodesTableCommand.CommandText = $@"
                    CREATE TABLE IF NOT EXISTS {_NodesTableName} (
                        id BLOB PRIMARY KEY,
                        vector_blob BLOB NOT NULL,
                        vector_dimension INTEGER NOT NULL,
                        metadata_json TEXT,
                        created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                        updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
                    )";
                await createNodesTableCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                using SqliteCommand addMetaCol = _Connection.CreateCommand();
                addMetaCol.CommandText = $"ALTER TABLE {_NodesTableName} ADD COLUMN metadata_json TEXT";
                try
                {
                    await addMetaCol.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (SqliteException)
                {
                    // Column already exists.
                }

                using SqliteCommand createNeighborsTableCommand = _Connection.CreateCommand();
                createNeighborsTableCommand.CommandText = $@"
                    CREATE TABLE IF NOT EXISTS {_NeighborsTableName} (
                        node_id BLOB PRIMARY KEY,
                        neighbors_blob BLOB,
                        updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
                    )";
                await createNeighborsTableCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                using SqliteCommand createMetadataTableCommand = _Connection.CreateCommand();
                createMetadataTableCommand.CommandText = $@"
                    CREATE TABLE IF NOT EXISTS {_MetadataTableName} (
                        key TEXT PRIMARY KEY,
                        value TEXT,
                        updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
                    )";
                await createMetadataTableCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                using SqliteCommand createNodeIndexCommand = _Connection.CreateCommand();
                createNodeIndexCommand.CommandText = $"CREATE INDEX IF NOT EXISTS idx_{_NodesTableName}_id ON {_NodesTableName}(id)";
                await createNodeIndexCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                using SqliteCommand createNeighborIndexCommand = _Connection.CreateCommand();
                createNeighborIndexCommand.CommandText = $"CREATE INDEX IF NOT EXISTS idx_{_NeighborsTableName}_node_id ON {_NeighborsTableName}(node_id)";
                await createNeighborIndexCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _DatabaseLock.Release();
            }
        }

        private async Task<List<float>> LoadVectorAsync(Guid id, CancellationToken cancellationToken)
        {
            await _DatabaseLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using SqliteCommand command = _Connection.CreateCommand();
                command.CommandText = $"SELECT vector_blob FROM {_NodesTableName} WHERE id = @id";
                command.Parameters.AddWithValue("@id", id.ToByteArray());

                object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                if (result is not byte[] vectorBlob)
                {
                    throw new KeyNotFoundException($"Node with ID {id} not found in storage.");
                }

                return DeserializeVector(vectorBlob);
            }
            finally
            {
                _DatabaseLock.Release();
            }
        }

        private async Task<Dictionary<Guid, List<float>>> LoadVectorsAsync(List<Guid> idsToLoad, CancellationToken cancellationToken)
        {
            Dictionary<Guid, List<float>> result = new Dictionary<Guid, List<float>>();
            string placeholders = string.Join(",", idsToLoad.Select((_, i) => $"@id{i}"));

            await _DatabaseLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using SqliteCommand command = _Connection.CreateCommand();
                command.CommandText = $"SELECT id, vector_blob FROM {_NodesTableName} WHERE id IN ({placeholders})";
                for (int i = 0; i < idsToLoad.Count; i++)
                {
                    command.Parameters.AddWithValue($"@id{i}", idsToLoad[i].ToByteArray());
                }

                using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    Guid nodeId = new Guid((byte[])reader[0]);
                    result[nodeId] = DeserializeVector((byte[])reader[1]);
                }
            }
            finally
            {
                _DatabaseLock.Release();
            }

            return result;
        }

        private async Task<SqliteHnswNode> CreateNodeAsync(Guid id, List<float> vector, CancellationToken cancellationToken)
        {
            return await SqliteHnswNode.CreateAsync(
                id,
                vector,
                _Connection,
                _DatabaseLock,
                _NeighborsTableName,
                _NodesTableName,
                cancellationToken).ConfigureAwait(false);
        }

        private async Task EnsureEntryPointLoadedUnsafeAsync(CancellationToken cancellationToken)
        {
            await _DatabaseLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await EnsureEntryPointLoadedCoreAsync(null, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _DatabaseLock.Release();
            }
        }

        private async Task EnsureEntryPointLoadedCoreAsync(SqliteTransaction? transaction, CancellationToken cancellationToken)
        {
            if (_EntryPointLoaded)
            {
                return;
            }

            _EntryPoint = await LoadEntryPointFromDatabaseCoreAsync(transaction, cancellationToken).ConfigureAwait(false);
            _EntryPointLoaded = true;
        }

        private async Task<Guid?> LoadEntryPointFromDatabaseCoreAsync(SqliteTransaction? transaction, CancellationToken cancellationToken)
        {
            using SqliteCommand command = _Connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"SELECT value FROM {_MetadataTableName} WHERE key = 'entry_point'";
            object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            return result != null
                   && result != DBNull.Value
                   && Guid.TryParse(result.ToString(), out Guid entryPoint)
                ? entryPoint
                : null;
        }

        private async Task SaveEntryPointToDatabaseCoreAsync(SqliteTransaction? transaction, CancellationToken cancellationToken)
        {
            using SqliteCommand command = _Connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"
                INSERT OR REPLACE INTO {_MetadataTableName} (key, value, updated_at)
                VALUES ('entry_point', @value, CURRENT_TIMESTAMP)";
            command.Parameters.AddWithValue("@value", _EntryPoint?.ToString() ?? string.Empty);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task<bool> NodeExistsInDatabaseCoreAsync(Guid id, SqliteTransaction? transaction, CancellationToken cancellationToken)
        {
            using SqliteCommand command = _Connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"SELECT COUNT(*) FROM {_NodesTableName} WHERE id = @id";
            command.Parameters.AddWithValue("@id", id.ToByteArray());
            object? count = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToInt32(count) > 0;
        }

        private async Task<Guid?> SelectFirstNodeIdCoreAsync(SqliteTransaction? transaction, CancellationToken cancellationToken)
        {
            using SqliteCommand command = _Connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"SELECT id FROM {_NodesTableName} LIMIT 1";
            object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is byte[] idBytes ? new Guid(idBytes) : null;
        }

        private void RemoveCachedNodeUnsafe(Guid id)
        {
            if (_NodeCache.TryGetValue(id, out SqliteHnswNode? existingNode))
            {
                existingNode.Dispose();
                _NodeCache.Remove(id);
            }
        }

        private void DisposeCore()
        {
            if (_Disposed) return;
            _Disposed = true;

            foreach (SqliteHnswNode node in _NodeCache.Values)
            {
                node.Dispose();
            }

            _NodeCache.Clear();
            _Connection.Dispose();
            _StorageLock.Dispose();
            if (_OwnsDatabaseLock)
            {
                _DatabaseLock.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_Disposed)
            {
                throw new ObjectDisposedException(nameof(SqliteHnswStorage));
            }
        }

        private static void ValidateNode(Guid id, List<float> vector)
        {
            if (id == Guid.Empty) throw new ArgumentException("Id cannot be Guid.Empty.", nameof(id));
            if (vector == null) throw new ArgumentNullException(nameof(vector));
            if (vector.Count == 0) throw new ArgumentException("Vector cannot be empty.", nameof(vector));
            for (int i = 0; i < vector.Count; i++)
            {
                if (float.IsNaN(vector[i]) || float.IsInfinity(vector[i]))
                {
                    throw new ArgumentException($"Vector contains invalid value at index {i}. All values must be finite.", nameof(vector));
                }
            }
        }

        private static byte[] SerializeVector(List<float> vector)
        {
            byte[] result = new byte[4 + vector.Count * 4];
            BitConverter.TryWriteBytes(result.AsSpan(0, 4), vector.Count);
            ReadOnlySpan<float> src = CollectionsMarshal.AsSpan(vector);
            MemoryMarshal.AsBytes(src).CopyTo(result.AsSpan(4));
            return result;
        }

        private static List<float> DeserializeVector(byte[] bytes)
        {
            int count = BitConverter.ToInt32(bytes, 0);
            ReadOnlySpan<float> floats = MemoryMarshal.Cast<byte, float>(bytes.AsSpan(4, count * 4));
            List<float> vector = new List<float>(count);
            for (int i = 0; i < count; i++)
            {
                vector.Add(floats[i]);
            }

            return vector;
        }
    }
}
