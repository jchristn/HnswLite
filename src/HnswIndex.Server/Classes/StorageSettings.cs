namespace HnswIndex.Server.Classes
{
    /// <summary>
    /// Storage configuration settings.
    /// </summary>
    public class StorageSettings
    {
        #region Public-Members

        /// <summary>
        /// Directory path for SQLite index storage.
        /// </summary>
        public string SqliteDirectory
        {
            get { return _SqliteDirectory; }
            set { _SqliteDirectory = value ?? throw new ArgumentNullException(nameof(value)); }
        }

        #endregion

        #region Private-Members

        private string _SqliteDirectory = "./data/indexes/";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the StorageSettings class.
        /// </summary>
        public StorageSettings()
        {
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}