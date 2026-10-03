namespace HnswIndex.Server.Telemetry
{
    using System;
    using System.Diagnostics;
    using Hnsw;

    /// <summary>
    /// Tracks one server operation: a span named "hnswlite {operation}" (a child of Watson's request span), a child
    /// span per stage named "stage:{stage}", the operation duration and outcome, and per-stage durations.
    /// Best-effort; never throws. Not thread safe: one scope belongs to one operation.
    /// </summary>
    public sealed class ServerOperationScope : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// The operation span, or null when no listener is sampling.
        /// </summary>
        public Activity? Activity => _Activity;

        /// <summary>
        /// Normalized storage type used as the metric label. Default: unknown.
        /// </summary>
        public string StorageType
        {
            get { return _StorageType; }
            set { _StorageType = ServerTelemetry.NormalizeStorageType(value); }
        }

        #endregion

        #region Private-Members

        private readonly string _Operation;
        private readonly long _StartTimestamp;
        private readonly Activity? _Activity;
        private string _StorageType = ServerTelemetryNames.StorageUnknown;
        private Activity? _StageActivity = null;
        private string? _StageName = null;
        private long _StageStartTimestamp = 0;
        private Exception? _Exception = null;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        private ServerOperationScope(string operation, ActivityKind kind)
        {
            _Operation = operation;
            _StartTimestamp = HnswTelemetry.GetTimestamp();
            try
            {
                _Activity = ServerTelemetry.ActivitySource.StartActivity("hnswlite " + operation, kind);
                _Activity?.SetTag(ServerTelemetryNames.LabelOperation, operation);
            }
            catch (Exception)
            {
                _Activity = null;
            }
        }

        /// <summary>
        /// Start tracking an operation.
        /// </summary>
        /// <param name="operation">The operation name (see the Operation* constants in <see cref="ServerTelemetryNames"/>).</param>
        /// <param name="indexName">Optional index name, recorded on the span only.</param>
        /// <returns>The scope. Dispose it when the operation ends.</returns>
        public static ServerOperationScope Start(string operation, string? indexName = null)
        {
            ServerOperationScope scope = new ServerOperationScope(operation, ActivityKind.Internal);
            if (indexName != null) scope.SetTag(ServerTelemetryNames.AttributeIndexName, indexName);
            return scope;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Set an attribute on the operation span. High-cardinality values are allowed here (spans only).
        /// </summary>
        /// <param name="key">Attribute key.</param>
        /// <param name="value">Attribute value.</param>
        public void SetTag(string key, object? value)
        {
            if (_Activity == null) return;
            try
            {
                _Activity.SetTag(key, value);
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        /// <summary>
        /// End the current stage (if any) and begin a new one.
        /// </summary>
        /// <param name="stage">The stage name.</param>
        public void BeginStage(string stage)
        {
            EndStage(null);
            _StageName = stage;
            _StageStartTimestamp = HnswTelemetry.GetTimestamp();
            try
            {
                if (_Activity != null)
                {
                    _StageActivity = ReferenceEquals(Activity.Current, _Activity)
                        ? ServerTelemetry.ActivitySource.StartActivity("stage:" + stage, ActivityKind.Internal)
                        : ServerTelemetry.ActivitySource.StartActivity("stage:" + stage, ActivityKind.Internal, _Activity.Context);
                    _StageActivity?.SetTag(ServerTelemetryNames.LabelStage, stage);
                }
            }
            catch (Exception)
            {
                _StageActivity = null;
            }
        }

        /// <summary>
        /// End the current stage, if any.
        /// </summary>
        /// <param name="exception">The failure that ended the stage, or null.</param>
        public void EndStage(Exception? exception)
        {
            if (_StageName == null) return;
            ServerTelemetry.RecordStage(_Operation, _StageName, HnswTelemetry.GetElapsedSeconds(_StageStartTimestamp), exception);
            HnswTelemetry.CompleteActivity(_StageActivity, exception);
            _StageActivity = null;
            _StageName = null;
        }

        /// <summary>
        /// Mark the operation as failed. The outcome is recorded when the scope is disposed.
        /// </summary>
        /// <param name="exception">The failure.</param>
        public void Fail(Exception exception)
        {
            if (exception == null) return;
            _Exception = exception;
            EndStage(exception);
        }

        /// <summary>
        /// Record the operation duration and outcome and end the span.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            EndStage(_Exception);
            SetTag(ServerTelemetryNames.LabelStorageType, _StorageType);
            ServerTelemetry.RecordOperation(_Operation, _StorageType, HnswTelemetry.GetElapsedSeconds(_StartTimestamp), _Exception);
            HnswTelemetry.CompleteActivity(_Activity, _Exception);
        }

        #endregion
    }
}
