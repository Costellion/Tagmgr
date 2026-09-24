using Windows.Storage;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Tagmgr
{
    public enum FileSortMode
    {
        NameAsc,        // 名称 A-Z
        NameDesc,       // 名称 Z-A
        ModifiedDesc,   // 修改时间 新 → 旧
        ModifiedAsc     // 修改时间 旧 → 新
    }
    public class TagInfo
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
        public string CountText => $"{Count} 个文件";
    }
    public static class DataService
    {
        // 全局唯一的文件记录集合，两个页面共享同一实例
        public static ObservableCollection<FileTagItem> FileItems { get; } = new();

        private const string FileName = "tags.json";
        private const string CustomPathKey = "CustomDataPath";
        private const string DefaultFolderName = "Tagmgr";

        private static string _dataFolder;
        private static string _dataFile;

        // 当前数据文件夹与数据文件路径
        public static string DataFolder => _dataFolder;
        public static string DataFile => _dataFile;

        // 防止重复加载
        private static Task? _loadTask;

        static DataService()
        {
            var custom = ApplicationData.Current.LocalSettings.Values[CustomPathKey] as string;

            _dataFolder = string.IsNullOrEmpty(custom)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    DefaultFolderName)
                : custom;

            _dataFile = Path.Combine(_dataFolder, FileName);
        }

        public static Task EnsureLoadedAsync()
        {
            return _loadTask ??= LoadAsync();
        }

        private static async Task LoadAsync()
        {
            try
            {
                if (!File.Exists(_dataFile)) return;

                var json = await File.ReadAllTextAsync(_dataFile);
                var list = JsonSerializer.Deserialize<List<FileTagItem>>(json);
                if (list == null) return;

                FileItems.Clear();
                foreach (var item in list)
                    FileItems.Add(item);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载数据失败: {ex.Message}");
            }
        }

        public static async Task SaveAsync()
        {
            try
            {
                Directory.CreateDirectory(_dataFolder);
                var json = JsonSerializer.Serialize(
                    FileItems,
                    new JsonSerializerOptions { WriteIndented = true });

                await File.WriteAllTextAsync(_dataFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存数据失败: {ex.Message}");
            }
        }
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

                var newFile = Path.Combine(newFolder, FileName);

                // 先把当前数据写盘，确保复制的是最新数据
                await SaveAsync();

                // 复制到新位置（如果路径不同）
                if (!string.Equals(_dataFile, newFile, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(_dataFile))
                {
                    File.Copy(_dataFile, newFile, overwrite: true);
                }

                // 更新设置：如果是默认文件夹，移除自定义设置
                var defaultFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    DefaultFolderName);

                if (string.Equals(newFolder, defaultFolder, StringComparison.OrdinalIgnoreCase))
                    ApplicationData.Current.LocalSettings.Values.Remove(CustomPathKey);
                else
                    ApplicationData.Current.LocalSettings.Values[CustomPathKey] = newFolder;

                // 更新内部路径
                _dataFolder = newFolder;
                _dataFile = newFile;

                // 重置加载缓存，从新位置重新读取
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

                // 修改时间为空（还没加载出来或读取失败）的排最后
                FileSortMode.ModifiedDesc => source
                    .OrderByDescending(f => f.ModifiedTime ?? DateTimeOffset.MinValue),

                FileSortMode.ModifiedAsc => source
                    .OrderBy(f => f.ModifiedTime ?? DateTimeOffset.MaxValue),

                _ => source
            };
        }
    }
}