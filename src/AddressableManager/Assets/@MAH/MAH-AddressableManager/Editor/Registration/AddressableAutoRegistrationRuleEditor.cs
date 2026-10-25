using System.Collections.Generic;
using MAH.AddressableManager.Editor.Localization;
using UnityEditor;
using UnityEngine;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary>
    /// ルールの SO の Inspector（仕様 3.6）。拡張子の選択（ドロップダウン・手入力）と、読み取り専用のプレビュー
    /// （対象のアセットの数・最初の 3 件の展開結果・警告）を出す。プレビューは書き込まない。
    /// 対象のアセットを探す処理は重いため、描画のたびには行わず、値を変えたときに 1 回だけ計算して持つ。
    /// </summary>
    [CustomEditor(typeof(AddressableAutoRegistrationRuleSO))]
    internal sealed class AddressableAutoRegistrationRuleEditor : UnityEditor.Editor
    {

        private SerializedProperty _targetModeProperty;
        private SerializedProperty _extensionsProperty;
        private SerializedProperty _labelsProperty;
        private RegistrationRulePreview _preview;
        private string _previewMessage = string.Empty;
        private bool _isPreviewDirty = true;
        private string _manualExtension = string.Empty;
        private string _manualLabel = string.Empty;

        // 対象フォルダのアセットの一覧と Addressables 側の状態のキャッシュ。集めるのは重いため、
        // 対象フォルダが変わったとき・開いたとき・プロジェクトが変わったときだけ集め直す（それ以外の変更では展開だけをやり直す）
        private readonly List<RegistrationAssetInput> _cachedAssets = new();
        private RegistrationEnvironment _cachedEnvironment;
        private string _cachedFolderPath = string.Empty;
        private bool _isCacheValid;

        private void OnEnable()
        {
            _targetModeProperty = serializedObject.FindProperty("_targetMode");
            _extensionsProperty = serializedObject.FindProperty("_extensions");
            _labelsProperty = serializedObject.FindProperty("_labels");
            _isPreviewDirty = true;
            _isCacheValid = false;
            Undo.undoRedoPerformed += MarkPreviewDirty;
            EditorApplication.projectChanged += OnProjectChanged;
            EditorText.LanguageChanged += MarkPreviewDirty; // プレビューの警告は組み立てたときの言語のため、作り直す
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= MarkPreviewDirty;
            EditorApplication.projectChanged -= OnProjectChanged;
            EditorText.LanguageChanged -= MarkPreviewDirty;
        }

        // アセットの追加・削除・Addressables の設定の変更などがあったら、集めた結果を捨てて、プレビューを計算し直す
        private void OnProjectChanged()
        {
            _isCacheValid = false;
            MarkPreviewDirty();
        }

        public override void OnInspectorGUI()
        {
            // 計算は Layout のときに行い、同じ描画の中で Layout と Repaint の内容がそろうようにする
            if (_isPreviewDirty && Event.current.type == EventType.Layout) RefreshPreview();

            serializedObject.Update();
            EditorGUI.BeginChangeCheck();

            DrawField("_enabled");
            DrawField("_targetFolder");
            LocalizedPropertyView.Field(serializedObject, _targetModeProperty, false);
            if ((RegistrationTargetMode)_targetModeProperty.enumValueIndex == RegistrationTargetMode.Extensions)
            {
                DrawExtensions();
            }
            else
            {
                DrawField("_excludedExtensions");
            }
            DrawField("_excludePatterns");
            DrawField("_addressTemplate");
            DrawTargetGroup();
            DrawField("_groupTemplateAsset");
            DrawLabels();
            DrawField("_addressMode");

            var changed = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();
            if (changed) MarkPreviewDirty();

            DrawPreview();
        }

        private void DrawField(string property_name)
        {
            LocalizedPropertyView.Field(serializedObject, serializedObject.FindProperty(property_name));
        }

        // 拡張子のリストと、選択・手入力の操作
        private void DrawExtensions()
        {
            LocalizedPropertyView.Field(serializedObject, _extensionsProperty);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(EditorText.Get("ruleEditor.pickExtensions"), GUILayout.Width(120f)))
                {
                    var button_rect = GUILayoutUtility.GetLastRect();
                    ShowExtensionDropdown(button_rect);
                }

                _manualExtension = EditorGUILayout.TextField(_manualExtension);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_manualExtension)))
                {
                    if (GUILayout.Button(EditorText.Get("manual.label.add"), GUILayout.Width(48f)))
                    {
                        AddExtension(_manualExtension);
                        _manualExtension = string.Empty;
                        GUI.FocusControl(null);
                    }
                }
            }
        }

        // グループ名のテンプレートの入力欄と、既存のグループから選ぶボタン。マクロや、まだ無いグループは、入力欄に直接書ける
        private void DrawTargetGroup()
        {
            var group_property = serializedObject.FindProperty("_targetGroup");
            LocalizedPropertyView.Field(serializedObject, group_property, false);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth);
                // Addressables の設定が無いときは、グループを一覧できないため押せない
                using (new EditorGUI.DisabledScope(RegistrationRuleScanner.GetSettings() == null))
                {
                    if (GUILayout.Button(EditorText.Get("manual.group.pick"), GUILayout.Width(140f)))
                    {
                        var button_rect = GUILayoutUtility.GetLastRect();
                        new RegistrationGroupDropdown(RegistrationRuleScanner.GetSettings(), group_property.stringValue, SelectGroup).Show(button_rect);
                    }
                }
            }
        }

        // 選ばれたグループ名を、グループ名のテンプレートにする（ドロップダウンの選択から呼ばれる。描画の外で実行される）
        private void SelectGroup(string group_name)
        {
            serializedObject.Update();
            serializedObject.FindProperty("_targetGroup").stringValue = group_name;
            serializedObject.ApplyModifiedProperties();
            MarkPreviewDirty();
            Repaint();
        }

        // Label のリストと、選択・手入力の操作。手入力では、マクロ（{Folder.Top} など）やまだ無い Label も足せる
        private void DrawLabels()
        {
            LocalizedPropertyView.Field(serializedObject, _labelsProperty);

            using (new EditorGUILayout.HorizontalScope())
            {
                // Addressables の設定が無いときは、Label を一覧できないため押せない
                using (new EditorGUI.DisabledScope(RegistrationRuleScanner.GetSettings() == null))
                {
                    if (GUILayout.Button(EditorText.Get("manual.label.pick"), GUILayout.Width(120f)))
                    {
                        var button_rect = GUILayoutUtility.GetLastRect();
                        new RegistrationLabelDropdown(RegistrationRuleScanner.GetSettings(), RegistrationListProperty.Read(_labelsProperty), ToggleLabel).Show(button_rect);
                    }
                }

                _manualLabel = EditorGUILayout.TextField(_manualLabel);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_manualLabel)))
                {
                    if (GUILayout.Button(EditorText.Get("manual.label.add"), GUILayout.Width(48f)))
                    {
                        AddLabel(_manualLabel);
                        _manualLabel = string.Empty;
                        GUI.FocusControl(null);
                    }
                }
            }
        }

        // 選ばれた Label を、すでにあれば外し、なければ足す（ドロップダウンの選択から呼ばれる。描画の外で実行される）
        private void ToggleLabel(string label)
        {
            serializedObject.Update();
            RegistrationListProperty.ToggleLabel(_labelsProperty, label);
            serializedObject.ApplyModifiedProperties();
            MarkPreviewDirty();
        }

        // 手入力で足す（すでにあれば何もしない）
        private void AddLabel(string label)
        {
            RegistrationListProperty.AddLabel(_labelsProperty, label);
            MarkPreviewDirty();
        }

        private void ShowExtensionDropdown(Rect button_rect)
        {
            var rule = (AddressableAutoRegistrationRuleSO)target;
            var folder_extensions = RegistrationRuleScanner.CollectExtensionCounts(rule.GetTargetFolderPath());
            var dropdown = new RegistrationExtensionDropdown(folder_extensions, RegistrationListProperty.Read(_extensionsProperty), ToggleExtension);
            dropdown.Show(button_rect);
        }

        // 選ばれた拡張子を、すでにあれば外し、なければ足す（ドロップダウンの選択から呼ばれる。描画の外で実行される）
        private void ToggleExtension(string extension)
        {
            serializedObject.Update();
            RegistrationListProperty.ToggleExtension(_extensionsProperty, extension);
            serializedObject.ApplyModifiedProperties();
            MarkPreviewDirty();
        }

        // 手入力で足す（すでにあれば何もしない）
        private void AddExtension(string extension)
        {
            RegistrationListProperty.AddExtension(_extensionsProperty, extension);
            MarkPreviewDirty();
        }

        private void MarkPreviewDirty()
        {
            _isPreviewDirty = true;
            Repaint();
        }

        // プレビューを計算して持つ。Addressables の設定が無いときは、計算せずに理由を出す
        private void RefreshPreview()
        {
            _isPreviewDirty = false;
            _preview = null;
            _previewMessage = string.Empty;

            var settings = RegistrationRuleScanner.GetSettings();
            if (settings == null)
            {
                _previewMessage = EditorText.Get("rule.text.noSettings");
                return;
            }

            var rule_input = ((AddressableAutoRegistrationRuleSO)target).ToInput();
            if (!_isCacheValid || _cachedFolderPath != rule_input.AccFolderPath)
            {
                _cachedAssets.Clear();
                if (rule_input.AccFolderPath.Length > 0)
                    RegistrationRuleScanner.TryCollectAssets(settings, new[] { rule_input.AccFolderPath }, false, _cachedAssets);
                _cachedEnvironment = RegistrationRuleScanner.CreateEnvironment(settings);
                _cachedFolderPath = rule_input.AccFolderPath;
                _isCacheValid = true;
            }

            _preview = RegistrationRulePreviewBuilder.Build(
                rule_input, _cachedAssets, RegistrationRuleScanner.GetRuleInputs(), _cachedEnvironment);
        }

        private void DrawPreview()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(EditorText.Get("ruleEditor.preview.title"), EditorStyles.boldLabel);

            if (_preview == null)
            {
                if (_previewMessage.Length > 0) EditorGUILayout.HelpBox(_previewMessage, MessageType.Info);
                return;
            }

            for (var i = 0; i < _preview.AccWarnings.Count; i++) EditorGUILayout.HelpBox(_preview.AccWarnings[i], MessageType.Warning);

            EditorGUILayout.LabelField(EditorText.Format("ruleEditor.preview.targetCount", _preview.AccTargetCount));
            for (var i = 0; i < _preview.AccSamples.Count; i++)
            {
                var sample = _preview.AccSamples[i];
                var labels = sample.AccLabels.Count > 0 ? string.Join(", ", sample.AccLabels) : EditorText.Get("ruleEditor.preview.none");
                EditorGUILayout.HelpBox(
                    EditorText.Format("ruleEditor.preview.sample", sample.AccAssetPath, sample.AccAddress, sample.AccGroup, labels),
                    MessageType.None);
            }
            if (_preview.AccTargetCount > _preview.AccSamples.Count)
                EditorGUILayout.LabelField(EditorText.Format("ruleEditor.preview.more", _preview.AccTargetCount - _preview.AccSamples.Count));
        }
    }
}
