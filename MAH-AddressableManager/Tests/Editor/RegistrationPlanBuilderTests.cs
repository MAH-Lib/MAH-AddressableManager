using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using MAH.AddressableManager.Editor.Registration;

namespace MAH.AddressableManager.Tests.Editor
{
    internal sealed class RegistrationPlanBuilderTests
    {
        private const string DEFAULT_GROUP = "Default Local Group";
        private static readonly string[] NONE = new string[0];

        // ---- テストデータの組み立て ----

        private static RegistrationRuleInput Rule(
            string name,
            string folder,
            string[] extensions = null,
            string address = "",
            string group = "",
            string[] labels = null,
            RegistrationAddressMode mode = RegistrationAddressMode.Follow,
            string template_guid = "",
            bool enabled = true)
        {
            return new RegistrationRuleInput(
                name, name, folder, enabled, RegistrationTargetMode.Extensions,
                extensions ?? new[] { ".prefab", ".png" }, NONE, NONE,
                address, group, template_guid, labels ?? NONE, mode);
        }

        // 名前と GUID を別々に指定するルール（名前順と GUID 順が食い違う入力を作るため）
        private static RegistrationRuleInput RuleWithGuid(string name, string guid, string folder)
        {
            return new RegistrationRuleInput(
                name, guid, folder, true, RegistrationTargetMode.Extensions,
                new[] { ".prefab" }, NONE, NONE,
                string.Empty, string.Empty, string.Empty, NONE, RegistrationAddressMode.Follow);
        }

        private static RegistrationAssetInput Asset(
            string path,
            bool registered = false,
            string group = "",
            string address = "",
            string[] labels = null,
            string moved_from = "")
        {
            return new RegistrationAssetInput(path, "guid:" + path, "GameObject", registered, group, address, labels ?? NONE, moved_from);
        }

        private static RegistrationEnvironment Env(
            string[] groups = null,
            string[] labels = null,
            RegistrationEntryInput[] entries = null)
        {
            return new RegistrationEnvironment(
                groups ?? new[] { DEFAULT_GROUP }, DEFAULT_GROUP, labels ?? NONE,
                new Dictionary<string, string>(), entries ?? new RegistrationEntryInput[0]);
        }

        private static RegistrationPlan Build(RegistrationRuleInput[] rules, RegistrationAssetInput[] assets, RegistrationEnvironment env = null)
        {
            return RegistrationPlanBuilder.BuildAuto(rules, assets, env ?? Env());
        }

        // ---- 未登録の追加・テンプレートの展開 ----

        // 未登録のアセットが、空のテンプレートでは「アセットパス・DefaultGroup・Label なし」で追加される
        [Test]
        public void AddsUnregisteredAssetWithDefaults()
        {
            var plan = Build(new[] { Rule("R", "Assets/App") }, new[] { Asset("Assets/App/Title.prefab") });

            Assert.AreEqual(1, plan.AccChanges.Count);
            var change = plan.AccChanges[0];
            Assert.IsTrue(change.AccIsNewEntry);
            Assert.AreEqual("Assets/App/Title.prefab", change.AccToAddress);
            Assert.AreEqual(DEFAULT_GROUP, change.AccToGroup);
            Assert.AreEqual(0, change.AccLabelsToAdd.Count);
            Assert.AreEqual(1, plan.CountAdded());
            Assert.AreEqual(0, plan.AccNewGroups.Count);
            Assert.AreEqual("R", change.AccRuleName);
        }

        // テンプレートが展開され、無いグループと Label は新しく作る対象になる
        [Test]
        public void ExpandsTemplatesAndRecordsNewGroupAndLabels()
        {
            var rule = Rule("R", "Assets/App", address: "{Path.Relative}", group: "UI_{Folder.Top}",
                labels: new[] { "Common", "{Folder.Top}" }, template_guid: "tpl-guid");
            var plan = Build(new[] { rule }, new[] { Asset("Assets/App/Menu/Title.prefab") });

            var change = plan.AccChanges[0];
            Assert.AreEqual("Menu/Title", change.AccToAddress);
            Assert.AreEqual("UI_Menu", change.AccToGroup);
            CollectionAssert.AreEqual(new[] { "Common", "Menu" }, change.AccLabelsToAdd);
            Assert.AreEqual(1, plan.AccNewGroups.Count);
            Assert.AreEqual("UI_Menu", plan.AccNewGroups[0].AccName);
            Assert.AreEqual("tpl-guid", plan.AccNewGroups[0].AccTemplateAssetGuid);
            CollectionAssert.AreEqual(new[] { "Common", "Menu" }, plan.AccNewLabels);
        }

