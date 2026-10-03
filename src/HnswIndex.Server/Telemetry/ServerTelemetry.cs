namespace HnswIndex.Server.Telemetry
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using Hnsw;
    using HnswIndex.Server.Classes;

    /// <summary>
    /// Server-level metrics and spans, emitted through the BCL on the <see cref="ServerTelemetryNames.MeterName"/>
    /// meter and <see cref="ServerTelemetryNames.ActivitySourceName"/> activity source. Covers the service layer
    /// behind Watson's routes (index and vector operations, search), authentication decisions, API errors, the
    /// startup index-reload job, inventory gauges, and build/configuration info.
    /// Instrumentation is best-effort: no method here throws.
    /// Thread safety: all members are thread safe.
    /// </summary>
    public static class ServerTelemetry
    {
        #region Public-Members

        /// <summary>
        /// The server meter. Never null.
        /// </summary>
        public static Meter Meter => _Meter;

        /// <summary>
        /// The server activity source. Never null.
        /// </summary>
        public static ActivitySource ActivitySource => _ActivitySource;

        /// <summary>
        /// The server version. Never null.
        /// </summary>
        public static string Version => _Version;

        /// <summary>
        /// Callback that returns current index and vector totals per storage type for the inventory gauges.
        /// Set once at the composition root. Null reports no inventory.
        /// </summary>
        public static Func<IEnumerable<StorageInventory>>? InventoryProvider
        {
            get { return _InventoryProvider; }
            set { _InventoryProvider = value; }
        }

        #endregion

        #region Private-Members

        private static readonly string _Version = ResolveVersion();
        private static readonly Meter _Meter = new Meter(ServerTelemetryNames.MeterName, _Version);
        private static readonly ActivitySource _ActivitySource = new ActivitySource(ServerTelemetryNames.ActivitySourceName, _Version);

        private static volatile Func<IEnumerable<StorageInventory>>? _InventoryProvider = null;
        private static long _LastReloadSuccessUnixSeconds = 0;
        private static volatile KeyValuePair<string, object?>[] _ConfigTags = Array.Empty<KeyValuePair<string, object?>>();

        private static readonly Histogram<double> _OperationDuration = _Meter.CreateHistogram<double>(
            ServerTelemetryNames.OperationDuration, "s", "Duration of server operations behind API routes.");

        private static readonly Counter<long> _Operations = _Meter.CreateCounter<long>(
            ServerTelemetryNames.Operations, "{operation}", "Server operations by outcome.");

        private static readonly Histogram<double> _StageDuration = _Meter.CreateHistogram<double>(
            ServerTelemetryNames.StageDuration, "s", "Duration of each stage of a server operation.");

        private static readonly Histogram<long> _SearchResults = _Meter.CreateHistogram<long>(
            ServerTelemetryNames.SearchResults, "{result}", "Results returned by a search after metadata filtering.");

        private static readonly Counter<long> _SearchFiltered = _Meter.CreateCounter<long>(
            ServerTelemetryNames.SearchFiltered, "{result}", "Search candidates removed by metadata filters.");

        private static readonly Counter<long> _AuthRequests = _Meter.CreateCounter<long>(
            ServerTelemetryNames.AuthRequests, "{request}", "API key authentication decisions.");

        private static readonly Counter<long> _ApiErrors = _Meter.CreateCounter<long>(
            ServerTelemetryNames.ApiErrors, "{error}", "API error responses by error code.");

        private static readonly Counter<long> _ReloadJobs = _Meter.CreateCounter<long>(
            ServerTelemetryNames.ReloadJobs, "{job}", "Startup index-reload jobs by outcome.");

        private static readonly Histogram<double> _ReloadDuration = _Meter.CreateHistogram<double>(
            ServerTelemetryNames.ReloadDuration, "s", "Duration of the startup index-reload job.");

        private static readonly Histogram<double> _ReloadStageDuration = _Meter.CreateHistogram<double>(
            ServerTelemetryNames.ReloadStageDuration, "s", "Duration of each startup index-reload stage.");

        private static readonly Counter<long> _ReloadStages = _Meter.CreateCounter<long>(
            ServerTelemetryNames.ReloadStages, "{stage}", "Startup index-reload stage executions by outcome.");

        private static readonly Counter<long> _ReloadIndexes = _Meter.CreateCounter<long>(
            ServerTelemetryNames.ReloadIndexes, "{index}", "Indexes processed by the startup reload job, by result.");

        private static readonly ObservableGauge<long> _IndexesGauge = _Meter.CreateObservableGauge<long>(
            ServerTelemetryNames.Indexes, ObserveIndexes, "{index}", "Indexes currently loaded, by storage type.");

        private static readonly ObservableGauge<long> _VectorsGauge = _Meter.CreateObservableGauge<long>(
            ServerTelemetryNames.Vectors, ObserveVectors, "{vector}", "Vectors held by loaded indexes, by storage type.");

        private static readonly ObservableGauge<long> _ReloadLastSuccessGauge = _Meter.CreateObservableGauge<long>(
            ServerTelemetryNames.ReloadLastSuccess, ObserveReloadLastSuccess, "s", "Unix time of the last successful index-reload job; 0 when none.");

        private static readonly ObservableGauge<int> _BuildInfoGauge = _Meter.CreateObservableGauge<int>(
            ServerTelemetryNames.BuildInfo, ObserveBuildInfo, "{info}", "Build information; the value is always 1.");

        private static readonly ObservableGauge<int> _ConfigInfoGauge = _Meter.CreateObservableGauge<int>(
            ServerTelemetryNames.ConfigInfo, ObserveConfigInfo, "{info}", "Safe configuration values; the value is always 1.");

        #endregion

        #region Public-Methods

        /// <summary>
        /// Normalize a configured storage type to a bounded label value.
        /// </summary>
        /// <param name="storageType">The configured storage type. May be null.</param>
        /// <returns>postgresql, sqlite, ram, or unknown.</returns>
        public static string NormalizeStorageType(string? storageType)
        {
            if (string.IsNullOrWhiteSpace(storageType)) return ServerTelemetryNames.StorageUnknown;
            if (string.Equals(storageType, "PostgreSQL", StringComparison.OrdinalIgnoreCase)
                || string.Equals(storageType, "Postgres", StringComparison.OrdinalIgnoreCase)) return ServerTelemetryNames.StoragePostgresql;
            if (string.Equals(storageType, "SQLite", StringComparison.OrdinalIgnoreCase)) return ServerTelemetryNames.StorageSqlite;
            if (string.Equals(storageType, "RAM", StringComparison.OrdinalIgnoreCase)) return ServerTelemetryNames.StorageRam;
            return ServerTelemetryNames.StorageUnknown;
        }

        /// <summary>
        /// Capture the safe (non-secret) configuration values reported by the config info gauge.
        /// No credentials, connection strings, or keys are captured.
        /// </summary>
        /// <param name="settings">Server settings. Null is ignored.</param>
        public static void SetConfiguration(HnswIndexSettings? settings)
        {
            if (settings == null) return;
            try
            {
                _ConfigTags = new KeyValuePair<string, object?>[]
                {
                    new KeyValuePair<string, object?>(ServerTelemetryNames.LabelConfigDefaultStorageType, NormalizeStorageType(settings.Storage.DefaultStorageType)),
                    new KeyValuePair<string, object?>(ServerTelemetryNames.LabelConfigRequireAuthentication, settings.Server.RequireAuthentication ? "true" : "false"),
                    new KeyValuePair<string, object?>(ServerTelemetryNames.LabelConfigCorsEnabled, settings.Cors.Enable ? "true" : "false"),
                    new KeyValuePair<string, object?>(ServerTelemetryNames.LabelConfigOtlpEnabled, settings.Telemetry.Enable && settings.Telemetry.OtlpEnable ? "true" : "false"),
                    new KeyValuePair<string, object?>(ServerTelemetryNames.LabelConfigPrometheusEnabled, settings.Telemetry.Enable && settings.Telemetry.PrometheusEnable ? "true" : "false"),
                };
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        /// <summary>
        /// Record an API key authentication decision.
        /// </summary>
        /// <param name="result">The result (see the Auth* constants in <see cref="ServerTelemetryNames"/>).</param>
        public static void RecordAuth(string result)
        {
            try
            {
                if (!_AuthRequests.Enabled) return;
                _AuthRequests.Add(1, new KeyValuePair<string, object?>(ServerTelemetryNames.LabelAuthResult, result));
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        /// <summary>
        /// Record an API error response and tag the current request span with the error code.
        /// </summary>
        /// <param name="error">The error code.</param>
        public static void RecordApiError(ApiErrorEnum error)
        {
            try
            {
                string code = error.ToString();
                Activity.Current?.SetTag(ServerTelemetryNames.LabelApiError, code);
                if (!_ApiErrors.Enabled) return;
                _ApiErrors.Add(1, new KeyValuePair<string, object?>(ServerTelemetryNames.LabelApiError, code));
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        /// <summary>
        /// Record the results of a search after filtering.
        /// </summary>
        /// <param name="results">Results returned.</param>
        /// <param name="filtered">Candidates removed by metadata filters.</param>
        public static void RecordSearch(long results, long filtered)
        {
            try
            {
                if (_SearchResults.Enabled) _SearchResults.Record(results);
                if (filtered > 0 && _SearchFiltered.Enabled) _SearchFiltered.Add(filtered);
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        /// <summary>
        /// Record a completed startup reload job.
        /// </summary>
        /// <param name="seconds">Job duration in seconds.</param>
        /// <param name="exception">The failure, or null on success.</param>
        public static void RecordReloadJob(double seconds, Exception? exception)
        {
            try
            {
                string outcome = HnswTelemetry.ClassifyOutcome(exception);
                KeyValuePair<string, object?> tag = new KeyValuePair<string, object?>(ServerTelemetryNames.LabelOutcome, outcome);
                if (_ReloadJobs.Enabled) _ReloadJobs.Add(1, tag);
                if (_ReloadDuration.Enabled) _ReloadDuration.Record(seconds, tag);
                if (exception == null) Interlocked.Exchange(ref _LastReloadSuccessUnixSeconds, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        /// <summary>
        /// Record one reload stage.
        /// </summary>
        /// <param name="stage">The stage (sqlite or postgresql).</param>
        /// <param name="seconds">Stage duration in seconds.</param>
        /// <param name="exception">The failure, or null on success.</param>
        public static void RecordReloadStage(string stage, double seconds, Exception? exception)
        {
            try
            {
                TagList tags = new TagList();
                tags.Add(ServerTelemetryNames.LabelStage, stage);
                tags.Add(ServerTelemetryNames.LabelOutcome, HnswTelemetry.ClassifyOutcome(exception));
                if (_ReloadStageDuration.Enabled) _ReloadStageDuration.Record(seconds, tags);
                if (_ReloadStages.Enabled) _ReloadStages.Add(1, tags);
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        /// <summary>
        /// Record one index processed by the reload job.
        /// </summary>
        /// <param name="storageType">Normalized storage type.</param>
        /// <param name="result">loaded, skipped, or failed.</param>
        public static void RecordReloadIndex(string storageType, string result)
        {
            try
            {
                if (!_ReloadIndexes.Enabled) return;
                TagList tags = new TagList();
                tags.Add(ServerTelemetryNames.LabelStorageType, storageType);
                tags.Add(ServerTelemetryNames.LabelReloadResult, result);
                _ReloadIndexes.Add(1, tags);
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        #endregion

        #region Internal-Methods

        internal static void RecordOperation(string operation, string storageType, double seconds, Exception? exception)
        {
            try
            {
                if (!_OperationDuration.Enabled && !_Operations.Enabled) return;
                TagList tags = new TagList();
                tags.Add(ServerTelemetryNames.LabelOperation, operation);
                tags.Add(ServerTelemetryNames.LabelStorageType, storageType);
                tags.Add(ServerTelemetryNames.LabelOutcome, HnswTelemetry.ClassifyOutcome(exception));
                if (exception != null) tags.Add(ServerTelemetryNames.LabelErrorType, HnswTelemetry.GetErrorType(exception));
                _OperationDuration.Record(seconds, tags);
                _Operations.Add(1, tags);
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        internal static void RecordStage(string operation, string stage, double seconds, Exception? exception)
        {
            try
            {
                if (!_StageDuration.Enabled) return;
                TagList tags = new TagList();
                tags.Add(ServerTelemetryNames.LabelOperation, operation);
                tags.Add(ServerTelemetryNames.LabelStage, stage);
                tags.Add(ServerTelemetryNames.LabelOutcome, HnswTelemetry.ClassifyOutcome(exception));
                _StageDuration.Record(seconds, tags);
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        #endregion

        #region Private-Methods

        private static IEnumerable<Measurement<long>> ObserveIndexes()
        {
            return ObserveInventory(true);
        }

        private static IEnumerable<Measurement<long>> ObserveVectors()
        {
            return ObserveInventory(false);
        }

        private static IEnumerable<Measurement<long>> ObserveInventory(bool indexes)
        {
            List<Measurement<long>> measurements = new List<Measurement<long>>();
            try
            {
                Func<IEnumerable<StorageInventory>>? provider = _InventoryProvider;
                if (provider == null) return measurements;
                foreach (StorageInventory inventory in provider())
                {
                    measurements.Add(new Measurement<long>(
                        indexes ? inventory.IndexCount : inventory.VectorCount,
                        new KeyValuePair<string, object?>(ServerTelemetryNames.LabelStorageType, inventory.StorageType)));
                }
            }
            catch (Exception)
            {
                // best-effort
            }

            return measurements;
        }

        private static long ObserveReloadLastSuccess()
        {
            return Interlocked.Read(ref _LastReloadSuccessUnixSeconds);
        }

        private static Measurement<int> ObserveBuildInfo()
        {
            return new Measurement<int>(
                1,
                new KeyValuePair<string, object?>(ServerTelemetryNames.LabelVersion, _Version),
                new KeyValuePair<string, object?>(ServerTelemetryNames.LabelRuntime, RuntimeInformation.FrameworkDescription));
        }

        private static IEnumerable<Measurement<int>> ObserveConfigInfo()
        {
            KeyValuePair<string, object?>[] tags = _ConfigTags;
            if (tags.Length == 0) return Array.Empty<Measurement<int>>();
            return new Measurement<int>[] { new Measurement<int>(1, tags) };
        }

        private static string ResolveVersion()
        {
            try
            {
                Assembly assembly = typeof(ServerTelemetry).Assembly;
                AssemblyInformationalVersionAttribute? info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                string? version = info?.InformationalVersion ?? assembly.GetName().Version?.ToString();
                if (string.IsNullOrEmpty(version)) return "0.0.0";
                int plus = version.IndexOf('+');
                return plus > 0 ? version.Substring(0, plus) : version;
            }
            catch (Exception)
            {
                return "0.0.0";
            }
        }

        #endregion
    }
}
