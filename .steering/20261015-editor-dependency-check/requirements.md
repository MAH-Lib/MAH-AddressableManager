# 要求内容：AddressableManager Editor 機能4「未登録の依存の確認（Dependency Check）」

> 作業ディレクトリ：`.steering/20261015-editor-dependency-check/` ／ 作成日：2026-10-15 ／ ステータス：承認済み（受け入れ条件の確認も完了） ／ 対象 Unity バージョン：6000.3.25f1
> 関連する永続的ドキュメント：`src/AddressableManager/Docs/specs/editor-dependency-check-spec.md`（本機能の仕様の正。実装状況・2.3・4.1・4.4 は更新済み）／`src/AddressableManager/Docs/diagrams/editor-dependency-check-class.md`（新規・作成済み）・`diagrams/index.md`・`diagrams/editor-foundation-class.md`（`EditorTextFileWriter` を追記済み）／`src/AddressableManager/Docs/specs/editor-debug-window-spec.md`（`LeakLogWriter` が `EditorTextFileWriter` へ委譲する旨を追記済み）／`src/AddressableManager/Docs/addressable-manager-design.md` 3.9(4)・6章（2026-10-15 の決定として記載済み。変更なし）／`conventions.md`・`Docs/glossary.md`（前回の steering `20261015-editor-dependency-check-docs` で更新済み。新しい命名パターン・用語が出たときだけ確認する）
> 備考：実装（コード・テスト）とドキュメント更新は、この steering の作成前に完了している。本 steering は、実施内容と受け入れ条件を履歴として残すための後追いの記録である。

## 1. 変更・追加する機能の説明

Addressables に登録したアセット（親）が参照している**未登録のアセット（子）**を一覧で確認する Editor ウィンドウ `AddressableDependencyWindow`（メニュー `MAH/Addressable/Dependency Check`）を追加する。Play もビルドもせずに使え、「Scan」ボタンで全グループの依存を静的に走査する。

背景：未登録の子は、参照元の親のバンドルに暗黙的に同梱され、複数グループの親から参照されるとグループごとに重複して同梱される。ビルドサイズ・ダウンロード容量・メモリの無駄になるが、ビルドするまで気づきにくい。Editor 機能4は、当初の「読み込み計測」をやめ、この機能に差し替えた（設計仕様書 6章 2026-10-15）。

決定済みの仕様（詳細は `specs/editor-dependency-check-spec.md`）：

- **別のウィンドウにする**：データの出所がアセットの静的な走査で、Play 中の Manager ではないため、デバッグウィンドウのタブにしない。Runtime アセンブリの型・窓口には触れない
- **1行＝未登録の子1つ**：参照元の親とグループ、参照元のグループ数（2以上は重複として強調）を持つ。孫の依存も、辿り始めた登録済みの親を参照元とする
- **「登録済み」の判定**：フォルダーエントリの配下と、登録済み SpriteAtlas の packables（Texture・Sprite・フォルダー配下の Texture）は登録済みとみなす。`PlayerDataGroupSchema` のグループ（Built In Data）は親にしない
- **印**：`Resources/`（Resources と Addressables の二重化の原因）・`Pack Separately`（グループ数では検出できない重複の可能性）。`.cs`・`.dll`・`Packages/` は「既定で隠す」印を付けて行に残し、フィルターで表示できる
- **操作**：型（複数選択）・名前・グループの絞り込み、サイズ順の並べ替え、行クリックで Project ウィンドウに表示、表示中の行のテキスト出力、列幅のドラッグ変更
- **出力は人が読むテキスト**：リークのログと同じ形。日時付きのファイル名で出力先（Editor SO の設定）に溜める。書き込みの共通部は `EditorTextFileWriter` に切り出し、`LeakLogWriter` と共有する

## 2. ユーザーストーリー

- Addressables のグループを組む開発者として、ビルド前に重複同梱の原因を知るために、未登録の依存を複数グループからの参照数つきで一覧したい
- 開発者として、登録するか参照を外すかを判断するために、未登録の子を誰（どのグループの親）が参照しているかを見たい
- 開発者として、大きな重複から直すために、ファイルサイズの大きい順に並べ替え、型・グループで絞り込みたい
- 開発者として、該当アセットをすぐ確認するために、行をクリックして Project ウィンドウで選択・ハイライトしたい
- 開発者として、共有・記録するために、表示中の結果をテキストファイルに出力したい
- レビュアーとして、Addressables の設定を壊さないことを確認するために、ウィンドウが読み取り専用であってほしい

## 3. 受け入れ条件

### 走査・集約
- [x] Addressables の設定が無いとき、Scan は例外を出さず「Addressables が未設定です」と表示する
- [x] Scan で、全グループの登録済みアセット（フォルダーエントリ配下を含む）の依存（孫以降を含む）が走査され、未登録の子が1つにつき1行になる。親自身・登録済みのアセットは行にならない
- [x] `PlayerDataGroupSchema` のグループ（Built In Data）のアセットは、親として扱われない
- [x] 登録済み SpriteAtlas の packables（Texture・Sprite・フォルダー配下の Texture）は、「登録済み」とみなされ、行にならない
- [x] 別グループの2つ以上の親から参照される子は、グループ数が2以上になり、重複として強調される。同じグループの親からの参照だけなら重複にならない（親の数は増える）
- [x] パスに `/Resources/` を含む子に「Resources/」の印、参照元に `Pack Separately` のグループの親を含む子に「Pack Separately」の印が付く
- [x] `.cs`・`.dll`（大文字小文字を問わない）・`Packages/` で始まるパスの子は「既定で隠す」印が付き、既定では一覧に出ず、表示の切り替えで出る
- [x] 走査中に進捗バーが表示され、キャンセルで打ち切ると一覧が前回の結果のままになる。完了・キャンセル・例外のいずれでも進捗バーが消える
- [x] 集約・整形が Unity の型（`Type`・`Object`）を使わず、パスと型名の文字列だけで動く

