namespace HnswIndex.Server.Classes
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Sort orderings supported by enumeration queries.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum EnumerationOrderEnum
    {
        /// <summary>
        /// Ascending by CreatedUtc (oldest first).
        /// </summary>
        CreatedAscending,

        /// <summary>
        /// Descending by CreatedUtc (newest first). Default.
        /// </summary>
        CreatedDescending,

        /// <summary>
        /// Ascending by name (A–Z).
        /// </summary>
        NameAscending,

        /// <summary>
        /// Descending by name (Z–A).
        /// </summary>
        NameDescending
    }
}