        // 同じ名前の新しいグループ・Label は、1回だけ記録される。すでにあるものは記録されない
        [Test]
        public void NewGroupsAndLabelsAreRecordedOnceAndSkipExisting()
        {
            var rule = Rule("R", "Assets/App", group: "UI", labels: new[] { "Common", "Old" });
            var plan = Build(
                new[] { rule },
                new[] { Asset("Assets/App/A.prefab"), Asset("Assets/App/B.prefab") },
                Env(groups: new[] { DEFAULT_GROUP }, labels: new[] { "Old" }));

            Assert.AreEqual(2, plan.AccChanges.Count);
            Assert.AreEqual(1, plan.AccNewGroups.Count);
            CollectionAssert.AreEqual(new[] { "Common" }, plan.AccNewLabels);

            var existing = Build(new[] { rule }, new[] { Asset("Assets/App/A.prefab") }, Env(groups: new[] { DEFAULT_GROUP, "UI" }));
            Assert.AreEqual(0, existing.AccNewGroups.Count);
        }

        // 展開した結果が空のときは、アドレスはアセットパス、グループは DefaultGroup、Label は付けない
        [Test]
        public void EmptyExpansionFallsBackToDefaults()
        {
            var rule = Rule("R", "Assets/App", address: "{Folder.Top}", group: "{Folder.Top}", labels: new[] { "{Folder.Top}" });
            var plan = Build(new[] { rule }, new[] { Asset("Assets/App/Title.prefab") });

            var change = plan.AccChanges[0];
            Assert.AreEqual("Assets/App/Title.prefab", change.AccToAddress);
            Assert.AreEqual(DEFAULT_GROUP, change.AccToGroup);
            Assert.AreEqual(0, change.AccLabelsToAdd.Count);
        }

        // 同じ Label は1つにまとめられる
        [Test]
        public void DuplicateLabelsAreMerged()
        {
            var rule = Rule("R", "Assets/App", labels: new[] { "A", "A", "B" });
            var plan = Build(new[] { rule }, new[] { Asset("Assets/App/Title.prefab") });

            CollectionAssert.AreEqual(new[] { "A", "B" }, plan.AccChanges[0].AccLabelsToAdd);
        }

        // 知らないマクロは、書かれたままで登録され、アセットパス付きの警告が出る
        [Test]
        public void UnknownMacroIsKeptAndWarned()
        {
            var rule = Rule("R", "Assets/App", address: "{Asset.Nmae}");
            var plan = Build(new[] { rule }, new[] { Asset("Assets/App/Title.prefab") });

            Assert.AreEqual("{Asset.Nmae}", plan.AccChanges[0].AccToAddress);
            Assert.AreEqual(1, plan.AccWarnings.Count);
            StringAssert.Contains("Assets/App/Title.prefab", plan.AccWarnings[0]);
            StringAssert.Contains("{Asset.Nmae}", plan.AccWarnings[0]);
        }

        // グループ名に使えない文字が入るアセットは、登録せずエラーになる。ほかのアセットは登録される
        [Test]
        public void InvalidGroupNameIsErrorAndSkipsOnlyThatAsset()
        {
            var rule = Rule("R", "Assets/App", group: "{Folder.Relative}");
            var plan = Build(
                new[] { rule },
                new[] { Asset("Assets/App/Sub/Deep/Title.prefab"), Asset("Assets/App/Root.prefab") });

            Assert.AreEqual(1, plan.AccErrors.Count);
            StringAssert.Contains("Assets/App/Sub/Deep/Title.prefab", plan.AccErrors[0]);
            Assert.AreEqual(1, plan.AccChanges.Count);
            Assert.AreEqual("Assets/App/Root.prefab", plan.AccChanges[0].AccAssetPath);
        }

        // ---- 差分なし・更新 ----

        // ルールの内容と同じ登録済みのアセットは、差分がなく、変更を作らない
        [Test]
        public void NoDiffProducesEmptyPlan()
        {
            var rule = Rule("R", "Assets/App", group: "UI", labels: new[] { "Common" });
            var asset = Asset("Assets/App/Title.prefab", registered: true, group: "UI", address: "Assets/App/Title.prefab", labels: new[] { "Common" });
            var plan = Build(new[] { rule }, new[] { asset }, Env(groups: new[] { DEFAULT_GROUP, "UI" }, labels: new[] { "Common" }));

            Assert.IsFalse(plan.AccHasChanges);
            Assert.AreEqual(0, plan.AccWarnings.Count);
            Assert.AreEqual(0, plan.AccNewGroups.Count);
        }

