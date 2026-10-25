# タスクリスト：アセットの登録（自動登録・手動一括登録）のレビュー指摘修正

> 対象：`design.md`（承認済み版） ／ 最終更新：2026-10-24 ／ ステータス：実装完了（Test Runner での実行と手動の確認はユーザー待ち）

## タスク

状態：⚪未着手 → 🔵作業中 → ✅完了（🔴ブロック中はブロック理由を備考へ）

**Claude の担当は実装（コードとテストコード）まで**。テストの実行（Test Runner は MCP から起動できない）・手動の確認・コミット・CHANGELOG の更新は担当外。仕様書（`asset-registration-spec.md`）は、「ドキュメントの更新に進んで」と指示されるまで書き換えない。各タスクは1コミット単位で、完了した時点でプロジェクトが動く状態を保つ。

コードは `src/AddressableManager/Assets/@MAH/MAH-AddressableManager/`（開発フォルダ）を起点とする。

| No | タスク | 状態 | 完了条件 | 備考 |
|---|---|---|---|---|
| 0 | steering の作成（requirements → design → tasklist。1ファイルごとに承認） | ✅ | 3ファイルが存在し、それぞれ承認されている | 3ファイルとも承認済み |
| 1 | ルールのキャッシュを捨てる条件を直す：`AutoRegistrationPostprocessor` の判定をスイッチの前に移し、`ContainsFolder`・`ContainsDeletedFolder` を足す | ✅ | コンパイルが通る。対象フォルダの名前を変えたあと、中に置いたアセットが登録される（手動） | 🔴。実装済み・Console にエラーなし。手動の確認（フォルダの名前の変更・スイッチがオフの間の削除）はユーザー |
| 2 | Matcher の確保をなくし、`IsUnderAnyEnabledRuleFolder` を足す。`CollectImportedAssets` に `rules` を渡して事前に絞る。Matcher のテストを足す | ✅ | `RegistrationRuleMatcherTests` がすべて通る（既存の拡張子のテストを含む）。スクリプトの保存で `CreateEnvironment` が走らない（Profiler。手動） | 🔴。実装・テストの追加済み（`IsUnderAnyEnabledRuleFolder` と拡張子の境界）。Test Runner での実行と Profiler の確認はユーザー |
| 3 | 規約の修正：`RegistrationGroupRequest`・`RegistrationApplyResult`・`CreateRuleEndNameEditAction.Rule`・`AccTargetFolderPath`・`RegistrationPlan` の件数4つ・`RegistrationChange`・`RegistrationManualRow`。テストの参照も直す | ✅ | コンパイルが通り、EditMode のテストがすべて通る | 🟡。実装済み・Console にエラーなし。private の入れ子クラス（`TargetGroup`・`RuleRow`）は対象外。Test Runner での実行はユーザー |
| 4 | Writer で状態を確かめ、スキップ件数を `RegistrationApplyResult` に返す。2つのウィンドウで警告とメッセージを出す | ✅ | コンパイルが通る。ウィンドウを開いたまま同じアセットを別に登録して「登録する」を押すと、書き込まれず警告が出る（手動） | 🟡。実装済み。スキップごとの警告は Console があふれるため出さず、`RegistrationLogFormatter.FormatSkipped` で件数をまとめて知らせる（計画書の「読み込み直してください」は、ウィンドウが書き込み後に自動で読み直すため「もう一度実行してください」にした）。手で変えたエントリを戻す動作が変わらないことも手動で確認 |
| 5 | 競合に GUID を持たせ、名前と組で並べる。`SelectRule` を GUID にする。PlanBuilder と Formatter のテストを直して足す | ✅ | `RegistrationPlanBuilderTests`・`RegistrationLogFormatterTests` が通る | 🟢。実装・テストの追加済み（名前順と GUID 順が逆になる入力、同じ名前の SO が2つの入力）。`RegistrationConflict` のコンストラクタは GUID を省略可にしたため、既存のテストは無修正。Test Runner での実行はユーザー |
| 6 | Formatter に警告の要約（`MAX_SUMMARY_WARNINGS = 5`）を足す。ルール一覧の差分の文字列を1回だけ作り、差分と警告を見えている行だけ描く | ✅ | 要約のテストが通る。6件以上の当て直しで Console の警告が1件にまとまる（手動） | 🟡。実装・テストの追加済み（要約・上限以内・`PerAsset` で全件・エラーは要約しない・`FormatSkipped`）。ルール一覧のエラーと警告も、見えている行だけを描く一覧にした（`HelpBox` の赤・黄のアイコンは見出しに置き換えた）。Test Runner での実行と画面の確認はユーザー |
| 7 | Inspector：`[Delayed]`、プレビューのキャッシュ、重なりの警告を前回と比べる | ✅ | コンパイルが通る。テンプレートを打っている間はプレビューも Console も更新されず、確定すると更新される（手動） | 🟡。実装済み。プレビューのキャッシュは、`EditorApplication.projectChanged` でも捨てる（フォルダに足したアセットが反映されるように）。`[Delayed]` がリストの要素に効かない場合は、リストの `[Delayed]` を外す（手動で確認） |
| 8 | 手動一括登録：サムネイルのキャッシュと `GetCachedIcon`、グループ・Label の `[Delayed]`。🟢：`"{Rule.Name"` の判定、`Collect` の戻り値、Create メニューのキャンセルで SO を破棄 | ✅ | コンパイルが通る。`.asset`・`.anim` を含むフォルダでスクロールしても、毎回の読み込みが起きない（手動） | 🟡・🟢。実装済み。プレビューが来ないアセットは、1回待ってから種類のアイコンをキャッシュする（要求した直後に誤ってキャッシュしないため）。`Collect` は `TryCollect`（戻り値あり）を呼ぶ形にし、登録後の読み込みがキャンセルされたときは、書き込みの結果とキャンセルのメッセージを両方残す |
| 9 | 仕様書の更新箇所の一覧の報告 | ✅ | 2.4・8章、4.3・5.4、3.6、11.2、9.1 の更新箇所を一覧で報告している | 報告のあと、「ドキュメント更新に進んで」の指示で更新した（No.10） |
| 10 | ドキュメントの更新：`src/AddressableManager/Docs/specs/asset-registration-spec.md`（冒頭・2.1・2.2・2.4・3.6・4.2・4.3・5.2・5.4・6.5・8章・9.1・9.2・10章・11.2）と、利用者向けの `Docs/usage.md`（6.3・6.4・6.5。英語） | ✅ | 仕様書と usage.md が、実装した挙動（事前の絞り込み・スキップ・警告の要約・`[Delayed]`・プレビューのキャッシュ・GUID での選択・グループ名の既知の制約）と一致している | usage.md は「警告は常に出る」が要約の動きと食い違っていたため、あわせて直した。10章には、却下・見送りの判断も日付つきで追記した。用語集・設計仕様書・`conventions.md` は影響なし。最終確認はユーザー |

## 完了の定義（Definition of Done）

- [ ] requirements.md の受け入れ条件をすべて満たす
- [ ] Runtime の窓口・`AddressableManager` に触れていない。`AddressableAssetSettings` に書き込むのは `RegistrationEntryWriter` だけ
- [ ] コンパイルエラーがない（Unity の Console）
- [ ] 既存テストと追加テストがすべて通る（ユーザーが Test Runner で実行して確認する）
- [ ] 実装計画書の「手動での検証」をすべて満たす（ユーザーが確認する）
- [x] 影響のある永続的ドキュメント（Docs/）の更新が完了している（仕様書と usage.md。最終確認はユーザー）
- [x] `CHANGELOG.md` は更新していない（担当者の運用）
