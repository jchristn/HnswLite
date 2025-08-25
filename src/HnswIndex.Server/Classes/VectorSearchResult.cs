namespace HnswIndex.Server.Classes
{
    /// <summary>
    /// A single vector search result.
    /// </summary>
    public class VectorSearchResult
    {
        #region Public-Members

        /// <summary>
        /// Vector GUID.
        /// </summary>
        public Guid GUID { get; set; }

        /// <summary>
        /// Vector data.
        /// </summary>
        public List<float> Vector { get; set; } = new List<float>();

        /// <summary>
        /// Distance from query vector.
        /// </summary>
        public float Distance { get; set; } = 0.0f;

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the VectorSearchResult class.
        /// </summary>
        public VectorSearchResult()
        {
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}