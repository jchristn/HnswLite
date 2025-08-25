namespace HnswIndex.Server.Classes
{
    /// <summary>
    /// Authentication configuration settings.
    /// </summary>
    public class AuthenticationSettings
    {
        #region Public-Members

        /// <summary>
        /// Default API key for authentication.
        /// </summary>
        public string DefaultApiKey
        {
            get { return _DefaultApiKey; }
            set { _DefaultApiKey = value ?? throw new ArgumentNullException(nameof(value)); }
        }

        /// <summary>
        /// Token expiration in seconds.
        /// </summary>
        public int TokenExpirySeconds { get; set; } = 3600;

        #endregion

        #region Private-Members

        private string _DefaultApiKey = Guid.NewGuid().ToString();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the AuthenticationSettings class.
        /// </summary>
        public AuthenticationSettings()
        {
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}