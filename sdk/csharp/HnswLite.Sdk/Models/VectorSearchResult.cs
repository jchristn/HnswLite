namespace HnswLite.Sdk.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A single nearest-neighbour search result.
    /// </summary>
    public class VectorSearchResult
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier of the vector.
        /// </summary>
        public Guid GUID { get; set; } = Guid.Empty;

        /// <summary>
        /// The vector data.
        /// </summary>
        public List<float> Vector { get; set; } = new List<float>();

        /// <summary>
        /// Distance from the query vector, according to the index distance function.
        /// </summary>
        public float Distance { get; set; } = 0;

        #endregion
    }
}
