namespace HnswIndex.Server.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
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

            ReloadPersistedIndexes();
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
        /// Enumerate indexes using the supplied query for filtering, sorting, and pagination.
        /// </summary>
        /// <param name="query">Enumeration parameters. Must not be null.</param>
        /// <returns>Paginated enumeration result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> is null.</exception>
        public EnumerationResult<IndexResponse> EnumerateIndexes(EnumerationQuery query)
        {
            ArgumentNullException.ThrowIfNull(query);

            IEnumerable<IndexMetadata> filtered = _Indexes.Values;

            if (!string.IsNullOrEmpty(query.Prefix))
            {
                string prefix = query.Prefix;
                filtered = filtered.Where(m => m.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrEmpty(query.Suffix))
            {
                string suffix = query.Suffix;
                filtered = filtered.Where(m => m.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            }
            if (query.CreatedAfterUtc.HasValue)
            {
                DateTime after = query.CreatedAfterUtc.Value;
                filtered = filtered.Where(m => m.CreatedUtc > after);
            }
            if (query.CreatedBeforeUtc.HasValue)
            {
                DateTime before = query.CreatedBeforeUtc.Value;
                filtered = filtered.Where(m => m.CreatedUtc < before);
            }

            filtered = query.Ordering switch
            {
                EnumerationOrderEnum.CreatedAscending => filtered.OrderBy(m => m.CreatedUtc),
                EnumerationOrderEnum.NameAscending => filtered.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase),
                EnumerationOrderEnum.NameDescending => filtered.OrderByDescending(m => m.Name, StringComparer.OrdinalIgnoreCase),
                _ => filtered.OrderByDescending(m => m.CreatedUtc),
            };

            List<IndexResponse> all = filtered
                .Select(m => new IndexResponse
                {
                    GUID = m.GUID,
                    Name = m.Name,
                    Dimension = m.Dimension,
                    StorageType = m.StorageType,
                    DistanceFunction = m.DistanceFunction,
                    M = m.M,
                    MaxM = m.MaxM,
                    EfConstruction = m.EfConstruction,
                    VectorCount = m.VectorCount,
                    CreatedUtc = m.CreatedUtc,
                })
                .ToList();

            return EnumerationResult<IndexResponse>.FromQuery(query, all);
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
        /// <summary>
        /// Retrieve a single vector (including its values) from an index.
        /// Returns null when the vector is absent.
        /// </summary>
        /// <param name="indexName">Index name.</param>
        /// <param name="vectorGuid">Vector identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The vector entry, or null when not present.</returns>
        public async Task<VectorEntryResponse?> GetVectorAsync(
            string indexName,
            Guid vectorGuid,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(indexName);

            if (!_Indexes.TryGetValue(indexName, out IndexMetadata? metadata) || metadata == null)
            {
                throw new InvalidOperationException($"Index '{indexName}' not found.");
            }

            IStorageProvider? provider = null;
            if (metadata.StorageObjects != null)
            {
                foreach (IDisposable obj in metadata.StorageObjects)
                {
                    if (obj is IStorageProvider sp) { provider = sp; break; }
                }
            }
            if (provider == null)
            {
                throw new InvalidOperationException($"Index '{indexName}' has no accessible storage provider.");
            }

            TryGetNodeResult r = await provider.TryGetNodeAsync(vectorGuid, cancellationToken).ConfigureAwait(false);
            if (!r.Success || r.Node == null) return null;

            return NodeToEntry(r.Node, includeVector: true);
        }

        public async Task<EnumerationResult<VectorEntryResponse>> EnumerateVectorsAsync(
            string indexName,
            EnumerationQuery query,
            bool includeVectors,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(indexName);
            ArgumentNullException.ThrowIfNull(query);

            if (!_Indexes.TryGetValue(indexName, out IndexMetadata? metadata) || metadata == null)
            {
                throw new InvalidOperationException($"Index '{indexName}' not found.");
            }

            IStorageProvider? provider = null;
            if (metadata.StorageObjects != null)
            {
                foreach (IDisposable obj in metadata.StorageObjects)
                {
                    if (obj is IStorageProvider sp) { provider = sp; break; }
                }
            }
            if (provider == null)
            {
                throw new InvalidOperationException($"Index '{indexName}' has no accessible storage provider.");
            }

            IEnumerable<Guid> allIds = await provider.GetAllNodeIdsAsync(cancellationToken).ConfigureAwait(false);
            List<Guid> sorted = allIds.ToList();
            sorted.Sort();

            List<Guid> filtered = sorted;
            if (!string.IsNullOrEmpty(query.Prefix))
            {
                string prefix = query.Prefix;
                filtered = sorted.Where(g => g.ToString().StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            // When a label/tag filter is present we must fetch every candidate node's metadata
            // (not just the current page) so that pagination math reflects the post-filter set.
            bool hasMetadataFilter = (query.Labels != null && query.Labels.Count > 0)
                                     || (query.Tags != null && query.Tags.Count > 0);
            long filteredOut = 0;
            Dictionary<Guid, IHnswNode>? prefetchedNodes = null;

            if (hasMetadataFilter && filtered.Count > 0)
            {
                prefetchedNodes = await provider.GetNodesAsync(filtered, cancellationToken).ConfigureAwait(false);
                List<Guid> postMetadata = new List<Guid>(filtered.Count);
                foreach (Guid id in filtered)
                {
                    prefetchedNodes.TryGetValue(id, out IHnswNode? node);
                    if (MetadataFilter.Matches(node, query.Labels, query.Tags, query.CaseInsensitive))
                    {
                        postMetadata.Add(id);
                    }
                    else
                    {
                        filteredOut++;
                    }
                }
                filtered = postMetadata;
            }

            int skip = Math.Min(query.Skip, filtered.Count);
            int take = Math.Min(query.MaxResults, Math.Max(0, filtered.Count - skip));
            List<Guid> page = filtered.GetRange(skip, take);

            List<VectorEntryResponse> objects = new List<VectorEntryResponse>(page.Count);
            // Always fetch nodes so metadata (Name/Labels/Tags) is populated.
            // Vector bodies are included only when the caller requests them.
            // Reuse the prefetched node map when available to avoid a second round-trip.
            Dictionary<Guid, IHnswNode> nodes;
            if (prefetchedNodes != null)
            {
                nodes = prefetchedNodes;
            }
            else
            {
                nodes = page.Count > 0
                    ? await provider.GetNodesAsync(page, cancellationToken).ConfigureAwait(false)
                    : new Dictionary<Guid, IHnswNode>();
            }
            foreach (Guid id in page)
            {
                if (nodes.TryGetValue(id, out IHnswNode? node) && node != null)
                {
                    objects.Add(NodeToEntry(node, includeVectors));
                }
                else
                {
                    objects.Add(new VectorEntryResponse { GUID = id });
                }
            }

            long remaining = Math.Max(0, (long)filtered.Count - skip - take);
            return new EnumerationResult<VectorEntryResponse>
            {
                Success = true,
                MaxResults = query.MaxResults,
                Skip = skip,
                TotalRecords = filtered.Count,
                RecordsRemaining = remaining,
                EndOfResults = remaining == 0,
                ContinuationToken = null,
                TimestampUtc = DateTime.UtcNow,
                FilteredCount = filteredOut,
                Objects = objects,
            };
        }

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

            // Set optional metadata on the node post-creation.
            if (request.Name != null || request.Labels != null || request.Tags != null)
            {
                IStorageProvider? provider = GetProvider(metadata);
                if (provider != null)
                {
                    IHnswNode node = await provider.GetNodeAsync(vectorGuid, cancellationToken).ConfigureAwait(false);
                    node.Name = request.Name;
                    node.Labels = request.Labels;
                    node.Tags = request.Tags;
                }
            }

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

            // Set metadata on each vector that has any non-null fields.
            IStorageProvider? batchProvider = GetProvider(metadata);
            if (batchProvider != null)
            {
                foreach (AddVectorRequest vr in request.Vectors)
                {
                    if (vr.Name != null || vr.Labels != null || vr.Tags != null)
                    {
                        IHnswNode node = await batchProvider.GetNodeAsync(vr.GUID, cancellationToken).ConfigureAwait(false);
                        node.Name = vr.Name;
                        node.Labels = vr.Labels;
                        node.Tags = vr.Tags;
                    }
                }
            }

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
            // Batch-fetch nodes to populate metadata alongside the vector results.
            IStorageProvider? searchProvider = GetProvider(metadata);
            List<Guid> resultIds = results.Select(r => r.GUID).ToList();
            Dictionary<Guid, IHnswNode> nodeMap = (searchProvider != null && resultIds.Count > 0)
                ? await searchProvider.GetNodesAsync(resultIds, cancellationToken).ConfigureAwait(false)
                : new Dictionary<Guid, IHnswNode>();

            bool hasFilter = (request.Labels != null && request.Labels.Count > 0)
                             || (request.Tags != null && request.Tags.Count > 0);
            int filteredOut = 0;

            foreach (VectorResult result in results)
            {
                IHnswNode? n = null;
                nodeMap.TryGetValue(result.GUID, out n);

                if (hasFilter && !MetadataFilter.Matches(n, request.Labels, request.Tags, request.CaseInsensitive))
                {
                    filteredOut++;
                    continue;
                }

                VectorSearchResult sr = new VectorSearchResult
                {
                    GUID = result.GUID,
                    Vector = result.Vectors,
                    Distance = result.Distance,
                };
                if (n != null)
                {
                    sr.Name = n.Name;
                    sr.Labels = n.Labels;
                    sr.Tags = n.Tags;
                }
                searchResults.Add(sr);
            }

            return new SearchResponse
            {
                Results = searchResults,
                SearchTimeMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
                FilteredCount = filteredOut
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
                RamStorageProvider provider = new RamStorageProvider();
                index = new HnswIndex(metadata.Dimension, provider);
                metadata.StorageObjects = new List<IDisposable> { provider };
            }
            else if (string.Equals(metadata.StorageType, "SQLite", StringComparison.OrdinalIgnoreCase))
            {
                string dbPath = Path.Combine(_SqliteDirectory, $"{metadata.Name}.db");
                SqliteStorageProvider provider = new SqliteStorageProvider(dbPath);
                index = new HnswIndex(metadata.Dimension, provider);
                metadata.StorageObjects = new List<IDisposable> { provider };

                // Persist server-level metadata into the index's SQLite file so the index
                // self-describes across restarts. The library uses hnsw_metadata as a
                // key/value table; the server writes its own namespaced keys alongside.
                WriteServerMetadata(provider.Connection, metadata);
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

        private const string _MetaKeyGuid = "server.guid";
        private const string _MetaKeyDimension = "server.dimension";
        private const string _MetaKeyStorageType = "server.storage_type";
        private const string _MetaKeyDistanceFn = "server.distance_function";
        private const string _MetaKeyM = "server.m";
        private const string _MetaKeyMaxM = "server.max_m";
        private const string _MetaKeyEfC = "server.ef_construction";
        private const string _MetaKeyCreatedUtc = "server.created_utc";

        private static void WriteServerMetadata(Microsoft.Data.Sqlite.SqliteConnection conn, IndexMetadata m)
        {
            Dictionary<string, string> kv = new Dictionary<string, string>
            {
                [_MetaKeyGuid] = m.GUID.ToString(),
                [_MetaKeyDimension] = m.Dimension.ToString(System.Globalization.CultureInfo.InvariantCulture),
                [_MetaKeyStorageType] = m.StorageType,
                [_MetaKeyDistanceFn] = m.DistanceFunction,
                [_MetaKeyM] = m.M.ToString(System.Globalization.CultureInfo.InvariantCulture),
                [_MetaKeyMaxM] = m.MaxM.ToString(System.Globalization.CultureInfo.InvariantCulture),
                [_MetaKeyEfC] = m.EfConstruction.ToString(System.Globalization.CultureInfo.InvariantCulture),
                [_MetaKeyCreatedUtc] = m.CreatedUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            };

            foreach (KeyValuePair<string, string> pair in kv)
            {
                using Microsoft.Data.Sqlite.SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT OR REPLACE INTO hnsw_metadata (key, value, updated_at) VALUES ($k, $v, CURRENT_TIMESTAMP)";
                cmd.Parameters.AddWithValue("$k", pair.Key);
                cmd.Parameters.AddWithValue("$v", pair.Value);
                cmd.ExecuteNonQuery();
            }
        }

        private static Dictionary<string, string>? ReadServerMetadata(Microsoft.Data.Sqlite.SqliteConnection conn)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            using Microsoft.Data.Sqlite.SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT key, value FROM hnsw_metadata WHERE key LIKE 'server.%'";
            using Microsoft.Data.Sqlite.SqliteDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result[reader.GetString(0)] = reader.GetString(1);
            }
            return result.Count == 0 ? null : result;
        }

        private void ReloadPersistedIndexes()
        {
            string[] dbFiles;
            try
            {
                dbFiles = Directory.GetFiles(_SqliteDirectory, "*.db");
            }
            catch (Exception ex)
            {
                _Logging?.Warn(_Header + $"cannot enumerate SQLite directory: {ex.Message}");
                return;
            }

            int loaded = 0;
            foreach (string dbPath in dbFiles)
            {
                string name = Path.GetFileNameWithoutExtension(dbPath);
                if (string.IsNullOrWhiteSpace(name)) continue;

                try
                {
                    SqliteStorageProvider provider = new SqliteStorageProvider(dbPath, createIfNotExists: false);

                    Dictionary<string, string>? meta = ReadServerMetadata(provider.Connection);
                    if (meta == null)
                    {
                        // No server-written metadata (e.g., a db created before this fix). Skip
                        // rather than guess — the index can be rebuilt explicitly by the user.
                        _Logging?.Warn(_Header + $"skipping '{name}': no server metadata in {Path.GetFileName(dbPath)}");
                        provider.Dispose();
                        continue;
                    }

                    IndexMetadata im = new IndexMetadata
                    {
                        Name = name,
                        GUID = meta.TryGetValue(_MetaKeyGuid, out string? g) && Guid.TryParse(g, out Guid gp) ? gp : Guid.NewGuid(),
                        Dimension = meta.TryGetValue(_MetaKeyDimension, out string? d) && int.TryParse(d, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int dp) ? dp : 0,
                        StorageType = meta.TryGetValue(_MetaKeyStorageType, out string? st) ? st : "SQLite",
                        DistanceFunction = meta.TryGetValue(_MetaKeyDistanceFn, out string? df) ? df : "Euclidean",
                        M = meta.TryGetValue(_MetaKeyM, out string? ms) && int.TryParse(ms, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int mp) ? mp : 16,
                        MaxM = meta.TryGetValue(_MetaKeyMaxM, out string? mxs) && int.TryParse(mxs, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int mxp) ? mxp : 32,
                        EfConstruction = meta.TryGetValue(_MetaKeyEfC, out string? efs) && int.TryParse(efs, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int efp) ? efp : 200,
                        CreatedUtc = meta.TryGetValue(_MetaKeyCreatedUtc, out string? cu) && DateTime.TryParse(cu, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime cp) ? cp : DateTime.UtcNow,
                    };

                    if (im.Dimension < 1)
                    {
                        _Logging?.Warn(_Header + $"skipping '{name}': invalid persisted dimension");
                        provider.Dispose();
                        continue;
                    }

                    HnswIndex index = new HnswIndex(im.Dimension, provider);
                    index.M = im.M;
                    index.MaxM = im.MaxM;
                    index.EfConstruction = im.EfConstruction;
                    index.DistanceFunction = im.DistanceFunction.ToLowerInvariant() switch
                    {
                        "cosine" => new CosineDistance(),
                        "dotproduct" => new DotProductDistance(),
                        _ => new EuclideanDistance(),
                    };

                    im.Index = index;
                    im.StorageObjects = new List<IDisposable> { provider };
                    im.VectorCount = CountVectorsBestEffort(provider);

                    _Indexes.TryAdd(name, im);
                    loaded++;
                    _Logging?.Info(_Header + $"reloaded index '{name}' ({im.Dimension}-d, {im.VectorCount} vectors)");
                }
                catch (Exception ex)
                {
                    _Logging?.Warn(_Header + $"failed to reload index from {Path.GetFileName(dbPath)}: {ex.Message}");
                }
            }

            if (loaded > 0) _Logging?.Info(_Header + $"reloaded {loaded} persisted index(es) from disk");
        }

        private static IStorageProvider? GetProvider(IndexMetadata metadata)
        {
            if (metadata.StorageObjects == null) return null;
            foreach (IDisposable obj in metadata.StorageObjects)
            {
                if (obj is IStorageProvider sp) return sp;
            }
            return null;
        }

        private static VectorEntryResponse NodeToEntry(IHnswNode node, bool includeVector)
        {
            return new VectorEntryResponse
            {
                GUID = node.Id,
                Vector = includeVector ? new List<float>(node.Vector) : null,
                Name = node.Name,
                Labels = node.Labels != null ? new List<string>(node.Labels) : null,
                Tags = node.Tags != null ? new Dictionary<string, object>(node.Tags) : null,
            };
        }

        private static int CountVectorsBestEffort(SqliteStorageProvider provider)
        {
            try
            {
                return provider.GetAllNodeLayers().Count;
            }
            catch
            {
                return 0;
            }
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