        // グループの移動・アドレスの変更・Label の追加と削除を、ルールの内容に合わせて行う（手で変えたものも戻す）
        [Test]
        public void UpdatesGroupAddressAndLabels()
        {
            var rule = Rule("R", "Assets/App", address: "{Asset.Name}", group: "UI", labels: new[] { "Keep", "New" });
            var asset = Asset("Assets/App/Title.prefab", registered: true, group: "Manual", address: "OldAddress", labels: new[] { "Keep", "Stale" });
            var plan = Build(new[] { rule }, new[] { asset }, Env(groups: new[] { DEFAULT_GROUP, "UI", "Manual" }, labels: new[] { "Keep", "Stale", "New" }));

            var change = plan.AccChanges.Single();
            Assert.IsFalse(change.AccIsNewEntry);
            Assert.AreEqual("Manual", change.AccFromGroup);
            Assert.AreEqual("UI", change.AccToGroup);
            Assert.IsTrue(change.AccGroupChanged);
            Assert.AreEqual("OldAddress", change.AccFromAddress);
            Assert.AreEqual("Title", change.AccToAddress);
            Assert.IsTrue(change.AccAddressChanged);
            CollectionAssert.AreEqual(new[] { "New" }, change.AccLabelsToAdd);
            CollectionAssert.AreEqual(new[] { "Stale" }, change.AccLabelsToRemove);
            Assert.AreEqual(0, plan.CountAdded());
            Assert.AreEqual(1, plan.CountMoved());
            Assert.AreEqual(1, plan.CountReaddressed());
            Assert.AreEqual(1, plan.CountLabelChanged());
        }

        // 追従のルールでアドレスが変わるときは、変更前後が警告に出る
        [Test]
        public void FollowModeWarnsWithOldAndNewAddress()
        {
            var rule = Rule("R", "Assets/App", address: "{Asset.Name}");
            var asset = Asset("Assets/App/Title.prefab", registered: true, group: DEFAULT_GROUP, address: "Old");
            var plan = Build(new[] { rule }, new[] { asset });

            Assert.AreEqual(1, plan.AccWarnings.Count);
            StringAssert.Contains("Old", plan.AccWarnings[0]);
            StringAssert.Contains("Title", plan.AccWarnings[0]);
        }

        // 固定のルールは、登録済みのアドレスを変えない（グループは当て直す）。警告も出ない
        [Test]
        public void FixedModeKeepsRegisteredAddressButUpdatesGroup()
        {
            var rule = Rule("R", "Assets/App", address: "{Asset.Name}", group: "UI", mode: RegistrationAddressMode.Fixed);
            var asset = Asset("Assets/App/Title.prefab", registered: true, group: DEFAULT_GROUP, address: "Pinned");
            var plan = Build(new[] { rule }, new[] { asset });

            var change = plan.AccChanges.Single();
            Assert.AreEqual("Pinned", change.AccToAddress);
            Assert.IsFalse(change.AccAddressChanged);
            Assert.AreEqual("UI", change.AccToGroup);
            Assert.AreEqual(0, plan.AccWarnings.Count);
        }

        // 固定でも、新しく登録するときは展開する
        [Test]
        public void FixedModeExpandsForNewEntry()
        {
            var rule = Rule("R", "Assets/App", address: "{Asset.Name}", mode: RegistrationAddressMode.Fixed);
            var plan = Build(new[] { rule }, new[] { Asset("Assets/App/Title.prefab") });

            Assert.AreEqual("Title", plan.AccChanges[0].AccToAddress);
        }

        // ---- ルールに当たらない・競合・登録しないもの ----

        // どのルールにも当たらないアセットは、触らない（警告も出さない）
        [Test]
        public void UnmatchedAssetIsUntouched()
        {
            var plan = Build(
                new[] { Rule("R", "Assets/App") },
                new[] { Asset("Assets/Other/Title.prefab", registered: true, group: "Manual", address: "x") });

            Assert.IsFalse(plan.AccHasChanges);
            Assert.AreEqual(0, plan.AccWarnings.Count);
        }

