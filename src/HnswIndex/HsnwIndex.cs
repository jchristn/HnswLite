namespace Hnsw
{
    using Hsnw;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Main HNSW (Hierarchical Navigable Small World) index implementation.
    /// </summary>
    public class HnswIndex
    {
        // Private members
        private readonly IHnswStorage storage;
        private readonly IHnswLayerStorage layerStorage;
        private readonly SemaphoreSlim indexLock = new SemaphoreSlim(1, 1); 
        private readonly int vectorDimension;

        private Random random;
        private IDistanceFunction _distanceFunction = new EuclideanDistance();
        private int _m = 16;
        private int _maxM = 32;
        private int _efConstruction = 200;
        private int _maxLayers = 16;
        private double _levelMultiplier = 1.0 / Math.Log(2.0);
        private bool _extendCandidates = false;
        private bool _keepPrunedConnections = false;
        private int? _seed = null;

        // Public members
        /// <summary>
        /// Gets or sets the distance function used for vector comparisons.
        /// Cannot be null. Setting to null creates a new EuclideanDistance instance.
        /// Default: EuclideanDistance.
        /// </summary>
        public IDistanceFunction DistanceFunction
        {
            get => _distanceFunction;
            set => _distanceFunction = value ?? new EuclideanDistance();
        }

        /// <summary>
        /// Gets or sets the maximum number of connections per layer (except layer 0).
        /// Minimum: 2, Maximum: 100, Default: 16.
        /// Typical values range from 8 to 48.
        /// </summary>
        public int M
        {
            get => _m;
            set
            {
                if (value < 2)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "M must be at least 2 for meaningful connectivity.");
                if (value > 100)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "M values greater than 100 are not recommended due to performance implications.");
                _m = value;
            }
        }

        /// <summary>
        /// Gets or sets the maximum number of connections for layer 0.
        /// Minimum: 1, Maximum: 200, Default: 32.
        /// Should be greater than or equal to M, typically 2*M.
        /// </summary>
        public int MaxM
        {
            get => _maxM;
            set
            {
                if (value < 1)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "MaxM must be at least 1.");
                if (value > 200)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "MaxM values greater than 200 are not recommended due to performance implications.");
                _maxM = value;
            }
        }

        /// <summary>
        /// Gets or sets the size of the dynamic candidate list.
        /// Minimum: 1, Maximum: 2000, Default: 200.
        /// Higher values improve recall but decrease construction speed.
        /// </summary>
        public int EfConstruction
        {
            get => _efConstruction;
            set
            {
                if (value < 1)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "EfConstruction must be at least 1.");
                if (value > 2000)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "EfConstruction values greater than 2000 provide diminishing returns.");
                _efConstruction = value;
            }
        }

        /// <summary>
        /// Gets or sets the maximum number of layers in the graph.
        /// Minimum: 1, Maximum: 64, Default: 16.
        /// Typically between 10 and 30.
        /// </summary>
        public int MaxLayers
        {
            get => _maxLayers;
            set
            {
                if (value < 1)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "MaxLayers must be at least 1.");
                if (value > 64)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "MaxLayers greater than 64 is not recommended.");
                _maxLayers = value;
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
            get => _levelMultiplier;
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
                _levelMultiplier = value;
            }
        }

        /// <summary>
        /// Gets or sets whether to extend candidates with neighbors' neighbors.
        /// Default: false.
        /// Can improve recall at the cost of construction time.
        /// </summary>
        public bool ExtendCandidates
        {
            get => _extendCandidates;
            set => _extendCandidates = value;
        }

        /// <summary>
        /// Gets or sets whether to add pruned connections to lower levels.
        /// Default: false.
        /// Can improve connectivity at the cost of more memory usage.
        /// </summary>
        public bool KeepPrunedConnections
        {
            get => _keepPrunedConnections;
            set => _keepPrunedConnections = value;
        }

        /// <summary>
        /// Gets or sets the random seed for reproducible results.
        /// Minimum: -1 (random seed), Maximum: int.MaxValue, Default: -1.
        /// Use -1 for random seed, or any non-negative value for deterministic behavior.
        /// </summary>
        public int Seed
        {
            get => _seed ?? -1;
            set
            {
                if (value < -1)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "Seed must be -1 (for random) or a non-negative value.");

                _seed = value == -1 ? null : value;
                random = _seed.HasValue ? new Random(_seed.Value) : new Random();
            }
        }

        /// <summary>
        /// Gets the vector dimension for this index.
        /// </summary>
        public int VectorDimension => vectorDimension;

        // Constructors
        /// <summary>
        /// Initializes a new instance of the HNSWIndex class with custom storage.
        /// </summary>
        /// <param name="dimension">The dimensionality of vectors to be indexed. Minimum: 1, Maximum: 4096.</param>
        /// <param name="storage">Storage backend implementation. Cannot be null.</param>
        /// <param name="layerStorage">Layer storage backend implementation.  Cannot be null.</param>
        public HnswIndex(int dimension, IHnswStorage storage, IHnswLayerStorage layerStorage)
        {
            if (dimension < 1)
                throw new ArgumentOutOfRangeException(nameof(dimension),
                    "Dimension must be at least 1.");
            if (dimension > 4096)
                throw new ArgumentOutOfRangeException(nameof(dimension),
                    "Dimension greater than 4096 is not recommended due to memory and performance constraints.");
            if (storage == null)
                throw new ArgumentNullException(nameof(storage));
            if (layerStorage == null)
                throw new ArgumentNullException(nameof(layerStorage));

            this.vectorDimension = dimension;
            this.storage = storage;
            this.layerStorage = layerStorage;
            this.random = new Random();
        }

        /// <summary>
        /// Initializes a new instance of the HNSWIndex class with custom storage and seed.
        /// </summary>
        /// <param name="dimension">The dimensionality of vectors to be indexed. Minimum: 1, Maximum: 4096.</param>
        /// <param name="storage">Storage backend implementation. Cannot be null.</param>
        /// <param name="layerStorage">Layer storage backend implementation.  Cannot be null.</param>
        /// <param name="seed">Random seed for reproducible results. Use null for random seed.</param>
        public HnswIndex(int dimension, IHnswStorage storage, IHnswLayerStorage layerStorage, int? seed) : this(dimension, storage, layerStorage)
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
            if (vector.Count != vectorDimension)
                throw new ArgumentException($"Vector dimension {vector.Count} does not match index dimension {vectorDimension}");

            await indexLock.WaitAsync(cancellationToken);
            try
            {
                await storage.AddNodeAsync(guid, vector, cancellationToken);

                var count = await storage.GetCountAsync(cancellationToken);
                if (count == 1)
                {
                    SetNodeLayer(guid, 0);
                    return;
                }

                // Assign layer using standard HNSW approach
                int nodeLevel = AssignLevel();
                SetNodeLayer(guid, nodeLevel);

                // Get entry point
                var entryPointId = storage.EntryPoint.Value;
                var currentNearest = entryPointId;

                // Search for nearest neighbor from top to target layer
                var entryPointLayer = GetNodeLayer(entryPointId);
                for (int layer = entryPointLayer; layer > nodeLevel; layer--)
                {
                    currentNearest = await GreedySearchLayerAsync(vector, currentNearest, layer, cancellationToken);
                }

                // Insert at all layers from nodeLevel to 0
                for (int layer = nodeLevel; layer >= 0; layer--)
                {
                    var candidates = await SearchLayerAsync(vector, currentNearest, EfConstruction, layer, cancellationToken);

                    int mValue = layer == 0 ? MaxM : M;

                    // Select M neighbors using a heuristic
                    var neighbors = await SelectNeighborsHeuristicAsync(vector, candidates, mValue, layer, ExtendCandidates, KeepPrunedConnections, cancellationToken);

                    var newNode = await storage.GetNodeAsync(guid, cancellationToken);
                    foreach (var neighborId in neighbors)
                    {
                        // Add bidirectional connections
                        newNode.AddNeighbor(layer, neighborId);
                        var neighbor = await storage.GetNodeAsync(neighborId, cancellationToken);
                        neighbor.AddNeighbor(layer, guid);

                        // Prune neighbor's connections if needed
                        var neighborConnections = neighbor.GetNeighbors();
                        if (neighborConnections.ContainsKey(layer))
                        {
                            var currentConnections = neighborConnections[layer];
                            if (currentConnections.Count > mValue)
                            {
                                // Prune to M connections using heuristic
                                var pruneCandidates = new List<(float, Guid)>();
                                foreach (var connId in currentConnections)
                                {
                                    var conn = await storage.GetNodeAsync(connId, cancellationToken);
                                    pruneCandidates.Add((DistanceFunction.Distance(neighbor.Vector, conn.Vector), connId));
                                }

                                var newConnections = await SelectNeighborsHeuristicAsync(neighbor.Vector, pruneCandidates, mValue, layer, ExtendCandidates, KeepPrunedConnections, cancellationToken);

                                // Remove connections not in newConnections
                                foreach (var connId in currentConnections.ToList())
                                {
                                    if (!newConnections.Contains(connId))
                                    {
                                        neighbor.RemoveNeighbor(layer, connId);
                                        var connNode = await storage.GetNodeAsync(connId, cancellationToken);
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
                    storage.EntryPoint = guid;
                }
            }
            finally
            {
                indexLock.Release();
            }
        }

        /// <summary>
        /// Adds multiple vectors to the index.
        /// </summary>
        /// <param name="items">Collection of (id, vector) pairs to add. Cannot be null or contain null vectors.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task AddBatchAsync(IEnumerable<(Guid id, List<float> vector)> items, CancellationToken cancellationToken = default)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            // Validate all items first
            foreach (var (id, vector) in items)
            {
                if (vector == null)
                    throw new ArgumentNullException(nameof(vector), $"Vector for ID {id} is null");
                if (vector.Count != vectorDimension)
                    throw new ArgumentException($"Vector dimension {vector.Count} for ID {id} does not match index dimension {vectorDimension}");
            }

            // Add items sequentially
            foreach (var (id, vector) in items)
            {
                await AddAsync(id, vector, cancellationToken);
            }
        }

        /// <summary>
        /// Removes a vector from the index.
        /// </summary>
        /// <param name="guid">Identifier of the vector to remove.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task RemoveAsync(Guid guid, CancellationToken cancellationToken = default)
        {
            await indexLock.WaitAsync(cancellationToken);
            try
            {
                var (success, nodeToRemove) = await storage.TryGetNodeAsync(guid, cancellationToken);
                if (!success)
                    return;

                // Get all neighbors before removing the node
                var neighbors = nodeToRemove.GetNeighbors();

                // Remove the node from storage
                await storage.RemoveNodeAsync(guid, cancellationToken);

                // Remove from layer storage
                layerStorage.RemoveNodeLayer(guid);

                // Remove all connections to this node from other nodes
                var allNodeIds = await storage.GetAllNodeIdsAsync(cancellationToken);
                foreach (var nodeId in allNodeIds)
                {
                    var node = await storage.GetNodeAsync(nodeId, cancellationToken);
                    foreach (var layer in neighbors.Keys)
                    {
                        node.RemoveNeighbor(layer, guid);
                    }
                }

                // Update entry point if the removed node was the entry point
                if (storage.EntryPoint == guid)
                {
                    // Find a new entry point - pick the node with the highest layer
                    var remainingNodeIds = await storage.GetAllNodeIdsAsync(cancellationToken);
                    if (remainingNodeIds.Any())
                    {
                        Guid? newEntryPoint = null;
                        int maxLayer = -1;

                        foreach (var nodeId in remainingNodeIds)
                        {
                            int nodeLayer = layerStorage.GetNodeLayer(nodeId);
                            if (nodeLayer > maxLayer)
                            {
                                maxLayer = nodeLayer;
                                newEntryPoint = nodeId;
                            }
                        }

                        storage.EntryPoint = newEntryPoint;
                    }
                    else
                    {
                        storage.EntryPoint = null;
                    }
                }
            }
            finally
            {
                indexLock.Release();
            }
        }

        /// <summary>
        /// Removes multiple vectors from the index.
        /// </summary>
        /// <param name="guids">Collection of identifiers to remove. Cannot be null.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task RemoveBatchAsync(IEnumerable<Guid> guids, CancellationToken cancellationToken = default)
        {
            if (guids == null) throw new ArgumentNullException(nameof(guids));

            foreach (var guid in guids)
            {
                await RemoveAsync(guid, cancellationToken);
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
            if (vector.Count != vectorDimension)
                throw new ArgumentException($"Vector dimension {vector.Count} does not match index dimension {vectorDimension}");
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

            var entryPointId = storage.EntryPoint;
            if (!entryPointId.HasValue)
                return Enumerable.Empty<VectorResult>();

            // Use provided ef or calculate based on count
            var searchEf = ef ?? Math.Max(EfConstruction, count * 2);

            var currentNearest = entryPointId.Value;

            // Search from top layer to layer 0
            var entryPointLayer = layerStorage.GetNodeLayer(entryPointId.Value);
            for (int layer = entryPointLayer; layer > 0; layer--)
            {
                currentNearest = await GreedySearchLayerAsync(vector, currentNearest, layer, cancellationToken);
            }

            // Search at layer 0 with ef
            var candidates = await SearchLayerAsync(vector, currentNearest, searchEf, 0, cancellationToken);

            var results = new List<VectorResult>();
            foreach (var candidate in candidates.Take(count))
            {
                var node = await storage.GetNodeAsync(candidate.Item2, cancellationToken);
                results.Add(new VectorResult
                {
                    GUID = candidate.Item2,
                    Distance = Math.Abs(candidate.Item1), // Use absolute value to handle negative distances
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
            var state = new HnswState
            {
                VectorDimension = this.vectorDimension,
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

            var nodeIds = await storage.GetAllNodeIdsAsync(cancellationToken);
            foreach (var nodeId in nodeIds)
            {
                var node = await storage.GetNodeAsync(nodeId, cancellationToken);
                var nodeState = new NodeState
                {
                    Id = nodeId,
                    Vector = new List<float>(node.Vector),
                    Layer = GetNodeLayer(nodeId),
                    Neighbors = new Dictionary<int, List<Guid>>()
                };

                var neighbors = node.GetNeighbors();
                foreach (var kvp in neighbors)
                {
                    nodeState.Neighbors[kvp.Key] = kvp.Value.ToList();
                }

                state.Nodes.Add(nodeState);
            }

            state.EntryPointId = storage.EntryPoint;
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
            if (state.VectorDimension != vectorDimension)
                throw new ArgumentException($"State dimension {state.VectorDimension} does not match index dimension {vectorDimension}");

            await indexLock.WaitAsync(cancellationToken);
            try
            {
                // Clear existing data from storage
                var existingIds = await storage.GetAllNodeIdsAsync(cancellationToken);
                foreach (var nodeId in existingIds.ToList())
                {
                    await storage.RemoveNodeAsync(nodeId, cancellationToken);
                }

                // Clear existing layer data
                layerStorage.Clear();

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
                foreach (var nodeState in state.Nodes)
                {
                    await storage.AddNodeAsync(nodeState.Id, nodeState.Vector, cancellationToken);
                    layerStorage.SetNodeLayer(nodeState.Id, nodeState.Layer);
                }

                // Second pass: reconstruct connections
                foreach (var nodeState in state.Nodes)
                {
                    var node = await storage.GetNodeAsync(nodeState.Id, cancellationToken);
                    foreach (var kvp in nodeState.Neighbors)
                    {
                        foreach (var neighborId in kvp.Value)
                        {
                            node.AddNeighbor(kvp.Key, neighborId);
                        }
                    }
                }

                // Set entry point
                storage.EntryPoint = state.EntryPointId;
            }
            finally
            {
                indexLock.Release();
            }
        }

        // Private methods
        private int GetNodeLayer(Guid nodeId)
        {
            return layerStorage.GetNodeLayer(nodeId);
        }

        private void SetNodeLayer(Guid nodeId, int layer)
        {
            layerStorage.SetNodeLayer(nodeId, layer);
        }

        private int AssignLevel()
        {
            // Standard HNSW level assignment: -ln(uniform(0,1)) * levelMultiplier
            int level = 0;
            while (random.NextDouble() < LevelMultiplier && level < MaxLayers - 1)
            {
                level++;
            }
            return level;
        }

        private async Task<List<Guid>> SelectNeighborsHeuristicAsync(List<float> baseVector, List<(float, Guid)> candidates, int m, int layer, bool extendCandidates, bool keepPrunedConnections, CancellationToken cancellationToken)
        {
            return await Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var returnList = new List<Guid>();
                var discardedList = new List<(float distance, Guid id)>();

                // Sort candidates by distance
                candidates = candidates.OrderBy(c => c.Item1).ToList();

                // Process each candidate
                foreach (var (distance, candidateId) in candidates)
                {
                    if (returnList.Count >= m)
                        break;

                    // Check if candidate is closer to base than to any element in the return list
                    bool shouldAdd = true;
                    foreach (var returnId in returnList)
                    {
                        var returnNode = await storage.GetNodeAsync(returnId, cancellationToken);
                        var candidateNode = await storage.GetNodeAsync(candidateId, cancellationToken);
                        var distToReturn = DistanceFunction.Distance(candidateNode.Vector, returnNode.Vector);

                        if (distToReturn < distance)
                        {
                            // Candidate is closer to an existing neighbor than to the base
                            shouldAdd = false;
                            discardedList.Add((distance, candidateId));
                            break;
                        }
                    }

                    if (shouldAdd)
                    {
                        returnList.Add(candidateId);
                    }
                }

                // If we have space and extendCandidates is true, add some discarded connections
                if (extendCandidates && returnList.Count < m && discardedList.Count > 0)
                {
                    discardedList = discardedList.OrderBy(d => d.distance).ToList();
                    foreach (var (distance, id) in discardedList)
                    {
                        if (returnList.Count >= m)
                            break;
                        returnList.Add(id);
                    }
                }

                return returnList;
            }, cancellationToken);
        }

        private async Task<Guid> GreedySearchLayerAsync(List<float> query, Guid entryPointId, int layer, CancellationToken cancellationToken)
        {
            return await Task.Run(async () =>
            {
                var currentNearest = entryPointId;
                var entryNode = await storage.GetNodeAsync(entryPointId, cancellationToken);
                var currentDist = DistanceFunction.Distance(query, entryNode.Vector);

                bool improved = true;
                while (improved)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    improved = false;
                    var currentNode = await storage.GetNodeAsync(currentNearest, cancellationToken);
                    var neighbors = currentNode.GetNeighbors();

                    if (neighbors.ContainsKey(layer))
                    {
                        foreach (var neighborId in neighbors[layer])
                        {
                            var neighbor = await storage.GetNodeAsync(neighborId, cancellationToken);
                            var dist = DistanceFunction.Distance(query, neighbor.Vector);
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

        private async Task<List<(float, Guid)>> SearchLayerAsync(List<float> query, Guid entryPointId, int ef, int layer, CancellationToken cancellationToken)
        {
            return await Task.Run(async () =>
            {
                var visited = new HashSet<Guid>();
                var candidates = new MinHeap<Guid>(Comparer<Guid>.Default);
                var dynamicNearestNeighbors = new MinHeap<Guid>(Comparer<Guid>.Default);
                var farthestDistance = float.MaxValue;

                var entryPoint = await storage.GetNodeAsync(entryPointId, cancellationToken);
                float d = DistanceFunction.Distance(query, entryPoint.Vector);
                candidates.Push(d, entryPointId);
                dynamicNearestNeighbors.Push(-d, entryPointId); // Use negative distance for max-heap behavior
                visited.Add(entryPointId);

                while (candidates.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var current = candidates.Pop();

                    // Early termination with search optimization
                    if (current.priority > farthestDistance)
                        break;

                    var currentNode = await storage.GetNodeAsync(current.item, cancellationToken);
                    var neighbors = currentNode.GetNeighbors();

                    if (neighbors.ContainsKey(layer))
                    {
                        foreach (var neighborId in neighbors[layer])
                        {
                            if (!visited.Contains(neighborId))
                            {
                                visited.Add(neighborId);
                                var neighbor = await storage.GetNodeAsync(neighborId, cancellationToken);
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
                var result = dynamicNearestNeighbors.GetAll()
                    .Select(item => (-item.priority, item.item))
                    .OrderBy(x => x.Item1)
                    .ToList();

                return result;
            }, cancellationToken);
        }
    }
}