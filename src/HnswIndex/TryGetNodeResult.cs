namespace Hnsw
{
    /// <summary>
    /// Represents the result of a TryGetNode operation.
    /// </summary>
    public class TryGetNodeResult
    {
        #region Public-Members

        /// <summary>
        /// Gets or sets a value indicating whether the node was found.
        /// </summary>
        public bool Success 
        { 
            get => _Success;
            set => _Success = value;
        }

        /// <summary>
        /// Gets or sets the node if found, null otherwise.
        /// </summary>
        public IHnswNode? Node 
        { 
            get => _Node;
            set => _Node = value;
        }

        #endregion

        #region Private-Members

        private bool _Success;
        private IHnswNode? _Node;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the TryGetNodeResult class.
        /// </summary>
        public TryGetNodeResult()
        {
        }

        /// <summary>
        /// Initializes a new instance of the TryGetNodeResult class with specified values.
        /// </summary>
        /// <param name="success">Whether the node was found.</param>
        /// <param name="node">The node if found. Can be null.</param>
        public TryGetNodeResult(bool success, IHnswNode? node)
        {
            _Success = success;
            _Node = node;
        }

        /// <summary>
        /// Creates a successful result with the specified node.
        /// </summary>
        /// <param name="node">The found node. Cannot be null.</param>
        /// <returns>A successful result.</returns>
        /// <exception cref="System.ArgumentNullException">Thrown when node is null.</exception>
        public static TryGetNodeResult Found(IHnswNode node)
        {
            System.ArgumentNullException.ThrowIfNull(node, nameof(node));
            return new TryGetNodeResult(true, node);
        }

        /// <summary>
        /// Creates a failed result indicating the node was not found.
        /// </summary>
        /// <returns>A failed result.</returns>
        public static TryGetNodeResult NotFound()
        {
            return new TryGetNodeResult(false, null);
        }

        #endregion
        
        #region Public-Methods
        
        #endregion
        
        #region Private-Methods
        
        #endregion
    }
}