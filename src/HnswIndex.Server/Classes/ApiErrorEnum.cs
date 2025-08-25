namespace HnswIndex.Server.Classes
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// API error enumeration.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ApiErrorEnum
    {
        /// <summary>
        /// Success.
        /// </summary>
        [EnumMember(Value = "Success")]
        Success,
        /// <summary>
        /// Bad request.
        /// </summary>
        [EnumMember(Value = "BadRequest")]
        BadRequest,
        /// <summary>
        /// Unauthorized.
        /// </summary>
        [EnumMember(Value = "Unauthorized")]
        Unauthorized,
        /// <summary>
        /// Forbidden.
        /// </summary>
        [EnumMember(Value = "Forbidden")]
        Forbidden,
        /// <summary>
        /// Not found.
        /// </summary>
        [EnumMember(Value = "NotFound")]
        NotFound,
        /// <summary>
        /// Conflict.
        /// </summary>
        [EnumMember(Value = "Conflict")]
        Conflict,
        /// <summary>
        /// Internal server error.
        /// </summary>
        [EnumMember(Value = "InternalServerError")]
        InternalServerError,
        /// <summary>
        /// Index not found.
        /// </summary>
        [EnumMember(Value = "IndexNotFound")]
        IndexNotFound,
        /// <summary>
        /// Vector not found.
        /// </summary>
        [EnumMember(Value = "VectorNotFound")]
        VectorNotFound,
        /// <summary>
        /// Invalid dimension.
        /// </summary>
        [EnumMember(Value = "InvalidDimension")]
        InvalidDimension,
        /// <summary>
        /// Storage error.
        /// </summary>
        [EnumMember(Value = "StorageError")]
        StorageError
    }
}