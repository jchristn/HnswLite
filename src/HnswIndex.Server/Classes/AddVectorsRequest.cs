namespace HnswIndex.Server.Classes
{
    /// <summary>
    /// Request to add multiple vectors to an index.
    /// </summary>
    public class AddVectorsRequest
    {
        #region Public-Members

        /// <summary>
        /// List of vectors to add.
        /// </summary>
        public List<AddVectorRequest> Vectors { get; set; } = new List<AddVectorRequest>();

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the AddVectorsRequest class.
        /// </summary>
        public AddVectorsRequest()
        {
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}