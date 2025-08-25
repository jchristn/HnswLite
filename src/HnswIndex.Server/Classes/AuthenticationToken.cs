namespace HnswIndex.Server.Classes
{
    /// <summary>
    /// Authentication token.
    /// </summary>
    public class AuthenticationToken
    {
        #region Public-Members

        /// <summary>
        /// Token string.
        /// </summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>
        /// User GUID.
        /// </summary>
        public string UserGuid { get; set; } = string.Empty;

        /// <summary>
        /// Timestamp when the token was created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Timestamp when the token expires.
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow.AddHours(1);

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the AuthenticationToken class.
        /// </summary>
        public AuthenticationToken()
        {
        }

        /// <summary>
        /// Initializes a new instance of the AuthenticationToken class.
        /// </summary>
        /// <param name="userGuid">User GUID.</param>
        /// <param name="expirySeconds">Expiry in seconds.</param>
        public AuthenticationToken(string userGuid, int expirySeconds)
        {
            ArgumentNullException.ThrowIfNull(userGuid);
            if (expirySeconds < 1) throw new ArgumentOutOfRangeException(nameof(expirySeconds));

            UserGuid = userGuid;
            Token = Guid.NewGuid().ToString();
            CreatedUtc = DateTime.UtcNow;
            ExpiresUtc = CreatedUtc.AddSeconds(expirySeconds);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Check if the token is expired.
        /// </summary>
        /// <returns>True if expired.</returns>
        public bool IsExpired()
        {
            return DateTime.UtcNow > ExpiresUtc;
        }

        #endregion

        #region Private-Methods

        #endregion
    }
}