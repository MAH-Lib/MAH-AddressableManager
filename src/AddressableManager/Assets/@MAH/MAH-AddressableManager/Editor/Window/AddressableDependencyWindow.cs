using System;
using System.Collections.Generic;
using System.Globalization;
using MAH.AddressableManager.Core;
using MAH.AddressableManager.Editor.Localization;
using MAH.AddressableManager.Editor.SettingsSync;
using UnityEditor;
using UnityEngine;

namespace MAH.AddressableManager.Editor.Window
{
    /// <summary>
    /// Addressables に登録したアセット（親）が参照している、未登録のアセット（子）を一覧で確認するウィンドウ。Play もビルドもせずに使える。
    /// 「Scan」ボタンでだけ走査する（DependencyCheckScanner）。走査結果は DependencyCheckAggregator で行にし、このウィンドウが保持する。
    /// デバッグウィンドウ（AddressableDebugWindow）とは別のウィンドウで、Runtime 側の型・窓口には触れない。読み取り専用。
    /// 絞り込み・並べ替えの結果（表示する行の添字）は、Scan・絞り込み・並べ替えの変更のときだけ作り直し、OnGUI のたびには作らない。
    /// 描画は、スクロール位置から見えている範囲の行だけを描く。
    /// </summary>
    internal sealed class AddressableDependencyWindow : EditorWindow
    {
        private const string MENU_PATH = "MAH/Addressable/Dependency Check";
        private const string WINDOW_TITLE = "Addressable Dependency";
        private const string DATE_TIME_FORMAT = "yyyy-MM-dd HH:mm:ss";
        private const string FILE_PREFIX = "AddressableDependencies_";

        private const float ROW_HEIGHT = 20f;
        private const float FOLDOUT_WIDTH = 16f;
        private const int COLUMN_COUNT = 7;
        private const float MIN_COLUMN_WIDTH = 30f;
        private const float HANDLE_WIDTH = 6f; // 列の境界の、ドラッグできる幅

        // 列：名前・型・パス・サイズ・グループ数・親の数・印
        private static readonly string[] COLUMN_TITLE_KEYS = { "dep.column.name", "dep.column.type", "dep.column.path", "dep.column.size", "dep.column.groups", "dep.column.parents", "dep.column.marks" };
        private static readonly float[] DEFAULT_COLUMN_WIDTHS = { 180f, 110f, 320f, 70f, 80f, 60f, 170f };
        private static readonly Color SEPARATOR_COLOR = new(0.5f, 0.5f, 0.5f, 0.4f);

        private const float REFERRER_INDENT = 40f;

        private static readonly Color DUPLICATE_COLOR_DARK = new(1f, 0.55f, 0.1f, 0.25f);
        private static readonly Color DUPLICATE_COLOR_LIGHT = new(1f, 0.55f, 0.1f, 0.35f);

        // 走査結果と絞り込みの状態。ドメインリロード（再コンパイル）では消えず、Editor の再起動で消える
        [SerializeField] private List<DependencyCheckRow> _rows = new();
        [SerializeField] private long _scannedTicks; // 0 は未走査
        // 走査できなかった理由（未設定など）の文言のキー。文言そのものではなくキーを持つ（言語が変わっても追従するため）。
        // 以前は文言を _message に保存していたため、名前を変えて、更新前に保存された文（キーではない）を読み込まないようにしている
        [SerializeField] private string _messageKey = string.Empty;
        [SerializeField] private List<string> _selectedTypes = new(); // 空は、すべて
        [SerializeField] private string _nameFilter = string.Empty;
        [SerializeField] private string _groupFilter = string.Empty; // 空は、すべて
        [SerializeField] private bool _showHidden;
        [SerializeField] private bool _sortBySize;
        [SerializeField] private Vector2 _scroll;
        [SerializeField] private float[] _columnWidths; // 列ごとの幅。ヘッダーの境界のドラッグで変える

