namespace HnswLite.Sdk.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Paginated enumeration result returned by collection endpoints.
    /// </summary>
    /// <typeparam name="T">The type of objects in the result set.</typeparam>
    public class EnumerationResult<T>
    {
        #region Public-Members

        /// <summary>
        /// Indicates whether the request was successful.
        /// </summary>
        public bool Success { get; set; } = true;

        /// <summary>
        /// Maximum number of results requested per page.
        /// </summary>
        public int MaxResults { get; set; } = 100;

        /// <summary>
        /// Number of records skipped.
        /// </summary>
        public int Skip { get; set; } = 0;

        /// <summary>
        /// Continuation token for cursor-based paging. Null when not applicable.
        /// </summary>
        public Guid? ContinuationToken { get; set; } = null;

        /// <summary>
        /// True if this is the last page of results.
        /// </summary>
        public bool EndOfResults { get; set; } = true;

        /// <summary>
        /// Total number of records matching the filter criteria.
        /// </summary>
        public long TotalRecords { get; set; } = 0;

        /// <summary>
        /// Number of records remaining after this page.
        /// </summary>
        public long RecordsRemaining { get; set; } = 0;

        /// <summary>
        /// Number of records dropped by the server-side metadata filter
        /// (<c>Labels</c> or <c>Tags</c>). Zero when no metadata filter was supplied.
        /// Independent of the other filters (Prefix/Suffix/CreatedAfter/Before).
        /// </summary>
        public long FilteredCount { get; set; } = 0;

        /// <summary>
        /// Server-side UTC timestamp of the response.
        /// </summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The objects in the current page.
        /// </summary>
        public List<T> Objects { get; set; } = new List<T>();

        #endregion
    }
}
