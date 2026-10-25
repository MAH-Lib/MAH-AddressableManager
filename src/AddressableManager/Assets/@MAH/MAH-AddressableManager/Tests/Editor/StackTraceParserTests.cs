using NUnit.Framework;
using MAH.AddressableManager.Editor.Window;

namespace MAH.AddressableManager.Tests.Editor
{
    internal sealed class StackTraceParserTests
    {
        private const string ROOT = "/Users/me/proj";

        // 実環境（Unity 6000.3・Mono）の Environment.StackTrace から取った行
        private const string REAL_TRACE =
            "  at System.Environment.get_StackTrace () [0x00000] in <4ac0ee785b7843ea944af6fc649f4ee5>:0 \n" +
            "  at MAH.AddressableManager.Core.Loading.IssuedTicketRecord..ctor (MAH.AddressableManager.Core.Loading.AssetEntryKey entry_key, MAH.AddressableManager.Core.Loading.AssetEntryModel entry) [0x00024] in /Users/me/proj/src/AddressableManager/Assets/@MAH/AddressableManager/Runtime/Core/Loading/Model/IssuedTicketRecord.cs:50 \n" +
            "  at MAH.AddressableManager.Core.Loading.LoadingController.LoadAsync[T] (MAH.AddressableManager.AssetKey key, System.Threading.CancellationToken ct) [0x00115] in /Users/me/proj/src/AddressableManager/Assets/@MAH/AddressableManager/Runtime/Core/Loading/Controller/LoadingController.cs:92 \n" +
            "  at MAH.AddressableManager.Core.Loading.LoadingController.LoadAsync[T] (MAH.AddressableManager.AssetKey key, System.Threading.CancellationToken ct) [0x00000] in <cd378d3957c94c3788dce722895f7b43>:0 \n" +
            "  at MyGame.EnemySpawner.Spawn () [0x00027] in /Users/me/proj/Assets/Scripts/EnemySpawner.cs:74 \n" +
            "  at System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start[TStateMachine] (TStateMachine& stateMachine) [0x00000] in <4ac0ee785b7843ea944af6fc649f4ee5>:0 \n";

        // Mono 形式の行から、パス・行番号・表示用の名前を取り出せる
        [Test]
        public void ParseMonoLineExtractsPathLineAndLabel()
        {
            var frames = StackTraceParser.Parse("  at A.B.C.Method (System.Int32 x) [0x00012] in /Users/me/proj/Assets/Foo.cs:42 ", ROOT);

            Assert.AreEqual(1, frames.Length);
            Assert.IsTrue(frames[0].AccCanJump);
            Assert.AreEqual("/Users/me/proj/Assets/Foo.cs", frames[0].AccPath);
            Assert.AreEqual(42, frames[0].AccLine);
            Assert.AreEqual("A.B.C.Method  (Foo.cs:42)", frames[0].AccLabel);
        }

        // ジェネリックメソッドの名前（[T]）が欠けない
        [Test]
        public void ParseGenericMethodKeepsTypeParameterInLabel()
        {
            var frames = StackTraceParser.Parse("  at X.Y.LoadAsync[T] (X.Key key) [0x00115] in /p/Foo.cs:92 ", ROOT);

            Assert.AreEqual("X.Y.LoadAsync[T]  (Foo.cs:92)", frames[0].AccLabel);
        }

        // ファイルの情報が無い行（ハッシュ・filename unknown・行番号 0）は、ジャンプできない。表示はメソッド名だけ
        [TestCase("  at System.Environment.get_StackTrace () [0x00000] in <4ac0ee785b7843ea944af6fc649f4ee5>:0 ")]
        [TestCase("  at A.B () [0x00000] in <filename unknown>:0 ")]
        [TestCase("  at A.B () [0x00000] in /p/Foo.cs:0 ")]
        [TestCase("  at A.B () [0x00000] ")]
        [TestCase("  at A.B ()")]
        public void ParseLineWithoutFileInfoCannotJump(string line)
        {
            var frames = StackTraceParser.Parse(line, ROOT);

            Assert.AreEqual(1, frames.Length);
            Assert.IsFalse(frames[0].AccCanJump);
            Assert.AreEqual(string.Empty, frames[0].AccPath);
            Assert.IsFalse(frames[0].AccLabel.Contains(":"), "ファイルの情報が無いので、表示はメソッド名だけのはず");
        }

