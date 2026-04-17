namespace HnswIndex.Server.Classes
{
    using System.Collections.Generic;

    /// <summary>
    /// Request to search for nearest neighbors.
    /// </summary>
    /// <remarks>
    /// When <see cref="Labels"/> or <see cref="Tags"/> are supplied the server performs
    /// an HNSW top-K search first and then drops any result whose vector metadata does
    /// not satisfy the filter. Because filtering is applied after graph traversal, the
    /// response may contain fewer than <see cref="K"/> results — see
    /// <see cref="SearchResponse.FilteredCount"/> for the number of candidates that were
    /// dropped by the filter.
    /// </remarks>
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

        /// <summary>
        /// Optional label filter. A result is kept only when <b>every</b> label in this list
        /// is present on the vector's <c>Labels</c> collection (AND semantics).
        /// Comparison is case-sensitive unless <see cref="CaseInsensitive"/> is true.
        /// Null or empty disables label filtering.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Optional tag filter. A result is kept only when <b>every</b> key in this dictionary
        /// exists on the vector's <c>Tags</c> and its stringified value equals the filter value
        /// (AND semantics). Tag values stored as non-string types are compared via
        /// <c>Convert.ToString(value, InvariantCulture)</c>. Comparison is case-sensitive unless
        /// <see cref="CaseInsensitive"/> is true. Null or empty disables tag filtering.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        /// <summary>
        /// When true, label and tag comparisons (both keys and values) use
        /// <c>StringComparison.OrdinalIgnoreCase</c>. Default is false (exact match).
        /// </summary>
        public bool CaseInsensitive { get; set; } = false;

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
