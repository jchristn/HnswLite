namespace HnswIndex.Server.Classes
{
    using System;
    using System.Collections.Specialized;
    using System.Globalization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Query parameters for paginated, filtered enumeration requests.
    /// Populated from the request query string by <see cref="FromQueryString"/>.
    /// </summary>
    public class EnumerationQuery
    {
        #region Public-Members

        /// <summary>
        /// Maximum number of results to return. Clamped to 1–1000. Default: 100.
        /// </summary>
        public int MaxResults
        {
            get { return _MaxResults; }
            set
            {
                if (value < 1) _MaxResults = 1;
                else if (value > 1000) _MaxResults = 1000;
                else _MaxResults = value;
            }
        }

        /// <summary>
        /// Number of matching records to skip (offset pagination). Must be &gt;= 0. Default: 0.
        /// </summary>
        public int Skip
        {
            get { return _Skip; }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(Skip), "Skip must be >= 0.");
                _Skip = value;
            }
        }

        /// <summary>
        /// Optional cursor token (GUID of the last record from the previous page).
        /// Mutually exclusive with <see cref="Skip"/> &gt; 0. Not every collection supports
        /// cursor pagination — callers should use <see cref="Skip"/> when unsure.
        /// </summary>
        public Guid? ContinuationToken { get; set; } = null;

        /// <summary>
        /// Sort ordering for the result set. Default: <see cref="EnumerationOrderEnum.CreatedDescending"/>.
        /// </summary>
        public EnumerationOrderEnum Ordering { get; set; } = EnumerationOrderEnum.CreatedDescending;

        /// <summary>
        /// Filter: keep only records whose name starts with this prefix (case-insensitive).
        /// </summary>
        public string? Prefix { get; set; } = null;

        /// <summary>
        /// Filter: keep only records whose name ends with this suffix (case-insensitive).
        /// </summary>
        public string? Suffix { get; set; } = null;

        /// <summary>
        /// Filter: keep only records with CreatedUtc &gt; this timestamp.
        /// </summary>
        public DateTime? CreatedAfterUtc { get; set; } = null;

        /// <summary>
        /// Filter: keep only records with CreatedUtc &lt; this timestamp.
        /// </summary>
        public DateTime? CreatedBeforeUtc { get; set; } = null;

        #endregion

        #region Private-Members

        private int _MaxResults = 100;
        private int _Skip = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a default EnumerationQuery (MaxResults=100, Skip=0, CreatedDescending).
        /// </summary>
        public EnumerationQuery()
        {
        }

        /// <summary>
        /// Parse an EnumerationQuery from the request query-string name/value pairs.
        /// All parameter names are matched case-insensitively.
        /// </summary>
        /// <param name="query">The query-string collection. Null is treated as empty.</param>
        /// <returns>A populated EnumerationQuery.</returns>
        /// <exception cref="ArgumentException">A parameter has an unparseable value.</exception>
        public static EnumerationQuery FromQueryString(NameValueCollection? query)
        {
            EnumerationQuery result = new EnumerationQuery();
            if (query == null) return result;

            string? max = query["maxResults"] ?? query["MaxResults"] ?? query["max"] ?? query["limit"];
            if (!string.IsNullOrEmpty(max))
            {
                if (!int.TryParse(max, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                    throw new ArgumentException("maxResults must be an integer.");
                result.MaxResults = parsed;
            }

            string? skip = query["skip"] ?? query["Skip"] ?? query["offset"];
            if (!string.IsNullOrEmpty(skip))
            {
                if (!int.TryParse(skip, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                    throw new ArgumentException("skip must be an integer.");
                result.Skip = parsed;
            }

            string? cont = query["continuationToken"] ?? query["ContinuationToken"] ?? query["token"];
            if (!string.IsNullOrEmpty(cont))
            {
                if (!Guid.TryParse(cont, out Guid guid))
                    throw new ArgumentException("continuationToken must be a GUID.");
                result.ContinuationToken = guid;
            }

            string? order = query["ordering"] ?? query["Ordering"] ?? query["order"];
            if (!string.IsNullOrEmpty(order))
            {
                if (!Enum.TryParse<EnumerationOrderEnum>(order, ignoreCase: true, out EnumerationOrderEnum parsed))
                {
                    throw new ArgumentException("ordering must be one of: "
                        + "CreatedAscending, CreatedDescending, NameAscending, NameDescending.");
                }
                result.Ordering = parsed;
            }

            string? prefix = query["prefix"] ?? query["Prefix"];
            if (!string.IsNullOrEmpty(prefix)) result.Prefix = prefix;

            string? suffix = query["suffix"] ?? query["Suffix"];
            if (!string.IsNullOrEmpty(suffix)) result.Suffix = suffix;

            string? after = query["createdAfterUtc"] ?? query["CreatedAfterUtc"] ?? query["after"];
            if (!string.IsNullOrEmpty(after))
            {
                if (!DateTime.TryParse(after, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime parsed))
                    throw new ArgumentException("createdAfterUtc must be an ISO-8601 timestamp.");
                result.CreatedAfterUtc = parsed;
            }

            string? before = query["createdBeforeUtc"] ?? query["CreatedBeforeUtc"] ?? query["before"];
            if (!string.IsNullOrEmpty(before))
            {
                if (!DateTime.TryParse(before, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime parsed))
                    throw new ArgumentException("createdBeforeUtc must be an ISO-8601 timestamp.");
                result.CreatedBeforeUtc = parsed;
            }

            return result;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate the query's internal consistency.
        /// </summary>
        /// <param name="errorMessage">Populated with a diagnostic when validation fails.</param>
        /// <returns>True when the query is valid; false otherwise.</returns>
        public bool Validate(out string? errorMessage)
        {
            errorMessage = null;

            if (ContinuationToken.HasValue && Skip > 0)
            {
                errorMessage = "ContinuationToken and Skip cannot both be specified.";
                return false;
            }

            if (CreatedAfterUtc.HasValue && CreatedBeforeUtc.HasValue
                && CreatedAfterUtc.Value >= CreatedBeforeUtc.Value)
            {
                errorMessage = "CreatedAfterUtc must be strictly before CreatedBeforeUtc.";
                return false;
            }

            return true;
        }

        #endregion

        #region Private-Methods

        #endregion
    }
}
