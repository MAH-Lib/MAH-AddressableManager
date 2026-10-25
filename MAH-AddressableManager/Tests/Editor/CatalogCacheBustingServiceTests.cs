using System;
using NUnit.Framework;
using MAH.AddressableManager.Core.Initialize;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace MAH.AddressableManager.Tests.Editor
{
    internal sealed class CatalogCacheBustingServiceTests
    {
        private const long SESSION_VALUE = 638634528000000000L;

        private Func<IResourceLocation, string> _originalTransform;
        private CatalogCacheBustingService _service;

        [SetUp]
        public void SetUp()
        {
            // Addressables.InternalIdTransformFunc は全体で1つの static。他のテストに影響しないよう、前後で退避・復元する
            _originalTransform = Addressables.InternalIdTransformFunc;
            Addressables.InternalIdTransformFunc = null;
            _service = new CatalogCacheBustingService(SESSION_VALUE);
        }

        [TearDown]
        public void TearDown()
        {
            Addressables.InternalIdTransformFunc = _originalTransform;
        }

        // 空・null の InternalId も渡せるよう、ResourceLocationBase ではなく偽物を使う（ResourceLocationBase は空の id を受け付けない）
        private static IResourceLocation Location(string internal_id) => new FakeResourceLocation(internal_id);

        // ---- 変換の規則 ----

        // https の .hash には、起動時の値と連番のクエリが付く
        [Test]
        public void TransformAddsQueryToHttpsHash()
        {
            var result = _service.TransformInternalId(Location("https://cdn.example.com/Android/catalog_1.0.hash"));

            Assert.AreEqual($"https://cdn.example.com/Android/catalog_1.0.hash?t={SESSION_VALUE}-1", result);
        }

        // http（https ではない）の .hash も対象
        [Test]
        public void TransformAddsQueryToHttpHash()
        {
            var result = _service.TransformInternalId(Location("http://cdn.example.com/catalog.hash"));

            Assert.AreEqual($"http://cdn.example.com/catalog.hash?t={SESSION_VALUE}-1", result);
        }

        // 呼ぶたびに連番が進み、起動時の値は変わらない
        [Test]
        public void TransformIncrementsSerialEachCall()
        {
            var first = _service.TransformInternalId(Location("https://cdn.example.com/catalog.hash"));
            var second = _service.TransformInternalId(Location("https://cdn.example.com/catalog.hash"));
            var third = _service.TransformInternalId(Location("https://cdn.example.com/catalog.hash"));

            Assert.AreEqual($"https://cdn.example.com/catalog.hash?t={SESSION_VALUE}-1", first);
            Assert.AreEqual($"https://cdn.example.com/catalog.hash?t={SESSION_VALUE}-2", second);
            Assert.AreEqual($"https://cdn.example.com/catalog.hash?t={SESSION_VALUE}-3", third);
        }

        // 署名トークン等のクエリが付いた .hash も対象。InternalId にすでに ? があれば & で続ける
        [Test]
        public void TransformUsesAmpersandWhenQueryAlreadyExists()
        {
            var result = _service.TransformInternalId(Location("https://cdn.example.com/catalog.hash?token=abc"));

            Assert.AreEqual($"https://cdn.example.com/catalog.hash?token=abc&t={SESSION_VALUE}-1", result);
        }

        // .hash 以外（カタログ本体の .bin / .json・アセットバンドル等）と、http で始まらないもの（ローカル）は変換せず、同じ文字列をそのまま返す
        [TestCase("https://cdn.example.com/Android/catalog_1.0.bin")]
        [TestCase("https://cdn.example.com/Android/catalog_1.0.json")]
        [TestCase("https://cdn.example.com/Android/assets_all.bundle")]
        [TestCase("https://cdn.example.com/Android/remote_data.json")]
        [TestCase("https://cdn.example.com/data.json?v=catalog.hash")] // .hash はクエリの中にあるだけで、パスは .json
        [TestCase("http")] // 拡張子より短くても、範囲外にならず対象外になる
        [TestCase("Library/com.unity.addressables/aa/Android/catalog_1.0.hash")]
        [TestCase("file:///data/user/0/app/files/catalog.hash")]
        [TestCase("Assets/Prefabs/Hero.prefab")]
        [TestCase("")]
        public void TransformDoesNotChangeNonTarget(string internal_id)
        {
            var result = _service.TransformInternalId(Location(internal_id));

            Assert.AreSame(internal_id, result, "対象外は新しい文字列を作らないはず");
        }

        // null の InternalId も、例外にならず、そのまま返す
        [Test]
        public void TransformReturnsNullForNullInternalId()
        {
            Assert.IsNull(_service.TransformInternalId(Location(null)));
        }

        // 対象外の呼び出しでは、連番が進まない
        [Test]
        public void TransformNonTargetDoesNotAdvanceSerial()
        {
            _service.TransformInternalId(Location("https://cdn.example.com/catalog.bin"));

            var result = _service.TransformInternalId(Location("https://cdn.example.com/catalog.hash"));

            Assert.AreEqual($"https://cdn.example.com/catalog.hash?t={SESSION_VALUE}-1", result);
        }

        // 拡張子の判定は大文字・小文字を区別する（Ordinal）
        [Test]
        public void TransformIsCaseSensitive()
        {
            const string internal_id = "https://cdn.example.com/catalog.HASH";

            Assert.AreSame(internal_id, _service.TransformInternalId(Location(internal_id)));
        }

        // ---- 既存の変換関数との連結 ----

        // 既存の関数を先に通し、その結果が対象ならクエリを付ける
        [Test]
        public void ApplyChainsExistingTransformBeforeAddingQuery()
        {
            Addressables.InternalIdTransformFunc = location => location.InternalId.Replace("https://old-cdn", "https://new-cdn");

            _service.Apply();
            var result = Addressables.InternalIdTransformFunc(Location("https://old-cdn.example.com/catalog.hash"));

            Assert.AreEqual($"https://new-cdn.example.com/catalog.hash?t={SESSION_VALUE}-1", result);
        }

        // 既存の関数が対象外の InternalId に返した値は、そのまま返る
        [Test]
        public void ApplyKeepsExistingTransformResultForNonTarget()
        {
            Addressables.InternalIdTransformFunc = location => "custom/" + location.InternalId;

            _service.Apply();
            var result = Addressables.InternalIdTransformFunc(Location("Assets/a.prefab"));

            Assert.AreEqual("custom/Assets/a.prefab", result);
        }

        // 既存の関数が無いときも、設定すると変換される
        [Test]
        public void ApplySetsTransformWhenNoneExisted()
        {
            _service.Apply();

            var result = Addressables.InternalIdTransformFunc(Location("https://cdn.example.com/catalog.hash"));

            Assert.AreEqual($"https://cdn.example.com/catalog.hash?t={SESSION_VALUE}-1", result);
        }

        // Apply を2回呼んでも、二重に連結されない（クエリが1回だけ付く）
        [Test]
        public void ApplyTwiceDoesNotChainTwice()
        {
            _service.Apply();
            _service.Apply();

            var result = Addressables.InternalIdTransformFunc(Location("https://cdn.example.com/catalog.hash"));

            Assert.AreEqual($"https://cdn.example.com/catalog.hash?t={SESSION_VALUE}-1", result);
        }

        // ---- 復元 ----

        // 自分の設定した関数のままなら、設定前の関数へ戻す
        [Test]
        public void RestoreReturnsPreviousTransformWhenStillOurs()
        {
            Func<IResourceLocation, string> user_transform = location => location.InternalId;
            Addressables.InternalIdTransformFunc = user_transform;
            _service.Apply();

            _service.Restore();

            Assert.AreSame(user_transform, Addressables.InternalIdTransformFunc);
        }

        // 設定前に関数が無かった場合は、null に戻る
        [Test]
        public void RestoreReturnsNullWhenNoneExisted()
        {
            _service.Apply();

            _service.Restore();

            Assert.IsNull(Addressables.InternalIdTransformFunc);
        }

        // 利用者が後から別の関数に上書きしていたら、戻さず（上書きされた関数を消さず）、Warning を出す
        [Test]
        public void RestoreDoesNotTouchOverwrittenTransformAndWarns()
        {
            _service.Apply();
            Func<IResourceLocation, string> overwritten = location => "overwritten";
            Addressables.InternalIdTransformFunc = overwritten;

            using (var counter = new WarningCounter("InternalIdTransformFunc"))
            {
                _service.Restore();

                Assert.AreEqual(1, counter.AccCount);
            }
            Assert.AreSame(overwritten, Addressables.InternalIdTransformFunc);
        }

        // 上書きした関数がこちらの関数を内部で呼ぶ形でも、Restore の後に Awake より前に設定されていた元の関数が通り続ける
        [Test]
        public void RestoreKeepsPreviousTransformWhenOverwrittenFunctionChainsOurs()
        {
            Addressables.InternalIdTransformFunc = location => "https://cdn.example.com/catalog.hash"; // 元の関数
            _service.Apply();
            var ours = Addressables.InternalIdTransformFunc;
            Func<IResourceLocation, string> chaining = location => ours(location); // こちらの関数を呼ぶ形で上書き
            Addressables.InternalIdTransformFunc = chaining;

            using (var counter = new WarningCounter("InternalIdTransformFunc"))
            {
                _service.Restore();

                Assert.AreEqual(1, counter.AccCount);
            }
            var result = Addressables.InternalIdTransformFunc(Location("ignored"));

            Assert.AreSame(chaining, Addressables.InternalIdTransformFunc);
            Assert.AreEqual($"https://cdn.example.com/catalog.hash?t={SESSION_VALUE}-1", result, "元の関数を通ってから、クエリが付くはず");
        }

        // Apply していなければ、Restore は何もしない（重複した Manager など）
        [Test]
        public void RestoreWithoutApplyDoesNothing()
        {
            Func<IResourceLocation, string> user_transform = location => location.InternalId;
            Addressables.InternalIdTransformFunc = user_transform;

            using (var counter = new WarningCounter("InternalIdTransformFunc"))
            {
                _service.Restore();

                Assert.AreEqual(0, counter.AccCount);
            }
            Assert.AreSame(user_transform, Addressables.InternalIdTransformFunc);
        }

        // Restore を2回呼んでも、2回目は何もしない
        [Test]
        public void RestoreTwiceIsIdempotent()
        {
            _service.Apply();
            _service.Restore();
            Func<IResourceLocation, string> later = location => "later";
            Addressables.InternalIdTransformFunc = later;

            using (var counter = new WarningCounter("InternalIdTransformFunc"))
            {
                _service.Restore();

                Assert.AreEqual(0, counter.AccCount);
            }
            Assert.AreSame(later, Addressables.InternalIdTransformFunc);
        }
    }
}
