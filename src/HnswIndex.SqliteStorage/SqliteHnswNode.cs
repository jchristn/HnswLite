namespace Hnsw.SqliteStorage
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using Microsoft.Data.Sqlite;
    using System.Text.Json;
    using Hnsw;

    /// <summary>
    /// SQLite-based implementation of HNSW node with thread-safe operations.
    /// </summary>
    public class SqliteHnswNode : IHnswNode, IDisposable
    {
        // Private members
        private readonly Guid _id;
        private readonly List<float> _vector;
        private readonly Dictionary<int, HashSet<Guid>> _neighbors = new Dictionary<int, HashSet<Guid>>();
        private readonly ReaderWriterLockSlim _nodeLock = new ReaderWriterLockSlim();
        private readonly SqliteConnection _connection;
        private readonly string _tableName;
        private bool _disposed = false;
        private bool _isDirty = false;

        // Public properties
        /// <summary>
        /// Gets the unique identifier of the node.
        /// Cannot be Guid.Empty.
        /// </summary>
        public Guid Id => _id;

        /// <summary>
        /// Gets the vector associated with the node.
        /// Never null. Vector dimension typically ranges from 1 to 4096.
        /// All values must be finite (not NaN or Infinity).
        /// </summary>
        public List<float> Vector => _vector;

        /// <summary>
        /// Gets whether this node has been disposed.
        /// </summary>
        public bool IsDisposed => _disposed;

        /// <summary>
        /// Gets whether the node has unsaved changes.
        /// </summary>
        public bool IsDirty => _isDirty;

        // Constructors
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
                throw new ArgumentException("Id cannot be Guid.Empty.", nameof(id));
            if (vector == null)
                throw new ArgumentNullException(nameof(vector));
            if (vector.Count == 0)
                throw new ArgumentException("Vector cannot be empty.", nameof(vector));
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            if (string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentNullException(nameof(tableName));

            // Validate vector values
            for (int i = 0; i < vector.Count; i++)
            {
                if (float.IsNaN(vector[i]) || float.IsInfinity(vector[i]))
                    throw new ArgumentException($"Vector contains invalid value at index {i}. All values must be finite.", nameof(vector));
            }

            _id = id;
            _vector = new List<float>(vector); // Create defensive copy
            _connection = connection;
            _tableName = tableName;

            LoadNeighborsFromDatabase();
        }

        // Public methods
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

            _nodeLock.EnterReadLock();
            try
            {
                var result = new Dictionary<int, HashSet<Guid>>();
                foreach (var kvp in _neighbors)
                {
                    result[kvp.Key] = new HashSet<Guid>(kvp.Value);
                }
                return result;
            }
            finally
            {
                _nodeLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Adds a neighbor connection at the specified layer.
        /// Thread-safe operation. Idempotent - adding the same neighbor multiple times has no additional effect.
        /// </summary>
        /// <param name="layer">The layer number. Minimum: 0, Maximum: 63.</param>
        /// <param name="neighborId">The ID of the neighbor to add. Cannot be Guid.Empty or equal to this node's ID.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when layer is negative or exceeds maximum.</exception>
        /// <exception cref="ArgumentException">Thrown when neighborId is invalid.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the node has been disposed.</exception>
        public void AddNeighbor(int layer, Guid neighborId)
        {
            ThrowIfDisposed();

            if (layer < 0)
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot be negative.");
            if (layer > 63)
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot exceed 63.");
            if (neighborId == Guid.Empty)
                throw new ArgumentException("NeighborId cannot be Guid.Empty.", nameof(neighborId));
            if (neighborId == _id)
                throw new ArgumentException("Node cannot be its own neighbor.", nameof(neighborId));

            _nodeLock.EnterWriteLock();
            try
            {
                if (!_neighbors.ContainsKey(layer))
                    _neighbors[layer] = new HashSet<Guid>();

                if (_neighbors[layer].Add(neighborId))
                {
                    _isDirty = true;
                    SaveNeighborsToDatabase();
                }
            }
            finally
            {
                _nodeLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Removes a neighbor connection at the specified layer.
        /// Thread-safe operation. No effect if the neighbor doesn't exist.
        /// Removes the layer entry if it becomes empty after neighbor removal.
        /// </summary>
        /// <param name="layer">The layer number. Minimum: 0, Maximum: 63.</param>
        /// <param name="neighborId">The ID of the neighbor to remove.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when layer is negative or exceeds maximum.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the node has been disposed.</exception>
        public void RemoveNeighbor(int layer, Guid neighborId)
        {
            ThrowIfDisposed();

            if (layer < 0)
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot be negative.");
            if (layer > 63)
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot exceed 63.");

            _nodeLock.EnterWriteLock();
            try
            {
                if (_neighbors.ContainsKey(layer))
                {
                    if (_neighbors[layer].Remove(neighborId))
                    {
                        _isDirty = true;
                        if (_neighbors[layer].Count == 0)
                            _neighbors.Remove(layer);
                        SaveNeighborsToDatabase();
                    }
                }
            }
            finally
            {
                _nodeLock.ExitWriteLock();
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
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot be negative.");
            if (layer > 63)
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot exceed 63.");

            _nodeLock.EnterReadLock();
            try
            {
                return _neighbors.TryGetValue(layer, out var layerNeighbors) ? layerNeighbors.Count : 0;
            }
            finally
            {
                _nodeLock.ExitReadLock();
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

            _nodeLock.EnterReadLock();
            try
            {
                return _neighbors.Values.Sum(set => set.Count);
            }
            finally
            {
                _nodeLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Checks if a specific neighbor exists at the given layer.
        /// Thread-safe operation.
        /// </summary>
        /// <param name="layer">The layer number. Minimum: 0, Maximum: 63.</param>
        /// <param name="neighborId">The neighbor ID to check.</param>
        /// <returns>true if the neighbor exists at the specified layer; otherwise, false.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when layer is negative or exceeds maximum.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the node has been disposed.</exception>
        public bool HasNeighbor(int layer, Guid neighborId)
        {
            ThrowIfDisposed();

            if (layer < 0)
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot be negative.");
            if (layer > 63)
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot exceed 63.");

            _nodeLock.EnterReadLock();
            try
            {
                return _neighbors.TryGetValue(layer, out var layerNeighbors) && layerNeighbors.Contains(neighborId);
            }
            finally
            {
                _nodeLock.ExitReadLock();
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

            _nodeLock.EnterWriteLock();
            try
            {
                if (_isDirty)
                {
                    SaveNeighborsToDatabase();
                    _isDirty = false;
                }
            }
            finally
            {
                _nodeLock.ExitWriteLock();
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

        // Protected methods
        /// <summary>
        /// Disposes of the node's resources.
        /// </summary>
        /// <param name="disposing">true if disposing managed resources; otherwise, false.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
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
                    _nodeLock?.Dispose();
                }
                _disposed = true;
            }
        }

        // Private methods
        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SqliteHnswNode));
        }

        private void LoadNeighborsFromDatabase()
        {
            try
            {
                var command = _connection.CreateCommand();
                command.CommandText = $"SELECT neighbors_json FROM {_tableName} WHERE node_id = @nodeId";
                command.Parameters.AddWithValue("@nodeId", _id.ToString());

                var result = command.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                {
                    var json = result.ToString();
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var neighborData = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
                        foreach (var kvp in neighborData)
                        {
                            if (int.TryParse(kvp.Key, out int layer))
                            {
                                _neighbors[layer] = new HashSet<Guid>();
                                foreach (var neighborIdStr in kvp.Value)
                                {
                                    if (Guid.TryParse(neighborIdStr, out Guid neighborId))
                                    {
                                        _neighbors[layer].Add(neighborId);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // If loading fails, start with empty neighbors
                _neighbors.Clear();
            }
        }

        private void SaveNeighborsToDatabase()
        {
            try
            {
                var neighborData = new Dictionary<string, List<string>>();
                foreach (var kvp in _neighbors)
                {
                    neighborData[kvp.Key.ToString()] = kvp.Value.Select(g => g.ToString()).ToList();
                }

                var json = JsonSerializer.Serialize(neighborData);

                var command = _connection.CreateCommand();
                command.CommandText = $@"
                    INSERT OR REPLACE INTO {_tableName} (node_id, neighbors_json) 
                    VALUES (@nodeId, @neighborsJson)";
                command.Parameters.AddWithValue("@nodeId", _id.ToString());
                command.Parameters.AddWithValue("@neighborsJson", json);

                command.ExecuteNonQuery();
                _isDirty = false;
            }
            catch
            {
                // Mark as dirty if save fails so we can retry later
                _isDirty = true;
                throw;
            }
        }
    }
}