        private readonly List<int> _visibleRows = new(); // 表示する行の _rows 上の添字（絞り込み・並べ替え後）
        private readonly List<string> _typeNames = new(); // 結果に現れた型名（絞り込みの選択肢）
        private readonly List<string> _groupNames = new(); // 結果に現れた参照元のグループ名
        private readonly HashSet<string> _expandedPaths = new();
        private readonly float[] _columnX = new float[COLUMN_COUNT + 1]; // 列の左端（内容の左端から。最後は右端）。描画のたびに幅から作り直す
        private bool _isVisibleDirty = true;

        // 表示行から作る値。_visibleRows を作り直したときと、行の展開・折りたたみのときだけ更新し、OnGUI のたびに全行を数え直さない
        private int _visibleMultiGroupCount;
        private float _visibleTotalHeight;
        private string _statusText = string.Empty;

        private GUIStyle _clipLabel;
        private GUIStyle _boldClipLabel;

        [MenuItem(MENU_PATH)]
        private static void Open() => GetWindow<AddressableDependencyWindow>(WINDOW_TITLE);

        private void OnEnable()
        {
            titleContent = new GUIContent(WINDOW_TITLE);
            if (_columnWidths == null || _columnWidths.Length != COLUMN_COUNT)
                _columnWidths = (float[])DEFAULT_COLUMN_WIDTHS.Clone();
            RebuildFilterOptions();
            _isVisibleDirty = true;
            EditorText.LanguageChanged += OnLanguageChanged;
        }

        private void OnDisable() => EditorText.LanguageChanged -= OnLanguageChanged;

        // 取得時に作った状態表示の文字列を、新しい言語で作り直す
        private void OnLanguageChanged()
        {
            _isVisibleDirty = true;
            Repaint();
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawToolbar();

            if (_isVisibleDirty)
            {
                _isVisibleDirty = false;
                BuildVisibleRows(_rows, _selectedTypes, _nameFilter, _groupFilter, _showHidden, _sortBySize, _visibleRows);
                RefreshVisibleSummary();
            }

            EditorGUILayout.HelpBox(EditorText.Get("dep.note"), MessageType.Info);
            DrawStatus();
            if (_rows.Count > 0) DrawList();
        }

        // ---- 絞り込み・並べ替え（テストから直接検証できるよう static） ----

        /// <summary>
        /// 絞り込み・並べ替えの結果（表示する行の rows 上の添字）を result に作り直す。
        /// 型は複数選択（空はすべて）、名前は部分一致（大文字小文字を区別しない）、グループは参照元のグループ。
        /// 既定で隠す行は show_hidden のときだけ表示する。並べ替えは、既定がパス順（rows の順）、sort_by_size でファイルサイズの大きい順
        /// </summary>
        internal static void BuildVisibleRows(
            List<DependencyCheckRow> rows, List<string> selected_types, string name_filter, string group_filter,
            bool show_hidden, bool sort_by_size, List<int> result)
        {
            result.Clear();
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (!show_hidden && row.AccIsHiddenByDefault) continue;
                if (selected_types != null && selected_types.Count > 0 && !selected_types.Contains(row.AccTypeName)) continue;
                if (!string.IsNullOrEmpty(name_filter) && row.AccName.IndexOf(name_filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!string.IsNullOrEmpty(group_filter) && !HasReferrerGroup(row, group_filter)) continue;

                result.Add(i);
            }

            if (!sort_by_size) return;

            // 同じサイズのときは、パス順（添字順）を保つ
            result.Sort((left, right) =>
            {
                var compared = rows[right].AccSizeBytes.CompareTo(rows[left].AccSizeBytes);
                return compared != 0 ? compared : left.CompareTo(right);
            });
        }

        private static bool HasReferrerGroup(DependencyCheckRow row, string group_name)
        {
            foreach (var referrer in row.AccReferrers)
                if (referrer.AccGroupName == group_name) return true;
            return false;
        }

