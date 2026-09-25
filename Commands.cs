using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Tagmgr
{
    // ============================================================
    //  命令接口
    // ============================================================

    /// <summary>
    /// 可撤销命令的统一接口。
    /// </summary>
    public interface IUndoableCommand
    {
        string Description { get; }
        Task RedoAsync();
        Task UndoAsync();
    }

    // ============================================================
    //  撤销 / 重做服务
    // ============================================================

    /// <summary>
    /// 全局撤销 / 重做服务。
    /// 所有数据修改都通过 ExecuteAsync 提交命令，内部负责保存和通知刷新。
    /// </summary>
    public static class UndoService
    {
        private const int MaxDepth = 50;

        private static readonly List<IUndoableCommand> _undo = new();
        private static readonly List<IUndoableCommand> _redo = new();
        private static readonly System.Threading.SemaphoreSlim _lock = new(1, 1);

        /// <summary>撤销栈或重做栈发生变化时触发。</summary>
        public static event Action? StateChanged;

        public static bool CanUndo
        {
            get { return _undo.Count > 0; }
        }

        public static bool CanRedo
        {
            get { return _redo.Count > 0; }
        }

        /// <summary>
        /// 执行一个命令并压入撤销栈。新命令会清空重做栈。
        /// </summary>
        public static async Task ExecuteAsync(IUndoableCommand command)
        {
            if (command == null) return;

            await _lock.WaitAsync();
            try
            {
                await command.RedoAsync();

                _undo.Add(command);
                if (_undo.Count > MaxDepth)
                    _undo.RemoveAt(0);

                _redo.Clear();

                await SaveAndNotifyAsync();
            }
            finally
            {
                _lock.Release();
            }

            NotifyStateChanged();
        }

        public static async Task UndoAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (_undo.Count == 0) return;

                var cmd = _undo[_undo.Count - 1];
                _undo.RemoveAt(_undo.Count - 1);

                await cmd.UndoAsync();
                _redo.Add(cmd);

                await SaveAndNotifyAsync();
            }
            finally
            {
                _lock.Release();
            }

            NotifyStateChanged();
        }

        public static async Task RedoAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (_redo.Count == 0) return;

                var cmd = _redo[_redo.Count - 1];
                _redo.RemoveAt(_redo.Count - 1);

                await cmd.RedoAsync();
                _undo.Add(cmd);

                await SaveAndNotifyAsync();
            }
            finally
            {
                _lock.Release();
            }

            NotifyStateChanged();
        }

        /// <summary>清空撤销与重做栈。数据导入后应调用。</summary>
        public static void Clear()
        {
            _undo.Clear();
            _redo.Clear();
            NotifyStateChanged();
        }

        // 每次执行、撤销或重做后，统一保存并通知界面刷新
        private static async Task SaveAndNotifyAsync()
        {
            await DataService.SaveAsync();
            DataService.NotifyDataChanged();
        }

        private static void NotifyStateChanged()
        {
            if (StateChanged != null)
            {
                StateChanged.Invoke();
            }
        }
    }

    // ============================================================
    //  标签：添加 / 移除
    // ============================================================

    /// <summary>
    /// 给一组文件添加或移除同一个标签。
    /// 两个方向的逻辑完全对称，只是正反操作互换，共用一个基类。
    /// </summary>
    public abstract class TagToggleCommand : IUndoableCommand
    {
        protected readonly string Tag;
        protected readonly List<FileTagItem> Files;

        protected TagToggleCommand(IEnumerable<FileTagItem> files, string tag)
        {
            Tag = tag;
            Files = files.Where(f => NeedsChange(f)).ToList();
        }

        // 该文件是否需要变更，避免对已经是目标状态的文件做无用操作
        protected abstract bool NeedsChange(FileTagItem file);

        // 正向执行（Redo）
        protected abstract void Apply(FileTagItem file);

        // 反向执行（Undo）
        protected abstract void Revert(FileTagItem file);

        public abstract string Description { get; }

        public Task RedoAsync()
        {
            foreach (var f in Files)
                Apply(f);
            return Task.CompletedTask;
        }

        public Task UndoAsync()
        {
            foreach (var f in Files)
                Revert(f);
            return Task.CompletedTask;
        }
    }

    /// <summary>给一组文件添加同一个标签。</summary>
    public class AddTagCommand : TagToggleCommand
    {
        public AddTagCommand(IEnumerable<FileTagItem> files, string tag)
            : base(files, tag) { }

        protected override bool NeedsChange(FileTagItem file)
        {
            return !file.Tags.Contains(Tag);
        }

        protected override void Apply(FileTagItem file)
        {
            if (!file.Tags.Contains(Tag))
                file.Tags.Add(Tag);
        }

        protected override void Revert(FileTagItem file)
        {
            file.Tags.Remove(Tag);
        }

        public override string Description
        {
            get { return $"添加标签“{Tag}”"; }
        }
    }

    /// <summary>从一组文件中移除同一个标签。</summary>
    public class RemoveTagCommand : TagToggleCommand
    {
        public RemoveTagCommand(IEnumerable<FileTagItem> files, string tag)
            : base(files, tag) { }

        protected override bool NeedsChange(FileTagItem file)
        {
            return file.Tags.Contains(Tag);
        }

        protected override void Apply(FileTagItem file)
        {
            file.Tags.Remove(Tag);
        }

        protected override void Revert(FileTagItem file)
        {
            if (!file.Tags.Contains(Tag))
                file.Tags.Add(Tag);
        }

        public override string Description
        {
            get { return $"移除标签“{Tag}”"; }
        }
    }

    // ============================================================
    //  文件记录：添加 / 删除
    // ============================================================

    /// <summary>添加若干文件记录。</summary>
    public class AddFilesCommand : IUndoableCommand
    {
        private readonly List<FileTagItem> _files;

        public AddFilesCommand(IEnumerable<FileTagItem> files)
        {
            _files = files.ToList();
        }

        public string Description
        {
            get { return $"添加 {_files.Count} 个文件记录"; }
        }

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

    /// <summary>
    /// 删除若干文件记录（不触碰磁盘文件）。
    /// 记录每项在集合中的原始索引，撤销时恢复到原来的位置。
    /// </summary>
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

        public string Description
        {
            get { return $"删除 {_items.Count} 个文件记录"; }
        }

        public Task RedoAsync()
        {
            // 倒序删除，避免索引偏移
            foreach (var tuple in _items.OrderByDescending(t => t.Index))
                DataService.FileItems.Remove(tuple.Item);
            return Task.CompletedTask;
        }

        public Task UndoAsync()
        {
            foreach (var tuple in _items)
            {
                if (DataService.FileItems.Contains(tuple.Item)) continue;

                var index = Math.Min(tuple.Index, DataService.FileItems.Count);
                DataService.FileItems.Insert(index, tuple.Item);
            }
            return Task.CompletedTask;
        }
    }

    // ============================================================
    //  标签：重命名 / 删除 / 合并 / 修改颜色
    // ============================================================

    /// <summary>重命名标签，所有拥有旧标签的文件都替换为新标签。</summary>
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

        public string Description
        {
            get { return $"重命名“{_oldName}”为“{_newName}”"; }
        }

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

    /// <summary>删除若干标签，从所有文件中移除这些标签。</summary>
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

        public string Description
        {
            get { return $"删除 {_names.Count} 个标签"; }
        }

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

    /// <summary>把若干标签合并到一个目标标签。</summary>
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

        public string Description
        {
            get { return $"合并 {_sourceNames.Count} 个标签到“{_targetName}”"; }
        }

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

            // 颜色迁移：目标没有颜色时，从源标签继承一个
            if (string.IsNullOrEmpty(_targetOldColor))
            {
                string? sourceColor = null;
                foreach (var name in _sourceNames)
                {
                    if (_sourceColors.TryGetValue(name, out var c) &&
                        !string.IsNullOrEmpty(c))
                    {
                        sourceColor = c;
                        break;
                    }
                }

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

    /// <summary>修改标签颜色。</summary>
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

        public string Description
        {
            get { return $"修改“{_tagName}”的颜色"; }
        }

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