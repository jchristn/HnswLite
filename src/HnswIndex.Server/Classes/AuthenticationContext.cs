namespace HnswIndex.Server.Classes
{
    /// <summary>
    /// Authentication context for requests.
    /// </summary>
    public class AuthenticationContext
    {
        #region Public-Members

        /// <summary>
        /// User GUID.
        /// </summary>
        public string UserGuid { get; set; } = string.Empty;

        /// <summary>
        /// Authentication token.
        /// </summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>
        /// Indicates if the user is authenticated.
        /// </summary>
        public bool Authenticated { get; set; } = false;

        /// <summary>
        /// Indicates if the user is an administrator.
        /// </summary>
        public bool IsAdmin { get; set; } = false;

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the AuthenticationContext class.
        /// </summary>
        public AuthenticationContext()
        {
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}