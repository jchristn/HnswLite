namespace HnswIndex.Server.Classes
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Response shape for a single vector record returned by the vector enumeration endpoint.
    /// </summary>
    public class VectorEntryResponse
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier of the vector.
        /// </summary>
        public Guid GUID { get; set; }

        /// <summary>
        /// Vector data. Omitted from the JSON payload when the caller opts out of
        /// including vector values (via <c>includeVectors=false</c>) — useful for
        /// listings that only need identifiers.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<float>? Vector { get; set; } = null;

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the object.
        /// </summary>
        public VectorEntryResponse()
        {
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}
