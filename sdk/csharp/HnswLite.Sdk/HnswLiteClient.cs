namespace HnswLite.Sdk
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using HnswLite.Sdk.Models;

    /// <summary>
    /// Client for the HnswLite REST API.
    /// Provides async methods for index management, vector operations, and nearest-neighbour search.
    /// This class is thread-safe and implements <see cref="IDisposable"/>.
    /// </summary>
    public class HnswLiteClient : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Base URL of the HnswLite server (e.g. "http://localhost:8080").
        /// </summary>
        public string BaseUrl
        {
            get => _BaseUrl;
        }

        #endregion

        #region Private-Members

        private readonly string _BaseUrl;
        private readonly string _ApiKey;
        private readonly string _ApiKeyHeader;
        private readonly HttpClient _HttpClient;
        private readonly JsonSerializerOptions _JsonOptions;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of <see cref="HnswLiteClient"/>.
        /// </summary>
        /// <param name="baseUrl">Base URL of the HnswLite server (e.g. "http://localhost:8080"). Must not be null or empty.</param>
        /// <param name="apiKey">API key for authentication. Must not be null or empty.</param>
        /// <param name="apiKeyHeader">Header name for the API key. Defaults to "x-api-key".</param>
        /// <exception cref="ArgumentNullException">Thrown when baseUrl or apiKey is null.</exception>
        /// <exception cref="ArgumentException">Thrown when baseUrl or apiKey is empty or whitespace.</exception>
        public HnswLiteClient(string baseUrl, string apiKey, string apiKeyHeader = "x-api-key")
        {
            ArgumentNullException.ThrowIfNull(baseUrl);
            ArgumentNullException.ThrowIfNull(apiKey);

            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException("Base URL must not be empty or whitespace.", nameof(baseUrl));
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key must not be empty or whitespace.", nameof(apiKey));

            _BaseUrl = baseUrl.TrimEnd('/');
            _ApiKey = apiKey;
            _ApiKeyHeader = string.IsNullOrWhiteSpace(apiKeyHeader) ? "x-api-key" : apiKeyHeader;

            _HttpClient = new HttpClient();
            _HttpClient.DefaultRequestHeaders.Add(_ApiKeyHeader, _ApiKey);

            _JsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = null,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNameCaseInsensitive = false
            };
            _JsonOptions.Converters.Add(new JsonStringEnumConverter());
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Health check ping. Sends GET / and verifies a 200 response. This endpoint is unauthenticated.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the server returned 200 OK.</returns>
        public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, _BaseUrl + "/");
            request.Headers.Remove(_ApiKeyHeader);

            using HttpResponseMessage response = await _HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.StatusCode == HttpStatusCode.OK;
        }

        /// <summary>
        /// Head ping. Sends HEAD / and verifies a 200 response. This endpoint is unauthenticated.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the server returned 200 OK.</returns>
        public async Task<bool> HeadPingAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, _BaseUrl + "/");
            request.Headers.Remove(_ApiKeyHeader);

            using HttpResponseMessage response = await _HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.StatusCode == HttpStatusCode.OK;
        }

        /// <summary>
        /// Enumerate indexes with optional pagination and filtering.
        /// </summary>
        /// <param name="query">Optional enumeration query parameters. Null uses server defaults.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Paginated enumeration result containing index responses.</returns>
        /// <exception cref="HnswLiteApiException">Thrown when the server returns a non-2xx status code.</exception>
        public async Task<EnumerationResult<IndexResponse>> EnumerateIndexesAsync(
            EnumerationQuery? query = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string url = _BaseUrl + "/v1.0/indexes";
            List<string> queryParams = BuildEnumerationQueryParams(query);

            if (queryParams.Count > 0)
                url += "?" + string.Join("&", queryParams);

            using HttpResponseMessage response = await _HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            await ThrowOnErrorAsync(response, cancellationToken).ConfigureAwait(false);

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            EnumerationResult<IndexResponse> result = JsonSerializer.Deserialize<EnumerationResult<IndexResponse>>(body, _JsonOptions)
                ?? new EnumerationResult<IndexResponse>();
            return result;
        }

        /// <summary>
        /// Create a new HNSW index.
        /// </summary>
        /// <param name="request">The index creation request. Must not be null.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The created index response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="HnswLiteApiException">Thrown when the server returns a non-2xx status code.</exception>
        public async Task<IndexResponse> CreateIndexAsync(
            CreateIndexRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            string url = _BaseUrl + "/v1.0/indexes";
            string json = JsonSerializer.Serialize(request, _JsonOptions);
            using StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await _HttpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
            await ThrowOnErrorAsync(response, cancellationToken).ConfigureAwait(false);

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            IndexResponse result = JsonSerializer.Deserialize<IndexResponse>(body, _JsonOptions)
                ?? new IndexResponse();
            return result;
        }

        /// <summary>
        /// Get a single index by name.
        /// </summary>
        /// <param name="name">The index name. Must not be null or empty.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The index response.</returns>
        /// <exception cref="ArgumentException">Thrown when name is null or empty.</exception>
        /// <exception cref="HnswLiteApiException">Thrown when the server returns a non-2xx status code (e.g. 404).</exception>
        public async Task<IndexResponse> GetIndexAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            ValidateName(name);
            cancellationToken.ThrowIfCancellationRequested();

            string url = _BaseUrl + "/v1.0/indexes/" + Uri.EscapeDataString(name);

            using HttpResponseMessage response = await _HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            await ThrowOnErrorAsync(response, cancellationToken).ConfigureAwait(false);

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            IndexResponse result = JsonSerializer.Deserialize<IndexResponse>(body, _JsonOptions)
                ?? new IndexResponse();
            return result;
        }

        /// <summary>
        /// Delete an index and all its vectors.
        /// </summary>
        /// <param name="name">The index name. Must not be null or empty.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <exception cref="ArgumentException">Thrown when name is null or empty.</exception>
        /// <exception cref="HnswLiteApiException">Thrown when the server returns a non-2xx status code (e.g. 404).</exception>
        public async Task DeleteIndexAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            ValidateName(name);
            cancellationToken.ThrowIfCancellationRequested();

            string url = _BaseUrl + "/v1.0/indexes/" + Uri.EscapeDataString(name);

            using HttpResponseMessage response = await _HttpClient.DeleteAsync(url, cancellationToken).ConfigureAwait(false);
            await ThrowOnErrorAsync(response, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Perform a K-nearest-neighbour search on an index.
        /// </summary>
        /// <param name="indexName">The index name. Must not be null or empty.</param>
        /// <param name="request">The search request. Must not be null.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The search response containing results and timing.</returns>
        /// <exception cref="ArgumentException">Thrown when indexName is null or empty.</exception>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="HnswLiteApiException">Thrown when the server returns a non-2xx status code.</exception>
        public async Task<SearchResponse> SearchAsync(
            string indexName,
            SearchRequest request,
            CancellationToken cancellationToken = default)
        {
            ValidateName(indexName);
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            string url = _BaseUrl + "/v1.0/indexes/" + Uri.EscapeDataString(indexName) + "/search";
            string json = JsonSerializer.Serialize(request, _JsonOptions);
            using StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await _HttpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
            await ThrowOnErrorAsync(response, cancellationToken).ConfigureAwait(false);

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            SearchResponse result = JsonSerializer.Deserialize<SearchResponse>(body, _JsonOptions)
                ?? new SearchResponse();
            return result;
        }

        /// <summary>
        /// Add a single vector to an index.
        /// </summary>
        /// <param name="indexName">The index name. Must not be null or empty.</param>
        /// <param name="request">The vector to add. Must not be null.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The echoed add-vector request from the server.</returns>
        /// <exception cref="ArgumentException">Thrown when indexName is null or empty.</exception>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="HnswLiteApiException">Thrown when the server returns a non-2xx status code.</exception>
        public async Task<AddVectorRequest> AddVectorAsync(
            string indexName,
            AddVectorRequest request,
            CancellationToken cancellationToken = default)
        {
            ValidateName(indexName);
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            string url = _BaseUrl + "/v1.0/indexes/" + Uri.EscapeDataString(indexName) + "/vectors";
            string json = JsonSerializer.Serialize(request, _JsonOptions);
            using StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await _HttpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
            await ThrowOnErrorAsync(response, cancellationToken).ConfigureAwait(false);

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            AddVectorRequest result = JsonSerializer.Deserialize<AddVectorRequest>(body, _JsonOptions)
                ?? new AddVectorRequest();
            return result;
        }

        /// <summary>
        /// Add a batch of vectors to an index.
        /// </summary>
        /// <param name="indexName">The index name. Must not be null or empty.</param>
        /// <param name="request">The batch of vectors to add. Must not be null.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The echoed batch request from the server.</returns>
        /// <exception cref="ArgumentException">Thrown when indexName is null or empty.</exception>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="HnswLiteApiException">Thrown when the server returns a non-2xx status code.</exception>
        public async Task<AddVectorsRequest> AddVectorsAsync(
            string indexName,
            AddVectorsRequest request,
            CancellationToken cancellationToken = default)
        {
            ValidateName(indexName);
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            string url = _BaseUrl + "/v1.0/indexes/" + Uri.EscapeDataString(indexName) + "/vectors/batch";
            string json = JsonSerializer.Serialize(request, _JsonOptions);
            using StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await _HttpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
            await ThrowOnErrorAsync(response, cancellationToken).ConfigureAwait(false);

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            AddVectorsRequest result = JsonSerializer.Deserialize<AddVectorsRequest>(body, _JsonOptions)
                ?? new AddVectorsRequest();
            return result;
        }

        /// <summary>
        /// Remove a vector from an index by its GUID.
        /// </summary>
        /// <param name="indexName">The index name. Must not be null or empty.</param>
        /// <param name="vectorGuid">The GUID of the vector to remove.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <exception cref="ArgumentException">Thrown when indexName is null or empty.</exception>
        /// <exception cref="HnswLiteApiException">Thrown when the server returns a non-2xx status code (e.g. 404).</exception>
        public async Task RemoveVectorAsync(
            string indexName,
            Guid vectorGuid,
            CancellationToken cancellationToken = default)
        {
            ValidateName(indexName);
            cancellationToken.ThrowIfCancellationRequested();

            string url = _BaseUrl + "/v1.0/indexes/" + Uri.EscapeDataString(indexName)
                + "/vectors/" + vectorGuid.ToString();

            using HttpResponseMessage response = await _HttpClient.DeleteAsync(url, cancellationToken).ConfigureAwait(false);
            await ThrowOnErrorAsync(response, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Enumerate vectors within an index with optional pagination and filtering.
        /// </summary>
        /// <param name="indexName">The index name. Must not be null or empty.</param>
        /// <param name="query">Optional enumeration query parameters. Null uses server defaults.</param>
        /// <param name="includeVectors">When true, each returned entry includes its Vector values. When false (default), only GUIDs are returned.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Paginated enumeration result containing vector entry responses.</returns>
        /// <exception cref="ArgumentException">Thrown when indexName is null or empty.</exception>
        /// <exception cref="HnswLiteApiException">Thrown when the server returns a non-2xx status code.</exception>
        public async Task<EnumerationResult<VectorEntryResponse>> EnumerateVectorsAsync(
            string indexName,
            EnumerationQuery? query = null,
            bool includeVectors = false,
            CancellationToken cancellationToken = default)
        {
            ValidateName(indexName);
            cancellationToken.ThrowIfCancellationRequested();

            string url = _BaseUrl + "/v1.0/indexes/" + Uri.EscapeDataString(indexName) + "/vectors";
            List<string> queryParams = BuildEnumerationQueryParams(query);
            queryParams.Add("includeVectors=" + (includeVectors ? "true" : "false"));

            url += "?" + string.Join("&", queryParams);

            using HttpResponseMessage response = await _HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            await ThrowOnErrorAsync(response, cancellationToken).ConfigureAwait(false);

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            EnumerationResult<VectorEntryResponse> result = JsonSerializer.Deserialize<EnumerationResult<VectorEntryResponse>>(body, _JsonOptions)
                ?? new EnumerationResult<VectorEntryResponse>();
            return result;
        }

        /// <summary>
        /// Get a single vector by its GUID. The returned response always includes the Vector values.
        /// </summary>
        /// <param name="indexName">The index name. Must not be null or empty.</param>
        /// <param name="vectorGuid">The GUID of the vector to retrieve.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The vector entry response including its Vector values.</returns>
        /// <exception cref="ArgumentException">Thrown when indexName is null or empty.</exception>
        /// <exception cref="HnswLiteApiException">Thrown when the server returns a non-2xx status code (e.g. 404 VectorNotFound).</exception>
        public async Task<VectorEntryResponse> GetVectorAsync(
            string indexName,
            Guid vectorGuid,
            CancellationToken cancellationToken = default)
        {
            ValidateName(indexName);
            cancellationToken.ThrowIfCancellationRequested();

            string url = _BaseUrl + "/v1.0/indexes/" + Uri.EscapeDataString(indexName)
                + "/vectors/" + Uri.EscapeDataString(vectorGuid.ToString());

            using HttpResponseMessage response = await _HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            await ThrowOnErrorAsync(response, cancellationToken).ConfigureAwait(false);

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            VectorEntryResponse result = JsonSerializer.Deserialize<VectorEntryResponse>(body, _JsonOptions)
                ?? new VectorEntryResponse();
            return result;
        }

        /// <summary>
        /// Releases all resources used by the <see cref="HnswLiteClient"/>.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Releases unmanaged and optionally managed resources.
        /// </summary>
        /// <param name="disposing">True to release managed resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_Disposed)
            {
                if (disposing)
                {
                    _HttpClient.Dispose();
                }

                _Disposed = true;
            }
        }

        private static void ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Name must not be null, empty, or whitespace.", nameof(name));
        }

        private static List<string> BuildEnumerationQueryParams(EnumerationQuery? query)
        {
            List<string> queryParams = new List<string>();

            if (query != null)
            {
                queryParams.Add("maxResults=" + query.MaxResults.ToString());
                queryParams.Add("skip=" + query.Skip.ToString());
                queryParams.Add("ordering=" + query.Ordering.ToString());

                if (!string.IsNullOrEmpty(query.Prefix))
                    queryParams.Add("prefix=" + Uri.EscapeDataString(query.Prefix));
                if (!string.IsNullOrEmpty(query.Suffix))
                    queryParams.Add("suffix=" + Uri.EscapeDataString(query.Suffix));
                if (query.ContinuationToken.HasValue)
                    queryParams.Add("continuationToken=" + query.ContinuationToken.Value.ToString());
                if (query.CreatedAfterUtc.HasValue)
                    queryParams.Add("createdAfterUtc=" + query.CreatedAfterUtc.Value.ToString("o"));
                if (query.CreatedBeforeUtc.HasValue)
                    queryParams.Add("createdBeforeUtc=" + query.CreatedBeforeUtc.Value.ToString("o"));

                if (query.Labels != null && query.Labels.Count > 0)
                {
                    List<string> encoded = new List<string>(query.Labels.Count);
                    foreach (string label in query.Labels)
                    {
                        if (!string.IsNullOrEmpty(label))
                            encoded.Add(Uri.EscapeDataString(label));
                    }
                    if (encoded.Count > 0)
                        queryParams.Add("labels=" + string.Join(",", encoded));
                }

                if (query.Tags != null && query.Tags.Count > 0)
                {
                    List<string> encoded = new List<string>(query.Tags.Count);
                    foreach (KeyValuePair<string, string> kv in query.Tags)
                    {
                        if (string.IsNullOrEmpty(kv.Key)) continue;
                        string key = Uri.EscapeDataString(kv.Key);
                        string value = Uri.EscapeDataString(kv.Value ?? string.Empty);
                        encoded.Add(key + ":" + value);
                    }
                    if (encoded.Count > 0)
                        queryParams.Add("tags=" + string.Join(",", encoded));
                }

                if (query.CaseInsensitive)
                    queryParams.Add("caseInsensitive=true");
            }

            return queryParams;
        }

        private async Task ThrowOnErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            int statusCode = (int)response.StatusCode;
            if (statusCode >= 200 && statusCode < 300)
                return;

            string errorBody = string.Empty;
            try
            {
                errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Unable to read error body; proceed with empty values.
            }

            string error = response.StatusCode.ToString();
            string message = errorBody;

            try
            {
                ApiErrorResponse? apiError = JsonSerializer.Deserialize<ApiErrorResponse>(errorBody, _JsonOptions);
                if (apiError != null)
                {
                    error = !string.IsNullOrEmpty(apiError.Error) ? apiError.Error : error;
                    message = !string.IsNullOrEmpty(apiError.Message) ? apiError.Message : message;
                }
            }
            catch
            {
                // Unable to parse error body as ApiErrorResponse; use raw body as message.
            }

            throw new HnswLiteApiException(response.StatusCode, error, message);
        }

        #endregion
    }
}
