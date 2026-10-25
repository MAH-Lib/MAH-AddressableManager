using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using MAH.AddressableManager.Editor.Registration;

namespace MAH.AddressableManager.Tests.Editor
{
    internal sealed class RegistrationLogFormatterTests
    {
        private RegistrationLogOutput _output;

        [SetUp]
        public void SetUp()
        {
            _output = new RegistrationLogOutput();
        }

        private static RegistrationChange NewEntry(string path, string group = "UI", string address = "Addr", params string[] labels)
        {
            return new RegistrationChange(path, "guid", "R", true, string.Empty, group, string.Empty, address, labels.ToList(), new List<string>());
        }

        private static RegistrationChange Update(string path, string from_group, string to_group, string from_address, string to_address,
            string[] add = null, string[] remove = null)
        {
            return new RegistrationChange(path, "guid", "R", false, from_group, to_group, from_address, to_address,
                (add ?? new string[0]).ToList(), (remove ?? new string[0]).ToList());
        }

        // ---- まとめ ----

        // 差分がないときは、ログを出さない（警告などがなければ空）
        [Test]
        public void EmptyPlanProducesNoOutput()
        {
            RegistrationLogFormatter.Format(new RegistrationPlan(), RegistrationLogDetail.Summary, _output);

            Assert.IsTrue(_output.AccIsEmpty);
            Assert.AreEqual(RegistrationLogFormatter.NoChangeMessage, RegistrationLogFormatter.FormatSummary(new RegistrationPlan()));
        }

        // まとめは、件数が 0 の項目を省く
        [Test]
        public void SummaryListsOnlyNonZeroCounts()
        {
            var plan = new RegistrationPlan();
            plan.AccChanges.Add(NewEntry("Assets/A.prefab"));
            plan.AccChanges.Add(NewEntry("Assets/B.prefab"));
            plan.AccChanges.Add(NewEntry("Assets/C.prefab"));
            plan.AccChanges.Add(Update("Assets/D.prefab", "Old", "New", "d", "d"));

            var summary = RegistrationLogFormatter.FormatSummary(plan);

            StringAssert.Contains("3 件を登録", summary);
            StringAssert.Contains("1 件のグループを移動", summary);
            StringAssert.DoesNotContain("アドレスを変更", summary);
            StringAssert.DoesNotContain("Label", summary);
        }

        // まとめだけのときは、Info が 1 行だけ。アセットごとの行は出ない
        [Test]
        public void SummaryDetailEmitsSingleInfoLine()
        {
            var plan = new RegistrationPlan();
            plan.AccChanges.Add(NewEntry("Assets/A.prefab"));
            plan.AccChanges.Add(NewEntry("Assets/B.prefab"));

            RegistrationLogFormatter.Format(plan, RegistrationLogDetail.Summary, _output);

            Assert.AreEqual(1, _output.AccInfos.Count);
            StringAssert.Contains("2 件を登録", _output.AccInfos[0]);
        }

        // ---- 1件ずつ ----

        // 1件ずつのときは、まとめに加えて、アセットごとの行が出る
        [Test]
        public void PerAssetDetailEmitsSummaryAndOneLinePerChange()
        {
            var plan = new RegistrationPlan();
            plan.AccChanges.Add(NewEntry("Assets/A.prefab", "UI", "Title", "Common"));
            plan.AccChanges.Add(Update("Assets/B.prefab", "Old", "UI", "b", "B", add: new[] { "New" }, remove: new[] { "Stale" }));

            RegistrationLogFormatter.Format(plan, RegistrationLogDetail.PerAsset, _output);

            Assert.AreEqual(3, _output.AccInfos.Count);
            StringAssert.Contains("Assets/A.prefab", _output.AccInfos[1]);
            StringAssert.Contains("[UI] Title", _output.AccInfos[1]);
            StringAssert.StartsWith("登録：", _output.AccInfos[1]);
            StringAssert.Contains("+Common", _output.AccInfos[1]);

            var update_line = _output.AccInfos[2];
            StringAssert.StartsWith("更新：", update_line);
            StringAssert.Contains("グループ：Old → UI", update_line);
            StringAssert.Contains("アドレス：b → B", update_line);
            StringAssert.Contains("+New", update_line);
            StringAssert.Contains("-Stale", update_line);
        }

