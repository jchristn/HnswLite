namespace HnswLite.Sdk.Models
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Ordering options for enumeration queries.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum EnumerationOrderEnum
    {
        /// <summary>
        /// Order by creation date ascending.
        /// </summary>
        CreatedAscending,

        /// <summary>
        /// Order by creation date descending.
        /// </summary>
        CreatedDescending,

        /// <summary>
        /// Order by name ascending.
        /// </summary>
        NameAscending,

        /// <summary>
        /// Order by name descending.
        /// </summary>
        NameDescending
    }
}
