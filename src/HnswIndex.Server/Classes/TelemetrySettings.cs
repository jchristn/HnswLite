namespace HnswIndex.Server.Classes
{
    /// <summary>
    /// Telemetry settings: metrics and traces exported through a Radiant host (OTLP push to a collector such as
    /// Tempo, plus an in-process Prometheus scrape endpoint). Modeled on Pneuma's TelemetrySettings.
    /// All loopback defaults use 127.0.0.1 rather than localhost.
    /// </summary>
    public class TelemetrySettings
    {
        #region Public-Members

        /// <summary>
        /// Master switch. When false, no telemetry host is started and nothing is exported; the server still
        /// emits through the BCL with negligible cost. Default: true.
        /// </summary>
        public bool Enable { get; set; } = true;

        /// <summary>
        /// Service name stamped on every metric and span as service.name. Default: hnswlite-server.
        /// </summary>
        public string ServiceName
        {
            get { return _ServiceName; }
            set { _ServiceName = String.IsNullOrWhiteSpace(value) ? throw new ArgumentNullException(nameof(value)) : value; }
        }

        /// <summary>
        /// Enable OTLP push export of traces and metrics. Default: true.
        /// </summary>
        public bool OtlpEnable { get; set; } = true;

        /// <summary>
        /// OTLP collector endpoint (Tempo or an OpenTelemetry Collector). Use the gRPC port (4317) with the
        /// "grpc" protocol, or the HTTP port (4318) with "httpprotobuf". Default: http://127.0.0.1:4317.
        /// </summary>
        public string OtlpEndpoint
        {
            get { return _OtlpEndpoint; }
            set { _OtlpEndpoint = String.IsNullOrWhiteSpace(value) ? throw new ArgumentNullException(nameof(value)) : value; }
        }

        /// <summary>
        /// OTLP wire protocol: "grpc" (default) or "httpprotobuf".
        /// </summary>
        public string OtlpProtocol
        {
            get { return _OtlpProtocol; }
            set { _OtlpProtocol = String.IsNullOrWhiteSpace(value) ? "grpc" : value; }
        }

        /// <summary>
        /// Enable the in-process Prometheus scrape endpoint. It serves every subscribed meter, including Watson's
        /// HTTP metrics, the HnswLite library metrics, and the server metrics. Default: true.
        /// </summary>
        public bool PrometheusEnable { get; set; } = true;

        /// <summary>
        /// Hostname the Prometheus endpoint binds to. Default: 127.0.0.1 (loopback only). Inside a container, set
        /// it to the container's own DNS name (for example hnswlite-server in compose) so Prometheus can reach it
        /// on the internal network. Wildcards (*, +) and 0.0.0.0 are rejected by the underlying listener in
        /// Radiant 0.1.2. Never expose the endpoint on a public interface.
        /// </summary>
        public string PrometheusHostname
        {
            get { return _PrometheusHostname; }
            set { _PrometheusHostname = String.IsNullOrWhiteSpace(value) ? throw new ArgumentNullException(nameof(value)) : value; }
        }

        /// <summary>
        /// TCP port of the Prometheus endpoint. Minimum: 1, maximum: 65535, default: 9464.
        /// </summary>
        public int PrometheusPort
        {
            get { return _PrometheusPort; }
            set { _PrometheusPort = (value < 1 || value > 65535) ? throw new ArgumentOutOfRangeException(nameof(value), "PrometheusPort must be between 1 and 65535.") : value; }
        }

        /// <summary>
        /// Head-based trace sampling ratio. Minimum: 0.0 (no traces), maximum: 1.0 (every trace), default: 1.0.
        /// </summary>
        public double TraceSamplingRatio
        {
            get { return _TraceSamplingRatio; }
            set { _TraceSamplingRatio = (value < 0 || value > 1 || double.IsNaN(value)) ? throw new ArgumentOutOfRangeException(nameof(value), "TraceSamplingRatio must be between 0.0 and 1.0.") : value; }
        }

        /// <summary>
        /// Include .NET runtime and process metrics (GC, heap, thread pool, working set). Default: true.
        /// </summary>
        public bool IncludeRuntimeMetrics { get; set; } = true;

        /// <summary>
        /// Subscribe to the Npgsql driver's per-command spans. Off by default because index builds issue many
        /// small commands; the HnswLite storage metrics and spans already attribute PostgreSQL time.
        /// Npgsql connection-pool metrics are always collected. Default: false.
        /// </summary>
        public bool TraceDatabaseCommands { get; set; } = false;

        #endregion

        #region Private-Members

        private string _ServiceName = "hnswlite-server";
        private string _OtlpEndpoint = "http://127.0.0.1:4317";
        private string _OtlpProtocol = "grpc";
        private string _PrometheusHostname = "127.0.0.1";
        private int _PrometheusPort = 9464;
        private double _TraceSamplingRatio = 1.0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the TelemetrySettings class.
        /// </summary>
        public TelemetrySettings()
        {
        }

        #endregion
    }
}
