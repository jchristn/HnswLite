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
    /// Unified SQLite storage provider that combines vector node storage and
    /// layer assignment storage into a single <see cref="IStorageProvider"/>.
    /// Thread-safe. Uses a single database file and connection.
    /// Disposes both backing stores on disposal.
    /// </summary>
    public class SqliteStorageProvider : IStorageProvider
    {
        #region Public-Members

        /// <summary>
        /// Gets or sets the entry point node ID.
        /// </summary>
        public Guid? EntryPoint
        {
            get { return _Storage.EntryPoint; }
            set { _Storage.EntryPoint = value; }
        }

        /// <summary>
        /// Number of nodes with layer assignments.
        /// </summary>
        public int Count => _LayerStorage.Count;

        /// <summary>
        /// Path to the SQLite database file.
        /// </summary>
        public string DatabasePath => _Storage.DatabasePath;

        /// <summary>
        /// The underlying SQLite connection (shared between node and layer storage).
        /// </summary>
        public SqliteConnection Connection => _Storage.Connection;

        #endregion

        #region Private-Members

        private readonly SqliteHnswStorage _Storage;
        private readonly SqliteHnswLayerStorage _LayerStorage;
        private bool _Disposed;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new SQLite storage provider with default table names.
        /// </summary>
        /// <param name="databasePath">Path to the SQLite database file.</param>
        /// <param name="createIfNotExists">Create the database and tables if they don't exist. Default: true.</param>
        public SqliteStorageProvider(string databasePath, bool createIfNotExists = true)
        {
            _Storage = new SqliteHnswStorage(databasePath, createIfNotExists);
            _LayerStorage = new SqliteHnswLayerStorage(_Storage.Connection);
        }

        /// <summary>
        /// Initializes a new SQLite storage provider with custom table names.
        /// </summary>
        /// <param name="databasePath">Path to the SQLite database file.</param>
        /// <param name="nodesTableName">Table name for vector nodes.</param>
        /// <param name="neighborsTableName">Table name for neighbor relationships.</param>
        /// <param name="metadataTableName">Table name for metadata.</param>
        /// <param name="layersTableName">Table name for layer assignments.</param>
        /// <param name="createIfNotExists">Create the database and tables if they don't exist. Default: true.</param>
        public SqliteStorageProvider(
            string databasePath,
            string nodesTableName,
            string neighborsTableName,
            string metadataTableName,
            string layersTableName = "hnsw_node_layers",
            bool createIfNotExists = true)
        {
            _Storage = new SqliteHnswStorage(databasePath, nodesTableName, neighborsTableName, metadataTableName, createIfNotExists);
            _LayerStorage = new SqliteHnswLayerStorage(_Storage.Connection, layersTableName);
        }

        #endregion

        #region Public-Methods

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
        public int GetNodeLayer(Guid nodeId)
        {
            return _LayerStorage.GetNodeLayer(nodeId);
        }

        /// <inheritdoc />
        public void SetNodeLayer(Guid nodeId, int layer)
        {
            _LayerStorage.SetNodeLayer(nodeId, layer);
        }

        /// <inheritdoc />
        public void RemoveNodeLayer(Guid nodeId)
        {
            _LayerStorage.RemoveNodeLayer(nodeId);
        }

        /// <inheritdoc />
        public Dictionary<Guid, int> GetAllNodeLayers()
        {
            return _LayerStorage.GetAllNodeLayers();
        }

        /// <inheritdoc />
        public void Clear()
        {
            _Storage.Clear();
            _LayerStorage.Clear();
        }

        /// <summary>
        /// Disposes both the node storage and layer storage.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Disposes managed resources.
        /// </summary>
        /// <param name="disposing">True when called from Dispose().</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                _LayerStorage.Dispose();
                _Storage.Dispose();
            }
            _Disposed = true;
        }

        #endregion
    }
}
