using NUnit.Framework;
using MAH.AddressableManager;
using MAH.AddressableManager.Core.Loading;
using UnityEngine;

namespace MAH.AddressableManager.Tests.Editor
{
    internal sealed class AssetRegistryModelTests
    {
        // 同じキーでも型が違えば、別のエントリとして登録・取得できる
        [Test]
        public void AddEntrySameKeyDifferentTypeAreSeparateEntries()
        {
            var registry = new AssetRegistryModel();
            var key = AssetKey.FromPath("test/key");
            var sprite_key = new AssetEntryKey(key, typeof(Sprite));
            var texture_key = new AssetEntryKey(key, typeof(Texture2D));

            var sprite_entry = registry.AddEntry(sprite_key, new FakeAssetLoaderWorker(typeof(Sprite)));
            var texture_entry = registry.AddEntry(texture_key, new FakeAssetLoaderWorker(typeof(Texture2D)));

            Assert.IsTrue(registry.TryGetEntry(sprite_key, out var found_sprite));
            Assert.IsTrue(registry.TryGetEntry(texture_key, out var found_texture));
            Assert.AreSame(sprite_entry, found_sprite);
            Assert.AreSame(texture_entry, found_texture);
        }

        // 登録されているエントリが同じなら取り除き、true を返す
        [Test]
        public void RemoveEntryIfSameRegisteredEntryRemovesAndReturnsTrue()
        {
            var registry = new AssetRegistryModel();
            var entry_key = new AssetEntryKey(AssetKey.FromPath("test/key"), typeof(Sprite));
            var entry = registry.AddEntry(entry_key, new FakeAssetLoaderWorker(typeof(Sprite)));

            Assert.IsTrue(registry.RemoveEntryIfSame(entry_key, entry));
            Assert.IsFalse(registry.TryGetEntry(entry_key, out _));
        }

        // 同じキーで新しいエントリに置き換わった後は、古いエントリを渡しても、新しいエントリを取り除かない
        [Test]
        public void RemoveEntryIfSameReplacedEntryKeepsNewEntryAndReturnsFalse()
        {
            var registry = new AssetRegistryModel();
            var entry_key = new AssetEntryKey(AssetKey.FromPath("test/key"), typeof(Sprite));
            var old_entry = registry.AddEntry(entry_key, new FakeAssetLoaderWorker(typeof(Sprite)));
            Assert.IsTrue(registry.RemoveEntryIfSame(entry_key, old_entry)); // ロード失敗などで取り除かれた
            var new_entry = registry.AddEntry(entry_key, new FakeAssetLoaderWorker(typeof(Sprite))); // 同じキーで新しく作られた

            Assert.IsFalse(registry.RemoveEntryIfSame(entry_key, old_entry), "古いエントリでは取り除けないはず");
            Assert.IsTrue(registry.TryGetEntry(entry_key, out var found));
            Assert.AreSame(new_entry, found, "新しいエントリは残るはず");
        }

        // 未登録のキーでは何もせず false を返す
        [Test]
        public void RemoveEntryIfSameUnregisteredKeyReturnsFalse()
        {
            var registry = new AssetRegistryModel();
            var entry_key = new AssetEntryKey(AssetKey.FromPath("test/key"), typeof(Sprite));
            var entry = new AssetEntryModel(entry_key, new FakeAssetLoaderWorker(typeof(Sprite)));

            Assert.IsFalse(registry.RemoveEntryIfSame(entry_key, entry));
        }
    }
}
