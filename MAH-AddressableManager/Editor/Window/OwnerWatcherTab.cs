using System;
using System.Collections.Generic;
using MAH.AddressableManager.Core;
using MAH.AddressableManager.Core.Loading;
using MAH.AddressableManager.Editor.Localization;
using UnityEditor;
using UnityEngine;

namespace MAH.AddressableManager.Editor.Window
{
    /// <summary>
    /// 「Owner」タブ。Owner 寿命で監視中の GameObject と、Watcher の貼り付け・登録の状態を一覧し、
    /// Owner の回収が OnDestroy 経由か、ポーリング経由かを区別した記録を見られる。
    /// ポーリング経由の回収が多い場合は、Watcher が取りこぼしていること（破棄検知の多重の安全網が期待通り働いていないこと）を示す。
    /// 確保中のチケット数は、Model には持たせず、発行済みチケットの Snapshot から InstanceID ごとに数える。
    /// 表示用の文字列は、描画のたびに作らないよう、取得時（Refresh）にまとめて作る。読み取りは IAddressableDebugReader 越しに行い、窓口は保持しない。
    /// </summary>
    internal sealed class OwnerWatcherTab : IDebugWindowTab
    {
        private const string TITLE = "Owner";
        private const string ROUTE_ON_DESTROY_LABEL = "[OnDestroy]";
        private const string STATUS_OK_LABEL = "● Watcher ✓";
        private const string TIME_FORMAT = "HH:mm:ss";

        // 状態の種別。添字として _statusStyles に使う
        private const int STATUS_OK = 0;
        private const int STATUS_ERROR = 1;
        private const int STATUS_WARNING = 2;
        private const int STATUS_COUNT = 3;

        // 色は Dark / Light どちらのスキンでも読める中間の明るさにする。「実行時状態」タブのロード状態の色（緑・黄・赤）に合わせる
        private static readonly Color OK_COLOR = new(0.25f, 0.70f, 0.30f);
        private static readonly Color ERROR_COLOR = new(0.90f, 0.30f, 0.30f);
        private static readonly Color WARNING_COLOR = new(0.90f, 0.65f, 0.05f);
        private static readonly Color NEUTRAL_COLOR = new(0.60f, 0.60f, 0.60f);

        // GUILayout.Width は呼ぶたびに確保するため、使い回す
        private static readonly GUILayoutOption[] TICKET_WIDTH = { GUILayout.Width(80f) };
        private static readonly GUILayoutOption[] STATUS_WIDTH = { GUILayout.Width(140f) };
        private static readonly GUILayoutOption[] ROUTE_WIDTH = { GUILayout.Width(84f) };
        private static readonly GUILayoutOption[] COUNT_WIDTH = { GUILayout.Width(56f) };
        private static readonly GUILayoutOption[] TIME_WIDTH = { GUILayout.Width(64f) };
        private static readonly Comparison<OwnerWatchSnapshot> OWNER_COMPARISON = CompareOwners;
        private static readonly Comparison<OwnerReclaimSnapshot> RECLAIM_COMPARISON = CompareReclaims;

        private readonly List<OwnerWatchSnapshot> _owners = new();
        private readonly List<IssuedTicketSnapshot> _tickets = new();
        private readonly List<OwnerReclaimSnapshot> _reclaims = new();
        // InstanceID ごとの、確保中のチケット数。Refresh のたびに数え直す（Dictionary は使い回す）
        private readonly Dictionary<int, int> _ticketCountByOwner = new();
        // 表示用。_owners / _reclaims と同じ添字で対応する（Refresh のたびに作り直す）
        private readonly List<GUIContent> _ownerTitles = new(); // GUIContent は使い回す（縮めない）
        private readonly List<string> _ownerTicketLabels = new();
        private readonly List<string> _ownerStatusLabels = new();
        private readonly List<int> _ownerStatuses = new();
        private readonly List<GUIContent> _reclaimTitles = new();
        private readonly List<string> _reclaimRouteLabels = new();
        private readonly List<string> _reclaimCountLabels = new();
        private readonly List<string> _reclaimTicketLabels = new();
        private readonly List<string> _reclaimTimeLabels = new();
        private readonly List<bool> _reclaimIsPolling = new();
        private string _ownersSummaryLabel;
        private string _reclaimsSummaryLabel;
        private Vector2 _scroll;
        // スタイルは EditorStyles が OnGUI の中でしか使えないため、最初の描画で作る
        private GUIStyle[] _statusStyles;
        private GUIStyle _pollingStyle;
        private GUIStyle _onDestroyStyle;
        private GUIStyle _countStyle;

        public string AccTitle => TITLE;