        // 変わっていない項目は、更新の行に出ない
        [Test]
        public void UpdateLineOmitsUnchangedParts()
        {
            var line = RegistrationLogFormatter.FormatChange(Update("Assets/B.prefab", "UI", "UI", "b", "b", add: new[] { "New" }));

            StringAssert.DoesNotContain("グループ：", line);
            StringAssert.DoesNotContain("アドレス：", line);
            StringAssert.Contains("+New", line);
        }

        // ---- 競合 ----

        // 競合は、ルールの組ごとに1件のエラー。件数と、最初の 5 件のパス、残りの件数が出る
        [Test]
        public void ConflictShowsFirstFivePathsAndRemainder()
        {
            var conflict = new RegistrationConflict(new List<string> { "Child", "Parent" });
            for (var i = 1; i <= 8; i++) conflict.AccAssetPaths.Add($"Assets/App/{i}.prefab");
            var plan = new RegistrationPlan();
            plan.AccConflicts.Add(conflict);

            RegistrationLogFormatter.Format(plan, RegistrationLogDetail.Summary, _output);

            Assert.AreEqual(1, _output.AccErrors.Count);
            var text = _output.AccErrors[0];
            StringAssert.Contains("Child、Parent", text);
            StringAssert.Contains("8 件のアセット", text);
            for (var i = 1; i <= 5; i++) StringAssert.Contains($"Assets/App/{i}.prefab", text);
            StringAssert.DoesNotContain("Assets/App/6.prefab", text);
            StringAssert.Contains("ほか 3 件", text);
        }

        // 競合が 5 件以内なら、すべて出し、「ほか」は付かない
        [Test]
        public void ConflictWithinLimitShowsAllWithoutRemainder()
        {
            var conflict = new RegistrationConflict(new List<string> { "A", "B" });
            conflict.AccAssetPaths.Add("Assets/1.prefab");
            conflict.AccAssetPaths.Add("Assets/2.prefab");

            var text = RegistrationLogFormatter.FormatConflict(conflict);

            StringAssert.Contains("Assets/1.prefab", text);
            StringAssert.Contains("Assets/2.prefab", text);
            StringAssert.DoesNotContain("ほか", text);
            Assert.AreEqual(5, RegistrationLogFormatter.MAX_CONFLICT_PATHS);
        }

        // 競合の組が複数あれば、組ごとに別のエラーになる
        [Test]
        public void EachConflictPairIsSeparateError()
        {
            var plan = new RegistrationPlan();
            var first = new RegistrationConflict(new List<string> { "A", "B" });
            first.AccAssetPaths.Add("Assets/X/1.prefab");
            var second = new RegistrationConflict(new List<string> { "C", "D" });
            second.AccAssetPaths.Add("Assets/Y/1.prefab");
            plan.AccConflicts.Add(first);
            plan.AccConflicts.Add(second);

            RegistrationLogFormatter.Format(plan, RegistrationLogDetail.Summary, _output);

            Assert.AreEqual(2, _output.AccErrors.Count);
        }

        // ---- 手動一括登録の確認ダイアログ ----

