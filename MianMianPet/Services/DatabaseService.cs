using Microsoft.Data.Sqlite;
using MianMianPet.Infrastructure;
using MianMianPet.Models;

namespace MianMianPet.Services;

public sealed class DatabaseService
{
    private const string SchemaSql = @"
        PRAGMA journal_mode = WAL;
        CREATE TABLE IF NOT EXISTS conversations (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            role TEXT NOT NULL CHECK(role IN ('user', 'assistant')),
            content TEXT NOT NULL,
            created_at TEXT NOT NULL,
            status TEXT NOT NULL CHECK(status IN ('complete', 'cancelled', 'failed'))
        );
        CREATE INDEX IF NOT EXISTS ix_conversations_created_at
            ON conversations(created_at DESC);
        CREATE TABLE IF NOT EXISTS app_state (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );
        ";

    private const string LegacyConversationRepairSql = @"
        -- 2.0.1 及更早版本可能在请求失败时留下 complete 状态的孤立用户消息。
        -- 只有紧随其后的下一条完整消息是 assistant，用户消息才属于完整对话。
        UPDATE conversations
        SET status = 'failed'
        WHERE role = 'user'
          AND status = 'complete'
          AND COALESCE((
              SELECT next.role
              FROM conversations AS next
              WHERE next.id > conversations.id
                AND next.status = 'complete'
              ORDER BY next.id
              LIMIT 1
          ), '') <> 'assistant';
        ";

    private readonly AppPaths _paths;
    private readonly LogService _logService;
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal string? RecoveryBackupPath { get; private set; }