        // Windows のパス（ドライブレターの ':' を壊さない。区切りは / にそろえる）
        [Test]
        public void ParseWindowsPathKeepsDriveLetterAndNormalizesSeparators()
        {
            var frames = StackTraceParser.Parse("  at A.B () [0x00001] in C:\\Users\\me\\proj\\Assets\\Foo.cs:42 ", ROOT);

            Assert.IsTrue(frames[0].AccCanJump);
            Assert.AreEqual("C:/Users/me/proj/Assets/Foo.cs", frames[0].AccPath);
            Assert.AreEqual(42, frames[0].AccLine);
        }

        // プロジェクトからの相対パス（Assets/・Packages/・./Library/）は、プロジェクトのルートからの絶対パスにする
        [TestCase("Assets/Foo.cs", "/Users/me/proj/Assets/Foo.cs")]
        [TestCase("Packages/com.x/Foo.cs", "/Users/me/proj/Packages/com.x/Foo.cs")]
        [TestCase("./Library/PackageCache/com.x@abc/Foo.cs", "/Users/me/proj/Library/PackageCache/com.x@abc/Foo.cs")]
        public void ParseRelativePathBecomesAbsoluteFromProjectRoot(string relative, string expected)
        {
            var frames = StackTraceParser.Parse($"  at A.B () [0x00001] in {relative}:7 ", ROOT);

            Assert.AreEqual(expected, frames[0].AccPath);
            Assert.AreEqual(7, frames[0].AccLine);
        }

        // プロジェクトのルートが空のときは、相対パスのまま
        [Test]
        public void ParseRelativePathWithoutProjectRootKeepsPath()
        {
            var frames = StackTraceParser.Parse("  at A.B () [0x00001] in Assets/Foo.cs:7 ", string.Empty);

            Assert.AreEqual("Assets/Foo.cs", frames[0].AccPath);
        }

        // 空行は飛ばす。null・空文字は空の配列
        [Test]
        public void ParseSkipsBlankLinesAndHandlesNullOrEmpty()
        {
            Assert.AreEqual(0, StackTraceParser.Parse(null, ROOT).Length);
            Assert.AreEqual(0, StackTraceParser.Parse(string.Empty, ROOT).Length);
            Assert.AreEqual(2, StackTraceParser.Parse("  at A () [0x0] in /p/A.cs:1 \r\n\r\n  at B () [0x0] in /p/B.cs:2 \r\n", ROOT).Length);
        }

        // "at " の無い行・解析できない行も、例外にならず、ジャンプ不可のフレームとして残る
        [Test]
        public void ParseUnrecognizedLineIsKeptAsNonJumpFrame()
        {
            var frames = StackTraceParser.Parse("something unexpected", ROOT);

            Assert.AreEqual(1, frames.Length);
            Assert.IsFalse(frames[0].AccCanJump);
            Assert.AreEqual("something unexpected", frames[0].AccLabel);
        }

        // モジュール内（メソッドがモジュールの名前空間に属する・Library/PackageCache 配下）のフレームに印が付く。
        // 利用者側（Samples・Tests の名前空間、それ以外の名前空間）には付かない
        [Test]
        public void ParseMarksModuleFrames()
        {
            var frames = StackTraceParser.Parse(
                "  at MAH.AddressableManager.Core.Loading.LoadingController.LoadAsync[T] () [0x0] in /Users/me/proj/Assets/@MAH/AddressableManager/Runtime/Core/Foo.cs:1 \n" +
                "  at Cysharp.Threading.Tasks.Foo.Bar () [0x0] in /Users/me/proj/Library/PackageCache/com.cysharp.unitask@abc/Foo.cs:2 \n" +
                "  at MAH.AddressableManager.Samples.SampleLoader.Load () [0x0] in /Users/me/proj/Assets/@MAH/AddressableManager/Samples/Foo.cs:3 \n" +
                "  at MAH.AddressableManager.Tests.Editor.FooTests.Test () [0x0] in /Users/me/proj/Assets/@MAH/AddressableManager/Tests/Editor/Foo.cs:4 \n" +
                "  at MyGame.Foo.Bar () [0x0] in /Users/me/proj/Assets/Scripts/Foo.cs:5 \n", ROOT);

            Assert.IsTrue(frames[0].AccIsModule);
            Assert.IsTrue(frames[1].AccIsModule);
            Assert.IsFalse(frames[2].AccIsModule);
            Assert.IsFalse(frames[3].AccIsModule);
            Assert.IsFalse(frames[4].AccIsModule);
        }