        // 確認の文言：登録とグループの移動の件数、新しく作るグループ・Label。重複がなければ「登録しますか？」
        [Test]
        public void ConfirmationShowsCountsNewGroupsAndLabels()
        {
            var plan = new RegistrationPlan();
            plan.AccChanges.Add(NewEntry("Assets/A.prefab"));
            plan.AccChanges.Add(NewEntry("Assets/B.prefab"));
            plan.AccChanges.Add(Update("Assets/C.prefab", "Old", "UI", "c", "c"));
            plan.AccNewGroups.Add(new RegistrationGroupRequest("UI", string.Empty));
            plan.AccNewGroups.Add(new RegistrationGroupRequest("Item", string.Empty));
            plan.AccNewLabels.Add("Common");

            var text = RegistrationLogFormatter.FormatConfirmation(plan);

            StringAssert.Contains("2 件を登録し、1 件のグループを移動します", text);
            StringAssert.Contains("新しく作るグループ：UI、Item", text);
            StringAssert.Contains("新しく作る Label：Common", text);
            StringAssert.EndsWith("登録しますか？", text);
            StringAssert.DoesNotContain("重複", text);
        }

        // 新しいグループ・Label がなければ、その行は出ない
        [Test]
        public void ConfirmationOmitsNewGroupAndLabelLinesWhenNone()
        {
            var plan = new RegistrationPlan();
            plan.AccChanges.Add(NewEntry("Assets/A.prefab"));

            var text = RegistrationLogFormatter.FormatConfirmation(plan);

            StringAssert.DoesNotContain("新しく作る", text);
        }

        // アドレスの重複があれば、そのアドレスを出して、そのまま登録するかを尋ねる
        [Test]
        public void ConfirmationWarnsDuplicateAndAsksToProceed()
        {
            var plan = new RegistrationPlan();
            plan.AccChanges.Add(NewEntry("Assets/A.prefab"));
            plan.AccDuplicates.Add(new RegistrationDuplicate("Icon", new List<RegistrationEntryInput>()));
            plan.AccDuplicates.Add(new RegistrationDuplicate("Title", new List<RegistrationEntryInput>()));

            var text = RegistrationLogFormatter.FormatConfirmation(plan);

            StringAssert.Contains("アドレスが重複します（2 件）：Icon、Title", text);
            StringAssert.EndsWith("そのまま登録しますか？", text);
        }

        // ---- 重複・警告・エラー ----

        // アドレスの重複は、アドレスと、アセットとグループを出す
        [Test]
        public void DuplicateShowsAddressAndEntries()
        {
            var plan = new RegistrationPlan();
            plan.AccDuplicates.Add(new RegistrationDuplicate("Icon", new List<RegistrationEntryInput>
            {
                new RegistrationEntryInput("Assets/A/Icon.png", "Icon", "UI"),
                new RegistrationEntryInput("Assets/B/Icon.png", "Icon", "Item"),
            }));

            RegistrationLogFormatter.Format(plan, RegistrationLogDetail.Summary, _output);

            Assert.AreEqual(1, _output.AccErrors.Count);
            StringAssert.Contains("Icon", _output.AccErrors[0]);
            StringAssert.Contains("Assets/A/Icon.png（UI）", _output.AccErrors[0]);
            StringAssert.Contains("Assets/B/Icon.png（Item）", _output.AccErrors[0]);
        }

        // 競合・重複・警告は、ログの細かさに関係なく出る。差分がなくても出る
        [TestCase(RegistrationLogDetail.Summary)]
        [TestCase(RegistrationLogDetail.PerAsset)]
        public void ProblemsAreReportedRegardlessOfDetail(RegistrationLogDetail detail)
        {
            var plan = new RegistrationPlan();
            plan.AccWarnings.Add("警告A");
            plan.AccErrors.Add("エラーB");
            var conflict = new RegistrationConflict(new List<string> { "A", "B" });
            conflict.AccAssetPaths.Add("Assets/1.prefab");
            plan.AccConflicts.Add(conflict);
            plan.AccDuplicates.Add(new RegistrationDuplicate("Icon", new List<RegistrationEntryInput>()));

            RegistrationLogFormatter.Format(plan, detail, _output);

            Assert.AreEqual(0, _output.AccInfos.Count);
            Assert.AreEqual(1, _output.AccWarnings.Count);
            StringAssert.Contains("警告A", _output.AccWarnings[0]);
            Assert.AreEqual(3, _output.AccErrors.Count);
            StringAssert.Contains("エラーB", _output.AccErrors[0]);
        }

