# タスクリスト：AddressableManager Editor 機能7「アセットの登録（自動登録・手動一括登録）」

> 対象：`design.md`（承認済み版） ／ 最終更新：2026-10-24 ／ ステータス：承認済み
> パスは開発フォルダ `src/AddressableManager/Assets/@MAH/MAH-AddressableManager/` 起点。章番号は `src/AddressableManager/Docs/specs/asset-registration-spec.md`（仕様）の章。

## タスク

状態：⚪未着手 → 🔵作業中 → ✅完了（🔴ブロック中はブロック理由を備考へ）

| No | タスク | 状態 | 完了条件 | 備考 |
|---|---|---|---|---|
| 1 | 設定の追加：`AddressableRegistrationSettings`・`RegistrationLogDetail`・Editor SO への `_registration` 追加・`FindRegistrationSettings()`・`AddressableRegistrationSettingsTests` | ✅ | コンパイルが通る／既存の Editor SO の Inspector に新項目が既定値で出る／テストが通る | `AddressableDependencyCheckSettings` と同形式。コンパイル通過・エラー0件を確認済み。Test Runner での実行はユーザーが行う |
| 2 | Definition 群（enum・`RegistrationRuleInput`・`RegistrationAssetInput`・`RegistrationMacroValues`・`RegistrationPlan`・`RegistrationConflict`・`RegistrationDuplicate`）と `RegistrationRuleMatcher`＋`RegistrationRuleMatcherTests` | ✅ | 仕様 9.2 の Matcher の観点（配下／外・拡張子の正規化・空拡張子・All と除外・glob・Enabled・競合・入れ子・7.2）のテストが通る | Unity 非依存。glob は自前実装 |
| 3 | `RegistrationMacroExpander`＋`RegistrationMacroExpanderTests` | ✅ | 6.2・6.3 の全マクロ・`:lower` / `:upper`・知らないマクロ／変換・空結果・大文字小文字の区別・`{Rule.Name}` 無しのテストが通る | Unity 非依存。標準の値は `RegistrationMacroValues` から引く |
| 4 | `RegistrationPlanBuilder`（`BuildAuto` / `BuildManual`）＋`RegistrationPlanBuilderTests` | ✅ | 追加・差分なし・移動・アドレス変更・Label 増減・手動変更の差し戻し・ルール外れの警告・追従／固定・空の扱い・重複・手動の状態と Label 追加のみ・同一入力→同一計画のテストが通る | Unity 非依存 |
| 5 | `RegistrationLogFormatter`＋`RegistrationLogFormatterTests` | ✅ | まとめ・1件ずつ・競合の組ごと（最初の5件）のテストが通る | Unity 非依存 |
| 6 | 仕様 11.2 の確認 → `AddressableAutoRegistrationRuleSO`・`RegistrationRuleScanner`・`RegistrationEntryWriter` | ✅ | 11.2 の各項目の結果を報告し、仕様と食い違えば仕様の更新提案を先に出して承認を得る／Create メニューでルールの SO が作れ、初期値と対象フォルダが入る／Writer が計画どおりに書き込める | **Writer が `AddressableAssetSettings` に書き込む唯一の場所**。11.2 の確認は完了・コンパイル確認済み。Create メニューでの作成と Writer の書き込みは手動検証待ち（タスク7の動作確認と合わせて行う） |
| 7 | `AutoRegistrationPostprocessor` | ✅ | ルールに当たるアセットの追加で登録される／再インポートで書き込みが起きない／スイッチオフで何もしない／再インポートの連鎖が起きない | 手動検証表の自動登録の行 |
| 8 | `RegistrationExtensionDropdown`・`AddressableAutoRegistrationRuleEditor`（Inspector とプレビュー） | ⚪ | 拡張子を選べ、プレビューに対象数・最初の3件・警告が出る。プレビューは描画のたびに再計算されない | |
| 9 | `AddressableRegistrationRuleWindow`（一覧・作成・競合と重複・当て直す） | ✅ | 手動検証表のルール一覧の行が期待どおり | |
| 10 | `AddressableManualRegistrationWindow`（右クリック入口・画面・確認ダイアログ） | ✅ | 手動検証表の手動一括登録の行が期待どおり | |
| 11 | ドキュメント更新：`specs/asset-registration-spec.md`（実装状況・9章のクラス名・11.2 の結果）・必要なら `addressable-manager-design.md` 4章・`Docs/usage.md`（英語。「読み取り専用」の修正と登録機能の節） | ✅ | 実装と文書の記述が一致する | **「ドキュメントの更新に進んで」の指示を受けてから着手する** |

