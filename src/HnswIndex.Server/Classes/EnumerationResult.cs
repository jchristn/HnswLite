namespace HnswIndex.Server.Classes
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Paginated enumeration response. Returned from every GET API that enumerates a collection.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    public class EnumerationResult<T>
    {
        #region Public-Members

        /// <summary>
        /// True when the operation succeeded. Check this before using Objects.
        /// </summary>
        [JsonPropertyOrder(0)]
        public bool Success { get; set; } = true;

        /// <summary>
        /// Echo of the requested <see cref="EnumerationQuery.MaxResults"/>.
        /// </summary>
        [JsonPropertyOrder(1)]
        public int MaxResults { get; set; } = 100;

        /// <summary>
        /// Echo of the requested <see cref="EnumerationQuery.Skip"/>.
        /// </summary>
        [JsonPropertyOrder(2)]
        public int Skip { get; set; } = 0;

        /// <summary>
        /// Cursor token for the next page. Present only when the backing store supports cursors
        /// and the page is not the last. Null when <see cref="EndOfResults"/> is true.
        /// </summary>
        [JsonPropertyOrder(3)]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Guid? ContinuationToken { get; set; } = null;

        /// <summary>
        /// True when there are no records after this page.
        /// </summary>
        [JsonPropertyOrder(4)]
        public bool EndOfResults { get; set; } = true;

        /// <summary>
        /// Total count of records matching the query (before pagination).
        /// </summary>
        [JsonPropertyOrder(5)]
        public long TotalRecords { get; set; } = 0;

        /// <summary>
        /// Number of records remaining after this page.
        /// </summary>
        [JsonPropertyOrder(6)]
        public long RecordsRemaining { get; set; } = 0;

        /// <summary>
        /// Timestamp (UTC) when this response was generated.
        /// </summary>
        [JsonPropertyOrder(7)]
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The page of enumerated objects.
        /// </summary>
        [JsonPropertyOrder(999)]
        public List<T> Objects { get; set; } = new List<T>();

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty EnumerationResult.
        /// </summary>
        public EnumerationResult()
        {
        }

        /// <summary>
        /// Build an EnumerationResult for a given query and full result set of matches.
        /// Pagination (skip / maxResults) is applied here.
        /// </summary>
        /// <param name="query">The query that produced these records.</param>
        /// <param name="allMatches">All records that match the query's filters, pre-pagination and pre-sort.</param>
        /// <returns>A populated EnumerationResult paged according to the query.</returns>
        public static EnumerationResult<T> FromQuery(EnumerationQuery query, List<T> allMatches)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (allMatches == null) allMatches = new List<T>();

            int skip = Math.Min(query.Skip, allMatches.Count);
            int take = Math.Min(query.MaxResults, Math.Max(0, allMatches.Count - skip));
            List<T> page = allMatches.GetRange(skip, take);

            long remaining = Math.Max(0, (long)allMatches.Count - skip - take);

            return new EnumerationResult<T>
            {
                Success = true,
                MaxResults = query.MaxResults,
                Skip = skip,
                TotalRecords = allMatches.Count,
                RecordsRemaining = remaining,
                EndOfResults = remaining == 0,
                ContinuationToken = null,
                TimestampUtc = DateTime.UtcNow,
                Objects = page
            };
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}
