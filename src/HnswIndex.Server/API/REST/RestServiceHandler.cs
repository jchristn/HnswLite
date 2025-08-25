namespace HnswIndex.Server.API.REST
{
    using System.Text;
    using System.Text.Json;
    using SerializationHelper;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using HnswIndex.Server.Classes;
    using HnswIndex.Server.Services;
    using SyslogLogging;

    /// <summary>
    /// REST service handler for HNSW index operations.
    /// </summary>
    public static class RestServiceHandler
    {
        #region Public-Members

        #endregion

        #region Private-Members

        private static readonly string _Header = "[API] ";
        private static readonly Serializer _Serializer = new Serializer();
        private static IndexManager _IndexManager = null!;
        private static HnswIndexSettings _Settings = null!;
        private static LoggingModule? _Logging;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Initialize the REST service handler.
        /// </summary>
        /// <param name="indexManager">Index manager instance.</param>
        /// <param name="settings">Server settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when parameters are null.</exception>
        public static void Initialize(IndexManager indexManager, HnswIndexSettings settings, LoggingModule? logging = null)
        {
            ArgumentNullException.ThrowIfNull(indexManager);
            ArgumentNullException.ThrowIfNull(settings);

            _IndexManager = indexManager;
            _Settings = settings;
            _Logging = logging;
        }

        /// <summary>
        /// Root handler for GET / and HEAD /.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when ctx is null.</exception>
        public static async Task RootHandler(HttpContextBase ctx)
        {
            ArgumentNullException.ThrowIfNull(ctx);

            ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            ctx.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
            ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, x-api-key");

            if (ctx.Request.Method == HttpMethod.HEAD)
            {
                ctx.Response.StatusCode = 200;
                await ctx.Response.Send();
                return;
            }

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = Constants.HtmlContentType;
            await ctx.Response.Send(Constants.DefaultHomepage);
        }

        /// <summary>
        /// OPTIONS handler for CORS support.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when ctx is null.</exception>
        public static async Task OptionsHandler(HttpContextBase ctx)
        {
            ArgumentNullException.ThrowIfNull(ctx);

            ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            ctx.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
            ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, x-api-key");

            await ctx.Response.Send();
        }

        /// <summary>
        /// Route handler for API requests.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when ctx is null.</exception>
        public static async Task RouteHandler(HttpContextBase ctx)
        {
            ArgumentNullException.ThrowIfNull(ctx);

            try
            {
                if (_Settings.Debug.Api)
                {
                    _Logging?.Debug(_Header + $"[API] {ctx.Request.Method} {ctx.Request.Url.RawWithoutQuery}");
                }

                // Authentication check
                if (_Settings.Server.RequireAuthentication)
                {
                    if (!IsAuthenticated(ctx))
                    {
                        _Logging?.Warn(_Header + $"unauthorized access attempt from {ctx.Request.Source.IpAddress}:{ctx.Request.Source.Port}");
                        await SendErrorResponseAsync(ctx, ApiErrorEnum.Unauthorized, "Authentication required").ConfigureAwait(false);
                        return;
                    }
                }

                string[] segments = ctx.Request.Url.Elements;
                if (segments.Length < 2)
                {
                    await SendErrorResponseAsync(ctx, ApiErrorEnum.BadRequest, "Invalid API endpoint").ConfigureAwait(false);
                    return;
                }

                string version = segments[0]; // v1.0
                string resource = segments[1]; // indexes

                if (!string.Equals(version, "v1.0", StringComparison.OrdinalIgnoreCase))
                {
                    await SendErrorResponseAsync(ctx, ApiErrorEnum.BadRequest, "Invalid API version").ConfigureAwait(false);
                    return;
                }

                switch (resource.ToLowerInvariant())
                {
                    case "indexes":
                        await HandleIndexesAsync(ctx, segments).ConfigureAwait(false);
                        break;
                    default:
                        await SendErrorResponseAsync(ctx, ApiErrorEnum.NotFound, $"Unknown resource: {resource}").ConfigureAwait(false);
                        break;
                }
            }
            catch (Exception ex)
            {
                if (_Settings.Debug.Api)
                {
                    _Logging?.Warn(_Header + $"API exception: {ex}");
                }

                await SendErrorResponseAsync(ctx, ApiErrorEnum.InternalServerError, ex.Message).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private static bool IsAuthenticated(HttpContextBase ctx)
        {
            ArgumentNullException.ThrowIfNull(ctx);

            string? apiKey = ctx.Request.RetrieveHeaderValue(_Settings.Server.AdminApiKeyHeader);
            if (string.IsNullOrEmpty(apiKey))
            {
                return false;
            }

            return string.Equals(apiKey, _Settings.Server.AdminApiKey, StringComparison.Ordinal);
        }

        private static async Task HandleIndexesAsync(HttpContextBase ctx, string[] segments)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            ArgumentNullException.ThrowIfNull(segments);

            string method = ctx.Request.Method.ToString();

            // GET /v1.0/indexes - List all indexes
            if (method == "GET" && segments.Length == 2)
            {
                await ListIndexesAsync(ctx).ConfigureAwait(false);
                return;
            }

            // POST /v1.0/indexes - Create new index
            if (method == "POST" && segments.Length == 2)
            {
                await CreateIndexAsync(ctx).ConfigureAwait(false);
                return;
            }

            // GET /v1.0/indexes/{name} - Get index info
            if (method == "GET" && segments.Length == 3)
            {
                await GetIndexAsync(ctx, segments[2]).ConfigureAwait(false);
                return;
            }

            // DELETE /v1.0/indexes/{name} - Delete index
            if (method == "DELETE" && segments.Length == 3)
            {
                await DeleteIndexAsync(ctx, segments[2]).ConfigureAwait(false);
                return;
            }

            // POST /v1.0/indexes/{name}/search - Search vectors
            if (method == "POST" && segments.Length == 4 && string.Equals(segments[3], "search", StringComparison.OrdinalIgnoreCase))
            {
                await SearchAsync(ctx, segments[2]).ConfigureAwait(false);
                return;
            }

            // POST /v1.0/indexes/{name}/vectors - Add vector
            if (method == "POST" && segments.Length == 4 && string.Equals(segments[3], "vectors", StringComparison.OrdinalIgnoreCase))
            {
                await AddVectorAsync(ctx, segments[2]).ConfigureAwait(false);
                return;
            }

            // POST /v1.0/indexes/{name}/vectors/batch - Add multiple vectors
            if (method == "POST" && segments.Length == 5 && string.Equals(segments[3], "vectors", StringComparison.OrdinalIgnoreCase) && string.Equals(segments[4], "batch", StringComparison.OrdinalIgnoreCase))
            {
                await AddVectorsAsync(ctx, segments[2]).ConfigureAwait(false);
                return;
            }

            // DELETE /v1.0/indexes/{name}/vectors/{guid} - Remove vector
            if (method == "DELETE" && segments.Length == 5 && string.Equals(segments[3], "vectors", StringComparison.OrdinalIgnoreCase))
            {
                await RemoveVectorAsync(ctx, segments[2], segments[4]).ConfigureAwait(false);
                return;
            }

            await SendErrorResponseAsync(ctx, ApiErrorEnum.NotFound, "Endpoint not found").ConfigureAwait(false);
        }

        private static async Task ListIndexesAsync(HttpContextBase ctx)
        {
            try
            {
                List<IndexResponse> indexes = _IndexManager.ListIndexes();
                string json = _Serializer.SerializeJson(indexes, true);

                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.Send(json).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await SendErrorResponseAsync(ctx, ApiErrorEnum.InternalServerError, ex.Message).ConfigureAwait(false);
            }
        }

        private static async Task CreateIndexAsync(HttpContextBase ctx)
        {
            try
            {
                using StreamReader reader = new StreamReader(ctx.Request.Data);
                string requestBody = await reader.ReadToEndAsync().ConfigureAwait(false);
                
                CreateIndexRequest? request;
                try
                {
                    request = _Serializer.DeserializeJson<CreateIndexRequest>(requestBody);
                }
                catch (JsonException)
                {
                    await SendErrorResponseAsync(ctx, ApiErrorEnum.BadRequest, "Invalid JSON format").ConfigureAwait(false);
                    return;
                }

                if (request == null)
                {
                    await SendErrorResponseAsync(ctx, ApiErrorEnum.BadRequest, "Invalid request body").ConfigureAwait(false);
                    return;
                }

                IndexResponse response = await _IndexManager.CreateIndexAsync(request).ConfigureAwait(false);
                string json = _Serializer.SerializeJson(response, true);

                ctx.Response.StatusCode = 201;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.Send(json).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                await SendErrorResponseAsync(ctx, ApiErrorEnum.Conflict, ex.Message).ConfigureAwait(false);
            }
            catch (ArgumentException ex)
            {
                await SendErrorResponseAsync(ctx, ApiErrorEnum.BadRequest, ex.Message).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await SendErrorResponseAsync(ctx, ApiErrorEnum.InternalServerError, ex.Message).ConfigureAwait(false);
            }
        }

        private static async Task GetIndexAsync(HttpContextBase ctx, string indexName)
        {
            try
            {
                IndexResponse? response = _IndexManager.GetIndex(indexName);

                if (response == null)
                {
                    await SendErrorResponseAsync(ctx, ApiErrorEnum.IndexNotFound, $"Index '{indexName}' not found").ConfigureAwait(false);
                    return;
                }

                string json = _Serializer.SerializeJson(response, true);

                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.Send(json).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await SendErrorResponseAsync(ctx, ApiErrorEnum.InternalServerError, ex.Message).ConfigureAwait(false);
            }
        }

        private static async Task DeleteIndexAsync(HttpContextBase ctx, string indexName)
        {
            try
            {
                bool deleted = _IndexManager.DeleteIndex(indexName);

                if (!deleted)
                {
                    await SendErrorResponseAsync(ctx, ApiErrorEnum.IndexNotFound, $"Index '{indexName}' not found").ConfigureAwait(false);
                    return;
                }

                ctx.Response.StatusCode = 204;
                await ctx.Response.Send().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await SendErrorResponseAsync(ctx, ApiErrorEnum.InternalServerError, ex.Message).ConfigureAwait(false);
            }
        }

        private static async Task SearchAsync(HttpContextBase ctx, string indexName)
        {
            try
            {
                using StreamReader reader = new StreamReader(ctx.Request.Data);
                string requestBody = await reader.ReadToEndAsync().ConfigureAwait(false);
                
                SearchRequest? request;
                try
                {
                    request = _Serializer.DeserializeJson<SearchRequest>(requestBody);
                }
                catch (JsonException)
                {
                    await SendErrorResponseAsync(ctx, ApiErrorEnum.BadRequest, "Invalid JSON format").ConfigureAwait(false);
                    return;
                }

                if (request == null)
                {
                    await SendErrorResponseAsync(ctx, ApiErrorEnum.BadRequest, "Invalid request body").ConfigureAwait(false);
                    return;
                }

                SearchResponse response = await _IndexManager.SearchAsync(indexName, request).ConfigureAwait(false);
                string json = _Serializer.SerializeJson(response, true);

                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.Send(json).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                await SendErrorResponseAsync(ctx, ApiErrorEnum.IndexNotFound, ex.Message).ConfigureAwait(false);
            }
            catch (ArgumentException ex)
            {
                await SendErrorResponseAsync(ctx, ApiErrorEnum.BadRequest, ex.Message).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await SendErrorResponseAsync(ctx, ApiErrorEnum.InternalServerError, ex.Message).ConfigureAwait(false);
            }
        }

        private static async Task AddVectorAsync(HttpContextBase ctx, string indexName)
        {
            try
            {
                using StreamReader reader = new StreamReader(ctx.Request.Data);
                string requestBody = await reader.ReadToEndAsync().ConfigureAwait(false);
                
                AddVectorRequest? request;
                try
                {
                    request = _Serializer.DeserializeJson<AddVectorRequest>(requestBody);
                }
                catch (JsonException)
                {
                    await SendErrorResponseAsync(ctx, ApiErrorEnum.BadRequest, "Invalid JSON format").ConfigureAwait(false);
                    return;
                }

                if (request == null)
                {
                    await SendErrorResponseAsync(ctx, ApiErrorEnum.BadRequest, "Invalid request body").ConfigureAwait(false);
                    return;
                }

                bool success = await _IndexManager.AddVectorAsync(indexName, request).ConfigureAwait(false);

                if (success)
                {
                    ctx.Response.StatusCode = 201;
                    ctx.Response.ContentType = "application/json";
                    string json = _Serializer.SerializeJson(request, true);
                    await ctx.Response.Send(json).ConfigureAwait(false);
                }
                else
                {
                    ctx.Response.StatusCode = 500;
                    await ctx.Response.Send().ConfigureAwait(false);
                }
            }
            catch (InvalidOperationException ex)
            {
                await SendErrorResponseAsync(ctx, ApiErrorEnum.IndexNotFound, ex.Message).ConfigureAwait(false);
            }
            catch (ArgumentException ex)
            {
                await SendErrorResponseAsync(ctx, ApiErrorEnum.BadRequest, ex.Message).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await SendErrorResponseAsync(ctx, ApiErrorEnum.InternalServerError, ex.Message).ConfigureAwait(false);
            }
        }

        private static async Task AddVectorsAsync(HttpContextBase ctx, string indexName)
        {
            try
            {
                using StreamReader reader = new StreamReader(ctx.Request.Data);
                string requestBody = await reader.ReadToEndAsync().ConfigureAwait(false);
                
                AddVectorsRequest? request;
                try
                {
                    request = _Serializer.DeserializeJson<AddVectorsRequest>(requestBody);
                }
                catch (JsonException)
                {
                    await SendErrorResponseAsync(ctx, ApiErrorEnum.BadRequest, "Invalid JSON format").ConfigureAwait(false);
                    return;
                }

                if (request == null)
                {
                    await SendErrorResponseAsync(ctx, ApiErrorEnum.BadRequest, "Invalid request body").ConfigureAwait(false);
                    return;
                }

                bool success = await _IndexManager.AddVectorsAsync(indexName, request).ConfigureAwait(false);

                if (success)
                {
                    ctx.Response.StatusCode = 201;
                    ctx.Response.ContentType = "application/json";
                    string json = _Serializer.SerializeJson(request, true);
                    await ctx.Response.Send(json).ConfigureAwait(false);
                }
                else
                {
                    ctx.Response.StatusCode = 500;
                    await ctx.Response.Send().ConfigureAwait(false);
                }
            }
            catch (InvalidOperationException ex)
            {
                await SendErrorResponseAsync(ctx, ApiErrorEnum.IndexNotFound, ex.Message).ConfigureAwait(false);
            }
            catch (ArgumentException ex)
            {
                await SendErrorResponseAsync(ctx, ApiErrorEnum.BadRequest, ex.Message).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await SendErrorResponseAsync(ctx, ApiErrorEnum.InternalServerError, ex.Message).ConfigureAwait(false);
            }
        }

        private static async Task RemoveVectorAsync(HttpContextBase ctx, string indexName, string vectorGuid)
        {
            try
            {
                Guid guid = Guid.Parse(vectorGuid);
                bool success = await _IndexManager.RemoveVectorAsync(indexName, guid).ConfigureAwait(false);

                ctx.Response.StatusCode = success ? 204 : 404;
                await ctx.Response.Send().ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                await SendErrorResponseAsync(ctx, ApiErrorEnum.IndexNotFound, ex.Message).ConfigureAwait(false);
            }
            catch (ArgumentException ex)
            {
                await SendErrorResponseAsync(ctx, ApiErrorEnum.BadRequest, ex.Message).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await SendErrorResponseAsync(ctx, ApiErrorEnum.InternalServerError, ex.Message).ConfigureAwait(false);
            }
        }

        private static async Task SendErrorResponseAsync(HttpContextBase ctx, ApiErrorEnum error, string message)
        {
            ApiErrorResponse errorResponse = new ApiErrorResponse(error, message);
            string json = _Serializer.SerializeJson(errorResponse, true);

            int statusCode = error switch
            {
                ApiErrorEnum.BadRequest => 400,
                ApiErrorEnum.Unauthorized => 401,
                ApiErrorEnum.Forbidden => 403,
                ApiErrorEnum.NotFound => 404,
                ApiErrorEnum.IndexNotFound => 404,
                ApiErrorEnum.VectorNotFound => 404,
                ApiErrorEnum.Conflict => 409,
                _ => 500
            };

            ctx.Response.StatusCode = statusCode;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.Send(json).ConfigureAwait(false);
        }

        #endregion
    }
}