        // 移動でルールに当たらなくなった登録済みのアセットは、登録を外さず、警告する
        [Test]
        public void MovedOutOfRuleWarnsAndKeepsRegistration()
        {
            var rule = Rule("R", "Assets/App");
            var asset = Asset("Assets/Other/Title.prefab", registered: true, group: DEFAULT_GROUP, address: "x", moved_from: "Assets/App/Title.prefab");
            var plan = Build(new[] { rule }, new[] { asset });

            Assert.IsFalse(plan.AccHasChanges);
            Assert.AreEqual(1, plan.AccWarnings.Count);
            StringAssert.Contains("Assets/App/Title.prefab", plan.AccWarnings[0]);
        }

        // もとからルールに当たらない移動・未登録の移動は、警告しない
        [Test]
        public void MoveWithoutPreviousMatchDoesNotWarn()
        {
            var rule = Rule("R", "Assets/App");
            var from_outside = Asset("Assets/Other/B.prefab", registered: true, group: "G", address: "b", moved_from: "Assets/Else/B.prefab");
            var unregistered = Asset("Assets/Other/C.prefab", moved_from: "Assets/App/C.prefab");
            var plan = Build(new[] { rule }, new[] { from_outside, unregistered });

            Assert.AreEqual(0, plan.AccWarnings.Count);
        }

        // 移動で別のルールに当たるようになったら、新しいルールで当て直す
        [Test]
        public void MovedIntoOtherRuleIsReapplied()
        {
            var old_rule = Rule("Old", "Assets/Old", group: "OldGroup");
            var new_rule = Rule("New", "Assets/New", group: "NewGroup");
            var asset = Asset("Assets/New/Title.prefab", registered: true, group: "OldGroup", address: "Assets/Old/Title.prefab", moved_from: "Assets/Old/Title.prefab");
            var plan = Build(new[] { old_rule, new_rule }, new[] { asset }, Env(groups: new[] { DEFAULT_GROUP, "OldGroup", "NewGroup" }));

            var change = plan.AccChanges.Single();
            Assert.AreEqual("NewGroup", change.AccToGroup);
            Assert.AreEqual("Assets/New/Title.prefab", change.AccToAddress);
        }

        // Enabled がオフのルールは、無いものとして扱う
        [Test]
        public void DisabledRuleIsIgnored()
        {
            var plan = Build(new[] { Rule("R", "Assets/App", enabled: false) }, new[] { Asset("Assets/App/Title.prefab") });

            Assert.IsFalse(plan.AccHasChanges);
        }

        // 競合したアセットは登録も変更もされず、ルールの組ごとに1件の競合にまとまる
        [Test]
        public void ConflictSkipsAssetsAndGroupsByRulePair()
        {
            var parent = Rule("Parent", "Assets", group: "P");
            var child = Rule("Child", "Assets/App", group: "C");
            var assets = new[]
            {
                Asset("Assets/App/B.prefab", registered: true, group: "Manual", address: "b"),
                Asset("Assets/App/A.prefab"),
                // Parent だけに当たるアセットは、競合せず、普通に登録される
                Asset("Assets/Top/T.prefab"),
            };
            var plan = Build(new[] { parent, child }, assets);

            Assert.AreEqual(1, plan.AccConflicts.Count);
            CollectionAssert.AreEqual(new[] { "Child", "Parent" }, plan.AccConflicts[0].AccRuleNames);
            CollectionAssert.AreEqual(new[] { "Assets/App/A.prefab", "Assets/App/B.prefab" }, plan.AccConflicts[0].AccAssetPaths);
            Assert.AreEqual("Assets/Top/T.prefab", plan.AccChanges.Single().AccAssetPath);
        }

        // 競合のルール名と GUID は、同じ並び（名前順）で組のまま持たれる。名前順と GUID 順が逆でも、対応がずれない
        [Test]
        public void ConflictKeepsRuleNamesAndGuidsInSameOrder()
        {
            // 名前順は Alpha → Beta、GUID 順は逆（guid-2 → guid-1）
            var alpha = RuleWithGuid("Alpha", "guid-2", "Assets/App");
            var beta = RuleWithGuid("Beta", "guid-1", "Assets/App");
            var plan = Build(new[] { beta, alpha }, new[] { Asset("Assets/App/A.prefab") });

            var conflict = plan.AccConflicts.Single();
            CollectionAssert.AreEqual(new[] { "Alpha", "Beta" }, conflict.AccRuleNames);
            CollectionAssert.AreEqual(new[] { "guid-2", "guid-1" }, conflict.AccRuleGuids);
        }

