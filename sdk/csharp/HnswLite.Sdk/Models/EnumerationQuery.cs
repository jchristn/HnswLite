namespace HnswLite.Sdk.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Query parameters for paginated enumeration endpoints.
    /// </summary>
    public class EnumerationQuery
    {
        #region Public-Members

        /// <summary>
        /// Maximum number of results to return per page. Clamped to [1, 1000] server-side. Default is 100.
        /// </summary>
        public int MaxResults { get; set; } = 100;

        /// <summary>
        /// Number of records to skip. Must be greater than or equal to zero.
        /// </summary>
        public int Skip { get; set; } = 0;

        /// <summary>
        /// Continuation token from a previous page. Mutually exclusive with Skip greater than zero.
        /// </summary>
        public Guid? ContinuationToken { get; set; } = null;

        /// <summary>
        /// Ordering of results.
        /// </summary>
        public EnumerationOrderEnum Ordering { get; set; } = EnumerationOrderEnum.CreatedDescending;

        /// <summary>
        /// Case-insensitive prefix filter on the record name.
        /// </summary>
        public string? Prefix { get; set; } = null;

        /// <summary>
        /// Case-insensitive suffix filter on the record name.
        /// </summary>
        public string? Suffix { get; set; } = null;

        /// <summary>
        /// Keep only records created strictly after this UTC timestamp.
        /// </summary>
        public DateTime? CreatedAfterUtc { get; set; } = null;

        /// <summary>
        /// Keep only records created strictly before this UTC timestamp.
        /// </summary>
        public DateTime? CreatedBeforeUtc { get; set; } = null;

        /// <summary>
        /// Optional label filter (AND semantics). A record is kept only when <b>every</b>
        /// label in this list is present on the record's <c>Labels</c> collection. Null or
        /// empty disables label filtering. Comparison is case-sensitive unless
        /// <see cref="CaseInsensitive"/> is true.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Optional tag filter (AND semantics). A record is kept only when <b>every</b> key
        /// in this dictionary exists on the record's <c>Tags</c> and its stringified value
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
