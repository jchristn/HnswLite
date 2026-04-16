namespace HnswLite.Sdk.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Represents a single vector entry as returned by the server.
    /// </summary>
    public class VectorEntryResponse
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier of the vector.
        /// </summary>
        public Guid GUID { get; set; } = Guid.Empty;

        /// <summary>
        /// The vector values. Null when the enumeration request did not request vector values (includeVectors=false).
        /// Always populated for single-vector GET responses.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<float>? Vector { get; set; } = null;

        #endregion
    }
}
