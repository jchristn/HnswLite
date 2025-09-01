namespace Hsnw
{
    using System;
    using System.Collections.Generic;
    using Hnsw;

    /// <summary>
    /// Represents the complete state of an HNSW index.
    /// </summary>
    public class HnswState
    {
        // Private backing fields
        private int _vectorDimension = 128;
        private HnswParameters _parameters = new HnswParameters();
        private List<NodeState> _nodes = new List<NodeState>();
        private Guid? _entryPointId = null;

        /// <summary>
        /// Gets or sets the vector dimension.
        /// Minimum: 1, Maximum: 4096, Default: 128.
        /// Common values: 128, 256, 512, 768, 1024.
        /// </summary>
        public int VectorDimension
        {
            get => _vectorDimension;
            set
            {
                if (value < 1)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "VectorDimension must be at least 1.");
                if (value > 4096)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "VectorDimension greater than 4096 is not recommended due to memory and performance constraints.");
                _vectorDimension = value;
            }
        }

        /// <summary>
        /// Gets or sets the index parameters.
        /// Cannot be null. Setting to null creates a new instance with default values.
        /// Default: New HnswParameters instance with default values.
        /// </summary>
        public HnswParameters Parameters
        {
            get => _parameters;
            set => _parameters = value ?? new HnswParameters();
        }

        /// <summary>
        /// Gets or sets the collection of nodes.
        /// Cannot be null. Setting to null creates a new empty list.
        /// Default: Empty list.
        /// Maximum recommended size: 100,000,000 nodes (depending on available memory).
        /// </summary>
        public List<NodeState> Nodes
        {
            get => _nodes;
            set
            {
                if (value == null)
                {
                    _nodes = new List<NodeState>();
                }
                else
                {
                    // Validate that the list doesn't contain null elements
                    foreach (NodeState node in value)
                    {
                        if (node == null)
                            throw new ArgumentException("Nodes list cannot contain null elements.", nameof(value));
                    }
                    _nodes = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the entry point node ID.
        /// Can be null when the index is empty.
        /// Default: null.
        /// </summary>
        public Guid? EntryPointId
        {
            get => _entryPointId;
            set => _entryPointId = value;
        }

        /// <summary>
        /// Initializes a new instance of the HnswState class with default values.
        /// </summary>
        public HnswState()
        {

        }

        /// <summary>
        /// Initializes a new instance of the HnswState class with specified vector dimension.
        /// </summary>
        /// <param name="vectorDimension">The dimension of vectors to be indexed.</param>
        public HnswState(int vectorDimension)
        {
            VectorDimension = vectorDimension;
        }

        /// <summary>
        /// Validates that the state is consistent and ready for use.
        /// </summary>
        public void Validate()
        {
            // Validate parameters
            Parameters.Validate();

            // Check if entry point exists in nodes when specified
            if (EntryPointId.HasValue && Nodes.Count > 0)
            {
                bool entryPointExists = false;
                foreach (NodeState node in Nodes)
                {
                    if (node.Id == EntryPointId.Value)
                    {
                        entryPointExists = true;
                        break;
                    }
                }

                if (!entryPointExists)
                    throw new InvalidOperationException(
                        $"EntryPointId {EntryPointId.Value} does not exist in the Nodes collection.");
            }

            // Check if we have nodes but no entry point
            if (Nodes.Count > 0 && !EntryPointId.HasValue)
            {
                throw new InvalidOperationException(
                    "Index has nodes but no entry point specified.");
            }

            // Validate all nodes have consistent vector dimensions
            foreach (NodeState node in Nodes)
            {
                if (node.Vector != null && node.Vector.Count != VectorDimension)
                {
                    throw new InvalidOperationException(
                        $"Node {node.Id} has vector dimension {node.Vector.Count} " +
                        $"but index expects dimension {VectorDimension}.");
                }
            }
        }

        /// <summary>
        /// Gets the total number of nodes in the index.
        /// </summary>
        public int NodeCount => Nodes.Count;

        /// <summary>
        /// Checks if the index is empty (has no nodes).
        /// </summary>
        public bool IsEmpty => Nodes.Count == 0;
    }
}