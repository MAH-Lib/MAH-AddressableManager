using System;
using System.Collections.Generic;
using MAH.AddressableManager.Core;
using MAH.AddressableManager.Core.Loading;
using MAH.AddressableManager.Editor.Localization;
using MAH.AddressableManager.Editor.SettingsSync;
using UnityEditor;
using UnityEngine;

namespace MAH.AddressableManager.Editor.Window
{
    /// <summary>
    /// 「リーク」タブ。明示 Release されずに寿命回収されたチケット（リーク）を、確保元ごとに集約した行で一覧する。
    /// 行には、発生回数・最後に発生したスコープ・最終発生時刻・呼び出し元（最初の利用者のフレーム）へのジャンプがあり、開くとスタックトレースを見られる。
    /// 「表示をクリア」は、Runtime の記録を消さず、このタブが「クリア時点の基準」を持って、以降の発生分だけを表示する（Window を読み取り専用に保つため）。
    /// 表示用の文字列は、描画のたびに作らないよう、取得時（Refresh）にまとめて作る。読み取りは IAddressableDebugReader 越しに行い、窓口は保持しない。
    /// </summary>
    internal sealed class LeakListTab : IDebugWindowTab
    {
        private const string TIME_FORMAT = "HH:mm:ss";

        // 色は Dark / Light どちらのスキンでも読める中間の明るさにする。実行時状態タブの寿命の色（Scene：緑・Owner：橙）に合わせる
        private static readonly Color SCENE_COLOR = new(0.15f, 0.65f, 0.55f);
        private static readonly Color OWNER_COLOR = new(0.95f, 0.55f, 0.15f);
        private static readonly Color FALLBACK_COLOR = new(0.60f, 0.60f, 0.60f);

        // GUILayout.Width は呼ぶたびに確保するため、使い回す
        private static readonly GUILayoutOption[] CLEAR_WIDTH = { GUILayout.Width(88f) };
        private static readonly GUILayoutOption[] EXPORT_WIDTH = { GUILayout.Width(80f) };
        private static readonly GUILayoutOption[] GROUP_WIDTH = { GUILayout.Width(72f) };
        private static readonly GUILayoutOption[] COUNT_WIDTH = { GUILayout.Width(52f) };
        private static readonly GUILayoutOption[] TIME_WIDTH = { GUILayout.Width(64f) };
        private static readonly GUILayoutOption[] JUMP_WIDTH = { GUILayout.Width(170f) };

        private readonly List<LeakSnapshot> _leaks = new();
        // 表示する行（基準より新しい発生があるもの）の _leaks 上の添字。最終発生の新しい順
        private readonly List<int> _visible = new();
        // 表示用。_visible と同じ添字で対応する（Refresh のたびに作り直す）
        private readonly List<GUIContent> _rowTitles = new(); // GUIContent は使い回す（縮めない）
        private readonly List<int> _rowCounts = new(); // 行ごとの回数（クリア以降の分）。ログ出力に渡す
        private readonly List<string> _rowCountLabels = new();
        private readonly List<string> _rowTimeLabels = new();
        private readonly List<string> _rowScopeLabels = new();
        private readonly List<GUIContent> _rowJumpContents = new(); // 最初の利用者のフレームへのジャンプのボタン。使い回す
        private readonly List<TraceFrame> _rowJumpFrames = new(); // ジャンプできない行は default（AccCanJump が false）
        private readonly HashSet<int> _expandedRows = new(); // 開いている行の ID だけを保持する。Clear で空にする
        // 「表示をクリア」の基準。通し番号がこれ以下の行は隠し、行ごとの回数は、クリア時点の回数を引いて数える
        private readonly Dictionary<int, int> _clearedCounts = new();
        private readonly StackTraceView _stackTraceView = new(); // スタックトレースの表示とジャンプ（「実行時状態」タブと共通）
        private readonly Comparison<int> _visibleComparison; // クロージャを毎回作らないよう、キャッシュする
        private readonly GUIContent _clearContent = new(); // 文言は、言語が変わっても追従するよう、描画のたびに設定する
        private readonly GUIContent _exportContent = new();
        private long _clearedSequence;
        // 直近の BuildRows の入力（行の数と最大の通し番号）。Refresh で、変化が無いときに作り直しを省くために覚える
        private int _builtLeakCount;
        private long _builtMaxSequence;
        private bool _pendingClear; // ボタンを押した次のレイアウトで反映する（描画の途中で、行の数を変えないため）
        private string _summaryLabel;
        private Vector2 _scroll;
        // スタイルは EditorStyles が OnGUI の中でしか使えないため、最初の描画で作る
        private GUIStyle _sceneStyle;
        private GUIStyle _ownerStyle;
        private GUIStyle _otherStyle;
        private GUIStyle _countStyle;
        private GUIStyle _groupStyle;

        public string AccTitle => EditorText.Get("debug.tab.leak");

