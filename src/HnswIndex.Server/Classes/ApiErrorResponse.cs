namespace HnswIndex.Server.Classes
{
    /// <summary>
    /// API error response.
    /// </summary>
    public class ApiErrorResponse
    {
        #region Public-Members

        /// <summary>
        /// Error code.
        /// </summary>
        public ApiErrorEnum Error { get; set; } = ApiErrorEnum.Success;

        /// <summary>
        /// Error message.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Timestamp when the error occurred.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the ApiErrorResponse class.
        /// </summary>
        public ApiErrorResponse()
        {
        }

        /// <summary>
        /// Initializes a new instance of the ApiErrorResponse class.
        /// </summary>
        /// <param name="error">Error code.</param>
        /// <param name="message">Error message.</param>
        public ApiErrorResponse(ApiErrorEnum error, string message)
        {
            ArgumentNullException.ThrowIfNull(message);
            Error = error;
            Message = message;
            Timestamp = DateTime.UtcNow;
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}