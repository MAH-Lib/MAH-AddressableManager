using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MAH.AddressableManager
{
    /// <summary>
    /// Load 時に指定する寿命。この寿命の終了時点で解放されていないアセットは自動的に回収される（セーフティネット）。
    /// Manual / Scene / Owner の3種類。
    /// </summary>
    public readonly struct AssetLifetime
    {
        private readonly LifetimeKind _kind;
        private readonly Scene _scene;
        private readonly GameObject _owner;

        internal LifetimeKind AccKind => _kind;
        /// <summary> Scene 寿命のときに指定されたシーン（Manual/Owner のときは default） </summary>
        internal Scene AccScene => _scene;
        /// <summary> Owner 寿命のときに指定された GameObject（Manual/Scene のときは null） </summary>
        internal GameObject AccOwner => _owner;

        private AssetLifetime(LifetimeKind kind, Scene scene = default, GameObject owner = null)
        {
            _kind = kind;
            _scene = scene;
            _owner = owner;
        }

        /// <summary> 自動回収なし。明示的に Release を呼ぶまで、または Manager の破棄時のみ解放される </summary>
        public static AssetLifetime Manual => new(LifetimeKind.Manual);

        /// <summary> 呼び出し時点のアクティブシーンがアンロードされた時点で回収される </summary>
        public static AssetLifetime Scene() => new(LifetimeKind.Scene, SceneManager.GetActiveScene());

        /// <summary> 指定したシーンがアンロードされた時点で回収される </summary>
        public static AssetLifetime Scene(Scene scene) => new(LifetimeKind.Scene, scene);

        /// <summary> 指定した GameObject が破棄された時点で回収される </summary>
        public static AssetLifetime Owner(GameObject go)
        {
            if (go == null)
                throw new ArgumentException("owner が null です。", nameof(go));
            return new AssetLifetime(LifetimeKind.Owner, owner: go);
        }

        /// <summary> 指定した Component の GameObject が破棄された時点で回収される </summary>
        public static AssetLifetime Owner(Component component)
        {
            if (component == null)
                throw new ArgumentException("owner が null です。", nameof(component));
            return Owner(component.gameObject);
        }
    }
}