        // 導入場所（Packages の embedded）に左右されない：モジュールの名前空間のフレームは、パスがどこでもモジュール内になる
        [Test]
        public void ParseMarksModuleFrameInEmbeddedPackage()
        {
            var frames = StackTraceParser.Parse(
                "  at MAH.AddressableManager.AddressableHandler.LoadAsync[T] () [0x0] in /Users/me/proj/Packages/com.mah.addressablemanager/Runtime/Core/Handler/AddressableHandler.cs:10 \n" +
                "  at MyGame.Foo.Bar () [0x0] in /Users/me/proj/Assets/Scripts/Foo.cs:5 \n", ROOT);

            Assert.IsTrue(frames[0].AccIsModule);
            Assert.AreEqual(1, StackTraceParser.FindFirstUserFrame(frames));
        }

        // 非同期のステートマシンのフレーム（型名に + や <> を含む）も、モジュール内と判定される
        [Test]
        public void ParseMarksAsyncStateMachineFrameAsModule()
        {
            var frames = StackTraceParser.Parse(
                "  at MAH.AddressableManager.Core.Loading.LoadingController+<LoadAsync>d__5`1[T].MoveNext () [0x00010] in /Users/me/proj/Assets/@MAH/AddressableManager/Runtime/Core/Loading/Controller/LoadingController.cs:92 \n", ROOT);

            Assert.IsTrue(frames[0].AccIsModule);
        }

        // ファイル名（フォルダを除く）を持つ。ジャンプできないフレームは空文字
        [Test]
        public void ParseExposesFileName()
        {
            var frames = StackTraceParser.Parse(
                "  at A.B () [0x0] in /Users/me/proj/Assets/Scripts/Foo.cs:7 \n" +
                "  at C.D () [0x0] in <abc>:0 \n", ROOT);

            Assert.AreEqual("Foo.cs", frames[0].AccFileName);
            Assert.AreEqual(string.Empty, frames[1].AccFileName);
        }

        // 実環境のトレースでは、モジュール内とファイル情報の無いフレームを飛ばして、利用者の最初のフレームが選ばれる
        [Test]
        public void FindFirstUserFrameSkipsModuleAndNonJumpFramesInRealTrace()
        {
            var frames = StackTraceParser.Parse(REAL_TRACE, ROOT);

            var index = StackTraceParser.FindFirstUserFrame(frames);

            Assert.AreEqual(4, index);
            Assert.AreEqual("/Users/me/proj/Assets/Scripts/EnemySpawner.cs", frames[index].AccPath);
            Assert.AreEqual(74, frames[index].AccLine);
        }

        // 利用者のフレームが無いときは -1
        [Test]
        public void FindFirstUserFrameWithoutUserFrameReturnsMinusOne()
        {
            var frames = StackTraceParser.Parse(
                "  at A () [0x0] in <abc>:0 \n  at MAH.AddressableManager.Core.B () [0x0] in /Users/me/proj/Assets/@MAH/AddressableManager/Runtime/Foo.cs:2 \n", ROOT);

            Assert.AreEqual(-1, StackTraceParser.FindFirstUserFrame(frames));
            Assert.AreEqual(-1, StackTraceParser.FindFirstUserFrame(StackTraceParser.Parse(null, ROOT)));
        }
    }
}
