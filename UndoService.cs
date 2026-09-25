using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tagmgr
{
    /// <summary>
    /// 可撤销命令的统一接口。
    /// </summary>
    public interface IUndoableCommand
    {
        string Description { get; }
        Task RedoAsync();
        Task UndoAsync();
    }

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

        public static bool CanUndo => _undo.Count > 0;
        public static bool CanRedo => _redo.Count > 0;

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

                await DataService.SaveAsync();
                DataService.NotifyDataChanged();
            }
            finally
            {
                _lock.Release();
            }

            StateChanged?.Invoke();
        }

        public static async Task UndoAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (_undo.Count == 0) return;

                var cmd = _undo[^1];
                _undo.RemoveAt(_undo.Count - 1);

                await cmd.UndoAsync();
                _redo.Add(cmd);

                await DataService.SaveAsync();
                DataService.NotifyDataChanged();
            }
            finally
            {
                _lock.Release();
            }

            StateChanged?.Invoke();
        }

        public static async Task RedoAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (_redo.Count == 0) return;

                var cmd = _redo[^1];
                _redo.RemoveAt(_redo.Count - 1);

                await cmd.RedoAsync();
                _undo.Add(cmd);

                await DataService.SaveAsync();
                DataService.NotifyDataChanged();
            }
            finally
            {
                _lock.Release();
            }

            StateChanged?.Invoke();
        }

        /// <summary>清空撤销与重做栈。数据导入后应调用。</summary>
        public static void Clear()
        {
            _undo.Clear();
            _redo.Clear();
            StateChanged?.Invoke();
        }
    }
}