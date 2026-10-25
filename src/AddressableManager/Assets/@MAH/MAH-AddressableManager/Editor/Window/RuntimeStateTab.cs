using System;
using System.Collections.Generic;
using MAH.AddressableManager.Core;
using MAH.AddressableManager.Core.Loading;
using MAH.AddressableManager.Editor.Localization;
using UnityEditor;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

namespace MAH.AddressableManager.Editor.Window
{
    /// <summary>
    /// 「実行時状態」タブ。Registry の Entry を一覧し、開くと寿命スコープごとの確保数と、発行済みチケット（確保元のスタックトレース）を見られる。
    /// 寿命の種類（All / Manual / Scene / Owner）で絞り込める。件数・表示用の文字列は、描画のたびに作らないよう、取得時（Refresh）にまとめて作る。
    /// 読み取りは IAddressableDebugReader 越しに行う。Owner の GameObject 名・Scene のシーン名は、
    /// Runtime の Snapshot に UnityEngine.Object を持たせないため、ここ（Editor 側）で解決する。
    /// </summary>
    internal sealed class RuntimeStateTab : IDebugWindowTab
    {
        private const float ICON_SIZE = 16f;

        // 絞り込み。0 は All、1 以降は LifetimeKind の順に対応する（添字 = LifetimeKind の値 + 1）。
        // LifetimeKind に種類が増えても配列が足りなくならないよう、数と名前は enum から導く（static の初期化順に依存するため、この順に書く）
        private const int FILTER_ALL = 0;
        private static readonly int KIND_COUNT = Enum.GetValues(typeof(LifetimeKind)).Length;
        private static readonly int FILTER_COUNT = KIND_COUNT + 1;
        private static readonly string[] FILTER_NAMES = CreateFilterNames();

        // 色は Dark / Light どちらのスキンでも読める中間の明るさにする。添字は LifetimeKind の値。
        // ここに無い種類（LifetimeKind に足した直後など）は、FALLBACK_KIND_COLOR で表示する
        private static readonly Color[] KIND_COLORS =
        {
            new(0.30f, 0.55f, 0.95f), // Manual：青
            new(0.15f, 0.65f, 0.55f), // Scene：緑
            new(0.95f, 0.55f, 0.15f), // Owner：橙
        };
        private static readonly Color FALLBACK_KIND_COLOR = new(0.60f, 0.60f, 0.60f);
        private static readonly Color LOADING_COLOR = new(0.90f, 0.65f, 0.05f);
        private static readonly Color LOADED_COLOR = new(0.25f, 0.70f, 0.30f);
        private static readonly Color FAILED_COLOR = new(0.90f, 0.30f, 0.30f);

        // GUILayout.Width は呼ぶたびに確保するため、使い回す
        private static readonly GUILayoutOption[] REF_COUNT_WIDTH = { GUILayout.Width(64f) };
        private static readonly GUILayoutOption[] STATUS_WIDTH = { GUILayout.Width(88f) };
        private static readonly GUILayoutOption[] GROUP_WIDTH = { GUILayout.Width(72f) };
        private static readonly Comparison<AssetEntrySnapshot> ENTRY_COMPARISON = CompareEntries;

        private readonly List<AssetEntrySnapshot> _entries = new();
        private readonly List<ScopeCountSnapshot> _scopeCounts = new();
        private readonly List<IssuedTicketSnapshot> _tickets = new();
        // 表示用。_entries / _scopeCounts / _tickets と同じ添字で対応する（Refresh のたびに作り直す）
        private readonly List<GUIContent> _entryContents = new(); // 型アイコン＋キー。GUIContent は使い回す（縮めない）
        private readonly List<string> _entryRefCountLabels = new();
        private readonly List<int> _entryKindMasks = new(); // Entry が持つ寿命の種類（1 << LifetimeKind のビット和）。絞り込みの判定用
        private readonly List<string> _scopeLabels = new();
        private readonly List<string> _ticketLabels = new();
        // 絞り込みごとの件数と表示文字列。添字は絞り込み（0 = All）
        private readonly int[] _entryCountByFilter = new int[FILTER_COUNT];
        private readonly int[] _ticketCountByFilter = new int[FILTER_COUNT];
        private readonly string[] _filterLabels = new string[FILTER_COUNT];
        private readonly string[] _summaryLabels = new string[FILTER_COUNT];
        // 開いている Entry とチケットだけを保持する。消えた分は放置し（再登場時に開いたままになるだけ）、Clear で空にする
        private readonly HashSet<(AssetKey, Type)> _expandedEntries = new();
        private readonly HashSet<long> _expandedTickets = new();
        private readonly StackTraceView _stackTraceView = new(); // スタックトレースの表示とジャンプ（「リーク」タブと共通）
        private int _filter = FILTER_ALL;
        private Vector2 _scroll;
        // スタイルは EditorStyles が OnGUI の中でしか使えないため、最初の描画で作る
        private GUIStyle _loadingStyle;
        private GUIStyle _loadedStyle;
        private GUIStyle _failedStyle;
        private GUIStyle _groupStyle;
        private GUIStyle[] _kindStyles;

