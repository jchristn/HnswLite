namespace HnswIndex.PostgresqlStorage
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net.Sockets;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hnsw;
    using Npgsql;
    using NpgsqlTypes;

    /// <summary>
    /// PostgreSQL-backed implementation of HNSW storage.
    /// Multiple logical indexes share one schema and are isolated by the
    /// <c>hnsw_indexes.id</c> partition key stored on every node, layer, neighbor,
    /// and metadata row.
    /// </summary>
    public sealed class PostgresqlStorageProvider : IStorageProvider
    {
        private const string _ProviderName = HnswTelemetryNames.ProviderPostgresql;
        private static readonly HashSet<string> _SpanOperations = new HashSet<string>(StringComparer.Ordinal)
        {
            "AddNodes", "RemoveNodes", "GetAllNodeIds", "GetAllNodeLayers", "ClearLayers", "Clear"
        };

        private readonly NpgsqlDataSource _DataSource;
        private readonly Dictionary<Guid, PostgresqlHnswNode> _NodeCache = new Dictionary<Guid, PostgresqlHnswNode>();
        private readonly SemaphoreSlim _Lock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _TransactionCommandLock = new SemaphoreSlim(1, 1);
        private readonly bool _OwnsDataSource;
        private TransactionContext? _Transaction;
        private bool _Disposed;

        private PostgresqlStorageProvider(NpgsqlDataSource dataSource, Guid indexId, string indexName, bool ownsDataSource)
        {
            _DataSource = dataSource;
            IndexId = indexId;
            IndexName = indexName;
            _OwnsDataSource = ownsDataSource;
        }

        /// <summary>
        /// Gets the PostgreSQL index identifier.
        /// </summary>
        public Guid IndexId { get; }

        /// <summary>
        /// Gets the logical index name.
        /// </summary>
        public string IndexName { get; }

        /// <summary>
        /// Lists index metadata stored in PostgreSQL.
        /// </summary>
        public static async Task<List<PostgresqlIndexMetadata>> ListIndexesAsync(string connectionString, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentNullException(nameof(connectionString));
            try
            {
                await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);
                return await ListIndexesAsync(dataSource, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsPostgresqlDiagnosticException(ex))
            {
                throw CreateDiagnosticException("list PostgreSQL HNSW indexes", connectionString, ex);
            }
        }

        /// <summary>
        /// Lists index metadata using a caller-owned PostgreSQL data source.
        /// </summary>
        public static Task<List<PostgresqlIndexMetadata>> ListIndexesAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            return ObserveAsync("ListIndexes", true, () => ListIndexesCoreAsync(dataSource, cancellationToken));
        }

        private static async Task<List<PostgresqlIndexMetadata>> ListIndexesCoreAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
        {
            await EnsureSchemaAsync(dataSource, cancellationToken).ConfigureAwait(false);

            List<PostgresqlIndexMetadata> results = new List<PostgresqlIndexMetadata>();
            await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using NpgsqlCommand cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT i.id,
                       i.name,
                       i.dimension,
                       i.distance_function,
                       i.m,
                       i.max_m,
                       i.ef_construction,
                       i.created_at,
                       COUNT(n.id)::integer AS vector_count
                FROM hnsw_indexes i
                LEFT JOIN hnsw_nodes n ON n.index_id = i.id
                WHERE i.storage_type = 'PostgreSQL'
                GROUP BY i.id, i.name, i.dimension, i.distance_function, i.m, i.max_m, i.ef_construction, i.created_at
                ORDER BY i.name;";
            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(new PostgresqlIndexMetadata(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    reader.GetDateTime(7),
                    reader.GetInt32(8)));
            }

            return results;
        }

        /// <summary>
        /// Creates or opens a PostgreSQL storage provider.
        /// </summary>
        public static async Task<PostgresqlStorageProvider> CreateAsync(
            string connectionString,
            string indexName,
            int dimension = 0,
            string distanceFunction = "Euclidean",
            int m = 16,
            int maxM = 32,
            int efConstruction = 200,
            bool createIfNotExists = true,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentNullException(nameof(connectionString));
            if (string.IsNullOrWhiteSpace(indexName)) throw new ArgumentNullException(nameof(indexName));

            NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);
            try
            {
                await EnsureSchemaAsync(dataSource, cancellationToken).ConfigureAwait(false);
                Guid indexId = await EnsureIndexAsync(
                    dataSource,
                    indexName,
                    dimension,
                    distanceFunction,
                    m,
                    maxM,
                    efConstruction,
                    createIfNotExists,
                    cancellationToken).ConfigureAwait(false);
                return new PostgresqlStorageProvider(dataSource, indexId, indexName, ownsDataSource: true);
            }
            catch (Exception ex) when (IsPostgresqlDiagnosticException(ex))
            {
                await dataSource.DisposeAsync().ConfigureAwait(false);
                throw CreateDiagnosticException($"create or open PostgreSQL HNSW index '{indexName}'", connectionString, ex);
            }
            catch
            {
                await dataSource.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Creates or opens a PostgreSQL storage provider using a caller-owned data source.
        /// </summary>
        public static async Task<PostgresqlStorageProvider> CreateAsync(
            NpgsqlDataSource dataSource,
            string indexName,
            int dimension = 0,
            string distanceFunction = "Euclidean",
            int m = 16,
            int maxM = 32,
            int efConstruction = 200,
            bool createIfNotExists = true,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            if (string.IsNullOrWhiteSpace(indexName)) throw new ArgumentNullException(nameof(indexName));

            await EnsureSchemaAsync(dataSource, cancellationToken).ConfigureAwait(false);
            Guid indexId = await EnsureIndexAsync(
                dataSource,
                indexName,
                dimension,
                distanceFunction,
                m,
                maxM,
                efConstruction,
                createIfNotExists,
                cancellationToken).ConfigureAwait(false);

            return new PostgresqlStorageProvider(dataSource, indexId, indexName, ownsDataSource: false);
        }

        /// <inheritdoc />
        public async Task AddNodeAsync(Guid id, List<float> vector, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateNode(id, vector);

            await _Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ExecuteAsync(async (conn, tx) =>
                {
                    await using NpgsqlCommand cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
                        INSERT INTO hnsw_nodes (index_id, id, vector_blob, vector_dimension, metadata_json, updated_at)
                        VALUES (@index_id, @id, @vector_blob, @vector_dimension, NULL, now())
                        ON CONFLICT (index_id, id)
                        DO UPDATE SET vector_blob = EXCLUDED.vector_blob,
                                      vector_dimension = EXCLUDED.vector_dimension,
                                      metadata_json = NULL,
                                      updated_at = now();";
                    cmd.Parameters.AddWithValue("index_id", IndexId);
                    cmd.Parameters.AddWithValue("id", id);
                    cmd.Parameters.Add("vector_blob", NpgsqlDbType.Bytea).Value = SerializeVector(vector);
                    cmd.Parameters.AddWithValue("vector_dimension", vector.Count);
                    await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false);

                _NodeCache[id] = new PostgresqlHnswNode(this, id, vector, new Dictionary<int, HashSet<Guid>>(), null, null, null);

                Guid? entryPoint = await GetEntryPointAsync(cancellationToken).ConfigureAwait(false);
                if (!entryPoint.HasValue)
                {
                    await SetEntryPointAsync(id, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _Lock.Release();
            }
        }

        /// <inheritdoc />
        public async Task AddNodesAsync(Dictionary<Guid, List<float>> nodes, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(nodes, nameof(nodes));

            foreach (KeyValuePair<Guid, List<float>> kvp in nodes)
            {
                ValidateNode(kvp.Key, kvp.Value);
            }

            await _Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ExecuteAsync(async (conn, tx) =>
                {
                    foreach (KeyValuePair<Guid, List<float>> kvp in nodes)
                    {
                        await using NpgsqlCommand cmd = conn.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
                            INSERT INTO hnsw_nodes (index_id, id, vector_blob, vector_dimension, metadata_json, updated_at)
                            VALUES (@index_id, @id, @vector_blob, @vector_dimension, NULL, now())
                            ON CONFLICT (index_id, id)
                            DO UPDATE SET vector_blob = EXCLUDED.vector_blob,
                                          vector_dimension = EXCLUDED.vector_dimension,
                                          metadata_json = NULL,
                                          updated_at = now();";
                        cmd.Parameters.AddWithValue("index_id", IndexId);
                        cmd.Parameters.AddWithValue("id", kvp.Key);
                        cmd.Parameters.Add("vector_blob", NpgsqlDbType.Bytea).Value = SerializeVector(kvp.Value);
                        cmd.Parameters.AddWithValue("vector_dimension", kvp.Value.Count);
                        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                        _NodeCache[kvp.Key] = new PostgresqlHnswNode(this, kvp.Key, kvp.Value, new Dictionary<int, HashSet<Guid>>(), null, null, null);
                    }
                }, cancellationToken).ConfigureAwait(false);

                Guid? entryPoint = await GetEntryPointAsync(cancellationToken).ConfigureAwait(false);
                if (nodes.Count > 0 && !entryPoint.HasValue)
                {
                    await SetEntryPointAsync(nodes.Keys.First(), cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _Lock.Release();
            }
        }

        /// <inheritdoc />
        public async Task RemoveNodeAsync(Guid id, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ExecuteAsync(async (conn, tx) =>
                {
                    await using NpgsqlCommand cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = "DELETE FROM hnsw_nodes WHERE index_id = @index_id AND id = @id;";
                    cmd.Parameters.AddWithValue("index_id", IndexId);
                    cmd.Parameters.AddWithValue("id", id);
                    await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false);

                _NodeCache.Remove(id);
            }
            finally
            {
                _Lock.Release();
            }
        }

        /// <inheritdoc />
        public async Task RemoveNodesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(ids, nameof(ids));
            Guid[] idArray = ids.Distinct().ToArray();
            if (idArray.Length == 0) return;

            await _Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ExecuteAsync(async (conn, tx) =>
                {
                    await using NpgsqlCommand cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = "DELETE FROM hnsw_nodes WHERE index_id = @index_id AND id = ANY(@ids);";
                    cmd.Parameters.AddWithValue("index_id", IndexId);
                    cmd.Parameters.AddWithValue("ids", idArray);
                    await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false);

                foreach (Guid id in idArray) _NodeCache.Remove(id);
            }
            finally
            {
                _Lock.Release();
            }
        }

        /// <inheritdoc />
        public async Task<IHnswNode> GetNodeAsync(Guid id, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_NodeCache.TryGetValue(id, out PostgresqlHnswNode? cached))
                {
                    return cached;
                }
            }
            finally
            {
                _Lock.Release();
            }

            Dictionary<Guid, IHnswNode> nodes = await GetNodesAsync(new[] { id }, cancellationToken).ConfigureAwait(false);
            if (nodes.TryGetValue(id, out IHnswNode? node))
            {
                return node;
            }

            throw new KeyNotFoundException($"Node with ID {id} not found in PostgreSQL storage.");
        }

        /// <inheritdoc />
        public async Task<Dictionary<Guid, IHnswNode>> GetNodesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(ids, nameof(ids));

            Guid[] idArray = ids.Distinct().ToArray();
            Dictionary<Guid, IHnswNode> result = new Dictionary<Guid, IHnswNode>();
            List<Guid> missing = new List<Guid>();

            await _Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (Guid id in idArray)
                {
                    if (_NodeCache.TryGetValue(id, out PostgresqlHnswNode? cached))
                    {
                        result[id] = cached;
                    }
                    else
                    {
                        missing.Add(id);
                    }
                }
            }
            finally
            {
                _Lock.Release();
            }

            if (missing.Count == 0) return result;

            Dictionary<Guid, NodeRow> rows = new Dictionary<Guid, NodeRow>();
            await ExecuteAsync(async (conn, tx) =>
            {
                await using NpgsqlCommand cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT id, vector_blob, metadata_json FROM hnsw_nodes WHERE index_id = @index_id AND id = ANY(@ids);";
                cmd.Parameters.AddWithValue("index_id", IndexId);
                cmd.Parameters.AddWithValue("ids", missing.ToArray());
                await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    Guid nodeId = reader.GetGuid(0);
                    byte[] vectorBlob = (byte[])reader["vector_blob"];
                    string? metadataJson = reader["metadata_json"] == DBNull.Value ? null : reader["metadata_json"].ToString();
                    rows[nodeId] = new NodeRow(DeserializeVector(vectorBlob), metadataJson);
                }
            }, cancellationToken).ConfigureAwait(false);

            Dictionary<Guid, Dictionary<int, HashSet<Guid>>> neighbors = await LoadNeighborsAsync(rows.Keys.ToArray(), cancellationToken).ConfigureAwait(false);

            await _Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (KeyValuePair<Guid, NodeRow> row in rows)
                {
                    ParseMetadata(row.Value.MetadataJson, out string? name, out List<string>? labels, out Dictionary<string, object>? tags);
                    neighbors.TryGetValue(row.Key, out Dictionary<int, HashSet<Guid>>? nodeNeighbors);
                    PostgresqlHnswNode node = new PostgresqlHnswNode(this, row.Key, row.Value.Vector, nodeNeighbors ?? new Dictionary<int, HashSet<Guid>>(), name, labels, tags);
                    _NodeCache[row.Key] = node;
                    result[row.Key] = node;
                }
            }
            finally
            {
                _Lock.Release();
            }

            return result;
        }

        /// <inheritdoc />
        public async Task<TryGetNodeResult> TryGetNodeAsync(Guid id, CancellationToken cancellationToken = default)
        {
            try
            {
                return TryGetNodeResult.Found(await GetNodeAsync(id, cancellationToken).ConfigureAwait(false));
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
            List<Guid> ids = new List<Guid>();
            await ExecuteAsync(async (conn, tx) =>
            {
                await using NpgsqlCommand cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT id FROM hnsw_nodes WHERE index_id = @index_id ORDER BY id;";
                cmd.Parameters.AddWithValue("index_id", IndexId);
                await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    ids.Add(reader.GetGuid(0));
                }
            }, cancellationToken).ConfigureAwait(false);
            return ids;
        }

        /// <inheritdoc />
        public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return await ExecuteAsync(async (conn, tx) =>
            {
                await using NpgsqlCommand cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT COUNT(*) FROM hnsw_nodes WHERE index_id = @index_id;";
                cmd.Parameters.AddWithValue("index_id", IndexId);
                object? value = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                return Convert.ToInt32(value);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Guid?> GetEntryPointAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return await ExecuteAsync(async (conn, tx) =>
            {
                await using NpgsqlCommand cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT entry_point_id FROM hnsw_indexes WHERE id = @index_id;";
                cmd.Parameters.AddWithValue("index_id", IndexId);
                object? value = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                return value == null || value == DBNull.Value ? null : (Guid?)value;
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task SetEntryPointAsync(Guid? entryPoint, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await ExecuteAsync(async (conn, tx) =>
            {
                await using NpgsqlCommand cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE hnsw_indexes SET entry_point_id = @entry_point_id, updated_at = now() WHERE id = @index_id;";
                cmd.Parameters.AddWithValue("index_id", IndexId);
                cmd.Parameters.Add("entry_point_id", NpgsqlDbType.Uuid).Value = entryPoint.HasValue ? entryPoint.Value : DBNull.Value;
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<int> GetNodeLayerAsync(Guid nodeId, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return await ExecuteAsync(async (conn, tx) =>
            {
                await using NpgsqlCommand cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT layer FROM hnsw_node_layers WHERE index_id = @index_id AND node_id = @node_id;";
                cmd.Parameters.AddWithValue("index_id", IndexId);
                cmd.Parameters.AddWithValue("node_id", nodeId);
                object? value = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                return value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task SetNodeLayerAsync(Guid nodeId, int layer, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (nodeId == Guid.Empty) throw new ArgumentException("Node ID cannot be empty.", nameof(nodeId));
            if (layer < 0 || layer > 63) throw new ArgumentOutOfRangeException(nameof(layer), "Layer must be between 0 and 63.");

            await ExecuteAsync(async (conn, tx) =>
            {
                await using NpgsqlCommand cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO hnsw_node_layers (index_id, node_id, layer, updated_at)
                    VALUES (@index_id, @node_id, @layer, now())
                    ON CONFLICT (index_id, node_id)
                    DO UPDATE SET layer = EXCLUDED.layer, updated_at = now();";
                cmd.Parameters.AddWithValue("index_id", IndexId);
                cmd.Parameters.AddWithValue("node_id", nodeId);
                cmd.Parameters.AddWithValue("layer", layer);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task RemoveNodeLayerAsync(Guid nodeId, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await ExecuteAsync(async (conn, tx) =>
            {
                await using NpgsqlCommand cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM hnsw_node_layers WHERE index_id = @index_id AND node_id = @node_id;";
                cmd.Parameters.AddWithValue("index_id", IndexId);
                cmd.Parameters.AddWithValue("node_id", nodeId);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Dictionary<Guid, int>> GetAllNodeLayersAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            Dictionary<Guid, int> layers = new Dictionary<Guid, int>();
            await ExecuteAsync(async (conn, tx) =>
            {
                await using NpgsqlCommand cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT node_id, layer FROM hnsw_node_layers WHERE index_id = @index_id;";
                cmd.Parameters.AddWithValue("index_id", IndexId);
                await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    layers[reader.GetGuid(0)] = reader.GetInt32(1);
                }
            }, cancellationToken).ConfigureAwait(false);
            return layers;
        }

        /// <inheritdoc />
        public async Task ClearLayersAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await ExecuteAsync(async (conn, tx) =>
            {
                await using NpgsqlCommand cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM hnsw_node_layers WHERE index_id = @index_id;";
                cmd.Parameters.AddWithValue("index_id", IndexId);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<int> GetLayerCountAsync(CancellationToken cancellationToken = default)
        {
            return (await GetAllNodeLayersAsync(cancellationToken).ConfigureAwait(false)).Count;
        }

        /// <inheritdoc />
        public Task<IHnswStorageTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (!IsObserved()) return BeginTransactionCoreAsync(cancellationToken);
            return ObserveAsync("BeginTransaction", true, () => BeginTransactionCoreAsync(cancellationToken));
        }

        private async Task<IHnswStorageTransaction> BeginTransactionCoreAsync(CancellationToken cancellationToken)
        {

            await _TransactionCommandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_Transaction != null)
                {
                    return new NoOpHnswStorageTransaction();
                }

                NpgsqlConnection conn = await _DataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                TransactionContext context = new TransactionContext(conn, tx);
                _Transaction = context;
                return new PostgresqlStorageTransaction(this, context);
            }
            finally
            {
                _TransactionCommandLock.Release();
            }
        }

        /// <inheritdoc />
        public Task FlushAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Clears all data for this index.
        /// </summary>
        public async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await ExecuteAsync(async (conn, tx) =>
            {
                await using NpgsqlCommand cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    DELETE FROM hnsw_nodes WHERE index_id = @index_id;
                    DELETE FROM hnsw_node_layers WHERE index_id = @index_id;
                    DELETE FROM hnsw_neighbors WHERE index_id = @index_id;
                    UPDATE hnsw_indexes SET entry_point_id = NULL, updated_at = now() WHERE id = @index_id;";
                cmd.Parameters.AddWithValue("index_id", IndexId);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
            _NodeCache.Clear();
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_Disposed) return;
            _Disposed = true;
            _Lock.Dispose();
            _TransactionCommandLock.Dispose();
            if (_OwnsDataSource)
            {
                await _DataSource.DisposeAsync().ConfigureAwait(false);
            }
        }

        internal async Task UpsertNeighborAsync(Guid nodeId, int layer, Guid neighborId, CancellationToken cancellationToken)
        {
            await ExecuteAsync(async (conn, tx) =>
            {
                await using NpgsqlCommand cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO hnsw_neighbors (index_id, node_id, layer, neighbor_id, updated_at)
                    VALUES (@index_id, @node_id, @layer, @neighbor_id, now())
                    ON CONFLICT (index_id, node_id, layer, neighbor_id) DO NOTHING;";
                cmd.Parameters.AddWithValue("index_id", IndexId);
                cmd.Parameters.AddWithValue("node_id", nodeId);
                cmd.Parameters.AddWithValue("layer", layer);
                cmd.Parameters.AddWithValue("neighbor_id", neighborId);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }

        internal async Task DeleteNeighborAsync(Guid nodeId, int layer, Guid neighborId, CancellationToken cancellationToken)
        {
            await ExecuteAsync(async (conn, tx) =>
            {
                await using NpgsqlCommand cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM hnsw_neighbors WHERE index_id = @index_id AND node_id = @node_id AND layer = @layer AND neighbor_id = @neighbor_id;";
                cmd.Parameters.AddWithValue("index_id", IndexId);
                cmd.Parameters.AddWithValue("node_id", nodeId);
                cmd.Parameters.AddWithValue("layer", layer);
                cmd.Parameters.AddWithValue("neighbor_id", neighborId);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }

        internal async Task UpdateMetadataAsync(Guid nodeId, string? name, List<string>? labels, Dictionary<string, object>? tags, CancellationToken cancellationToken)
        {
            string? json = null;
            if (name != null || labels != null || tags != null)
            {
                Dictionary<string, object?> obj = new Dictionary<string, object?>();
                if (name != null) obj["Name"] = name;
                if (labels != null) obj["Labels"] = labels;
                if (tags != null) obj["Tags"] = tags;
                json = JsonSerializer.Serialize(obj);
            }

            await ExecuteAsync(async (conn, tx) =>
            {
                await using NpgsqlCommand cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE hnsw_nodes SET metadata_json = @metadata_json, updated_at = now() WHERE index_id = @index_id AND id = @id;";
                cmd.Parameters.AddWithValue("index_id", IndexId);
                cmd.Parameters.AddWithValue("id", nodeId);
                cmd.Parameters.Add("metadata_json", NpgsqlDbType.Jsonb).Value = json == null ? DBNull.Value : json;
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }

        private async Task<Dictionary<Guid, Dictionary<int, HashSet<Guid>>>> LoadNeighborsAsync(Guid[] ids, CancellationToken cancellationToken)
        {
            Dictionary<Guid, Dictionary<int, HashSet<Guid>>> result = new Dictionary<Guid, Dictionary<int, HashSet<Guid>>>();
            if (ids.Length == 0) return result;

            await ExecuteAsync(async (conn, tx) =>
            {
                await using NpgsqlCommand cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT node_id, layer, neighbor_id FROM hnsw_neighbors WHERE index_id = @index_id AND node_id = ANY(@ids);";
                cmd.Parameters.AddWithValue("index_id", IndexId);
                cmd.Parameters.AddWithValue("ids", ids);
                await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    Guid nodeId = reader.GetGuid(0);
                    int layer = reader.GetInt32(1);
                    Guid neighborId = reader.GetGuid(2);
                    if (!result.TryGetValue(nodeId, out Dictionary<int, HashSet<Guid>>? byLayer))
                    {
                        byLayer = new Dictionary<int, HashSet<Guid>>();
                        result[nodeId] = byLayer;
                    }

                    if (!byLayer.TryGetValue(layer, out HashSet<Guid>? neighbors))
                    {
                        neighbors = new HashSet<Guid>();
                        byLayer[layer] = neighbors;
                    }

                    neighbors.Add(neighborId);
                }
            }, cancellationToken).ConfigureAwait(false);

            return result;
        }

        private async Task ExecuteAsync(Func<NpgsqlConnection, NpgsqlTransaction?, Task> action, CancellationToken cancellationToken, [CallerMemberName] string caller = "")
        {
            if (!IsObserved())
            {
                await ExecuteCoreAsync(action, cancellationToken).ConfigureAwait(false);
                return;
            }

            string operation = ToOperationName(caller);
            Activity? activity = _SpanOperations.Contains(operation) ? HnswTelemetry.StartStorageActivity(_ProviderName, operation) : null;
            long startTimestamp = HnswTelemetry.GetTimestamp();
            try
            {
                await ExecuteCoreAsync(action, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                HnswTelemetry.RecordStorageOperation(_ProviderName, operation, startTimestamp, e);
                HnswTelemetry.CompleteActivity(activity, e);
                throw;
            }

            HnswTelemetry.RecordStorageOperation(_ProviderName, operation, startTimestamp);
            HnswTelemetry.CompleteActivity(activity, null);
        }

        private async Task<T> ExecuteAsync<T>(Func<NpgsqlConnection, NpgsqlTransaction?, Task<T>> action, CancellationToken cancellationToken, [CallerMemberName] string caller = "")
        {
            if (!IsObserved())
            {
                return await ExecuteCoreAsync(action, cancellationToken).ConfigureAwait(false);
            }

            string operation = ToOperationName(caller);
            Activity? activity = _SpanOperations.Contains(operation) ? HnswTelemetry.StartStorageActivity(_ProviderName, operation) : null;
            long startTimestamp = HnswTelemetry.GetTimestamp();
            T result;
            try
            {
                result = await ExecuteCoreAsync(action, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                HnswTelemetry.RecordStorageOperation(_ProviderName, operation, startTimestamp, e);
                HnswTelemetry.CompleteActivity(activity, e);
                throw;
            }

            HnswTelemetry.RecordStorageOperation(_ProviderName, operation, startTimestamp);
            HnswTelemetry.CompleteActivity(activity, null);
            return result;
        }

        private async Task ExecuteCoreAsync(Func<NpgsqlConnection, NpgsqlTransaction?, Task> action, CancellationToken cancellationToken)
        {
            await _TransactionCommandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                TransactionContext? context = _Transaction;
                if (context != null)
                {
                    await action(context.Connection, context.Transaction).ConfigureAwait(false);
                    return;
                }
            }
            finally
            {
                _TransactionCommandLock.Release();
            }

            await using NpgsqlConnection conn = await _DataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await action(conn, null).ConfigureAwait(false);
        }

        private async Task<T> ExecuteCoreAsync<T>(Func<NpgsqlConnection, NpgsqlTransaction?, Task<T>> action, CancellationToken cancellationToken)
        {
            await _TransactionCommandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                TransactionContext? context = _Transaction;
                if (context != null)
                {
                    return await action(context.Connection, context.Transaction).ConfigureAwait(false);
                }
            }
            finally
            {
                _TransactionCommandLock.Release();
            }

            await using NpgsqlConnection conn = await _DataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            return await action(conn, null).ConfigureAwait(false);
        }

        private static Task EnsureSchemaAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
        {
            return ObserveAsync("EnsureSchema", true, async () =>
            {
                await EnsureSchemaCoreAsync(dataSource, cancellationToken).ConfigureAwait(false);
                return true;
            });
        }

        private static async Task EnsureSchemaCoreAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
        {
            string sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "postgresql-schema.sql"), cancellationToken).ConfigureAwait(false);
            await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using NpgsqlCommand cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        private static Task<Guid> EnsureIndexAsync(
            NpgsqlDataSource dataSource,
            string indexName,
            int dimension,
            string distanceFunction,
            int m,
            int maxM,
            int efConstruction,
            bool createIfNotExists,
            CancellationToken cancellationToken)
        {
            return ObserveAsync("EnsureIndex", true, () => EnsureIndexCoreAsync(dataSource, indexName, dimension, distanceFunction, m, maxM, efConstruction, createIfNotExists, cancellationToken));
        }

        private static async Task<Guid> EnsureIndexCoreAsync(
            NpgsqlDataSource dataSource,
            string indexName,
            int dimension,
            string distanceFunction,
            int m,
            int maxM,
            int efConstruction,
            bool createIfNotExists,
            CancellationToken cancellationToken)
        {
            await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using (NpgsqlCommand select = conn.CreateCommand())
            {
                select.CommandText = "SELECT id FROM hnsw_indexes WHERE name = @name;";
                select.Parameters.AddWithValue("name", indexName);
                object? existing = await select.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                if (existing != null && existing != DBNull.Value)
                {
                    return (Guid)existing;
                }
            }

            if (!createIfNotExists)
            {
                throw new KeyNotFoundException($"PostgreSQL HNSW index '{indexName}' does not exist.");
            }

            Guid id = Guid.NewGuid();
            await using NpgsqlCommand insert = conn.CreateCommand();
            insert.CommandText = @"
                INSERT INTO hnsw_indexes (id, name, dimension, storage_type, distance_function, m, max_m, ef_construction, created_at, updated_at)
                VALUES (@id, @name, @dimension, 'PostgreSQL', @distance_function, @m, @max_m, @ef_construction, now(), now());";
            insert.Parameters.AddWithValue("id", id);
            insert.Parameters.AddWithValue("name", indexName);
            insert.Parameters.AddWithValue("dimension", dimension);
            insert.Parameters.AddWithValue("distance_function", distanceFunction);
            insert.Parameters.AddWithValue("m", m);
            insert.Parameters.AddWithValue("max_m", maxM);
            insert.Parameters.AddWithValue("ef_construction", efConstruction);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return id;
        }

        private static void ValidateNode(Guid id, List<float> vector)
        {
            if (id == Guid.Empty) throw new ArgumentException("Id cannot be empty.", nameof(id));
            ArgumentNullException.ThrowIfNull(vector, nameof(vector));
            if (vector.Count == 0) throw new ArgumentException("Vector cannot be empty.", nameof(vector));
            for (int i = 0; i < vector.Count; i++)
            {
                if (float.IsNaN(vector[i]) || float.IsInfinity(vector[i]))
                {
                    throw new ArgumentException($"Vector contains invalid value at index {i}.", nameof(vector));
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
            for (int i = 0; i < count; i++) vector.Add(floats[i]);
            return vector;
        }

        private static bool IsPostgresqlDiagnosticException(Exception ex)
        {
            return ex is NpgsqlException
                   || ex is PostgresException
                   || ex is TimeoutException
                   || ex is SocketException;
        }

        private static InvalidOperationException CreateDiagnosticException(string operation, string connectionString, Exception inner)
        {
            return new InvalidOperationException(
                $"Unable to {operation}. PostgreSQL target: {DescribeConnectionTarget(connectionString)}. See inner exception for the database error.",
                inner);
        }

        private static string DescribeConnectionTarget(string connectionString)
        {
            try
            {
                NpgsqlConnectionStringBuilder builder = new NpgsqlConnectionStringBuilder(connectionString);
                return $"Host={builder.Host};Port={builder.Port};Database={builder.Database};Username={builder.Username}";
            }
            catch
            {
                return "<unable to parse connection string without exposing secrets>";
            }
        }

        private static void ParseMetadata(string? json, out string? name, out List<string>? labels, out Dictionary<string, object>? tags)
        {
            name = null;
            labels = null;
            tags = null;
            if (string.IsNullOrWhiteSpace(json)) return;

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.TryGetProperty("Name", out JsonElement nameEl) && nameEl.ValueKind == JsonValueKind.String)
            {
                name = nameEl.GetString();
            }

            if (root.TryGetProperty("Labels", out JsonElement labelsEl) && labelsEl.ValueKind == JsonValueKind.Array)
            {
                labels = new List<string>();
                foreach (JsonElement el in labelsEl.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.String) labels.Add(el.GetString()!);
                }
            }

            if (root.TryGetProperty("Tags", out JsonElement tagsEl) && tagsEl.ValueKind == JsonValueKind.Object)
            {
                tags = new Dictionary<string, object>();
                foreach (JsonProperty prop in tagsEl.EnumerateObject())
                {
                    tags[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString()!,
                        JsonValueKind.Number => prop.Value.TryGetInt64(out long l) ? l : prop.Value.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => prop.Value.GetRawText(),
                    };
                }
            }
        }

        private void CompleteTransaction(TransactionContext context, bool rolledBack)
        {
            if (rolledBack)
            {
                _NodeCache.Clear();
            }

            if (ReferenceEquals(_Transaction, context))
            {
                _Transaction = null;
            }
        }

        private static bool IsObserved()
        {
            return HnswTelemetry.IsStorageObserved || HnswTelemetry.ActivitySource.HasListeners();
        }

        private static string ToOperationName(string caller)
        {
            if (string.IsNullOrEmpty(caller)) return "Unknown";
            return caller.EndsWith("Async", StringComparison.Ordinal) ? caller.Substring(0, caller.Length - 5) : caller;
        }

        private static async Task<T> ObserveAsync<T>(string operation, bool span, Func<Task<T>> action)
        {
            Activity? activity = span ? HnswTelemetry.StartStorageActivity(_ProviderName, operation) : null;
            long startTimestamp = HnswTelemetry.GetTimestamp();
            T result;
            try
            {
                result = await action().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                HnswTelemetry.RecordStorageOperation(_ProviderName, operation, startTimestamp, e);
                HnswTelemetry.CompleteActivity(activity, e);
                throw;
            }

            HnswTelemetry.RecordStorageOperation(_ProviderName, operation, startTimestamp);
            HnswTelemetry.CompleteActivity(activity, null);
            return result;
        }

        private void ThrowIfDisposed()
        {
            if (_Disposed) throw new ObjectDisposedException(nameof(PostgresqlStorageProvider));
        }

        private sealed record NodeRow(List<float> Vector, string? MetadataJson);

        private sealed class TransactionContext
        {
            public TransactionContext(NpgsqlConnection connection, NpgsqlTransaction transaction)
            {
                Connection = connection;
                Transaction = transaction;
            }

            public NpgsqlConnection Connection { get; }
            public NpgsqlTransaction Transaction { get; }
        }

        private sealed class PostgresqlStorageTransaction : IHnswStorageTransaction
        {
            private readonly PostgresqlStorageProvider _Provider;
            private readonly TransactionContext _Context;
            private bool _Completed;

            public PostgresqlStorageTransaction(PostgresqlStorageProvider provider, TransactionContext context)
            {
                _Provider = provider;
                _Context = context;
            }

            public async Task CommitAsync(CancellationToken cancellationToken = default)
            {
                if (_Completed) return;
                long startTimestamp = HnswTelemetry.GetTimestamp();
                await _Provider._TransactionCommandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await _Context.Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    _Completed = true;
                    _Provider.CompleteTransaction(_Context, rolledBack: false);
                    HnswTelemetry.RecordStorageOperation(_ProviderName, "Commit", startTimestamp);
                    HnswTelemetry.RecordStorageTransaction(_ProviderName, HnswTelemetryNames.TransactionCommitted);
                }
                catch (Exception e)
                {
                    HnswTelemetry.RecordStorageOperation(_ProviderName, "Commit", startTimestamp, e);
                    HnswTelemetry.RecordStorageTransaction(_ProviderName, HnswTelemetryNames.TransactionFailed);
                    throw;
                }
                finally
                {
                    _Provider._TransactionCommandLock.Release();
                }
            }

            public async Task RollbackAsync(CancellationToken cancellationToken = default)
            {
                if (_Completed) return;
                long startTimestamp = HnswTelemetry.GetTimestamp();
                await _Provider._TransactionCommandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await _Context.Transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    _Completed = true;
                    _Provider.CompleteTransaction(_Context, rolledBack: true);
                    HnswTelemetry.RecordStorageOperation(_ProviderName, "Rollback", startTimestamp);
                    HnswTelemetry.RecordStorageTransaction(_ProviderName, HnswTelemetryNames.TransactionRolledBack);
                }
                catch (Exception e)
                {
                    HnswTelemetry.RecordStorageOperation(_ProviderName, "Rollback", startTimestamp, e);
                    HnswTelemetry.RecordStorageTransaction(_ProviderName, HnswTelemetryNames.TransactionFailed);
                    throw;
                }
                finally
                {
                    _Provider._TransactionCommandLock.Release();
                }
            }

            public async ValueTask DisposeAsync()
            {
                if (!_Completed)
                {
                    await RollbackAsync().ConfigureAwait(false);
                }

                await _Context.Transaction.DisposeAsync().ConfigureAwait(false);
                await _Context.Connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
