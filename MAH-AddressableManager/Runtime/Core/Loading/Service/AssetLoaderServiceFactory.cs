using System;
using System.Collections.Generic;
using System.Reflection;
using MAH.AddressableManager;

namespace MAH.AddressableManager.Core.Loading
{
    /// <summary> AddressableLoadingSettings のリトライ設定を渡して AssetLoaderService&lt;T&gt; を生成する </summary>
    internal sealed class AssetLoaderServiceFactory : IAssetLoaderServiceFactory
    {
        private static readonly MethodInfo CREATE_WORKER_METHOD =
            typeof(AssetLoaderServiceFactory).GetMethod(nameof(CreateWorker), BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly AddressableLoadingSettings _settings;
        // 実行時の型から生成するための delegate を、型ごとにキャッシュする（リフレクションのコストは型ごとの初回だけにする）
        private readonly Dictionary<Type, Func<AssetKey, IAssetLoaderWorker>> _createByType = new();

        internal AssetLoaderServiceFactory(AddressableLoadingSettings settings)
        {
            _settings = settings;
        }

        /// <summary> 単体の Load 用。リフレクションを通さず、そのまま生成する </summary>
        public IAssetLoaderWorker Create<T>(AssetKey key) where T : UnityEngine.Object
            => CreateWorker<T>(key);

        public IAssetLoaderWorker Create(AssetKey key, Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            if (!typeof(UnityEngine.Object).IsAssignableFrom(type))
                throw new ArgumentException($"{type.Name} は UnityEngine.Object の派生型ではありません。", nameof(type));

            if (!_createByType.TryGetValue(type, out var create))
            {
                // MakeGenericMethod と Delegate.CreateDelegate で作る。型引数は参照型なので IL2CPP でも共有ジェネリックとして動く。
                // Activator.CreateInstance と Expression.Compile は使わない（IL2CPP でのインタプリタ実行・ストリッピングの影響を避ける）
                var method = CREATE_WORKER_METHOD.MakeGenericMethod(type);
                create = (Func<AssetKey, IAssetLoaderWorker>)Delegate.CreateDelegate(typeof(Func<AssetKey, IAssetLoaderWorker>), this, method);
                _createByType[type] = create;
            }
            return create(key);
        }

        private IAssetLoaderWorker CreateWorker<T>(AssetKey key) where T : UnityEngine.Object
            => new AssetLoaderService<T>(key, _settings.AccMaxRetry, _settings.AccRetryDelayMs);
    }
}