        // 表示行の複数グループの件数・高さの合計・状態の文字列を作り直す。_visibleRows を作り直した直後に呼ぶ
        private void RefreshVisibleSummary()
        {
            _visibleMultiGroupCount = 0;
            _visibleTotalHeight = 0f;
            foreach (var index in _visibleRows)
            {
                var row = _rows[index];
                if (row.AccIsMultiGroup) _visibleMultiGroupCount++;
                _visibleTotalHeight += GetRowHeight(row);
            }

            var scanned_at = new DateTime(_scannedTicks).ToString(DATE_TIME_FORMAT, CultureInfo.InvariantCulture);
            _statusText = EditorText.Format("dep.status", scanned_at, _visibleRows.Count, _visibleMultiGroupCount, _rows.Count);
        }

        // ---- 走査 ----

        private void Scan()
        {
            var result = DependencyCheckScanner.TryScan(out var input);
            switch (result)
            {
                case DependencyCheckScanner.ScanResult.NoSettings:
                    _messageKey = "dep.noSettings";
                    Repaint();
                    return;
                case DependencyCheckScanner.ScanResult.Canceled:
                    return; // 一覧は前回の結果のまま
            }

            DependencyCheckAggregator.Aggregate(input, _rows);
            _scannedTicks = DateTime.Now.Ticks;
            _messageKey = string.Empty;
            _expandedPaths.Clear();
            RebuildFilterOptions();
            _isVisibleDirty = true;
            Repaint();
        }

        // 結果に現れた型・グループから、絞り込みの選択肢を作る（固定しない）。無くなった選択は外す
        private void RebuildFilterOptions()
        {
            var types = new HashSet<string>();
            var groups = new HashSet<string>();
            foreach (var row in _rows)
            {
                types.Add(row.AccTypeName);
                foreach (var referrer in row.AccReferrers)
                    groups.Add(referrer.AccGroupName);
            }

            _typeNames.Clear();
            _typeNames.AddRange(types);
            _typeNames.Sort(StringComparer.Ordinal);
            _groupNames.Clear();
            _groupNames.AddRange(groups);
            _groupNames.Sort(StringComparer.Ordinal);

            _selectedTypes.RemoveAll(type => !types.Contains(type));
            if (!string.IsNullOrEmpty(_groupFilter) && !groups.Contains(_groupFilter)) _groupFilter = string.Empty;
        }

        // ---- テキスト出力 ----

        // 表示中の行（絞り込み後・並べ替え後）を、設定の出力先に日時付きのファイルとして書き出す。出力先は押したときに読む
        private void ExportText()
        {
            var project_root = EditorProjectPath.GetProjectRoot();
            var output_folder = AddressableEditorSettingsLocator.FindDependencyCheckSettings().AccOutputFolder;
            var folder = EditorTextFileWriter.ResolveFolder(project_root, output_folder, AddressableDependencyCheckSettings.DEFAULT_OUTPUT_FOLDER);
            var now = DateTime.Now;
            var text = DependencyCheckLogFormatter.Format(now, _rows, _visibleRows, BuildFilterSummary());

            var path = EditorTextFileWriter.WriteTimestamped(text, folder, FILE_PREFIX, now, EditorText.Get("dep.exportFailure"));
            if (path != null)
                AddressableLogger.LogInfo(EditorText.Format("dep.exported", path));
        }

        private string BuildFilterSummary()
        {
            var types = _selectedTypes.Count == 0 ? EditorText.Get("dep.all") : string.Join(", ", _selectedTypes);
            var groups = string.IsNullOrEmpty(_groupFilter) ? EditorText.Get("dep.all") : _groupFilter;
            var summary = EditorText.Format("dep.filterSummary", types, groups);
            if (!string.IsNullOrEmpty(_nameFilter)) summary += EditorText.Format("dep.filterSummary.name", _nameFilter);
            return summary;
        }

        // ---- 描画 ----

        private void EnsureStyles()
        {
            if (_clipLabel != null) return;

            _clipLabel = new GUIStyle(EditorStyles.label) { clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft };
            _boldClipLabel = new GUIStyle(_clipLabel) { fontStyle = FontStyle.Bold };
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Scan", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                // OnGUI の途中で結果が変わるとレイアウトが崩れるため、ボタンの処理が終わってから走査する
                EditorApplication.delayCall += Scan;
                GUIUtility.ExitGUI();
            }

