namespace HnswLite.Sdk
{
    using System;
    using System.Net;

    /// <summary>
    /// Exception thrown when the HnswLite REST API returns a non-2xx status code.
    /// </summary>
    public class HnswLiteApiException : Exception
    {
        #region Public-Members

        /// <summary>
        /// HTTP status code returned by the server.
        /// </summary>
        public HttpStatusCode StatusCode { get; }

        /// <summary>
        /// Error enum value from the server response (e.g. "BadRequest", "NotFound").
        /// </summary>
        public string Error { get; }

        /// <summary>
        /// Diagnostic message from the server response.
        /// </summary>
        public string ApiMessage { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of <see cref="HnswLiteApiException"/>.
        /// </summary>
        /// <param name="statusCode">The HTTP status code.</param>
        /// <param name="error">The error enum value from the response body.</param>
        /// <param name="apiMessage">The diagnostic message from the response body.</param>
        public HnswLiteApiException(HttpStatusCode statusCode, string error, string apiMessage)
            : base($"HnswLite API error {(int)statusCode} ({error}): {apiMessage}")
        {
            StatusCode = statusCode;
            Error = error ?? string.Empty;
            ApiMessage = apiMessage ?? string.Empty;
        }

        /// <summary>
        /// Initializes a new instance of <see cref="HnswLiteApiException"/> with an inner exception.
        /// </summary>
        /// <param name="statusCode">The HTTP status code.</param>
        /// <param name="error">The error enum value from the response body.</param>
        /// <param name="apiMessage">The diagnostic message from the response body.</param>
        /// <param name="innerException">The inner exception.</param>
        public HnswLiteApiException(HttpStatusCode statusCode, string error, string apiMessage, Exception innerException)
            : base($"HnswLite API error {(int)statusCode} ({error}): {apiMessage}", innerException)
        {
            StatusCode = statusCode;
            Error = error ?? string.Empty;
            ApiMessage = apiMessage ?? string.Empty;
        }

        #endregion
    }
}