        internal LeakListTab()
        {
            _visibleComparison = CompareVisible;
            BuildSummary(0, 0);
        }

        public void Refresh(IAddressableDebugReader reader)
        {
            reader.CopyLeaksTo(_leaks);

            // 行の数と最後の発生の通し番号が前回の組み立てと同じなら、表示用の文字列を作り直さない（新しい発生は、必ず通し番号を進める）
            if (_leaks.Count == _builtLeakCount && GetMaxSequence() == _builtMaxSequence) return;
            BuildRows();
        }

        public void Clear()
        {
            _leaks.Clear();
            _visible.Clear();
            _rowCounts.Clear();
            _rowCountLabels.Clear();
            _rowTimeLabels.Clear();
            _rowScopeLabels.Clear();
            _rowJumpFrames.Clear();
            _expandedRows.Clear();
            _clearedCounts.Clear();
            _clearedSequence = 0;
            _builtLeakCount = 0;
            _builtMaxSequence = 0;
            _pendingClear = false;
            _stackTraceView.Clear();
            _scroll = Vector2.zero;
            BuildSummary(0, 0);
            // _rowTitles・_rowJumpContents の GUIContent は、次の Refresh で使い回すため残す
        }

        public void InvalidateLabels()
        {
            // Refresh の省略判定（行の数・通し番号が同じなら作り直さない）に当たらないようにし、次の Refresh で新しい言語の文字列を作り直す
            _builtLeakCount = -1;
            // 未起動のときは Refresh が呼ばれないため、見出しだけは今の件数で作り直す
            BuildSummary(CountVisibleTotal(), _visible.Count);
        }

        // 表示中の行の回数の合計（クリア以降の分）
        private int CountVisibleTotal()
        {
            var total = 0;
            for (var i = 0; i < _rowCounts.Count; i++) total += _rowCounts[i];
            return total;
        }