        // 同じ名前のルールが2つあっても、GUID が別々に持たれる
        [Test]
        public void ConflictDistinguishesRulesWithSameName()
        {
            var first = RuleWithGuid("Same", "guid-a", "Assets/App");
            var second = RuleWithGuid("Same", "guid-b", "Assets/App");
            var plan = Build(new[] { second, first }, new[] { Asset("Assets/App/A.prefab") });

            var conflict = plan.AccConflicts.Single();
            CollectionAssert.AreEqual(new[] { "Same", "Same" }, conflict.AccRuleNames);
            CollectionAssert.AreEqual(new[] { "guid-a", "guid-b" }, conflict.AccRuleGuids);
        }

        // 違うルールの組の競合は、別々にまとまる
        [Test]
        public void DifferentRulePairsAreSeparateConflicts()
        {
            var a = Rule("A", "Assets/X");
            var b = Rule("B", "Assets/X");
            var c = Rule("C", "Assets/Y");
            var d = Rule("D", "Assets/Y");
            var plan = Build(new[] { a, b, c, d }, new[] { Asset("Assets/X/1.prefab"), Asset("Assets/Y/1.prefab") });

            Assert.AreEqual(2, plan.AccConflicts.Count);
            Assert.IsFalse(plan.AccHasChanges);
        }

        // スクリプト・.meta は黙って無視する。Resources の配下は、ルールに当たるときだけ警告して登録しない
        [Test]
        public void IgnoredAndResourcesAssetsAreNotRegistered()
        {
            var rule = new RegistrationRuleInput("All", "All", "Assets/App", true, RegistrationTargetMode.All, NONE, NONE, NONE,
                string.Empty, string.Empty, string.Empty, NONE, RegistrationAddressMode.Follow);
            var assets = new[]
            {
                Asset("Assets/App/Script.cs"),
                Asset("Assets/App/Resources/Title.prefab"),
                Asset("Assets/Elsewhere/Resources/Title.prefab"),
                Asset("Assets/App/Title.prefab"),
            };
            var plan = Build(new[] { rule }, assets);

            Assert.AreEqual(1, plan.AccChanges.Count);
            Assert.AreEqual("Assets/App/Title.prefab", plan.AccChanges[0].AccAssetPath);
            Assert.AreEqual(1, plan.AccWarnings.Count);
            StringAssert.Contains("Assets/App/Resources/Title.prefab", plan.AccWarnings[0]);
        }

        // ---- アドレスの重複 ----

        // 別々のアセットが同じアドレスになるときは、登録はして、重複として記録する
        [Test]
        public void DetectsDuplicateAddressBetweenTargets()
        {
            var rule = Rule("R", "Assets/App", address: "{Asset.Name}");
            var plan = Build(new[] { rule }, new[] { Asset("Assets/App/A/Icon.png"), Asset("Assets/App/B/Icon.png") });

            Assert.AreEqual(2, plan.AccChanges.Count);
            Assert.AreEqual(1, plan.AccDuplicates.Count);
            Assert.AreEqual("Icon", plan.AccDuplicates[0].AccAddress);
            Assert.AreEqual(2, plan.AccDuplicates[0].AccEntries.Count);
        }

        // 手動・他のツールで登録した既存のエントリとの重複も検出する
        [Test]
        public void DetectsDuplicateWithExistingEntry()
        {
            var rule = Rule("R", "Assets/App", address: "{Asset.Name}");
            var env = Env(entries: new[] { new RegistrationEntryInput("Assets/Manual/Icon.png", "Icon", "ManualGroup") });
            var plan = Build(new[] { rule }, new[] { Asset("Assets/App/Icon.png") }, env);

            Assert.AreEqual(1, plan.AccDuplicates.Count);
            CollectionAssert.AreEquivalent(
                new[] { "Assets/Manual/Icon.png", "Assets/App/Icon.png" },
                plan.AccDuplicates[0].AccEntries.Select(e => e.AccAssetPath));
        }

