namespace HnswIndex.Server.Classes
{
    /// <summary>
    /// Response containing index information.
    /// </summary>
    public class IndexResponse
    {
        #region Public-Members

        /// <summary>
        /// Index GUID.
        /// </summary>
        public Guid GUID { get; set; }

        /// <summary>
        /// Index name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Vector dimension.
        /// </summary>
        public int Dimension { get; set; } = 0;

        /// <summary>
        /// Storage type.
        /// </summary>
        public string StorageType { get; set; } = string.Empty;

        /// <summary>
        /// Distance function type.
        /// </summary>
        public string DistanceFunction { get; set; } = string.Empty;

        /// <summary>
        /// Number of connections per layer.
        /// </summary>
        public int M { get; set; } = 0;

        /// <summary>
        /// Maximum number of connections per layer.
        /// </summary>
        public int MaxM { get; set; } = 0;

        /// <summary>
        /// Construction time search depth.
        /// </summary>
        public int EfConstruction { get; set; } = 0;

        /// <summary>
        /// Number of vectors in the index.
        /// </summary>
        public int VectorCount { get; set; } = 0;

        /// <summary>
        /// Timestamp when the index was created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the IndexResponse class.
        /// </summary>
        public IndexResponse()
        {
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}