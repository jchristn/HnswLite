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