namespace HnswIndex.Server.Classes
{
    /// <summary>
    /// Request to add a vector to an index.
    /// </summary>
    public class AddVectorRequest
    {
        #region Public-Members

        /// <summary>
        /// Vector GUID. Auto-generated if not provided.
        /// </summary>
        public Guid GUID { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Vector data.
        /// </summary>
        public List<float> Vector { get; set; } = new List<float>();

        /// <summary>
        /// Optional human-readable name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Optional classification labels.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Optional arbitrary key/value tags.
        /// </summary>
        public Dictionary<string, object>? Tags { get; set; } = null;

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the AddVectorRequest class.
        /// </summary>
        public AddVectorRequest()
        {
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}