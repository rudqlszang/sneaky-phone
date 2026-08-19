using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Molae.UI
{
    /// <summary>
    /// 종료 확인 다이얼로그.
    ///
    /// 규격(Material 및 Android 관례):
    ///  - 긍정(계속하기)은 오른쪽, 부정(종료하기)은 그 왼쪽. 이 순서를 게임 내에서 절대 바꾸지 않는다.
    ///  - 파괴적 액션에 빨간색을 쓰지 않는다. 이 게임에서 '종료'는 위험한 행동이 아니다.
    ///  - 등장 200ms / 퇴장 150ms. Scale 애니메이션은 픽셀아트를 뭉개므로 금지, 위치 이동만 쓴다.
    ///  - 열려 있는 동안 timeScale = 0 이므로 모든 보간은 unscaledDeltaTime.
    /// </summary>
    public class ExitConfirmDialog : MonoBehaviour
    {
        [Header("루트")]
        [SerializeField] private GameObject root;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform panel;
        [SerializeField] private Image scrim;

        [Header("내용")]
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text bodyLabel;
        [SerializeField] private Button continueButton;
        [SerializeField] private Button quitButton;

        [Header("문구")]
        [SerializeField] private string titleText = "종료하시겠습니까?";
        [SerializeField, TextArea] private string bodyText = "지금 나가면 이번 교시 기록은 저장되지 않습니다.";

        [Header("타이밍")]
        [SerializeField] private float openSec = 0.20f;
        [SerializeField] private float closeSec = 0.15f;
        [Tooltip("픽셀아트가 뭉개지지 않도록 위치를 이 값의 배수로 스냅한다.")]
        [SerializeField] private float pixelGrid = 4f;

        [Header("스크림")]
        [SerializeField] private Color scrimColor = new Color(0.106f, 0.114f, 0.200f, 1f); // #1B1D33
        [SerializeField, Range(0f, 1f)] private float scrimAlpha = 0.32f;

        private bool _isOpen;
        private bool _isAnimating;
        private Coroutine _anim;
        private float _prevTimeScale = 1f;

        /// <summary>다이얼로그가 열려 있는지. 게임 로직이 이걸 보고 인지도 누적을 멈춘다.</summary>
        public bool IsOpen => _isOpen;

        /// <summary>닫힌 뒤 이 시간 동안은 인지도가 안 오른다(복귀 직후 억울한 적발 방지).</summary>
        public float InvulnerableRemaining { get; private set; }

        private void Awake()
        {
            if (titleLabel != null) titleLabel.text = titleText;
            if (bodyLabel != null) bodyLabel.text = bodyText;
            if (root != null) root.SetActive(false);

            if (continueButton != null) continueButton.onClick.AddListener(OnContinuePressed);
            if (quitButton != null) quitButton.onClick.AddListener(OnQuitPressed);
        }

        private void Update()
        {
            if (InvulnerableRemaining > 0f)
                InvulnerableRemaining = Mathf.Max(0f, InvulnerableRemaining - Time.unscaledDeltaTime);

            // Android 뒤로가기 = Escape
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_isAnimating) return;
                if (_isOpen) OnContinuePressed();
                else Open();
            }
        }

        public void Open()
        {
            if (_isOpen || _isAnimating) return;
            _isOpen = true;

            _prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            AudioListener.pause = true;

            if (root != null) root.SetActive(true);
            if (_anim != null) StopCoroutine(_anim);
            _anim = StartCoroutine(Animate(true));
        }

        /// <summary>'계속하기' — 취소하고 게임으로 돌아간다.</summary>
        public void OnContinuePressed()
        {
            if (!_isOpen || _isAnimating) return;
            if (_anim != null) StopCoroutine(_anim);
            _anim = StartCoroutine(CloseRoutine());
        }

        /// <summary>'종료하기' — 저장하고 앱을 종료한다.</summary>
        public void OnQuitPressed()
        {
            if (_isAnimating) return;

            PlayerPrefs.Save();
            Time.timeScale = _prevTimeScale;
            AudioListener.pause = false;

            // 예전엔 moveTaskToBack 으로 백그라운드에 보냈다. 그래서 아이콘을 다시 누르면
            // 죽지 않은 프로세스가 그대로 복귀해 "종료했는데 이어서 시작"으로 보였다.
            // 이제는 태스크까지 지우고 완전히 끝낸다.
            Molae.Core.QuitService.QuitApp();
        }

        private IEnumerator CloseRoutine()
        {
            yield return Animate(false);
            if (root != null) root.SetActive(false);
            _isOpen = false;

            Time.timeScale = _prevTimeScale;
            AudioListener.pause = false;

            // 복귀 직후 600ms 무적 — 다이얼로그를 닫자마자 걸리면 억울하다
            InvulnerableRemaining = 0.6f;
        }

        private IEnumerator Animate(bool opening)
        {
            _isAnimating = true;
            float dur = opening ? openSec : closeSec;
            float fromY = opening ? -96f : 0f;
            float toY = opening ? 0f : 64f;

            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / dur);
                // 등장은 감속, 퇴장은 가속
                float e = opening ? 1f - Mathf.Pow(1f - u, 3f) : u * u;

                if (panel != null)
                {
                    float y = Mathf.Lerp(fromY, toY, e);
                    var p = panel.anchoredPosition;
                    p.y = Mathf.Round(y / pixelGrid) * pixelGrid;   // 픽셀 그리드 스냅
                    panel.anchoredPosition = p;
                }
                if (canvasGroup != null)
                    canvasGroup.alpha = opening ? Mathf.Clamp01(u * 2f) : 1f - u;
                if (scrim != null)
                {
                    var c = scrimColor;
                    c.a = (opening ? u : 1f - u) * scrimAlpha;
                    scrim.color = c;
                }
                yield return null;
            }

            if (canvasGroup != null) canvasGroup.alpha = opening ? 1f : 0f;
            _isAnimating = false;
            _anim = null;
        }
    }
}