### 一覧・操作
- [x] メニュー `MAH/Addressable/Dependency Check` でウィンドウ（タイトル `Addressable Dependency`）が開く。未走査のときは「Scan を押してください」と表示され、走査後は最終走査日時と件数（重複件数を含む）が表示される
- [x] 名前・型・パス・サイズ・グループ数・親の数・印の列が表示され、サイズは KB・MB 表記になる。サイズ・重複に関する注記が画面に表示される
- [x] 行を展開すると、参照元の親とそのグループが並ぶ
- [x] 型（複数選択）・名前（部分一致・大文字小文字を区別しない）・グループで絞り込め、条件は AND で組み合わさる。型・グループの選択肢は、走査結果から作られる
- [x] 並べ替えは既定がパス順で、サイズ順にするとファイルサイズの大きい順（同じサイズはパス順）になる
- [x] 子の行・参照元の親の行のクリックで、Project ウィンドウでそのアセットが選択・ハイライトされる。アセットが消えていたら警告を出して何もしない
- [x] 見出しと各行に列の区切り線が表示され、見出しの境界のドラッグで列幅を変えられる（最小 30px）。列幅の合計が表示幅を超えると横スクロールになり、見出しも一緒に動く
- [x] スクリプトの再コンパイルで、走査結果・絞り込み・列幅が残る
- [x] ウィンドウ上に、登録・グループの変更・Release・強制回収の操作が存在しない

### テキスト出力
- [x] 「テキスト出力」で、表示中の行（絞り込み後・並べ替え後）が `AddressableDependencies_yyyyMMdd_HHmmss.txt` に UTF-8（BOM なし）で出力される。同じ秒なら `_2`・`_3`… が付く
- [x] 出力内容が仕様書 2.5 の形（見出し・件数・重複の印・参照元の `←` 行）に一致し、参照元のパスの幅は出力全体の最長にそろう
- [x] 出力先は Editor SO の設定（既定 `Logs/AddressableManager`）に従い、相対パスはプロジェクトのルートから解決される。SO が無いときは既定値を使う。既存の SO アセットにも、設定の既定値が補われる
- [x] 書き込みに失敗しても、警告を出して Editor の操作を止めない

### 構造・既存機能への影響
- [x] 追加・変更が Editor アセンブリ（`MAH.AddressableManager.Editor`）の中だけで完結し、Runtime の型・窓口（`IAddressableDebugReader`）・`AddressableManager` に触れていない
- [x] グループ・エントリ・アセットを書き換える API（`CreateOrMoveEntry`・`RemoveAssetEntry` 等）を呼んでいない
- [x] `OnGUI` のたびに集計・絞り込みをやり直していない（表示行の添字は、Scan・絞り込み・並べ替えの変更のときだけ作り直す）。描画は見えている範囲の行だけ
- [x] `LeakLogWriter` の公開シグネチャと警告文言が変わらず、「リーク」タブの手動出力・Play 終了時の自動出力が従来どおり動く

### テスト・ドキュメント
- [x] 集約・ログ整形・絞り込み（`BuildVisibleRows`）・`EditorTextFileWriter`・設定に対する EditMode テストが追加され、既存の `LeakLogWriterTests` を含めてすべて通る
- [x] `DependencyCheckScanner` とウィンドウの描画は、手動検証（Scan → 一覧 → 絞り込み → 移動 → 出力、フォルダーエントリ配下・SpriteAtlas の判定）で確認される
- [x] 仕様書・クラス図・図のインデックスの記述が、コードと一致している

## 4. 制約事項

- Editor 拡張は**読み取り専用**（CLAUDE.md「設計の絶対ルール」）。登録・グループ整理の自動化、手動 Release・強制回収のボタンを作らない
- Runtime アセンブリの型・窓口には触れない。機能フォルダの相互参照をしない
- 集約・整形（Unity 非依存のクラス）に Unity の型を持ち込まない
- DI コンテナ・R3 に依存しない。UniTask を使わず、同期処理（`DisplayCancelableProgressBar`）で走査する
- 一時コレクションを描画のたびに確保しない。描画は IMGUI（既存の Editor コードと揃える）
- 既存の挙動（「リーク」タブのログ出力）を変えない。`LeakLogWriter` の抽出は、公開シグネチャ・警告文言を保つ
- 画面の文字は日本語（既存のデバッグウィンドウに合わせる）
- CHANGELOG は更新しない（担当者の運用）。コードレビュー・手動検証・ドキュメントの最終確認は Claude の担当外

## 5. スコープ外（今回やらないこと）

- Addressables への登録・グループの整理
- 「登録済みだが、どこからも参照されていない」アセットの検出（コードのアドレス指定による参照を未使用と誤判定するため）
- バンドル単位での正確な重複判定（ビルドレイアウトの計算が必要。`Pack Separately` のグループ内の重複は印と注記のみ）
- 走査の自動実行（ウィンドウを開いたとき・アセットの変更時）
- 走査結果のファイル保存と、Editor 再起動後の復元（出力したテキストで代える）
- 列幅の Editor 再起動後の保持
- `.spriteatlasv2`（SpriteAtlas V2）の packables の判定
- 機能5（EditMode でのサイズ概算）。後でこのウィンドウのタブとして加える
- ロード所要時間の計測（作らないことにした。設計仕様書 6章 2026-10-15）
