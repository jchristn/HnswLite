namespace HnswLite.Test.Shared
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One metric measurement captured by <see cref="TelemetryCapture"/>.
    /// </summary>
    public sealed class CapturedMeasurement
    {
        #region Public-Members

        /// <summary>
        /// Instrument name, for example hnswlite.index.operations.
        /// </summary>
        public string Instrument { get; }

        /// <summary>
        /// Meter name the instrument belongs to.
        /// </summary>
        public string Meter { get; }

        /// <summary>
        /// Measured value, widened to double.
        /// </summary>
        public double Value { get; }

        /// <summary>
        /// Label (tag) values keyed by label key. Values are rendered with ToString. Never null.
        /// </summary>
        public IReadOnlyDictionary<string, string?> Tags { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create a captured measurement.
        /// </summary>
        /// <param name="instrument">Instrument name.</param>
        /// <param name="meter">Meter name.</param>
        /// <param name="value">Measured value.</param>
        /// <param name="tags">Label values.</param>
        public CapturedMeasurement(string instrument, string meter, double value, IReadOnlyDictionary<string, string?> tags)
        {
            Instrument = instrument ?? throw new ArgumentNullException(nameof(instrument));
            Meter = meter ?? throw new ArgumentNullException(nameof(meter));
            Value = value;
            Tags = tags ?? throw new ArgumentNullException(nameof(tags));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Return true when the measurement carries the label with the given value.
        /// </summary>
        /// <param name="key">Label key.</param>
        /// <param name="value">Expected value.</param>
        /// <returns>True on match.</returns>
        public bool Has(string key, string value)
        {
            return Tags.TryGetValue(key, out string? actual) && string.Equals(actual, value, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, string?> kvp in Tags) parts.Add(kvp.Key + "=" + kvp.Value);
            return Instrument + "{" + string.Join(",", parts) + "} " + Value;
        }

        #endregion
    }
}