    public DatabaseService(AppPaths paths, LogService logService)
    {
        _paths = paths;
        _logService = logService;
    }

    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = _paths.DatabaseFile,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared
    }.ToString();

    public async Task InitializeAsync()
    {
        await _gate.WaitAsync();
        string? sidecarSnapshot = null;
        var initialized = false;
        try
        {
            // Even a read-only SQLite connection may rewrite SHM, so retain crash-recovery files first.
            var sidecars = new[] { _paths.DatabaseFile + "-wal", _paths.DatabaseFile + "-shm" }.Where(File.Exists).ToArray();
            if (sidecars.Length > 0)
            {
                sidecarSnapshot = CreateRecoveryDirectory();
                foreach (var file in sidecars)
                    File.Copy(file, Path.Combine(sidecarSnapshot, Path.GetFileName(file)));
            }
            await InitializeDatabaseAsync();
            initialized = true;
        }
        catch (SqliteException ex) when (IsDatabaseCorruption(ex))
        {
            await _logService.ErrorAsync("database_corruption_detected", ex);
            await RecoverCorruptDatabaseAsync(sidecarSnapshot);
            initialized = true;
        }
        catch (SqliteException ex)
        {
            await _logService.ErrorAsync("database_initialize_failed", ex);
            throw;
        }
        finally
        {
            if (initialized && sidecarSnapshot is not null && sidecarSnapshot != RecoveryBackupPath)
            {
                try
                {
                    foreach (var suffix in new[] { "-wal", "-shm" })
                        File.Delete(Path.Combine(sidecarSnapshot, Path.GetFileName(_paths.DatabaseFile) + suffix));
                    Directory.Delete(sidecarSnapshot);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    await _logService.ErrorAsync("database_recovery_snapshot_cleanup_failed", ex);
                }
            }
            _gate.Release();
        }
    }

    public async Task<long> AddMessageAsync(ChatMessage message)
    {
        await _gate.WaitAsync();
        try
        {
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO conversations(role, content, created_at, status)
                VALUES ($role, $content, $createdAt, $status);
                SELECT last_insert_rowid();
                ";
            command.Parameters.AddWithValue("$role", message.Role);
            command.Parameters.AddWithValue("$content", message.Content);
            command.Parameters.AddWithValue("$createdAt", message.CreatedAt.ToString("O"));
            command.Parameters.AddWithValue("$status", message.Status.ToString().ToLowerInvariant());
            return (long)(await command.ExecuteScalarAsync() ?? 0L);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AddExchangeAsync(ChatMessage userMessage, ChatMessage assistantMessage)
    {
        if (userMessage.Role != "user" || assistantMessage.Role != "assistant")
        {
            throw new ArgumentException("对话交换必须按 user、assistant 的顺序保存。");
        }

        await _gate.WaitAsync();
        try
        {
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            try
            {
                await InsertMessageAsync(connection, transaction, userMessage);
                await InsertMessageAsync(connection, transaction, assistantMessage);
                transaction.Commit();
            }
            catch
            {
                try
                {
                    transaction.Rollback();
                }
                catch
                {
                    // 保留原始写入异常；连接释放时仍会回滚未提交事务。
                }

                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ChatMessage>> GetRecentMessagesAsync(int count, long afterId = 0)
    {
        var result = new List<ChatMessage>();
        await _gate.WaitAsync();
        try
        {
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT id, role, content, created_at, status
                FROM conversations
                WHERE status = 'complete' AND id > $afterId
                ORDER BY id DESC
                LIMIT $count;
                ";
            command.Parameters.AddWithValue("$count", Math.Clamp(count, 1, 50));
            command.Parameters.AddWithValue("$afterId", Math.Max(0, afterId));
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                _ = Enum.TryParse<MessageStatus>(reader.GetString(4), true, out var status);
                _ = DateTimeOffset.TryParse(reader.GetString(3), out var createdAt);
                result.Add(new ChatMessage(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    createdAt,
                    status));
            }
        }
        finally
        {
            _gate.Release();
        }

        result.Reverse();
        return result;
    }

    public async Task<long> GetLatestConversationIdAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COALESCE(MAX(id), 0) FROM conversations;";
            return (long)(await command.ExecuteScalarAsync() ?? 0L);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearConversationAsync()
    {
        await ExecuteNonQueryAsync("DELETE FROM conversations;");
    }

    public async Task<string?> GetStateAsync(string key)
    {
        await _gate.WaitAsync();
        try
        {
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM app_state WHERE key = $key;";
            command.Parameters.AddWithValue("$key", key);
            return await command.ExecuteScalarAsync() as string;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetStateAsync(string key, string value)
    {
        await _gate.WaitAsync();
        try
        {
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO app_state(key, value) VALUES($key, $value)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value;
                ";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ExecuteNonQueryAsync(string sql)
    {
        await _gate.WaitAsync();
        try
        {
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private string CreateRecoveryDirectory()
    {
        var directory = Path.Combine(_paths.Root, $"mianmian-corrupt-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private async Task RecoverCorruptDatabaseAsync(string? sidecarSnapshot)
    {
        if (!File.Exists(_paths.DatabaseFile))
        {
            return;
        }

        using (var pooledConnection = new SqliteConnection(ConnectionString))
            SqliteConnection.ClearPool(pooledConnection);

        var backup = sidecarSnapshot ?? CreateRecoveryDirectory();
        var files = new[] { _paths.DatabaseFile, _paths.DatabaseFile + "-wal", _paths.DatabaseFile + "-shm" }
            .Where(File.Exists).ToArray();
        // Finish the complete backup before removing any original recovery material.
        foreach (var file in files)
        {
            var destination = Path.Combine(backup, Path.GetFileName(file));
            if (!File.Exists(destination)) File.Copy(file, destination);
        }
        RecoveryBackupPath = backup;
        await _logService.InfoAsync($"database_recovery_backup_created; backup={backup}");
        foreach (var file in files)
            File.Delete(file);

        await InitializeDatabaseAsync();
        await _logService.InfoAsync($"database_recovered; backup={backup}");
    }

    private async Task InitializeDatabaseAsync()
    {
        if (File.Exists(_paths.DatabaseFile))
        {
            // Probe without write access: opening a corrupt database for writing can discard its WAL sidecars.
            var probeConnection = new SqliteConnectionStringBuilder(ConnectionString)
            {
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString();
            await using var probe = new SqliteConnection(probeConnection);
            await probe.OpenAsync();
            await using var probeCommand = probe.CreateCommand();
            probeCommand.CommandText = "PRAGMA schema_version;";
            await probeCommand.ExecuteScalarAsync();
        }
        // A failed initialization must release the native handle before file recovery on Windows.
        var initializationConnection = new SqliteConnectionStringBuilder(ConnectionString) { Pooling = false }.ToString();
        await using var connection = new SqliteConnection(initializationConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = SchemaSql;
        await command.ExecuteNonQueryAsync();
        command.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(await command.ExecuteScalarAsync());
        if (version < 1)
        {
            using var transaction = connection.BeginTransaction();
            command.Transaction = transaction;
            command.CommandText = LegacyConversationRepairSql + " PRAGMA user_version = 1;";
            await command.ExecuteNonQueryAsync();
            transaction.Commit();
        }
    }

    private static bool IsDatabaseCorruption(SqliteException exception)
    {
        const int sqliteCorrupt = 11;
        const int sqliteNotADatabase = 26;
        return exception.SqliteErrorCode is sqliteCorrupt or sqliteNotADatabase;
    }

    private static async Task InsertMessageAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ChatMessage message)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO conversations(role, content, created_at, status)
            VALUES ($role, $content, $createdAt, $status);
            ";
        command.Parameters.AddWithValue("$role", message.Role);
        command.Parameters.AddWithValue("$content", message.Content);
        command.Parameters.AddWithValue("$createdAt", message.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$status", message.Status.ToString().ToLowerInvariant());
        await command.ExecuteNonQueryAsync();
    }
}
