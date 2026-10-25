using System.Runtime.CompilerServices;

// internal な Controller/Model/Service をテストから直接検証できるようにする。
// 公開する相手はテスト用アセンブリと、動作確認用サンプルのみに限定する。
[assembly: InternalsVisibleTo("MAH.AddressableManager.Editor.Tests")]
[assembly: InternalsVisibleTo("MAH.AddressableManager.Runtime.Tests")]
// 2026-09-29 サンプルのログ出力を AddressableLogger に揃えるために追加（AddressableLogger のみ参照する）
[assembly: InternalsVisibleTo("MAH.AddressableManager.AddressableLoad")]
// 2026-10-05 初期化サンプルのログ出力を AddressableLogger に揃えるために追加（AddressableLogger のみ参照する）
[assembly: InternalsVisibleTo("MAH.AddressableManager.AddressableInitialize")]
// 2026-10-08 ダウンロードサンプルのログ出力を AddressableLogger に揃えるために追加（AddressableLogger のみ参照する）
[assembly: InternalsVisibleTo("MAH.AddressableManager.AddressableDownload")]
// 2026-10-14 デバッグウィンドウ（Editor）が窓口を使うために追加。
// Editor 側のコードが使ってよいのは IAddressableDebugReader と各 Snapshot のみ（Model・Controller は使わない。構造では防げないためレビュー観点にする）
[assembly: InternalsVisibleTo("MAH.AddressableManager.Editor")]
