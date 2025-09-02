namespace Hnsw
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hsnw;

    /// <summary>
    /// Main HNSW (Hierarchical Navigable Small World) index implementation.
    /// </summary>
    public class HnswIndex
    {
        // Private members
        private readonly IHnswStorage _Storage;
        private readonly IHnswLayerStorage _LayerStorage;
        private readonly SemaphoreSlim _IndexLock = new SemaphoreSlim(1, 1);
        private readonly int _VectorDimension;

        private Random _Random;
        private IDistanceFunction _DistanceFunction = new EuclideanDistance();
        private int _M = 16;
        private int _MaxM = 32;
        private int _EfConstruction = 200;
        private int _MaxLayers = 16;
        private double _LevelMultiplier = 1.0 / Math.Log(2.0);
        private bool _ExtendCandidates = false;
        private bool _KeepPrunedConnections = false;
        private int? _Seed = null;

        // Public members
        /// <summary>
        /// Gets or sets the distance function used for vector comparisons.
        /// Cannot be null. Setting to null creates a new EuclideanDistance instance.
        /// Default: EuclideanDistance.
        /// </summary>
        public IDistanceFunction DistanceFunction
        {
            get => _DistanceFunction;
            set => _DistanceFunction = value ?? new EuclideanDistance();
        }

        /// <summary>
        /// Gets or sets the maximum number of connections per layer (except layer 0).
        /// Minimum: 2, Maximum: 100, Default: 16.
        /// Typical values range from 8 to 48.
        /// </summary>
        public int M
        {
            get => _M;
            set
            {
                if (value < 2)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "M must be at least 2 for meaningful connectivity.");
                if (value > 100)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "M values greater than 100 are not recommended due to performance implications.");
                _M = value;
            }
        }

        /// <summary>
        /// Gets or sets the maximum number of connections for layer 0.
        /// Minimum: 1, Maximum: 200, Default: 32.
        /// Should be greater than or equal to M, typically 2*M.
        /// </summary>
        public int MaxM
        {
            get => _MaxM;
            set
            {
                if (value < 1)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "MaxM must be at least 1.");
                if (value > 200)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "MaxM values greater than 200 are not recommended due to performance implications.");
                _MaxM = value;
            }
        }

        /// <summary>
        /// Gets or sets the size of the dynamic candidate list.
        /// Minimum: 1, Maximum: 2000, Default: 200.
        /// Higher values improve recall but decrease construction speed.
        /// </summary>
        public int EfConstruction
        {
            get => _EfConstruction;
            set
            {
                if (value < 1)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "EfConstruction must be at least 1.");
                if (value > 2000)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "EfConstruction values greater than 2000 provide diminishing returns.");
                _EfConstruction = value;
            }
        }

        /// <summary>
        /// Gets or sets the maximum number of layers in the graph.
        /// Minimum: 1, Maximum: 64, Default: 16.
        /// Typically between 10 and 30.
        /// </summary>
        public int MaxLayers
        {
            get => _MaxLayers;
            set
            {
                if (value < 1)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "MaxLayers must be at least 1.");
                if (value > 64)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "MaxLayers greater than 64 is not recommended.");
                _MaxLayers = value;
            }
        }

        /// <summary>
        /// Gets or sets the level assignment multiplier.
        /// Minimum: Greater than 0, Maximum: 2.0, Default: 1/ln(2) ≈ 1.44.
        /// Controls the layer assignment probability distribution.
        /// Must be a finite number (not NaN or Infinity).
        /// </summary>
        public double LevelMultiplier
        {
            get => _LevelMultiplier;
            set
            {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "LevelMultiplier must be greater than 0.");
                if (value > 2.0)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "LevelMultiplier greater than 2.0 is not typical.");
                if (double.IsNaN(value) || double.IsInfinity(value))
                    throw new ArgumentException("LevelMultiplier must be a valid finite number.",
                        nameof(value));
                _LevelMultiplier = value;
            }
        }

        /// <summary>
        /// Gets or sets whether to extend candidates with neighbors' neighbors.
        /// Default: false.
        /// Can improve recall at the cost of construction time.
        /// </summary>
        public bool ExtendCandidates
        {
            get => _ExtendCandidates;
            set => _ExtendCandidates = value;
        }

        /// <summary>
        /// Gets or sets whether to add pruned connections to lower levels.
        /// Default: false.
        /// Can improve connectivity at the cost of more memory usage.
        /// </summary>
        public bool KeepPrunedConnections
        {
            get => _KeepPrunedConnections;
            set => _KeepPrunedConnections = value;
        }

        /// <summary>
        /// Gets or sets the _Random seed for reproducible results.
        /// Minimum: -1 (_Random seed), Maximum: int.MaxValue, Default: -1.
        /// Use -1 for _Random seed, or any non-negative value for deterministic behavior.
        /// </summary>
        public int Seed
        {
            get => _Seed ?? -1;
            set
            {
                if (value < -1)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "Seed must be -1 (for _Random) or a non-negative value.");

                _Seed = value == -1 ? null : value;
                _Random = _Seed.HasValue ? new Random(_Seed.Value) : new Random();
            }
        }

        /// <summary>
        /// Gets the vector dimension for this index.
        /// </summary>
        public int VectorDimension => _VectorDimension;

        // Constructors
        /// <summary>
        /// Initializes a new instance of the HNSWIndex class with custom _Storage.
        /// </summary>
        /// <param name="dimension">The dimensionality of vectors to be indexed. Minimum: 1, Maximum: 4096.</param>
        /// <param name="_Storage">Storage backend implementation. Cannot be null.</param>
        /// <param name="_LayerStorage">Layer _Storage backend implementation.  Cannot be null.</param>
        public HnswIndex(int dimension, IHnswStorage _Storage, IHnswLayerStorage _LayerStorage)
        {
            if (dimension < 1)
                throw new ArgumentOutOfRangeException(nameof(dimension),
                    "Dimension must be at least 1.");
            if (dimension > 4096)
                throw new ArgumentOutOfRangeException(nameof(dimension),
                    "Dimension greater than 4096 is not recommended due to memory and performance constraints.");
            if (_Storage == null)
                throw new ArgumentNullException(nameof(_Storage));
            if (_LayerStorage == null)
                throw new ArgumentNullException(nameof(_LayerStorage));

            this._VectorDimension = dimension;
            this._Storage = _Storage;
            this._LayerStorage = _LayerStorage;
            this._Random = new Random();
        }

        /// <summary>
        /// Initializes a new instance of the HNSWIndex class with custom _Storage and seed.
        /// </summary>
        /// <param name="dimension">The dimensionality of vectors to be indexed. Minimum: 1, Maximum: 4096.</param>
        /// <param name="_Storage">Storage backend implementation. Cannot be null.</param>
        /// <param name="_LayerStorage">Layer _Storage backend implementation.  Cannot be null.</param>
        /// <param name="seed">Random seed for reproducible results. Use null for _Random seed.</param>
        public HnswIndex(int dimension, IHnswStorage _Storage, IHnswLayerStorage _LayerStorage, int? seed) : this(dimension, _Storage, _LayerStorage)
        {
            if (seed.HasValue)
            {
                Seed = seed.Value;
            }
        }

        // Public methods
        /// <summary>
        /// Adds a vector to the index.
        /// </summary>
        /// <param name="guid">Unique identifier for the vector.</param>
        /// <param name="vector">Vector data. Cannot be null. Must match index dimension.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task AddAsync(Guid guid, List<float> vector, CancellationToken cancellationToken = default)
        {
            if (vector == null) throw new ArgumentNullException(nameof(vector));
            if (vector.Count != _VectorDimension)
                throw new ArgumentException($"Vector dimension {vector.Count} does not match index dimension {_VectorDimension}");

            await _IndexLock.WaitAsync(cancellationToken);
            try
            {
                await _Storage.AddNodeAsync(guid, vector, cancellationToken);

                int count = await _Storage.GetCountAsync(cancellationToken);
                if (count == 1)
                {
                    SetNodeLayer(guid, 0);
                    return;
                }

                // Assign layer using standard HNSW approach
                int nodeLevel = AssignLevel();
                SetNodeLayer(guid, nodeLevel);

                // Get entry point
                Guid? entryPoint = _Storage.EntryPoint;
                if (!entryPoint.HasValue)
                    throw new InvalidOperationException("Entry point should exist when there are multiple nodes in the index.");

                Guid entryPointId = entryPoint.Value;
                Guid currentNearest = entryPointId;

                // Search for nearest neighbor from top to target layer
                int entryPointLayer = GetNodeLayer(entryPointId);
                for (int layer = entryPointLayer; layer > nodeLevel; layer--)
                {
                    currentNearest = await GreedySearchLayerAsync(vector, currentNearest, layer, cancellationToken);
                }

                // Insert at all layers from nodeLevel to 0
                for (int layer = nodeLevel; layer >= 0; layer--)
                {
                    List<SearchCandidate> candidates = await SearchLayerAsync(vector, currentNearest, EfConstruction, layer, cancellationToken);

                    int mValue = layer == 0 ? MaxM : M;

                    // Select M neighbors using a heuristic
                    List<Guid> neighbors = await SelectNeighborsHeuristicAsync(vector, candidates, mValue, layer, ExtendCandidates, KeepPrunedConnections, cancellationToken);

                    IHnswNode newNode = await _Storage.GetNodeAsync(guid, cancellationToken);
                    foreach (Guid neighborId in neighbors)
                    {
                        if (neighborId == guid)
                            continue; // Skip self-connections

                        // Add bidirectional connections
                        newNode.AddNeighbor(layer, neighborId);
                        IHnswNode neighbor = await _Storage.GetNodeAsync(neighborId, cancellationToken);
                        neighbor.AddNeighbor(layer, guid);

                        // Prune neighbor's connections if needed
                        Dictionary<int, HashSet<Guid>> neighborConnections = neighbor.GetNeighbors();
                        if (neighborConnections.ContainsKey(layer))
                        {
                            HashSet<Guid> currentConnections = neighborConnections[layer];
                            if (currentConnections.Count > mValue)
                            {
                                // Prune to M connections using heuristic
                                List<SearchCandidate> pruneCandidates = new List<SearchCandidate>();
                                foreach (Guid connId in currentConnections)
                                {
                                    IHnswNode conn = await _Storage.GetNodeAsync(connId, cancellationToken);
                                    pruneCandidates.Add(new SearchCandidate(DistanceFunction.Distance(neighbor.Vector, conn.Vector), connId));
                                }

                                List<Guid> newConnections = await SelectNeighborsHeuristicAsync(neighbor.Vector, pruneCandidates, mValue, layer, ExtendCandidates, KeepPrunedConnections, cancellationToken);

                                // Remove connections not in newConnections
                                foreach (Guid connId in currentConnections.ToList())
                                {
                                    if (!newConnections.Contains(connId))
                                    {
                                        neighbor.RemoveNeighbor(layer, connId);
                                        IHnswNode connNode = await _Storage.GetNodeAsync(connId, cancellationToken);
                                        connNode.RemoveNeighbor(layer, neighborId);
                                    }
                                }
                            }
                        }
                    }
                }

                // Update entry point if necessary
                if (nodeLevel > entryPointLayer)
                {
                    _Storage.EntryPoint = guid;
                }
            }
            finally
            {
                _IndexLock.Release();
            }
        }

        /// <summary>
        /// Adds multiple vectors to the index in a single atomic operation.
        /// Acquires write lock once, updates _Storage and graph structure, then releases lock.
        /// More efficient than calling AddAsync multiple times.
        /// </summary>
        /// <param name="nodes">Dictionary mapping node IDs to their vector data. Cannot be null or empty.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when nodes is null.</exception>
        /// <exception cref="ArgumentException">Thrown when nodes is empty, any ID is Guid.Empty, or vector dimensions don't match.</exception>
        public async Task AddNodesAsync(Dictionary<Guid, List<float>> nodes, CancellationToken cancellationToken = default)
        {
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            if (nodes.Count == 0) throw new ArgumentException("Nodes collection cannot be empty.", nameof(nodes));

            // Validate all nodes before acquiring lock
            foreach (KeyValuePair<Guid, List<float>> kvp in nodes)
            {
                if (kvp.Key == Guid.Empty)
                    throw new ArgumentException($"Node ID cannot be Guid.Empty.", nameof(nodes));
                if (kvp.Value == null)
                    throw new ArgumentNullException(nameof(nodes), $"Vector for node {kvp.Key} is null");
                if (kvp.Value.Count != _VectorDimension)
                    throw new ArgumentException($"Vector dimension {kvp.Value.Count} for node {kvp.Key} does not match index dimension {_VectorDimension}");
            }

            // Acquire write lock for entire operation
            await _IndexLock.WaitAsync(cancellationToken);
            try
            {
                // Step 1: Add all nodes to _Storage in batch
                await _Storage.AddNodesAsync(nodes, cancellationToken);

                // Step 2: Build graph structure for each node while holding lock
                // Use SearchContext to cache nodes during graph construction for better performance
                SearchContext context = new SearchContext(_Storage, cancellationToken);
                bool isFirstNode = await _Storage.GetCountAsync(cancellationToken) == nodes.Count;

                int processedCount = 0;
                int totalCount = nodes.Count;
                foreach (KeyValuePair<Guid, List<float>> kvp in nodes)
                {
                    Guid nodeId = kvp.Key;
                    List<float> vector = kvp.Value;

                    if (isFirstNode)
                    {
                        // First node in empty index
                        SetNodeLayer(nodeId, 0);
                        _Storage.EntryPoint = nodeId;
                        isFirstNode = false;
                        continue;
                    }

                    // Assign layer using standard HNSW approach
                    int nodeLevel = AssignLevel();
                    SetNodeLayer(nodeId, nodeLevel);

                    // Get entry point
                    Guid? entryPoint = _Storage.EntryPoint;
                    if (!entryPoint.HasValue)
                        throw new InvalidOperationException("Entry point should exist when there are multiple nodes in the index.");

                    Guid entryPointId = entryPoint.Value;
                    Guid currentNearest = entryPointId;

                    // Search for nearest neighbor from top to target layer
                    int entryPointLayer = GetNodeLayer(entryPointId);
                    for (int layer = entryPointLayer; layer > nodeLevel; layer--)
                    {
                        currentNearest = await GreedySearchLayerWithContextAsync(vector, currentNearest, layer, context, cancellationToken);
                    }

                    // Insert at all layers from nodeLevel to 0
                    for (int layer = nodeLevel; layer >= 0; layer--)
                    {
                        List<SearchCandidate> candidates = await SearchLayerWithContextAsync(vector, currentNearest, EfConstruction, layer, context, cancellationToken);
                        int mValue = layer == 0 ? MaxM : M;

                        // Select M neighbors using a heuristic
                        List<Guid> neighbors = await SelectNeighborsHeuristicAsync(vector, candidates, mValue, layer, ExtendCandidates, KeepPrunedConnections, cancellationToken);

                        IHnswNode newNode = await context.GetNodeAsync(nodeId);
                        foreach (Guid neighborId in neighbors)
                        {
                            if (neighborId == nodeId)
                                continue; // Skip self-connections

                            // Add bidirectional connections
                            newNode.AddNeighbor(layer, neighborId);
                            IHnswNode neighbor = await context.GetNodeAsync(neighborId);
                            neighbor.AddNeighbor(layer, nodeId);

                            // Prune neighbor's connections if needed
                            Dictionary<int, HashSet<Guid>> neighborConnections = neighbor.GetNeighbors();
                            if (neighborConnections.ContainsKey(layer))
                            {
                                HashSet<Guid> currentConnections = neighborConnections[layer];
                                if (currentConnections.Count > mValue)
                                {
                                    // Prune to M connections using heuristic
                                    List<SearchCandidate> pruneCandidates = new List<SearchCandidate>();
                                    foreach (Guid connId in currentConnections)
                                    {
                                        IHnswNode conn = await context.GetNodeAsync(connId);
                                        pruneCandidates.Add(new SearchCandidate(DistanceFunction.Distance(neighbor.Vector, conn.Vector), connId));
                                    }

                                    List<Guid> newConnections = await SelectNeighborsHeuristicAsync(neighbor.Vector, pruneCandidates, mValue, layer, ExtendCandidates, KeepPrunedConnections, cancellationToken);

                                    // Remove connections not in newConnections
                                    foreach (Guid connId in currentConnections.ToList())
                                    {
                                        if (!newConnections.Contains(connId))
                                        {
                                            neighbor.RemoveNeighbor(layer, connId);
                                            IHnswNode connNode = await context.GetNodeAsync(connId);
                                            connNode.RemoveNeighbor(layer, neighborId);
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // Update entry point if necessary
                    if (nodeLevel > entryPointLayer)
                    {
                        _Storage.EntryPoint = nodeId;
                    }

                    processedCount++;

                    // TODO: Add Flush() to IHnswStorage interface for better batch performance
                }

                // TODO: Call Flush() here when added to interface
            }
            finally
            {
                _IndexLock.Release();
            }
        }

        /// <summary>
        /// Removes a vector from the index.
        /// </summary>
        /// <param name="guid">Identifier of the vector to remove.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task RemoveAsync(Guid guid, CancellationToken cancellationToken = default)
        {
            await _IndexLock.WaitAsync(cancellationToken);
            try
            {
                TryGetNodeResult tryGetResult = await _Storage.TryGetNodeAsync(guid, cancellationToken);
                if (!tryGetResult.Success || tryGetResult.Node == null)
                    return;
                IHnswNode nodeToRemove = tryGetResult.Node;

                // Get all neighbors before removing the node
                Dictionary<int, HashSet<Guid>> neighbors = nodeToRemove.GetNeighbors();

                // Remove the node from _Storage
                await _Storage.RemoveNodeAsync(guid, cancellationToken);

                // Remove from layer _Storage
                _LayerStorage.RemoveNodeLayer(guid);

                // Remove all connections to this node from other nodes
                IEnumerable<Guid> allNodeIds = await _Storage.GetAllNodeIdsAsync(cancellationToken);
                foreach (Guid nodeId in allNodeIds)
                {
                    IHnswNode node = await _Storage.GetNodeAsync(nodeId, cancellationToken);
                    foreach (int layer in neighbors.Keys)
                    {
                        node.RemoveNeighbor(layer, guid);
                    }
                }

                // Update entry point if the removed node was the entry point
                if (_Storage.EntryPoint == guid)
                {
                    // Find a new entry point - pick the node with the highest layer
                    IEnumerable<Guid> remainingNodeIds = await _Storage.GetAllNodeIdsAsync(cancellationToken);
                    if (remainingNodeIds.Any())
                    {
                        Guid? newEntryPoint = null;
                        int maxLayer = -1;

                        foreach (Guid nodeId in remainingNodeIds)
                        {
                            int nodeLayer = _LayerStorage.GetNodeLayer(nodeId);
                            if (nodeLayer > maxLayer)
                            {
                                maxLayer = nodeLayer;
                                newEntryPoint = nodeId;
                            }
                        }

                        _Storage.EntryPoint = newEntryPoint;
                    }
                    else
                    {
                        _Storage.EntryPoint = null;
                    }
                }
            }
            finally
            {
                _IndexLock.Release();
            }
        }

        /// <summary>
        /// Removes multiple vectors from the index in a single atomic operation.
        /// Acquires write lock once, updates _Storage and graph structure, then releases lock.
        /// More efficient than calling RemoveAsync multiple times.
        /// </summary>
        /// <param name="nodeIds">List of node IDs to remove. Cannot be null or empty.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when nodeIds is null.</exception>
        /// <exception cref="ArgumentException">Thrown when nodeIds is empty.</exception>
        public async Task RemoveNodesAsync(List<Guid> nodeIds, CancellationToken cancellationToken = default)
        {
            if (nodeIds == null) throw new ArgumentNullException(nameof(nodeIds));
            if (nodeIds.Count == 0) throw new ArgumentException("Node IDs collection cannot be empty.", nameof(nodeIds));

            // Remove duplicates
            HashSet<Guid> uniqueNodeIds = new HashSet<Guid>(nodeIds);

            // Acquire write lock for entire operation
            await _IndexLock.WaitAsync(cancellationToken);
            try
            {
                // Collect all nodes that need to be processed before removal
                Dictionary<Guid, IHnswNode> nodesToRemove = new Dictionary<Guid, IHnswNode>();
                HashSet<Guid> nodesToUpdate = new HashSet<Guid>();

                // First pass: Identify nodes to remove and their neighbors
                foreach (Guid nodeId in uniqueNodeIds)
                {
                    TryGetNodeResult tryGetResult = await _Storage.TryGetNodeAsync(nodeId, cancellationToken);
                    if (tryGetResult.Success && tryGetResult.Node != null)
                    {
                        nodesToRemove[nodeId] = tryGetResult.Node;

                        // Get all neighbors that will need updating
                        Dictionary<int, HashSet<Guid>> neighbors = tryGetResult.Node.GetNeighbors();
                        foreach (HashSet<Guid> layerNeighbors in neighbors.Values)
                        {
                            foreach (Guid neighborId in layerNeighbors)
                            {
                                if (!uniqueNodeIds.Contains(neighborId))
                                {
                                    nodesToUpdate.Add(neighborId);
                                }
                            }
                        }
                    }
                }

                // Second pass: Remove all connections to nodes being deleted
                foreach (Guid neighborId in nodesToUpdate)
                {
                    IHnswNode neighbor = await _Storage.GetNodeAsync(neighborId, cancellationToken);
                    Dictionary<int, HashSet<Guid>> neighborConnections = neighbor.GetNeighbors();

                    foreach (int layer in neighborConnections.Keys.ToList())
                    {
                        foreach (Guid nodeIdToRemove in uniqueNodeIds)
                        {
                            neighbor.RemoveNeighbor(layer, nodeIdToRemove);
                        }
                    }
                }

                // Third pass: Remove nodes from _Storage and layer _Storage
                await _Storage.RemoveNodesAsync(uniqueNodeIds, cancellationToken);

                foreach (Guid nodeId in uniqueNodeIds)
                {
                    _LayerStorage.RemoveNodeLayer(nodeId);
                }

                // Fourth pass: Update entry point if necessary
                if (_Storage.EntryPoint.HasValue && uniqueNodeIds.Contains(_Storage.EntryPoint.Value))
                {
                    // Find a new entry point - pick the node with the highest layer
                    IEnumerable<Guid> remainingNodeIds = await _Storage.GetAllNodeIdsAsync(cancellationToken);
                    if (remainingNodeIds.Any())
                    {
                        Guid? newEntryPoint = null;
                        int maxLayer = -1;

                        foreach (Guid nodeId in remainingNodeIds)
                        {
                            int nodeLayer = _LayerStorage.GetNodeLayer(nodeId);
                            if (nodeLayer > maxLayer)
                            {
                                maxLayer = nodeLayer;
                                newEntryPoint = nodeId;
                            }
                        }

                        _Storage.EntryPoint = newEntryPoint;
                    }
                    else
                    {
                        _Storage.EntryPoint = null;
                    }
                }

                // Fifth pass: Repair graph connectivity if needed
                // For each updated neighbor, ensure they still have enough connections
                foreach (Guid neighborId in nodesToUpdate)
                {
                    IHnswNode neighbor = await _Storage.GetNodeAsync(neighborId, cancellationToken);
                    int neighborLayer = _LayerStorage.GetNodeLayer(neighborId);
                    Dictionary<int, HashSet<Guid>> connections = neighbor.GetNeighbors();

                    for (int layer = 0; layer <= neighborLayer; layer++)
                    {
                        int currentCount = connections.ContainsKey(layer) ? connections[layer].Count : 0;
                        int targetCount = layer == 0 ? MaxM : M;

                        // If this node has too few connections, try to find more
                        if (currentCount < targetCount / 2) // Repair if less than half the target
                        {
                            // Search for new neighbors
                            Guid? entryPoint = _Storage.EntryPoint;
                            if (entryPoint.HasValue)
                            {
                                List<SearchCandidate> candidates = await SearchLayerAsync(neighbor.Vector, entryPoint.Value, targetCount * 2, layer, cancellationToken);

                                // Filter out existing connections and nodes being removed
                                HashSet<Guid> existingConnections = connections.ContainsKey(layer) ? connections[layer] : new HashSet<Guid>();
                                List<SearchCandidate> validCandidates = candidates
                                    .Where(c => c.NodeId != neighborId &&
                                               !existingConnections.Contains(c.NodeId) &&
                                               !uniqueNodeIds.Contains(c.NodeId))
                                    .Take(targetCount - currentCount)
                                    .ToList();

                                // Add new connections
                                foreach (SearchCandidate validCandidate in validCandidates)
                                {
                                    neighbor.AddNeighbor(layer, validCandidate.NodeId);
                                    IHnswNode candidate = await _Storage.GetNodeAsync(validCandidate.NodeId, cancellationToken);
                                    candidate.AddNeighbor(layer, neighborId);

                                    // Check if candidate needs pruning
                                    Dictionary<int, HashSet<Guid>> candidateConnections = candidate.GetNeighbors();
                                    if (candidateConnections.ContainsKey(layer) &&
                                        candidateConnections[layer].Count > targetCount)
                                    {
                                        // Prune candidate's connections
                                        List<SearchCandidate> pruneCandidates = new List<SearchCandidate>();
                                        foreach (Guid connId in candidateConnections[layer])
                                        {
                                            IHnswNode conn = await _Storage.GetNodeAsync(connId, cancellationToken);
                                            pruneCandidates.Add(new SearchCandidate(DistanceFunction.Distance(candidate.Vector, conn.Vector), connId));
                                        }

                                        List<Guid> newConnections = await SelectNeighborsHeuristicAsync(
                                            candidate.Vector, pruneCandidates, targetCount, layer,
                                            ExtendCandidates, KeepPrunedConnections, cancellationToken);

                                        foreach (Guid connId in candidateConnections[layer].ToList())
                                        {
                                            if (!newConnections.Contains(connId))
                                            {
                                                candidate.RemoveNeighbor(layer, connId);
                                                IHnswNode connNode = await _Storage.GetNodeAsync(connId, cancellationToken);
                                                connNode.RemoveNeighbor(layer, validCandidate.NodeId);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                _IndexLock.Release();
            }
        }

        /// <summary>
        /// Finds the k nearest neighbors to a query vector.
        /// </summary>
        /// <param name="vector">Query vector. Cannot be null. Must match index dimension.</param>
        /// <param name="count">Number of neighbors to return. Minimum: 1, Maximum: 10000.</param>
        /// <param name="ef">Optional search parameter. Minimum: 1, Maximum: 10000. Default: max(EfConstruction, count*2).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Collection of nearest neighbors with distances.</returns>
        public async Task<IEnumerable<VectorResult>> GetTopKAsync(List<float> vector, int count, int? ef = null, CancellationToken cancellationToken = default)
        {
            if (vector == null) throw new ArgumentNullException(nameof(vector));
            if (vector.Count != _VectorDimension)
                throw new ArgumentException($"Vector dimension {vector.Count} does not match index dimension {_VectorDimension}");
            if (count < 1)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be at least 1.");
            if (count > 10000)
                throw new ArgumentOutOfRangeException(nameof(count), "Count greater than 10000 is not recommended.");

            if (ef.HasValue)
            {
                if (ef.Value < 1)
                    throw new ArgumentOutOfRangeException(nameof(ef), "EF must be at least 1.");
                if (ef.Value > 10000)
                    throw new ArgumentOutOfRangeException(nameof(ef), "EF greater than 10000 is not recommended.");
            }

            Guid? entryPointId = _Storage.EntryPoint;
            if (!entryPointId.HasValue)
                return Enumerable.Empty<VectorResult>();

            // Create search context for caching
            SearchContext context = new SearchContext(_Storage, cancellationToken);

            // Use provided ef or calculate based on count
            int searchEf = ef ?? Math.Max(EfConstruction, count * 2);

            Guid currentNearest = entryPointId.Value;

            // Pre-fetch entry point and its neighbors for better performance
            IHnswNode entryNode = await context.GetNodeAsync(entryPointId.Value);
            if (entryNode == null) return Enumerable.Empty<VectorResult>();
            Dictionary<int, HashSet<Guid>> entryNeighbors = entryNode.GetNeighbors();

            // Pre-fetch all neighbors at higher layers
            HashSet<Guid> neighborsToPrefetch = new HashSet<Guid>();
            foreach (HashSet<Guid> layerNeighbors in entryNeighbors.Values)
                neighborsToPrefetch.UnionWith(layerNeighbors);
            if (neighborsToPrefetch.Count > 0)
                await context.PrefetchNodesAsync(neighborsToPrefetch);

            // Search from top layer to layer 0
            int entryPointLayer = _LayerStorage.GetNodeLayer(entryPointId.Value);
            for (int layer = entryPointLayer; layer > 0; layer--)
            {
                currentNearest = await GreedySearchLayerWithContextAsync(vector, currentNearest, layer, context, cancellationToken);
            }

            // Search at layer 0 with ef
            List<SearchCandidate> candidates = await SearchLayerWithContextAsync(vector, currentNearest, searchEf, 0, context, cancellationToken);

            // Build results - nodes are already cached
            List<VectorResult> results = new List<VectorResult>();
            foreach (SearchCandidate candidate in candidates.Take(count))
            {
                IHnswNode node = await context.GetNodeAsync(candidate.NodeId);
                results.Add(new VectorResult
                {
                    GUID = candidate.NodeId,
                    Distance = Math.Abs(candidate.Distance), // Use absolute value to handle negative distances
                    Vectors = new List<float>(node.Vector)
                });
            }

            return results;
        }

        /// <summary>
        /// Exports the current state of the index.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Serializable state of the index. Never null.</returns>
        public async Task<HnswState> ExportStateAsync(CancellationToken cancellationToken = default)
        {
            HnswState state = new HnswState
            {
                VectorDimension = this._VectorDimension,
                Parameters = new HnswParameters
                {
                    M = this.M,
                    MaxM = this.MaxM,
                    EfConstruction = this.EfConstruction,
                    MaxLayers = this.MaxLayers,
                    LevelMultiplier = this.LevelMultiplier,
                    ExtendCandidates = this.ExtendCandidates,
                    KeepPrunedConnections = this.KeepPrunedConnections,
                    DistanceFunctionName = this.DistanceFunction.Name
                },
                Nodes = new List<NodeState>()
            };

            IEnumerable<Guid> nodeIds = await _Storage.GetAllNodeIdsAsync(cancellationToken);
            foreach (Guid nodeId in nodeIds)
            {
                IHnswNode node = await _Storage.GetNodeAsync(nodeId, cancellationToken);
                NodeState nodeState = new NodeState
                {
                    Id = nodeId,
                    Vector = new List<float>(node.Vector),
                    Layer = GetNodeLayer(nodeId),
                    Neighbors = new Dictionary<int, List<Guid>>()
                };

                Dictionary<int, HashSet<Guid>> neighbors = node.GetNeighbors();
                foreach (KeyValuePair<int, HashSet<Guid>> kvp in neighbors)
                {
                    nodeState.Neighbors[kvp.Key] = kvp.Value.ToList();
                }

                state.Nodes.Add(nodeState);
            }

            state.EntryPointId = _Storage.EntryPoint;
            return state;
        }

        /// <summary>
        /// Imports a previously exported state into the index.
        /// </summary>
        /// <param name="state">State to import. Cannot be null. Vector dimension must match index dimension.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task ImportStateAsync(HnswState state, CancellationToken cancellationToken = default)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.VectorDimension != _VectorDimension)
                throw new ArgumentException($"State dimension {state.VectorDimension} does not match index dimension {_VectorDimension}");

            await _IndexLock.WaitAsync(cancellationToken);
            try
            {
                // Clear existing data from _Storage
                IEnumerable<Guid> existingIds = await _Storage.GetAllNodeIdsAsync(cancellationToken);
                foreach (Guid nodeId in existingIds.ToList())
                {
                    await _Storage.RemoveNodeAsync(nodeId, cancellationToken);
                }

                // Clear existing layer data
                _LayerStorage.Clear();

                // Import parameters
                this.M = state.Parameters.M;
                this.MaxM = state.Parameters.MaxM;
                this.EfConstruction = state.Parameters.EfConstruction;
                this.MaxLayers = state.Parameters.MaxLayers;
                this.LevelMultiplier = state.Parameters.LevelMultiplier;
                this.ExtendCandidates = state.Parameters.ExtendCandidates;
                this.KeepPrunedConnections = state.Parameters.KeepPrunedConnections;

                // Set distance function
                switch (state.Parameters.DistanceFunctionName)
                {
                    case "Euclidean":
                        this.DistanceFunction = new EuclideanDistance();
                        break;
                    case "Cosine":
                        this.DistanceFunction = new CosineDistance();
                        break;
                    case "DotProduct":
                        this.DistanceFunction = new DotProductDistance();
                        break;
                    default:
                        // Default to Euclidean if unknown
                        this.DistanceFunction = new EuclideanDistance();
                        break;
                }

                // First pass: add all nodes and their layer assignments
                foreach (NodeState nodeState in state.Nodes)
                {
                    await _Storage.AddNodeAsync(nodeState.Id, nodeState.Vector, cancellationToken);
                    _LayerStorage.SetNodeLayer(nodeState.Id, nodeState.Layer);
                }

                // Second pass: reconstruct connections
                foreach (NodeState nodeState in state.Nodes)
                {
                    IHnswNode node = await _Storage.GetNodeAsync(nodeState.Id, cancellationToken);
                    foreach (KeyValuePair<int, List<Guid>> kvp in nodeState.Neighbors)
                    {
                        foreach (Guid neighborId in kvp.Value)
                        {
                            node.AddNeighbor(kvp.Key, neighborId);
                        }
                    }
                }

                // Set entry point
                _Storage.EntryPoint = state.EntryPointId;
            }
            finally
            {
                _IndexLock.Release();
            }
        }

        // Private methods
        private int GetNodeLayer(Guid nodeId)
        {
            return _LayerStorage.GetNodeLayer(nodeId);
        }

        private void SetNodeLayer(Guid nodeId, int layer)
        {
            _LayerStorage.SetNodeLayer(nodeId, layer);
        }

        private int AssignLevel()
        {
            // Standard HNSW level assignment: -ln(uniform(0,1)) * levelMultiplier
            int level = 0;
            while (_Random.NextDouble() < LevelMultiplier && level < MaxLayers - 1)
            {
                level++;
            }
            return level;
        }

        private async Task<List<Guid>> SelectNeighborsHeuristicAsync(List<float> baseVector, List<SearchCandidate> candidates, int m, int layer, bool extendCandidates, bool keepPrunedConnections, CancellationToken cancellationToken)
        {
            return await Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                List<Guid> returnList = new List<Guid>();
                List<SearchCandidate> discardedList = new List<SearchCandidate>();

                // Sort candidates by distance
                candidates = candidates.OrderBy(c => c.Distance).ToList();

                // Process each candidate
                foreach (SearchCandidate candidate in candidates)
                {
                    if (returnList.Count >= m)
                        break;

                    // Check if candidate is closer to base than to any element in the return list
                    bool shouldAdd = true;
                    foreach (Guid returnId in returnList)
                    {
                        IHnswNode returnNode = await _Storage.GetNodeAsync(returnId, cancellationToken);
                        IHnswNode candidateNode = await _Storage.GetNodeAsync(candidate.NodeId, cancellationToken);
                        float distToReturn = DistanceFunction.Distance(candidateNode.Vector, returnNode.Vector);

                        if (distToReturn < candidate.Distance)
                        {
                            // Candidate is closer to an existing neighbor than to the base
                            shouldAdd = false;
                            discardedList.Add(candidate);
                            break;
                        }
                    }

                    if (shouldAdd)
                    {
                        returnList.Add(candidate.NodeId);
                    }
                }

                // If we have space and extendCandidates is true, add some discarded connections
                if (extendCandidates && returnList.Count < m && discardedList.Count > 0)
                {
                    discardedList = discardedList.OrderBy(d => d.Distance).ToList();
                    foreach (SearchCandidate discarded in discardedList)
                    {
                        if (returnList.Count >= m)
                            break;
                        returnList.Add(discarded.NodeId);
                    }
                }

                return returnList;
            }, cancellationToken);
        }

        private async Task<Guid> GreedySearchLayerAsync(List<float> query, Guid entryPointId, int layer, CancellationToken cancellationToken)
        {
            return await Task.Run(async () =>
            {
                Guid currentNearest = entryPointId;
                IHnswNode entryNode = await _Storage.GetNodeAsync(entryPointId, cancellationToken);
                float currentDist = DistanceFunction.Distance(query, entryNode.Vector);

                bool improved = true;
                while (improved)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    improved = false;
                    IHnswNode currentNode = await _Storage.GetNodeAsync(currentNearest, cancellationToken);
                    Dictionary<int, HashSet<Guid>> neighbors = currentNode.GetNeighbors();

                    if (neighbors.ContainsKey(layer))
                    {
                        foreach (Guid neighborId in neighbors[layer])
                        {
                            IHnswNode neighbor = await _Storage.GetNodeAsync(neighborId, cancellationToken);
                            float dist = DistanceFunction.Distance(query, neighbor.Vector);
                            if (dist < currentDist)
                            {
                                currentDist = dist;
                                currentNearest = neighborId;
                                improved = true;
                            }
                        }
                    }
                }

                return currentNearest;
            }, cancellationToken);
        }

        private async Task<List<SearchCandidate>> SearchLayerAsync(List<float> query, Guid entryPointId, int ef, int layer, CancellationToken cancellationToken)
        {
            return await Task.Run(async () =>
            {
                HashSet<Guid> visited = new HashSet<Guid>();
                MinHeap<Guid> candidates = new MinHeap<Guid>(Comparer<Guid>.Default);
                MinHeap<Guid> dynamicNearestNeighbors = new MinHeap<Guid>(Comparer<Guid>.Default);
                float farthestDistance = float.MaxValue;

                IHnswNode entryPoint = await _Storage.GetNodeAsync(entryPointId, cancellationToken);
                float d = DistanceFunction.Distance(query, entryPoint.Vector);
                candidates.Push(d, entryPointId);
                dynamicNearestNeighbors.Push(-d, entryPointId); // Use negative distance for max-heap behavior
                visited.Add(entryPointId);

                while (candidates.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    (float priority, Guid item) current = candidates.Pop();

                    // Early termination with search optimization
                    if (current.priority > farthestDistance)
                        break;

                    IHnswNode currentNode = await _Storage.GetNodeAsync(current.item, cancellationToken);
                    Dictionary<int, HashSet<Guid>> neighbors = currentNode.GetNeighbors();

                    if (neighbors.ContainsKey(layer))
                    {
                        foreach (Guid neighborId in neighbors[layer])
                        {
                            if (!visited.Contains(neighborId))
                            {
                                visited.Add(neighborId);
                                IHnswNode neighbor = await _Storage.GetNodeAsync(neighborId, cancellationToken);
                                d = DistanceFunction.Distance(query, neighbor.Vector);

                                if (d < farthestDistance || dynamicNearestNeighbors.Count < ef)
                                {
                                    candidates.Push(d, neighborId);
                                    dynamicNearestNeighbors.Push(-d, neighborId);

                                    if (dynamicNearestNeighbors.Count > ef)
                                    {
                                        dynamicNearestNeighbors.Pop();
                                        // Update farthest distance
                                        farthestDistance = -dynamicNearestNeighbors.Peek().priority;
                                    }
                                    else if (dynamicNearestNeighbors.Count == ef)
                                    {
                                        farthestDistance = -dynamicNearestNeighbors.Peek().priority;
                                    }
                                }
                            }
                        }
                    }
                }

                // Convert heap to sorted list
                List<SearchCandidate> result = dynamicNearestNeighbors.GetAll()
                    .Select(item => new SearchCandidate(-item.priority, item.item))
                    .OrderBy(x => x.Distance)
                    .ToList();

                return result;
            }, cancellationToken);
        }

        private async Task<Guid> GreedySearchLayerWithContextAsync(List<float> query, Guid entryPointId, int layer, SearchContext context, CancellationToken cancellationToken)
        {
            Guid currentNearest = entryPointId;
            IHnswNode entryNode = await context.GetNodeAsync(entryPointId);
            float currentDist = DistanceFunction.Distance(query, entryNode.Vector);

            bool improved = true;
            while (improved)
            {
                cancellationToken.ThrowIfCancellationRequested();
                improved = false;
                IHnswNode currentNode = await context.GetNodeAsync(currentNearest);
                Dictionary<int, HashSet<Guid>> neighbors = currentNode.GetNeighbors();

                if (neighbors.ContainsKey(layer))
                {
                    // Pre-fetch all neighbors at this layer for better performance
                    List<Guid> layerNeighbors = neighbors[layer].ToList();
                    if (layerNeighbors.Count > 0)
                    {
                        await context.PrefetchNodesAsync(layerNeighbors);

                        foreach (Guid neighborId in layerNeighbors)
                        {
                            IHnswNode neighbor = await context.GetNodeAsync(neighborId);
                            float dist = DistanceFunction.Distance(query, neighbor.Vector);
                            if (dist < currentDist)
                            {
                                currentDist = dist;
                                currentNearest = neighborId;
                                improved = true;
                            }
                        }
                    }
                }
            }

            return currentNearest;
        }

        private async Task<List<SearchCandidate>> SearchLayerWithContextAsync(List<float> query, Guid entryPointId, int ef, int layer, SearchContext context, CancellationToken cancellationToken)
        {
            HashSet<Guid> visited = new HashSet<Guid>();
            MinHeap<Guid> candidates = new MinHeap<Guid>(Comparer<Guid>.Default);
            MinHeap<Guid> dynamicNearestNeighbors = new MinHeap<Guid>(Comparer<Guid>.Default);
            float farthestDistance = float.MaxValue;

            IHnswNode entryPoint = await context.GetNodeAsync(entryPointId);
            float d = DistanceFunction.Distance(query, entryPoint.Vector);
            candidates.Push(d, entryPointId);
            dynamicNearestNeighbors.Push(-d, entryPointId); // Use negative distance for max-heap behavior
            visited.Add(entryPointId);

            while (candidates.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (float priority, Guid item) current = candidates.Pop();

                // Early termination with search optimization
                if (current.priority > farthestDistance)
                    break;

                IHnswNode currentNode = await context.GetNodeAsync(current.item);
                Dictionary<int, HashSet<Guid>> neighbors = currentNode.GetNeighbors();

                if (neighbors.ContainsKey(layer))
                {
                    // Batch-load all unvisited neighbors
                    List<Guid> unvisitedNeighbors = neighbors[layer]
                        .Where(n => !visited.Contains(n))
                        .ToList();

                    if (unvisitedNeighbors.Count > 0)
                    {
                        // Pre-fetch all neighbors in one batch
                        await context.PrefetchNodesAsync(unvisitedNeighbors);

                        // Process neighbors
                        foreach (Guid neighborId in unvisitedNeighbors)
                        {
                            visited.Add(neighborId);
                            IHnswNode neighbor = await context.GetNodeAsync(neighborId);
                            d = DistanceFunction.Distance(query, neighbor.Vector);

                            if (d < farthestDistance || dynamicNearestNeighbors.Count < ef)
                            {
                                candidates.Push(d, neighborId);
                                dynamicNearestNeighbors.Push(-d, neighborId);

                                if (dynamicNearestNeighbors.Count > ef)
                                {
                                    dynamicNearestNeighbors.Pop();
                                    // Update farthest distance
                                    farthestDistance = -dynamicNearestNeighbors.Peek().priority;
                                }
                                else if (dynamicNearestNeighbors.Count == ef)
                                {
                                    farthestDistance = -dynamicNearestNeighbors.Peek().priority;
                                }
                            }
                        }
                    }
                }
            }

            // Convert heap to sorted list
            List<SearchCandidate> result = dynamicNearestNeighbors.GetAll()
                .Select(item => new SearchCandidate(-item.priority, item.item))
                .OrderBy(x => x.Distance)
                .ToList();

            return result;
        }
    }
}