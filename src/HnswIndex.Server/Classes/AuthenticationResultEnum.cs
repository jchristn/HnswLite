namespace HnswIndex.Server.Classes
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Authentication result enumeration.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AuthenticationResultEnum
    {
        /// <summary>
        /// Success.
        /// </summary>
        [EnumMember(Value = "Success")]
        Success,
        /// <summary>
        /// Invalid credentials.
        /// </summary>
        [EnumMember(Value = "InvalidCredentials")]
        InvalidCredentials,
        /// <summary>
        /// Token expired.
        /// </summary>
        [EnumMember(Value = "TokenExpired")]
        TokenExpired,
        /// <summary>
        /// Token not found.
        /// </summary>
        [EnumMember(Value = "TokenNotFound")]
        TokenNotFound,
        /// <summary>
        /// User not found.
        /// </summary>
        [EnumMember(Value = "UserNotFound")]
        UserNotFound,
        /// <summary>
        /// Access denied.
        /// </summary>
        [EnumMember(Value = "AccessDenied")]
        AccessDenied
    }
}