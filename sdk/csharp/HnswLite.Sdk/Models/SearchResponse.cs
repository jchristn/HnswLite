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

        #endregion
    }
}
