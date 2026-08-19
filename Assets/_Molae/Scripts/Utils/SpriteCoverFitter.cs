using UnityEngine;

namespace Molae.Utils
{
    /// <summary>
    /// 스프라이트를 카메라 뷰에 "cover" 방식으로 맞춘다 (CSS background-size: cover 와 동일).
    ///
    /// 왜 필요한가: Google Play "Level Up" 가이드라인은 세로 게임의 3:4 ~ 9:21 범위에서
    /// 레터박스/필러박스를 금지한다. 즉 1080x1440(3:4)부터 1080x2520(9:21)까지 검은 띠 없이
    /// 꽉 차야 한다. 배경 스프라이트를 고정 스케일로 두면 어느 한쪽에서 반드시 띠가 생긴다.
    ///
    /// contain이 아니라 cover인 이유: 남는 부분은 잘려도 되지만, 비는 부분은 절대 안 된다.
    /// 그래서 배경 아트는 실제 필요보다 크게 그려두고 가장자리에 중요한 정보를 두지 않는다.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(SpriteRenderer))]
    public class SpriteCoverFitter : MonoBehaviour
    {
        public enum FitMode
        {
            /// <summary>뷰를 완전히 덮는다. 넘치는 부분은 잘린다.</summary>
            Cover,
            /// <summary>가로 폭만 맞춘다. 세로는 스프라이트 비율대로.</summary>
            MatchWidth,
            /// <summary>세로 높이만 맞춘다.</summary>
            MatchHeight,
        }

        [SerializeField] private Camera targetCamera;
        [SerializeField] private FitMode mode = FitMode.Cover;
        [Tooltip("계산된 크기에 추가로 곱할 여유. 1.02 정도면 반올림 오차로 생기는 1px 틈을 막는다.")]
        [SerializeField] private float overscan = 1.02f;
        [Tooltip("에디터에서 매 프레임 갱신. Device Simulator로 비율을 바꿔볼 때 켠다.")]
        [SerializeField] private bool continuousUpdate = true;

        private SpriteRenderer _renderer;
        private Vector2Int _lastScreen;
        private float _lastOrthoSize;

        private void OnEnable()
        {
            _renderer = GetComponent<SpriteRenderer>();
            if (targetCamera == null) targetCamera = Camera.main;
            Fit();
        }

        private void LateUpdate()
        {
            if (!continuousUpdate && Application.isPlaying) return;

            var screen = new Vector2Int(Screen.width, Screen.height);
            float ortho = targetCamera != null ? targetCamera.orthographicSize : 0f;

            if (screen == _lastScreen && Mathf.Approximately(ortho, _lastOrthoSize)) return;

            _lastScreen = screen;
            _lastOrthoSize = ortho;
            Fit();
        }

        /// <summary>지금 즉시 맞춘다.</summary>
        public void Fit()
        {
            if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
            if (_renderer == null || _renderer.sprite == null) return;
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null || !targetCamera.orthographic) return;

            // 카메라가 보는 월드 크기
            float viewHeight = targetCamera.orthographicSize * 2f;
            float viewWidth = viewHeight * targetCamera.aspect;

            // 스프라이트 원본 월드 크기 (PPU 반영, 스케일 1 기준)
            Sprite sprite = _renderer.sprite;
            float spriteWidth = sprite.rect.width / sprite.pixelsPerUnit;
            float spriteHeight = sprite.rect.height / sprite.pixelsPerUnit;
            if (spriteWidth <= 0f || spriteHeight <= 0f) return;

            float scaleX = viewWidth / spriteWidth;
            float scaleY = viewHeight / spriteHeight;

            float scale;
            switch (mode)
            {
                case FitMode.MatchWidth: scale = scaleX; break;
                case FitMode.MatchHeight: scale = scaleY; break;
                default: scale = Mathf.Max(scaleX, scaleY); break; // Cover
            }

            scale *= overscan;
            transform.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
