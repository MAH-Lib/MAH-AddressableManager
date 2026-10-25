using System.Collections.Generic;
using MAH.AddressableManager.Core;
using MAH.AddressableManager.Editor.Localization;
using MAH.AddressableManager.Editor.SettingsSync;
using UnityEditor;
using UnityEngine;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary>
    /// ルールの一覧のウィンドウ（仕様 4章）。どのルールの SO がどのフォルダ・拡張子を受け持っているかの一覧、
    /// ルールの競合・アドレスの重複の一覧、ルールを既存のアセットに当て直す操作を持つ。
    /// ルールの編集はしない（SO の Inspector で行う）。自動登録に必須ではない（ウィンドウを開かなくても自動登録は動く）。
    /// 書き込みは RegistrationEntryWriter に任せ、このウィンドウからは直接書き込まない。
    /// </summary>
    internal sealed class AddressableRegistrationRuleWindow : EditorWindow
    {
        private const string MENU_PATH = "MAH/Addressable/Auto Registration";
        private const string WINDOW_TITLE = "Auto Registration";
        private const string DEFAULT_RULE_FILE_NAME = "AutoRegistrationRule";

        private const string MARK_NOTICE = "!";

        private const float COLUMN_ENABLED = 36f;
        private const float COLUMN_NAME = 150f;
        private const float COLUMN_FOLDER = 220f;
        private const float COLUMN_EXTENSIONS = 190f;
        private const float COLUMN_GROUP = 140f;
        private const float COLUMN_ADDRESS = 150f;
        private const float COLUMN_COUNT = 60f;

        // 差分・エラー・警告の一覧。大量でも重くならないよう、行の高さを固定して、見えている行だけを描く
        private const float LIST_ROW_HEIGHT = 18f;
        private const float LIST_MIN_HEIGHT = 80f;
        private const float DIFF_LIST_MAX_HEIGHT = 220f;
        private const float PROBLEM_LIST_MAX_HEIGHT = 140f;
        private const int LIST_MARGIN_ROWS = 2;

        /// <summary> 一覧の1行（ルール1つ分）。再コンパイルで消えてよいので、シリアライズしない </summary>
        private sealed class RuleRow
        {
            internal AddressableAutoRegistrationRuleSO Rule;
            internal RegistrationRuleInput Input;
            internal int TargetCount;
            internal string ExtensionsText;
            internal string NoticeText;
        }

        [SerializeField] private Vector2 _ruleScroll;
        [SerializeField] private Vector2 _problemScroll;
        [SerializeField] private Vector2 _diffScroll;
        [SerializeField] private Vector2 _errorScroll;
        [SerializeField] private Vector2 _warningScroll;
        [SerializeField] private string _newRuleName = DEFAULT_RULE_FILE_NAME;

        private readonly List<RuleRow> _rows = new();
        // 「当て直す」の差分の行。描画のたびに組み立てないよう、差分を求めたときに1回だけ作る
        private readonly List<string> _diffLines = new();
        // 1行に収めるため、はみ出しは切る（全文はツールチップに出す）
        private GUIStyle _clippedLabelStyle;
        private readonly RegistrationLogOutput _logOutput = new();
        private RegistrationPlan _plan;
        private bool _needsReload = true;
        private bool _isStale;
        private bool _showDiff;
        private bool _autoRegistrationEnabled = true;
        private bool _hasSettings = true;
        private int _loadedRuleVersion;
        private int _loadedRuleCount;
        private string _infoMessage = string.Empty;

        [MenuItem(MENU_PATH)]
        private static void Open()
        {
            GetWindow<AddressableRegistrationRuleWindow>(WINDOW_TITLE);
        }

        private void OnEnable()
        {
            _needsReload = true;
            EditorText.LanguageChanged += OnLanguageChanged;
        }

        private void OnDisable() => EditorText.LanguageChanged -= OnLanguageChanged;

        // 一覧の行・差分の行は、組み立てたときの言語の文字列を持つため、読み直して作り直す（言語の切り替えは稀なので、重くても構わない）
        private void OnLanguageChanged()
        {
            _needsReload = true;
            Repaint();
        }

        private void OnFocus()
        {
            // 自動登録のスイッチは Editor SO で変わり得るため、戻ってきたときに読み直す
            _autoRegistrationEnabled = AddressableEditorSettingsLocator.FindRegistrationSettings().AccAutoRegistration;
        }

        private void OnGUI()
        {
            if (_needsReload)
            {
                // 描画の途中で結果が変わらないよう、描画のあとに読み込む
                _needsReload = false;
                EditorApplication.delayCall += Reload;
            }

            DrawNotices();
            DrawToolbar();
            DrawRuleList();
            DrawDiff();
            DrawProblems();
        }

        // 一覧を作り直す。対象のアセットを集めるため重く、開いたとき・再読み込み・ルールの追加と削除のときだけ行う
        private void Reload()
        {
            // delayCall で呼ばれるため、その間にウィンドウが閉じられていたら何もしない
            if (this == null) return;

            _rows.Clear();
            _plan = null;
            _diffLines.Clear();
            _showDiff = false;
            _infoMessage = string.Empty;
            _autoRegistrationEnabled = AddressableEditorSettingsLocator.FindRegistrationSettings().AccAutoRegistration;

            var settings = RegistrationRuleScanner.GetSettings();
            _hasSettings = settings != null;
            var rules = RegistrationRuleScanner.GetRuleAssets();
            var inputs = RegistrationRuleScanner.GetRuleInputs();
            _loadedRuleVersion = RegistrationRuleScanner.AccRuleVersion;
            _loadedRuleCount = rules.Count;
            _isStale = false;

            if (settings != null)
            {
                var assets = new List<RegistrationAssetInput>();
                var scan = RegistrationRuleScanner.TryCollectAssets(settings, CollectFolders(inputs), true, assets);
                if (scan == RegistrationScanResult.Canceled)
                {
                    _infoMessage = EditorText.Get("rule.info.canceledReload");
                    Repaint();
                    return;
                }

                for (var i = 0; i < inputs.Count; i++) _rows.Add(CreateRow(rules[i], inputs[i], assets));

                // 自動登録がオフのときは、競合のチェックもしない（仕様 1.2）
                if (_autoRegistrationEnabled)
                    _plan = RegistrationPlanBuilder.BuildAuto(inputs, assets, RegistrationRuleScanner.CreateEnvironment(settings));
            }
            Repaint();
        }

        // 「当て直す」：差分を求めて、適用の前に一覧で見せる（まだ書き込まない）
        private void BeginReapply()
        {
            Reload();
            if (_plan == null) return;

            if (!_plan.AccHasChanges)
            {
                _infoMessage = RegistrationLogFormatter.NoChangeMessage;
                return;
            }

            for (var i = 0; i < _plan.AccChanges.Count; i++) _diffLines.Add(RegistrationLogFormatter.FormatChange(_plan.AccChanges[i]));
            _showDiff = true;
        }

        // 「適用」：見せた差分を書き込む。キャンセルしたら、そこまでの分が書き込まれた状態で止まる
        private void ApplyPlan()
        {
            var settings = RegistrationRuleScanner.GetSettings();
            if (settings == null || _plan == null) return;

            var registration = AddressableEditorSettingsLocator.FindRegistrationSettings();
            var plan = _plan;
            var result = RegistrationEntryWriter.Apply(settings, plan, registration.AccDefaultGroupTemplate, true);
            AssetDatabase.SaveAssets();

            RegistrationLogFormatter.Format(plan, registration.AccLogDetail, _logOutput);
            RegistrationLogPrinter.Print(_logOutput);
            if (result.AccCanceled)
                AddressableLogger.LogWarning(RegistrationLogFormatter.Prefix + EditorText.Format("rule.info.canceled", result.AccAppliedCount));
            if (result.AccFailedCount > 0)
                AddressableLogger.LogWarning(RegistrationLogFormatter.Prefix + EditorText.Format("rule.info.failed", result.AccFailedCount));
            if (result.AccSkippedCount > 0)
                AddressableLogger.LogWarning(RegistrationLogFormatter.FormatSkipped(result.AccSkippedCount));

            Reload();
            _infoMessage = result.AccCanceled
                ? EditorText.Format("rule.info.canceled", result.AccAppliedCount)
                : EditorText.Format("rule.info.applied", result.AccAppliedCount);
            if (result.AccSkippedCount > 0) _infoMessage += $"\n{RegistrationLogFormatter.FormatSkipped(result.AccSkippedCount)}";
        }

        private void CreateRule()
        {
            var rule = RegistrationRuleAssetCreator.CreateInFixedFolder(_newRuleName);
            Selection.activeObject = rule;
            EditorGUIUtility.PingObject(rule);
            Reload();
        }

        // ---- 描画 ----

        private void DrawNotices()
        {
            if (!_hasSettings) EditorGUILayout.HelpBox(EditorText.Get("rule.text.noSettings"), MessageType.Warning);
            if (!_autoRegistrationEnabled) EditorGUILayout.HelpBox(EditorText.Get("rule.text.switchOff"), MessageType.Warning);

            if (!_isStale && (RegistrationRuleScanner.AccRuleVersion != _loadedRuleVersion))
            {
                // ルールの SO の追加・削除なら自動で読み直し、値の変更だけなら読み直しを促す（値の変更のたびに全体を集めると重いため）
                if (RegistrationRuleScanner.GetRuleAssets().Count != _loadedRuleCount) _needsReload = true;
                else _isStale = true;
            }
            if (_isStale) EditorGUILayout.HelpBox(EditorText.Get("rule.text.stale"), MessageType.Info);
            if (_infoMessage.Length > 0) EditorGUILayout.HelpBox(_infoMessage, MessageType.Info);
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(EditorText.Get("rule.toolbar.reload"), EditorStyles.toolbarButton, GUILayout.Width(80f))) _needsReload = true;

                GUILayout.Space(12f);
                GUILayout.Label(EditorText.Get("rule.toolbar.name"), GUILayout.Width(40f));
                _newRuleName = EditorGUILayout.TextField(_newRuleName, EditorStyles.toolbarTextField, GUILayout.Width(180f));
                if (GUILayout.Button(EditorText.Get("rule.toolbar.create"), EditorStyles.toolbarButton, GUILayout.Width(48f))) EditorApplication.delayCall += CreateRule;

                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(!_autoRegistrationEnabled || !_hasSettings || _showDiff))
                {
                    if (GUILayout.Button(EditorText.Get("rule.toolbar.reapply"), EditorStyles.toolbarButton, GUILayout.Width(80f))) EditorApplication.delayCall += BeginReapply;
                }
            }
        }

        private void DrawRuleList()
        {
            if (_rows.Count == 0)
            {
                if (_hasSettings) EditorGUILayout.HelpBox(EditorText.Get("rule.text.noRule"), MessageType.None);
                return;
            }

            DrawRuleHeader();
            _ruleScroll = EditorGUILayout.BeginScrollView(_ruleScroll, GUILayout.MinHeight(80f), GUILayout.MaxHeight(260f));
            for (var i = 0; i < _rows.Count; i++) DrawRuleRow(_rows[i]);
            EditorGUILayout.EndScrollView();
        }

        private static void DrawRuleHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(EditorText.Get("rule.column.enabled"), EditorStyles.boldLabel, GUILayout.Width(COLUMN_ENABLED));
                GUILayout.Label(EditorText.Get("rule.column.rule"), EditorStyles.boldLabel, GUILayout.Width(COLUMN_NAME));
                GUILayout.Label(EditorText.Get("rule.column.folder"), EditorStyles.boldLabel, GUILayout.Width(COLUMN_FOLDER));
                GUILayout.Label(EditorText.Get("rule.column.extensions"), EditorStyles.boldLabel, GUILayout.Width(COLUMN_EXTENSIONS));
                GUILayout.Label(EditorText.Get("rule.column.group"), EditorStyles.boldLabel, GUILayout.Width(COLUMN_GROUP));
                GUILayout.Label(EditorText.Get("rule.column.address"), EditorStyles.boldLabel, GUILayout.Width(COLUMN_ADDRESS));
                GUILayout.Label(EditorText.Get("rule.column.targets"), EditorStyles.boldLabel, GUILayout.Width(COLUMN_COUNT));
            }
        }

        // 行をクリックすると、その SO を選択する
        private void DrawRuleRow(RuleRow row)
        {
            var rect = EditorGUILayout.BeginHorizontal();
            GUILayout.Label(row.Input.AccEnabled ? "✔" : "—", GUILayout.Width(COLUMN_ENABLED));
            GUILayout.Label(new GUIContent(row.Input.AccName, row.NoticeText), GUILayout.Width(COLUMN_NAME));
            GUILayout.Label(row.Input.AccFolderPath, GUILayout.Width(COLUMN_FOLDER));
            GUILayout.Label(new GUIContent(row.ExtensionsText, row.NoticeText), GUILayout.Width(COLUMN_EXTENSIONS));
            GUILayout.Label(row.Input.AccTargetGroup, GUILayout.Width(COLUMN_GROUP));
            GUILayout.Label(row.Input.AccAddressTemplate, GUILayout.Width(COLUMN_ADDRESS));
            GUILayout.Label(row.TargetCount.ToString(), GUILayout.Width(COLUMN_COUNT));
            if (row.NoticeText.Length > 0) GUILayout.Label(new GUIContent(MARK_NOTICE, row.NoticeText), EditorStyles.boldLabel, GUILayout.Width(16f));
            EditorGUILayout.EndHorizontal();

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                Selection.activeObject = row.Rule;
                EditorGUIUtility.PingObject(row.Rule);
                Event.current.Use();
            }
        }

        // 「当て直す」の差分。確認して「適用」を押すと書き込む
        private void DrawDiff()
        {
            if (!_showDiff || _plan == null) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(EditorText.Get("rule.diff.title"), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(RegistrationLogFormatter.FormatSummary(_plan));

            DrawLineList(ref _diffScroll, _diffLines, DIFF_LIST_MAX_HEIGHT);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(EditorText.Get("rule.diff.cancel"), GUILayout.Width(90f))) _showDiff = false;
                if (GUILayout.Button(EditorText.Get("rule.diff.apply"), GUILayout.Width(90f)))
                {
                    _showDiff = false;
                    EditorApplication.delayCall += ApplyPlan;
                }
            }
        }

        // 競合・アドレスの重複・警告（Console のエラーは流れて見逃されるため、ここに溜めて見せる）
        private void DrawProblems()
        {
            if (_plan == null) return;
            if (_plan.AccConflicts.Count == 0 && _plan.AccDuplicates.Count == 0 && _plan.AccErrors.Count == 0 && _plan.AccWarnings.Count == 0) return;

            EditorGUILayout.Space();
            _problemScroll = EditorGUILayout.BeginScrollView(_problemScroll);

            if (_plan.AccConflicts.Count > 0) EditorGUILayout.LabelField(EditorText.Format("rule.problem.conflicts", _plan.AccConflicts.Count), EditorStyles.boldLabel);
            for (var i = 0; i < _plan.AccConflicts.Count; i++) DrawConflict(_plan.AccConflicts[i]);

            if (_plan.AccDuplicates.Count > 0) EditorGUILayout.LabelField(EditorText.Format("rule.problem.duplicates", _plan.AccDuplicates.Count), EditorStyles.boldLabel);
            for (var i = 0; i < _plan.AccDuplicates.Count; i++) DrawDuplicate(_plan.AccDuplicates[i]);

            if (_plan.AccErrors.Count > 0)
            {
                EditorGUILayout.LabelField(EditorText.Format("rule.problem.errors", _plan.AccErrors.Count), EditorStyles.boldLabel);
                DrawLineList(ref _errorScroll, _plan.AccErrors, PROBLEM_LIST_MAX_HEIGHT);
            }
            if (_plan.AccWarnings.Count > 0)
            {
                EditorGUILayout.LabelField(EditorText.Format("rule.problem.warnings", _plan.AccWarnings.Count), EditorStyles.boldLabel);
                DrawLineList(ref _warningScroll, _plan.AccWarnings, PROBLEM_LIST_MAX_HEIGHT);
            }

            EditorGUILayout.EndScrollView();
        }

        // 1行に1項目の一覧を、見えている行だけ描く。行の高さを固定して、上下を空白で埋める（手動一括登録の一覧と同じ方式）。
        // 数千件の警告・差分でも、描画のコストが件数に比例しないようにするため
        private void DrawLineList(ref Vector2 scroll, List<string> lines, float max_height)
        {
            _clippedLabelStyle ??= new GUIStyle(EditorStyles.label) { clipping = TextClipping.Clip };

            var list_height = Mathf.Clamp(lines.Count * LIST_ROW_HEIGHT, LIST_MIN_HEIGHT, max_height);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(list_height));
            var first = Mathf.Clamp(Mathf.FloorToInt(scroll.y / LIST_ROW_HEIGHT), 0, lines.Count);
            var last = Mathf.Min(lines.Count, first + Mathf.CeilToInt(list_height / LIST_ROW_HEIGHT) + LIST_MARGIN_ROWS);
            GUILayout.Space(first * LIST_ROW_HEIGHT);
            for (var i = first; i < last; i++) GUILayout.Label(new GUIContent(lines[i], lines[i]), _clippedLabelStyle, GUILayout.Height(LIST_ROW_HEIGHT));
            GUILayout.Space((lines.Count - last) * LIST_ROW_HEIGHT);
            EditorGUILayout.EndScrollView();
        }

        private void DrawConflict(RegistrationConflict conflict)
        {
            EditorGUILayout.HelpBox(EditorText.Format("rule.conflict.message", string.Join(EditorText.Get("common.listSeparator"), conflict.AccRuleNames), conflict.AccAssetPaths.Count), MessageType.Error);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(12f);
                for (var i = 0; i < conflict.AccRuleNames.Count; i++)
                {
                    if (GUILayout.Button(conflict.AccRuleNames[i], EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                        SelectRule(conflict.AccRuleGuids[i], conflict.AccRuleNames[i]);
                }
            }
            var shown = conflict.AccAssetPaths.Count < RegistrationLogFormatter.MAX_CONFLICT_PATHS
                ? conflict.AccAssetPaths.Count
                : RegistrationLogFormatter.MAX_CONFLICT_PATHS;
            for (var i = 0; i < shown; i++) DrawAssetLink(conflict.AccAssetPaths[i]);
            if (conflict.AccAssetPaths.Count > shown) EditorGUILayout.LabelField(EditorText.Format("rule.conflict.more", conflict.AccAssetPaths.Count - shown));
        }

        private void DrawDuplicate(RegistrationDuplicate duplicate)
        {
            EditorGUILayout.HelpBox(EditorText.Format("rule.duplicate.message", duplicate.AccAddress), MessageType.Error);
            for (var i = 0; i < duplicate.AccEntries.Count; i++)
                DrawAssetLink(duplicate.AccEntries[i].AccAssetPath, duplicate.AccEntries[i].AccGroupName);
        }

        // アセットのパスを、クリックで Project ウィンドウに表示するボタンとして出す
        private static void DrawAssetLink(string asset_path, string group_name = "")
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(12f);
                var label = group_name.Length > 0 ? EditorText.Format("rule.assetLink", asset_path, group_name) : asset_path;
                if (GUILayout.Button(label, EditorStyles.linkLabel)) PingAsset(asset_path);
            }
        }

        // ---- 選択 ----

        // 同じ名前の SO が別のフォルダにあっても取り違えないよう、GUID で探す（GUID が分からないときだけ、名前で探す）
        private void SelectRule(string rule_guid, string rule_name)
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                var matches = rule_guid.Length > 0 ? _rows[i].Input.AccGuid == rule_guid : _rows[i].Input.AccName == rule_name;
                if (!matches) continue;
                Selection.activeObject = _rows[i].Rule;
                EditorGUIUtility.PingObject(_rows[i].Rule);
                return;
            }
        }

        private static void PingAsset(string asset_path)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(asset_path);
            if (asset == null)
            {
                AddressableLogger.LogWarning(EditorText.Format("rule.assetNotFound", asset_path));
                return;
            }
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        // ---- 一覧の組み立て ----

        // 対象フォルダ（重複なし）。フォルダが未指定のルールは除く
        private static List<string> CollectFolders(IReadOnlyList<RegistrationRuleInput> inputs)
        {
            var folders = new List<string>();
            for (var i = 0; i < inputs.Count; i++)
            {
                var folder = inputs[i].AccFolderPath;
                if (folder.Length > 0 && !folders.Contains(folder)) folders.Add(folder);
            }
            return folders;
        }

        private static RuleRow CreateRow(AddressableAutoRegistrationRuleSO rule, RegistrationRuleInput input, List<RegistrationAssetInput> assets)
        {
            var count = 0;
            for (var i = 0; i < assets.Count; i++)
            {
                var path = assets[i].AccPath;
                if (RegistrationRuleMatcher.GetExclusionKind(path) == RegistrationExclusionKind.None && RegistrationRuleMatcher.IsMatch(input, path)) count++;
            }

            var notice = string.Empty;
            string extensions_text;
            if (input.AccTargetMode == RegistrationTargetMode.All)
            {
                extensions_text = input.AccExcludedExtensions.Count > 0 ? EditorText.Format("rule.ext.allExcluding", string.Join(", ", input.AccExcludedExtensions)) : "All";
                notice = EditorText.Get("rule.notice.all");
            }
            else if (input.AccExtensions.Count == 0)
            {
                extensions_text = EditorText.Get("rule.text.emptyExtensions");
                notice = EditorText.Get("rule.notice.emptyExtensions");
            }
            else
            {
                extensions_text = string.Join(", ", input.AccExtensions);
            }

            return new RuleRow { Rule = rule, Input = input, TargetCount = count, ExtensionsText = extensions_text, NoticeText = notice };
        }
    }
}