        public string AccTitle => EditorText.Get("debug.tab.runtimeState");

        internal RuntimeStateTab()
        {
            BuildFilterLabels();
        }

        public void Refresh(IAddressableDebugReader reader)
        {
            reader.CopyAssetEntriesTo(_entries, _scopeCounts);
            reader.CopyIssuedTicketsTo(_tickets);
            _entries.Sort(ENTRY_COMPARISON); // Dictionary の順序は変わり得るため、行が入れ替わらないよう並べる
            BuildEntryLabels();
            BuildScopeLabels();
            BuildTicketLabels();
            BuildFilterLabels();
        }

        // Refresh のたびに全ての表示用の文字列を作り直すため、言語の変更に追従するのに何もしなくてよい
        public void InvalidateLabels()
        {
        }

        public void Clear()
        {
            _entries.Clear();
            _scopeCounts.Clear();
            _tickets.Clear();
            _entryRefCountLabels.Clear();
            _entryKindMasks.Clear();
            _scopeLabels.Clear();
            _ticketLabels.Clear();
            _expandedEntries.Clear();
            _expandedTickets.Clear();
            _stackTraceView.Clear();
            _scroll = Vector2.zero;
            Array.Clear(_entryCountByFilter, 0, FILTER_COUNT);
            Array.Clear(_ticketCountByFilter, 0, FILTER_COUNT);
            BuildFilterLabels();
            // _entryContents の GUIContent は、次の Refresh で使い回すため残す
        }

