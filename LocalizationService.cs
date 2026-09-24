using Microsoft.Windows.ApplicationModel.Resources;

namespace Tagmgr
{
    public static class LocalizationService
    {
        private static readonly ResourceLoader _loader = new();

        /// <summary>
        /// 按 key 获取本地化字符串。找不到时返回 key 本身，便于排查。
        /// </summary>
        public static string Get(string key)
        {
            var value = _loader.GetString(key);
            return string.IsNullOrEmpty(value) ? key : value;
        }
    }
}