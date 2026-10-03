namespace Hnsw.SqliteStorage
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hnsw;
    using Microsoft.Data.Sqlite;

    /// <summary>
    /// SQLite-backed HNSW node implementation.
    /// </summary>
    public sealed class SqliteHnswNode : IHnswNode, IDisposable
    {
        private readonly Guid _Id;
        private readonly List<float> _Vector;
        private readonly Dictionary<int, HashSet<Guid>> _Neighbors = new Dictionary<int, HashSet<Guid>>();
        private readonly SemaphoreSlim _NodeLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _DatabaseLock;
        private readonly SqliteConnection _Connection;
        private readonly string _NeighborsTableName;
        private readonly string? _NodesTableName;
        private bool _Disposed;
        private bool _IsDirty;
        private string? _Name;
        private List<string>? _Labels;
        private Dictionary<string, object>? _Tags;

        private SqliteHnswNode(
            Guid id,
            List<float> vector,
            SqliteConnection connection,
            SemaphoreSlim databaseLock,
            string neighborsTableName,
            string? nodesTableName)
        {
            _Id = id;
            _Vector = new List<float>(vector);
            _Connection = connection;
            _DatabaseLock = databaseLock;
            _NeighborsTableName = neighborsTableName;
            _NodesTableName = nodesTableName;
        }

        /// <inheritdoc />
        public Guid Id => _Id;

        /// <inheritdoc />
        public IReadOnlyList<float> Vector => _Vector;

        /// <inheritdoc />
        public string? Name => _Name;

        /// <inheritdoc />
        public IReadOnlyList<string>? Labels => _Labels;

        /// <inheritdoc />
        public IReadOnlyDictionary<string, object>? Tags => _Tags;

        /// <summary>
        /// Gets whether this node has been disposed.
        /// </summary>
        public bool IsDisposed => _Disposed;

        /// <summary>
        /// Gets whether this node has unsaved neighbor changes after a failed write.
        /// </summary>
        public bool IsDirty => _IsDirty;

        /// <summary>
        /// Creates and asynchronously loads a SQLite-backed node.
        /// </summary>
        public static async Task<SqliteHnswNode> CreateAsync(
            Guid id,
            List<float> vector,
            SqliteConnection connection,
            SemaphoreSlim databaseLock,
            string neighborsTableName,
            string? nodesTableName = null,
            CancellationToken cancellationToken = default)
        {
            Validate(id, vector, connection, databaseLock, neighborsTableName);

            SqliteHnswNode node = new SqliteHnswNode(id, vector, connection, databaseLock, neighborsTableName, nodesTableName);
            await node.LoadNeighborsFromDatabaseAsync(cancellationToken).ConfigureAwait(false);
            await node.LoadMetadataFromDatabaseAsync(cancellationToken).ConfigureAwait(false);
            return node;
        }

        /// <inheritdoc />
        public async Task<Dictionary<int, HashSet<Guid>>> GetNeighborsAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await _NodeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return _Neighbors.ToDictionary(kvp => kvp.Key, kvp => new HashSet<Guid>(kvp.Value));
            }
            finally
            {
                _NodeLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task AddNeighborAsync(int layer, Guid neighborGuid, CancellationToken cancellationToken = default)
        {
            ValidateNeighbor(layer, neighborGuid);

            await _NodeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_Neighbors.TryGetValue(layer, out HashSet<Guid>? layerNeighbors))
                {
                    layerNeighbors = new HashSet<Guid>();
                    _Neighbors[layer] = layerNeighbors;
                }

                if (!layerNeighbors.Add(neighborGuid))
                {
                    return;
                }

                _IsDirty = true;
                await SaveNeighborsToDatabaseAsync(cancellationToken).ConfigureAwait(false);
                _IsDirty = false;
            }
            finally
            {
                _NodeLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task RemoveNeighborAsync(int layer, Guid neighborGuid, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (layer < 0) throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot be negative.");
            if (layer > 63) throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot exceed 63.");

            await _NodeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_Neighbors.TryGetValue(layer, out HashSet<Guid>? layerNeighbors))
                {
                    return;
                }

                if (!layerNeighbors.Remove(neighborGuid))
                {
                    return;
                }

                if (layerNeighbors.Count == 0)
                {
                    _Neighbors.Remove(layer);
                }

                _IsDirty = true;
                await SaveNeighborsToDatabaseAsync(cancellationToken).ConfigureAwait(false);
                _IsDirty = false;
            }
            finally
            {
                _NodeLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task SetMetadataAsync(
            string? name,
            List<string>? labels,
            Dictionary<string, object>? tags,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _NodeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _Name = name;
                _Labels = labels == null ? null : new List<string>(labels);
                _Tags = tags == null ? null : new Dictionary<string, object>(tags);
                await SaveMetadataToDatabaseAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _NodeLock.Release();
            }
        }

        /// <summary>
        /// Forces a retry of any buffered neighbor write that previously failed.
        /// </summary>
        public async Task FlushAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _NodeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_IsDirty)
                {
                    await SaveNeighborsToDatabaseAsync(cancellationToken).ConfigureAwait(false);
                    await SaveMetadataToDatabaseAsync(cancellationToken).ConfigureAwait(false);
                    _IsDirty = false;
                }
            }
            finally
            {
                _NodeLock.Release();
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            _NodeLock.Dispose();
        }

        private static void Validate(
            Guid id,
            List<float> vector,
            SqliteConnection connection,
            SemaphoreSlim databaseLock,
            string neighborsTableName)
        {
            if (id == Guid.Empty) throw new ArgumentException("Id cannot be Guid.Empty.", nameof(id));
            ArgumentNullException.ThrowIfNull(vector, nameof(vector));
            ArgumentNullException.ThrowIfNull(connection, nameof(connection));
            ArgumentNullException.ThrowIfNull(databaseLock, nameof(databaseLock));
            if (string.IsNullOrWhiteSpace(neighborsTableName)) throw new ArgumentNullException(nameof(neighborsTableName));
            if (vector.Count == 0) throw new ArgumentException("Vector cannot be empty.", nameof(vector));

            for (int i = 0; i < vector.Count; i++)
            {
                if (float.IsNaN(vector[i]) || float.IsInfinity(vector[i]))
                {
                    throw new ArgumentException($"Vector contains invalid value at index {i}. All values must be finite.", nameof(vector));
                }
            }
        }

        private void ValidateNeighbor(int layer, Guid neighborGuid)
        {
            ThrowIfDisposed();
            if (layer < 0) throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot be negative.");
            if (layer > 63) throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot exceed 63.");
            if (neighborGuid == Guid.Empty) throw new ArgumentException("NeighborId cannot be Guid.Empty.", nameof(neighborGuid));
            if (neighborGuid == _Id) throw new ArgumentException("Node cannot be its own neighbor.", nameof(neighborGuid));
        }

        private void ThrowIfDisposed()
        {
            if (_Disposed)
            {
                throw new ObjectDisposedException(nameof(SqliteHnswNode));
            }
        }

        private async Task LoadNeighborsFromDatabaseAsync(CancellationToken cancellationToken)
        {
            await _DatabaseLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using SqliteCommand command = _Connection.CreateCommand();
                command.CommandText = $"SELECT neighbors_blob FROM {_NeighborsTableName} WHERE node_id = @nodeId";
                command.Parameters.AddWithValue("@nodeId", _Id.ToByteArray());

                object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                if (result is byte[] blob && blob.Length > 0)
                {
                    Dictionary<int, HashSet<Guid>> deserializedNeighbors = DeserializeNeighbors(blob);
                    _Neighbors.Clear();
                    foreach (KeyValuePair<int, HashSet<Guid>> kvp in deserializedNeighbors)
                    {
                        _Neighbors[kvp.Key] = kvp.Value;
                    }
                }
            }
            finally
            {
                _DatabaseLock.Release();
            }
        }

        private async Task SaveNeighborsToDatabaseAsync(CancellationToken cancellationToken)
        {
            if (!HnswTelemetry.IsStorageObserved)
            {
                await SaveNeighborsToDatabaseCoreAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            long startTimestamp = HnswTelemetry.GetTimestamp();
            try
            {
                await SaveNeighborsToDatabaseCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                HnswTelemetry.RecordStorageOperation(HnswTelemetryNames.ProviderSqlite, "SaveNeighbors", startTimestamp, e);
                throw;
            }

            HnswTelemetry.RecordStorageOperation(HnswTelemetryNames.ProviderSqlite, "SaveNeighbors", startTimestamp);
        }

        private async Task SaveNeighborsToDatabaseCoreAsync(CancellationToken cancellationToken)
        {
            byte[] blob = SerializeNeighbors(_Neighbors);

            await _DatabaseLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using SqliteCommand command = _Connection.CreateCommand();
                command.CommandText = $@"
                    INSERT OR REPLACE INTO {_NeighborsTableName} (node_id, neighbors_blob, updated_at)
                    VALUES (@nodeId, @neighborsBlob, CURRENT_TIMESTAMP)";
                command.Parameters.AddWithValue("@nodeId", _Id.ToByteArray());
                command.Parameters.AddWithValue("@neighborsBlob", blob);

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                _IsDirty = true;
                throw;
            }
            finally
            {
                _DatabaseLock.Release();
            }
        }

        private async Task LoadMetadataFromDatabaseAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_NodesTableName)) return;

            await _DatabaseLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using SqliteCommand cmd = _Connection.CreateCommand();
                cmd.CommandText = $"SELECT metadata_json FROM {_NodesTableName} WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", _Id.ToByteArray());

                object? result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                if (result is not string json || json.Length == 0)
                {
                    return;
                }

                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                if (root.TryGetProperty("Name", out JsonElement nameEl) && nameEl.ValueKind == JsonValueKind.String)
                {
                    _Name = nameEl.GetString();
                }

                if (root.TryGetProperty("Labels", out JsonElement labelsEl) && labelsEl.ValueKind == JsonValueKind.Array)
                {
                    _Labels = new List<string>();
                    foreach (JsonElement el in labelsEl.EnumerateArray())
                    {
                        if (el.ValueKind == JsonValueKind.String)
                        {
                            _Labels.Add(el.GetString()!);
                        }
                    }
                }

                if (root.TryGetProperty("Tags", out JsonElement tagsEl) && tagsEl.ValueKind == JsonValueKind.Object)
                {
                    _Tags = new Dictionary<string, object>();
                    foreach (JsonProperty prop in tagsEl.EnumerateObject())
                    {
                        _Tags[prop.Name] = prop.Value.ValueKind switch
                        {
                            JsonValueKind.String => prop.Value.GetString()!,
                            JsonValueKind.Number => prop.Value.TryGetInt64(out long l) ? (object)l : prop.Value.GetDouble(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            _ => prop.Value.GetRawText(),
                        };
                    }
                }
            }
            finally
            {
                _DatabaseLock.Release();
            }
        }

        private async Task SaveMetadataToDatabaseAsync(CancellationToken cancellationToken)
        {
            if (!HnswTelemetry.IsStorageObserved)
            {
                await SaveMetadataToDatabaseCoreAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            long startTimestamp = HnswTelemetry.GetTimestamp();
            try
            {
                await SaveMetadataToDatabaseCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                HnswTelemetry.RecordStorageOperation(HnswTelemetryNames.ProviderSqlite, "SaveMetadata", startTimestamp, e);
                throw;
            }

            HnswTelemetry.RecordStorageOperation(HnswTelemetryNames.ProviderSqlite, "SaveMetadata", startTimestamp);
        }

        private async Task SaveMetadataToDatabaseCoreAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_NodesTableName)) return;

            await _DatabaseLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_Name == null && _Labels == null && _Tags == null)
                {
                    using SqliteCommand cmd = _Connection.CreateCommand();
                    cmd.CommandText = $"UPDATE {_NodesTableName} SET metadata_json = NULL, updated_at = CURRENT_TIMESTAMP WHERE id = @id";
                    cmd.Parameters.AddWithValue("@id", _Id.ToByteArray());
                    await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }

                Dictionary<string, object?> obj = new Dictionary<string, object?>();
                if (_Name != null) obj["Name"] = _Name;
                if (_Labels != null) obj["Labels"] = _Labels;
                if (_Tags != null) obj["Tags"] = _Tags;

                string json = JsonSerializer.Serialize(obj);

                using SqliteCommand update = _Connection.CreateCommand();
                update.CommandText = $"UPDATE {_NodesTableName} SET metadata_json = @json, updated_at = CURRENT_TIMESTAMP WHERE id = @id";
                update.Parameters.AddWithValue("@json", json);
                update.Parameters.AddWithValue("@id", _Id.ToByteArray());
                await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _DatabaseLock.Release();
            }
        }

        private static byte[] SerializeNeighbors(Dictionary<int, HashSet<Guid>> neighbors)
        {
            using MemoryStream ms = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(ms);

            writer.Write(neighbors.Count);
            foreach (KeyValuePair<int, HashSet<Guid>> kvp in neighbors)
            {
                writer.Write(kvp.Key);
                writer.Write(kvp.Value.Count);
                foreach (Guid neighborGuid in kvp.Value)
                {
                    writer.Write(neighborGuid.ToByteArray());
                }
            }

            return ms.ToArray();
        }

        private static Dictionary<int, HashSet<Guid>> DeserializeNeighbors(byte[] bytes)
        {
            if (bytes.Length == 0)
            {
                return new Dictionary<int, HashSet<Guid>>();
            }

            using MemoryStream ms = new MemoryStream(bytes);
            using BinaryReader reader = new BinaryReader(ms);

            Dictionary<int, HashSet<Guid>> neighbors = new Dictionary<int, HashSet<Guid>>();
            int layerCount = reader.ReadInt32();

            for (int i = 0; i < layerCount; i++)
            {
                int layer = reader.ReadInt32();
                int neighborCount = reader.ReadInt32();
                HashSet<Guid> layerNeighbors = new HashSet<Guid>();

                for (int j = 0; j < neighborCount; j++)
                {
                    byte[] guidBytes = reader.ReadBytes(16);
                    layerNeighbors.Add(new Guid(guidBytes));
                }

                neighbors[layer] = layerNeighbors;
            }

            return neighbors;
        }
    }
}
