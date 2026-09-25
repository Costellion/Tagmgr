using Windows.Storage;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.UI.Xaml.Media;

namespace Tagmgr
{
    public enum FileSortMode
    {
        NameAsc,
        NameDesc,
        ModifiedDesc,
        ModifiedAsc
    }

    public class TagInfo
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
        public string CountText => $"{Count} 个文件";
        public SolidColorBrush BackgroundBrush => UiService.GetTagBackgroundBrush(Name);
    }

    public static class DataService
    {
        // 全局唯一的文件记录集合
        public static ObservableCollection<FileTagItem> FileItems { get; } = new();

        private const string DbFileName = "tagmgr.db";
        private const string CustomPathKey = "CustomDataPath";
        private const string DefaultFolderName = "Tagmgr";

        private static string _dataFolder;
        private static string _dbFile;

        // 标签名 → 颜色（#RRGGBB）
        private static readonly Dictionary<string, string> _tagColors = new();

        public static string DataFolder => _dataFolder;
        public static string DataFile => _dbFile;

        // 防止重复加载
        private static Task? _loadTask;

        // 串行化写入，避免多个 SaveAsync 并发
        private static readonly System.Threading.SemaphoreSlim _writeLock = new(1, 1);

        static DataService()
        {
            var custom = ApplicationData.Current.LocalSettings.Values[CustomPathKey] as string;

            _dataFolder = string.IsNullOrEmpty(custom)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    DefaultFolderName)
                : custom;

            _dbFile = Path.Combine(_dataFolder, DbFileName);
        }

        public static Task EnsureLoadedAsync()
        {
            return _loadTask ??= LoadAsync();
        }
        public static event Action? DataChanged;

        public static void NotifyDataChanged()
        {
            DataChanged?.Invoke();
        }
        private static string ConnectionString => $"Data Source={_dbFile};Pooling=False";

        // ---------------- 加载 ----------------

        private static async Task LoadAsync()
        {
            try
            {
                Directory.CreateDirectory(_dataFolder);

                using var conn = new SqliteConnection(ConnectionString);
                await conn.OpenAsync();
                await EnsureSchemaAsync(conn);

                FileItems.Clear();
                _tagColors.Clear();

                // 1) 加载标签 + 颜色
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT Name, Color FROM Tags;";
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var name = reader.GetString(0);
                        if (!reader.IsDBNull(1))
                        {
                            var color = reader.GetString(1);
                            if (!string.IsNullOrEmpty(color))
                                _tagColors[name] = color;
                        }
                    }
                }

                // 2) 加载文件
                var fileDict = new Dictionary<long, FileTagItem>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT Id, FilePath FROM Files ORDER BY Id;";
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var id = reader.GetInt64(0);
                        var path = reader.GetString(1);
                        var item = new FileTagItem { FilePath = path };
                        fileDict[id] = item;
                        FileItems.Add(item);
                    }
                }

                // 3) 加载文件-标签关联
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT ft.FileId, t.Name
                        FROM FileTags ft
                        JOIN Tags t ON t.Id = ft.TagId
                        ORDER BY ft.FileId;";
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var fileId = reader.GetInt64(0);
                        var tagName = reader.GetString(1);
                        if (fileDict.TryGetValue(fileId, out var item)
                            && !item.Tags.Contains(tagName))
                        {
                            item.Tags.Add(tagName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载数据失败: {ex.Message}");
            }
        }

        // ---------------- 保存（全量重写） ----------------

        public static async Task SaveAsync()
        {
            await _writeLock.WaitAsync();
            try
            {
                Directory.CreateDirectory(_dataFolder);

                using var conn = new SqliteConnection(ConnectionString);
                await conn.OpenAsync();
                await EnsureSchemaAsync(conn);

                using var tx = conn.BeginTransaction();

                // 1) 清空所有表
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "DELETE FROM FileTags; DELETE FROM Files; DELETE FROM Tags;";
                    await cmd.ExecuteNonQueryAsync();
                }

                // 2) 收集所有标签（来自文件 + 颜色字典）
                var allTags = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in FileItems)
                    foreach (var tag in item.Tags)
                        allTags.Add(tag);
                foreach (var kv in _tagColors)
                    allTags.Add(kv.Key);

                // 3) 插入标签，拿到 Id
                var tagIdMap = new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (var tag in allTags)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText =
                        "INSERT INTO Tags (Name, Color) VALUES ($n, $c); " +
                        "SELECT last_insert_rowid();";
                    cmd.Parameters.AddWithValue("$n", tag);

                    if (_tagColors.TryGetValue(tag, out var color) && !string.IsNullOrEmpty(color))
                        cmd.Parameters.AddWithValue("$c", color);
                    else
                        cmd.Parameters.AddWithValue("$c", DBNull.Value);

                    var id = (long)(await cmd.ExecuteScalarAsync())!;
                    tagIdMap[tag] = id;
                }

                // 4) 插入文件和 FileTags
                foreach (var item in FileItems)
                {
                    long fileId;
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText =
                            "INSERT INTO Files (FilePath) VALUES ($p); " +
                            "SELECT last_insert_rowid();";
                        cmd.Parameters.AddWithValue("$p", item.FilePath);
                        fileId = (long)(await cmd.ExecuteScalarAsync())!;
                    }

                    foreach (var tag in item.Tags)
                    {
                        if (!tagIdMap.TryGetValue(tag, out var tagId)) continue;

                        using var cmd = conn.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText =
                            "INSERT OR IGNORE INTO FileTags (FileId, TagId) VALUES ($f, $t);";
                        cmd.Parameters.AddWithValue("$f", fileId);
                        cmd.Parameters.AddWithValue("$t", tagId);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                tx.Commit();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存数据失败: {ex.Message}");
            }
            finally
            {
                _writeLock.Release();
            }
        }

        // ---------------- 数据库结构 ----------------

        private static async Task EnsureSchemaAsync(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                PRAGMA foreign_keys = ON;

                CREATE TABLE IF NOT EXISTS Files (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FilePath TEXT NOT NULL UNIQUE
                );

                CREATE TABLE IF NOT EXISTS Tags (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL UNIQUE,
                    Color TEXT
                );

                CREATE TABLE IF NOT EXISTS FileTags (
                    FileId INTEGER NOT NULL,
                    TagId INTEGER NOT NULL,
                    PRIMARY KEY (FileId, TagId),
                    FOREIGN KEY (FileId) REFERENCES Files(Id) ON DELETE CASCADE,
                    FOREIGN KEY (TagId) REFERENCES Tags(Id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS IX_FileTags_TagId ON FileTags(TagId);";

            await cmd.ExecuteNonQueryAsync();
        }

        // ---------------- 标签颜色 ----------------

        public static string? GetTagColor(string tagName)
        {
            if (string.IsNullOrEmpty(tagName)) return null;
            return _tagColors.TryGetValue(tagName, out var hex) ? hex : null;
        }

        public static void SetTagColor(string tagName, string? colorHex)
        {
            if (string.IsNullOrEmpty(tagName)) return;

            if (string.IsNullOrEmpty(colorHex))
                _tagColors.Remove(tagName);
            else
                _tagColors[tagName] = colorHex;
        }

        // 颜色写入数据库，和 SaveAsync 走同一条路径
        public static Task SaveTagColorsAsync() => SaveAsync();

        // ---------------- （废弃）数据位置切换 ----------------
        /*
        public static async Task<bool> ChangeDataFolderAsync(string newFolder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(newFolder))
                    return false;

                newFolder = newFolder.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

                Directory.CreateDirectory(newFolder);

                var newDbFile = Path.Combine(newFolder, DbFileName);

                // 确保数据已落盘
                await SaveAsync();

                // 复制数据库文件
                if (!string.Equals(_dbFile, newDbFile, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(_dbFile))
                {
                    File.Copy(_dbFile, newDbFile, overwrite: true);
                }

                // 更新设置
                var defaultFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    DefaultFolderName);

                if (string.Equals(newFolder, defaultFolder, StringComparison.OrdinalIgnoreCase))
                    ApplicationData.Current.LocalSettings.Values.Remove(CustomPathKey);
                else
                    ApplicationData.Current.LocalSettings.Values[CustomPathKey] = newFolder;

                // 更新路径
                _dataFolder = newFolder;
                _dbFile = newDbFile;

                // 重新加载
                _loadTask = null;
                await EnsureLoadedAsync();

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"更改数据位置失败: {ex.Message}");
                return false;
            }
        }
        */
        private static void CopyFileRaw(string source, string dest)
        {
            using var src = new FileStream(
                source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var dst = new FileStream(
                dest, FileMode.Create, FileAccess.Write, FileShare.None);
            src.CopyTo(dst);
        }
        // ---------------- 导入 / 导出 ----------------
        // 导出当前数据库到指定路径。
        public static async Task<bool> ExportAsync(string destPath)
        {
            if (string.IsNullOrWhiteSpace(destPath)) return false;

            try
            {
                await SaveAsync();

                await _writeLock.WaitAsync();
                try
                {
                    if (!File.Exists(_dbFile))
                        return false;

                    var dir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
                    SqliteConnection.ClearAllPools();
                    CopyFileRaw(_dbFile, destPath);
                    return true;
                }
                finally
                {
                    _writeLock.Release();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"导出失败: {ex.Message}");
                return false;
            }
        }
        // 从指定路径导入数据库，覆盖当前数据。成功后重新加载 FileItems。
        public static async Task<bool> ImportAsync(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                return false;

            if (!IsSqliteFile(sourcePath))
                return false;

            try
            {
                await _writeLock.WaitAsync();
                try
                {
                    Directory.CreateDirectory(_dataFolder);
                    SqliteConnection.ClearAllPools();
                    CopyFileRaw(sourcePath, _dbFile);
                }
                finally
                {
                    _writeLock.Release();
                }

                _loadTask = null;
                await EnsureLoadedAsync();
                _loadTask = null;
                await EnsureLoadedAsync();

                UndoService.Clear();   // ← 新增：导入后清空撤销历史
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"导入失败: {ex.Message}");
                return false;
            }
        }
        // 校验文件头，拒绝明显不是 SQLite 的文件
        private static bool IsSqliteFile(string path)
        {
            try
            {
                using var fs = File.OpenRead(path);
                if (fs.Length < 16) return false;

                var header = new byte[16];
                int read = fs.Read(header, 0, 16);
                if (read < 16) return false;

                var text = System.Text.Encoding.ASCII.GetString(header, 0, 15);
                return text == "SQLite format 3";
            }
            catch
            {
                return false;
            }
        }

        // ---------------- 排序 ----------------

        public static IEnumerable<FileTagItem> ApplySort(
            IEnumerable<FileTagItem> source,
            FileSortMode mode)
        {
            return mode switch
            {
                FileSortMode.NameAsc => source
                    .OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase),

                FileSortMode.NameDesc => source
                    .OrderByDescending(f => f.FileName, StringComparer.OrdinalIgnoreCase),

                FileSortMode.ModifiedDesc => source
                    .OrderByDescending(f => f.ModifiedTime ?? DateTimeOffset.MinValue),

                FileSortMode.ModifiedAsc => source
                    .OrderBy(f => f.ModifiedTime ?? DateTimeOffset.MaxValue),

                _ => source
            };
        }
    }
}