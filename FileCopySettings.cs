namespace FileCopy.Configuration
{
    /// <summary>
    /// Configuration settings for FileCopy operations
    /// </summary>
    public class FileCopySettings
    {
        /// <summary>
        /// Maximum file size in bytes that can be copied (0 = unlimited)
        /// </summary>
        public long MaxFileSizeBytes { get; set; } = 0;

        /// <summary>
        /// Maximum number of retry attempts for failed copy operations
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Delay in milliseconds between retry attempts
        /// </summary>
        public int RetryDelayMs { get; set; } = 1000;

        /// <summary>
        /// Enable detailed logging
        /// </summary>
        public bool EnableDetailedLogging { get; set; } = true;

        /// <summary>
        /// Buffer size in bytes for file copy operations
        /// </summary>
        public int BufferSizeBytes { get; set; } = 81920; // 80 KB
    }
}
