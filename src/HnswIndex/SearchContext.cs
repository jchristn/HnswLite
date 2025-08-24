namespace Hnsw
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Search context that provides caching of nodes during search operations.
    /// This dramatically reduces the number of storage reads during search traversal.
    /// </summary>
    public class SearchContext
    {
        #region Public-Members

        /// <summary>
        /// Gets the number of nodes currently cached.
        /// </summary>
        public int CachedNodeCount => _NodeCache.Count;

        #endregion

        #region Private-Members

        private readonly Dictionary<Guid, IHnswNode> _NodeCache = new Dictionary<Guid, IHnswNode>();
        private readonly IHnswStorage _Storage;
        private readonly CancellationToken _CancellationToken;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the SearchContext class.
        /// </summary>
        /// <param name="storage">The storage backend to read nodes from.</param>
        /// <param name="cancellationToken">Cancellation token for the search operation.</param>
        /// <exception cref="ArgumentNullException">Thrown when storage is null.</exception>
        public SearchContext(IHnswStorage storage, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(storage, nameof(storage));
            _Storage = storage;
            _CancellationToken = cancellationToken;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Gets a node by ID, using the cache if available.
        /// </summary>
        /// <param name="id">The node ID to retrieve.</param>
        /// <returns>The requested node.</returns>
        public async Task<IHnswNode> GetNodeAsync(Guid id)
        {
            _CancellationToken.ThrowIfCancellationRequested();
            
            if (_NodeCache.TryGetValue(id, out IHnswNode? node))
                return node;

            node = await _Storage.GetNodeAsync(id, _CancellationToken).ConfigureAwait(false);
            _NodeCache[id] = node;
            return node;
        }

        /// <summary>
        /// Gets multiple nodes by their IDs, using cache where possible and batch-loading the rest.
        /// </summary>
        /// <param name="ids">Collection of node IDs to retrieve.</param>
        /// <returns>Dictionary mapping node IDs to nodes.</returns>
        public async Task<Dictionary<Guid, IHnswNode>> GetNodesAsync(IEnumerable<Guid> ids)
        {
            _CancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(ids, nameof(ids));
            
            Dictionary<Guid, IHnswNode> result = new Dictionary<Guid, IHnswNode>();
            List<Guid> missingIds = new List<Guid>();

            // First check cache
            foreach (Guid id in ids)
            {
                if (_NodeCache.TryGetValue(id, out IHnswNode? node))
                    result[id] = node;
                else
                    missingIds.Add(id);
            }

            // Batch-load missing nodes
            if (missingIds.Count > 0)
            {
                Dictionary<Guid, IHnswNode> loadedNodes = await _Storage.GetNodesAsync(missingIds, _CancellationToken).ConfigureAwait(false);
                foreach (KeyValuePair<Guid, IHnswNode> kvp in loadedNodes)
                {
                    _NodeCache[kvp.Key] = kvp.Value;
                    result[kvp.Key] = kvp.Value;
                }
            }

            return result;
        }

        /// <summary>
        /// Pre-fetches nodes into the cache to avoid individual loads later.
        /// </summary>
        /// <param name="ids">Collection of node IDs to pre-fetch.</param>
        public async Task PrefetchNodesAsync(IEnumerable<Guid> ids)
        {
            _CancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(ids, nameof(ids));
            
            List<Guid> missingIds = ids.Where(id => !_NodeCache.ContainsKey(id)).ToList();
            
            if (missingIds.Count > 0)
            {
                Dictionary<Guid, IHnswNode> nodes = await _Storage.GetNodesAsync(missingIds, _CancellationToken).ConfigureAwait(false);
                foreach (KeyValuePair<Guid, IHnswNode> kvp in nodes)
                {
                    _NodeCache[kvp.Key] = kvp.Value;
                }
            }
        }

        /// <summary>
        /// Tries to get a node, returning success status.
        /// </summary>
        /// <param name="id">The node ID to retrieve.</param>
        /// <returns>Result indicating success and the node if found.</returns>
        public async Task<TryGetNodeResult> TryGetNodeAsync(Guid id)
        {
            _CancellationToken.ThrowIfCancellationRequested();
            
            if (_NodeCache.TryGetValue(id, out IHnswNode? node))
                return TryGetNodeResult.Found(node);

            TryGetNodeResult result = await _Storage.TryGetNodeAsync(id, _CancellationToken).ConfigureAwait(false);
            if (result.Success && result.Node != null)
                _NodeCache[id] = result.Node;
            
            return result;
        }

        /// <summary>
        /// Clears the cache. Useful for long-running operations to control memory usage.
        /// </summary>
        public void ClearCache()
        {
            _NodeCache.Clear();
        }

        #endregion
        
        #region Private-Methods
        
        #endregion
    }
}