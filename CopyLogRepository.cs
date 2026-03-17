using FileCopy.Models;
using System.Data;
using Microsoft.Data.SqlClient;

namespace FileCopy.Data
{
    public interface ICopyLogRepository
    {
        Task<int> InsertAsync(CopyLog copyLog);
        Task<List<CopyLog>> GetAllAsync();
        Task<List<CopyLog>> GetBetweenAsync(DateTime startDate, DateTime endDate);
    }

    public class CopyLogRepository : ICopyLogRepository
    {
        private readonly DataAccessHelper _dataAccessHelper;

        public CopyLogRepository(DataAccessHelper dataAccessHelper)
        {
            _dataAccessHelper = dataAccessHelper;
        }

        public async Task<int> InsertAsync(CopyLog copyLog)
        {
            var parameters = new[]
            {
                new SqlParameter("@FileName", copyLog.FileName ?? (object)DBNull.Value),
                new SqlParameter("@SourcePath", copyLog.SourcePath ?? (object)DBNull.Value),
                new SqlParameter("@DestinationPath", copyLog.DestinationPath ?? (object)DBNull.Value),
                new SqlParameter("@OverwriteFlag", copyLog.OverwriteFlag ? 1 : 0),
                new SqlParameter("@StartTime", copyLog.StartTime),
                new SqlParameter("@EndTime", copyLog.EndTime ?? (object)DBNull.Value),
                new SqlParameter("@Status", copyLog.Status ?? (object)DBNull.Value),
                new SqlParameter("@ErrorDetails", copyLog.ErrorDetails ?? (object)DBNull.Value),
                new SqlParameter("@FileSizeBytes", copyLog.FileSizeBytes ?? (object)DBNull.Value),
                new SqlParameter("@Id", SqlDbType.Int) { Direction = ParameterDirection.Output }
            };

            var insertQuery = @"
                INSERT INTO Tab_CopyLog (FileName, SourcePath, DestinationPath, OverwriteFlag, StartTime, EndTime, Status, ErrorDetails, FileSizeBytes)
                VALUES (@FileName, @SourcePath, @DestinationPath, @OverwriteFlag, @StartTime, @EndTime, @Status, @ErrorDetails, @FileSizeBytes);
                SET @Id = SCOPE_IDENTITY();
            ";

            using (var connection = new SqlConnection(_dataAccessHelper.GetConnectionString()))
            {
                using (var command = new SqlCommand(insertQuery, connection))
                {
                    command.CommandType = CommandType.Text;
                    command.Parameters.AddRange(parameters);
                    await connection.OpenAsync();
                    await command.ExecuteNonQueryAsync();
                    return (int)command.Parameters["@Id"].Value;
                }
            }
        }

        public async Task<List<CopyLog>> GetAllAsync()
        {
            return await _dataAccessHelper.ExecuteReaderAsync<CopyLog>(
                "SELECT Id, FileName, SourcePath, DestinationPath, OverwriteFlag, StartTime, EndTime, Status, ErrorDetails, FileSizeBytes FROM Tab_CopyLog ORDER BY StartTime DESC",
                reader => new CopyLog
                {
                    Id = reader.GetInt32(0),
                    FileName = reader.GetString(1),
                    SourcePath = reader.GetString(2),
                    DestinationPath = reader.GetString(3),
                    OverwriteFlag = reader.GetInt32(4) != 0,
                    StartTime = reader.GetDateTime(5),
                    EndTime = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                    Status = reader.GetString(7),
                    ErrorDetails = reader.IsDBNull(8) ? null : reader.GetString(8),
                    FileSizeBytes = reader.IsDBNull(9) ? null : reader.GetInt64(9)
                },
                CommandType.Text
            );
        }

        public async Task<List<CopyLog>> GetBetweenAsync(DateTime startDate, DateTime endDate)
        {
            return await _dataAccessHelper.ExecuteReaderAsync<CopyLog>(
                "SELECT Id, FileName, SourcePath, DestinationPath, OverwriteFlag, StartTime, EndTime, Status, ErrorDetails, FileSizeBytes FROM Tab_CopyLog WHERE StartTime >= @StartDate AND StartTime <= @EndDate ORDER BY StartTime DESC",
                reader => new CopyLog
                {
                    Id = reader.GetInt32(0),
                    FileName = reader.GetString(1),
                    SourcePath = reader.GetString(2),
                    DestinationPath = reader.GetString(3),
                    OverwriteFlag = reader.GetInt32(4) != 0,
                    StartTime = reader.GetDateTime(5),
                    EndTime = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                    Status = reader.GetString(7),
                    ErrorDetails = reader.IsDBNull(8) ? null : reader.GetString(8),
                    FileSizeBytes = reader.IsDBNull(9) ? null : reader.GetInt64(9)
                },
                CommandType.Text,
                new SqlParameter("@StartDate", startDate),
                new SqlParameter("@EndDate", endDate)
            );
        }
    }
}
