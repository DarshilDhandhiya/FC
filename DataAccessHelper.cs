using System.Data;
using Microsoft.Data.SqlClient;

namespace FileCopy.Data
{
    public class DataAccessHelper
    {
        private readonly string _connectionString;

        public DataAccessHelper(string connectionString)
        {
            _connectionString = connectionString;
        }

        public string GetConnectionString() => _connectionString;

        public async Task ExecuteNonQueryAsync(string commandText, CommandType commandType = CommandType.Text, params SqlParameter[] parameters)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand(commandText, connection))
                {
                    command.CommandType = commandType;
                    if (parameters.Length > 0)
                    {
                        command.Parameters.AddRange(parameters);
                    }
                    await connection.OpenAsync();
                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        public async Task<T?> ExecuteScalarAsync<T>(string commandText, CommandType commandType = CommandType.Text, params SqlParameter[] parameters)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand(commandText, connection))
                {
                    command.CommandType = commandType;
                    if (parameters.Length > 0)
                    {
                        command.Parameters.AddRange(parameters);
                    }
                    await connection.OpenAsync();
                    var result = await command.ExecuteScalarAsync();
                    return result == null || result == DBNull.Value ? default : (T)Convert.ChangeType(result, typeof(T))!;
                }
            }
        }

        public async Task<DataTable> ExecuteDataTableAsync(string commandText, CommandType commandType = CommandType.Text, params SqlParameter[] parameters)
        {
            var dataTable = new DataTable();
            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand(commandText, connection))
                {
                    command.CommandType = commandType;
                    if (parameters.Length > 0)
                    {
                        command.Parameters.AddRange(parameters);
                    }
                    using (var adapter = new SqlDataAdapter(command))
                    {
                        adapter.Fill(dataTable);
                    }
                }
            }
            return dataTable;
        }

        public async Task<List<T>> ExecuteReaderAsync<T>(string commandText, Func<SqlDataReader, T> mapper, CommandType commandType = CommandType.Text, params SqlParameter[] parameters)
        {
            var results = new List<T>();
            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand(commandText, connection))
                {
                    command.CommandType = commandType;
                    if (parameters.Length > 0)
                    {
                        command.Parameters.AddRange(parameters);
                    }
                    await connection.OpenAsync();
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            results.Add(mapper(reader));
                        }
                    }
                }
            }
            return results;
        }
    }
}
