namespace HnswIndex.Server.Classes
{
    /// <summary>
    /// CORS (Cross-Origin Resource Sharing) configuration settings.
    /// These values are emitted as response headers on every HTTP response.
    /// </summary>
    public class CorsSettings
    {
        #region Public-Members

        /// <summary>
        /// Enable or disable emission of CORS headers.
        /// </summary>
        public bool Enable { get; set; } = true;

        /// <summary>
        /// Value for the Access-Control-Allow-Origin header.
        /// Use "*" to allow any origin.
        /// </summary>
        public string AllowOrigin
        {
            get { return _AllowOrigin; }
            set { _AllowOrigin = value ?? throw new ArgumentNullException(nameof(value)); }
        }

        /// <summary>
        /// Value for the Access-Control-Allow-Methods header.
        /// </summary>
        public string AllowMethods
        {
            get { return _AllowMethods; }
            set { _AllowMethods = value ?? throw new ArgumentNullException(nameof(value)); }
        }

        /// <summary>
        /// Value for the Access-Control-Allow-Headers header.
        /// </summary>
        public string AllowHeaders
        {
            get { return _AllowHeaders; }
            set { _AllowHeaders = value ?? throw new ArgumentNullException(nameof(value)); }
        }

        /// <summary>
        /// Value for the Access-Control-Expose-Headers header.
        /// </summary>
        public string ExposeHeaders
        {
            get { return _ExposeHeaders; }
            set { _ExposeHeaders = value ?? throw new ArgumentNullException(nameof(value)); }
        }

        /// <summary>
        /// Value for the Access-Control-Max-Age header (in seconds).
        /// </summary>
        public int MaxAgeSeconds { get; set; } = 86400;

        /// <summary>
        /// Value for the Access-Control-Allow-Credentials header.
        /// </summary>
        public bool AllowCredentials { get; set; } = false;

        #endregion

        #region Private-Members

        private string _AllowOrigin = "*";
        private string _AllowMethods = "OPTIONS, HEAD, GET, PUT, POST, DELETE, PATCH";
        private string _AllowHeaders = "*";
        private string _ExposeHeaders = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the CorsSettings class.
        /// </summary>
        public CorsSettings()
        {
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}
