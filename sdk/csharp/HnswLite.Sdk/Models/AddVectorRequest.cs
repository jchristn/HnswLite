namespace HnswLite.Sdk.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Request body for adding a single vector to an index.
    /// </summary>
    public class AddVectorRequest
    {
        #region Public-Members

        /// <summary>
        /// Optional GUID for the vector. If not supplied, the server generates one.
        /// </summary>
        public Guid? GUID { get; set; } = null;

        /// <summary>
        /// The vector data. Length must match the index dimension.
        /// </summary>
        public List<float> Vector { get; set; } = new List<float>();

        /// <summary>
        /// Optional human-readable name for the vector. May be null.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Optional classification labels. May be null or empty.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Optional arbitrary key/value tags. May be null or empty.
        /// </summary>
        public Dictionary<string, object>? Tags { get; set; } = null;

        #endregion
    }
}
