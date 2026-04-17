namespace HnswIndex.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Hnsw;

    /// <summary>
    /// Shared predicate used by search and enumeration to drop nodes whose metadata
    /// does not satisfy a caller-supplied <c>Labels</c> / <c>Tags</c> filter.
    /// </summary>
    /// <remarks>
    /// Semantics:
    /// <list type="bullet">
    ///   <item><description><b>Labels</b>: AND — every label in the filter must be present on the node.</description></item>
    ///   <item><description><b>Tags</b>: AND — every key in the filter must exist on the node and its stringified value must equal the filter value.</description></item>
    ///   <item><description>A null or empty filter matches every node (no-op).</description></item>
    ///   <item><description>Tag values on the node are compared using <c>Convert.ToString(value, InvariantCulture)</c> so non-string types (numbers, bools) compare predictably.</description></item>
    /// </list>
    /// </remarks>
    public static class MetadataFilter
    {
        #region Public-Members

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        #endregion

        #region Public-Methods

        /// <summary>
        /// Return true when <paramref name="node"/> satisfies the supplied filter.
        /// </summary>
        /// <param name="node">The node to test; may be null (a null node never matches a non-empty filter).</param>
        /// <param name="labels">Required labels (AND). Null or empty skips label checking.</param>
        /// <param name="tags">Required tag key/value pairs (AND). Null or empty skips tag checking.</param>
        /// <param name="caseInsensitive">When true, string comparisons use <c>OrdinalIgnoreCase</c>.</param>
        /// <returns>True if the node passes all supplied filter predicates; otherwise false.</returns>
        public static bool Matches(
            IHnswNode? node,
            IReadOnlyCollection<string>? labels,
            IReadOnlyDictionary<string, string>? tags,
            bool caseInsensitive)
        {
            bool hasLabelFilter = labels != null && labels.Count > 0;
            bool hasTagFilter = tags != null && tags.Count > 0;

            if (!hasLabelFilter && !hasTagFilter) return true;
            if (node == null) return false;

            StringComparison cmp = caseInsensitive
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            StringComparer cmpEq = caseInsensitive
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

            if (hasLabelFilter)
            {
                if (node.Labels == null || node.Labels.Count == 0) return false;
                foreach (string required in labels!)
                {
                    bool found = false;
                    foreach (string candidate in node.Labels)
                    {
                        if (candidate != null && string.Equals(candidate, required, cmp))
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found) return false;
                }
            }

            if (hasTagFilter)
            {
                if (node.Tags == null || node.Tags.Count == 0) return false;

                // Build a case-appropriate lookup from the node's tags.
                Dictionary<string, object?> nodeTags = new Dictionary<string, object?>(cmpEq);
                foreach (KeyValuePair<string, object> entry in node.Tags)
                {
                    if (entry.Key == null) continue;
                    nodeTags[entry.Key] = entry.Value;
                }

                foreach (KeyValuePair<string, string> required in tags!)
                {
                    if (!nodeTags.TryGetValue(required.Key, out object? actual)) return false;
                    string actualStr = actual == null
                        ? string.Empty
                        : Convert.ToString(actual, CultureInfo.InvariantCulture) ?? string.Empty;
                    if (!string.Equals(actualStr, required.Value, cmp)) return false;
                }
            }

            return true;
        }

        #endregion

        #region Private-Methods

        #endregion
    }
}
