using System;
using System.Collections.Generic;
using MAH.AddressableManager;
using MAH.AddressableManager.Core.Loading;

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary> IAssetLoaderServiceFactory の偽実装。生成した FakeAssetLoaderWorker を (key, 型) ごとに記録する。 </summary>
    internal sealed class FakeAssetLoaderServiceFactory : IAssetLoaderServiceFactory
    {
        private readonly Dictionary<(AssetKey, Type), FakeAssetLoaderWorker> _createdWorkers = new();
        private readonly Dictionary<AssetKey, FakeAssetLoaderWorker> _latestWorkers = new();

        /// <summary> Create が呼ばれた回数（重複ロードが共有されていれば1回のはず） </summary>
        internal int CreateCallCount { get; private set; }

        public IAssetLoaderWorker Create<T>(AssetKey key) where T : UnityEngine.Object
            => Create(key, typeof(T));

        public IAssetLoaderWorker Create(AssetKey key, Type type)
        {
            CreateCallCount++;
            var worker = new FakeAssetLoaderWorker(type);
            _createdWorkers[(key, type)] = worker;
            _latestWorkers[key] = worker;
            return worker;
        }

        /// <summary> テストから、指定キーで最後に生成された Worker を取り出して Complete/Fail を呼ぶために使う </summary>
        internal FakeAssetLoaderWorker GetWorker(AssetKey key) => _latestWorkers[key];

        /// <summary> 同じキーを違う型で読んだ場合に、型を指定して Worker を取り出す </summary>
        internal FakeAssetLoaderWorker GetWorker(AssetKey key, Type type) => _createdWorkers[(key, type)];
    }
}