        // 自分自身の今のエントリは、重複にならない（アドレスが変わった後の状態で比べる）
        [Test]
        public void OwnCurrentEntryIsNotDuplicate()
        {
            var rule = Rule("R", "Assets/App", address: "New");
            var asset = Asset("Assets/App/Icon.png", registered: true, group: DEFAULT_GROUP, address: "Old");
            var env = Env(entries: new[] { new RegistrationEntryInput("Assets/App/Icon.png", "Old", DEFAULT_GROUP) });
            var plan = Build(new[] { rule }, new[] { asset }, env);

            Assert.AreEqual(0, plan.AccDuplicates.Count);
        }

        // 対象を含まない既存どうしの重複は、この計画では記録しない
        [Test]
        public void ExistingOnlyDuplicatesAreIgnored()
        {
            var rule = Rule("R", "Assets/App");
            var env = Env(entries: new[]
            {
                new RegistrationEntryInput("Assets/X/a.png", "same", "G"),
                new RegistrationEntryInput("Assets/Y/b.png", "same", "G"),
            });
            var plan = Build(new[] { rule }, new[] { Asset("Assets/App/Title.prefab") }, env);

            Assert.AreEqual(0, plan.AccDuplicates.Count);
        }

        // 差分がない対象でも、重複は検出する
        [Test]
        public void DuplicateIsDetectedEvenWithoutDiff()
        {
            var rule = Rule("R", "Assets/App", address: "Same");
            var asset = Asset("Assets/App/Icon.png", registered: true, group: DEFAULT_GROUP, address: "Same");
            var env = Env(entries: new[]
            {
                new RegistrationEntryInput("Assets/App/Icon.png", "Same", DEFAULT_GROUP),
                new RegistrationEntryInput("Assets/Other/Icon.png", "Same", DEFAULT_GROUP),
            });
            var plan = Build(new[] { rule }, new[] { asset }, env);

            Assert.IsFalse(plan.AccHasChanges);
            Assert.AreEqual(1, plan.AccDuplicates.Count);
        }

        // ---- 同じ入力から同じ計画 ----

        // 入力の並びを変えても、同じ計画（変更の並びも同じ）ができる
        [Test]
        public void SameInputProducesSamePlanRegardlessOfOrder()
        {
            var rule = Rule("R", "Assets/App", address: "{Asset.Name}", group: "UI", labels: new[] { "L" });
            var a = Asset("Assets/App/A.prefab");
            var b = Asset("Assets/App/B.prefab", registered: true, group: "Old", address: "b");
            var c = Asset("Assets/App/C.prefab");

            var first = Build(new[] { rule }, new[] { a, b, c });
            var second = Build(new[] { rule }, new[] { c, a, b });
            var third = Build(new[] { rule }, new[] { a, b, c });

            Assert.AreEqual(Describe(first), Describe(second));
            Assert.AreEqual(Describe(first), Describe(third));
            CollectionAssert.AreEqual(
                new[] { "Assets/App/A.prefab", "Assets/App/B.prefab", "Assets/App/C.prefab" },
                first.AccChanges.Select(x => x.AccAssetPath));
        }

        private static string Describe(RegistrationPlan plan)
        {
            return string.Join(";", plan.AccChanges.Select(c =>
                $"{c.AccAssetPath}|{c.AccIsNewEntry}|{c.AccFromGroup}>{c.AccToGroup}|{c.AccFromAddress}>{c.AccToAddress}|+{string.Join(",", c.AccLabelsToAdd)}|-{string.Join(",", c.AccLabelsToRemove)}"));
        }

        // ---- 手動一括登録 ----

        private static RegistrationRuleInput Spec(string folder, string address = "{Asset.Name}", string group = "", string[] labels = null, string template_guid = "")
        {
            return new RegistrationRuleInput(
                string.Empty, string.Empty, folder, true, RegistrationTargetMode.Extensions,
                new[] { ".prefab", ".png" }, NONE, NONE,
                address, group, template_guid, labels ?? NONE, RegistrationAddressMode.Follow);
        }

        private static List<RegistrationManualRow> Rows(RegistrationRuleInput spec, RegistrationRuleInput[] rules, RegistrationAssetInput[] assets, RegistrationEnvironment env = null)
        {
            var rows = new List<RegistrationManualRow>();
            RegistrationPlanBuilder.BuildManualRows(spec, rules, assets, env ?? Env(), rows);
            return rows;
        }

