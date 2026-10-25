using System.Collections.Generic;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary>
    /// 手動一括登録の一覧の1行（アセット1つ分）。状態・展開した結果・チェックの有無を持つ。
    /// チェックだけは画面で切り替えるため、書き換えられる。Unity の型に依存しない。
    /// </summary>
    internal sealed class RegistrationManualRow
    {
        private readonly string _assetPath;
        private readonly ManualRegistrationState _state;
        private readonly string _currentGroup;
        private readonly string _autoRuleName;
        private readonly string _expandedAddress;
        private readonly string _error;
        private readonly RegistrationChange _change;
        private readonly List<string> _warnings;
        private readonly bool _canCheck;

        /// <summary> アセットパス </summary>
        internal string AccAssetPath => _assetPath;
        /// <summary> 状態（5.3） </summary>
        internal ManualRegistrationState AccState => _state;
        /// <summary> 今のグループ名（未登録なら空） </summary>
        internal string AccCurrentGroup => _currentGroup;
        /// <summary> 自動登録のルールの SO の名前（自動登録の状態のとき。それ以外は空） </summary>
        internal string AccAutoRuleName => _autoRuleName;
        /// <summary> 画面の指定で展開したアドレス（表示用） </summary>
        internal string AccExpandedAddress => _expandedAddress;
        /// <summary> 展開のエラー（グループ名に使えない文字など）。空ならエラーなし </summary>
        internal string AccError => _error;
        /// <summary> 登録したときの変更。差分がなければ null </summary>
        internal RegistrationChange AccChange => _change;
        /// <summary> 展開の警告（知らないマクロなど） </summary>
        internal IReadOnlyList<string> AccWarnings => _warnings;

        /// <summary> チェックを入れられるか（未登録・登録済みで、エラーがないときだけ） </summary>
        internal bool AccCanCheck => _canCheck;

        /// <summary> チェックの有無（画面で切り替える） </summary>
        internal bool AccChecked { get; set; }

        internal RegistrationManualRow(
            string asset_path,
            ManualRegistrationState state,
            string current_group,
            string auto_rule_name,
            string expanded_address,
            string error,
            RegistrationChange change,
            List<string> warnings)
        {
            _assetPath = asset_path;
            _state = state;
            _currentGroup = current_group ?? string.Empty;
            _autoRuleName = auto_rule_name ?? string.Empty;
            _expandedAddress = expanded_address ?? string.Empty;
            _error = error ?? string.Empty;
            _change = change;
            _warnings = warnings ?? new List<string>();
            // アクセサの中で計算しないよう、作るときに求めておく
            _canCheck = _error.Length == 0
                        && (_state == ManualRegistrationState.Unregistered || _state == ManualRegistrationState.Registered);
            // 既定は、未登録だけチェックがオン（登録済みはオフ。5.3）
            AccChecked = state == ManualRegistrationState.Unregistered && _error.Length == 0;
        }
    }
}