            using (new EditorGUI.DisabledScope(_visibleRows.Count == 0))
            {
                if (GUILayout.Button(EditorText.Get("dep.toolbar.export"), EditorStyles.toolbarButton, GUILayout.Width(90)))
                    ExportText();
            }

            GUILayout.Space(8);

            using (new EditorGUI.DisabledScope(_rows.Count == 0))
            {
                var type_label = _selectedTypes.Count == 0 ? EditorText.Get("dep.all") : EditorText.Format("dep.toolbar.types", _selectedTypes.Count);
                if (GUILayout.Button(EditorText.Format("dep.toolbar.typeButton", type_label), EditorStyles.toolbarDropDown, GUILayout.Width(100)))
                    ShowTypeMenu();

                var group_label = string.IsNullOrEmpty(_groupFilter) ? EditorText.Get("dep.all") : _groupFilter;
                if (GUILayout.Button(EditorText.Format("dep.toolbar.groupButton", group_label), EditorStyles.toolbarDropDown, GUILayout.Width(150)))
                    ShowGroupMenu();
            }

            EditorGUI.BeginChangeCheck();
            _nameFilter = GUILayout.TextField(_nameFilter, EditorStyles.toolbarSearchField, GUILayout.MinWidth(80), GUILayout.MaxWidth(200));
            _showHidden = GUILayout.Toggle(_showHidden, EditorText.Get("dep.toolbar.showHidden"), EditorStyles.toolbarButton, GUILayout.Width(190));
            _sortBySize = GUILayout.Toggle(_sortBySize, EditorText.Get("dep.toolbar.sortBySize"), EditorStyles.toolbarButton, GUILayout.Width(90));
            if (EditorGUI.EndChangeCheck()) _isVisibleDirty = true;

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void ShowTypeMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(EditorText.Get("dep.all")), _selectedTypes.Count == 0, () =>
            {
                _selectedTypes.Clear();
                _isVisibleDirty = true;
            });
            foreach (var type_name in _typeNames)
            {
                var captured = type_name;
                menu.AddItem(new GUIContent(captured), _selectedTypes.Contains(captured), () =>
                {
                    if (!_selectedTypes.Remove(captured)) _selectedTypes.Add(captured);
                    _isVisibleDirty = true;
                });
            }
            menu.ShowAsContext();
        }

        private void ShowGroupMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(EditorText.Get("dep.all")), string.IsNullOrEmpty(_groupFilter), () =>
            {
                _groupFilter = string.Empty;
                _isVisibleDirty = true;
            });
            foreach (var group_name in _groupNames)
            {
                var captured = group_name;
                menu.AddItem(new GUIContent(captured), _groupFilter == captured, () =>
                {
                    _groupFilter = captured;
                    _isVisibleDirty = true;
                });
            }
            menu.ShowAsContext();
        }

        private void DrawStatus()
        {
            if (!string.IsNullOrEmpty(_messageKey))
                EditorGUILayout.HelpBox(EditorText.Get(_messageKey), MessageType.Warning);

            if (_scannedTicks == 0)
            {
                EditorGUILayout.LabelField(EditorText.Get("dep.notScanned"));
                return;
            }

            EditorGUILayout.LabelField(_statusText);
        }

        private void DrawList()
        {
            UpdateColumnX();
            DrawHeader(GUILayoutUtility.GetRect(0f, ROW_HEIGHT, GUILayout.ExpandWidth(true)));

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            // 列の幅の合計が表示幅を超えたときは、横スクロールになる
            var area = GUILayoutUtility.GetRect(_columnX[COLUMN_COUNT], _visibleTotalHeight,GUILayout.MinWidth(_columnX[COLUMN_COUNT]), GUILayout.ExpandWidth(true));
            var view_top = _scroll.y;
            var view_bottom = _scroll.y + position.height;

            var y = area.y;
            foreach (var index in _visibleRows)
            {
                var row = _rows[index];
                var height = GetRowHeight(row);
                if (y + height >= view_top && y <= view_bottom) // 見えている範囲の行だけ描く（y はスクロール内容の座標）
                    DrawRow(new Rect(area.x, y, area.width, height), row);
                y += height;
            }

            EditorGUILayout.EndScrollView();
        }

        // 列の左端を、現在の列幅から作る（幅の下限も、ここで守る）
        private void UpdateColumnX()
        {
            var x = FOLDOUT_WIDTH;
            for (var i = 0; i < COLUMN_COUNT; i++)
            {
                _columnWidths[i] = Mathf.Max(MIN_COLUMN_WIDTH, _columnWidths[i]);
                _columnX[i] = x;
                x += _columnWidths[i];
            }
            _columnX[COLUMN_COUNT] = x;
        }

        // 列の見出し。列の境界に区切り線を引き、境界をドラッグすると、その左の列の幅が変わる。
        // 一覧の横スクロールに合わせて、見出しもずらして描く
        private void DrawHeader(Rect rect)
        {
            GUI.BeginGroup(rect);
            var origin_x = -_scroll.x;
            GUI.Box(new Rect(0f, 0f, rect.width, rect.height), GUIContent.none, EditorStyles.toolbar);

            for (var i = 0; i < COLUMN_COUNT; i++)
            {
                var cell = new Rect(origin_x + _columnX[i], 0f, _columnWidths[i], rect.height);
                GUI.Label(cell, EditorText.Get(COLUMN_TITLE_KEYS[i]), EditorStyles.boldLabel);
                DrawSeparator(origin_x + _columnX[i + 1], 0f, rect.height);
                DrawResizeHandle(i, origin_x + _columnX[i + 1], rect.height);
            }
            GUI.EndGroup();
        }

        // 列の境界のドラッグ。マウスがハンドルの外へ出ても、押している間は追従する（hotControl）
        private void DrawResizeHandle(int column, float right_x, float height)
        {
            var handle = new Rect(right_x - HANDLE_WIDTH * 0.5f, 0f, HANDLE_WIDTH, height);
            EditorGUIUtility.AddCursorRect(handle, MouseCursor.ResizeHorizontal);

            var control_id = GUIUtility.GetControlID(FocusType.Passive);
            var current = Event.current;
            switch (current.GetTypeForControl(control_id))
            {
                case EventType.MouseDown:
                    if (current.button == 0 && handle.Contains(current.mousePosition))
                    {
                        GUIUtility.hotControl = control_id;
                        current.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == control_id)
                    {
                        _columnWidths[column] = Mathf.Max(MIN_COLUMN_WIDTH, _columnWidths[column] + current.delta.x);
                        current.Use();
                        Repaint();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == control_id)
                    {
                        GUIUtility.hotControl = 0;
                        current.Use();
                    }
                    break;
            }
        }

        private static void DrawSeparator(float x, float y, float height)
        {
            if (Event.current.type != EventType.Repaint) return;

            EditorGUI.DrawRect(new Rect(x - 1f, y, 1f, height), SEPARATOR_COLOR);
        }

        private float GetRowHeight(DependencyCheckRow row)
            => _expandedPaths.Contains(row.AccPath) ? ROW_HEIGHT * (1 + row.AccReferrers.Count) : ROW_HEIGHT;

        private Rect GetCell(Rect line, int column)
            => new(line.x + _columnX[column], line.y, _columnWidths[column], ROW_HEIGHT);

        private void DrawRow(Rect rect, DependencyCheckRow row)
        {
            var line = new Rect(rect.x, rect.y, rect.width, ROW_HEIGHT);
            if (Event.current.type == EventType.Repaint)
            {
                if (row.AccIsMultiGroup)
                    EditorGUI.DrawRect(line, EditorGUIUtility.isProSkin ? DUPLICATE_COLOR_DARK : DUPLICATE_COLOR_LIGHT);

                // 行の下線と、列の区切り線
                EditorGUI.DrawRect(new Rect(line.x, line.yMax - 1f, line.width, 1f), SEPARATOR_COLOR);
                for (var i = 0; i < COLUMN_COUNT; i++)
                    DrawSeparator(line.x + _columnX[i + 1], line.y, ROW_HEIGHT);
            }

            var is_expanded = _expandedPaths.Contains(row.AccPath);
            var foldout_rect = new Rect(line.x, line.y, FOLDOUT_WIDTH, ROW_HEIGHT);
            var now_expanded = EditorGUI.Foldout(foldout_rect, is_expanded, GUIContent.none, true);
            if (now_expanded != is_expanded)
            {
                // 高さの合計は、全行を数え直さず、この行の差分だけ足し引きする
                var referrers_height = ROW_HEIGHT * row.AccReferrers.Count;
                if (now_expanded)
                {
                    _expandedPaths.Add(row.AccPath);
                    _visibleTotalHeight += referrers_height;
                }
                else
                {
                    _expandedPaths.Remove(row.AccPath);
                    _visibleTotalHeight -= referrers_height;
                }
                Repaint();
            }

            GUI.Label(GetCell(line, 0), new GUIContent(row.AccName, row.AccPath), _clipLabel);
            GUI.Label(GetCell(line, 1), row.AccTypeName, _clipLabel);
            GUI.Label(GetCell(line, 2), new GUIContent(row.AccPath, row.AccPath), _clipLabel);
            GUI.Label(GetCell(line, 3), DependencyCheckLogFormatter.FormatSize(row.AccSizeBytes), _clipLabel);
            GUI.Label(GetCell(line, 4), row.AccGroupCount.ToString(), row.AccIsMultiGroup ? _boldClipLabel : _clipLabel);
            GUI.Label(GetCell(line, 5), row.AccReferrers.Count.ToString(), _clipLabel);
            GUI.Label(GetCell(line, 6), BuildMarks(row), _clipLabel);

            // 行のクリックで、Project ウィンドウでそのアセットを表示する（折りたたみの矢印は除く）
            var click_rect = new Rect(line.x + FOLDOUT_WIDTH, line.y, line.width - FOLDOUT_WIDTH, ROW_HEIGHT);
            if (IsClicked(click_rect)) Ping(row.AccPath);

            if (!is_expanded) return;

            for (var i = 0; i < row.AccReferrers.Count; i++)
            {
                var referrer = row.AccReferrers[i];
                var referrer_rect = new Rect(rect.x + REFERRER_INDENT, rect.y + ROW_HEIGHT * (i + 1), rect.width - REFERRER_INDENT, ROW_HEIGHT);
                GUI.Label(referrer_rect, new GUIContent($"← {referrer.AccPath}  [{referrer.AccGroupName}]{DependencyCheckLogFormatter.FormatVia(referrer)}", referrer.AccPath), _clipLabel);
                if (IsClicked(referrer_rect)) Ping(referrer.AccPath);
            }
        }

        private static string BuildMarks(DependencyCheckRow row)
        {
            var marks = string.Empty;
            if (row.AccIsResources) marks += "Resources/ ";
            if (row.AccIsPackSeparately) marks += "Pack Separately ";
            if (row.AccIsHiddenByDefault) marks += EditorText.Get("dep.mark.hidden");
            return marks.TrimEnd();
        }

        private static bool IsClicked(Rect rect)
        {
            var current = Event.current;
            if (current.type != EventType.MouseDown || current.button != 0 || !rect.Contains(current.mousePosition)) return false;

            current.Use();
            return true;
        }

        // 子の行・参照元の親の行のクリック。Selection に設定し、Project ウィンドウで開いてハイライトする。消えていたら警告して何もしない
        private static void Ping(string asset_path)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(asset_path);
            if (asset == null)
            {
                AddressableLogger.LogWarning(EditorText.Format("dep.assetNotFound", asset_path));
                return;
            }

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }
    }
}
