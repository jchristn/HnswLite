namespace Hnsw
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Represents the state of a node for serialization.
    /// Used for exporting and importing HNSW index state.
    /// </summary>
    public class NodeState
    {

        #region Private-Members

        private Guid _Id = Guid.Empty;
        private List<float> _Vector = new List<float>();
        private int _Layer = 0;
        private Dictionary<int, List<Guid>> _Neighbors = new Dictionary<int, List<Guid>>();

        #endregion

        #region Public-Members
        /// <summary>
        /// Gets or sets the node identifier.
        /// Cannot be Guid.Empty.
        /// Default: Guid.Empty (should be set to valid value before use).
        /// </summary>
        public Guid Id
        {
            get => _Id;
            set
            {
                if (value == Guid.Empty)
                    throw new ArgumentException("Id cannot be Guid.Empty.", nameof(value));
                _Id = value;
            }
        }

        /// <summary>
        /// Gets or sets the vector data.
        /// Cannot be null. Setting to null creates a new empty list.
        /// Vector dimension typically ranges from 1 to 4096.
        /// All values should be finite (not NaN or Infinity).
        /// Default: Empty list.
        /// </summary>
        public List<float> Vector
        {
            get => _Vector;
            set
            {
                if (value == null)
                {
                    _Vector = new List<float>();
                }
                else
                {
                    // Validate vector values
                    for (int i = 0; i < value.Count; i++)
                    {
                        if (float.IsNaN(value[i]) || float.IsInfinity(value[i]))
                            throw new ArgumentException($"Vector contains invalid value at index {i}. All values must be finite.", nameof(value));
                    }
                    _Vector = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the layer assignment.
        /// Minimum: 0, Maximum: 63, Default: 0.
        /// Represents the highest layer where this node appears in the HNSW graph.
        /// </summary>
        public int Layer
        {
            get => _Layer;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "Layer cannot be negative.");
                if (value > 63)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "Layer cannot exceed 63.");
                _Layer = value;
            }
        }

        /// <summary>
        /// Gets or sets the neighbor connections by layer.
        /// Cannot be null. Setting to null creates a new empty dictionary.
        /// Keys are layer numbers (0 to Layer), values are lists of neighbor IDs.
        /// Each layer can have different connection limits:
        /// - Layer 0: Up to MaxM connections (typically 32)
        /// - Other layers: Up to M connections (typically 16)
        /// Default: Empty dictionary.
        /// </summary>
        public Dictionary<int, List<Guid>> Neighbors
        {
            get => _Neighbors;
            set
            {
                if (value == null)
                {
                    _Neighbors = new Dictionary<int, List<Guid>>();
                }
                else
                {
                    // Validate the dictionary
                    Dictionary<int, List<Guid>> validatedNeighbors = new Dictionary<int, List<Guid>>();

                    foreach (KeyValuePair<int, List<Guid>> kvp in value)
                    {
                        // Validate layer number
                        if (kvp.Key < 0)
                            throw new ArgumentException($"Layer {kvp.Key} cannot be negative.", nameof(value));
                        if (kvp.Key > 63)
                            throw new ArgumentException($"Layer {kvp.Key} cannot exceed 63.", nameof(value));

                        // Ensure list is not null and doesn't contain empty GUIDs
                        List<Guid> neighborList = kvp.Value ?? new List<Guid>();
                        List<Guid> validatedList = new List<Guid>();

                        foreach (Guid neighborId in neighborList)
                        {
                            if (neighborId == Guid.Empty)
                                throw new ArgumentException($"Neighbor list for layer {kvp.Key} contains Guid.Empty.", nameof(value));
                            if (neighborId == _Id && _Id != Guid.Empty)
                                throw new ArgumentException($"Node cannot be its own neighbor (layer {kvp.Key}).", nameof(value));
                            validatedList.Add(neighborId);
                        }

                        if (validatedList.Count > 0)
                        {
                            validatedNeighbors[kvp.Key] = validatedList;
                        }
                    }

                    _Neighbors = validatedNeighbors;
                }
            }
        }

        #endregion

        #region Constructors-and-Factories
        /// <summary>
        /// Initializes a new instance of the NodeState class with default values.
        /// </summary>
        public NodeState()
        {
            // All fields are already initialized with their default values
        }

        /// <summary>
        /// Initializes a new instance of the NodeState class with specified ID and vector.
        /// </summary>
        /// <param name="id">The node identifier. Cannot be Guid.Empty.</param>
        /// <param name="vector">The vector data. Cannot be null.</param>
        public NodeState(Guid id, List<float> vector)
        {
            Id = id;
            Vector = vector ?? throw new ArgumentNullException(nameof(vector));
        }

        /// <summary>
        /// Initializes a new instance of the NodeState class with full specification.
        /// </summary>
        /// <param name="id">The node identifier. Cannot be Guid.Empty.</param>
        /// <param name="vector">The vector data. Cannot be null.</param>
        /// <param name="layer">The layer assignment. Minimum: 0, Maximum: 63.</param>
        /// <param name="neighbors">The neighbor connections. Can be null (creates empty dictionary).</param>
        public NodeState(Guid id, List<float> vector, int layer, Dictionary<int, List<Guid>>? neighbors = null)
        {
            Id = id;
            Vector = vector ?? throw new ArgumentNullException(nameof(vector));
            Layer = layer;
            Neighbors = neighbors ?? new Dictionary<int, List<Guid>>();
        }

        #endregion

        #region Public-Methods
        /// <summary>
        /// Validates that the node state is consistent and ready for use.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when validation fails.</exception>
        public void Validate()
        {
            // Check ID
            if (_Id == Guid.Empty)
                throw new InvalidOperationException("Node ID cannot be Guid.Empty.");

            // Check vector
            if (_Vector == null || _Vector.Count == 0)
                throw new InvalidOperationException("Node vector cannot be null or empty.");

            // Check that all neighbor layers are within valid range
            foreach (KeyValuePair<int, List<Guid>> kvp in _Neighbors)
            {
                if (kvp.Key < 0 || kvp.Key > _Layer)
                    throw new InvalidOperationException(
                        $"Neighbor layer {kvp.Key} is outside valid range (0 to {_Layer}).");

                // Check for self-references
                if (kvp.Value.Contains(_Id))
                    throw new InvalidOperationException(
                        $"Node cannot be its own neighbor (found in layer {kvp.Key}).");
            }
        }

        /// <summary>
        /// Gets the total number of neighbors across all layers.
        /// </summary>
        /// <returns>The total neighbor count.</returns>
        public int GetTotalNeighborCount()
        {
            return _Neighbors.Values.Sum(list => list.Count);
        }

        /// <summary>
        /// Gets the neighbors at a specific layer.
        /// </summary>
        /// <param name="layer">The layer number. Minimum: 0, Maximum: 63.</param>
        /// <returns>A list of neighbor IDs at the specified layer, or empty list if layer has no neighbors.</returns>
        public List<Guid> GetNeighborsAtLayer(int layer)
        {
            if (layer < 0)
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot be negative.");
            if (layer > 63)
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer cannot exceed 63.");

            return _Neighbors.TryGetValue(layer, out List<Guid>? neighbors)
                ? new List<Guid>(neighbors)
                : new List<Guid>();
        }

        /// <summary>
        /// Creates a deep copy of this NodeState.
        /// </summary>
        /// <returns>A new NodeState instance with copied values.</returns>
        public NodeState Clone()
        {
            NodeState clone = new NodeState
            {
                _Id = this._Id,
                Vector = new List<float>(this._Vector),
                Layer = this._Layer,
                Neighbors = new Dictionary<int, List<Guid>>()
            };

            foreach (KeyValuePair<int, List<Guid>> kvp in this._Neighbors)
            {
                clone._Neighbors[kvp.Key] = new List<Guid>(kvp.Value);
            }

            return clone;
        }

        #endregion
    }
}