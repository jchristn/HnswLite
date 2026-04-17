namespace HnswIndex.Server.Classes
{
    using System;
    using System.Collections.Generic;
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

        /// <summary>
        /// Optional label filter. A record is kept only when <b>every</b> label in this list
        /// is present on the record's <c>Labels</c> collection (AND semantics).
        /// Comparison is case-sensitive unless <see cref="CaseInsensitive"/> is true.
        /// Null or empty disables label filtering.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Optional tag filter. A record is kept only when <b>every</b> key in this dictionary
        /// exists on the record's <c>Tags</c> and its stringified value equals the filter value
        /// (AND semantics). Comparison is case-sensitive unless <see cref="CaseInsensitive"/>
        /// is true. Null or empty disables tag filtering.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        /// <summary>
        /// When true, label and tag comparisons (both keys and values) use
        /// <c>StringComparison.OrdinalIgnoreCase</c>. Default is false (exact match).
        /// </summary>
        public bool CaseInsensitive { get; set; } = false;

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

            string? labels = query["labels"] ?? query["Labels"];
            if (labels != null)
            {
                List<string> parsedLabels = new List<string>();
                foreach (string segment in labels.Split(','))
                {
                    string trimmed = segment.Trim();
                    if (trimmed.Length > 0) parsedLabels.Add(trimmed);
                }
                if (parsedLabels.Count > 0) result.Labels = parsedLabels;
            }

            string? tags = query["tags"] ?? query["Tags"];
            if (tags != null)
            {
                Dictionary<string, string> parsedTags = new Dictionary<string, string>();
                foreach (string segment in tags.Split(','))
                {
                    string trimmed = segment.Trim();
                    if (trimmed.Length == 0) continue;
                    int colon = trimmed.IndexOf(':');
                    if (colon <= 0)
                        throw new ArgumentException("tags must be a comma-separated list of key:value pairs.");
                    string key = trimmed.Substring(0, colon).Trim();
                    string value = trimmed.Substring(colon + 1).Trim();
                    if (key.Length == 0)
                        throw new ArgumentException("tag keys must be non-empty.");
                    parsedTags[key] = value;
                }
                if (parsedTags.Count > 0) result.Tags = parsedTags;
            }

            string? ci = query["caseInsensitive"] ?? query["CaseInsensitive"];
            if (!string.IsNullOrEmpty(ci))
            {
                if (string.Equals(ci, "true", StringComparison.OrdinalIgnoreCase) || ci == "1")
                    result.CaseInsensitive = true;
                else if (string.Equals(ci, "false", StringComparison.OrdinalIgnoreCase) || ci == "0")
                    result.CaseInsensitive = false;
                else
                    throw new ArgumentException("caseInsensitive must be true, false, 1, or 0.");
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
