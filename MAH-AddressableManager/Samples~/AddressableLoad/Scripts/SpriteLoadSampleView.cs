using UnityEngine;
using UnityEngine.UI;

namespace MAH.AddressableManager.Samples
{
    /// <summary>
    /// 画像（Sprite）を読み込んで画面に表示するサンプル。
    /// 基本の Load → Release の流れは <see cref="AssetLoadSampleViewBase{T}"/> を参照。
    /// このクラスは「読み込んだ Sprite をどう使うか」だけを書いている。
    /// <para>
    /// 寿命ごとに別の画像を読み込み、別の Image に表示する。
    /// Release すると Manual 側の画像だけが消え、Scene 側の画像は別シーンへ遷移するまで残る。
    /// </para>
    /// </summary>
    public sealed class SpriteLoadSampleView : AssetLoadSampleViewBase<Sprite>
    {
        [Header("表示先（寿命ごと）")]
        [SerializeField] private Image _manualImage;
        [SerializeField] private Image _sceneImage;

        protected override void Awake()
        {
            base.Awake();

            // Sprite が無い状態で Image を有効にすると白い四角が出るため、読み込むまで隠す
            Hide(_manualImage);
            Hide(_sceneImage);
        }

        protected override void OnAssetLoaded(Sprite asset, SampleLifetimeKind kind)
        {
            var image = kind == SampleLifetimeKind.Manual ? _manualImage : _sceneImage;
            if (image == null) return;

            // 読み込んだ Sprite をそのまま使える。アセットは Release するまで（または自動回収されるまで）有効
            image.sprite = asset;
            image.enabled = true;
        }

        protected override void OnAssetReleased(Sprite asset, bool is_last)
        {
            // 保持中のチケットが残っている間は、アセットはまだ有効なので表示を残す（参照カウントが 0 になっていない）
            if (!is_last) return;

            // ⚠ 最後のチケットを Release するとアセットがアンロードされる。
            //    Image が参照したままだと、表示が壊れたり、解放済みのアセットを触ることになる。
            //    そのため Release の「前に」参照を外す（この処理は基底クラスが Release の直前に呼ぶ）。
            // Release の対象は Manual 用のアセットだけなので、Scene 側の画像は触らない
            Hide(_manualImage);
        }

        private static void Hide(Image image)
        {
            if (image == null) return;
            image.sprite = null;
            image.enabled = false;
        }
    }
}
