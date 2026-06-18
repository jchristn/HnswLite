namespace Hnsw.SqliteStorage
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hnsw;
    using Microsoft.Data.Sqlite;

    /// <summary>
    /// SQLite-backed implementation of HNSW layer storage.
    /// </summary>
    public sealed class SqliteHnswLayerStorage : IHnswLayerStorage, IDisposable
    {
        private readonly SqliteConnection _Connection;
        private readonly SemaphoreSlim _DatabaseLock;
        private readonly SemaphoreSlim _LayersLock = new SemaphoreSlim(1, 1);
        private readonly Dictionary<Guid, int> _LayerCache = new Dictionary<Guid, int>();
        private readonly string _TableName;
        private bool _Disposed;
        private bool _CacheLoaded;

        private SqliteHnswLayerStorage(SqliteConnection connection, SemaphoreSlim databaseLock, string tableName)
        {
            _Connection = connection;
            _DatabaseLock = databaseLock;
            _TableName = tableName;
        }

        /// <summary>
        /// Gets whether the storage has been disposed.
        /// </summary>
        public bool IsDisposed => _Disposed;

        /// <summary>
        /// Gets the database table name used for layer storage.
        /// </summary>
        public string TableName => _TableName;

        /// <summary>
        /// Creates layer storage and initializes the backing table asynchronously.
        /// </summary>
        public static async Task<SqliteHnswLayerStorage> CreateAsync(
            SqliteConnection connection,
            SemaphoreSlim databaseLock,
            string tableName = "hnsw_node_layers",
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(connection, nameof(connection));
            ArgumentNullException.ThrowIfNull(databaseLock, nameof(databaseLock));
            if (string.IsNullOrWhiteSpace(tableName))
            {
                throw new ArgumentException("Table name cannot be null or empty.", nameof(tableName));
            }

            SqliteHnswLayerStorage storage = new SqliteHnswLayerStorage(connection, databaseLock, tableName);
            await storage.InitializeTableAsync(cancellationToken).ConfigureAwait(false);
            return storage;
        }

        /// <inheritdoc />
        public async Task<int> GetNodeLayerAsync(Guid nodeId, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _LayersLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await EnsureCacheLoadedUnsafeAsync(cancellationToken).ConfigureAwait(false);
                return _LayerCache.TryGetValue(nodeId, out int layer) ? layer : 0;
            }
            finally
            {
                _LayersLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task SetNodeLayerAsync(Guid nodeId, int layer, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (nodeId == Guid.Empty) throw new ArgumentException("NodeId cannot be Guid.Empty.", nameof(nodeId));
            if (layer < 0) throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot be negative.");
            if (layer > 63) throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot exceed 63.");

            await _LayersLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _DatabaseLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    using SqliteCommand command = _Connection.CreateCommand();
                    command.CommandText = $@"
                        INSERT OR REPLACE INTO {_TableName} (node_id, layer, updated_at)
                        VALUES (@nodeId, @layer, CURRENT_TIMESTAMP)";
                    command.Parameters.AddWithValue("@nodeId", nodeId.ToString());
                    command.Parameters.AddWithValue("@layer", layer);
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _DatabaseLock.Release();
                }

                if (_CacheLoaded)
                {
                    _LayerCache[nodeId] = layer;
                }
            }
            finally
            {
                _LayersLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task RemoveNodeLayerAsync(Guid nodeId, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _LayersLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _DatabaseLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    using SqliteCommand command = _Connection.CreateCommand();
                    command.CommandText = $"DELETE FROM {_TableName} WHERE node_id = @nodeId";
                    command.Parameters.AddWithValue("@nodeId", nodeId.ToString());
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _DatabaseLock.Release();
                }

                if (_CacheLoaded)
                {
                    _LayerCache.Remove(nodeId);
                }
            }
            finally
            {
                _LayersLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task<Dictionary<Guid, int>> GetAllNodeLayersAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _LayersLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await EnsureCacheLoadedUnsafeAsync(cancellationToken).ConfigureAwait(false);
                return new Dictionary<Guid, int>(_LayerCache);
            }
            finally
            {
                _LayersLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task ClearLayersAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _LayersLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _DatabaseLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    using SqliteCommand command = _Connection.CreateCommand();
                    command.CommandText = $"DELETE FROM {_TableName}";
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _DatabaseLock.Release();
                }

                _LayerCache.Clear();
                _CacheLoaded = true;
            }
            finally
            {
                _LayersLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task<int> GetLayerCountAsync(CancellationToken cancellationToken = default)
        {
            Dictionary<Guid, int> layers = await GetAllNodeLayersAsync(cancellationToken).ConfigureAwait(false);
            return layers.Count;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            _LayerCache.Clear();
            _LayersLock.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_Disposed)
            {
                throw new ObjectDisposedException(nameof(SqliteHnswLayerStorage));
            }
        }

        private async Task InitializeTableAsync(CancellationToken cancellationToken)
        {
            await _DatabaseLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using SqliteCommand command = _Connection.CreateCommand();
                command.CommandText = $@"
                    CREATE TABLE IF NOT EXISTS {_TableName} (
                        node_id TEXT PRIMARY KEY,
                        layer INTEGER NOT NULL,
                        updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
                    )";
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                using SqliteCommand indexCommand = _Connection.CreateCommand();
                indexCommand.CommandText = $"CREATE INDEX IF NOT EXISTS idx_{_TableName}_node_id ON {_TableName}(node_id)";
                await indexCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _DatabaseLock.Release();
            }
        }

        private async Task EnsureCacheLoadedUnsafeAsync(CancellationToken cancellationToken)
        {
            if (_CacheLoaded)
            {
                return;
            }

            _LayerCache.Clear();

            await _DatabaseLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using SqliteCommand command = _Connection.CreateCommand();
                command.CommandText = $"SELECT node_id, layer FROM {_TableName}";

                using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (Guid.TryParse(reader.GetString(0), out Guid nodeId))
                    {
                        _LayerCache[nodeId] = reader.GetInt32(1);
                    }
                }
            }
            finally
            {
                _DatabaseLock.Release();
            }

            _CacheLoaded = true;
        }
    }
}
