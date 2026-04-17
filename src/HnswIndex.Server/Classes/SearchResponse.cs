namespace HnswIndex.Server.Classes
{
    /// <summary>
    /// Response containing search results.
    /// </summary>
    public class SearchResponse
    {
        #region Public-Members

        /// <summary>
        /// List of search results.
        /// </summary>
        public List<VectorSearchResult> Results { get; set; } = new List<VectorSearchResult>();

        /// <summary>
        /// Search time in milliseconds.
        /// </summary>
        public double SearchTimeMs { get; set; } = 0;

        /// <summary>
        /// Number of HNSW candidates that were dropped by the metadata filter
        /// (<see cref="SearchRequest.Labels"/> or <see cref="SearchRequest.Tags"/>).
        /// Zero when no filter was supplied or when no candidates were filtered.
        /// When a filter is supplied, <c>Results.Count + FilteredCount</c> equals the
        /// number of HNSW candidates considered (at most <see cref="SearchRequest.K"/>).
        /// </summary>
        public int FilteredCount { get; set; } = 0;

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the SearchResponse class.
        /// </summary>
        public SearchResponse()
        {
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}