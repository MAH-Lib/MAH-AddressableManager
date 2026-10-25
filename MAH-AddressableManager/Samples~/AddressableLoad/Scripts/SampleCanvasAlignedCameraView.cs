using UnityEngine;

namespace MAH.AddressableManager.Samples
{
    /// <summary>
    /// 3D のオブジェクトを、UI（Canvas）と同じ座標で置けるようにするカメラの設定。Prefab シーンのカメラに付ける。
    /// <para>
    /// カメラを正射影（Orthographic）にして、Canvas の左上を、ワールドの原点 (0, 0) に合わせる。
    /// ワールドの 1 ユニットは Canvas 上の <see cref="_canvasUnitsPerWorldUnit"/> ユニットに固定する。
    /// そのため、Canvas 上の位置 (cx, cy)（cy は下向きが負）に置きたいオブジェクトは、
    /// ワールドの (cx / 係数, cy / 係数) に置けばよい。
    /// 縦長・横長のどちらの画面でも、UI に対する 3D オブジェクトの位置と大きさが変わらない。
    /// </para>
    /// <para>
    /// ⚠ これは動作確認用の配置のための仕組みで、モジュールの利用者が真似る必要はない。
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class SampleCanvasAlignedCameraView : MonoBehaviour
    {
        // カメラは、シーンの手前から奥（+Z）を見る。オブジェクト（Z=0）より手前に置く
        private const float CAMERA_DEPTH = -10f;

        [Header("基準にする Canvas")]
        [SerializeField] private Canvas _canvas;

        [Header("ワールド 1 ユニットが、Canvas 上で何ユニットか")]
        [SerializeField] private float _canvasUnitsPerWorldUnit = 120f;

        private Camera _camera;
        private int _lastScreenWidth;
        private int _lastScreenHeight;
        private float _lastScaleFactor;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        // CanvasScaler は画面サイズの変化に合わせて scaleFactor を更新するため、毎フレーム変化を見て合わせ直す
        private void LateUpdate()
        {
            var scale_factor = _canvas != null ? _canvas.scaleFactor : 1f;
            if (Screen.width == _lastScreenWidth && Screen.height == _lastScreenHeight && Mathf.Approximately(scale_factor, _lastScaleFactor)) return;

            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
            _lastScaleFactor = scale_factor;
            Fit(scale_factor);
        }

        private void Fit(float scale_factor)
        {
            // ワールド 1 ユニットが、画面上で何ピクセルか
            var pixels_per_unit = _canvasUnitsPerWorldUnit * scale_factor;
            if (pixels_per_unit <= 0f) return;

            var half_width = Screen.width * 0.5f / pixels_per_unit;
            var half_height = Screen.height * 0.5f / pixels_per_unit;

            _camera.orthographic = true;
            _camera.orthographicSize = half_height;

            // 画面の左上がワールドの (0, 0) になる位置。下方向がワールドの -Y
            transform.position = new Vector3(half_width, -half_height, CAMERA_DEPTH);
            transform.rotation = Quaternion.identity;
        }
    }
}
