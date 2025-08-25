#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
namespace HnswIndex.Server
{
    using System.Reflection;
    using SerializationHelper;
    using SyslogLogging;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using HnswIndex.Server.Classes;
    using HnswIndex.Server.Services;
    using HnswIndex.Server.API.REST;

    /// <summary>
    /// HNSW Index Server main application.
    /// </summary>
    public static class HnswIndexServer
    {
        #region Public-Members

        #endregion

        #region Private-Members

        private static readonly string _Header = "[HnswIndexServer] ";
        private static readonly Serializer _Serializer = new Serializer();
        private static HnswIndexSettings _Settings = new HnswIndexSettings();
        private static LoggingModule _Logging = null!;
        private static IndexManager _IndexManager = null!;
        private static Webserver _Server = null!;

        #endregion

        #region Entrypoint

        /// <summary>
        /// Main entry point for the HNSW Index Server.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
        /// <returns>Exit code.</returns>
        public static async Task<int> Main(string[] args)
        {
            Welcome();

            if (args.Length > 0)
            {
                if (args[0].Equals("setup") || args[0].Equals("--setup"))
                {
                    Setup();
                    return 0;
                }
            }

            if (!InitializeSettings()) return -1;
            if (!await InitializeGlobalsAsync().ConfigureAwait(false)) return -1;
            if (!StartWebserver()) return -1;

            _Logging.Info(_Header + "HNSW index server started successfully");
            _Logging.Info(_Header +
                          $"listening on {(_Settings.Server.Ssl ? "https" : "http")}://{_Settings.Server.Hostname}:{_Settings.Server.Port}");
            _Logging.Info(_Header + $"admin API key: {_Settings.Server.AdminApiKey}");
            _Logging.Info(_Header + "press CTRL+C to exit");

            // Keep the server running
            ManualResetEvent waitHandle = new ManualResetEvent(false);
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                waitHandle.Set();
            };

            await Task.Run(() => waitHandle.WaitOne()).ConfigureAwait(false);

            _Logging.Info(_Header + "shutdown requested");
            _Server?.Dispose();
            _IndexManager?.Dispose();

            return 0;
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        private static void Welcome()
        {
            Console.WriteLine(Environment.NewLine);
            Console.WriteLine(Constants.Logo);
            Console.WriteLine(Constants.ProductName);
            Console.WriteLine(Constants.Copyright);
            Console.WriteLine("");

            if (Environment.OSVersion.Platform == PlatformID.Unix ||
                Environment.OSVersion.Platform == PlatformID.MacOSX)
            {
                Console.WriteLine("");
            }
        }

        private static void Setup()
        {
            if (File.Exists(Constants.SettingsFile))
                File.Delete(Constants.SettingsFile);

            try
            {
                HnswIndexSettings defaultSettings = new HnswIndexSettings();
                string json = _Serializer.SerializeJson(defaultSettings, true);

                File.WriteAllText(Constants.SettingsFile, json);
                Console.WriteLine($"Configuration file '{Constants.SettingsFile}' created successfully.");
                Console.WriteLine("Please review the settings before starting the server.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create configuration file: {ex.Message}");
            }
        }