        // 状態の判定：未登録・登録済み・自動登録・競合中・自動登録の対象（未適用）。登録しないものと指定の外は出さない
        [Test]
        public void ManualRowsClassifyStates()
        {
            var spec = Spec("Assets/App");
            var rule_auto = Rule("AutoRule", "Assets/App/Auto");
            var rule_a = Rule("A", "Assets/App/Conflict");
            var rule_b = Rule("B", "Assets/App/Conflict");
            var assets = new[]
            {
                Asset("Assets/App/Unregistered.prefab"),
                Asset("Assets/App/Registered.prefab", registered: true, group: "Manual", address: "x"),
                // 自動登録のルールの内容と一致している
                Asset("Assets/App/Auto/Done.prefab", registered: true, group: DEFAULT_GROUP, address: "Assets/App/Auto/Done.prefab"),
                // 自動登録のルールに当たるが、まだ当て直していない
                Asset("Assets/App/Auto/Pending.prefab"),
                Asset("Assets/App/Conflict/X.prefab"),
                Asset("Assets/App/Script.cs"),
                Asset("Assets/App/Resources/R.prefab"),
                Asset("Assets/Outside/Out.prefab"),
            };
            var rows = Rows(spec, new[] { rule_auto, rule_a, rule_b }, assets);

            var states = rows.ToDictionary(r => r.AccAssetPath, r => r.AccState);
            Assert.AreEqual(5, rows.Count);
            Assert.AreEqual(ManualRegistrationState.Unregistered, states["Assets/App/Unregistered.prefab"]);
            Assert.AreEqual(ManualRegistrationState.Registered, states["Assets/App/Registered.prefab"]);
            Assert.AreEqual(ManualRegistrationState.AutoRegistered, states["Assets/App/Auto/Done.prefab"]);
            Assert.AreEqual(ManualRegistrationState.PendingAuto, states["Assets/App/Auto/Pending.prefab"]);
            Assert.AreEqual(ManualRegistrationState.Conflicting, states["Assets/App/Conflict/X.prefab"]);
            Assert.AreEqual("AutoRule", rows.First(r => r.AccState == ManualRegistrationState.AutoRegistered).AccAutoRuleName);
        }

        // チェックの既定：未登録だけオン。自動登録・競合中・未適用はチェックできない
        [Test]
        public void ManualRowsDefaultCheckAndCanCheck()
        {
            var spec = Spec("Assets/App");
            var rule = Rule("AutoRule", "Assets/App/Auto");
            var assets = new[]
            {
                Asset("Assets/App/A.prefab"),
                Asset("Assets/App/B.prefab", registered: true, group: "Manual", address: "b"),
                Asset("Assets/App/Auto/C.prefab"),
            };
            var rows = Rows(spec, new[] { rule }, assets).ToDictionary(r => r.AccAssetPath);

            Assert.IsTrue(rows["Assets/App/A.prefab"].AccChecked);
            Assert.IsTrue(rows["Assets/App/A.prefab"].AccCanCheck);
            Assert.IsFalse(rows["Assets/App/B.prefab"].AccChecked);
            Assert.IsTrue(rows["Assets/App/B.prefab"].AccCanCheck);
            Assert.IsFalse(rows["Assets/App/Auto/C.prefab"].AccChecked);
            Assert.IsFalse(rows["Assets/App/Auto/C.prefab"].AccCanCheck);
        }

        // 自動登録がオフのとき（ルールを渡さないとき）は、ルールに当たるアセットも手動で登録できる
        [Test]
        public void ManualRowsIgnoreRulesWhenNoneGiven()
        {
            var rows = Rows(Spec("Assets/App"), new RegistrationRuleInput[0], new[] { Asset("Assets/App/Auto/C.prefab") });

            Assert.AreEqual(ManualRegistrationState.Unregistered, rows[0].AccState);
        }

        // 画面の指定で展開したアドレスが行に入る。{Rule.Name} は空になる
        [Test]
        public void ManualRowsExpandAddressWithEmptyRuleName()
        {
            var spec = Spec("Assets/App", address: "{Folder.Top}/{Asset.Name}[{Rule.Name}]");
            var rows = Rows(spec, new RegistrationRuleInput[0], new[] { Asset("Assets/App/UI/Title.prefab") });

            Assert.AreEqual("UI/Title[]", rows[0].AccExpandedAddress);
        }

        // グループ名に使えない文字は、行のエラーになり、チェックできない
        [Test]
        public void ManualRowWithInvalidGroupNameHasErrorAndCannotCheck()
        {
            var spec = Spec("Assets/App", group: "{Folder.Relative}");
            var rows = Rows(spec, new RegistrationRuleInput[0], new[] { Asset("Assets/App/Sub/Deep/Title.prefab") });

            Assert.AreNotEqual(string.Empty, rows[0].AccError);
            Assert.IsFalse(rows[0].AccCanCheck);
            Assert.IsFalse(rows[0].AccChecked);
        }

