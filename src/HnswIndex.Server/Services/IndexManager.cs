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
    using HnswIndex.PostgresqlStorage;
    using HnswIndex.SqliteStorage;
    using HnswIndex.Server.Classes;
    using HnswIndex.Server.Telemetry;
    using Npgsql;
    using SyslogLogging;

    /// <summary>
    /// Service for managing HNSW indexes.
    /// </summary>
    public class IndexManager : IAsyncDisposable
    {
        #region Public-Members

        #endregion

        #region Private-Members

        private static readonly string _Header = "[IndexManager] ";
        private readonly ConcurrentDictionary<string, IndexMetadata> _Indexes = new ConcurrentDictionary<string, IndexMetadata>();
        private readonly StorageSettings _StorageSettings;
        private readonly string _SqliteDirectory = string.Empty;
        private readonly LoggingModule? _Logging;
        private NpgsqlDataSource? _PostgresqlDataSource;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the IndexManager class.
        /// </summary>
        /// <param name="sqliteDirectory">Directory for SQLite index storage.</param>
        /// <param name="logging">Logging module.</param>
        public IndexManager(string sqliteDirectory, LoggingModule? logging = null)
            : this(new StorageSettings
            {
                DefaultStorageType = "PostgreSQL",
                SqliteDirectory = sqliteDirectory,
                PostgresqlConnectionString = "Host=localhost;Port=5432;Database=hnswlite;Username=hnswlite;Password=hnswlite",
                PostgresqlAutoProvision = true
            }, logging)
        {
        }

        /// <summary>
        /// Initializes a new instance of the IndexManager class.
        /// </summary>
        /// <param name="storageSettings">Storage settings.</param>
        /// <param name="logging">Logging module.</param>
        public IndexManager(StorageSettings storageSettings, LoggingModule? logging = null)
        {
            ArgumentNullException.ThrowIfNull(storageSettings);
            _StorageSettings = storageSettings;
            _SqliteDirectory = storageSettings.SqliteDirectory;
            _Logging = logging;

            if (!Directory.Exists(_SqliteDirectory))
            {
                Directory.CreateDirectory(_SqliteDirectory);
            }

            _Logging?.Info(_Header + $"initialized with default storage: {_StorageSettings.DefaultStorageType}");
            _Logging?.Info(_Header + $"SQLite directory: {_SqliteDirectory}");
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
            using (ServerOperationScope scope = ServerOperationScope.Start(ServerTelemetryNames.OperationIndexCreate, request?.Name))
            {
                try
                {
                    scope.StorageType = string.IsNullOrWhiteSpace(request?.StorageType) ? _StorageSettings.DefaultStorageType : request.StorageType;
                    return await CreateIndexCoreAsync(request!, scope, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    scope.Fail(e);
                    throw;
                }
            }
        }

        private async Task<IndexResponse> CreateIndexCoreAsync(CreateIndexRequest request, ServerOperationScope scope, CancellationToken cancellationToken)
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
                StorageType = string.IsNullOrWhiteSpace(request.StorageType) ? _StorageSettings.DefaultStorageType : request.StorageType,
                DistanceFunction = request.DistanceFunction,
                M = request.M,
                MaxM = request.MaxM,
                EfConstruction = request.EfConstruction,
                CreatedUtc = DateTime.UtcNow
            };

            scope.BeginStage(ServerTelemetryNames.StageStorageOpen);
            HnswIndex index = await CreateHnswIndexAsync(metadata, cancellationToken, scope).ConfigureAwait(false);
            metadata.Index = index;
            scope.EndStage(null);

            _Indexes.TryAdd(request.Name, metadata);
            _Logging?.Info(_Header + $"created index '{request.Name}' with {request.Dimension}D vectors using {metadata.StorageType} storage");

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
            using (ServerOperationScope scope = ServerOperationScope.Start(ServerTelemetryNames.OperationIndexGet, indexName))
            {
                try
                {
                    return GetIndexCore(indexName, scope);
                }
                catch (Exception e)
                {
                    scope.Fail(e);
                    throw;
                }
            }
        }

        private IndexResponse? GetIndexCore(string indexName, ServerOperationScope scope)
        {
            ArgumentNullException.ThrowIfNull(indexName);

            if (!_Indexes.TryGetValue(indexName, out IndexMetadata? metadata))
            {
                return null;
            }

            scope.StorageType = metadata.StorageType;

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
            using (ServerOperationScope scope = ServerOperationScope.Start(ServerTelemetryNames.OperationIndexList))
            {
                try
                {
                    EnumerationResult<IndexResponse> result = EnumerateIndexesCore(query);
                    scope.SetTag(ServerTelemetryNames.AttributeResultCount, result.Objects?.Count ?? 0);
                    return result;
                }
                catch (Exception e)
                {
                    scope.Fail(e);
                    throw;
                }
            }
        }

        private EnumerationResult<IndexResponse> EnumerateIndexesCore(EnumerationQuery query)
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
        public async Task<bool> DeleteIndexAsync(string indexName)
        {
            using (ServerOperationScope scope = ServerOperationScope.Start(ServerTelemetryNames.OperationIndexDelete, indexName))
            {
                try
                {
                    return await DeleteIndexCoreAsync(indexName, scope).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    scope.Fail(e);
                    throw;
                }
            }
        }

        private async Task<bool> DeleteIndexCoreAsync(string indexName, ServerOperationScope scope)
        {
            ArgumentNullException.ThrowIfNull(indexName);

            if (!_Indexes.TryRemove(indexName, out IndexMetadata? metadata))
            {
                _Logging?.Warn(_Header + $"attempted to delete non-existent index: '{indexName}'");
                return false;
            }

            scope.StorageType = metadata.StorageType;
            scope.BeginStage(ServerTelemetryNames.StageDispose);
            await metadata.DisposeAsync().ConfigureAwait(false);
            scope.EndStage(null);
            _Logging?.Info(_Header + $"deleted index '{indexName}'");
            return true;
        }

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
            using (ServerOperationScope scope = ServerOperationScope.Start(ServerTelemetryNames.OperationVectorGet, indexName))
            {
                try
                {
                    scope.SetTag(ServerTelemetryNames.AttributeVectorId, vectorGuid.ToString());
                    return await GetVectorCoreAsync(indexName, vectorGuid, scope, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    scope.Fail(e);
                    throw;
                }
            }
        }

        private async Task<VectorEntryResponse?> GetVectorCoreAsync(string indexName, Guid vectorGuid, ServerOperationScope scope, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(indexName);

            if (!_Indexes.TryGetValue(indexName, out IndexMetadata? metadata) || metadata == null)
            {
                throw new InvalidOperationException($"Index '{indexName}' not found.");
            }

            scope.StorageType = metadata.StorageType;

            IStorageProvider? provider = GetProvider(metadata);
            if (provider == null)
            {
                throw new InvalidOperationException($"Index '{indexName}' has no accessible storage provider.");
            }

            scope.BeginStage(ServerTelemetryNames.StageNodeFetch);
            TryGetNodeResult r = await provider.TryGetNodeAsync(vectorGuid, cancellationToken).ConfigureAwait(false);
            scope.EndStage(null);
            if (!r.Success || r.Node == null) return null;

            return NodeToEntry(r.Node, includeVector: true);
        }

        /// <summary>
        /// Enumerate the vectors of an index with prefix and label/tag filtering and pagination.
        /// </summary>
        /// <param name="indexName">Index name.</param>
        /// <param name="query">Enumeration parameters.</param>
        /// <param name="includeVectors">Whether to include vector values.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Paginated enumeration result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when parameters are null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the index is not found.</exception>
        public async Task<EnumerationResult<VectorEntryResponse>> EnumerateVectorsAsync(
            string indexName,
            EnumerationQuery query,
            bool includeVectors,
            CancellationToken cancellationToken = default)
        {
            using (ServerOperationScope scope = ServerOperationScope.Start(ServerTelemetryNames.OperationVectorList, indexName))
            {
                try
                {
                    EnumerationResult<VectorEntryResponse> result = await EnumerateVectorsCoreAsync(indexName, query, includeVectors, scope, cancellationToken).ConfigureAwait(false);
                    scope.SetTag(ServerTelemetryNames.AttributeResultCount, result.Objects?.Count ?? 0);
                    return result;
                }
                catch (Exception e)
                {
                    scope.Fail(e);
                    throw;
                }
            }
        }

        private async Task<EnumerationResult<VectorEntryResponse>> EnumerateVectorsCoreAsync(
            string indexName,
            EnumerationQuery query,
            bool includeVectors,
            ServerOperationScope scope,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(indexName);
            ArgumentNullException.ThrowIfNull(query);

            if (!_Indexes.TryGetValue(indexName, out IndexMetadata? metadata) || metadata == null)
            {
                throw new InvalidOperationException($"Index '{indexName}' not found.");
            }

            scope.StorageType = metadata.StorageType;

            IStorageProvider? provider = GetProvider(metadata);
            if (provider == null)
            {
                throw new InvalidOperationException($"Index '{indexName}' has no accessible storage provider.");
            }

            scope.BeginStage(ServerTelemetryNames.StageEnumerateIds);
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
                scope.BeginStage(ServerTelemetryNames.StageFilter);
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
            scope.BeginStage(ServerTelemetryNames.StageMetadataFetch);
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

            scope.EndStage(null);
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

        /// <summary>
        /// Add a vector to an index.
        /// </summary>
        /// <param name="indexName">Index name.</param>
        /// <param name="request">Add vector request.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if successful.</returns>
        /// <exception cref="ArgumentNullException">Thrown when parameters are null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the index is not found.</exception>
        /// <exception cref="VectorDimensionMismatchException">Thrown when a vector length does not match the index dimension.</exception>
        public async Task<bool> AddVectorAsync(string indexName, AddVectorRequest request, CancellationToken cancellationToken = default)
        {
            using (ServerOperationScope scope = ServerOperationScope.Start(ServerTelemetryNames.OperationVectorAdd, indexName))
            {
                try
                {
                    if (request != null) scope.SetTag(ServerTelemetryNames.AttributeVectorId, request.GUID.ToString());
                    return await AddVectorCoreAsync(indexName, request!, scope, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    scope.Fail(e);
                    throw;
                }
            }
        }

        private async Task<bool> AddVectorCoreAsync(string indexName, AddVectorRequest request, ServerOperationScope scope, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(indexName);
            ArgumentNullException.ThrowIfNull(request);

            cancellationToken.ThrowIfCancellationRequested();

            if (!_Indexes.TryGetValue(indexName, out IndexMetadata? metadata))
            {
                throw new InvalidOperationException($"Index '{indexName}' not found.");
            }

            scope.StorageType = metadata.StorageType;

            if (request.Vector.Count != metadata.Dimension)
            {
                throw new VectorDimensionMismatchException($"Vector dimension {request.Vector.Count} does not match index dimension {metadata.Dimension}.", request.Vector.Count, metadata.Dimension);
            }

            System.Guid vectorGuid = request.GUID;

            scope.BeginStage(ServerTelemetryNames.StageInsert);
            await metadata.Index.AddAsync(vectorGuid, request.Vector, cancellationToken).ConfigureAwait(false);
            metadata.VectorCount++;
            scope.EndStage(null);

            // Set optional metadata on the node post-creation.
            if (request.Name != null || request.Labels != null || request.Tags != null)
            {
                scope.BeginStage(ServerTelemetryNames.StageMetadataWrite);
                IStorageProvider? provider = GetProvider(metadata);
                if (provider != null)
                {
                    IHnswNode node = await provider.GetNodeAsync(vectorGuid, cancellationToken).ConfigureAwait(false);
                    await node.SetMetadataAsync(request.Name, request.Labels, request.Tags, cancellationToken).ConfigureAwait(false);
                }
                scope.EndStage(null);
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
        /// <exception cref="InvalidOperationException">Thrown when the index is not found.</exception>
        /// <exception cref="VectorDimensionMismatchException">Thrown when a vector length does not match the index dimension.</exception>
        public async Task<bool> AddVectorsAsync(string indexName, AddVectorsRequest request, CancellationToken cancellationToken = default)
        {
            using (ServerOperationScope scope = ServerOperationScope.Start(ServerTelemetryNames.OperationVectorAddBatch, indexName))
            {
                try
                {
                    scope.SetTag(ServerTelemetryNames.AttributeBatchSize, request?.Vectors?.Count ?? 0);
                    return await AddVectorsCoreAsync(indexName, request!, scope, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    scope.Fail(e);
                    throw;
                }
            }
        }

        private async Task<bool> AddVectorsCoreAsync(string indexName, AddVectorsRequest request, ServerOperationScope scope, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(indexName);
            ArgumentNullException.ThrowIfNull(request);

            cancellationToken.ThrowIfCancellationRequested();

            if (!_Indexes.TryGetValue(indexName, out IndexMetadata? metadata))
            {
                throw new InvalidOperationException($"Index '{indexName}' not found.");
            }

            scope.StorageType = metadata.StorageType;

            Dictionary<System.Guid, List<float>> vectors = new Dictionary<System.Guid, List<float>>();

            foreach (AddVectorRequest vectorRequest in request.Vectors)
            {
                if (vectorRequest.Vector.Count != metadata.Dimension)
                {
                    throw new VectorDimensionMismatchException($"Vector dimension {vectorRequest.Vector.Count} does not match index dimension {metadata.Dimension}.", vectorRequest.Vector.Count, metadata.Dimension);
                }

                System.Guid vectorGuid = vectorRequest.GUID;
                vectors.Add(vectorGuid, vectorRequest.Vector);
            }

            scope.BeginStage(ServerTelemetryNames.StageInsert);
            await metadata.Index.AddNodesAsync(vectors, cancellationToken).ConfigureAwait(false);
            metadata.VectorCount += vectors.Count;

            // Set metadata on each vector that has any non-null fields.
            scope.BeginStage(ServerTelemetryNames.StageMetadataWrite);
            IStorageProvider? batchProvider = GetProvider(metadata);
            if (batchProvider != null)
            {
                foreach (AddVectorRequest vr in request.Vectors)
                {
                    if (vr.Name != null || vr.Labels != null || vr.Tags != null)
                    {
                        IHnswNode node = await batchProvider.GetNodeAsync(vr.GUID, cancellationToken).ConfigureAwait(false);
                        await node.SetMetadataAsync(vr.Name, vr.Labels, vr.Tags, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            scope.EndStage(null);
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
            using (ServerOperationScope scope = ServerOperationScope.Start(ServerTelemetryNames.OperationVectorRemove, indexName))
            {
                try
                {
                    scope.SetTag(ServerTelemetryNames.AttributeVectorId, vectorGuid.ToString());
                    return await RemoveVectorCoreAsync(indexName, vectorGuid, scope, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    scope.Fail(e);
                    throw;
                }
            }
        }

        private async Task<bool> RemoveVectorCoreAsync(string indexName, Guid vectorGuid, ServerOperationScope scope, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(indexName);

            cancellationToken.ThrowIfCancellationRequested();

            if (!_Indexes.TryGetValue(indexName, out IndexMetadata? metadata))
            {
                _Logging?.Warn(_Header + $"remove vector request for missing index: '{indexName}'");
                throw new InvalidOperationException($"Index '{indexName}' not found.");
            }

            scope.StorageType = metadata.StorageType;
            scope.BeginStage(ServerTelemetryNames.StageRemove);
            await metadata.Index.RemoveAsync(vectorGuid, cancellationToken).ConfigureAwait(false);
            metadata.VectorCount = Math.Max(0, metadata.VectorCount - 1);
            scope.EndStage(null);

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
        /// <exception cref="InvalidOperationException">Thrown when the index is not found.</exception>
        /// <exception cref="VectorDimensionMismatchException">Thrown when a vector length does not match the index dimension.</exception>
        public async Task<SearchResponse> SearchAsync(string indexName, SearchRequest request, CancellationToken cancellationToken = default)
        {
            using (ServerOperationScope scope = ServerOperationScope.Start(ServerTelemetryNames.OperationSearch, indexName))
            {
                try
                {
                    SearchResponse response = await SearchCoreAsync(indexName, request, scope, cancellationToken).ConfigureAwait(false);
                    scope.SetTag(ServerTelemetryNames.AttributeResultCount, response.Results.Count);
                    scope.SetTag(ServerTelemetryNames.AttributeFilteredCount, response.FilteredCount);
                    ServerTelemetry.RecordSearch(response.Results.Count, response.FilteredCount);
                    return response;
                }
                catch (Exception e)
                {
                    scope.Fail(e);
                    throw;
                }
            }
        }

        private async Task<SearchResponse> SearchCoreAsync(string indexName, SearchRequest request, ServerOperationScope scope, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(indexName);
            ArgumentNullException.ThrowIfNull(request);

            cancellationToken.ThrowIfCancellationRequested();

            if (!_Indexes.TryGetValue(indexName, out IndexMetadata? metadata))
            {
                throw new InvalidOperationException($"Index '{indexName}' not found.");
            }

            scope.StorageType = metadata.StorageType;

            if (request.Vector.Count != metadata.Dimension)
            {
                throw new VectorDimensionMismatchException($"Query vector dimension {request.Vector.Count} does not match index dimension {metadata.Dimension}.", request.Vector.Count, metadata.Dimension);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            scope.BeginStage(ServerTelemetryNames.StageTopK);
            IEnumerable<VectorResult> results = await metadata.Index.GetTopKAsync(request.Vector, request.K, request.Ef, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            List<VectorSearchResult> searchResults = new List<VectorSearchResult>();
            // Batch-fetch nodes to populate metadata alongside the vector results.
            scope.BeginStage(ServerTelemetryNames.StageMetadataFetch);
            IStorageProvider? searchProvider = GetProvider(metadata);
            List<Guid> resultIds = results.Select(r => r.GUID).ToList();
            Dictionary<Guid, IHnswNode> nodeMap = (searchProvider != null && resultIds.Count > 0)
                ? await searchProvider.GetNodesAsync(resultIds, cancellationToken).ConfigureAwait(false)
                : new Dictionary<Guid, IHnswNode>();

            scope.BeginStage(ServerTelemetryNames.StageFilter);
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
                    sr.Labels = n.Labels != null ? new List<string>(n.Labels) : null;
                    sr.Tags = n.Tags != null ? new Dictionary<string, object>(n.Tags) : null;
                }
                searchResults.Add(sr);
            }

            scope.EndStage(null);
            return new SearchResponse
            {
                Results = searchResults,
                SearchTimeMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
                FilteredCount = filteredOut
            };
        }

        /// <summary>
        /// Run the startup index-reload job: reload persisted SQLite index files, then PostgreSQL indexes.
        /// Emits a root span named job:index_reload with a child span per stage, a job counter and duration by
        /// outcome, per-stage durations and counters, per-index results, and the last-success timestamp gauge.
        /// Individual index failures are logged and counted but do not fail the job.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task ReloadPersistedIndexesAsync(CancellationToken cancellationToken = default)
        {
            long startTimestamp = HnswTelemetry.GetTimestamp();
            Activity? job = null;
            try
            {
                job = ServerTelemetry.ActivitySource.StartActivity(ServerTelemetryNames.ReloadJobSpanName, ActivityKind.Internal);
            }
            catch (Exception)
            {
                job = null;
            }

            Exception? failure = null;
            try
            {
                await ReloadSqlitePersistedIndexesAsync(cancellationToken).ConfigureAwait(false);
                await ReloadPostgresqlIndexesAsync(cancellationToken).ConfigureAwait(false);
                job?.SetTag(ServerTelemetryNames.AttributeResultCount, _Indexes.Count);
            }
            catch (Exception e)
            {
                failure = e;
                throw;
            }
            finally
            {
                ServerTelemetry.RecordReloadJob(HnswTelemetry.GetElapsedSeconds(startTimestamp), failure);
                HnswTelemetry.CompleteActivity(job, failure);
            }
        }

        /// <summary>
        /// Snapshot index and vector totals per storage type, for the inventory gauges.
        /// Thread safe; reads a point-in-time view of the loaded indexes.
        /// </summary>
        /// <returns>One entry per storage type in use. Never null.</returns>
        public List<StorageInventory> GetInventory()
        {
            Dictionary<string, StorageInventory> byType = new Dictionary<string, StorageInventory>(StringComparer.Ordinal);
            foreach (IndexMetadata metadata in _Indexes.Values)
            {
                string type = ServerTelemetry.NormalizeStorageType(metadata.StorageType);
                if (!byType.TryGetValue(type, out StorageInventory? inventory))
                {
                    inventory = new StorageInventory { StorageType = type };
                    byType[type] = inventory;
                }

                inventory.IndexCount++;
                inventory.VectorCount += Math.Max(0, metadata.VectorCount);
            }

            return byType.Values.ToList();
        }

        /// <summary>
        /// Dispose of the index manager.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (_Disposed) return;

            foreach (IndexMetadata metadata in _Indexes.Values)
            {
                await metadata.DisposeAsync().ConfigureAwait(false);
            }
            _Indexes.Clear();

            if (_PostgresqlDataSource != null)
            {
                await _PostgresqlDataSource.DisposeAsync().ConfigureAwait(false);
                _PostgresqlDataSource = null;
            }

            _Disposed = true;
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        private async Task<HnswIndex> CreateHnswIndexAsync(IndexMetadata metadata, CancellationToken cancellationToken = default, ServerOperationScope? scope = null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            HnswIndex index;

            if (string.Equals(metadata.StorageType, "RAM", StringComparison.OrdinalIgnoreCase))
            {
                RamStorageProvider provider = new RamStorageProvider();
                index = new HnswIndex(metadata.Dimension, provider);
                metadata.StorageObjects = new List<IAsyncDisposable> { provider };
            }
            else if (string.Equals(metadata.StorageType, "SQLite", StringComparison.OrdinalIgnoreCase))
            {
                string dbPath = Path.Combine(_SqliteDirectory, $"{metadata.Name}.db");
                SqliteStorageProvider provider = await SqliteStorageProvider.CreateAsync(
                    dbPath,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                index = new HnswIndex(metadata.Dimension, provider);
                metadata.StorageObjects = new List<IAsyncDisposable> { provider };

                // Persist server-level metadata into the index's SQLite file so the index
                // self-describes across restarts. The library uses hnsw_metadata as a
                // key/value table; the server writes its own namespaced keys alongside.
                scope?.BeginStage(ServerTelemetryNames.StageMetadataPersist);
                await WriteServerMetadataAsync(provider.Connection, metadata, cancellationToken).ConfigureAwait(false);
            }
            else if (string.Equals(metadata.StorageType, "PostgreSQL", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(metadata.StorageType, "Postgres", StringComparison.OrdinalIgnoreCase))
            {
                PostgresqlStorageProvider provider = await PostgresqlStorageProvider.CreateAsync(
                    GetPostgresqlDataSource(),
                    metadata.Name,
                    metadata.Dimension,
                    metadata.DistanceFunction,
                    metadata.M,
                    metadata.MaxM,
                    metadata.EfConstruction,
                    createIfNotExists: true,
                    cancellationToken).ConfigureAwait(false);
                metadata.GUID = provider.IndexId;
                metadata.StorageType = "PostgreSQL";
                index = new HnswIndex(metadata.Dimension, provider);
                metadata.StorageObjects = new List<IAsyncDisposable> { provider };
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

        private static void ConfigureIndex(HnswIndex index, IndexMetadata metadata)
        {
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
        }

        private const string _MetaKeyGuid = "server.guid";
        private const string _MetaKeyDimension = "server.dimension";
        private const string _MetaKeyStorageType = "server.storage_type";
        private const string _MetaKeyDistanceFn = "server.distance_function";
        private const string _MetaKeyM = "server.m";
        private const string _MetaKeyMaxM = "server.max_m";
        private const string _MetaKeyEfC = "server.ef_construction";
        private const string _MetaKeyCreatedUtc = "server.created_utc";

        private static async Task WriteServerMetadataAsync(
            Microsoft.Data.Sqlite.SqliteConnection conn,
            IndexMetadata m,
            CancellationToken cancellationToken)
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
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task<Dictionary<string, string>?> ReadServerMetadataAsync(
            Microsoft.Data.Sqlite.SqliteConnection conn,
            CancellationToken cancellationToken)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            using Microsoft.Data.Sqlite.SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT key, value FROM hnsw_metadata WHERE key LIKE 'server.%'";
            using Microsoft.Data.Sqlite.SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                result[reader.GetString(0)] = reader.GetString(1);
            }
            return result.Count == 0 ? null : result;
        }

        /// <summary>
        /// Reloads persisted PostgreSQL indexes.
        /// </summary>
        public async Task ReloadPostgresqlIndexesAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_StorageSettings.PostgresqlConnectionString))
            {
                return;
            }

            long startTimestamp = HnswTelemetry.GetTimestamp();
            Activity? stage = StartReloadStage(ServerTelemetryNames.StageReloadPostgresql);
            Exception? failure = null;
            int loaded = 0;
            try
            {
                List<PostgresqlIndexMetadata> persisted = await PostgresqlStorageProvider.ListIndexesAsync(
                    GetPostgresqlDataSource(),
                    cancellationToken).ConfigureAwait(false);

                foreach (PostgresqlIndexMetadata pg in persisted)
                {
                    if (_Indexes.ContainsKey(pg.Name)) continue;
                    if (pg.Dimension < 1)
                    {
                        _Logging?.Warn(_Header + $"skipping PostgreSQL index '{pg.Name}': invalid persisted dimension");
                        ServerTelemetry.RecordReloadIndex(ServerTelemetryNames.StoragePostgresql, ServerTelemetryNames.ReloadSkipped);
                        continue;
                    }

                    PostgresqlStorageProvider provider = await PostgresqlStorageProvider.CreateAsync(
                        GetPostgresqlDataSource(),
                        pg.Name,
                        pg.Dimension,
                        pg.DistanceFunction,
                        pg.M,
                        pg.MaxM,
                        pg.EfConstruction,
                        createIfNotExists: false,
                        cancellationToken).ConfigureAwait(false);

                    IndexMetadata im = new IndexMetadata
                    {
                        Name = pg.Name,
                        GUID = pg.Id,
                        Dimension = pg.Dimension,
                        StorageType = "PostgreSQL",
                        DistanceFunction = pg.DistanceFunction,
                        M = pg.M,
                        MaxM = pg.MaxM,
                        EfConstruction = pg.EfConstruction,
                        CreatedUtc = pg.CreatedUtc,
                        VectorCount = pg.VectorCount,
                    };

                    HnswIndex index = new HnswIndex(im.Dimension, provider);
                    ConfigureIndex(index, im);

                    im.Index = index;
                    im.StorageObjects = new List<IAsyncDisposable> { provider };

                    _Indexes.TryAdd(im.Name, im);
                    loaded++;
                    ServerTelemetry.RecordReloadIndex(ServerTelemetryNames.StoragePostgresql, ServerTelemetryNames.ReloadLoaded);
                    _Logging?.Info(_Header + $"reloaded PostgreSQL index '{im.Name}' ({im.Dimension}-d, {im.VectorCount} vectors)");
                }

                if (loaded > 0) _Logging?.Info(_Header + $"reloaded {loaded} PostgreSQL persisted index(es)");
            }
            catch (Exception ex)
            {
                failure = ex;
                ServerTelemetry.RecordReloadIndex(ServerTelemetryNames.StoragePostgresql, ServerTelemetryNames.ReloadFailed);
                _Logging?.Warn(_Header + $"failed to reload PostgreSQL indexes: {ex.Message}");
            }
            finally
            {
                stage?.SetTag(ServerTelemetryNames.AttributeLoadedCount, loaded);
                ServerTelemetry.RecordReloadStage(ServerTelemetryNames.StageReloadPostgresql, HnswTelemetry.GetElapsedSeconds(startTimestamp), failure);
                HnswTelemetry.CompleteActivity(stage, failure);
            }
        }

        /// <summary>
        /// Reloads persisted SQLite index files from the configured SQLite directory.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task ReloadSqlitePersistedIndexesAsync(CancellationToken cancellationToken = default)
        {
            long startTimestamp = HnswTelemetry.GetTimestamp();
            Activity? stage = StartReloadStage(ServerTelemetryNames.StageReloadSqlite);
            Exception? failure = null;
            int loaded = 0;
            try
            {
                loaded = await ReloadSqlitePersistedIndexesCoreAsync(cancellationToken).ConfigureAwait(false);
                if (loaded < 0)
                {
                    // Directory enumeration failed: startup continues (as before), but the stage is reported failed.
                    failure = new IOException($"Cannot enumerate SQLite directory '{_SqliteDirectory}'.");
                    loaded = 0;
                }
            }
            catch (Exception e)
            {
                failure = e;
                throw;
            }
            finally
            {
                stage?.SetTag(ServerTelemetryNames.AttributeLoadedCount, loaded);
                ServerTelemetry.RecordReloadStage(ServerTelemetryNames.StageReloadSqlite, HnswTelemetry.GetElapsedSeconds(startTimestamp), failure);
                HnswTelemetry.CompleteActivity(stage, failure);
            }
        }

        private async Task<int> ReloadSqlitePersistedIndexesCoreAsync(CancellationToken cancellationToken)
        {
            // Returns the number of indexes loaded, or -1 when the directory cannot be enumerated.
            cancellationToken.ThrowIfCancellationRequested();

            string[] dbFiles;
            try
            {
                dbFiles = Directory.GetFiles(_SqliteDirectory, "*.db");
            }
            catch (Exception ex)
            {
                _Logging?.Warn(_Header + $"cannot enumerate SQLite directory: {ex.Message}");
                return -1;
            }

            int loaded = 0;
            foreach (string dbPath in dbFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string name = Path.GetFileNameWithoutExtension(dbPath);
                if (string.IsNullOrWhiteSpace(name)) continue;

                try
                {
                    SqliteStorageProvider provider = await SqliteStorageProvider.CreateAsync(
                        dbPath,
                        createIfNotExists: false,
                        cancellationToken: cancellationToken).ConfigureAwait(false);

                    Dictionary<string, string>? meta = await ReadServerMetadataAsync(
                        provider.Connection,
                        cancellationToken).ConfigureAwait(false);
                    if (meta == null)
                    {
                        // No server-written metadata (e.g., a db created before this fix). Skip
                        // rather than guess; the index can be rebuilt explicitly by the user.
                        _Logging?.Warn(_Header + $"skipping '{name}': no server metadata in {Path.GetFileName(dbPath)}");
                        ServerTelemetry.RecordReloadIndex(ServerTelemetryNames.StorageSqlite, ServerTelemetryNames.ReloadSkipped);
                        await provider.DisposeAsync().ConfigureAwait(false);
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
                        ServerTelemetry.RecordReloadIndex(ServerTelemetryNames.StorageSqlite, ServerTelemetryNames.ReloadSkipped);
                        await provider.DisposeAsync().ConfigureAwait(false);
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
                    im.StorageObjects = new List<IAsyncDisposable> { provider };
                    im.VectorCount = await CountVectorsBestEffortAsync(provider, cancellationToken).ConfigureAwait(false);

                    _Indexes.TryAdd(name, im);
                    loaded++;
                    ServerTelemetry.RecordReloadIndex(ServerTelemetryNames.StorageSqlite, ServerTelemetryNames.ReloadLoaded);
                    _Logging?.Info(_Header + $"reloaded index '{name}' ({im.Dimension}-d, {im.VectorCount} vectors)");
                }
                catch (Exception ex)
                {
                    ServerTelemetry.RecordReloadIndex(ServerTelemetryNames.StorageSqlite, ServerTelemetryNames.ReloadFailed);
                    _Logging?.Warn(_Header + $"failed to reload index from {Path.GetFileName(dbPath)}: {ex.Message}");
                }
            }

            if (loaded > 0) _Logging?.Info(_Header + $"reloaded {loaded} persisted index(es) from disk");
            return loaded;
        }

        private static Activity? StartReloadStage(string stage)
        {
            try
            {
                Activity? activity = ServerTelemetry.ActivitySource.StartActivity("stage:" + stage, ActivityKind.Internal);
                activity?.SetTag(ServerTelemetryNames.LabelStage, stage);
                return activity;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static IStorageProvider? GetProvider(IndexMetadata metadata)
        {
            if (metadata.StorageObjects == null) return null;
            foreach (IAsyncDisposable obj in metadata.StorageObjects)
            {
                if (obj is IStorageProvider sp) return sp;
            }
            return null;
        }

        private NpgsqlDataSource GetPostgresqlDataSource()
        {
            if (_PostgresqlDataSource != null)
            {
                return _PostgresqlDataSource;
            }

            if (string.IsNullOrWhiteSpace(_StorageSettings.PostgresqlConnectionString))
            {
                throw new InvalidOperationException("PostgreSQL connection string is not configured.");
            }

            _PostgresqlDataSource = NpgsqlDataSource.Create(_StorageSettings.PostgresqlConnectionString);
            return _PostgresqlDataSource;
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

        private static async Task<int> CountVectorsBestEffortAsync(SqliteStorageProvider provider, CancellationToken cancellationToken = default)
        {
            try
            {
                Dictionary<Guid, int> layers = await provider.GetAllNodeLayersAsync(cancellationToken).ConfigureAwait(false);
                return layers.Count;
            }
            catch
            {
                return 0;
            }
        }

        #endregion

        #region Private-Classes

        /// <summary>
        /// Metadata for an HNSW index.
        /// </summary>
        private class IndexMetadata : IAsyncDisposable
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
            public List<IAsyncDisposable>? StorageObjects { get; set; } = null;

            public async ValueTask DisposeAsync()
            {
                if (StorageObjects != null)
                {
                    foreach (IAsyncDisposable obj in StorageObjects)
                    {
                        await obj.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
        }

        #endregion
    }
}
