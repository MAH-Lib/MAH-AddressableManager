# 要求内容：AddressableManager Editor 機能1「実行時の読み込み/解放状態の確認」

> 作業ディレクトリ：`.steering/20261014-editor-runtime-state/` ／ 作成日：2026-10-14 ／ ステータス：承認済み ／ 対象 Unity バージョン：6000.3.25f1
> 関連する永続的ドキュメント：`src/AddressableManager/Docs/specs/editor-debug-window-spec.md`（共通基盤の正。2.4・4章・6.1 を実コードに合わせて更新し、8章に機能1の節を追記）／`src/AddressableManager/Docs/addressable-manager-design.md`（3.9(2) の表示項目と整合を確認。影響が出る場合のみ更新）／`src/AddressableManager/Docs/conventions.md`（10章に Snapshot の実名・`<対象>Tab` の命名を追記）／`src/AddressableManager/Docs/diagrams/editor-foundation-class.md`（実コードのメンバー名に更新）／`src/AddressableManager/Docs/specs/loading-spec.md`（10章の「EditorWindow での一覧表示は Editor 拡張の実装時に行う」の参照先を更新。リーク一覧は機能2のまま）／`Docs/glossary.md`（影響なし。実装時に確認）

## 1. 変更・追加する機能の説明

Play 中の `AddressableManager` の Registry の内容を、EditorWindow `AddressableDebugWindow`（メニュー `MAH/Addressable/Debug Window`）で読み取り専用に一覧する。あわせて、`.steering/20261013-editor-foundation/` で設計のみ決めた共通基盤（窓口 `IAddressableDebugReader`・`<対象>Snapshot`・Editor SO・pull 更新・タブの枠）を、この機能でコードにする。

一覧する項目（設計仕様書 3.9(2)）：

- `AssetKey`（種別＋値）・アセットの型・読み込んだアセットの名前（ロード完了後。2026-10-14 追加）・`RefCount`
- 寿命スコープごとの確保内訳（`CountByScope`）
- ロードステータス（`AsyncOperationStatus`）
- 確保元のスタックトレース（発行済みチケット単位）

今やる理由：Editor 拡張の6項目は、この機能のウィンドウの枠と窓口の上に、タブを足す形で作る。先に枠と窓口を実コードで確定しないと、後続の機能が到達手段や更新方式を別々に作ってしまう。また「誰が解放していないのか」を、ログを追わずに特定できるようにしたい。

## 2. ユーザーストーリー

- 実装担当者として、Release の呼び忘れを調べるために、アセットごとに寿命スコープ別の確保数を見たい
- 実装担当者として、確保した箇所を特定するために、発行済みチケットごとの確保元のスタックトレースを見たい
- 実装担当者として、Owner 寿命の GameObject や Scene 寿命のシーンが何を指しているか分かるように、スコープの名前を見たい
- 実装担当者として、Window を開いていないときに実行時の負荷を増やしたくないので、状態の取得は Window 側からだけ行ってほしい
- レビュアーとして、Window から Registry を壊さないことを確認するために、Window が読み取り専用の窓口と `Snapshot` しか触れない構造にしたい
- 後続の機能の実装担当者として、タブを足すだけで済むように、窓口・更新間隔・「未起動」表示が枠に集約されていてほしい

## 3. 受け入れ条件

### Window の動作
- [ ] メニュー `MAH/Addressable/Debug Window` で `AddressableDebugWindow` が開き、タブのツールバーに「実行時状態」タブが表示される
- [ ] Play していない間（`AddressableManager.AccInstance == null`）は「未起動」と表示され、一覧は空になる
- [ ] Play 中、更新間隔（既定 0.5 秒）ごとに一覧が更新される。Window を閉じている間、Runtime 側に追加の処理はない
- [ ] Play を停止すると、一覧が空になって「未起動」表示に戻り、エラーログが出ない
- [ ] 一覧には、エントリごとに `AssetKey`（種別＋値）・型・アセットの名前（ロード完了後。完了前は空）・`RefCount`・ロードステータスが表示される。寿命の種類（All / Manual / Scene / Owner）で絞り込め、件数が絞り込みごとに切り替わる（2026-10-14 追加）
- [ ] エントリを開くと、寿命スコープごとの確保数（Manual・Scene・Owner）が表示される。Owner は GameObject 名、Scene はシーン名で表示し、解決できないものは「破棄済み」等の表示にする
- [ ] 発行済みチケットが、スコープごとに表示され、受け渡し済みか・Label 一括ロード由来か・確保元のスタックトレースが見られる
- [ ] Release・Owner の破棄・シーンのアンロードで、該当エントリ・スコープが次の更新で一覧から消える／`RefCount` が減る
- [ ] ウィンドウ上に、Release・強制回収など Registry を書き換える操作が存在しない

