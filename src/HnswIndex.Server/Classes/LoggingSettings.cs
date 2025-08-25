namespace HnswIndex.Server.Classes
{
    /// <summary>
    /// Logging configuration settings.
    /// </summary>
    public class LoggingSettings
    {
        #region Public-Members

        /// <summary>
        /// Enable or disable console logging.
        /// </summary>
        public bool EnableConsole { get; set; } = true;

        /// <summary>
        /// Enable or disable logging using colors.
        /// </summary>
        public bool EnableColors { get; set; } = true;

        /// <summary>
        /// Enable or disable syslog logging.
        /// </summary>
        public bool Syslog { get; set; } = true;

        /// <summary>
        /// Syslog server hostname or IP address.
        /// </summary>
        public string SyslogServerIp
        {
            get { return _SyslogServerIp; }
            set { _SyslogServerIp = value ?? throw new ArgumentNullException(nameof(value)); }
        }

        /// <summary>
        /// Syslog server port.
        /// </summary>
        public int SyslogServerPort { get; set; } = 514;

        /// <summary>
        /// Directory for log files.
        /// </summary>
        public string LogDirectory { get; set; } = "./logs/";

        /// <summary>
        /// Enable or disable file logging.
        /// </summary>
        public bool File { get; set; } = true;

        /// <summary>
        /// Log file path.
        /// </summary>
        public string LogFilename
        {
            get { return _LogFilename; }
            set { _LogFilename = value ?? throw new ArgumentNullException(nameof(value)); }
        }

        /// <summary>
        /// Minimum log level (Debug, Info, Warn, Error).
        /// </summary>
        public string MinimumLevel { get; set; } = "Info";

        /// <summary>
        /// Include UTC timestamps in log entries.
        /// </summary>
        public bool IncludeUtc { get; set; } = true;

        /// <summary>
        /// Include severity levels in log entries.
        /// </summary>
        public bool IncludeSeverity { get; set; } = true;

        #endregion

        #region Private-Members

        private string _SyslogServerIp = "127.0.0.1";
        private string _LogFilename = "./logs/hnswindex.log";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the LoggingSettings class.
        /// </summary>
        public LoggingSettings()
        {
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}