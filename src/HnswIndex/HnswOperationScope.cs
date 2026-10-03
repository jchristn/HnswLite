namespace Hnsw
{
    using System;
    using System.Diagnostics;

    /// <summary>
    /// Tracks one index operation for telemetry: a root span named "hnsw.{operation}", a child span per stage
    /// named "stage:{stage}", the operation duration and outcome, and per-stage durations.
    /// Instrumentation is best-effort and never throws. Not thread safe: one scope belongs to one operation.
    /// </summary>
    internal sealed class HnswOperationScope : IDisposable
    {
        #region Private-Members

        private readonly string _Operation;
        private readonly long _StartTimestamp;
        private readonly Activity? _Activity;
        private Activity? _StageActivity = null;
        private string? _StageName = null;
        private long _StageStartTimestamp = 0;
        private Exception? _Exception = null;
        private bool _Disposed = false;
        private bool _LockWaiting = false;
        private bool _LockHeld = false;

        #endregion

        #region Constructors-and-Factories

        private HnswOperationScope(string operation)
        {
            _Operation = operation;
            _StartTimestamp = HnswTelemetry.GetTimestamp();
            try
            {
                _Activity = HnswTelemetry.ActivitySource.StartActivity("hnsw." + operation, ActivityKind.Internal);
                _Activity?.SetTag(HnswTelemetryNames.LabelOperation, operation);
            }
            catch (Exception)
            {
                _Activity = null;
            }
        }

        internal static HnswOperationScope Start(string operation)
        {
            return new HnswOperationScope(operation);
        }

        #endregion

        #region Internal-Methods

        internal void SetTag(string key, object? value)
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

        internal void BeginStage(string stage)
        {
            EndStage(null);
            _StageName = stage;
            _StageStartTimestamp = HnswTelemetry.GetTimestamp();
            try
            {
                if (_Activity != null)
                {
                    // Prefer the ambient parent so Activity.Current is restored to the operation span when the
                    // stage stops; fall back to an explicit parent if the ambient context has drifted.
                    _StageActivity = ReferenceEquals(Activity.Current, _Activity)
                        ? HnswTelemetry.ActivitySource.StartActivity("stage:" + stage, ActivityKind.Internal)
                        : HnswTelemetry.ActivitySource.StartActivity("stage:" + stage, ActivityKind.Internal, _Activity.Context);
                    _StageActivity?.SetTag(HnswTelemetryNames.LabelStage, stage);
                }
            }
            catch (Exception)
            {
                _StageActivity = null;
            }
        }

        internal void EndStage(Exception? exception)
        {
            if (_StageName == null) return;
            HnswTelemetry.RecordStage(_Operation, _StageName, HnswTelemetry.GetElapsedSeconds(_StageStartTimestamp), exception);
            HnswTelemetry.CompleteActivity(_StageActivity, exception);
            _StageActivity = null;
            _StageName = null;
        }

        internal void BeginLockWait()
        {
            BeginStage(HnswTelemetryNames.StageQueued);
            HnswTelemetry.AddLockWaiting(_Operation, 1);
            _LockWaiting = true;
        }

        internal void LockAcquired()
        {
            if (_LockWaiting)
            {
                HnswTelemetry.AddLockWaiting(_Operation, -1);
                _LockWaiting = false;
            }

            EndStage(null);
            HnswTelemetry.AddLockHeld(_Operation, 1);
            _LockHeld = true;
        }

        internal void LockReleased()
        {
            if (!_LockHeld) return;
            HnswTelemetry.AddLockHeld(_Operation, -1);
            _LockHeld = false;
        }

        internal void Fail(Exception exception)
        {
            if (exception == null) return;
            _Exception = exception;
            EndStage(exception);
        }

        #endregion

        #region Public-Methods

        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;

            if (_LockWaiting)
            {
                HnswTelemetry.AddLockWaiting(_Operation, -1);
                _LockWaiting = false;
            }

            LockReleased();
            EndStage(_Exception);
            HnswTelemetry.RecordOperation(_Operation, HnswTelemetry.GetElapsedSeconds(_StartTimestamp), _Exception);
            HnswTelemetry.CompleteActivity(_Activity, _Exception);
        }

        #endregion
    }
}