        private static bool InitializeSettings()
        {
            try
            {
                if (File.Exists(Constants.SettingsFile))
                {
                    Console.WriteLine($"Loading settings from '{Constants.SettingsFile}'");
                    string json = File.ReadAllText(Constants.SettingsFile);
                    HnswIndexSettings? settings = _Serializer.DeserializeJson<HnswIndexSettings>(json);

                    if (settings != null)
                    {
                        _Settings = settings;
                    }
                }
                else
                {
                    Console.WriteLine(
                        $"Settings file '{Constants.SettingsFile}' not found, creating default configuration");
                    _Settings = new HnswIndexSettings();

                    // Auto-create the settings file
                    string json = _Serializer.SerializeJson(_Settings, true);
                    File.WriteAllText(Constants.SettingsFile, json);
                    Console.WriteLine($"Configuration file '{Constants.SettingsFile}' created successfully");
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load settings: {ex.Message}");
                return false;
            }
        }

        private static async Task<bool> InitializeGlobalsAsync()
        {
            try
            {
                // Initialize logging
                _Logging = new LoggingModule(_Settings.Logging.SyslogServerIp, _Settings.Logging.SyslogServerPort);
                _Logging.Settings.EnableConsole = _Settings.Logging.EnableConsole;
                _Logging.Settings.EnableColors = _Settings.Logging.EnableColors;

                if (!String.IsNullOrEmpty(_Settings.Logging.LogDirectory))
                {
                    if (!Directory.Exists(_Settings.Logging.LogDirectory))
                        Directory.CreateDirectory(_Settings.Logging.LogDirectory);
                }

                if (!String.IsNullOrEmpty(_Settings.Logging.LogFilename))
                {
                    _Logging.Settings.FileLogging = FileLoggingMode.FileWithDate;
                    _Logging.Settings.LogFilename = _Settings.Logging.LogFilename;
                }

                _Logging.Debug(_Header + "logging initialized");

                // Initialize index manager
                _IndexManager = new IndexManager(_Settings.Storage.SqliteDirectory, _Logging);

                // Initialize REST handler
                RestServiceHandler.Initialize(_IndexManager, _Settings, _Logging);

                _Logging.Info(_Header + "global objects initialized");
                return await Task.FromResult(true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging?.Warn(_Header + $"failed to initialize global objects: {ex.Message}");
                return false;
            }
        }

        private static bool StartWebserver()
        {
            try
            {
                WebserverSettings settings = new WebserverSettings(_Settings.Server.Hostname, _Settings.Server.Port);
                settings.Debug.Requests = _Settings.Debug.HttpRequests;
                settings.Debug.Responses = _Settings.Debug.HttpRequests;

                _Server = new Webserver(settings, DefaultRoute);
                _Server.Routes.PostRouting = PostRoutingHandler;

                // Add API routes
                _Server.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/", RestServiceHandler.RootHandler);
                _Server.Routes.PreAuthentication.Static.Add(HttpMethod.HEAD, "/", RestServiceHandler.RootHandler);
                _Server.Routes.PreAuthentication.Static.Add(HttpMethod.OPTIONS, "/", RestServiceHandler.OptionsHandler);
                _Server.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/v1.0/indexes",
                    RestServiceHandler.RouteHandler);
                _Server.Routes.PreAuthentication.Static.Add(HttpMethod.POST, "/v1.0/indexes",
                    RestServiceHandler.RouteHandler);

                _Server.Routes.PreAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/indexes/{name}",
                    RestServiceHandler.RouteHandler);
                _Server.Routes.PreAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/indexes/{name}",
                    RestServiceHandler.RouteHandler);

                _Server.Routes.PreAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/indexes/{name}/search",
                    RestServiceHandler.RouteHandler);
                _Server.Routes.PreAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/indexes/{name}/vectors",
                    RestServiceHandler.RouteHandler);
                _Server.Routes.PreAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/indexes/{name}/vectors/batch",
                    RestServiceHandler.RouteHandler);
                _Server.Routes.PreAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/indexes/{name}/vectors/{guid}",
                    RestServiceHandler.RouteHandler);

                _Server.Start();

                _Logging.Info(_Header + "web server started");
                return true;
            }
            catch (Exception ex)
            {
                _Logging?.Warn(_Header + $"failed to start web server: {ex.Message}");
                return false;
            }
        }

        private static async Task PostRoutingHandler(HttpContextBase ctx)
        {
            ctx.Timestamp.End = DateTime.UtcNow;
                
            _Logging?.Info(
                _Header
                + ctx.Request.Source.IpAddress + " " + ctx.Request.Method.ToString() + " " +
                ctx.Request.Url.RawWithoutQuery
                + " " + ctx.Response.StatusCode
                + " (" + ctx.Timestamp.TotalMs?.ToString("F2") + "ms)");
        }

        private static async Task DefaultRoute(HttpContextBase ctx)
        {
            ArgumentNullException.ThrowIfNull(ctx);

            if (_Settings.Debug.HttpRequests)
            {
                string msg =
                    $"{ctx.Request.Source.IpAddress}:{ctx.Request.Source.Port} {ctx.Request.Method} {ctx.Request.Url.RawWithoutQuery}";
                _Logging?.Debug(_Header + $"{msg}");
            }

            // Add CORS headers
            ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            ctx.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
            ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, x-api-key");

            ApiErrorResponse errorResponse = new ApiErrorResponse(ApiErrorEnum.NotFound, "Endpoint not found");
            string json = _Serializer.SerializeJson(errorResponse, true);

            ctx.Response.StatusCode = 404;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.Send(json).ConfigureAwait(false);
        }

        #endregion
    }
}