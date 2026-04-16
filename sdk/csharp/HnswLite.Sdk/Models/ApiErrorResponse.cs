namespace HnswLite.Sdk.Models
{
    using System;

    /// <summary>
    /// Error response body returned by the server on non-2xx status codes.
    /// </summary>
    public class ApiErrorResponse
    {
        #region Public-Members

        /// <summary>
        /// Error code enum value (e.g. "BadRequest", "NotFound", "Conflict").
        /// </summary>
        public string Error { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable diagnostic message.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Server-side UTC timestamp of the error.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
