namespace HnswLite.Sdk.Models
{
    using System;

    /// <summary>
    /// Represents an HNSW index as returned by the server.
    /// </summary>
    public class IndexResponse
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier of the index.
        /// </summary>
        public Guid GUID { get; set; } = Guid.Empty;

        /// <summary>
        /// Name of the index.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Dimensionality of vectors stored in this index.
        /// </summary>
        public int Dimension { get; set; } = 0;

        /// <summary>
        /// Storage backend type. One of "RAM" or "SQLite".
        /// </summary>
        public string StorageType { get; set; } = "RAM";

        /// <summary>
        /// Distance function used for similarity. One of "Euclidean", "Cosine", or "DotProduct".
        /// </summary>
        public string DistanceFunction { get; set; } = "Euclidean";

        /// <summary>
        /// Number of bi-directional connections per node per layer.
        /// </summary>
        public int M { get; set; } = 16;

        /// <summary>
        /// Maximum number of connections per node on the zeroth layer.
        /// </summary>
        public int MaxM { get; set; } = 32;

        /// <summary>
        /// Size of the dynamic candidate list during index construction.
        /// </summary>
        public int EfConstruction { get; set; } = 200;

        /// <summary>
        /// Number of vectors currently stored in the index.
        /// </summary>
        public int VectorCount { get; set; } = 0;

        /// <summary>
        /// UTC timestamp when the index was created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
