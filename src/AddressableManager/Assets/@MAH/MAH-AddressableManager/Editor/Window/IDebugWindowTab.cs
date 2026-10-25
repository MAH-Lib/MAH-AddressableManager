using MAH.AddressableManager.Core;

namespace MAH.AddressableManager.Editor.Window
{
    /// <summary>
    /// デバッグウィンドウ（AddressableDebugWindow）のタブ1つ分の契約。
    /// タブが持つのは「自分の Snapshot の List」と「描画」だけで、状態への到達手段・更新間隔・未起動時の表示は Window（枠）が持つ。
    /// 読み取りは IAddressableDebugReader 越しに行い、Model には触れない。
    /// </summary>
    internal interface IDebugWindowTab
    {
        /// <summary> ツールバーに表示するタブ名 </summary>
        string AccTitle { get; }

        /// <summary> 窓口から状態を取り直す（Window が更新間隔ごとに、表示中のタブにだけ呼ぶ）。List は Clear して使い回す </summary>
        void Refresh(IAddressableDebugReader reader);

        /// <summary> 保持している Snapshot と表示の状態を空にする（未起動になったときに Window が呼ぶ） </summary>
        void Clear();

        /// <summary>
        /// 表示言語が変わったときに Window が呼ぶ。作り直しの省略（変化が無いときの Refresh の早期 return）を無効にして、
        /// 次の Refresh で表示用の文字列を新しい言語で作り直させる。Snapshot・開閉・クリアの基準は消さない
        /// </summary>
        void InvalidateLabels();

        /// <summary> タブの中身を描画する（OnGUI の中から呼ばれる）。Registry を書き換える操作は置かない </summary>
        void Draw();
    }
}