        internal OwnerWatcherTab()
        {
            BuildSummaries(0, 0, 0);
        }

        public void Refresh(IAddressableDebugReader reader)
        {
            reader.CopyWatchedOwnersTo(_owners);
            reader.CopyIssuedTicketsTo(_tickets);
            reader.CopyOwnerReclaimsTo(_reclaims);
            _owners.Sort(OWNER_COMPARISON); // Dictionary の順序は変わり得るため、行が入れ替わらないよう並べる
            _reclaims.Sort(RECLAIM_COMPARISON);
            CountTicketsByOwner();
            BuildOwnerRows();
            BuildReclaimRows();
        }

        // Refresh のたびに全ての表示用の文字列を作り直すため、言語の変更に追従するのに何もしなくてよい
        public void InvalidateLabels()
        {
        }

        public void Clear()
        {
            _owners.Clear();
            _tickets.Clear();
            _reclaims.Clear();
            _ticketCountByOwner.Clear();
            _ownerTicketLabels.Clear();
            _ownerStatusLabels.Clear();
            _ownerStatuses.Clear();
            _reclaimRouteLabels.Clear();
            _reclaimCountLabels.Clear();
            _reclaimTicketLabels.Clear();
            _reclaimTimeLabels.Clear();
            _reclaimIsPolling.Clear();
            _scroll = Vector2.zero;
            BuildSummaries(0, 0, 0);
            // _ownerTitles・_reclaimTitles の GUIContent は、次の Refresh で使い回すため残す
        }