        private static RegistrationPlan PlanWithWarnings(int count)
        {
            var plan = new RegistrationPlan();
            for (var i = 0; i < count; i++) plan.AccWarnings.Add($"警告{i}");
            return plan;
        }

        // ---- 警告の要約 ----

        // 「まとめだけ」で警告が上限を超えたら、1件に要約する（件数・最初の5件・ほかの件数を含む）
        [Test]
        public void SummaryCollapsesWarningsOverLimit()
        {
            var plan = PlanWithWarnings(RegistrationLogFormatter.MAX_SUMMARY_WARNINGS + 3);

            RegistrationLogFormatter.Format(plan, RegistrationLogDetail.Summary, _output);

            Assert.AreEqual(1, _output.AccWarnings.Count);
            var warning = _output.AccWarnings[0];
            StringAssert.Contains($"{RegistrationLogFormatter.MAX_SUMMARY_WARNINGS + 3} 件", warning);
            StringAssert.Contains("警告0", warning);
            StringAssert.Contains($"警告{RegistrationLogFormatter.MAX_SUMMARY_WARNINGS - 1}", warning);
            StringAssert.DoesNotContain($"警告{RegistrationLogFormatter.MAX_SUMMARY_WARNINGS}", warning);
            StringAssert.Contains("ほか 3 件", warning);
        }

        // 上限ちょうど以下なら、1件ずつそのまま出す
        [Test]
        public void SummaryKeepsWarningsWithinLimit()
        {
            var plan = PlanWithWarnings(RegistrationLogFormatter.MAX_SUMMARY_WARNINGS);

            RegistrationLogFormatter.Format(plan, RegistrationLogDetail.Summary, _output);

            Assert.AreEqual(RegistrationLogFormatter.MAX_SUMMARY_WARNINGS, _output.AccWarnings.Count);
        }

        // 「1件ずつ」では、件数に関係なく全件出す
        [Test]
        public void PerAssetKeepsAllWarnings()
        {
            var plan = PlanWithWarnings(RegistrationLogFormatter.MAX_SUMMARY_WARNINGS + 3);

            RegistrationLogFormatter.Format(plan, RegistrationLogDetail.PerAsset, _output);

            Assert.AreEqual(RegistrationLogFormatter.MAX_SUMMARY_WARNINGS + 3, _output.AccWarnings.Count);
        }

        // 要約するのは警告だけで、エラー・競合はそのまま出る
        [Test]
        public void WarningSummaryDoesNotAffectErrors()
        {
            var plan = PlanWithWarnings(RegistrationLogFormatter.MAX_SUMMARY_WARNINGS + 1);
            plan.AccErrors.Add("エラーB");

            RegistrationLogFormatter.Format(plan, RegistrationLogDetail.Summary, _output);

            Assert.AreEqual(1, _output.AccWarnings.Count);
            Assert.AreEqual(1, _output.AccErrors.Count);
        }

        // スキップの知らせに、件数が入る
        [Test]
        public void FormatSkippedIncludesCount()
        {
            StringAssert.Contains("7 件", RegistrationLogFormatter.FormatSkipped(7));
        }

        // Format は呼ぶたびに、前の内容を消す
        [Test]
        public void FormatClearsPreviousOutput()
        {
            var plan = new RegistrationPlan();
            plan.AccWarnings.Add("警告A");
            RegistrationLogFormatter.Format(plan, RegistrationLogDetail.Summary, _output);

            RegistrationLogFormatter.Format(new RegistrationPlan(), RegistrationLogDetail.Summary, _output);

            Assert.IsTrue(_output.AccIsEmpty);
        }
    }
}
