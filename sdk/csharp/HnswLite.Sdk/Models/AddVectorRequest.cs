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

        #endregion
    }
}