        public void Draw()
        {
            EnsureStyles();

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(_ownersSummaryLabel, EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawOwners();
            EditorGUILayout.Space();
            GUILayout.Label(_reclaimsSummaryLabel, EditorStyles.boldLabel);
            DrawReclaims();
            EditorGUILayout.EndScrollView();
        }

        private void DrawOwners()
        {
            if (_owners.Count == 0)
            {
                EditorGUILayout.HelpBox(EditorText.Get("owner.empty.owners"), MessageType.None);
                return;
            }

            for (var i = 0; i < _owners.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(_ownerTitles[i], EditorStyles.label);
                GUILayout.Label(_ownerTicketLabels[i], _countStyle, TICKET_WIDTH);
                GUILayout.Label(_ownerStatusLabels[i], _statusStyles[_ownerStatuses[i]], STATUS_WIDTH);
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawReclaims()
        {
            if (_reclaims.Count == 0)
            {
                EditorGUILayout.HelpBox(EditorText.Get("owner.empty.reclaims"), MessageType.None);
                return;
            }

            for (var i = 0; i < _reclaims.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(_reclaimRouteLabels[i], _reclaimIsPolling[i] ? _pollingStyle : _onDestroyStyle, ROUTE_WIDTH);
                GUILayout.Label(_reclaimTitles[i], EditorStyles.label);
                GUILayout.Label(_reclaimCountLabels[i], _countStyle, COUNT_WIDTH);
                GUILayout.Label(_reclaimTicketLabels[i], _countStyle, TICKET_WIDTH);
                GUILayout.Label(_reclaimTimeLabels[i], EditorStyles.miniLabel, TIME_WIDTH);
                EditorGUILayout.EndHorizontal();
            }
        }

        // 発行済みチケットのうち Owner 寿命のものを、InstanceID ごとに数える
        private void CountTicketsByOwner()
        {
            _ticketCountByOwner.Clear();
            foreach (var ticket in _tickets)
            {
                if (ticket.AccScopeKind != LifetimeKind.Owner) continue;
                _ticketCountByOwner.TryGetValue(ticket.AccScopeValue, out var count);
                _ticketCountByOwner[ticket.AccScopeValue] = count + 1;
            }
        }

        private void BuildOwnerRows()
        {
            _ownerTicketLabels.Clear();
            _ownerStatusLabels.Clear();
            _ownerStatuses.Clear();

            for (var i = 0; i < _owners.Count; i++)
            {
                var owner = _owners[i];

                if (i >= _ownerTitles.Count) _ownerTitles.Add(new GUIContent());
                var title = _ownerTitles[i];
                title.text = EditorText.Format("owner.title", GetDisplayName(owner.AccOwnerName), owner.AccInstanceId);
                title.tooltip = title.text; // 長い名前が切れたときに、マウスオーバーで全体を見られるようにする

                _ownerTicketLabels.Add(EditorText.Format("owner.tickets", _ticketCountByOwner.TryGetValue(owner.AccInstanceId, out var count) ? count : 0));

                if (!owner.AccIsAlive)
                {
                    _ownerStatuses.Add(STATUS_WARNING);
                    _ownerStatusLabels.Add(EditorText.Get("owner.status.destroyed"));
                }
                else if (!owner.AccHasWatcher)
                {
                    _ownerStatuses.Add(STATUS_ERROR);
                    _ownerStatusLabels.Add(EditorText.Get("owner.status.noWatcher"));
                }
                else if (!owner.AccIsRegistered)
                {
                    _ownerStatuses.Add(STATUS_ERROR);
                    _ownerStatusLabels.Add(EditorText.Get("owner.status.notRegistered"));
                }
                else
                {
                    _ownerStatuses.Add(STATUS_OK);
                    _ownerStatusLabels.Add(STATUS_OK_LABEL);
                }
            }

        }

        private void BuildReclaimRows()
        {
            _reclaimRouteLabels.Clear();
            _reclaimCountLabels.Clear();
            _reclaimTicketLabels.Clear();
            _reclaimTimeLabels.Clear();
            _reclaimIsPolling.Clear();

            var on_destroy_count = 0;
            var polling_count = 0;
            for (var i = 0; i < _reclaims.Count; i++)
            {
                var reclaim = _reclaims[i];
                var is_polling = reclaim.AccRoute == OwnerReclaimRoute.Polling;
                if (is_polling) polling_count += reclaim.AccCount;
                else on_destroy_count += reclaim.AccCount;

                if (i >= _reclaimTitles.Count) _reclaimTitles.Add(new GUIContent());
                var title = _reclaimTitles[i];
                title.text = GetDisplayName(reclaim.AccOwnerName);
                title.tooltip = EditorText.Format("owner.reclaim.tooltip", title.text, reclaim.AccLastInstanceId);

                _reclaimIsPolling.Add(is_polling);
                _reclaimRouteLabels.Add(is_polling ? EditorText.Get("owner.route.polling") : ROUTE_ON_DESTROY_LABEL);
                _reclaimCountLabels.Add($"×{reclaim.AccCount}");
                _reclaimTicketLabels.Add(EditorText.Format("owner.tickets", reclaim.AccTicketCount));
                _reclaimTimeLabels.Add(reclaim.AccLastTime.ToString(TIME_FORMAT));
            }

            BuildSummaries(_owners.Count, on_destroy_count, polling_count);
        }

        // 監視中の件数と、回収の合計の見出し。ポーリングの割合が高いほど、Watcher の取りこぼしが多いことを示す
        private void BuildSummaries(int owner_count, int on_destroy_count, int polling_count)
        {
            _ownersSummaryLabel = EditorText.Format("owner.summary.owners", owner_count);
            var total = on_destroy_count + polling_count;
            _reclaimsSummaryLabel = total == 0
                ? EditorText.Get("owner.summary.reclaimsNone")
                : EditorText.Format("owner.summary.reclaims", on_destroy_count, polling_count, polling_count * 100f / total);
        }

        private static string GetDisplayName(string owner_name) => string.IsNullOrEmpty(owner_name) ? EditorText.Get("common.noName") : owner_name;

        // 名前 → InstanceID の順。string.CompareOrdinal は文化に依存せず、確保もしない
        private static int CompareOwners(OwnerWatchSnapshot left, OwnerWatchSnapshot right)
        {
            var by_name = string.CompareOrdinal(left.AccOwnerName, right.AccOwnerName);
            return by_name != 0 ? by_name : left.AccInstanceId.CompareTo(right.AccInstanceId);
        }

        // ポーリングの行を先に、その中で最終発生の新しい順。int・long の CompareTo は箱化しない
        private static int CompareReclaims(OwnerReclaimSnapshot left, OwnerReclaimSnapshot right)
        {
            var by_route = ((int)right.AccRoute).CompareTo((int)left.AccRoute);
            return by_route != 0 ? by_route : right.AccSequence.CompareTo(left.AccSequence);
        }

        private void EnsureStyles()
        {
            if (_statusStyles != null) return;

            _statusStyles = new GUIStyle[STATUS_COUNT];
            _statusStyles[STATUS_OK] = CreateColoredStyle(EditorStyles.label, OK_COLOR);
            _statusStyles[STATUS_ERROR] = CreateColoredStyle(EditorStyles.label, ERROR_COLOR);
            _statusStyles[STATUS_WARNING] = CreateColoredStyle(EditorStyles.label, WARNING_COLOR);
            _pollingStyle = CreateColoredStyle(EditorStyles.boldLabel, WARNING_COLOR);
            _onDestroyStyle = CreateColoredStyle(EditorStyles.label, NEUTRAL_COLOR);
            _countStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleRight };
        }

        private static GUIStyle CreateColoredStyle(GUIStyle source, Color color)
        {
            var style = new GUIStyle(source);
            style.normal.textColor = color;
            return style;
        }
    }
}
