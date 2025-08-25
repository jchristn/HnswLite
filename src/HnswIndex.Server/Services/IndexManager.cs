namespace HnswIndex.Server.Services
{
    using System.Collections.Concurrent;
    using System.Diagnostics;
    using Hnsw;
    using Hnsw.RamStorage;
    using Hnsw.SqliteStorage;
    using HnswIndex.SqliteStorage;
    using HnswIndex.Server.Classes;
    using SyslogLogging;

    /// <summary>
    /// Service for managing HNSW indexes.
    /// </summary>
    public class IndexManager : IDisposable
    {
        #region Public-Members

        #endregion

        #region Private-Members

        private static readonly string _Header = "[IndexManager] ";
        private readonly ConcurrentDictionary<string, IndexMetadata> _Indexes = new ConcurrentDictionary<string, IndexMetadata>();
        private readonly string _SqliteDirectory = string.Empty;
        private readonly LoggingModule? _Logging;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the IndexManager class.
        /// </summary>
        /// <param name="sqliteDirectory">Directory for SQLite index storage.</param>
        /// <param name="logging">Logging module.</param>
        public IndexManager(string sqliteDirectory, LoggingModule? logging = null)
        {
            ArgumentNullException.ThrowIfNull(sqliteDirectory);
            _SqliteDirectory = sqliteDirectory;
            _Logging = logging;

            if (!Directory.Exists(_SqliteDirectory))
            {
                Directory.CreateDirectory(_SqliteDirectory);
            }

            _Logging?.Info(_Header + $"initialized with SQLite directory: {_SqliteDirectory}");
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a new HNSW index.
        /// </summary>
        /// <param name="request">Create index request.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Index response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="ArgumentException">Thrown when request parameters are invalid.</exception>
        /// <exception cref="InvalidOperationException">Thrown when index with same name already exists.</exception>
        public async Task<IndexResponse> CreateIndexAsync(CreateIndexRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (string.IsNullOrEmpty(request.Name)) throw new ArgumentException("Index name cannot be null or empty.", nameof(request));
            if (request.Dimension < 1) throw new ArgumentException("Dimension must be greater than zero.", nameof(request));

            cancellationToken.ThrowIfCancellationRequested();

            if (_Indexes.ContainsKey(request.Name))
            {
                throw new InvalidOperationException($"Index with name '{request.Name}' already exists.");
            }

            IndexMetadata metadata = new IndexMetadata
            {
                GUID = System.Guid.NewGuid(),
                Name = request.Name,
                Dimension = request.Dimension,
                StorageType = request.StorageType,
                DistanceFunction = request.DistanceFunction,
                M = request.M,
                MaxM = request.MaxM,
                EfConstruction = request.EfConstruction,
                CreatedUtc = DateTime.UtcNow
            };

            HnswIndex index = await CreateHnswIndexAsync(metadata, cancellationToken).ConfigureAwait(false);
            metadata.Index = index;

            _Indexes.TryAdd(request.Name, metadata);
            _Logging?.Info(_Header + $"created index '{request.Name}' with {request.Dimension}D vectors using {request.StorageType} storage");

            return new IndexResponse
            {
                GUID = metadata.GUID,
                Name = metadata.Name,
                Dimension = metadata.Dimension,
                StorageType = metadata.StorageType,
                DistanceFunction = metadata.DistanceFunction,
                M = metadata.M,
                MaxM = metadata.MaxM,
                EfConstruction = metadata.EfConstruction,
                VectorCount = 0,
                CreatedUtc = metadata.CreatedUtc
            };
        }

        /// <summary>
        /// Get information about an index.
        /// </summary>
        /// <param name="indexName">Index name.</param>
        /// <returns>Index response or null if not found.</returns>
        /// <exception cref="ArgumentNullException">Thrown when indexName is null.</exception>
        public IndexResponse? GetIndex(string indexName)
        {
            ArgumentNullException.ThrowIfNull(indexName);

            if (!_Indexes.TryGetValue(indexName, out IndexMetadata? metadata))
            {
                return null;
            }

            return new IndexResponse
            {
                GUID = metadata.GUID,
                Name = metadata.Name,
                Dimension = metadata.Dimension,
                StorageType = metadata.StorageType,
                DistanceFunction = metadata.DistanceFunction,
                M = metadata.M,
                MaxM = metadata.MaxM,
                EfConstruction = metadata.EfConstruction,
                VectorCount = metadata.VectorCount,
                CreatedUtc = metadata.CreatedUtc
            };
        }

        /// <summary>
        /// List all indexes.
        /// </summary>
        /// <returns>List of index responses.</returns>
        public List<IndexResponse> ListIndexes()
        {
            List<IndexResponse> results = new List<IndexResponse>();

            foreach (IndexMetadata metadata in _Indexes.Values)
            {
                results.Add(new IndexResponse
                {
                    GUID = metadata.GUID,
                    Name = metadata.Name,
                    Dimension = metadata.Dimension,
                    StorageType = metadata.StorageType,
                    DistanceFunction = metadata.DistanceFunction,
                    M = metadata.M,
                    MaxM = metadata.MaxM,
                    EfConstruction = metadata.EfConstruction,
                    VectorCount = metadata.VectorCount,
                    CreatedUtc = metadata.CreatedUtc
                });
            }

            return results;
        }

        /// <summary>
        /// Delete an index.
        /// </summary>
        /// <param name="indexName">Index name.</param>
        /// <returns>True if deleted, false if not found.</returns>
        /// <exception cref="ArgumentNullException">Thrown when indexName is null.</exception>
        public bool DeleteIndex(string indexName)
        {
            ArgumentNullException.ThrowIfNull(indexName);

            if (!_Indexes.TryRemove(indexName, out IndexMetadata? metadata))
            {
                _Logging?.Warn(_Header + $"attempted to delete non-existent index: '{indexName}'");
                return false;
            }

            metadata.Dispose();
            _Logging?.Info(_Header + $"deleted index '{indexName}'");
            return true;
        }

        /// <summary>
        /// Add a vector to an index.
        /// </summary>
        /// <param name="indexName">Index name.</param>
        /// <param name="request">Add vector request.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if successful.</returns>
        /// <exception cref="ArgumentNullException">Thrown when parameters are null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when index not found or dimension mismatch.</exception>
        public async Task<bool> AddVectorAsync(string indexName, AddVectorRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(indexName);
            ArgumentNullException.ThrowIfNull(request);

            cancellationToken.ThrowIfCancellationRequested();

            if (!_Indexes.TryGetValue(indexName, out IndexMetadata? metadata))
            {
                throw new InvalidOperationException($"Index '{indexName}' not found.");
            }

            if (request.Vector.Count != metadata.Dimension)
            {
                throw new InvalidOperationException($"Vector dimension {request.Vector.Count} does not match index dimension {metadata.Dimension}.");
            }

            System.Guid vectorGuid = request.GUID;

            await metadata.Index.AddAsync(vectorGuid, request.Vector, cancellationToken).ConfigureAwait(false);
            metadata.VectorCount++;

            return true;
        }

        /// <summary>
        /// Add multiple vectors to an index.
        /// </summary>
        /// <param name="indexName">Index name.</param>
        /// <param name="request">Add vectors request.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if successful.</returns>
        /// <exception cref="ArgumentNullException">Thrown when parameters are null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when index not found or dimension mismatch.</exception>
        public async Task<bool> AddVectorsAsync(string indexName, AddVectorsRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(indexName);
            ArgumentNullException.ThrowIfNull(request);

            cancellationToken.ThrowIfCancellationRequested();

            if (!_Indexes.TryGetValue(indexName, out IndexMetadata? metadata))
            {
                throw new InvalidOperationException($"Index '{indexName}' not found.");
            }

            Dictionary<System.Guid, List<float>> vectors = new Dictionary<System.Guid, List<float>>();

            foreach (AddVectorRequest vectorRequest in request.Vectors)
            {
                if (vectorRequest.Vector.Count != metadata.Dimension)
                {
                    throw new InvalidOperationException($"Vector dimension {vectorRequest.Vector.Count} does not match index dimension {metadata.Dimension}.");
                }

                System.Guid vectorGuid = vectorRequest.GUID;
                vectors.Add(vectorGuid, vectorRequest.Vector);
            }

            await metadata.Index.AddNodesAsync(vectors, cancellationToken).ConfigureAwait(false);
            metadata.VectorCount += vectors.Count;

            return true;
        }

        /// <summary>
        /// Remove a vector from an index.
        /// </summary>
        /// <param name="indexName">Index name.</param>
        /// <param name="vectorGuid">Vector GUID.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if successful.</returns>
        /// <exception cref="ArgumentNullException">Thrown when parameters are null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when index not found.</exception>
        public async Task<bool> RemoveVectorAsync(string indexName, Guid vectorGuid, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(indexName);

            cancellationToken.ThrowIfCancellationRequested();

            if (!_Indexes.TryGetValue(indexName, out IndexMetadata? metadata))
            {
                _Logging?.Warn(_Header + $"remove vector request for missing index: '{indexName}'");
                throw new InvalidOperationException($"Index '{indexName}' not found.");
            }

            await metadata.Index.RemoveAsync(vectorGuid, cancellationToken).ConfigureAwait(false);
            metadata.VectorCount = Math.Max(0, metadata.VectorCount - 1);

            return true;
        }

        /// <summary>
        /// Search for nearest neighbors.
        /// </summary>
        /// <param name="indexName">Index name.</param>
        /// <param name="request">Search request.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Search response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when parameters are null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when index not found or dimension mismatch.</exception>
        public async Task<SearchResponse> SearchAsync(string indexName, SearchRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(indexName);
            ArgumentNullException.ThrowIfNull(request);

            cancellationToken.ThrowIfCancellationRequested();

            if (!_Indexes.TryGetValue(indexName, out IndexMetadata? metadata))
            {
                throw new InvalidOperationException($"Index '{indexName}' not found.");
            }

            if (request.Vector.Count != metadata.Dimension)
            {
                throw new InvalidOperationException($"Query vector dimension {request.Vector.Count} does not match index dimension {metadata.Dimension}.");
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            IEnumerable<VectorResult> results = await metadata.Index.GetTopKAsync(request.Vector, request.K, request.Ef, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            List<VectorSearchResult> searchResults = new List<VectorSearchResult>();
            foreach (VectorResult result in results)
            {
                searchResults.Add(new VectorSearchResult
                {
                    GUID = result.GUID,
                    Vector = result.Vectors,
                    Distance = result.Distance
                });
            }

            return new SearchResponse
            {
                Results = searchResults,
                SearchTimeMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2)
            };
        }

        /// <summary>
        /// Dispose of the index manager.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        private async Task<HnswIndex> CreateHnswIndexAsync(IndexMetadata metadata, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            HnswIndex index;

            if (string.Equals(metadata.StorageType, "RAM", StringComparison.OrdinalIgnoreCase))
            {
                RamHnswStorage storage = new RamHnswStorage();
                RamHnswLayerStorage layerStorage = new RamHnswLayerStorage();
                index = new HnswIndex(metadata.Dimension, storage, layerStorage);
            }
            else if (string.Equals(metadata.StorageType, "SQLite", StringComparison.OrdinalIgnoreCase))
            {
                string dbPath = Path.Combine(_SqliteDirectory, $"{metadata.Name}.db");
                SqliteHnswStorage storage = new SqliteHnswStorage(dbPath);
                SqliteHnswLayerStorage layerStorage = new SqliteHnswLayerStorage(storage.Connection);
                index = new HnswIndex(metadata.Dimension, storage, layerStorage);
                metadata.StorageObjects = new List<IDisposable> { storage, layerStorage };
            }
            else
            {
                throw new ArgumentException($"Invalid storage type: {metadata.StorageType}");
            }

            index.M = metadata.M;
            index.MaxM = metadata.MaxM;
            index.EfConstruction = metadata.EfConstruction;

            switch (metadata.DistanceFunction.ToLowerInvariant())
            {
                case "euclidean":
                    index.DistanceFunction = new EuclideanDistance();
                    break;
                case "cosine":
                    index.DistanceFunction = new CosineDistance();
                    break;
                case "dotproduct":
                    index.DistanceFunction = new DotProductDistance();
                    break;
                default:
                    index.DistanceFunction = new EuclideanDistance();
                    break;
            }

            return await Task.FromResult(index).ConfigureAwait(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_Disposed)
            {
                if (disposing)
                {
                    foreach (IndexMetadata metadata in _Indexes.Values)
                    {
                        metadata.Dispose();
                    }
                    _Indexes.Clear();
                }

                _Disposed = true;
            }
        }

        #endregion

        #region Private-Classes

        /// <summary>
        /// Metadata for an HNSW index.
        /// </summary>
        private class IndexMetadata : IDisposable
        {
            public Guid GUID { get; set; }
            public string Name { get; set; } = string.Empty;
            public int Dimension { get; set; } = 0;
            public string StorageType { get; set; } = string.Empty;
            public string DistanceFunction { get; set; } = string.Empty;
            public int M { get; set; } = 0;
            public int MaxM { get; set; } = 0;
            public int EfConstruction { get; set; } = 0;
            public int VectorCount { get; set; } = 0;
            public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
            public HnswIndex Index { get; set; } = null!;
            public List<IDisposable>? StorageObjects { get; set; } = null;

            public void Dispose()
            {
                if (StorageObjects != null)
                {
                    foreach (IDisposable obj in StorageObjects)
                    {
                        obj.Dispose();
                    }
                }
            }
        }

        #endregion
    }
}