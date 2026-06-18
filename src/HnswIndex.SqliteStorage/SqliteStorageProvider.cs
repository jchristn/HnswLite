namespace HnswIndex.SqliteStorage
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hnsw;
    using Hnsw.SqliteStorage;
    using Microsoft.Data.Sqlite;

    /// <summary>
    /// Unified SQLite storage provider that combines vector node storage and layer assignment storage.
    /// </summary>
    public sealed class SqliteStorageProvider : IStorageProvider
    {
        private readonly SqliteHnswStorage _Storage;
        private readonly SqliteHnswLayerStorage _LayerStorage;
        private readonly SemaphoreSlim _DatabaseLock;
        private bool _Disposed;

        private SqliteStorageProvider(
            SqliteHnswStorage storage,
            SqliteHnswLayerStorage layerStorage,
            SemaphoreSlim databaseLock)
        {
            _Storage = storage;
            _LayerStorage = layerStorage;
            _DatabaseLock = databaseLock;
        }

        /// <summary>
        /// Path to the SQLite database file.
        /// </summary>
        public string DatabasePath => _Storage.DatabasePath;

        /// <summary>
        /// The underlying SQLite connection shared between node and layer storage.
        /// </summary>
        public SqliteConnection Connection => _Storage.Connection;

        /// <summary>
        /// Creates a SQLite storage provider with default table names.
        /// </summary>
        public static async Task<SqliteStorageProvider> CreateAsync(
            string databasePath,
            bool createIfNotExists = true,
            CancellationToken cancellationToken = default)
        {
            SemaphoreSlim databaseLock = new SemaphoreSlim(1, 1);
            SqliteHnswStorage? storage = null;
            try
            {
                storage = await SqliteHnswStorage.CreateAsync(
                    databasePath,
                    createIfNotExists,
                    databaseLock,
                    cancellationToken).ConfigureAwait(false);

                SqliteHnswLayerStorage layerStorage = await SqliteHnswLayerStorage.CreateAsync(
                    storage.Connection,
                    databaseLock,
                    "hnsw_node_layers",
                    cancellationToken).ConfigureAwait(false);

                return new SqliteStorageProvider(storage, layerStorage, databaseLock);
            }
            catch
            {
                if (storage != null)
                {
                    await storage.DisposeAsync().ConfigureAwait(false);
                }

                databaseLock.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Creates a SQLite storage provider with custom table names.
        /// </summary>
        public static async Task<SqliteStorageProvider> CreateAsync(
            string databasePath,
            string nodesTableName,
            string neighborsTableName,
            string metadataTableName,
            string layersTableName = "hnsw_node_layers",
            bool createIfNotExists = true,
            CancellationToken cancellationToken = default)
        {
            SemaphoreSlim databaseLock = new SemaphoreSlim(1, 1);
            SqliteHnswStorage? storage = null;
            try
            {
                storage = await SqliteHnswStorage.CreateAsync(
                    databasePath,
                    nodesTableName,
                    neighborsTableName,
                    metadataTableName,
                    createIfNotExists,
                    databaseLock,
                    cancellationToken).ConfigureAwait(false);

                SqliteHnswLayerStorage layerStorage = await SqliteHnswLayerStorage.CreateAsync(
                    storage.Connection,
                    databaseLock,
                    layersTableName,
                    cancellationToken).ConfigureAwait(false);

                return new SqliteStorageProvider(storage, layerStorage, databaseLock);
            }
            catch
            {
                if (storage != null)
                {
                    await storage.DisposeAsync().ConfigureAwait(false);
                }

                databaseLock.Dispose();
                throw;
            }
        }

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
            return _Storage.FlushAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_Disposed) return;

            try
            {
                await FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                _LayerStorage.Dispose();
                await _Storage.DisposeAsync().ConfigureAwait(false);
                _DatabaseLock.Dispose();
                _Disposed = true;
                GC.SuppressFinalize(this);
            }
        }
    }
}
