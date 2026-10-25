using UnityEngine;

namespace MAH.AddressableManager.Editor.SettingsSync
{
    /// <summary>
    /// Editor 専用の設定をまとめた SO。Editor アセンブリにあるため、ビルドに含まれない（Runtime 側から参照しない）。
    /// 機能ごとの設定は [Serializable] の Addressable&lt;機能&gt;Settings として持つ（AddressableRuntimeSettingsSO と同じ形）。
    /// 配置場所はプロジェクトごとに変わり得るため、AddressableEditorSettingsLocator が型で探す。
    /// 1つだけ作ること（複数あると警告が出て、1件目が使われる）。
    /// </summary>
    [CreateAssetMenu(menuName = "MAH/Addressable/EditorSettings", fileName = "AddressableEditorSettings")]
    internal sealed class AddressableEditorSettingsSO : ScriptableObject
    {
        [SerializeField] private AddressableDebugWindowSettings _debugWindow = new();

        [SerializeField] private AddressableLeakLogSettings _leakLog = new();

        [SerializeField] private AddressableRegistrationSettings _registration = new();

        [SerializeField] private AddressableDependencyCheckSettings _dependencyCheck = new();

        /// <summary> デバッグウィンドウ共通の設定 </summary>
        internal AddressableDebugWindowSettings AccDebugWindow => _debugWindow;
        /// <summary> 「リーク」タブのログ出力の設定 </summary>
        internal AddressableLeakLogSettings AccLeakLog => _leakLog;
        /// <summary> Dependency Check ウィンドウのテキスト出力の設定 </summary>
        internal AddressableDependencyCheckSettings AccDependencyCheck => _dependencyCheck;

        /// <summary> アセットの登録（自動登録・手動一括登録）の設定 </summary>
        internal AddressableRegistrationSettings AccRegistration => _registration;

        private void OnValidate()
        {
            _debugWindow ??= new AddressableDebugWindowSettings();
            _debugWindow.ClampValues();
            _leakLog ??= new AddressableLeakLogSettings();
            _leakLog.ClampValues();
            _dependencyCheck ??= new AddressableDependencyCheckSettings();
            _dependencyCheck.ClampValues();
            _registration ??= new AddressableRegistrationSettings();
            _registration.ClampValues();
        }
    }
}
