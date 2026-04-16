namespace HnswLite.Sdk.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Request body for a K-nearest-neighbour search.
    /// </summary>
    public class SearchRequest
    {
        #region Public-Members

        /// <summary>
        /// The query vector. Length must match the index dimension.
        /// </summary>
        public List<float> Vector { get; set; } = new List<float>();

        /// <summary>
        /// Number of nearest neighbours to return. Default is 10.
        /// </summary>
        public int K { get; set; } = 10;

        /// <summary>
        /// Search exploration factor. Higher values yield better recall at the cost of speed. Null uses the server default.
        /// </summary>
        public int? Ef { get; set; } = null;

        #endregion
    }
}
