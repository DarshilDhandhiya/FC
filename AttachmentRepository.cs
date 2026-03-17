using FileCopy.Models;
using System.Data;
using Microsoft.Data.SqlClient;

namespace FileCopy.Data
{
    public interface IAttachmentRepository
    {
        Task<Attachment?> GetByIdAsync(int id);
        Task<List<Attachment>> GetAllAsync();
    }

    public class AttachmentRepository : IAttachmentRepository
    {
        private readonly DataAccessHelper _dataAccessHelper;

        public AttachmentRepository(DataAccessHelper dataAccessHelper)
        {
            _dataAccessHelper = dataAccessHelper;
        }

        public async Task<Attachment?> GetByIdAsync(int id)
        {
            var attachments = await _dataAccessHelper.ExecuteReaderAsync<Attachment>(
                "SELECT ID, FilePath, FilePathAbs, SystemFileName, 1 AS Overwrite, CreatedDate FROM TransEngChk_T_Att WHERE ID = @Id",
                reader => new Attachment
                {
                    Id = reader.GetInt32(0),
                    SourcePath = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    DestinationPath = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    FileName = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Overwrite = true,
                    CreatedDate = reader.GetDateTime(5)
                },
                CommandType.Text,
                new SqlParameter("@Id", id)
            );

            return attachments.FirstOrDefault();
        }

        public async Task<List<Attachment>> GetAllAsync()
        {
            return await _dataAccessHelper.ExecuteReaderAsync<Attachment>(
                "SELECT ID, FilePath, FilePathAbs, SystemFileName, 1 AS Overwrite, CreatedDate FROM TransEngChk_T_Att ORDER BY CreatedDate ASC",
                reader => new Attachment
                {
                    Id = reader.GetInt32(0),
                    SourcePath = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    DestinationPath = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    FileName = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Overwrite = true,
                    CreatedDate = reader.GetDateTime(5)
                },
                CommandType.Text
            );
        }
    }
}
