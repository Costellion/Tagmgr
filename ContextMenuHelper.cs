using Microsoft.UI.Xaml.Controls;

namespace Tagmgr
{
    /// <summary>
    /// 为文件项构建右键菜单。
    /// 每个 MenuFlyoutItem 的 Tag 是动作标识，由调用方在 Click 事件里区分。
    /// </summary>
    public static class ContextMenuHelper
    {
        // 动作标识
        public const string ActionOpen = "Open";
        public const string ActionOpenFolder = "OpenFolder";
        public const string ActionCopyPath = "CopyPath";
        public const string ActionCopyName = "CopyName";
        public const string ActionDelete = "Delete";

        /// <summary>
        /// 构建文件项的右键菜单。
        /// </summary>
        public static MenuFlyout BuildFileMenu()
        {
            var menu = new MenuFlyout();

            menu.Items.Add(CreateItem("打开文件", ActionOpen, "\uE8E5"));
            menu.Items.Add(CreateItem("打开所在文件夹", ActionOpenFolder, "\uE838"));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(CreateItem("复制完整路径", ActionCopyPath, "\uE8C8"));
            menu.Items.Add(CreateItem("复制文件名", ActionCopyName, "\uE8C8"));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(CreateItem("删除记录", ActionDelete, "\uE74D"));

            return menu;
        }

        private static MenuFlyoutItem CreateItem(string text, string action, string glyph)
        {
            var item = new MenuFlyoutItem
            {
                Text = text,
                Tag = action
            };

            if (!string.IsNullOrEmpty(glyph))
                item.Icon = new FontIcon { Glyph = glyph };

            return item;
        }
        public static MenuFlyout BuildReadOnlyFileMenu()
        {
            var menu = new MenuFlyout();

            menu.Items.Add(CreateItem("打开文件", ActionOpen, "\uE8E5"));
            menu.Items.Add(CreateItem("打开所在文件夹", ActionOpenFolder, "\uE838"));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(CreateItem("复制完整路径", ActionCopyPath, "\uE8C8"));
            menu.Items.Add(CreateItem("复制文件名", ActionCopyName, "\uE8C8"));

            return menu;
        }
    }
}