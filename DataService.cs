using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Tagmgr
{
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
    }
}