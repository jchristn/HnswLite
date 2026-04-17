namespace HnswLite.Sdk.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Request body for a K-nearest-neighbour search.
    /// </summary>
    /// <remarks>
    /// When <see cref="Labels"/> or <see cref="Tags"/> are supplied, the server performs
    /// the HNSW top-K search first and then drops any result whose vector metadata does
    /// not satisfy the filter. The response may therefore contain fewer than <see cref="K"/>
    /// results — see <see cref="SearchResponse.FilteredCount"/> for the number dropped.
    /// </remarks>
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

        /// <summary>
        /// Optional label filter (AND semantics). A result is kept only when <b>every</b> label
        /// in this list is present on the vector's <c>Labels</c> collection. Null or empty
        /// disables label filtering. Comparison is case-sensitive unless
        /// <see cref="CaseInsensitive"/> is true.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Optional tag filter (AND semantics). A result is kept only when <b>every</b> key
        /// in this dictionary exists on the vector's <c>Tags</c> and its stringified value
        /// equals the filter value. Null or empty disables tag filtering. Comparison is
        /// case-sensitive unless <see cref="CaseInsensitive"/> is true.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        /// <summary>
        /// When true, <see cref="Labels"/> and <see cref="Tags"/> comparisons are
        /// case-insensitive. Default is false (exact match).
        /// </summary>
        public bool CaseInsensitive { get; set; } = false;

        #endregion
    }
}
