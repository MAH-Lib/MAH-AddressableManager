using UnityEditor;
using UnityEngine;
using MAH.AddressableManager.Core;
using MAH.AddressableManager.Editor.Localization;
using MAH.AddressableManager.Editor.SettingsSync;

namespace MAH.AddressableManager.Editor.Window
{
    /// <summary>
    /// AddressableManager の実行中の状態を、読み取り専用で見るためのデバッグウィンドウ。
    /// ツールバーでタブを切り替える1つのウィンドウで、各タブは IDebugWindowTab として足す。
    /// 状態への到達（AddressableManager.AccInstance.AccDebugReader）・一定間隔の取得（pull）・未起動時の表示は、ここ（枠）に集約し、タブごとに書かない。
    /// 窓口への参照は保持せず、取得のたびに AccInstance から取り直す（Manager の破棄後に古い参照を触らないため）。
    /// Window を閉じていれば、Runtime 側の処理は何も増えない（Runtime 側にイベントを足していない）。
    /// </summary>
    internal sealed class AddressableDebugWindow : EditorWindow
    {
        private const string MENU_PATH = "MAH/Addressable/Debug Window";
        private const string WINDOW_TITLE = "Addressable Debug";

        private IDebugWindowTab[] _tabs;
        private string[] _tabTitles;
        private int _selectedTab;
        private bool _isRunning; // 直近の更新で、窓口を取得できていたか
        private int _managerId; // 直近の更新で読んだ Manager の InstanceID（0 は未取得）。入れ替わりの検知にだけ使い、Manager への参照は持たない
        private bool _needsRefresh; // 次の更新間隔を待たずに取り直す（起動直後・タブの切り替え直後）
        private double _lastRefreshTime;
        private AddressableEditorSettingsSO _settingsSo; // 無いときは null。取り直しのたびに探し直し、見つかるまでは既定値の設定を使う
        private readonly AddressableDebugWindowSettings _defaultSettings = new();

        [MenuItem(MENU_PATH)]
        private static void Open() => GetWindow<AddressableDebugWindow>(WINDOW_TITLE);

        private void OnEnable()
        {
            titleContent = new GUIContent(WINDOW_TITLE);

            // タブはここに足す（機能3以降は、この配列に IDebugWindowTab を追加する）
            _tabs = new IDebugWindowTab[]
            {
                new RuntimeStateTab(),
                new LeakListTab(),
                new OwnerWatcherTab(),
            };
            _tabTitles = new string[_tabs.Length];
            RebuildTabTitles();

            _settingsSo = AddressableEditorSettingsLocator.FindSettings();
            _needsRefresh = true;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorText.LanguageChanged += OnLanguageChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorText.LanguageChanged -= OnLanguageChanged;
            ClearTabs();
        }

        // タブの見出しと、取得時にまとめて作ったタブの文言を、新しい言語で作り直す
        private void OnLanguageChanged()
        {
            RebuildTabTitles();
            // 表示中でないタブも、切り替えたときの Refresh で新しい言語に作り直されるようにする
            for (var i = 0; i < _tabs.Length; i++)
                _tabs[i].InvalidateLabels();
            _needsRefresh = true;
            Repaint();
        }

        private void RebuildTabTitles()
        {
            for (var i = 0; i < _tabs.Length; i++)
                _tabTitles[i] = _tabs[i].AccTitle;
        }

        // Play の終了で Manager が破棄されるため、窓口が無効になる前に、保持している Snapshot を空にする
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
                SetNotRunning();
        }

        // OnInspectorUpdate は約 10Hz で呼ばれる。ここで、更新間隔を超えたときだけ取り直して再描画する（pull）
        private void OnInspectorUpdate()
        {
            var manager = TryGetManager();
            var reader = manager != null ? manager.AccDebugReader : null;
            if (reader == null)
            {
                SetNotRunning();
                return;
            }

            // 同じ Play 中に Manager が作り直されると、タブが持つ Model ごとの基準（クリアの通し番号・行の ID）が、新しい Model の値とずれる。
            // 未起動を観測できないまま入れ替わった場合でも、タブを空にして、新しい Manager として取り直す
            var manager_id = manager.GetInstanceID();
            if (_managerId != 0 && _managerId != manager_id)
            {
                ClearTabs();
                _needsRefresh = true;
            }
            _managerId = manager_id;

            var now = EditorApplication.timeSinceStartup;
            if (_isRunning && !_needsRefresh && now - _lastRefreshTime < GetRefreshIntervalSec())
                return;

            // SO は Window を開いた後に自動作成されることがある。見つかるまでは、取り直しのたびに探し直す
            // （更新間隔ごとにしか呼ばれないため、OnInspectorUpdate の頻度で FindAssets が走ることはない）
            if (_settingsSo == null)
                _settingsSo = AddressableEditorSettingsLocator.FindSettings();

            _isRunning = true;
            _needsRefresh = false;
            _lastRefreshTime = now;
            _tabs[_selectedTab].Refresh(reader); // 表示中のタブだけ取り直す
            Repaint();
        }

        private void OnGUI()
        {
            var selected = GUILayout.Toolbar(_selectedTab, _tabTitles);
            if (selected != _selectedTab)
            {
                _selectedTab = selected;
                _needsRefresh = true; // 切り替えたタブは、次の更新で取り直す
            }

            if (!_isRunning)
            {
                EditorGUILayout.HelpBox(EditorText.Get("debug.notRunning"), MessageType.Info);
                return;
            }

            _tabs[_selectedTab].Draw();
        }

        // Play 中で Manager が生きているときだけ Manager を返す。保持はしない
        private static AddressableManager TryGetManager()
        {
            if (!EditorApplication.isPlaying) return null;

            var manager = AddressableManager.AccInstance;
            return manager != null ? manager : null;
        }

        // SO が編集されても追従するよう、毎回 SO から読む。SO が無いときは既定値
        private float GetRefreshIntervalSec()
            => (_settingsSo != null ? _settingsSo.AccDebugWindow : _defaultSettings).AccRefreshIntervalSec;

        private void SetNotRunning()
        {
            _managerId = 0;
            if (!_isRunning) return;

            _isRunning = false;
            _needsRefresh = true;
            ClearTabs();
            Repaint();
        }

        private void ClearTabs()
        {
            if (_tabs == null) return;

            foreach (var tab in _tabs)
                tab.Clear();
        }
    }
}
