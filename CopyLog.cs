namespace FileCopy.Models
{
    public class CopyLog
    {
        public int Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public string DestinationPath { get; set; } = string.Empty;
        public bool OverwriteFlag { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? ErrorDetails { get; set; }
        public long? FileSizeBytes { get; set; }
    }
}
