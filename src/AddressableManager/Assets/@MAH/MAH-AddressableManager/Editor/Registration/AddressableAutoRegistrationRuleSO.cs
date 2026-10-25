using System;
using System.Collections.Generic;
using MAH.AddressableManager.Core;
using MAH.AddressableManager.Editor.Localization;
using MAH.AddressableManager.Editor.SettingsSync;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.Serialization;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary>
    /// 自動登録のルール1つ分（対象フォルダ1つ）。何個でも作れ、型で探すため置き場所は自由。
    /// 設定 SO（Runtime SO・Editor SO）と違い、利用者が何個でも作る「データ」のため、設定 SO の方針の対象外（設計仕様書 3.1.1）。
    /// 作成は専用の入口（RegistrationRuleAssetCreator）から行い、作成時に Editor SO の初期値をコピーする（CreateAssetMenu は使わない）。
    /// </summary>
    internal sealed class AddressableAutoRegistrationRuleSO : ScriptableObject
    {
        private const string ASSETS_PREFIX = "Assets/";

        [SerializeField] private bool _enabled = true;

        [SerializeField] private DefaultAsset _targetFolder;

        [SerializeField] private RegistrationTargetMode _targetMode = RegistrationTargetMode.Extensions;

        // 文字列の項目は [Delayed] で、Enter かフォーカスが外れたときに確定する。
        // 1文字ごとに OnValidate・プレビューの計算・重なりの警告が走るのを防ぐため
        [Delayed]
        [SerializeField] private List<string> _extensions = new();

        [Delayed]
        [SerializeField] private List<string> _excludedExtensions = new();

        [Delayed]
        [SerializeField] private List<string> _excludePatterns = new();

        [Delayed]
        [SerializeField] private string _addressTemplate = string.Empty;

        [FormerlySerializedAs("_groupTemplate")] // 旧名。すでに作ったルールの SO の値を引き継ぐため
        [Delayed]
        [SerializeField] private string _targetGroup = string.Empty;

        [SerializeField] private AddressableAssetGroupTemplate _groupTemplateAsset;

        [Delayed]
        [SerializeField] private List<string> _labels = new();

        [SerializeField] private RegistrationAddressMode _addressMode = RegistrationAddressMode.Follow;

        // 前回に警告した、重なっている相手のルールの GUID（つないだ文字列）。同じ組なら、また Console に出さない。保存しない
        [NonSerialized] private string _lastOverlapKey;

        /// <summary> このルールが有効か </summary>
        internal bool AccEnabled => _enabled;
        /// <summary> 対象の指定方法 </summary>
        internal RegistrationTargetMode AccTargetMode => _targetMode;

        /// <summary> Unity が、作成時・コンパイル後・Inspector の変更時に呼ぶ。null のリストを補い、ルールのキャッシュを無効にする </summary>
        private void OnValidate()
        {
            _extensions ??= new List<string>();
            _excludedExtensions ??= new List<string>();
            _excludePatterns ??= new List<string>();
            _labels ??= new List<string>();
            _addressTemplate ??= string.Empty;
            _targetGroup ??= string.Empty;

            // 値が変わったので、次に使うときに読み直す
            RegistrationRuleScanner.InvalidateRules();

            // Inspector で編集中のときだけ、他のルールとの重なり（競合しそうか）を警告する（読み込みのたびに出さないため）
            if (Selection.activeObject == this) ScheduleOverlapWarning();
        }

        /// <summary> Matcher・PlanBuilder に渡す入力に変える（Unity の型を持たない形） </summary>
        internal RegistrationRuleInput ToInput()
        {
            var asset_path = AssetDatabase.GetAssetPath(this);
            var group_template_guid = _groupTemplateAsset != null
                ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(_groupTemplateAsset))
                : string.Empty;

            return new RegistrationRuleInput(
                name, AssetDatabase.AssetPathToGUID(asset_path), GetTargetFolderPath(), _enabled, _targetMode,
                _extensions, _excludedExtensions, _excludePatterns,
                _addressTemplate, _targetGroup, group_template_guid, _labels, _addressMode);
        }

        /// <summary>
        /// 作成直後の値を、Editor SO の「ルールの作成時の初期値」からコピーして決める（仕様 3.4）。
        /// コピーしたあとは Editor SO と無関係になる。folder_path は対象フォルダの初期値（空ならなし）
        /// </summary>
        internal void InitializeFrom(AddressableRegistrationSettings settings, string folder_path)
        {
            _enabled = true;
            _targetMode = RegistrationTargetMode.Extensions;
            _extensions = new List<string>();
            _excludedExtensions = new List<string>();
            _excludePatterns = new List<string>(settings.AccDefaultExcludePatterns);
            _addressTemplate = settings.AccDefaultAddressTemplate;
            _targetGroup = string.Empty;
            _groupTemplateAsset = null;
            _labels = new List<string>(settings.AccDefaultLabels);
            _addressMode = RegistrationAddressMode.Follow;
            _targetFolder = string.IsNullOrEmpty(folder_path) ? null : AssetDatabase.LoadAssetAtPath<DefaultAsset>(folder_path);
        }

        /// <summary> 対象フォルダのパスを返す。未指定・フォルダでない・Assets の外のときは空 </summary>
        internal string GetTargetFolderPath()
        {
            if (_targetFolder == null) return string.Empty;

            var path = AssetDatabase.GetAssetPath(_targetFolder);
            if (!path.StartsWith(ASSETS_PREFIX) || !AssetDatabase.IsValidFolder(path)) return string.Empty;
            return path;
        }

        // 重なっている相手の組を表す文字列（GUID を並べてつなぐ。重なりがなければ空）
        private static string BuildOverlapKey(List<RegistrationRuleInput> overlapping)
        {
            var guids = new List<string>(overlapping.Count);
            for (var i = 0; i < overlapping.Count; i++) guids.Add(overlapping[i].AccGuid);
            guids.Sort(string.CompareOrdinal);
            return string.Join("|", guids);
        }

        // 他のルールとの重なりの警告を、現在の処理が終わってから出す（OnValidate の中で FindAssets を呼ばないため）
        private void ScheduleOverlapWarning()
        {
            EditorApplication.delayCall += () =>
            {
                if (this == null) return;

                var self = ToInput();
                var others = RegistrationRuleScanner.GetRuleInputs();
                var overlapping = new List<RegistrationRuleInput>();
                for (var i = 0; i < others.Count; i++)
                {
                    if (others[i].AccGuid == self.AccGuid) continue;
                    if (RegistrationRuleMatcher.AreOverlapping(self, others[i])) overlapping.Add(others[i]);
                }

                // 重なっている組が前回と同じなら、また出さない（重なりに関係ない項目を編集するたびに、同じ警告が出るのを防ぐ）
                var key = BuildOverlapKey(overlapping);
                if (key == _lastOverlapKey) return;
                _lastOverlapKey = key;

                for (var i = 0; i < overlapping.Count; i++)
                    AddressableLogger.LogWarning(EditorText.Format("rule.warn.overlap", self.AccName, overlapping[i].AccName));
            };
        }
    }
}
