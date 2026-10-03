namespace HnswLite.Test.Shared
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Linq;
    using Hnsw;
    using HnswIndex.Server.Telemetry;

    /// <summary>
    /// In-memory telemetry listener for tests. Captures every measurement on the HnswLite and HnswLite.Server meters
    /// through a BCL <see cref="MeterListener"/>, and every span on the HnswLite activity sources through an
    /// <see cref="ActivityListener"/>. Spans are scoped to this capture by starting a root test activity, so only
    /// spans in the same trace are reported even if other code emits concurrently.
    /// Dispose to stop listening.
    /// </summary>
    public sealed class TelemetryCapture : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Activity source used for the root test span.
        /// </summary>
        public static readonly ActivitySource TestSource = new ActivitySource("HnswLite.Tests");

        /// <summary>
        /// The root test activity. Every captured span descends from it.
        /// </summary>
        public Activity? Root => _Root;

        #endregion

        #region Private-Members

        private static readonly HashSet<string> _Sources = new HashSet<string>(StringComparer.Ordinal)
        {
            HnswTelemetryNames.MeterName,
            ServerTelemetryNames.MeterName,
            "HnswLite.Tests",
        };

        private readonly MeterListener _MeterListener = new MeterListener();
        private readonly ActivityListener _ActivityListener;
        private readonly ConcurrentQueue<CapturedMeasurement> _Measurements = new ConcurrentQueue<CapturedMeasurement>();
        private readonly ConcurrentQueue<Activity> _Activities = new ConcurrentQueue<Activity>();
        private readonly Activity? _Root;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Start capturing.
        /// </summary>
        /// <param name="rootName">Name of the root test span.</param>
        public TelemetryCapture(string rootName = "test")
        {
            _MeterListener.InstrumentPublished = (instrument, listener) =>
            {
                if (_Sources.Contains(instrument.Meter.Name)) listener.EnableMeasurementEvents(instrument);
            };
            _MeterListener.SetMeasurementEventCallback<long>((i, v, t, s) => Add(i, v, t));
            _MeterListener.SetMeasurementEventCallback<int>((i, v, t, s) => Add(i, v, t));
            _MeterListener.SetMeasurementEventCallback<double>((i, v, t, s) => Add(i, v, t));
            _MeterListener.Start();

            _ActivityListener = new ActivityListener
            {
                ShouldListenTo = source => _Sources.Contains(source.Name),
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => _Activities.Enqueue(activity),
            };
            ActivitySource.AddActivityListener(_ActivityListener);

            _Root = TestSource.StartActivity(rootName, ActivityKind.Internal);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Poll observable instruments (gauges) so their current values are captured.
        /// </summary>
        public void CollectObservables()
        {
            _MeterListener.RecordObservableInstruments();
        }

        /// <summary>
        /// All measurements captured for an instrument.
        /// </summary>
        /// <param name="instrument">Instrument name.</param>
        /// <returns>Measurements, oldest first.</returns>
        public List<CapturedMeasurement> Measurements(string instrument)
        {
            return _Measurements.Where(m => m.Instrument == instrument).ToList();
        }

        /// <summary>
        /// Measurements for an instrument that satisfy a predicate.
        /// </summary>
        /// <param name="instrument">Instrument name.</param>
        /// <param name="predicate">Filter.</param>
        /// <returns>Matching measurements.</returns>
        public List<CapturedMeasurement> Measurements(string instrument, Func<CapturedMeasurement, bool> predicate)
        {
            return _Measurements.Where(m => m.Instrument == instrument && predicate(m)).ToList();
        }

        /// <summary>
        /// Sum of values for an instrument that satisfy a predicate.
        /// </summary>
        /// <param name="instrument">Instrument name.</param>
        /// <param name="predicate">Filter.</param>
        /// <returns>The sum.</returns>
        public double Sum(string instrument, Func<CapturedMeasurement, bool> predicate)
        {
            return Measurements(instrument, predicate).Sum(m => m.Value);
        }

        /// <summary>
        /// Stopped spans that belong to this capture's trace (descendants of the root test span).
        /// </summary>
        /// <returns>Spans in stop order.</returns>
        public List<Activity> Spans()
        {
            if (_Root == null) return new List<Activity>();
            ActivityTraceId traceId = _Root.TraceId;
            return _Activities.Where(a => a.TraceId == traceId && !ReferenceEquals(a, _Root)).ToList();
        }

        /// <summary>
        /// Stopped spans with the given name in this capture's trace.
        /// </summary>
        /// <param name="name">Span (display) name.</param>
        /// <returns>Matching spans.</returns>
        public List<Activity> Spans(string name)
        {
            return Spans().Where(a => a.DisplayName == name).ToList();
        }

        /// <summary>
        /// Every stopped span with the given name on any trace, for work that deliberately starts a new root
        /// (for example the startup reload job).
        /// </summary>
        /// <param name="name">Span (display) name.</param>
        /// <returns>Matching spans.</returns>
        public List<Activity> AllSpans(string name)
        {
            return _Activities.Where(a => a.DisplayName == name).ToList();
        }

        /// <summary>
        /// Return true when <paramref name="child"/> descends from <paramref name="ancestor"/> among the captured spans.
        /// </summary>
        /// <param name="child">The candidate descendant.</param>
        /// <param name="ancestor">The candidate ancestor.</param>
        /// <returns>True when related.</returns>
        public bool IsDescendant(Activity child, Activity ancestor)
        {
            Dictionary<ActivitySpanId, Activity> bySpan = _Activities.GroupBy(a => a.SpanId).ToDictionary(g => g.Key, g => g.First());
            if (_Root != null) bySpan[_Root.SpanId] = _Root;
            ActivitySpanId current = child.ParentSpanId;
            int guard = 0;
            while (current != default && guard++ < 1000)
            {
                if (current == ancestor.SpanId) return true;
                if (!bySpan.TryGetValue(current, out Activity? parent)) return false;
                current = parent.ParentSpanId;
            }

            return false;
        }

        /// <summary>
        /// Stop listening and end the root span.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            _Root?.Stop();
            _MeterListener.Dispose();
            _ActivityListener.Dispose();
        }

        #endregion

        #region Private-Methods

        private void Add<T>(Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags) where T : struct
        {
            Dictionary<string, string?> map = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> tag in tags) map[tag.Key] = tag.Value?.ToString();
            _Measurements.Enqueue(new CapturedMeasurement(instrument.Name, instrument.Meter.Name, Convert.ToDouble(value), map));
        }

        #endregion
    }
}