### 構造
- [ ] Window（`Editor/`）は、`AddressableManager.AccInstance`・`IAddressableDebugReader`・各 `Snapshot` 以外の internal 型（Model・Controller）を使っていない
- [ ] 窓口・`Snapshot`・Model のコピー用メソッド・`AccDebugReader` が `#if UNITY_EDITOR` で囲まれ、本番ビルドに含まれない
- [ ] コピー処理が、`List` の再利用（`Clear()` して書き出し）で行われ、`ToArray()` / `ToList()` を使っていない
- [ ] 取得済みの `Snapshot` は、取得後に Model が変わっても値が変わらない
- [ ] `Core/Loading/` のファイルから `Core/DebugReader/` を `using` していない
- [ ] `AddressableEditorSettingsSO`（`AddressableDebugWindowSettings` を保持）が型で探索され、0件のときはエディター起動時に既定パス `Assets/MAH.Data/AddressableManager/Editor/AddressableEditorSettings.asset` へ自動作成されてログが出る。複数件のときは警告が出て1件目が使われる
- [ ] 更新間隔が Inspector から変更でき、0.1 秒未満は 0.1 秒に補正される

### テスト・ドキュメント
- [ ] `AddressableDebugReader` のコピー処理と `AddressableDebugWindowSettings` の補正に対する EditMode テストが追加され、既存テストを含めてすべて通る
- [ ] spec の 8章に機能1の節が追記され、状態表が更新されている。2.4・4章・6.1 が実コードと一致している
- [ ] 新しい型名が spec・conventions・図で表記揺れなく一致している

## 4. 制約事項

- Editor 拡張は**読み取り専用**。手動 Release・強制回収ボタンは作らない（CLAUDE.md「設計の絶対ルール」）
- Controller・Service・Model はピュアC#のまま。コピー用メソッドは Model への追記に留め、Snapshot には `UnityEngine.Object` の参照を持たせない（アセット名は、コピー時に読んで文字列で渡す）
- 3つの Controller は互いを参照しない。窓口は機能フォルダの外（`Runtime/Core/DebugReader/`）に置く例外（conventions 7.4）に従う
- DI コンテナ・R3 に依存しない（Manager の手動 `new` と注入）
- 記録・表示処理は `#if UNITY_EDITOR` で本番ビルドから完全に除外する。一時コレクションを都度確保しない。クロージャ・ラムダを毎フレーム生成しない
- 描画は IMGUI（既存の Editor コードと揃える）
- `InternalsVisibleTo("MAH.AddressableManager.Editor")` を追加する。Editor 側は窓口と `Snapshot` 以外の internal 型を使わない（構造では防げないため、レビュー観点として spec 7章に明記）
- 既存の Runtime の挙動を変えない（Manager の `Awake` は Model の生成位置を変えるだけで、注入先・順序は同じ）
- CHANGELOG は更新しない（担当者の運用）。コードレビュー・手動検証・ドキュメントの最終確認は Claude の担当外

## 5. スコープ外（今回やらないこと）

- 他のタブ（リーク検出一覧・owner Watcher の可視化・読み込み計測・サイズ概算）。タブの枠だけ用意する
- `OwnerWatchRegistryModel` の窓口への注入（機能3で必要になった時点で足す）
- リークの蓄積（現状はログのみ）。機能2で扱う
- スタックトレースからソースへのジャンプ（機能2で扱う）
- 一覧の検索・絞り込み・並べ替え UI
- Initialize / Download の状態表示（`IAddressableHandler` から読めるため、必要になれば別途）
- 計測レベルとシンボルの同期（`ProfilingSymbolUpdater`）・`LoadProfiler`（機能4）
- 設計仕様書 7章の未決事項の確定
