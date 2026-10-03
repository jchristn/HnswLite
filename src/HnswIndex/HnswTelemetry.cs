namespace Hnsw
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Reflection;

    /// <summary>
    /// Metrics and tracing for the HnswLite libraries, emitted through the .NET base class library
    /// (<see cref="System.Diagnostics.Metrics.Meter"/> and <see cref="System.Diagnostics.ActivitySource"/>).
    /// No exporter or SDK is referenced: nothing is collected until a host subscribes to
    /// <see cref="HnswTelemetryNames.MeterName"/> and <see cref="HnswTelemetryNames.ActivitySourceName"/>,
    /// and emission with no listener is effectively free.
    /// Instrumentation is best-effort: every public method here swallows its own failures and never throws.
    /// Thread safety: all members are thread safe.
    /// </summary>
    public static class HnswTelemetry
    {
        #region Public-Members

        /// <summary>
        /// The library meter, named <see cref="HnswTelemetryNames.MeterName"/>. Never null.
        /// </summary>
        public static Meter Meter => _Meter;

        /// <summary>
        /// The library activity source, named <see cref="HnswTelemetryNames.ActivitySourceName"/>. Never null.
        /// </summary>
        public static ActivitySource ActivitySource => _ActivitySource;

        /// <summary>
        /// The library version stamped on the meter and activity source. Never null.
        /// </summary>
        public static string Version => _Version;

        /// <summary>
        /// Recommended histogram bucket boundaries, in seconds, for the duration histograms.
        /// Hosts that cannot honor instrument advice (for example on .NET 8) should apply these through a view.
        /// Returns a defensive copy.
        /// </summary>
        public static double[] DurationBuckets => (double[])_DurationBuckets.Clone();

        /// <summary>
        /// Recommended histogram bucket boundaries for the count histograms (search results and nodes evaluated).
        /// Returns a defensive copy.
        /// </summary>
        public static double[] CountBuckets => (double[])_CountBuckets.Clone();

        /// <summary>
        /// True when a listener is collecting storage operation metrics. Storage providers may use it to skip
        /// timestamp collection when nobody is listening.
        /// </summary>
        public static bool IsStorageObserved => _StorageDuration.Enabled || _StorageOperations.Enabled;

        #endregion

        #region Private-Members

        private static readonly double[] _DurationBuckets = new double[]
        {
            0.0001, 0.00025, 0.0005, 0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 120
        };

        private static readonly double[] _CountBuckets = new double[]
        {
            0, 1, 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000, 25000, 50000, 100000
        };

        private static readonly string _Version = ResolveVersion();
        private static readonly Meter _Meter = new Meter(HnswTelemetryNames.MeterName, _Version);
        private static readonly ActivitySource _ActivitySource = new ActivitySource(HnswTelemetryNames.ActivitySourceName, _Version);

        private static readonly Histogram<double> _OperationDuration = CreateHistogram<double>(
            HnswTelemetryNames.IndexOperationDuration, "s", "Duration of HNSW index operations.", _DurationBuckets);

        private static readonly Counter<long> _Operations = _Meter.CreateCounter<long>(
            HnswTelemetryNames.IndexOperations, "{operation}", "HNSW index operations by outcome.");

        private static readonly Histogram<double> _StageDuration = CreateHistogram<double>(
            HnswTelemetryNames.IndexStageDuration, "s", "Duration of each stage of an HNSW index operation, including time queued for the index write lock.", _DurationBuckets);

        private static readonly Counter<long> _Vectors = _Meter.CreateCounter<long>(
            HnswTelemetryNames.IndexVectors, "{vector}", "Vectors added, removed, or imported.");

        private static readonly UpDownCounter<long> _LockWaiting = _Meter.CreateUpDownCounter<long>(
            HnswTelemetryNames.IndexLockWaiting, "{operation}", "Write operations waiting for the index write lock.");

        private static readonly UpDownCounter<long> _LockHeld = _Meter.CreateUpDownCounter<long>(
            HnswTelemetryNames.IndexLockHeld, "{operation}", "Write operations holding the index write lock (capacity one per index).");

        private static readonly Histogram<long> _SearchResults = CreateHistogram<long>(
            HnswTelemetryNames.SearchResults, "{result}", "Results returned by a top-K search.", _CountBuckets);

        private static readonly Histogram<long> _SearchNodesEvaluated = CreateHistogram<long>(
            HnswTelemetryNames.SearchNodesEvaluated, "{node}", "Graph nodes whose distance to the query was evaluated by a search.", _CountBuckets);

        private static readonly Counter<long> _SearchCacheRequests = _Meter.CreateCounter<long>(
            HnswTelemetryNames.SearchCacheRequests, "{request}", "Node lookups served by the per-operation search cache, by hit or miss.");

        private static readonly Histogram<double> _StorageDuration = CreateHistogram<double>(
            HnswTelemetryNames.StorageOperationDuration, "s", "Duration of storage provider operations.", _DurationBuckets);

        private static readonly Counter<long> _StorageOperations = _Meter.CreateCounter<long>(
            HnswTelemetryNames.StorageOperations, "{operation}", "Storage provider operations by outcome.");

        private static readonly Counter<long> _StorageTransactions = _Meter.CreateCounter<long>(
            HnswTelemetryNames.StorageTransactions, "{transaction}", "Storage transactions by outcome.");

        #endregion

        #region Public-Methods

        /// <summary>
        /// Capture a high-resolution timestamp to pass to the Record* methods.
        /// </summary>
        /// <returns>The current <see cref="Stopwatch"/> timestamp.</returns>
        public static long GetTimestamp()
        {
            return Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// Compute the elapsed seconds since a timestamp captured by <see cref="GetTimestamp"/>.
        /// </summary>
        /// <param name="startTimestamp">The start timestamp.</param>
        /// <returns>Elapsed seconds; never negative.</returns>
        public static double GetElapsedSeconds(long startTimestamp)
        {
            long delta = Stopwatch.GetTimestamp() - startTimestamp;
            if (delta < 0) return 0;
            return (double)delta / Stopwatch.Frequency;
        }

        /// <summary>
        /// Classify an exception into a bounded outcome value.
        /// </summary>
        /// <param name="exception">The exception, or null for success.</param>
        /// <returns><see cref="HnswTelemetryNames.OutcomeSuccess"/>, <see cref="HnswTelemetryNames.OutcomeCancelled"/>, or <see cref="HnswTelemetryNames.OutcomeError"/>.</returns>
        public static string ClassifyOutcome(Exception? exception)
        {
            if (exception == null) return HnswTelemetryNames.OutcomeSuccess;
            if (exception is OperationCanceledException) return HnswTelemetryNames.OutcomeCancelled;
            return HnswTelemetryNames.OutcomeError;
        }

        /// <summary>
        /// Return the bounded error.type value for an exception: its full type name.
        /// </summary>
        /// <param name="exception">The exception. May be null.</param>
        /// <returns>The full type name, or null when <paramref name="exception"/> is null.</returns>
        public static string? GetErrorType(Exception? exception)
        {
            if (exception == null) return null;
            Type type = exception.GetType();
            return type.FullName ?? type.Name;
        }

        /// <summary>
        /// Start a client span for a storage provider operation, named "{provider} {operation}".
        /// Returns null when no listener is sampling, so callers should use the null-conditional operator.
        /// Never throws.
        /// </summary>
        /// <param name="provider">The provider name (see the Provider* constants in <see cref="HnswTelemetryNames"/>).</param>
        /// <param name="operation">The operation name, for example AddNodes.</param>
        /// <returns>The started activity, or null.</returns>
        public static Activity? StartStorageActivity(string provider, string operation)
        {
            try
            {
                if (!_ActivitySource.HasListeners()) return null;
                Activity? activity = _ActivitySource.StartActivity(provider + " " + operation, ActivityKind.Client);
                if (activity == null) return null;
                activity.SetTag(HnswTelemetryNames.AttributeDbSystemName, provider);
                activity.SetTag(HnswTelemetryNames.AttributeDbOperationName, operation);
                activity.SetTag(HnswTelemetryNames.LabelStorageProvider, provider);
                activity.SetTag(HnswTelemetryNames.LabelStorageOperation, operation);
                return activity;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Set the final status of an activity, record the exception (if any) as a span event, and stop it.
        /// Safe to call with a null activity. Never throws.
        /// </summary>
        /// <param name="activity">The activity, or null.</param>
        /// <param name="exception">The exception that failed the work, or null on success.</param>
        public static void CompleteActivity(Activity? activity, Exception? exception)
        {
            if (activity == null) return;
            try
            {
                if (exception == null)
                {
                    activity.SetStatus(ActivityStatusCode.Ok);
                }
                else
                {
                    activity.SetStatus(ActivityStatusCode.Error, exception.Message);
                    activity.SetTag(HnswTelemetryNames.LabelErrorType, GetErrorType(exception));
                    activity.AddEvent(BuildExceptionEvent(exception));
                }

                activity.Stop();
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        /// <summary>
        /// Record the duration and outcome of a storage provider operation. Never throws.
        /// </summary>
        /// <param name="provider">The provider name (see the Provider* constants in <see cref="HnswTelemetryNames"/>).</param>
        /// <param name="operation">The operation name. Must come from a bounded, code-defined set (for example a method name).</param>
        /// <param name="startTimestamp">The timestamp captured by <see cref="GetTimestamp"/> when the operation began.</param>
        /// <param name="exception">The exception that failed the operation, or null on success.</param>
        public static void RecordStorageOperation(string provider, string operation, long startTimestamp, Exception? exception = null)
        {
            try
            {
                if (!_StorageDuration.Enabled && !_StorageOperations.Enabled) return;

                TagList tags = new TagList();
                tags.Add(HnswTelemetryNames.LabelStorageProvider, provider);
                tags.Add(HnswTelemetryNames.LabelStorageOperation, operation);
                tags.Add(HnswTelemetryNames.LabelOutcome, ClassifyOutcome(exception));
                if (exception != null) tags.Add(HnswTelemetryNames.LabelErrorType, GetErrorType(exception));

                _StorageDuration.Record(GetElapsedSeconds(startTimestamp), tags);
                _StorageOperations.Add(1, tags);
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        /// <summary>
        /// Record the outcome of a storage transaction. Never throws.
        /// </summary>
        /// <param name="provider">The provider name.</param>
        /// <param name="outcome">The outcome (see the Transaction* constants in <see cref="HnswTelemetryNames"/>).</param>
        public static void RecordStorageTransaction(string provider, string outcome)
        {
            try
            {
                if (!_StorageTransactions.Enabled) return;
                TagList tags = new TagList();
                tags.Add(HnswTelemetryNames.LabelStorageProvider, provider);
                tags.Add(HnswTelemetryNames.LabelTransactionOutcome, outcome);
                _StorageTransactions.Add(1, tags);
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        #endregion

        #region Internal-Methods

        internal static void RecordOperation(string operation, double seconds, Exception? exception)
        {
            try
            {
                if (!_OperationDuration.Enabled && !_Operations.Enabled) return;

                TagList tags = new TagList();
                tags.Add(HnswTelemetryNames.LabelOperation, operation);
                tags.Add(HnswTelemetryNames.LabelOutcome, ClassifyOutcome(exception));
                if (exception != null) tags.Add(HnswTelemetryNames.LabelErrorType, GetErrorType(exception));

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
                tags.Add(HnswTelemetryNames.LabelOperation, operation);
                tags.Add(HnswTelemetryNames.LabelStage, stage);
                tags.Add(HnswTelemetryNames.LabelOutcome, ClassifyOutcome(exception));
                _StageDuration.Record(seconds, tags);
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        internal static void RecordVectors(string operation, long count)
        {
            try
            {
                if (count <= 0 || !_Vectors.Enabled) return;
                _Vectors.Add(count, new KeyValuePair<string, object?>(HnswTelemetryNames.LabelOperation, operation));
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        internal static void AddLockWaiting(string operation, long delta)
        {
            try
            {
                if (!_LockWaiting.Enabled) return;
                _LockWaiting.Add(delta, new KeyValuePair<string, object?>(HnswTelemetryNames.LabelOperation, operation));
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        internal static void AddLockHeld(string operation, long delta)
        {
            try
            {
                if (!_LockHeld.Enabled) return;
                _LockHeld.Add(delta, new KeyValuePair<string, object?>(HnswTelemetryNames.LabelOperation, operation));
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        internal static void RecordSearch(long results, long nodesEvaluated, long cacheHits, long cacheMisses)
        {
            try
            {
                if (_SearchResults.Enabled) _SearchResults.Record(results);
                if (_SearchNodesEvaluated.Enabled) _SearchNodesEvaluated.Record(nodesEvaluated);
                if (_SearchCacheRequests.Enabled)
                {
                    if (cacheHits > 0) _SearchCacheRequests.Add(cacheHits, new KeyValuePair<string, object?>(HnswTelemetryNames.LabelCacheResult, HnswTelemetryNames.CacheHit));
                    if (cacheMisses > 0) _SearchCacheRequests.Add(cacheMisses, new KeyValuePair<string, object?>(HnswTelemetryNames.LabelCacheResult, HnswTelemetryNames.CacheMiss));
                }
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        internal static ActivityEvent BuildExceptionEvent(Exception exception)
        {
            ActivityTagsCollection tags = new ActivityTagsCollection();
            tags["exception.type"] = GetErrorType(exception);
            tags["exception.message"] = exception.Message;
            tags["exception.stacktrace"] = exception.ToString();
            return new ActivityEvent("exception", DateTimeOffset.UtcNow, tags);
        }

        #endregion

        #region Private-Methods

        private static Histogram<T> CreateHistogram<T>(string name, string unit, string description, double[] buckets) where T : struct
        {
#if NET9_0_OR_GREATER
            List<T> boundaries = new List<T>(buckets.Length);
            foreach (double b in buckets) boundaries.Add((T)Convert.ChangeType(b, typeof(T), System.Globalization.CultureInfo.InvariantCulture));
            InstrumentAdvice<T> advice = new InstrumentAdvice<T> { HistogramBucketBoundaries = boundaries };
            return _Meter.CreateHistogram<T>(name, unit, description, null, advice);
#else
            return _Meter.CreateHistogram<T>(name, unit, description);
#endif
        }

        private static string ResolveVersion()
        {
            try
            {
                Assembly assembly = typeof(HnswTelemetry).Assembly;
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
