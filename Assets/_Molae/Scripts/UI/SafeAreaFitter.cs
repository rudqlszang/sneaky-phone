using UnityEngine;

namespace Molae.UI
{
    /// <summary>
    /// 노치/펀치홀/제스처 바를 피해 UI를 안전 영역 안으로 밀어넣는다.
    ///
    /// Android 15(API 35)부터 edge-to-edge가 강제되어 게임 화면은 무조건 노치 뒤까지
    /// 그려진다. 따라서 SafeArea 처리는 앱이 직접 해야 한다.
    ///
    /// 중요: Player Settings의 "Render outside safe area"는 반드시 켠 채로 둔다(기본값).
    /// 끄면 Unity가 Player 윈도우 자체를 안전 영역 크기로 줄여버려서
    /// Screen.safeArea가 (0,0,width,height)가 되어 무의미해지고,
    /// 무엇보다 SeeSo 시선 좌표(물리 화면 픽셀 기준)와 Unity 좌표가 어긋난다.
    ///
    /// 배경 아트는 이 컴포넌트를 쓰지 말고 화면 전체를 채우게 두어야 검은 띠가 안 생긴다.
    /// (Google Play "Level Up" 가이드라인은 3:4 ~ 9:21 범위에서 레터박스를 금지한다)
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public class SafeAreaFitter : MonoBehaviour
    {
        [Header("적용 축")]
        [Tooltip("좌우 인셋을 적용한다. 세로 게임에서는 보통 꺼도 된다.")]
        [SerializeField] private bool applyHorizontal = true;
        [Tooltip("상하 인셋을 적용한다. 노치/제스처 바 대응의 핵심.")]
        [SerializeField] private bool applyVertical = true;

        [Header("추가 여백")]
        [Tooltip("안전 영역 위에 더 줄 여백(1080 기준 px).")]
        [SerializeField] private float extraPaddingTop;
        [SerializeField] private float extraPaddingBottom;

        [Header("디버그")]
        [Tooltip("에디터에서 매 프레임 갱신한다. Device Simulator로 확인할 때 켠다.")]
        [SerializeField] private bool refreshEveryFrame;

        private RectTransform _rect;
        private Canvas _canvas;
        private Rect _lastSafeArea = new Rect(0, 0, 0, 0);
        private Vector2Int _lastScreenSize;
        private ScreenOrientation _lastOrientation;

        private void Awake()
        {
            _rect = GetComponent<RectTransform>();
            _canvas = GetComponentInParent<Canvas>();
        }

        private void Start() => Apply(force: true);

        private void Update()
        {
            if (refreshEveryFrame) Apply(force: false);
        }

        /// <summary>
        /// 해상도/회전 변경 시 Unity가 자동으로 불러준다. Update 폴링보다 정확하고 싸다.
        /// </summary>
        private void OnRectTransformDimensionsChange()
        {
            if (_rect == null) return;
            Apply(force: false);
        }

        private void Apply(bool force)
        {
            if (_rect == null) _rect = GetComponent<RectTransform>();
            if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
            if (_canvas == null) return;

            Rect safeArea = Screen.safeArea;
            var screenSize = new Vector2Int(Screen.width, Screen.height);

            if (!force
                && safeArea == _lastSafeArea
                && screenSize == _lastScreenSize
                && Screen.orientation == _lastOrientation)
            {
                return;
            }

            _lastSafeArea = safeArea;
            _lastScreenSize = screenSize;
            _lastOrientation = Screen.orientation;

            // 분모는 Screen.width/height가 아니라 canvas.pixelRect.size 를 써야 정확하다.
            // Canvas Scaler 적용 후의 실제 픽셀 렉트이기 때문이다.
            Vector2 canvasSize = _canvas.pixelRect.size;
            if (canvasSize.x <= 0f || canvasSize.y <= 0f) return;

            float scale = canvasSize.x / 1080f;
            float padTop = extraPaddingTop * scale;
            float padBottom = extraPaddingBottom * scale;

            float xMin = applyHorizontal ? safeArea.xMin : 0f;
            float xMax = applyHorizontal ? safeArea.xMax : canvasSize.x;
            float yMin = applyVertical ? safeArea.yMin + padBottom : 0f;
            float yMax = applyVertical ? safeArea.yMax - padTop : canvasSize.y;

            var anchorMin = new Vector2(xMin / canvasSize.x, yMin / canvasSize.y);
            var anchorMax = new Vector2(xMax / canvasSize.x, yMax / canvasSize.y);

            // 잘못된 값이 들어오면 UI가 통째로 사라지므로 방어한다.
            if (float.IsNaN(anchorMin.x) || float.IsNaN(anchorMin.y) ||
                float.IsNaN(anchorMax.x) || float.IsNaN(anchorMax.y)) return;

            _rect.anchorMin = anchorMin;
            _rect.anchorMax = anchorMax;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }
    }
}
