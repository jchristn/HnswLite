namespace HnswIndex.Server.Classes
{
    /// <summary>
    /// Debug configuration settings.
    /// </summary>
    public class DebugSettings
    {
        #region Public-Members

        /// <summary>
        /// Enable or disable HTTP request debugging.
        /// </summary>
        public bool HttpRequests { get; set; } = false;

        /// <summary>
        /// Enable or disable API debugging.
        /// </summary>
        public bool Api { get; set; } = false;

        /// <summary>
        /// Enable or disable authentication debugging.
        /// </summary>
        public bool Authentication { get; set; } = false;

        /// <summary>
        /// Enable or disable storage debugging.
        /// </summary>
        public bool Storage { get; set; } = false;

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the DebugSettings class.
        /// </summary>
        public DebugSettings()
        {
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}