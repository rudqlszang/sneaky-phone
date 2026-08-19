using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Molae.UI
{
    /// <summary>
    /// 게임 시작 전 타이틀 화면.
    ///
    /// 여기서 시선 캘리브레이션을 시작하고, 끝나면 게임으로 넘어간다.
    /// "시작하기"를 누르기 전까지는 카메라를 켜지 않아 첫 인상이 가볍다.
    ///
    /// 구조 주의: 컴포넌트는 항상 활성인 루트에, 실제 토글은 content 자식에.
    /// </summary>
    public class TitleScreen : MonoBehaviour
    {
        [Header("루트")]
        [SerializeField] private GameObject content;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("요소")]
        [SerializeField] private RectTransform logo;
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text subtitleLabel;
        [SerializeField] private TMP_Text bestScoreLabel;
        [SerializeField] private RectTransform startButtonRt;
        [SerializeField] private Button startButton;
        [SerializeField] private TMP_Text howToLabel;

        [Header("문구")]
        [SerializeField] private string titleText = "선생님 몰래폰";
        [SerializeField, TextArea] private string subtitleText = "눈으로 하는 스릴 게임";
        [SerializeField, TextArea]
        private string howToText =
            "선생님이 칠판을 볼 때만 폰을 보세요.\n" +
            "돌아봤는데 계속 보고 있으면 걸립니다.\n\n" +
            "3교시를 모두 버티면 학교 짱";

        [Header("로고 애니메이션")]
        [Tooltip("로고가 아주 살짝 흔들린다. 진폭이 크면 산만하다.")]
        [SerializeField] private float bobAmplitude = 10f;
        [SerializeField] private float bobHz = 0.55f;
        [SerializeField] private float tiltDegrees = 1.6f;

        [Header("기록판")]
        [Tooltip("기록판 제목. 기록이 하나도 없으면 통째로 숨긴다.")]
        [SerializeField] private GameObject recordsPanel;
        [SerializeField] private TMP_Text recordsTitle;

        /// <summary>'시작하기'를 눌렀을 때.</summary>
        public event System.Action StartPressed;

        private Vector2 _logoBase;
        private float _t;
        private bool _visible;

        private void Awake()
        {
            if (titleLabel != null) titleLabel.text = titleText;
            if (subtitleLabel != null) subtitleLabel.text = subtitleText;
            if (howToLabel != null) howToLabel.text = howToText;
            if (logo != null) _logoBase = logo.anchoredPosition;
            if (startButton != null) startButton.onClick.AddListener(OnStart);
        }

        private void Start() => Show();

        public void Show()
        {
            _visible = true;
            if (content != null) content.SetActive(true);
            if (canvasGroup != null) canvasGroup.alpha = 1f;
            if (startButton != null) startButton.interactable = true;

            RefreshRecords();

            StopAllCoroutines();
            StartCoroutine(Intro());
        }

        public void Hide()
        {
            _visible = false;
            StopAllCoroutines();
            StartCoroutine(FadeOut());
        }

        /// <summary>최고 기록을 다시 읽어 표시한다. 타이틀로 돌아올 때마다 호출된다.</summary>
        public void RefreshRecords()
        {
            int best = Molae.Core.ScoreBoard.Best;

            if (recordsTitle != null) recordsTitle.text = "최고 기록";
            if (bestScoreLabel != null)
            {
                bestScoreLabel.richText = true;
                bestScoreLabel.text = best > 0 ? best.ToString("N0") : "-";
                bestScoreLabel.alignment = TextAlignmentOptions.Center;
            }
            // 기록이 없으면 빈 판을 보여주지 않는다.
            if (recordsPanel != null) recordsPanel.SetActive(best > 0);
        }

        private IEnumerator Intro()
        {
            // 로고가 위에서 내려와 자리잡는다 (225ms, 오버슛)
            if (logo == null) yield break;
            float t = 0f;
            const float dur = 0.30f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / dur);
                const float c1 = 1.70158f, c3 = c1 + 1f;
                float e = 1f + c3 * Mathf.Pow(u - 1f, 3f) + c1 * Mathf.Pow(u - 1f, 2f);
                logo.anchoredPosition = _logoBase + new Vector2(0f, Mathf.Lerp(420f, 0f, e));
                yield return null;
            }
            logo.anchoredPosition = _logoBase;
        }

        private IEnumerator FadeOut()
        {
            float t = 0f;
            const float dur = 0.195f;   // 이탈 요소는 195ms
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                if (canvasGroup != null) canvasGroup.alpha = 1f - Mathf.Clamp01(t / dur);
                yield return null;
            }
            if (canvasGroup != null) canvasGroup.alpha = 0f;
            if (content != null) content.SetActive(false);
        }

        private void Update()
        {
            if (!_visible || logo == null) return;

            // 로고를 아주 살짝 띄운다. 정지 화면이 죽어 보이지 않게 하는 최소한의 움직임.
            _t += Time.unscaledDeltaTime;
            float y = Mathf.Sin(_t * bobHz * Mathf.PI * 2f) * bobAmplitude;
            float z = Mathf.Sin(_t * bobHz * Mathf.PI * 2f * 0.7f) * tiltDegrees;
            logo.anchoredPosition = _logoBase + new Vector2(0f, y);
            logo.localRotation = Quaternion.Euler(0f, 0f, z);
        }

        private void OnStart()
        {
            if (startButton != null) startButton.interactable = false;
            Hide();
            StartPressed?.Invoke();
        }
    }
}
