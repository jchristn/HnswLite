namespace HnswIndex.SqliteStorage
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
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
        private const string _OperationOpen = "Open";
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
            Activity? activity = HnswTelemetry.StartStorageActivity(HnswTelemetryNames.ProviderSqlite, _OperationOpen);
            long startTimestamp = HnswTelemetry.GetTimestamp();
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

                HnswTelemetry.RecordStorageOperation(HnswTelemetryNames.ProviderSqlite, _OperationOpen, startTimestamp);
                HnswTelemetry.CompleteActivity(activity, null);
                return new SqliteStorageProvider(storage, layerStorage, databaseLock);
            }
            catch (Exception e)
            {
                HnswTelemetry.RecordStorageOperation(HnswTelemetryNames.ProviderSqlite, _OperationOpen, startTimestamp, e);
                HnswTelemetry.CompleteActivity(activity, e);
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
            Activity? activity = HnswTelemetry.StartStorageActivity(HnswTelemetryNames.ProviderSqlite, _OperationOpen);
            long startTimestamp = HnswTelemetry.GetTimestamp();
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

                HnswTelemetry.RecordStorageOperation(HnswTelemetryNames.ProviderSqlite, _OperationOpen, startTimestamp);
                HnswTelemetry.CompleteActivity(activity, null);
                return new SqliteStorageProvider(storage, layerStorage, databaseLock);
            }
            catch (Exception e)
            {
                HnswTelemetry.RecordStorageOperation(HnswTelemetryNames.ProviderSqlite, _OperationOpen, startTimestamp, e);
                HnswTelemetry.CompleteActivity(activity, e);
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
            if (!IsObserved(false)) return _Storage.AddNodeAsync(id, vector, cancellationToken);
            return ObserveAsync("AddNode", false, () => _Storage.AddNodeAsync(id, vector, cancellationToken));
        }

        /// <inheritdoc />
        public Task AddNodesAsync(Dictionary<Guid, List<float>> nodes, CancellationToken cancellationToken = default)
        {
            if (!IsObserved(true)) return _Storage.AddNodesAsync(nodes, cancellationToken);
            return ObserveAsync("AddNodes", true, () => _Storage.AddNodesAsync(nodes, cancellationToken));
        }

        /// <inheritdoc />
        public Task RemoveNodeAsync(Guid id, CancellationToken cancellationToken = default)
        {
            if (!IsObserved(false)) return _Storage.RemoveNodeAsync(id, cancellationToken);
            return ObserveAsync("RemoveNode", false, () => _Storage.RemoveNodeAsync(id, cancellationToken));
        }

        /// <inheritdoc />
        public Task RemoveNodesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        {
            if (!IsObserved(true)) return _Storage.RemoveNodesAsync(ids, cancellationToken);
            return ObserveAsync("RemoveNodes", true, () => _Storage.RemoveNodesAsync(ids, cancellationToken));
        }

        /// <inheritdoc />
        public Task<IHnswNode> GetNodeAsync(Guid id, CancellationToken cancellationToken = default)
        {
            if (!IsObserved(false)) return _Storage.GetNodeAsync(id, cancellationToken);
            return ObserveAsync("GetNode", false, () => _Storage.GetNodeAsync(id, cancellationToken));
        }

        /// <inheritdoc />
        public Task<Dictionary<Guid, IHnswNode>> GetNodesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        {
            if (!IsObserved(false)) return _Storage.GetNodesAsync(ids, cancellationToken);
            return ObserveAsync("GetNodes", false, () => _Storage.GetNodesAsync(ids, cancellationToken));
        }

        /// <inheritdoc />
        public Task<TryGetNodeResult> TryGetNodeAsync(Guid id, CancellationToken cancellationToken = default)
        {
            if (!IsObserved(false)) return _Storage.TryGetNodeAsync(id, cancellationToken);
            return ObserveAsync("TryGetNode", false, () => _Storage.TryGetNodeAsync(id, cancellationToken));
        }

        /// <inheritdoc />
        public Task<IEnumerable<Guid>> GetAllNodeIdsAsync(CancellationToken cancellationToken = default)
        {
            if (!IsObserved(true)) return _Storage.GetAllNodeIdsAsync(cancellationToken);
            return ObserveAsync("GetAllNodeIds", true, () => _Storage.GetAllNodeIdsAsync(cancellationToken));
        }

        /// <inheritdoc />
        public Task<int> GetCountAsync(CancellationToken cancellationToken = default)
        {
            if (!IsObserved(false)) return _Storage.GetCountAsync(cancellationToken);
            return ObserveAsync("GetCount", false, () => _Storage.GetCountAsync(cancellationToken));
        }

        /// <inheritdoc />
        public Task<Guid?> GetEntryPointAsync(CancellationToken cancellationToken = default)
        {
            if (!IsObserved(false)) return _Storage.GetEntryPointAsync(cancellationToken);
            return ObserveAsync("GetEntryPoint", false, () => _Storage.GetEntryPointAsync(cancellationToken));
        }

        /// <inheritdoc />
        public Task SetEntryPointAsync(Guid? entryPoint, CancellationToken cancellationToken = default)
        {
            if (!IsObserved(false)) return _Storage.SetEntryPointAsync(entryPoint, cancellationToken);
            return ObserveAsync("SetEntryPoint", false, () => _Storage.SetEntryPointAsync(entryPoint, cancellationToken));
        }

        /// <inheritdoc />
        public Task<int> GetNodeLayerAsync(Guid nodeId, CancellationToken cancellationToken = default)
        {
            if (!IsObserved(false)) return _LayerStorage.GetNodeLayerAsync(nodeId, cancellationToken);
            return ObserveAsync("GetNodeLayer", false, () => _LayerStorage.GetNodeLayerAsync(nodeId, cancellationToken));
        }

        /// <inheritdoc />
        public Task SetNodeLayerAsync(Guid nodeId, int layer, CancellationToken cancellationToken = default)
        {
            if (!IsObserved(false)) return _LayerStorage.SetNodeLayerAsync(nodeId, layer, cancellationToken);
            return ObserveAsync("SetNodeLayer", false, () => _LayerStorage.SetNodeLayerAsync(nodeId, layer, cancellationToken));
        }

        /// <inheritdoc />
        public Task RemoveNodeLayerAsync(Guid nodeId, CancellationToken cancellationToken = default)
        {
            if (!IsObserved(false)) return _LayerStorage.RemoveNodeLayerAsync(nodeId, cancellationToken);
            return ObserveAsync("RemoveNodeLayer", false, () => _LayerStorage.RemoveNodeLayerAsync(nodeId, cancellationToken));
        }

        /// <inheritdoc />
        public Task<Dictionary<Guid, int>> GetAllNodeLayersAsync(CancellationToken cancellationToken = default)
        {
            if (!IsObserved(true)) return _LayerStorage.GetAllNodeLayersAsync(cancellationToken);
            return ObserveAsync("GetAllNodeLayers", true, () => _LayerStorage.GetAllNodeLayersAsync(cancellationToken));
        }

        /// <inheritdoc />
        public Task ClearLayersAsync(CancellationToken cancellationToken = default)
        {
            if (!IsObserved(true)) return _LayerStorage.ClearLayersAsync(cancellationToken);
            return ObserveAsync("ClearLayers", true, () => _LayerStorage.ClearLayersAsync(cancellationToken));
        }

        /// <inheritdoc />
        public Task<int> GetLayerCountAsync(CancellationToken cancellationToken = default)
        {
            if (!IsObserved(false)) return _LayerStorage.GetLayerCountAsync(cancellationToken);
            return ObserveAsync("GetLayerCount", false, () => _LayerStorage.GetLayerCountAsync(cancellationToken));
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
            if (!IsObserved(true)) return _Storage.FlushAsync(cancellationToken);
            return ObserveAsync("Flush", true, () => _Storage.FlushAsync(cancellationToken));
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

        private static bool IsObserved(bool span)
        {
            return HnswTelemetry.IsStorageObserved || (span && HnswTelemetry.ActivitySource.HasListeners());
        }

        private static async Task ObserveAsync(string operation, bool span, Func<Task> action)
        {
            Activity? activity = span ? HnswTelemetry.StartStorageActivity(HnswTelemetryNames.ProviderSqlite, operation) : null;
            long startTimestamp = HnswTelemetry.GetTimestamp();
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                HnswTelemetry.RecordStorageOperation(HnswTelemetryNames.ProviderSqlite, operation, startTimestamp, e);
                HnswTelemetry.CompleteActivity(activity, e);
                throw;
            }

            HnswTelemetry.RecordStorageOperation(HnswTelemetryNames.ProviderSqlite, operation, startTimestamp);
            HnswTelemetry.CompleteActivity(activity, null);
        }

        private static async Task<T> ObserveAsync<T>(string operation, bool span, Func<Task<T>> action)
        {
            Activity? activity = span ? HnswTelemetry.StartStorageActivity(HnswTelemetryNames.ProviderSqlite, operation) : null;
            long startTimestamp = HnswTelemetry.GetTimestamp();
            T result;
            try
            {
                result = await action().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                HnswTelemetry.RecordStorageOperation(HnswTelemetryNames.ProviderSqlite, operation, startTimestamp, e);
                HnswTelemetry.CompleteActivity(activity, e);
                throw;
            }

            HnswTelemetry.RecordStorageOperation(HnswTelemetryNames.ProviderSqlite, operation, startTimestamp);
            HnswTelemetry.CompleteActivity(activity, null);
            return result;
        }
    }
}
