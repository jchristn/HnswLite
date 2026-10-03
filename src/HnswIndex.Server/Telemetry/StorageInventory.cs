namespace HnswIndex.Server.Telemetry
{
    /// <summary>
    /// Index and vector totals for one storage type, read by the inventory gauges at collection time.
    /// </summary>
    public class StorageInventory
    {
        #region Public-Members

        /// <summary>
        /// Normalized storage type (see <see cref="ServerTelemetryNames"/> Storage* values).
        /// </summary>
        public string StorageType { get; set; } = ServerTelemetryNames.StorageUnknown;

        /// <summary>
        /// Number of loaded indexes using this storage type. Minimum: 0.
        /// </summary>
        public long IndexCount { get; set; } = 0;

        /// <summary>
        /// Number of vectors held by indexes using this storage type. Minimum: 0.
        /// </summary>
        public long VectorCount { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the StorageInventory class.
        /// </summary>
        public StorageInventory()
        {
        }

        #endregion
    }
}
