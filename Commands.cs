using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Tagmgr
{
    // ==================== 添加标签 ====================
    public class AddTagCommand : IUndoableCommand
    {
        private readonly List<FileTagItem> _files;
        private readonly string _tag;

        public AddTagCommand(IEnumerable<FileTagItem> files, string tag)
        {
            _tag = tag;
            // 只记录原本没有该标签的文件
            _files = files.Where(f => !f.Tags.Contains(tag)).ToList();
        }

        public string Description => $"添加标签“{_tag}”";

        public Task RedoAsync()
        {
            foreach (var f in _files)
            {
                if (!f.Tags.Contains(_tag))
                    f.Tags.Add(_tag);
            }
            return Task.CompletedTask;
        }

        public Task UndoAsync()
        {
            foreach (var f in _files)
                f.Tags.Remove(_tag);
            return Task.CompletedTask;
        }
    }

    // ==================== 移除标签 ====================
    public class RemoveTagCommand : IUndoableCommand
    {
        private readonly List<FileTagItem> _files;
        private readonly string _tag;

        public RemoveTagCommand(IEnumerable<FileTagItem> files, string tag)
        {
            _tag = tag;
            // 只记录原本有该标签的文件
            _files = files.Where(f => f.Tags.Contains(tag)).ToList();
        }

        public string Description => $"移除标签“{_tag}”";

        public Task RedoAsync()
        {
            foreach (var f in _files)
                f.Tags.Remove(_tag);
            return Task.CompletedTask;
        }

        public Task UndoAsync()
        {
            foreach (var f in _files)
            {
                if (!f.Tags.Contains(_tag))
                    f.Tags.Add(_tag);
            }
            return Task.CompletedTask;
        }
    }

    // ==================== 添加文件记录 ====================
    public class AddFilesCommand : IUndoableCommand
    {
        private readonly List<FileTagItem> _files;

        public AddFilesCommand(IEnumerable<FileTagItem> files)
        {
            _files = files.ToList();
        }

        public string Description => $"添加 {_files.Count} 个文件记录";

        public Task RedoAsync()
        {
            foreach (var f in _files)
            {
                if (!DataService.FileItems.Contains(f))
                    DataService.FileItems.Add(f);
            }
            return Task.CompletedTask;
        }

        public Task UndoAsync()
        {
            foreach (var f in _files)
                DataService.FileItems.Remove(f);
            return Task.CompletedTask;
        }
    }

    // ==================== 删除文件记录 ====================
    public class DeleteFilesCommand : IUndoableCommand
    {
        private readonly List<(int Index, FileTagItem Item)> _items;

        public DeleteFilesCommand(IEnumerable<FileTagItem> files)
        {
            var collection = DataService.FileItems;
            _items = files
                .Select(f => (Index: collection.IndexOf(f), Item: f))
                .Where(t => t.Index >= 0)
                .OrderBy(t => t.Index)
                .ToList();
        }

        public string Description => $"删除 {_items.Count} 个文件记录";

        public Task RedoAsync()
        {
            // 倒序删除，避免索引偏移
            foreach (var (_, item) in _items.OrderByDescending(t => t.Index))
                DataService.FileItems.Remove(item);
            return Task.CompletedTask;
        }

        public Task UndoAsync()
        {
            foreach (var (index, item) in _items)
            {
                if (DataService.FileItems.Contains(item)) continue;
                var clamped = Math.Min(index, DataService.FileItems.Count);
                DataService.FileItems.Insert(clamped, item);
            }
            return Task.CompletedTask;
        }
    }

    // ==================== 重命名标签 ====================
    public class RenameTagCommand : IUndoableCommand
    {
        private readonly string _oldName;
        private readonly string _newName;
        private readonly Dictionary<FileTagItem, List<string>> _beforeTags = new();
        private readonly string? _oldColor;
        private readonly string? _newNameOldColor;

        public RenameTagCommand(string oldName, string newName)
        {
            _oldName = oldName;
            _newName = newName;

            // 快照：所有拥有旧标签或新标签的文件
            var affected = DataService.FileItems
                .Where(f => f.Tags.Contains(oldName) || f.Tags.Contains(newName))
                .ToList();

            foreach (var f in affected)
                _beforeTags[f] = f.Tags.ToList();

            _oldColor = DataService.GetTagColor(oldName);
            _newNameOldColor = DataService.GetTagColor(newName);
        }

        public string Description => $"重命名“{_oldName}”为“{_newName}”";

        public Task RedoAsync()
        {
            foreach (var f in _beforeTags.Keys)
            {
                if (f.Tags.Contains(_oldName))
                {
                    f.Tags.Remove(_oldName);
                    if (!f.Tags.Contains(_newName))
                        f.Tags.Add(_newName);
                }
            }

            // 颜色迁移：新标签没颜色时继承旧标签
            if (string.IsNullOrEmpty(_newNameOldColor) && !string.IsNullOrEmpty(_oldColor))
                DataService.SetTagColor(_newName, _oldColor);
            DataService.SetTagColor(_oldName, null);

            return Task.CompletedTask;
        }

        public Task UndoAsync()
        {
            foreach (var kv in _beforeTags)
            {
                kv.Key.Tags.Clear();
                foreach (var t in kv.Value)
                    kv.Key.Tags.Add(t);
            }

            DataService.SetTagColor(_oldName, _oldColor);
            DataService.SetTagColor(_newName, _newNameOldColor);

            return Task.CompletedTask;
        }
    }

    // ==================== 删除标签 ====================
    public class DeleteTagsCommand : IUndoableCommand
    {
        private readonly List<string> _names;
        private readonly Dictionary<string, List<FileTagItem>> _tagToFiles = new();
        private readonly Dictionary<string, string?> _tagColors = new();

        public DeleteTagsCommand(IEnumerable<string> names)
        {
            _names = names.ToList();

            foreach (var name in _names)
            {
                _tagToFiles[name] = DataService.FileItems
                    .Where(f => f.Tags.Contains(name))
                    .ToList();
                _tagColors[name] = DataService.GetTagColor(name);
            }
        }

        public string Description => $"删除 {_names.Count} 个标签";

        public Task RedoAsync()
        {
            foreach (var name in _names)
            {
                if (_tagToFiles.TryGetValue(name, out var files))
                {
                    foreach (var f in files)
                        f.Tags.Remove(name);
                }
                DataService.SetTagColor(name, null);
            }
            return Task.CompletedTask;
        }

        public Task UndoAsync()
        {
            foreach (var name in _names)
            {
                if (_tagToFiles.TryGetValue(name, out var files))
                {
                    foreach (var f in files)
                    {
                        if (!f.Tags.Contains(name))
                            f.Tags.Add(name);
                    }
                }
                DataService.SetTagColor(name, _tagColors[name]);
            }
            return Task.CompletedTask;
        }
    }

    // ==================== 合并标签 ====================
    public class MergeTagsCommand : IUndoableCommand
    {
        private readonly List<string> _sourceNames;
        private readonly string _targetName;
        private readonly Dictionary<FileTagItem, List<string>> _beforeTags = new();
        private readonly Dictionary<string, string?> _sourceColors = new();
        private readonly string? _targetOldColor;

        public MergeTagsCommand(IEnumerable<string> sourceNames, string targetName)
        {
            _targetName = targetName;
            _sourceNames = sourceNames.Where(n => n != targetName).ToList();

            _targetOldColor = DataService.GetTagColor(targetName);

            foreach (var name in _sourceNames)
                _sourceColors[name] = DataService.GetTagColor(name);

            // 快照：所有涉及源标签的文件
            var affected = DataService.FileItems
                .Where(f => _sourceNames.Any(n => f.Tags.Contains(n)))
                .ToList();

            foreach (var f in affected)
                _beforeTags[f] = f.Tags.ToList();
        }

        public string Description => $"合并 {_sourceNames.Count} 个标签到“{_targetName}”";

        public Task RedoAsync()
        {
            foreach (var f in _beforeTags.Keys)
            {
                bool changed = false;
                foreach (var name in _sourceNames)
                {
                    if (f.Tags.Remove(name))
                        changed = true;
                }

                if (changed && !f.Tags.Contains(_targetName))
                    f.Tags.Add(_targetName);
            }

            // 颜色迁移
            if (string.IsNullOrEmpty(_targetOldColor))
            {
                var sourceColor = _sourceNames
                    .Select(n => _sourceColors.TryGetValue(n, out var c) ? c : null)
                    .FirstOrDefault(c => !string.IsNullOrEmpty(c));
                if (!string.IsNullOrEmpty(sourceColor))
                    DataService.SetTagColor(_targetName, sourceColor);
            }

            foreach (var name in _sourceNames)
                DataService.SetTagColor(name, null);

            return Task.CompletedTask;
        }

        public Task UndoAsync()
        {
            foreach (var kv in _beforeTags)
            {
                kv.Key.Tags.Clear();
                foreach (var t in kv.Value)
                    kv.Key.Tags.Add(t);
            }

            DataService.SetTagColor(_targetName, _targetOldColor);
            foreach (var name in _sourceNames)
                DataService.SetTagColor(name, _sourceColors[name]);

            return Task.CompletedTask;
        }
    }

    // ==================== 修改标签颜色 ====================
    public class ChangeTagColorCommand : IUndoableCommand
    {
        private readonly string _tagName;
        private readonly string? _oldColor;
        private readonly string? _newColor;

        public ChangeTagColorCommand(string tagName, string? newColor)
        {
            _tagName = tagName;
            _oldColor = DataService.GetTagColor(tagName);
            _newColor = newColor;
        }

        public string Description => $"修改“{_tagName}”的颜色";

        public Task RedoAsync()
        {
            DataService.SetTagColor(_tagName, _newColor);
            return Task.CompletedTask;
        }

        public Task UndoAsync()
        {
            DataService.SetTagColor(_tagName, _oldColor);
            return Task.CompletedTask;
        }
    }
}