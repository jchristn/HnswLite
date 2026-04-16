namespace HnswLite.Sdk.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Request body for adding a batch of vectors to an index.
    /// </summary>
    public class AddVectorsRequest
    {
        #region Public-Members

        /// <summary>
        /// List of vectors to add. Each vector length must match the index dimension.
        /// </summary>
        public List<AddVectorRequest> Vectors { get; set; } = new List<AddVectorRequest>();

        #endregion
    }
}
