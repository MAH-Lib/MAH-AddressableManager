using System.Collections.Generic;
using System.IO;
using MAH.AddressableManager.Core;
using MAH.AddressableManager.Editor.Localization;
using MAH.AddressableManager.Editor.SettingsSync;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary>
    /// 手動一括登録のウィンドウ。Project ウィンドウでフォルダ・アセットを右クリックして開く。
    /// 流れは「1. 開いた時点で、対象の配下をすべて読み込んで一覧に出す → 2. 拡張子ごとのオン・オフと、行ごとのチェックで絞り込む
    /// → 3. 追加先のグループ・Label を決める（決めなければ Editor SO の設定）→ 4. 登録する」。
    /// アドレスのテンプレート・除外パターン・新規グループの Template は画面に出さず、Editor SO の「ルールの作成時の初期値」を使う。
    /// 自動登録のスイッチに関係なく、いつでも使える。自動登録のルールに当たるアセットは変更できない（次のインポートでルールの内容に戻されるため）。
    /// 計画の作成は RegistrationPlanBuilder、書き込みは RegistrationEntryWriter に任せ、このウィンドウからは直接書き込まない。
    /// </summary>
    internal sealed class AddressableManualRegistrationWindow : EditorWindow
    {
        // 右クリックのメニュー。「MAH → コマンド」の1階層にする。
        // priority を他の項目（Assets メニューの既存の項目は 1000〜3000 台）から大きく離すと、Unity が区切り線を入れる（11 以上離れると別の区画になる）。
        // 他のパッケージのコマンドと見分けやすくするため、区画を分ける
        private const string MENU_PATH = "Assets/MAH/Register to Addressables...";
        private const int MENU_PRIORITY = 10000;
        private const string WINDOW_TITLE = "Register to Addressables";
        private const string ASSETS_PREFIX = "Assets/";
        // {Rule.Name:lower} のように変換を付けて書いたものにも当たるよう、閉じる } は含めない
        private const string RULE_NAME_MACRO = "{Rule.Name";


        private const float THUMBNAIL_SIZE = 36f;
        private const float ROW_HEIGHT = 40f; // サムネイルと2段分の高さ
        // 拡張子の一覧の高さは、ウィンドウの高さの割合で決める（ウィンドウを大きくすると、一覧も大きくなる）。小さくなりすぎないよう下限を持つ
        private const float EXTENSION_LIST_HEIGHT_RATIO = 0.25f;
        private const float EXTENSION_LIST_MIN_HEIGHT = 60f;
        // アセットの一覧の高さは、ウィンドウの高さの割合で決める。少なくともこの行数が見える高さを下限にする
        private const float ROW_LIST_HEIGHT_RATIO = 0.4f;
        private const int ROW_LIST_MIN_VISIBLE_ROWS = 6;
        // 内容は外側のスクロールに入るため、小さくしても下のボタンは隠れない。小さくしすぎないよう下限だけ持つ
        private static readonly Vector2 WINDOW_MIN_SIZE = new(480f, 320f);

        /// <summary> 対象1つ分（右クリックで選んだフォルダ、または選んだアセット）と、その配下のアセット </summary>
        private sealed class TargetGroup
        {
            internal string Root;
            internal bool IsExplicitAsset;
            internal List<RegistrationAssetInput> Assets;
        }

        [SerializeField] private List<string> _targetPaths = new();
        [SerializeField] private bool _includeSubfolders = true;
        // オフにした拡張子（小文字・ドット付き。拡張子なしは空文字）。最初は空＝すべてオン
        [SerializeField] private List<string> _disabledExtensions = new();
        // 文字列は [Delayed] で、Enter かフォーカスが外れたときに確定する（1文字ごとに全行を作り直さないため）
        [Delayed]
        [SerializeField] private string _targetGroup = string.Empty;
        [Delayed]
        [SerializeField] private List<string> _labels = new();
        [SerializeField] private Vector2 _mainScroll;
        [SerializeField] private Vector2 _rowScroll;
        [SerializeField] private Vector2 _extensionScroll;
        [SerializeField] private string _manualLabel = string.Empty;

        private readonly List<TargetGroup> _groups = new();
        private readonly List<RegistrationManualRow> _rows = new();
        private readonly List<RegistrationManualRow> _groupRows = new();
        private readonly List<KeyValuePair<string, int>> _extensionCounts = new();
        // サムネイルのキャッシュ（見えている行の分だけ読み込み、できあがったものを持つ。集め直すときに捨てる）
        private readonly Dictionary<string, Texture2D> _thumbnails = new();
        // プレビューを一度要求して、まだできていないアセット（種類のアイコンに落とすかを、次の描画で決める）
        private readonly HashSet<string> _pendingPreviews = new();
        private readonly RegistrationLogOutput _logOutput = new();
        private SerializedObject _serialized;
        private RegistrationEnvironment _environment;
        private RegistrationRuleInput _firstSpec;
        private AddressableRegistrationSettings _registration;
        private int _hiddenRegisteredCount;
        private bool _needsCollect = true;
        private bool _needsRebuild;
        private bool _hasSettings = true;
        private bool _autoRegistrationEnabled = true;
        private string _infoMessage = string.Empty;

        // ---- 入口（Project ウィンドウの右クリック） ----

        [MenuItem(MENU_PATH, true)]
        private static bool CanOpen()
        {
            return GetSelectedPaths().Count > 0;
        }

        [MenuItem(MENU_PATH, false, MENU_PRIORITY)]
        private static void Open()
        {
            var window = GetWindow<AddressableManualRegistrationWindow>(WINDOW_TITLE);
            window.minSize = WINDOW_MIN_SIZE;
            window.SetTargets(GetSelectedPaths());
            window.Show();
        }

        // 選択中のフォルダ・アセット（Assets の中のものだけ）
        private static List<string> GetSelectedPaths()
        {
            var paths = new List<string>();
            foreach (var guid in Selection.assetGUIDs)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith(ASSETS_PREFIX) && !paths.Contains(path)) paths.Add(path);
            }
            return paths;
        }

        // 開くたびに、対象と画面の指定を既定の状態にする。グループ・Label の初期値は Editor SO の設定
        private void SetTargets(List<string> paths)
        {
            _registration = AddressableEditorSettingsLocator.FindRegistrationSettings();
            _targetPaths = paths;
            _includeSubfolders = true;
            _disabledExtensions = new List<string>();
            _targetGroup = _registration.AccDefaultTargetGroup;
            _labels = new List<string>(_registration.AccDefaultLabels);
            _infoMessage = string.Empty;
            _needsCollect = true;
            _serialized = null;
            Repaint();
        }

        private void OnEnable()
        {
            _needsCollect = true;
            EditorText.LanguageChanged += OnLanguageChanged;
        }

        private void OnDisable() => EditorText.LanguageChanged -= OnLanguageChanged;

        // 行ごとの警告・エラーの文言は、組み立てたときの言語のため、作り直す（チェックの状態は変えない）。
        // 結果のメッセージ（キャンセルしました・登録する項目がありません 等）は、組み立てたときの言語の文字列のため、消す
        // （当て直しの画面が、再読み込みでメッセージを消すのと同じ扱い）
        private void OnLanguageChanged()
        {
            _infoMessage = string.Empty;
            _needsRebuild = true;
            Repaint();
        }

        private void OnGUI()
        {
            _serialized ??= new SerializedObject(this);

            if (_needsCollect)
            {
                // 描画の途中で結果が変わらないよう、描画のあとに集める
                _needsCollect = false;
                EditorApplication.delayCall += Collect;
            }
            // 画面の指定が変わったら、集め直さずに、行だけ作り直す。Layout のときに行い、同じ描画の中で内容をそろえる
            if (_needsRebuild && Event.current.type == EventType.Layout)
            {
                _needsRebuild = false;
                RebuildRows();
            }

            // 上の部分（対象・拡張子・対象アセット）は外側のスクロールに入れ、ウィンドウが小さくても動かせるようにする。
            // 下の部分（各種設定・登録ボタン）は、ウィンドウが小さくても常に見えるよう、スクロールの外に固定する。
            // それぞれ枠（Box）で囲み、境界が分かるようにする
            _mainScroll = EditorGUILayout.BeginScrollView(_mainScroll, GUILayout.ExpandHeight(true));
            if (!_hasSettings) EditorGUILayout.HelpBox(EditorText.Get("rule.text.noSettings"), MessageType.Warning);
            if (_infoMessage.Length > 0) EditorGUILayout.HelpBox(_infoMessage, MessageType.Info);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) DrawTargets();
            if (_extensionCounts.Count > 0)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) DrawExtensions();
            }
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) DrawRows();
            EditorGUILayout.EndScrollView();

            _serialized.Update();
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(EditorText.Get("manual.settings"), EditorStyles.boldLabel);
                DrawGroup();
                DrawLabels();
            }
            if (EditorGUI.EndChangeCheck())
            {
                _serialized.ApplyModifiedProperties();
                _needsRebuild = true;
            }
            _serialized.ApplyModifiedProperties();

            DrawFooter();
        }

        // ---- 集める・作り直す ----

        // 対象のアセットを集める（1. 読み込んで一覧を表示）。重いので、開いたとき・サブフォルダの切り替え・登録のあとにだけ行う
        private void Collect()
        {
            // delayCall で呼ばれるため、その間にウィンドウが閉じられていたら何もしない
            if (this == null) return;

            TryCollect();
        }

        // 集める。進捗バーをキャンセルされたら false（そのとき _infoMessage にキャンセルしたことを入れる）
        private bool TryCollect()
        {
            _groups.Clear();
            _rows.Clear();
            _extensionCounts.Clear();
            _thumbnails.Clear();
            _pendingPreviews.Clear();
            _firstSpec = null;
            _registration = AddressableEditorSettingsLocator.FindRegistrationSettings();
            _autoRegistrationEnabled = _registration.AccAutoRegistration;

            var settings = RegistrationRuleScanner.GetSettings();
            _hasSettings = settings != null;
            if (settings == null)
            {
                Repaint();
                return true;
            }

            for (var i = 0; i < _targetPaths.Count; i++)
            {
                if (!AddTargetGroup(settings, _targetPaths[i]))
                {
                    _groups.Clear();
                    _infoMessage = EditorText.Get("manual.info.canceled");
                    Repaint();
                    return false;
                }
            }

            _environment = RegistrationRuleScanner.CreateEnvironment(settings);
            CountExtensions();
            RebuildRows();
            Repaint();
            return true;
        }

        // 対象1つ分を集める。進捗バーをキャンセルされたら false
        private bool AddTargetGroup(AddressableAssetSettings settings, string path)
        {
            var assets = new List<RegistrationAssetInput>();
            if (AssetDatabase.IsValidFolder(path))
            {
                if (RegistrationRuleScanner.TryCollectAssets(settings, new[] { path }, true, assets) == RegistrationScanResult.Canceled) return false;
                // サブフォルダを含めないときは、そのフォルダ直下のアセットだけにする
                if (!_includeSubfolders) assets.RemoveAll(asset => Path.GetDirectoryName(asset.AccPath).Replace('\\', '/') != path);
                _groups.Add(new TargetGroup { Root = path, IsExplicitAsset = false, Assets = assets });
                return true;
            }

            // アセットを直接選んだときは、そのアセットだけを対象にする（{Root} は、そのアセットを含むフォルダ）
            if (RegistrationRuleScanner.TryCreateAssetInput(settings, path, string.Empty, out var input))
            {
                assets.Add(input);
                _groups.Add(new TargetGroup { Root = Path.GetDirectoryName(path).Replace('\\', '/'), IsExplicitAsset = true, Assets = assets });
            }
            return true;
        }

        // 対象に実在する拡張子と件数を数える（2. 拡張子で絞り込む、の一覧）。オフにした拡張子も含めて数える。
        // アセットを直接選んだものは、拡張子では絞らないため数えない
        private void CountExtensions()
        {
            var counts = new Dictionary<string, int>();
            var seen = new HashSet<string>();
            _hiddenRegisteredCount = 0;
            for (var i = 0; i < _groups.Count; i++)
            {
                var spec = CreateSpec(_groups[i], false);
                foreach (var asset in _groups[i].Assets)
                {
                    if (RegistrationRuleMatcher.GetExclusionKind(asset.AccPath) != RegistrationExclusionKind.None) continue;
                    if (!RegistrationRuleMatcher.IsMatch(spec, asset.AccPath) || !seen.Add(asset.AccPath)) continue;

                    // 登録済みのアセットは一覧に出さない（件数にも入れず、出さなかった数だけ知らせる）
                    if (asset.AccIsRegistered)
                    {
                        _hiddenRegisteredCount++;
                        continue;
                    }
                    if (_groups[i].IsExplicitAsset) continue;

                    var extension = GetExtensionKey(asset.AccPath);
                    counts[extension] = counts.TryGetValue(extension, out var count) ? count + 1 : 1;
                }
            }

            _extensionCounts.AddRange(counts);
            _extensionCounts.Sort((a, b) => a.Value != b.Value ? b.Value.CompareTo(a.Value) : string.CompareOrdinal(a.Key, b.Key));
        }

        // 画面の指定から行を作り直す。チェックは、同じパスの行なら引き継ぐ（指定を変えるたびに選び直さないため）
        private void RebuildRows()
        {
            var previous_checked = new Dictionary<string, bool>();
            for (var i = 0; i < _rows.Count; i++) previous_checked[_rows[i].AccAssetPath] = _rows[i].AccChecked;

            _rows.Clear();
            _firstSpec = null;
            if (_environment == null) return;

            // 自動登録がオフのときは、ルールの SO を無いものとして扱い、手動で登録できる
            var rules = _autoRegistrationEnabled ? RegistrationRuleScanner.GetRuleInputs() : new List<RegistrationRuleInput>();
            var seen = new HashSet<string>();
            for (var i = 0; i < _groups.Count; i++)
            {
                var spec = CreateSpec(_groups[i], true);
                _firstSpec ??= spec;
                RegistrationPlanBuilder.BuildManualRows(spec, rules, GetVisibleAssets(_groups[i]), _environment, _groupRows);
                for (var j = 0; j < _groupRows.Count; j++)
                {
                    var row = _groupRows[j];
                    // フォルダとその中のアセットを両方選んだときなど、同じアセットは1行にする
                    if (!seen.Add(row.AccAssetPath)) continue;
                    if (row.AccCanCheck && previous_checked.TryGetValue(row.AccAssetPath, out var was_checked)) row.AccChecked = was_checked;
                    _rows.Add(row);
                }
            }
        }

        // 一覧に出すアセット。登録済みのものは出さない。
        // 「拡張子なし」をオフにしたときは、除外する拡張子の一覧では表せない（空の拡張子は一覧に入らない）ため、ここで取り除く
        private List<RegistrationAssetInput> GetVisibleAssets(TargetGroup group)
        {
            var hide_no_extension = !group.IsExplicitAsset && _disabledExtensions.Contains(string.Empty);
            var visible = new List<RegistrationAssetInput>(group.Assets.Count);
            for (var i = 0; i < group.Assets.Count; i++)
            {
                var asset = group.Assets[i];
                if (asset.AccIsRegistered) continue;
                if (hide_no_extension && GetExtensionKey(asset.AccPath).Length == 0) continue;
                visible.Add(asset);
            }
            return visible;
        }

        // 画面の指定を、ルールの入力と同じ形にする。対象はすべて（All）で、オフにした拡張子だけを除外する。
        // アドレスのテンプレート・除外パターンは Editor SO の初期値、新規グループの Template は Editor SO の既定（Writer に渡す）。
        // アセットを直接選んだときは、拡張子で絞らない。apply_extension_filter が false のときは、オフにした拡張子も除外しない（数えるため）
        private RegistrationRuleInput CreateSpec(TargetGroup group, bool apply_extension_filter)
        {
            var excluded = group.IsExplicitAsset || !apply_extension_filter ? new List<string>() : _disabledExtensions;
            return new RegistrationRuleInput(
                string.Empty, string.Empty, group.Root, true, RegistrationTargetMode.All,
                new List<string>(), excluded, new List<string>(_registration.AccDefaultExcludePatterns),
                _registration.AccDefaultAddressTemplate, _targetGroup, string.Empty, _labels, RegistrationAddressMode.Follow);
        }

        // ---- 登録 ----

        private void Register()
        {
            var settings = RegistrationRuleScanner.GetSettings();
            if (settings == null || _firstSpec == null) return;

            var plan = RegistrationPlanBuilder.BuildManual(_firstSpec, _rows, _environment);
            if (!plan.AccHasChanges)
            {
                _infoMessage = EditorText.Get("manual.info.nothing");
                Repaint();
                return;
            }

            // グループの変更は Undo で戻しにくいため、書き込む前に確認する
            if (!EditorUtility.DisplayDialog(EditorText.Get("manual.dialog.title"), RegistrationLogFormatter.FormatConfirmation(plan), EditorText.Get("manual.dialog.ok"), EditorText.Get("manual.dialog.cancel"))) return;

            var result = RegistrationEntryWriter.Apply(settings, plan, _registration.AccDefaultGroupTemplate, true);
            AssetDatabase.SaveAssets();

            RegistrationLogFormatter.Format(plan, _registration.AccLogDetail, _logOutput);
            RegistrationLogPrinter.Print(_logOutput);
            if (result.AccFailedCount > 0)
                AddressableLogger.LogWarning(RegistrationLogFormatter.Prefix + EditorText.Format("rule.info.failed", result.AccFailedCount));
            if (result.AccSkippedCount > 0)
                AddressableLogger.LogWarning(RegistrationLogFormatter.FormatSkipped(result.AccSkippedCount));

            var message = result.AccCanceled
                ? EditorText.Format("rule.info.canceled", result.AccAppliedCount)
                : EditorText.Format("rule.info.applied", result.AccAppliedCount);
            if (result.AccSkippedCount > 0) message += $"\n{RegistrationLogFormatter.FormatSkipped(result.AccSkippedCount)}";

            // 読み込み直す。その読み込みがキャンセルされたときは、そのメッセージも残す（書き込みの結果で上書きしない）
            _infoMessage = TryCollect() ? message : $"{message}\n{_infoMessage}";
        }

        // ---- 描画：対象と拡張子の絞り込み ----

        private void DrawTargets()
        {
            EditorGUILayout.LabelField(EditorText.Get("manual.targets"), EditorStyles.boldLabel);
            for (var i = 0; i < _targetPaths.Count; i++) EditorGUILayout.LabelField(_targetPaths[i]);

            var include = EditorGUILayout.Toggle(EditorText.Get("manual.includeSubfolders"), _includeSubfolders);
            if (include != _includeSubfolders)
            {
                _includeSubfolders = include;
                _needsCollect = true;
            }
        }

        // 拡張子ごとのオン・オフ（オフにした拡張子は、一覧から消える）
        private void DrawExtensions()
        {
            if (_extensionCounts.Count == 0) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(EditorText.Get("manual.filter.title"), EditorStyles.boldLabel);
                if (GUILayout.Button(EditorText.Get("manual.filter.allOn"), EditorStyles.miniButton, GUILayout.Width(70f))) SetAllExtensions(true);
                if (GUILayout.Button(EditorText.Get("manual.filter.allOff"), EditorStyles.miniButton, GUILayout.Width(70f))) SetAllExtensions(false);
            }

            var list_height = Mathf.Max(EXTENSION_LIST_MIN_HEIGHT, position.height * EXTENSION_LIST_HEIGHT_RATIO);
            _extensionScroll = EditorGUILayout.BeginScrollView(_extensionScroll, GUILayout.Height(list_height));
            for (var i = 0; i < _extensionCounts.Count; i++)
            {
                var key = _extensionCounts[i].Key;
                var label = (key.Length == 0 ? EditorText.Get("manual.noExtension") : key) + $"  ({_extensionCounts[i].Value})";
                var enabled = EditorGUILayout.ToggleLeft(label, !_disabledExtensions.Contains(key));
                if (enabled == !_disabledExtensions.Contains(key)) continue;

                if (enabled) _disabledExtensions.Remove(key);
                else _disabledExtensions.Add(key);
                _needsRebuild = true;
            }
            EditorGUILayout.EndScrollView();
        }

        private void SetAllExtensions(bool enabled)
        {
            _disabledExtensions.Clear();
            if (!enabled)
            {
                for (var i = 0; i < _extensionCounts.Count; i++) _disabledExtensions.Add(_extensionCounts[i].Key);
            }
            _needsRebuild = true;
        }

        // ---- 描画：グループ・Label ----

        private void DrawGroup()
        {
            EditorGUILayout.PropertyField(
                _serialized.FindProperty("_targetGroup"),
                new GUIContent(EditorText.Get("manual.group.label"), EditorText.Get("manual.group.tooltip")));
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth);
                using (new EditorGUI.DisabledScope(!_hasSettings))
                {
                    if (GUILayout.Button(EditorText.Get("manual.group.pick"), GUILayout.Width(140f)))
                    {
                        var button_rect = GUILayoutUtility.GetLastRect();
                        new RegistrationGroupDropdown(RegistrationRuleScanner.GetSettings(), _targetGroup, SelectGroup).Show(button_rect);
                    }
                }
            }
        }

        private void DrawLabels()
        {
            var list = _serialized.FindProperty("_labels");
            EditorGUILayout.PropertyField(list, new GUIContent("Label"), true);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!_hasSettings))
                {
                    if (GUILayout.Button(EditorText.Get("manual.label.pick"), GUILayout.Width(120f)))
                    {
                        var button_rect = GUILayoutUtility.GetLastRect();
                        new RegistrationLabelDropdown(RegistrationRuleScanner.GetSettings(), RegistrationListProperty.Read(list), ToggleLabel).Show(button_rect);
                    }
                }

                _manualLabel = EditorGUILayout.TextField(_manualLabel);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_manualLabel)))
                {
                    if (GUILayout.Button(EditorText.Get("manual.label.add"), GUILayout.Width(48f)))
                    {
                        RegistrationListProperty.AddLabel(list, _manualLabel);
                        _manualLabel = string.Empty;
                        GUI.FocusControl(null);
                    }
                }
            }

            if (_targetGroup.Contains(RULE_NAME_MACRO) || ContainsRuleName(_labels))
                EditorGUILayout.HelpBox(EditorText.Get("manual.text.ruleName"), MessageType.Warning);
        }

        private static bool ContainsRuleName(List<string> labels)
        {
            for (var i = 0; i < labels.Count; i++)
            {
                if (labels[i] != null && labels[i].Contains(RULE_NAME_MACRO)) return true;
            }
            return false;
        }

        // ---- 描画：アセットの一覧と登録 ----

        private void DrawRows()
        {
            EditorGUILayout.LabelField(EditorText.Format("manual.rows.title", _rows.Count), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(EditorText.Get("manual.text.autoNote"), EditorStyles.miniLabel);
            if (_hiddenRegisteredCount > 0)
                EditorGUILayout.LabelField(EditorText.Format("manual.rows.hidden", _hiddenRegisteredCount), EditorStyles.miniLabel);

            if (_rows.Count == 0)
            {
                if (_hasSettings) EditorGUILayout.HelpBox(EditorText.Get("manual.text.noRow"), MessageType.None);
                return;
            }

            // 大量のアセットでも重くならないよう、見えている行だけを描く（行の高さを固定して、上下を空白で埋める）
            // ウィンドウの高さの割合で決める（ウィンドウを大きくすると、一覧も大きくなる）
            var list_height = Mathf.Max(ROW_HEIGHT * ROW_LIST_MIN_VISIBLE_ROWS, position.height * ROW_LIST_HEIGHT_RATIO);
            _rowScroll = EditorGUILayout.BeginScrollView(_rowScroll, GUILayout.Height(list_height));
            var first = Mathf.Clamp(Mathf.FloorToInt(_rowScroll.y / ROW_HEIGHT), 0, _rows.Count);
            var last = Mathf.Min(_rows.Count, first + Mathf.CeilToInt(position.height / ROW_HEIGHT) + 2);
            GUILayout.Space(first * ROW_HEIGHT);
            for (var i = first; i < last; i++) DrawRow(_rows[i]);
            GUILayout.Space((_rows.Count - last) * ROW_HEIGHT);
            EditorGUILayout.EndScrollView();
        }

        // アセットのサムネイル。画像・Prefab などは中身の絵、それ以外や読み込み中は種類のアイコン。読み込み中は、できあがるまで描き直す。
        // プレビューを持たない種類（.asset・.anim など）は、プレビューが来ないと分かった時点で種類のアイコンをキャッシュに入れる
        // （入れないと、描画のたびにアセットを読み込み直してしまう）
        private Texture2D GetThumbnail(string asset_path)
        {
            if (_thumbnails.TryGetValue(asset_path, out var cached)) return cached;

            var asset = AssetDatabase.LoadMainAssetAtPath(asset_path);
            if (asset == null) return null;

            var preview = AssetPreview.GetAssetPreview(asset);
            if (preview != null)
            {
                _thumbnails[asset_path] = preview;
                return preview;
            }

            // 読み込み中、または要求した直後は、できあがるまで仮のアイコンを出して描き直す。
            // 1回待ってもプレビューが来ない（読み込み中でもない）ものは、プレビューを持たない種類とみなす
            if (AssetPreview.IsLoadingAssetPreview(asset.GetInstanceID()) || _pendingPreviews.Add(asset_path))
            {
                _pendingPreviews.Add(asset_path);
                Repaint();
                return AssetDatabase.GetCachedIcon(asset_path) as Texture2D;
            }

            var icon = AssetPreview.GetMiniThumbnail(asset);
            _thumbnails[asset_path] = icon;
            return icon;
        }

        private void DrawRow(RegistrationManualRow row)
        {
            // 横に長くならないよう、パスとアドレスを2段にする（上段はパス、下段はアドレスまたは展開のエラー）
            using (new EditorGUILayout.HorizontalScope(GUILayout.Height(ROW_HEIGHT)))
            {
                using (new EditorGUI.DisabledScope(!row.AccCanCheck))
                {
                    row.AccChecked = EditorGUILayout.Toggle(row.AccChecked, GUILayout.Width(16f));
                }

                GUILayout.Label(GetThumbnail(row.AccAssetPath), GUILayout.Width(THUMBNAIL_SIZE), GUILayout.Height(THUMBNAIL_SIZE));

                // サムネイルのすぐ右からパスを始め、2段の先頭の位置をそろえる。状態は下段の先頭に出す
                using (new EditorGUILayout.VerticalScope())
                {
                    if (GUILayout.Button(new GUIContent(row.AccAssetPath, row.AccWarnings.Count > 0 ? string.Join("\n", row.AccWarnings) : string.Empty), EditorStyles.linkLabel))
                        PingAsset(row.AccAssetPath);
                    var detail = row.AccError.Length > 0 ? "! " + row.AccError : "→ " + row.AccExpandedAddress;
                    GUILayout.Label($"{GetStateText(row)}   {detail}", EditorStyles.miniLabel);
                }
            }
        }

        private void DrawFooter()
        {
            var checked_count = 0;
            for (var i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].AccCanCheck && _rows[i].AccChecked) checked_count++;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(EditorText.Get("manual.footer.selectAll"), GUILayout.Width(90f))) SetAllChecked(true);
                if (GUILayout.Button(EditorText.Get("manual.footer.clearAll"), GUILayout.Width(90f))) SetAllChecked(false);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(checked_count == 0))
                {
                    if (GUILayout.Button(EditorText.Format("manual.footer.register", checked_count), GUILayout.Width(160f))) EditorApplication.delayCall += Register;
                }
            }
        }

        private void SetAllChecked(bool value)
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].AccCanCheck) _rows[i].AccChecked = value;
            }
        }

        private static string GetStateText(RegistrationManualRow row)
        {
            switch (row.AccState)
            {
                case ManualRegistrationState.Unregistered: return EditorText.Get("manual.state.unregistered");
                case ManualRegistrationState.Registered: return EditorText.Format("manual.state.registered", row.AccCurrentGroup);
                case ManualRegistrationState.AutoRegistered: return EditorText.Format("manual.state.auto", row.AccAutoRuleName);
                case ManualRegistrationState.Conflicting: return EditorText.Get("manual.state.conflicting");
                case ManualRegistrationState.PendingAuto: return EditorText.Get("manual.state.pendingAuto");
                default: return string.Empty;
            }
        }

        // ---- ドロップダウンからの選択（描画の外で実行される） ----

        private void ToggleLabel(string label)
        {
            _serialized.Update();
            RegistrationListProperty.ToggleLabel(_serialized.FindProperty("_labels"), label);
            _serialized.ApplyModifiedProperties();
            _needsRebuild = true;
            Repaint();
        }

        private void SelectGroup(string group_name)
        {
            _serialized.Update();
            _serialized.FindProperty("_targetGroup").stringValue = group_name;
            _serialized.ApplyModifiedProperties();
            _needsRebuild = true;
            Repaint();
        }

        // 拡張子のキー（小文字・ドット付き。拡張子なしは空文字）
        private static string GetExtensionKey(string asset_path)
        {
            var normalized = RegistrationRuleInput.NormalizeExtension(Path.GetExtension(asset_path));
            return normalized.Length == 0 ? string.Empty : "." + normalized;
        }

        private static void PingAsset(string asset_path)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(asset_path);
            if (asset == null) return;
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }
    }
}