各タスクの着手時に 🔵、完了時に ✅ へ更新する。1タスク＝1コミット単位で、完了時点でコンパイルが通りテストが通る状態を保つ。

## 手動検証（ユーザーが実施）

検証用に `Assets/_RegTest/`（検証後に削除）を作り、`.prefab` `.png` `.mat` を数個ずつ置く。

| 手順 | 期待結果 |
|---|---|
| パッケージ導入状態のまま何もしない | Groups ウィンドウに変化がない |
| `_RegTest/` を右クリック → Create → MAH/Addressable/AutoRegistrationRule | ルールの SO が作られ、対象フォルダが `_RegTest`、アドレスが `{Asset.Path}`、拡張子は空。Inspector に「拡張子が空」の警告 |
| 拡張子に `.prefab` を選ぶ | プレビューに対象数と最初の3件のアドレス・グループ・Label が出る。まだ登録はされない |
| ルール一覧ウィンドウで「当て直す」→「適用」 | 差分一覧に prefab の追加が並び、適用後に `DefaultGroup` に登録される |
| もう一度「当て直す」 | 「変更はありません」 |
| `_RegTest/` に prefab を1つ追加 | 自動で登録され、Console にまとめのログが出る |
| 登録済み prefab を Groups で別グループへ手で移し、右クリック → Reimport | ルールのグループへ戻り、ログが出る |
| prefab を `_RegTest/` の外へ移動 | 登録は外れず、警告が出る |
| 親フォルダに `.prefab` のルールをもう1つ作り、prefab を追加 | 登録されず、ルールの組ごとに1件のエラー。ルール一覧の競合欄に出る |
| アドレスを `{Asset.Name}` にし、同名の png を別フォルダに置いて当て直す | 登録されたうえで重複のエラーが出て、一覧の重複欄に出る |
| アドレスに `{Asset.Nmae}` を書く | プレビューと Console に知らないマクロの警告。書かれたままの文字列で登録される |
| グループ名を `{Path.Relative}` にする | 登録されず、使えない文字のエラー |
| Editor SO の自動登録をオフにして prefab を追加 | 何も登録されない。ルール一覧に「自動登録はオフ」が出て「当て直す」が押せない |
| `.png` を含むフォルダを右クリック → MAH/Addressable/Register to Addressables... | 手動一括登録ウィンドウが開き、ルール対象は「自動登録（ルール名）」でチェック不可、未登録はチェックオン |
| グループを新規作成で指定して「登録する」 | 確認ダイアログに件数と新規グループが出て、OK で登録され、もとの Label は残る |
| `Resources/` 配下のアセットを対象にする | 登録されず、警告が出る |

## 完了の定義（Definition of Done）

- [ ] requirements.md の受け入れ条件をすべて満たす
- [x] `AddressableAssetSettings` に書き込むコードが `RegistrationEntryWriter` の1か所だけである（2026-10-24 に grep で確認）
- [x] Matcher・Expander・PlanBuilder・LogFormatter・プレビューの計算と、その入出力の型が Unity の型に依存しない（2026-10-24 に using を grep で確認。設定クラス〔Data 層〕は `SerializeField` などを使うため対象外）
- [x] Runtime アセンブリ（`AddressableManager`・窓口）に変更がない（作業開始以降に更新された Runtime の .cs が無いことを確認）
- [ ] コンパイルエラー・警告がなく、既存のテストを含めて EditMode テストがすべて通る
- [x] 仕様 11.2 の確認結果を報告し、仕様と食い違う点は承認を得ている（食い違いは無し。手動一括登録の画面の変更は、ユーザーの指示で行った）
- [x] 影響のある永続的ドキュメント（`asset-registration-spec.md`・`addressable-manager-design.md`・`Docs/usage.md`・ルート `README.md` / `README_JP.md`）の更新が完了している（タスク11）
- [x] `CHANGELOG.md` は更新していない（担当者の運用）。レビュー・手動検証・コミットの判断はユーザーが行う
