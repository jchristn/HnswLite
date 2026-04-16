namespace HnswLite.Sdk.Models
{
    /// <summary>
    /// Request body for creating a new HNSW index.
    /// </summary>
    public class CreateIndexRequest
    {
        #region Public-Members

        /// <summary>
        /// Name of the index to create. Must be unique.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Dimensionality of vectors. Must be greater than zero.
        /// </summary>
        public int Dimension { get; set; } = 0;

        /// <summary>
        /// Storage backend type. One of "RAM" or "SQLite".
        /// </summary>
        public string StorageType { get; set; } = "RAM";

        /// <summary>
        /// Distance function. One of "Euclidean", "Cosine", or "DotProduct".
        /// </summary>
        public string DistanceFunction { get; set; } = "Euclidean";

        /// <summary>
        /// Number of bi-directional connections per node per layer. Default is 16.
        /// </summary>
        public int M { get; set; } = 16;

        /// <summary>
        /// Maximum number of connections per node on the zeroth layer. Default is 32.
        /// </summary>
        public int MaxM { get; set; } = 32;

        /// <summary>
        /// Size of the dynamic candidate list during construction. Default is 200.
        /// </summary>
        public int EfConstruction { get; set; } = 200;

        #endregion
    }
}