        public void Draw()
        {
            EnsureStyles();

            EditorGUILayout.LabelField(_summaryLabels[_filter], EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _filter = GUILayout.Toolbar(_filter, _filterLabels, EditorStyles.toolbarButton, GUI.ToolbarButtonSize.FitToContents);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (_entryCountByFilter[_filter] == 0)
            {
                EditorGUILayout.HelpBox(EditorText.Get("runtimeState.empty"), MessageType.None);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            var previous_icon_size = EditorGUIUtility.GetIconSize();
            EditorGUIUtility.SetIconSize(new Vector2(ICON_SIZE, ICON_SIZE));
            for (var i = 0; i < _entries.Count; i++)
            {
                if (_filter != FILTER_ALL && (_entryKindMasks[i] & ToKindMask(_filter - 1)) == 0) continue;
                DrawEntry(i);
            }
            EditorGUIUtility.SetIconSize(previous_icon_size);
            EditorGUILayout.EndScrollView();
        }

        private void DrawEntry(int index)
        {
            var entry = _entries[index];
            var id = (entry.AccKey, entry.AccAssetType);
            var was_open = _expandedEntries.Contains(id);

            EditorGUILayout.BeginHorizontal();
            var is_open = EditorGUILayout.Foldout(was_open, _entryContents[index], true);
            GUILayout.Label(_entryRefCountLabels[index], EditorStyles.miniLabel, REF_COUNT_WIDTH);
            DrawStatusLabel(entry.AccStatus);
            EditorGUILayout.EndHorizontal();

            if (is_open != was_open)
            {
                if (is_open) _expandedEntries.Add(id);
                else _expandedEntries.Remove(id);
            }
            if (!is_open) return;

            EditorGUI.indentLevel++;
            var end = entry.AccScopeCountStart + entry.AccScopeCountLength;
            for (var scope_index = entry.AccScopeCountStart; scope_index < end; scope_index++)
            {
                var scope = _scopeCounts[scope_index];
                if (_filter != FILTER_ALL && (int)scope.AccScopeKind != _filter - 1) continue;

                EditorGUILayout.LabelField(_scopeLabels[scope_index], _kindStyles[(int)scope.AccScopeKind]);
                EditorGUI.indentLevel++;
                DrawTickets(entry, scope);
                EditorGUI.indentLevel--;
            }
            EditorGUI.indentLevel--;
        }

        // Entry とスコープに一致するチケットを描く。対応は AssetKey・型・スコープの一致で付ける（件数は数百規模のため線形探索で足りる）
        private void DrawTickets(AssetEntrySnapshot entry, ScopeCountSnapshot scope)
        {
            for (var i = 0; i < _tickets.Count; i++)
            {
                var ticket = _tickets[i];
                if (ticket.AccKey != entry.AccKey
                    || ticket.AccAssetType != entry.AccAssetType
                    || ticket.AccScopeKind != scope.AccScopeKind
                    || ticket.AccScopeValue != scope.AccScopeValue)
                    continue;

                var was_open = _expandedTickets.Contains(ticket.AccSerial);

                EditorGUILayout.BeginHorizontal();
                var is_open = EditorGUILayout.Foldout(was_open, _ticketLabels[i], true);
                if (ticket.AccIsGroup)
                    GUILayout.Label(EditorText.Get("common.labelGroup"), _groupStyle, GROUP_WIDTH);
                // 「呼び出し元に結果を返し終えたか」を、呼び出し元から見た状態として表示する。
                // Entry のロード状態とは別で、Label 一括ロードでは、先に読み込み終えた分も全体の完了までは「ロード中」になる
                if (ticket.AccIsDelivered)
                    GUILayout.Label(EditorText.Get("runtimeState.status.loaded"), _loadedStyle, STATUS_WIDTH);
                else
                    GUILayout.Label(EditorText.Get("runtimeState.status.loading"), _loadingStyle, STATUS_WIDTH);
                EditorGUILayout.EndHorizontal();

                if (is_open != was_open)
                {
                    if (is_open) _expandedTickets.Add(ticket.AccSerial);
                    else _expandedTickets.Remove(ticket.AccSerial);
                }
                if (is_open) _stackTraceView.Draw(ticket.AccStackTrace);
            }
        }

        // ロード中の handle は None を返す（失敗した Entry は Registry から取り除かれる）ため、None を「ロード中」として表示する
        private void DrawStatusLabel(AsyncOperationStatus status)
        {
            switch (status)
            {
                case AsyncOperationStatus.Succeeded:
                    GUILayout.Label(EditorText.Get("runtimeState.status.loaded"), _loadedStyle, STATUS_WIDTH);
                    break;
                case AsyncOperationStatus.Failed:
                    GUILayout.Label(EditorText.Get("runtimeState.status.failed"), _failedStyle, STATUS_WIDTH);
                    break;
                default:
                    GUILayout.Label(EditorText.Get("runtimeState.status.loading"), _loadingStyle, STATUS_WIDTH);
                    break;
            }
        }

        private void EnsureStyles()
        {
            if (_loadingStyle != null) return;

            _loadingStyle = CreateColoredStyle(EditorStyles.miniLabel, LOADING_COLOR);
            _loadedStyle = CreateColoredStyle(EditorStyles.miniLabel, LOADED_COLOR);
            _failedStyle = CreateColoredStyle(EditorStyles.miniLabel, FAILED_COLOR);
            _groupStyle = new GUIStyle(EditorStyles.miniButton) { alignment = TextAnchor.MiddleCenter };
            _kindStyles = new GUIStyle[KIND_COUNT];
            for (var i = 0; i < KIND_COUNT; i++)
                _kindStyles[i] = CreateColoredStyle(EditorStyles.label, i < KIND_COLORS.Length ? KIND_COLORS[i] : FALLBACK_KIND_COLOR);
        }

        // "All" と LifetimeKind の名前を、値の順に並べる。添字を LifetimeKind の値として使うため、0 から連番であることを確認する
        private static string[] CreateFilterNames()
        {
            var kinds = (LifetimeKind[])Enum.GetValues(typeof(LifetimeKind));
            var names = new string[kinds.Length + 1];
            names[FILTER_ALL] = "All";
            for (var i = 0; i < kinds.Length; i++)
            {
                Debug.Assert((int)kinds[i] == i, $"{nameof(LifetimeKind)} の値が 0 から連番ではありません。{nameof(RuntimeStateTab)} の添字の前提が崩れます。");
                names[i + 1] = kinds[i].ToString();
            }
            return names;
        }

        private static GUIStyle CreateColoredStyle(GUIStyle source, Color color)
        {
            var style = new GUIStyle(source);
            style.normal.textColor = color;
            return style;
        }

        private void BuildEntryLabels()
        {
            _entryRefCountLabels.Clear();
            _entryKindMasks.Clear();
            Array.Clear(_entryCountByFilter, 0, FILTER_COUNT);
            _entryCountByFilter[FILTER_ALL] = _entries.Count;

            for (var i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (i >= _entryContents.Count) _entryContents.Add(new GUIContent());
                var content = _entryContents[i];
                // 名前はロード完了後にだけ分かる。分かるときは先頭に出し、ロード中はキーだけを出す
                content.text = entry.AccAssetName.Length > 0
                    ? $"{entry.AccAssetName}  ｜  {entry.AccKey}  <{entry.AccAssetType.Name}>"
                    : $"{entry.AccKey}  <{entry.AccAssetType.Name}>";
                content.tooltip = content.text; // 長いキーが切れたときに、マウスオーバーで全体を見られるようにする
                content.image = AssetPreview.GetMiniTypeThumbnail(entry.AccAssetType);
                _entryRefCountLabels.Add($"Ref {entry.AccRefCount}");

                var mask = 0;
                var end = entry.AccScopeCountStart + entry.AccScopeCountLength;
                for (var scope_index = entry.AccScopeCountStart; scope_index < end; scope_index++)
                    mask |= ToKindMask((int)_scopeCounts[scope_index].AccScopeKind);
                _entryKindMasks.Add(mask);

                for (var kind = 0; kind < KIND_COUNT; kind++)
                {
                    if ((mask & ToKindMask(kind)) != 0) _entryCountByFilter[kind + 1]++;
                }
            }
        }

        private void BuildScopeLabels()
        {
            _scopeLabels.Clear();
            foreach (var scope in _scopeCounts)
                _scopeLabels.Add($"■ {ResolveScopeName(scope.AccScopeKind, scope.AccScopeValue)}  ×{scope.AccCount}");
        }

        private void BuildTicketLabels()
        {
            _ticketLabels.Clear();
            Array.Clear(_ticketCountByFilter, 0, FILTER_COUNT);
            _ticketCountByFilter[FILTER_ALL] = _tickets.Count;
            foreach (var ticket in _tickets)
            {
                _ticketLabels.Add(EditorText.Format("runtimeState.ticket", ticket.AccSerial));
                _ticketCountByFilter[(int)ticket.AccScopeKind + 1]++;
            }
        }

        private void BuildFilterLabels()
        {
            for (var i = 0; i < FILTER_COUNT; i++)
            {
                _filterLabels[i] = $"{FILTER_NAMES[i]} ({_entryCountByFilter[i]})";
                _summaryLabels[i] = EditorText.Format("runtimeState.summary", _entryCountByFilter[i], _ticketCountByFilter[i]);
            }
        }

        private static int ToKindMask(int kind) => 1 << kind;

        // スコープの値（Owner の InstanceID・Scene の handle）を、人が読める名前にする。解決できないものは「破棄済み」
        private static string ResolveScopeName(LifetimeKind kind, int value)
        {
            switch (kind)
            {
                case LifetimeKind.Owner:
                    // Runtime 側は InstanceID（int）で持つ。Unity 6000.3 で InstanceIDToObject が非推奨になったため、EntityId に変換して引く
                    EntityId owner_id = value;
                    var owner = EditorUtility.EntityIdToObject(owner_id);
                    return EditorText.Format("common.scope.owner", owner != null ? owner.name : EditorText.Get("runtimeState.destroyed"), value);
                case LifetimeKind.Scene:
                    for (var i = 0; i < SceneManager.sceneCount; i++)
                    {
                        var scene = SceneManager.GetSceneAt(i);
                        if (scene.handle == value) return EditorText.Format("common.scope.scene", scene.name);
                    }
                    return EditorText.Format("common.scope.sceneDestroyed", EditorText.Get("runtimeState.destroyed"), value);
                default:
                    return kind.ToString();
            }
        }

        // 種別 → 値 → 型の完全名の順。enum の CompareTo は箱化するため、int にして比較する。
        // 型名だけだと、namespace が違う同名の型で比較結果が 0 になり、Sort（安定ではない）で行が入れ替わるため、FullName を使う
        internal static int CompareEntries(AssetEntrySnapshot a, AssetEntrySnapshot b)
        {
            var by_kind = ((int)a.AccKey.AccKind).CompareTo((int)b.AccKey.AccKind);
            if (by_kind != 0) return by_kind;

            var by_value = string.CompareOrdinal(a.AccKey.AccValue, b.AccKey.AccValue);
            if (by_value != 0) return by_value;

            return string.CompareOrdinal(a.AccAssetType.FullName, b.AccAssetType.FullName);
        }
    }
}
