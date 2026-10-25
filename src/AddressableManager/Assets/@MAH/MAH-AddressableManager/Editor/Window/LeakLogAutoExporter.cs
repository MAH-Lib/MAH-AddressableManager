using System;
using System.Collections.Generic;
using MAH.AddressableManager.Core;
using MAH.AddressableManager.Editor.Localization;
using MAH.AddressableManager.Core.Loading;
using MAH.AddressableManager.Editor.SettingsSync;
using UnityEditor;

namespace MAH.AddressableManager.Editor.Window
{
    /// <summary>
    /// Play の終了時に、リークをログファイルへ自動で出力する（SO の自動出力が ON のときだけ）。
    /// AddressableDebugWindow とは独立しており、Window を開いていなくても動く。
    /// Runtime にイベントを足さず、Play の終了の直前（ExitingPlayMode。Manager がまだ生きている）に、Editor 側が窓口から読む。
    /// 出力は固定名の AddressableLeaks_LastPlay.txt に上書きし、前回分を -prev に1世代だけ残す（LeakLogWriter.WriteLastPlay）。
    /// リークが0件のときも「リークはありませんでした」で上書きする（前の Play のファイルが残って取り違えるのを防ぐ）。
    /// 窓口への参照は保持せず、毎回 AccInstance から取り直す。
    /// </summary>
    [InitializeOnLoad]
    internal static class LeakLogAutoExporter
    {
        static LeakLogAutoExporter()
        {
            // 静的コンストラクタはドメインのリロードごとに1回だけ呼ばれるため、購読が二重にならない
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingPlayMode) return;

            try
            {
                Export();
            }
            catch (Exception exception) // Play の終了を妨げないため、例外は握って警告に留める
            {
                AddressableLogger.LogWarning(EditorText.Format("leaklog.autoFailure", exception.Message));
            }
        }

        private static void Export()
        {
            if (!AddressableEditorSettingsLocator.FindLeakLogSettings().AccExportOnExitPlayMode) return;

            var manager = AddressableManager.AccInstance;
            var reader = manager != null ? manager.AccDebugReader : null;
            if (reader == null) return; // Manager が無い（Play 中に破棄された・生成されなかった）

            // 窓口から全リークを読み、最終発生の新しい順に並べる。回数は、そのセッションの全回数（タブの「表示をクリア」とは無関係）
            var leaks = new List<LeakSnapshot>();
            reader.CopyLeaksTo(leaks);
            leaks.Sort((left, right) => right.AccSequence.CompareTo(left.AccSequence));
            var rows = new List<int>(leaks.Count);
            var counts = new List<int>(leaks.Count);
            for (var i = 0; i < leaks.Count; i++)
            {
                rows.Add(i);
                counts.Add(leaks[i].AccCount);
            }

            var project_root = EditorProjectPath.GetProjectRoot();
            var folder = LeakLogWriter.ResolveFolder(project_root, AddressableEditorSettingsLocator.FindLeakLogSettings().AccOutputFolder);
            var text = LeakLogFormatter.Format(DateTime.Now, leaks, rows, counts, project_root);

            var path = LeakLogWriter.WriteLastPlay(text, folder);
            if (path != null)
                AddressableLogger.LogInfo(EditorText.Format("leaklog.autoExported", path));
        }
    }
}
