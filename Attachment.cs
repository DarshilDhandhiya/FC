namespace FileCopy.Models
{
    public class Attachment
    {
        public int Id { get; set; }
        public string SourcePath { get; set; } = string.Empty;
        public string DestinationPath { get; set; } = string.Empty;
        public string? FileName { get; set; }
        public bool Overwrite { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}
