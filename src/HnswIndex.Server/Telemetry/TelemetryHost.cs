namespace HnswIndex.Server.Telemetry
{
    using System;
    using System.Collections.Generic;
    using Hnsw;
    using HnswIndex.Server.Classes;
    using Radiant;
    using SyslogLogging;

    /// <summary>
    /// The single Radiant host for the server process (the composition root's telemetry pipeline). Subscribes to
    /// every meter and activity source in the process (Watson, HnswLite, HnswLite.Server, Npgsql), exports traces and
    /// metrics over OTLP, and serves an in-process Prometheus scrape endpoint.
    /// Best-effort: a start failure logs a warning and the server runs without exported telemetry.
    /// Thread safety: create and dispose from one thread; the host itself is thread safe once started.
    /// </summary>
    public sealed class TelemetryHost : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// True when a live pipeline is running and exporting.
        /// </summary>
        public bool IsEnabled => _Host != null && _Host.IsEnabled;

        /// <summary>
        /// The Prometheus scrape URL, or null when the endpoint is disabled or the host did not start.
        /// </summary>
        public string? PrometheusScrapeUrl => _PrometheusScrapeUrl;

        #endregion

        #region Private-Members

        private static readonly string _Header = "[TelemetryHost] ";
        private readonly RadiantHost? _Host = null;
        private readonly string? _PrometheusScrapeUrl = null;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        private TelemetryHost(RadiantHost? host, string? prometheusScrapeUrl)
        {
            _Host = host;
            _PrometheusScrapeUrl = prometheusScrapeUrl;
        }

        /// <summary>
        /// Start the telemetry pipeline described by <paramref name="settings"/>. Never throws: on failure the
        /// returned host is inert and a warning is logged.
        /// </summary>
        /// <param name="settings">Telemetry settings. Null or disabled settings return an inert host.</param>
        /// <param name="logging">Optional logging module.</param>
        /// <returns>The telemetry host. Never null.</returns>
        public static TelemetryHost Start(TelemetrySettings? settings, LoggingModule? logging = null)
        {
            if (settings == null || !settings.Enable)
            {
                logging?.Info(_Header + "telemetry export disabled by settings");
                return new TelemetryHost(null, null);
            }

            try
            {
                RadiantSettings radiant = BuildRadiantSettings(settings, logging);
                RadiantHost host = RadiantHost.Start(radiant);
                string? scrapeUrl = settings.PrometheusEnable ? radiant.Prometheus.ToScrapeUrl() : null;

                logging?.Info(_Header + "telemetry started for service " + settings.ServiceName
                    + (settings.OtlpEnable ? "; OTLP (" + radiant.Otlp.Protocol + ") to " + settings.OtlpEndpoint : "; OTLP disabled")
                    + (scrapeUrl != null ? "; Prometheus scrape at " + scrapeUrl : "; Prometheus endpoint disabled"));

                return new TelemetryHost(host, scrapeUrl);
            }
            catch (Exception e)
            {
                Exception root = e.GetBaseException();
                logging?.Warn(_Header + "telemetry disabled (start failed): " + e.Message
                    + (ReferenceEquals(root, e) ? "" : " Cause: " + root.GetType().Name + ": " + root.Message));
                return new TelemetryHost(null, null);
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the Radiant settings for the server. Exposed for tests and diagnostics.
        /// </summary>
        /// <param name="settings">Telemetry settings. Must not be null.</param>
        /// <param name="logging">Optional logging module for Radiant diagnostics.</param>
        /// <returns>The Radiant settings.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is null.</exception>
        public static RadiantSettings BuildRadiantSettings(TelemetrySettings settings, LoggingModule? logging = null)
        {
            ArgumentNullException.ThrowIfNull(settings);

            RadiantSettings radiant = new RadiantSettings(settings.ServiceName);
            radiant.Enable = true;
            if (logging != null) radiant.DiagnosticCallback = message => logging.Debug(_Header + message);

            radiant.Otlp.Enable = settings.OtlpEnable;
            radiant.Otlp.Endpoint = settings.OtlpEndpoint;
            radiant.Otlp.Protocol = String.Equals(settings.OtlpProtocol, "httpprotobuf", StringComparison.OrdinalIgnoreCase)
                ? OtlpProtocolEnum.HttpProtobuf
                : OtlpProtocolEnum.Grpc;

            radiant.Prometheus.Enable = settings.PrometheusEnable;
            radiant.Prometheus.Hostname = settings.PrometheusHostname;
            radiant.Prometheus.Port = settings.PrometheusPort;

            radiant.Traces.SamplingRatio = settings.TraceSamplingRatio;
            radiant.Metrics.IncludeRuntime = settings.IncludeRuntimeMetrics;
            radiant.Metrics.IncludeProcess = settings.IncludeRuntimeMetrics;

            // This service ships no background workers, so logs stay in the existing syslog/file pipeline rather
            // than an OTLP log exporter (Tempo does not accept logs).
            radiant.Logs.Enable = false;

            radiant.Sources.AddMeter(ServerTelemetryNames.WatsonSourceName);
            radiant.Sources.AddActivitySource(ServerTelemetryNames.WatsonSourceName);
            radiant.Sources.AddMeter(HnswTelemetryNames.MeterName);
            radiant.Sources.AddActivitySource(HnswTelemetryNames.ActivitySourceName);
            radiant.Sources.AddMeter(ServerTelemetryNames.MeterName);
            radiant.Sources.AddActivitySource(ServerTelemetryNames.ActivitySourceName);
            radiant.Sources.AddMeter(ServerTelemetryNames.NpgsqlSourceName);
            if (settings.TraceDatabaseCommands) radiant.Sources.AddActivitySource(ServerTelemetryNames.NpgsqlSourceName);

            radiant.Metrics.DefineAll(GetHistogramConventions());
            return radiant;
        }

        /// <summary>
        /// The histogram conventions registered with Radiant so each histogram gets bucket boundaries suited to
        /// its unit (seconds or counts) instead of the SDK's millisecond-oriented defaults.
        /// </summary>
        /// <returns>The conventions. Never null.</returns>
        public static List<Convention> GetHistogramConventions()
        {
            double[] duration = HnswTelemetry.DurationBuckets;
            double[] counts = HnswTelemetry.CountBuckets;

            return new List<Convention>
            {
                Convention.Histogram("http.server.request.duration", "s", LatencyBuckets.Default,
                    "http.request.method", "http.response.status_code", "http.route", "url.scheme", "network.protocol.version", "error.type"),
                Convention.Histogram(HnswTelemetryNames.IndexOperationDuration, "s", duration,
                    HnswTelemetryNames.LabelOperation, HnswTelemetryNames.LabelOutcome, HnswTelemetryNames.LabelErrorType),
                Convention.Histogram(HnswTelemetryNames.IndexStageDuration, "s", duration,
                    HnswTelemetryNames.LabelOperation, HnswTelemetryNames.LabelStage, HnswTelemetryNames.LabelOutcome),
                Convention.Histogram(HnswTelemetryNames.StorageOperationDuration, "s", duration,
                    HnswTelemetryNames.LabelStorageProvider, HnswTelemetryNames.LabelStorageOperation, HnswTelemetryNames.LabelOutcome, HnswTelemetryNames.LabelErrorType),
                Convention.Histogram(HnswTelemetryNames.SearchResults, "{result}", counts),
                Convention.Histogram(HnswTelemetryNames.SearchNodesEvaluated, "{node}", counts),
                Convention.Histogram(ServerTelemetryNames.OperationDuration, "s", duration,
                    ServerTelemetryNames.LabelOperation, ServerTelemetryNames.LabelStorageType, ServerTelemetryNames.LabelOutcome, ServerTelemetryNames.LabelErrorType),
                Convention.Histogram(ServerTelemetryNames.StageDuration, "s", duration,
                    ServerTelemetryNames.LabelOperation, ServerTelemetryNames.LabelStage, ServerTelemetryNames.LabelOutcome),
                Convention.Histogram(ServerTelemetryNames.SearchResults, "{result}", counts),
                Convention.Histogram(ServerTelemetryNames.ReloadDuration, "s", LatencyBuckets.Network,
                    ServerTelemetryNames.LabelOutcome),
                Convention.Histogram(ServerTelemetryNames.ReloadStageDuration, "s", LatencyBuckets.Network,
                    ServerTelemetryNames.LabelStage, ServerTelemetryNames.LabelOutcome),
            };
        }

        /// <summary>
        /// Flush exporters and release the Prometheus port. Never throws.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            try
            {
                _Host?.Dispose();
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        #endregion
    }
}
