using UnityEditor;

namespace MAH.AddressableManager.Editor.Localization
{
    /// <summary>
    /// 言語ファイル（MAH.AddressableManager.Editor.&lt;言語コード&gt;.json）のインポート・削除・移動のときだけ、言語ファイルを読み直す。
    /// ドメインのリロード後にも1回読み直す（起動直後は AssetDatabase が使えず、利用者分を読めていないことがあるため）。
    /// 読み直しは、AssetDatabase の Refresh の完了後（delayCall）に行う。Refresh の最中は EditorApplication.isUpdating が true で、
    /// 利用者のファイルを読み飛ばしてしまうため。
    /// </summary>
    internal sealed class EditorLanguagePostprocessor : AssetPostprocessor
    {
        // 同じ Refresh の中で何度も予約しないよう、1回にまとめる。delegate は毎回作らないよう、キャッシュする
        private static readonly EditorApplication.CallbackFunction ReloadCallback = RunScheduledReload;
        private static bool _isReloadScheduled;

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] moved_from, bool did_domain_reload)
        {
            if (did_domain_reload || ContainsLanguageFile(imported) || ContainsLanguageFile(deleted) || ContainsLanguageFile(moved) || ContainsLanguageFile(moved_from))
                ScheduleReload();
        }

        private static void ScheduleReload()
        {
            if (_isReloadScheduled) return;

            _isReloadScheduled = true;
            EditorApplication.delayCall += ReloadCallback;
        }

        private static void RunScheduledReload()
        {
            _isReloadScheduled = false;
            EditorText.ReloadForAssetChange();
        }

        private static bool ContainsLanguageFile(string[] paths)
        {
            foreach (var path in paths)
            {
                if (EditorLanguageFileLocator.IsLanguageFilePath(path)) return true;
            }
            return false;
        }
    }
}
