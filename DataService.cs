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
    public static class DataService
    {
        // 全局唯一的文件记录集合，两个页面共享同一实例
        public static ObservableCollection<FileTagItem> FileItems { get; } = new();

        private static readonly string _dataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tagmgr");

        private static readonly string _dataFile = Path.Combine(_dataFolder, "tags.json");

        // 防止重复加载
        private static Task? _loadTask;

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