        public void Draw()
        {
            EnsureStyles();

            // ボタンが押された次のレイアウトの先頭で反映する。同じ描画サイクルの Layout と Repaint で、行の数がそろう
            if (_pendingClear && Event.current.type == EventType.Layout)
                ApplyClear();

            // 言語が変わっても追従するよう、描画のたびに設定する（キャッシュ済みの文字列を代入するだけで、確保はしない）
            _exportContent.text = EditorText.Get("leak.export.label");
            _exportContent.tooltip = EditorText.Get("leak.export.tooltip");
            _clearContent.text = EditorText.Get("leak.clear.label");
            _clearContent.tooltip = EditorText.Get("leak.clear.tooltip");

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(_summaryLabel, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            // 表示中の行が無いときは、出力するものが無いため押せない。ファイルへ書くだけで、行の数は変わらない
            EditorGUI.BeginDisabledGroup(_visible.Count == 0);
            if (GUILayout.Button(_exportContent, EditorStyles.toolbarButton, EXPORT_WIDTH))
                ExportLog();
            EditorGUI.EndDisabledGroup();
            if (GUILayout.Button(_clearContent, EditorStyles.toolbarButton, CLEAR_WIDTH))
                _pendingClear = true;
            EditorGUILayout.EndHorizontal();

            if (_visible.Count == 0)
            {
                EditorGUILayout.HelpBox(EditorText.Get("leak.empty"), MessageType.None);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (var i = 0; i < _visible.Count; i++)
                DrawRow(i);
            EditorGUILayout.EndScrollView();
        }

        private void DrawRow(int row)
        {
            var leak = _leaks[_visible[row]];
            var was_open = _expandedRows.Contains(leak.AccId);

            EditorGUILayout.BeginHorizontal();
            var is_open = EditorGUILayout.Foldout(was_open, _rowTitles[row], true);
            if (leak.AccIsGroup)
                GUILayout.Label(EditorText.Get("common.labelGroup"), _groupStyle, GROUP_WIDTH);
            GUILayout.Label(_rowCountLabels[row], _countStyle, COUNT_WIDTH);
            GUILayout.Label(_rowTimeLabels[row], EditorStyles.miniLabel, TIME_WIDTH);
            if (_rowJumpFrames[row].AccCanJump)
            {
                if (GUILayout.Button(_rowJumpContents[row], EditorStyles.miniButton, JUMP_WIDTH))
                    StackTraceView.Jump(_rowJumpFrames[row]);
            }
            else
            {
                GUILayout.Label(string.Empty, JUMP_WIDTH); // ジャンプできない行も、右端の位置をそろえる
            }
            EditorGUILayout.EndHorizontal();

            if (is_open != was_open)
            {
                if (is_open) _expandedRows.Add(leak.AccId);
                else _expandedRows.Remove(leak.AccId);
            }

            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField(_rowScopeLabels[row], GetScopeStyle(leak.AccScopeKind));
            if (is_open) _stackTraceView.Draw(leak.AccStackTrace);
            EditorGUI.indentLevel--;
        }

        // 表示中の行（クリア以降の分。回数もクリア以降の回数）を、設定の出力先に、日時付きのファイルとして書き出す。
        // 出力先は押したときに読む（SO が編集されても追従する）。書けなかったときは、LeakLogWriter が警告を出す
        private void ExportLog()
        {
            var project_root = EditorProjectPath.GetProjectRoot();
            var folder = LeakLogWriter.ResolveFolder(project_root, AddressableEditorSettingsLocator.FindLeakLogSettings().AccOutputFolder);
            var now = DateTime.Now;
            var text = LeakLogFormatter.Format(now, _leaks, _visible, _rowCounts, project_root);

            var path = LeakLogWriter.WriteManual(text, folder, now);
            if (path != null)
                AddressableLogger.LogInfo(EditorText.Format("leak.exported", path));
        }

        // 取得済みの全行を、クリア時点の基準として覚える。以降は、これより新しい発生だけを表示する
        private void ApplyClear()
        {
            _pendingClear = false;
            _clearedCounts.Clear();
            foreach (var leak in _leaks)
            {
                _clearedCounts[leak.AccId] = leak.AccCount;
                if (leak.AccSequence > _clearedSequence) _clearedSequence = leak.AccSequence;
            }
            BuildRows();
        }

        // 基準より新しい発生がある行を選び、最終発生の新しい順に並べて、表示用の文字列を作る
        private void BuildRows()
        {
            _visible.Clear();
            _rowCounts.Clear();
            _rowCountLabels.Clear();
            _rowTimeLabels.Clear();
            _rowScopeLabels.Clear();
            _rowJumpFrames.Clear();

            _builtLeakCount = _leaks.Count;
            _builtMaxSequence = GetMaxSequence();

            var total_count = 0;
            for (var i = 0; i < _leaks.Count; i++)
            {
                if (_leaks[i].AccSequence <= _clearedSequence) continue;
                _visible.Add(i);
            }
            _visible.Sort(_visibleComparison);

            for (var row = 0; row < _visible.Count; row++)
            {
                var leak = _leaks[_visible[row]];
                var count = leak.AccCount - (_clearedCounts.TryGetValue(leak.AccId, out var cleared) ? cleared : 0);
                total_count += count;
                _rowCounts.Add(count);

                if (row >= _rowTitles.Count) _rowTitles.Add(new GUIContent());
                var title = _rowTitles[row];
                title.text = LeakLogFormatter.BuildTitle(leak);
                title.tooltip = title.text; // 長いキーが切れたときに、マウスオーバーで全体を見られるようにする

                _rowCountLabels.Add($"×{count}");
                _rowTimeLabels.Add(leak.AccLastTime.ToString(TIME_FORMAT));
                _rowScopeLabels.Add($"■ {LeakLogFormatter.BuildScopeName(leak)}");

                if (row >= _rowJumpContents.Count) _rowJumpContents.Add(new GUIContent());
                if (_stackTraceView.TryGetFirstUserFrame(leak.AccStackTrace, out var frame))
                {
                    var jump = _rowJumpContents[row];
                    jump.text = $"{frame.AccFileName}:{frame.AccLine}";
                    jump.tooltip = $"{frame.AccPath}:{frame.AccLine}";
                    _rowJumpFrames.Add(frame);
                }
                else
                {
                    _rowJumpFrames.Add(default);
                }
            }

            BuildSummary(total_count, _visible.Count);
        }

        private long GetMaxSequence()
        {
            long max_sequence = 0;
            foreach (var leak in _leaks)
            {
                if (leak.AccSequence > max_sequence) max_sequence = leak.AccSequence;
            }
            return max_sequence;
        }

        private void BuildSummary(int total_count, int row_count)
            => _summaryLabel = EditorText.Format("leak.summary", total_count, row_count);

        // 最終発生の新しい順。long の CompareTo は箱化しない
        private int CompareVisible(int left_index, int right_index) => _leaks[right_index].AccSequence.CompareTo(_leaks[left_index].AccSequence);

        private GUIStyle GetScopeStyle(LifetimeKind kind)
        {
            switch (kind)
            {
                case LifetimeKind.Scene: return _sceneStyle;
                case LifetimeKind.Owner: return _ownerStyle;
                default: return _otherStyle;
            }
        }

        private void EnsureStyles()
        {
            if (_sceneStyle != null) return;

            _sceneStyle = CreateColoredStyle(EditorStyles.label, SCENE_COLOR);
            _ownerStyle = CreateColoredStyle(EditorStyles.label, OWNER_COLOR);
            _otherStyle = CreateColoredStyle(EditorStyles.label, FALLBACK_COLOR);
            _countStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleRight };
            _groupStyle = new GUIStyle(EditorStyles.miniButton) { alignment = TextAnchor.MiddleCenter };
        }

        private static GUIStyle CreateColoredStyle(GUIStyle source, Color color)
        {
            var style = new GUIStyle(source);
            style.normal.textColor = color;
            return style;
        }
    }
}
