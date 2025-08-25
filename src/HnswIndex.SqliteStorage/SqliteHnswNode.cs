namespace Hnsw.SqliteStorage
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using Microsoft.Data.Sqlite;
    using Hnsw;

    /// <summary>
    /// SQLite-based implementation of HNSW node with thread-safe operations.
    /// </summary>
    public class SqliteHnswNode : IHnswNode, IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Gets the unique identifier of the node.
        /// Cannot be Guid.Empty.
        /// </summary>
        public Guid Id => _Id;

        /// <summary>
        /// Gets the vector associated with the node.
        /// Never null. Vector dimension typically ranges from 1 to 4096.
        /// All values must be finite (not NaN or Infinity).
        /// </summary>
        public List<float> Vector => _Vector;

        /// <summary>
        /// Gets whether this node has been disposed.
        /// </summary>
        public bool IsDisposed => _Disposed;

        /// <summary>
        /// Gets whether the node has unsaved changes.
        /// </summary>
        public bool IsDirty => _IsDirty;

        #endregion

        #region Private-Members

        private readonly Guid _Id;
        private readonly List<float> _Vector;
        private readonly Dictionary<int, HashSet<Guid>> _Neighbors = new Dictionary<int, HashSet<Guid>>();
        private readonly ReaderWriterLockSlim _NodeLock = new ReaderWriterLockSlim();
        private readonly SqliteConnection _Connection;
        private readonly string _TableName;
        private bool _Disposed = false;
        private bool _IsDirty = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the SqliteHnswNode class.
        /// </summary>
        /// <param name="id">Node identifier. Cannot be Guid.Empty.</param>
        /// <param name="vector">Vector data. Cannot be null or empty. All values must be finite.</param>
        /// <param name="connection">SQLite database connection. Cannot be null.</param>
        /// <param name="tableName">Table name for storing neighbors. Cannot be null or empty.</param>
        /// <exception cref="ArgumentException">Thrown when id is Guid.Empty or vector contains invalid values.</exception>
        /// <exception cref="ArgumentNullException">Thrown when vector, connection, or tableName is null.</exception>
        public SqliteHnswNode(Guid id, List<float> vector, SqliteConnection connection, string tableName)
        {
            if (id == Guid.Empty)
            {
                throw new ArgumentException("Id cannot be Guid.Empty.", nameof(id));
            }
            
            ArgumentNullException.ThrowIfNull(vector, nameof(vector));
            ArgumentNullException.ThrowIfNull(connection, nameof(connection));
            
            if (vector.Count == 0)
            {
                throw new ArgumentException("Vector cannot be empty.", nameof(vector));
            }
            
            if (string.IsNullOrWhiteSpace(tableName))
            {
                throw new ArgumentNullException(nameof(tableName));
            }

            // Validate vector values
            for (int i = 0; i < vector.Count; i++)
            {
                if (float.IsNaN(vector[i]) || float.IsInfinity(vector[i]))
                {
                    throw new ArgumentException($"Vector contains invalid value at index {i}. All values must be finite.", nameof(vector));
                }
            }

            _Id = id;
            _Vector = new List<float>(vector); // Create defensive copy
            _Connection = connection;
            _TableName = tableName;

            LoadNeighborsFromDatabase();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Gets a copy of the node's neighbors organized by layer.
        /// Thread-safe operation.
        /// Returns a dictionary where keys are layer numbers (0 to MaxLayers-1) and values are sets of neighbor IDs.
        /// Never returns null. Returns an empty dictionary if no neighbors exist.
        /// </summary>
        /// <returns>A copy of the neighbors dictionary.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the node has been disposed.</exception>
        public Dictionary<int, HashSet<Guid>> GetNeighbors()
        {
            ThrowIfDisposed();

            _NodeLock.EnterReadLock();
            try
            {
                Dictionary<int, HashSet<Guid>> result = new Dictionary<int, HashSet<Guid>>();
                foreach (KeyValuePair<int, HashSet<Guid>> kvp in _Neighbors)
                {
                    result[kvp.Key] = new HashSet<Guid>(kvp.Value);
                }
                return result;
            }
            finally
            {
                _NodeLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Adds a neighbor connection at the specified layer.
        /// Thread-safe operation. Idempotent - adding the same neighbor multiple times has no additional effect.
        /// </summary>
        /// <param name="layer">The layer number. Minimum: 0, Maximum: 63.</param>
        /// <param name="NeighborGUID">The ID of the neighbor to add. Cannot be Guid.Empty or equal to this node's ID.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when layer is negative or exceeds maximum.</exception>
        /// <exception cref="ArgumentException">Thrown when NeighborGUID is invalid.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the node has been disposed.</exception>
        public void AddNeighbor(int layer, Guid NeighborGUID)
        {
            ThrowIfDisposed();

            if (layer < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot be negative.");
            }
            
            if (layer > 63)
            {
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot exceed 63.");
            }
            
            if (NeighborGUID == Guid.Empty)
            {
                throw new ArgumentException("NeighborId cannot be Guid.Empty.", nameof(NeighborGUID));
            }
            
            if (NeighborGUID == _Id)
            {
                throw new ArgumentException("Node cannot be its own neighbor.", nameof(NeighborGUID));
            }

            _NodeLock.EnterWriteLock();
            try
            {
                if (!_Neighbors.ContainsKey(layer))
                {
                    _Neighbors[layer] = new HashSet<Guid>();
                }

                if (_Neighbors[layer].Add(NeighborGUID))
                {
                    _IsDirty = true;
                    // Don't save immediately - wait for explicit Flush() call for better batch performance
                    // SaveNeighborsToDatabase();
                }
            }
            finally
            {
                _NodeLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Removes a neighbor connection at the specified layer.
        /// Thread-safe operation. No effect if the neighbor doesn't exist.
        /// Removes the layer entry if it becomes empty after neighbor removal.
        /// </summary>
        /// <param name="layer">The layer number. Minimum: 0, Maximum: 63.</param>
        /// <param name="NeighborGUID">The ID of the neighbor to remove.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when layer is negative or exceeds maximum.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the node has been disposed.</exception>
        public void RemoveNeighbor(int layer, Guid NeighborGUID)
        {
            ThrowIfDisposed();

            if (layer < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot be negative.");
            }
            
            if (layer > 63)
            {
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot exceed 63.");
            }

            _NodeLock.EnterWriteLock();
            try
            {
                if (_Neighbors.ContainsKey(layer))
                {
                    if (_Neighbors[layer].Remove(NeighborGUID))
                    {
                        _IsDirty = true;
                        if (_Neighbors[layer].Count == 0)
                        {
                            _Neighbors.Remove(layer);
                        }
                        // Don't save immediately - wait for explicit Flush() call for better batch performance
                        // SaveNeighborsToDatabase();
                    }
                }
            }
            finally
            {
                _NodeLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Gets the number of neighbors at a specific layer.
        /// Thread-safe operation.
        /// </summary>
        /// <param name="layer">The layer number. Minimum: 0, Maximum: 63.</param>
        /// <returns>The number of neighbors at the specified layer, or 0 if the layer has no neighbors.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when layer is negative or exceeds maximum.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the node has been disposed.</exception>
        public int GetNeighborCount(int layer)
        {
            ThrowIfDisposed();

            if (layer < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot be negative.");
            }
            
            if (layer > 63)
            {
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot exceed 63.");
            }

            _NodeLock.EnterReadLock();
            try
            {
                return _Neighbors.TryGetValue(layer, out HashSet<Guid>? layerNeighbors) ? layerNeighbors.Count : 0;
            }
            finally
            {
                _NodeLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Gets the total number of neighbors across all layers.
        /// Thread-safe operation.
        /// </summary>
        /// <returns>The total number of neighbors.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the node has been disposed.</exception>
        public int GetTotalNeighborCount()
        {
            ThrowIfDisposed();

            _NodeLock.EnterReadLock();
            try
            {
                return _Neighbors.Values.Sum(set => set.Count);
            }
            finally
            {
                _NodeLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Checks if a specific neighbor exists at the given layer.
        /// Thread-safe operation.
        /// </summary>
        /// <param name="layer">The layer number. Minimum: 0, Maximum: 63.</param>
        /// <param name="NeighborGUID">The neighbor ID to check.</param>
        /// <returns>true if the neighbor exists at the specified layer; otherwise, false.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when layer is negative or exceeds maximum.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the node has been disposed.</exception>
        public bool HasNeighbor(int layer, Guid NeighborGUID)
        {
            ThrowIfDisposed();

            if (layer < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot be negative.");
            }
            
            if (layer > 63)
            {
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot exceed 63.");
            }

            _NodeLock.EnterReadLock();
            try
            {
                return _Neighbors.TryGetValue(layer, out HashSet<Guid>? layerNeighbors) && layerNeighbors.Contains(NeighborGUID);
            }
            finally
            {
                _NodeLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Forces a save of neighbor data to the database.
        /// Thread-safe operation.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown when the node has been disposed.</exception>
        public void Flush()
        {
            ThrowIfDisposed();

            _NodeLock.EnterWriteLock();
            try
            {
                if (_IsDirty)
                {
                    SaveNeighborsToDatabase();
                    _IsDirty = false;
                }
            }
            finally
            {
                _NodeLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Disposes of the node's resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Disposes of the node's resources.
        /// </summary>
        /// <param name="disposing">true if disposing managed resources; otherwise, false.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_Disposed)
            {
                if (disposing)
                {
                    try
                    {
                        Flush(); // Save any pending changes
                    }
                    catch
                    {
                        // Ignore errors during disposal
                    }
                    _NodeLock?.Dispose();
                }
                _Disposed = true;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_Disposed)
            {
                throw new ObjectDisposedException(nameof(SqliteHnswNode));
            }
        }

        private void LoadNeighborsFromDatabase()
        {
            try
            {
                SqliteCommand command = _Connection.CreateCommand();
                command.CommandText = $"SELECT neighbors_blob FROM {_TableName} WHERE node_id = @nodeId";
                command.Parameters.AddWithValue("@nodeId", _Id.ToByteArray());

                object? result = command.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                {
                    byte[]? blob = (byte[]?)result;
                    if (blob != null && blob.Length > 0)
                    {
                        Dictionary<int, HashSet<Guid>> deserializedNeighbors = DeserializeNeighbors(blob);
                        _Neighbors.Clear();
                        foreach (KeyValuePair<int, HashSet<Guid>> kvp in deserializedNeighbors)
                        {
                            _Neighbors[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch
            {
                // If loading fails, start with empty neighbors
                _Neighbors.Clear();
            }
        }

        private void SaveNeighborsToDatabase()
        {
            try
            {
                byte[] blob = SerializeNeighbors(_Neighbors);

                SqliteCommand command = _Connection.CreateCommand();
                command.CommandText = $@"
                    INSERT OR REPLACE INTO {_TableName} (node_id, neighbors_blob, updated_at) 
                    VALUES (@nodeId, @neighborsBlob, CURRENT_TIMESTAMP)";
                command.Parameters.AddWithValue("@nodeId", _Id.ToByteArray());
                command.Parameters.AddWithValue("@neighborsBlob", blob);

                command.ExecuteNonQuery();
                _IsDirty = false;
            }
            catch
            {
                // Mark as dirty if save fails so we can retry later
                _IsDirty = true;
                throw;
            }
        }

        private byte[] SerializeNeighbors(Dictionary<int, HashSet<Guid>> neighbors)
        {
            using MemoryStream ms = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(ms);
            
            writer.Write(neighbors.Count);
            foreach (KeyValuePair<int, HashSet<Guid>> kvp in neighbors)
            {
                writer.Write(kvp.Key); // layer
                writer.Write(kvp.Value.Count); // neighbor count
                foreach (Guid NeighborGUID in kvp.Value)
                {
                    writer.Write(NeighborGUID.ToByteArray());
                }
            }
            return ms.ToArray();
        }

        private Dictionary<int, HashSet<Guid>> DeserializeNeighbors(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
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

        #endregion
    }
}