using FileCopy.Configuration;
using FileCopy.Data;
using FileCopy.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Data;

namespace FileCopy
{
    class Program
    {
        static string _ConnectionStringM = "";
        static string _ConnectionStringPOrig = "";

        static async Task<int> Main(string[] args)
        {
            Console.WriteLine("========================================");
            Console.WriteLine("  FileCopy Console Application");
            Console.WriteLine("  Started at: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Console.WriteLine("========================================");
            Console.WriteLine();

            int totalSuccessCount = 0;
            int totalFailedCount = 0;
            int projectsProcessed = 0;

            try
            {
                // Build configuration
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .Build();

                // Get connection strings
                _ConnectionStringM = configuration.GetConnectionString("MasterConnection")
                    ?? throw new InvalidOperationException("Connection string 'MasterConnection' not found.");
                _ConnectionStringPOrig = configuration.GetConnectionString("ProjectConnection")
                    ?? throw new InvalidOperationException("Connection string 'ProjectConnection' not found.");

                // Get FileCopySettings
                var fileCopySettings = configuration.GetSection("FileCopySettings").Get<FileCopySettings>() ?? new FileCopySettings();

                // Setup logging
                var services = new ServiceCollection();
                services.AddLogging(builder =>
                {
                    builder.AddConfiguration(configuration.GetSection("Logging"));
                    builder.AddConsole();
                });

                using var serviceProvider = services.BuildServiceProvider();
                var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

                logger.LogInformation("FileCopy process started.");

                // Get list of projects from master database
                var masterDataAccessHelper = new DataAccessHelper(_ConnectionStringM);
                DataTable dtProject = (await masterDataAccessHelper.ExecuteDataTableAsync(
                    "SELECT [ProjectCode], [Instance], ISNULL([DeliveryLocationSystem], '') AS [DeliveryLocationSystem] FROM [Projects] WHERE ISNULL([IsFileCopyRequired], 0) = 1"
                ));

                if (dtProject.Rows.Count == 0)
                {
                    Console.WriteLine("No projects found to process.");
                    logger.LogInformation("No projects found to process.");
                    return 0;
                }

                Console.WriteLine($"Found {dtProject.Rows.Count} project(s) to process.");
                Console.WriteLine();

                // Process each project
                foreach (DataRow rowProject in dtProject.Rows)
                {
                    string strProjCode = rowProject["ProjectCode"]?.ToString() ?? "";
                    string strInstance = rowProject["Instance"]?.ToString() ?? "";
                    string strDeliveryLocationSystem = rowProject["DeliveryLocationSystem"]?.ToString() ?? "";

                    if (string.IsNullOrEmpty(strProjCode))
                    {
                        logger.LogWarning("Skipping project with empty ProjectCode.");
                        continue;
                    }

                    // Build project-specific connection string
                    string connectionStringP = _ConnectionStringPOrig
                        .Replace("####", strProjCode)
                        .Replace("SQL_INST", strInstance);

                    Console.WriteLine("========================================");
                    Console.WriteLine($"  Processing Project: {strProjCode}");
                    Console.WriteLine("========================================");

                    try
                    {
                        // Create project-specific services
                        var projectServices = new ServiceCollection();
                        ConfigureProjectServices(projectServices, configuration, connectionStringP, fileCopySettings);

                        using var projectServiceProvider = projectServices.BuildServiceProvider();
                        var fileCopyService = projectServiceProvider.GetRequiredService<IFileCopyService>();

                        // Get pending attachments for this project
                        var pendingAttachments = await fileCopyService.GetPendingAttachmentsAsync();
                        var attachmentList = pendingAttachments.ToList();

                        if (attachmentList.Count == 0)
                        {
                            Console.WriteLine($"  No attachments found for project {strProjCode}.");
                            logger.LogInformation($"No attachments found for project {strProjCode}.");
                            continue;
                        }

                        Console.WriteLine($"  Found {attachmentList.Count} attachment(s) to process.");
                        Console.WriteLine();

                        // Process all files for this project
                        var copyLogs = await fileCopyService.CopyAllFilesAsync(strDeliveryLocationSystem);
                        var logList = copyLogs.ToList();

                        // Calculate counts for this project
                        int successCount = logList.Count(l => l.Status == "Success");
                        int failedCount = logList.Count(l => l.Status == "Failed");

                        totalSuccessCount += successCount;
                        totalFailedCount += failedCount;
                        projectsProcessed++;

                        // Display project summary
                        Console.WriteLine($"  Project {strProjCode} Summary:");
                        Console.WriteLine($"    Total Processed: {logList.Count}");
                        Console.WriteLine($"    Successful:      {successCount}");
                        Console.WriteLine($"    Failed:          {failedCount}");
                        Console.WriteLine();

                        // Display details for each operation
                        foreach (var log in logList)
                        {
                            string statusIcon = log.Status == "Success" ? "[OK]" : "[FAIL]";
                            Console.WriteLine($"    {statusIcon} {log.FileName}");
                            Console.WriteLine($"         Source: {log.SourcePath}");
                            Console.WriteLine($"         Dest:   {log.DestinationPath}");

                            if (log.Status == "Success" && log.FileSizeBytes.HasValue)
                            {
                                Console.WriteLine($"         Size:   {FormatFileSize(log.FileSizeBytes.Value)}");
                            }

                            if (!string.IsNullOrEmpty(log.ErrorDetails))
                            {
                                Console.WriteLine($"         Error:  {log.ErrorDetails}");
                            }

                            if (log.StartTime != default && log.EndTime.HasValue)
                            {
                                var duration = log.EndTime.Value - log.StartTime;
                                Console.WriteLine($"         Duration: {duration.TotalSeconds:F2} seconds");
                            }

                            Console.WriteLine();
                        }

                        logger.LogInformation($"Project {strProjCode} completed. Success: {successCount}, Failed: {failedCount}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  ERROR processing project {strProjCode}: {ex.Message}");
                        logger.LogError(ex, $"Error processing project {strProjCode}");
                    }
                }

                // Display overall summary
                Console.WriteLine();
                Console.WriteLine("========================================");
                Console.WriteLine("  Overall Processing Summary");
                Console.WriteLine("========================================");
                Console.WriteLine($"  Projects Processed: {projectsProcessed}");
                Console.WriteLine($"  Total Successful:   {totalSuccessCount}");
                Console.WriteLine($"  Total Failed:       {totalFailedCount}");
                Console.WriteLine();
                Console.WriteLine("========================================");
                Console.WriteLine("  Completed at: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                Console.WriteLine("========================================");

                logger.LogInformation($"FileCopy process completed. Projects: {projectsProcessed}, Success: {totalSuccessCount}, Failed: {totalFailedCount}");

                // Return non-zero exit code if any failures
                return totalFailedCount > 0 ? 1 : 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("========================================");
                Console.WriteLine("  FATAL ERROR");
                Console.WriteLine("========================================");
                Console.WriteLine($"  {ex.Message}");
                Console.WriteLine();
                Console.WriteLine("Stack Trace:");
                Console.WriteLine(ex.StackTrace);
                return -1;
            }
        }

        private static void ConfigureProjectServices(IServiceCollection services, IConfiguration configuration, string connectionString, FileCopySettings fileCopySettings)
        {
            // Add logging
            services.AddLogging(builder =>
            {
                builder.AddConfiguration(configuration.GetSection("Logging"));
                builder.AddConsole();
            });

            // Add configuration
            services.AddSingleton<IConfiguration>(configuration);

            // Add DataAccessHelper with project-specific connection string
            services.AddScoped(_ => new DataAccessHelper(connectionString));

            // Add repositories
            services.AddScoped<IAttachmentRepository, AttachmentRepository>();
            services.AddScoped<ICopyLogRepository, CopyLogRepository>();

            // Add FileCopyService
            services.AddScoped<IFileCopyService, FileCopyService>();

            // Add FileCopySettings
            services.AddSingleton(Options.Create(fileCopySettings));
        }

        private static string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int suffixIndex = 0;
            double size = bytes;

            while (size >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }

            return $"{size:F2} {suffixes[suffixIndex]}";
        }
    }
}
