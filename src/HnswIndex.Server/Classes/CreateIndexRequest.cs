namespace HnswIndex.Server.Classes
{
    /// <summary>
    /// Request to create a new HNSW index.
    /// </summary>
    public class CreateIndexRequest
    {
        #region Public-Members

        /// <summary>
        /// Index name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Vector dimension.
        /// </summary>
        public int Dimension { get; set; } = 0;

        /// <summary>
        /// Storage type (RAM or SQLite).
        /// </summary>
        public string StorageType { get; set; } = "RAM";

        /// <summary>
        /// Distance function type.
        /// </summary>
        public string DistanceFunction { get; set; } = "Euclidean";

        /// <summary>
        /// Number of connections per layer (M parameter).
        /// </summary>
        public int M { get; set; } = 16;

        /// <summary>
        /// Maximum number of connections per layer (MaxM parameter).
        /// </summary>
        public int MaxM { get; set; } = 32;

        /// <summary>
        /// Construction time search depth (EfConstruction parameter).
        /// </summary>
        public int EfConstruction { get; set; } = 200;

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the CreateIndexRequest class.
        /// </summary>
        public CreateIndexRequest()
        {
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}