        // チェックした未登録のアセットが、指定のグループ・アドレス・Label で追加される。新しいグループは指定の Template で作る
        [Test]
        public void ManualBuildAddsCheckedUnregistered()
        {
            var spec = Spec("Assets/App", address: "{Path.Relative}", group: "Manual", labels: new[] { "L" }, template_guid: "tpl");
            var env = Env();
            var rows = Rows(spec, new RegistrationRuleInput[0], new[] { Asset("Assets/App/Sub/A.prefab"), Asset("Assets/App/B.prefab") }, env);
            rows.First(r => r.AccAssetPath == "Assets/App/B.prefab").AccChecked = false;

            var plan = RegistrationPlanBuilder.BuildManual(spec, rows, env);

            var change = plan.AccChanges.Single();
            Assert.AreEqual("Assets/App/Sub/A.prefab", change.AccAssetPath);
            Assert.IsTrue(change.AccIsNewEntry);
            Assert.AreEqual("Sub/A", change.AccToAddress);
            Assert.AreEqual("Manual", change.AccToGroup);
            Assert.AreEqual("Manual", plan.AccNewGroups.Single().AccName);
            Assert.AreEqual("tpl", plan.AccNewGroups.Single().AccTemplateAssetGuid);
            CollectionAssert.AreEqual(new[] { "L" }, plan.AccNewLabels);
        }

        // チェックした登録済みのアセットは、グループの移動とアドレスの書き換えを行う。Label は追加だけで、外さない
        [Test]
        public void ManualBuildMovesRegisteredAndOnlyAddsLabels()
        {
            var spec = Spec("Assets/App", address: "{Asset.Name}", group: "Manual", labels: new[] { "New" });
            var env = Env(groups: new[] { DEFAULT_GROUP, "Manual", "Other" }, labels: new[] { "New", "Existing" });
            var asset = Asset("Assets/App/Title.prefab", registered: true, group: "Other", address: "Old", labels: new[] { "Existing" });
            var rows = Rows(spec, new RegistrationRuleInput[0], new[] { asset }, env);
            rows[0].AccChecked = true;

            var plan = RegistrationPlanBuilder.BuildManual(spec, rows, env);

            var change = plan.AccChanges.Single();
            Assert.IsFalse(change.AccIsNewEntry);
            Assert.AreEqual("Manual", change.AccToGroup);
            Assert.AreEqual("Title", change.AccToAddress);
            CollectionAssert.AreEqual(new[] { "New" }, change.AccLabelsToAdd);
            Assert.AreEqual(0, change.AccLabelsToRemove.Count);
            Assert.AreEqual(1, plan.CountMoved());
            // 手動の操作でのアドレスの書き換えは、警告しない
            Assert.AreEqual(0, plan.AccWarnings.Count);
        }

        // チェックを入れても、チェックできない行（自動登録・競合中など）は、計画に入らない
        [Test]
        public void ManualBuildSkipsRowsThatCannotBeChecked()
        {
            var spec = Spec("Assets/App");
            var rule = Rule("AutoRule", "Assets/App/Auto");
            var env = Env();
            var rows = Rows(spec, new[] { rule }, new[] { Asset("Assets/App/Auto/C.prefab"), Asset("Assets/App/D.prefab") }, env);
            foreach (var row in rows) row.AccChecked = true;

            var plan = RegistrationPlanBuilder.BuildManual(spec, rows, env);

            Assert.AreEqual("Assets/App/D.prefab", plan.AccChanges.Single().AccAssetPath);
        }

        // 手動一括登録でも、アドレスの重複を検出する
        [Test]
        public void ManualBuildDetectsDuplicateAddress()
        {
            var spec = Spec("Assets/App", address: "{Asset.Name}");
            var env = Env(entries: new[] { new RegistrationEntryInput("Assets/Other/Icon.png", "Icon", "G") });
            var rows = Rows(spec, new RegistrationRuleInput[0], new[] { Asset("Assets/App/Icon.png") }, env);

            var plan = RegistrationPlanBuilder.BuildManual(spec, rows, env);

            Assert.AreEqual(1, plan.AccDuplicates.Count);
        }
    }
}
