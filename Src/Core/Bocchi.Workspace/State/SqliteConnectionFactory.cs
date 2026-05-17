using Microsoft.Data.Sqlite;

namespace Bocchi.Workspace.State;

/// <summary>
/// SQLite 连接工厂。每次调用 <see cref="OpenConnection"/> 都会打开一个新连接（短连接策略）。
/// 连接字符串固定到 <see cref="BocchiDataLayout.SqliteDatabasePath"/>。
/// </summary>
public sealed class SqliteConnectionFactory
{
    private readonly string _databasePath;

    public SqliteConnectionFactory(BocchiDataLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _databasePath = layout.SqliteDatabasePath;
    }

    /// <summary>SQLite 数据库文件的绝对路径。</summary>
    public string DatabasePath => _databasePath;

    /// <summary>
    /// 打开一个新连接。调用方负责 <see cref="SqliteConnection.Dispose"/>。
    /// </summary>
    public SqliteConnection OpenConnection()
    {
        var dir = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var connection = new SqliteConnection($"Data Source={_databasePath};Cache=Shared");
        connection.Open();
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
            pragma.ExecuteNonQuery();
        }

        return connection;
    }
}