namespace HnswIndex.Server.Classes
{
    /// <summary>
    /// Storage configuration settings.
    /// </summary>
    public class StorageSettings
    {
        #region Public-Members

        /// <summary>
        /// Default storage backend for newly-created indexes when the request omits StorageType.
        /// Valid values: PostgreSQL, SQLite, RAM.
        /// </summary>
        public string DefaultStorageType
        {
            get { return _DefaultStorageType; }
            set { _DefaultStorageType = value ?? throw new ArgumentNullException(nameof(value)); }
        }

        /// <summary>
        /// Directory path for SQLite index storage.
        /// </summary>
        public string SqliteDirectory
        {
            get { return _SqliteDirectory; }
            set { _SqliteDirectory = value ?? throw new ArgumentNullException(nameof(value)); }
        }

        /// <summary>
        /// PostgreSQL connection string used by the server.
        /// </summary>
        public string PostgresqlConnectionString
        {
            get { return _PostgresqlConnectionString; }
            set { _PostgresqlConnectionString = value ?? throw new ArgumentNullException(nameof(value)); }
        }

        /// <summary>
        /// Whether PostgreSQL schema provisioning should be attempted by the provider.
        /// Docker deployments also run an explicit provisioner container.
        /// </summary>
        public bool PostgresqlAutoProvision
        {
            get { return _PostgresqlAutoProvision; }
            set { _PostgresqlAutoProvision = value; }
        }

        #endregion

        #region Private-Members

        private string _DefaultStorageType = "PostgreSQL";
        private string _SqliteDirectory = "./data/indexes/";
        private string _PostgresqlConnectionString = "Host=localhost;Port=5432;Database=hnswlite;Username=hnswlite;Password=hnswlite";
        private bool _PostgresqlAutoProvision = true;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the StorageSettings class.
        /// </summary>
        public StorageSettings()
        {
        }

        #endregion
    }
}
