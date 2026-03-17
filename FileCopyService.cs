using FileCopy.Configuration;
using FileCopy.Data;
using FileCopy.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FileCopy.Services
{
    public interface IFileCopyService
    {
        Task<CopyLog> CopyFileAsync(int attachmentId);
        Task<IEnumerable<Attachment>> GetPendingAttachmentsAsync();
        Task<IEnumerable<CopyLog>> CopyAllFilesAsync(string deliveryLocationSystem);
        Task<IEnumerable<CopyLog>> GetCopyLogsAsync();
        Task<IEnumerable<CopyLog>> GetCopyLogsBetweenAsync(DateTime startDate, DateTime endDate);
    }

    public class FileCopyService : IFileCopyService
    {
        private readonly IAttachmentRepository _attachmentRepository;
        private readonly ICopyLogRepository _copyLogRepository;
        private readonly ILogger<FileCopyService> _logger;
        private readonly FileCopySettings _settings;

        public FileCopyService(
            IAttachmentRepository attachmentRepository,
            ICopyLogRepository copyLogRepository,
            ILogger<FileCopyService> logger,
            IOptions<FileCopySettings>? settings = null)
        {
            _attachmentRepository = attachmentRepository;
            _copyLogRepository = copyLogRepository;
            _logger = logger;
            _settings = settings?.Value ?? new FileCopySettings();
        }

        public async Task<CopyLog> CopyFileAsync(int attachmentId)
        {
            var copyLog = new CopyLog
            {
                StartTime = DateTime.UtcNow,
                Status = "In Progress"
            };

            try
            {
                var attachment = await _attachmentRepository.GetByIdAsync(attachmentId);
                if (attachment == null)
                {
                    copyLog.Status = "Failed";
                    copyLog.ErrorDetails = "Attachment record not found.";
                    copyLog.EndTime = DateTime.UtcNow;
                    _logger.LogError($"Attachment {attachmentId} not found.");
                    await SaveCopyLogAsync(copyLog);
                    return copyLog;
                }

                copyLog.FileName = attachment.FileName ?? Path.GetFileName(attachment.SourcePath);
                copyLog.SourcePath = attachment.SourcePath;
                copyLog.DestinationPath = attachment.DestinationPath;
                copyLog.OverwriteFlag = attachment.Overwrite;

                await PerformFileCopyAsync(attachment, copyLog, string.Empty);
            }
            catch (Exception ex)
            {
                copyLog.Status = "Failed";
                copyLog.ErrorDetails = ex.Message;
                copyLog.EndTime = DateTime.UtcNow;
                _logger.LogError(ex, "File copy operation failed.");
                await SaveCopyLogAsync(copyLog);
            }

            return copyLog;
        }

        public async Task<IEnumerable<Attachment>> GetPendingAttachmentsAsync()
        {
            _logger.LogInformation("Retrieving pending attachments.");
            try
            {
                var attachments = await _attachmentRepository.GetAllAsync();
                _logger.LogInformation($"Found {attachments?.Count ?? 0} pending attachments.");
                return attachments ?? new List<Attachment>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving pending attachments.");
                return new List<Attachment>();
            }
        }

        public async Task<IEnumerable<CopyLog>> CopyAllFilesAsync(string deliveryLocationSystem)
        {
            _logger.LogInformation("Starting batch copy operation for all attachments.");
            var copyLogs = new List<CopyLog>();

            try
            {
                var attachments = await _attachmentRepository.GetAllAsync();
                if (attachments == null || attachments.Count == 0)
                {
                    _logger.LogWarning("No attachments found in the database.");
                    return copyLogs;
                }

                _logger.LogInformation($"Found {attachments.Count} attachments to process.");

                foreach (var attachment in attachments)
                {
                    var copyLog = new CopyLog
                    {
                        StartTime = DateTime.UtcNow,
                        Status = "In Progress"
                    };

                    try
                    {
                        copyLog.FileName = attachment.FileName ?? Path.GetFileName(attachment.SourcePath);
                        copyLog.SourcePath = attachment.SourcePath;
                        copyLog.DestinationPath = attachment.DestinationPath;
                        copyLog.OverwriteFlag = attachment.Overwrite;

                        await PerformFileCopyAsync(attachment, copyLog, deliveryLocationSystem);
                        copyLogs.Add(copyLog);
                    }
                    catch (Exception ex)
                    {
                        copyLog.Status = "Failed";
                        copyLog.ErrorDetails = ex.Message;
                        copyLog.EndTime = DateTime.UtcNow;
                        _logger.LogError(ex, $"Error processing attachment {attachment.Id}");
                        await SaveCopyLogAsync(copyLog);
                        copyLogs.Add(copyLog);
                    }
                }

                _logger.LogInformation($"Batch copy operation completed. Total processed: {copyLogs.Count}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Batch copy operation failed.");
            }

            return copyLogs;
        }

        private async Task PerformFileCopyAsync(Attachment attachment, CopyLog copyLog, string deliveryLocationSystem)
        {
            // Compute FilePathAbs (DestinationPath) if not already set
            if (string.IsNullOrEmpty(attachment.DestinationPath))
            {
                string computed = ComputeFilePathAbs(attachment.SourcePath, deliveryLocationSystem);
                if (string.IsNullOrEmpty(computed))
                {
                    copyLog.Status = "Failed";
                    copyLog.ErrorDetails = $"Cannot compute destination path: no matching folder found between FilePath '{attachment.SourcePath}' and DeliveryLocationSystem '{deliveryLocationSystem}'.";
                    copyLog.EndTime = DateTime.UtcNow;
                    _logger.LogWarning(copyLog.ErrorDetails);
                    await SaveCopyLogAsync(copyLog);
                    return;
                }

                attachment.DestinationPath = computed;
                copyLog.DestinationPath = computed;
                _logger.LogInformation($"Computed FilePathAbs: {computed}");
            }

            if (!File.Exists(attachment.SourcePath))
            {
                copyLog.Status = "Failed";
                copyLog.ErrorDetails = $"Source file not found: {attachment.SourcePath}";
                copyLog.EndTime = DateTime.UtcNow;
                _logger.LogWarning($"Source file not found: {attachment.SourcePath}");
                await SaveCopyLogAsync(copyLog);
                return;
            }

            try
            {
                var fileInfo = new FileInfo(attachment.SourcePath);
                
                // Check file size limit
                if (_settings.MaxFileSizeBytes > 0 && fileInfo.Length > _settings.MaxFileSizeBytes)
                {
                    copyLog.Status = "Failed";
                    copyLog.ErrorDetails = $"File size exceeds maximum allowed size of {_settings.MaxFileSizeBytes} bytes.";
                    copyLog.EndTime = DateTime.UtcNow;
                    copyLog.FileSizeBytes = fileInfo.Length;
                    _logger.LogWarning($"File size exceeds limit: {attachment.SourcePath} ({fileInfo.Length} bytes)");
                    await SaveCopyLogAsync(copyLog);
                    return;
                }

                string destDirectory = Path.GetDirectoryName(attachment.DestinationPath) ?? "";

                if (!Directory.Exists(destDirectory))
                {
                    Directory.CreateDirectory(destDirectory);
                    _logger.LogInformation($"Created destination directory: {destDirectory}");
                }

                if (File.Exists(attachment.DestinationPath) && !attachment.Overwrite)
                {
                    copyLog.Status = "Failed";
                    copyLog.ErrorDetails = $"Destination file already exists and overwrite is disabled: {attachment.DestinationPath}";
                    copyLog.EndTime = DateTime.UtcNow;
                    copyLog.FileSizeBytes = fileInfo.Length;
                    _logger.LogWarning($"Destination file exists and overwrite disabled: {attachment.DestinationPath}");
                    await SaveCopyLogAsync(copyLog);
                    return;
                }

                copyLog.FileSizeBytes = fileInfo.Length;

                // Attempt copy with retry logic
                bool copySuccessful = false;
                Exception? lastException = null;

                for (int attempt = 0; attempt <= _settings.MaxRetryAttempts; attempt++)
                {
                    try
                    {
                        File.Copy(attachment.SourcePath, attachment.DestinationPath, attachment.Overwrite);
                        copySuccessful = true;
                        _logger.LogInformation($"File copied successfully from {attachment.SourcePath} to {attachment.DestinationPath}");
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        if (attempt < _settings.MaxRetryAttempts)
                        {
                            _logger.LogWarning($"Copy attempt {attempt + 1} failed, retrying in {_settings.RetryDelayMs}ms...");
                            await Task.Delay(_settings.RetryDelayMs);
                        }
                    }
                }

                if (copySuccessful)
                {
                    copyLog.Status = "Success";
                    await _attachmentRepository.UpdateFilePathAbsAsync(attachment.Id, attachment.DestinationPath);
                }
                else
                {
                    copyLog.Status = "Failed";
                    copyLog.ErrorDetails = $"Copy operation failed after {_settings.MaxRetryAttempts + 1} attempts. Last error: {lastException?.Message}";
                    _logger.LogError(lastException, $"Failed to copy file after {_settings.MaxRetryAttempts + 1} attempts");
                }

                copyLog.EndTime = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                copyLog.Status = "Failed";
                copyLog.ErrorDetails = ex.Message;
                copyLog.EndTime = DateTime.UtcNow;
                _logger.LogError(ex, $"Error copying file from {attachment.SourcePath} to {attachment.DestinationPath}");
            }
            finally
            {
                await SaveCopyLogAsync(copyLog);
            }
        }

        private static string ComputeFilePathAbs(string filePath, string deliveryLocationSystem)
        {
            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(deliveryLocationSystem))
                return string.Empty;

            // Normalize: strip trailing separators to get the base delivery path
            string deliveryBase = deliveryLocationSystem.TrimEnd('\\', '/');

            // The deepest folder in DeliveryLocationSystem is the match anchor
            string matchFolder = Path.GetFileName(deliveryBase);
            if (string.IsNullOrEmpty(matchFolder))
                return string.Empty;

            // Split FilePath into segments to find the matching folder
            string[] parts = filePath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);

            int matchIndex = -1;
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.Equals(parts[i], matchFolder, StringComparison.OrdinalIgnoreCase))
                    matchIndex = i;
            }

            if (matchIndex < 0 || matchIndex >= parts.Length - 1)
                return string.Empty;

            // Everything after the matched folder is the relative path
            string relativePath = string.Join("\\", parts.Skip(matchIndex + 1));
            return Path.Combine(deliveryBase, relativePath);
        }

        private async Task SaveCopyLogAsync(CopyLog copyLog)
        {
            await _copyLogRepository.InsertAsync(copyLog);
        }

        public async Task<IEnumerable<CopyLog>> GetCopyLogsAsync()
        {
            return await _copyLogRepository.GetAllAsync();
        }

        public async Task<IEnumerable<CopyLog>> GetCopyLogsBetweenAsync(DateTime startDate, DateTime endDate)
        {
            return await _copyLogRepository.GetBetweenAsync(startDate, endDate);
        }
    }
}
