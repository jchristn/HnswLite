namespace HnswIndex.Server.Classes
{
    /// <summary>
    /// Server configuration settings.
    /// </summary>
    public class ServerSettings
    {
        #region Public-Members

        /// <summary>
        /// Hostname or IP address on which to listen.
        /// </summary>
        public string Hostname
        {
            get { return _Hostname; }
            set { _Hostname = value ?? throw new ArgumentNullException(nameof(value)); }
        }

        /// <summary>
        /// TCP port on which to listen.
        /// </summary>
        public int Port { get; set; } = 8080;

        /// <summary>
        /// Enable or disable SSL.
        /// </summary>
        public bool Ssl { get; set; } = false;

        /// <summary>
        /// Header to check for administrative token.
        /// </summary>
        public string AdminApiKeyHeader
        {
            get { return _AdminApiKeyHeader; }
            set { _AdminApiKeyHeader = value ?? throw new ArgumentNullException(nameof(value)); }
        }

        /// <summary>
        /// Administrative API key.
        /// </summary>
        public string AdminApiKey
        {
            get { return _AdminApiKey; }
            set { _AdminApiKey = value ?? throw new ArgumentNullException(nameof(value)); }
        }

        /// <summary>
        /// Enable or disable authentication.
        /// </summary>
        public bool RequireAuthentication { get; set; } = true;

        #endregion

        #region Private-Members

        private string _Hostname = "localhost";
        private string _AdminApiKeyHeader = "x-api-key";
        private string _AdminApiKey = Guid.NewGuid().ToString();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the ServerSettings class.
        /// </summary>
        public ServerSettings()
        {
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}