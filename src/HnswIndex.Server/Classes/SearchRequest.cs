namespace HnswIndex.Server.Classes
{
    /// <summary>
    /// Request to search for nearest neighbors.
    /// </summary>
    public class SearchRequest
    {
        #region Public-Members

        /// <summary>
        /// Query vector.
        /// </summary>
        public List<float> Vector { get; set; } = new List<float>();

        /// <summary>
        /// Number of nearest neighbors to return.
        /// </summary>
        public int K { get; set; } = 10;

        /// <summary>
        /// Search time parameter (ef).
        /// </summary>
        public int? Ef { get; set; } = null;

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the SearchRequest class.
        /// </summary>
        public SearchRequest()
        {
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}