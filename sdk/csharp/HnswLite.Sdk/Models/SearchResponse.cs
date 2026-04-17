namespace HnswLite.Sdk.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Response body from a K-nearest-neighbour search.
    /// </summary>
    public class SearchResponse
    {
        #region Public-Members

        /// <summary>
        /// The nearest-neighbour results ordered by distance.
        /// </summary>
        public List<VectorSearchResult> Results { get; set; } = new List<VectorSearchResult>();

        /// <summary>
        /// Time taken for the search in milliseconds.
        /// </summary>
        public double SearchTimeMs { get; set; } = 0;

        /// <summary>
        /// Number of HNSW candidates that were dropped by the server-side metadata filter
        /// (<see cref="SearchRequest.Labels"/> or <see cref="SearchRequest.Tags"/>).
        /// Zero when no filter was supplied. When a filter is set,
        /// <c>Results.Count + FilteredCount</c> equals the number of HNSW candidates
        /// considered (at most <see cref="SearchRequest.K"/>).
        /// </summary>
        public int FilteredCount { get; set; } = 0;

        #endregion
    }
}
