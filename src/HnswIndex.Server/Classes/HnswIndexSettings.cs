namespace HnswIndex.Server.Classes
{
    /// <summary>
    /// Settings for HnswIndex server configuration.
    /// </summary>
    public class HnswIndexSettings
    {
        #region Public-Members

        /// <summary>
        /// Server settings.
        /// </summary>
        public ServerSettings Server
        {
            get { return _Server; }
            set { _Server = value ?? throw new ArgumentNullException(nameof(value)); }
        }

        /// <summary>
        /// Debug settings.
        /// </summary>
        public DebugSettings Debug
        {
            get { return _Debug; }
            set { _Debug = value ?? throw new ArgumentNullException(nameof(value)); }
        }

        /// <summary>
        /// Logging settings.
        /// </summary>
        public LoggingSettings Logging
        {
            get { return _Logging; }
            set { _Logging = value ?? throw new ArgumentNullException(nameof(value)); }
        }

        /// <summary>
        /// Authentication settings.
        /// </summary>
        public AuthenticationSettings Authentication
        {
            get { return _Authentication; }
            set { _Authentication = value ?? throw new ArgumentNullException(nameof(value)); }
        }

        /// <summary>
        /// Storage settings.
        /// </summary>
        public StorageSettings Storage
        {
            get { return _Storage; }
            set { _Storage = value ?? throw new ArgumentNullException(nameof(value)); }
        }

        #endregion

        #region Private-Members

        private ServerSettings _Server = new ServerSettings();
        private DebugSettings _Debug = new DebugSettings();
        private LoggingSettings _Logging = new LoggingSettings();
        private AuthenticationSettings _Authentication = new AuthenticationSettings();
        private StorageSettings _Storage = new StorageSettings();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the HnswIndexSettings class.
        /// </summary>
        public HnswIndexSettings()
        {
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}