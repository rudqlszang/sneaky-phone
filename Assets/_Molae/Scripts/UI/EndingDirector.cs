using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Molae.UI
{
    /// <summary>
    /// 3라운드를 모두 깼을 때의 "학교 짱" 등극 엔딩.
    ///
    /// 총 5.4초 시퀀스. 화면을 탭하면 즉시 끝까지 건너뛴다.
    /// Time.timeScale 이 0이어도 돌아야 하므로 전부 unscaledDeltaTime 을 쓴다.
    ///
    /// 연출 순서(승격을 시각적으로 표현하는 표준 패턴):
    ///   플래시 → 배경 전환 → 암전 → 스포트라이트 → 주인공 등장 → 왕관 → 칭호 → 통계 → 별 → 버튼
    /// </summary>
    public class EndingDirector : MonoBehaviour
    {
        [Header("루트")]
        [SerializeField] private GameObject root;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("레이어")]
        [SerializeField] private Image flash;
        [SerializeField] private Image background;
        [SerializeField] private Image dim;
        [SerializeField] private RectTransform beamLeft;
        [SerializeField] private RectTransform beamRight;
        [SerializeField] private RectTransform hero;
        [SerializeField] private RectTransform crown;
        [SerializeField] private RectTransform ribbon;

        [Header("텍스트")]
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text statsLabel;
        [SerializeField] private TMP_Text scoreLabel;

        [Header("별")]
        [SerializeField] private RectTransform[] stars = new RectTransform[3];
        [SerializeField] private Image[] starImages = new Image[3];

        [Header("버튼")]
        [SerializeField] private RectTransform buttonRow;
        [SerializeField] private Button retryButton;
        [SerializeField] private Button quitButton;
        [Tooltip("메인화면으로 돌아가는 버튼.")]
        [SerializeField] private Button homeButton;

        [Header("연출")]
        [SerializeField] private Feedback.ScreenShaker screenShaker;
        [SerializeField] private ParticleSystem confettiLeft;
        [SerializeField] private ParticleSystem confettiRight;
        [SerializeField] private AudioSource stingerSource;
        [SerializeField] private AudioClip stingerClip;

        [Header("색")]
        [SerializeField] private Color paper = new Color(0.984f, 0.961f, 0.937f);
        [SerializeField] private Color night = new Color(0.106f, 0.114f, 0.200f);
        [SerializeField] private Color sun = new Color(0.988f, 0.839f, 0.553f);
        [SerializeField] private Color dusk = new Color(0.286f, 0.302f, 0.494f);

        [Header("엔딩 분기")]
        [SerializeField] private string goodTitle = "학교 짱";
        [SerializeField, TextArea] private string goodSubtitle = "아무도 눈치채지 못했다";
        [SerializeField] private string badTitle = "그냥 학생";
        [SerializeField, TextArea] private string badSubtitle = "선생님은 다 알고 있었다";
        [SerializeField] private TMP_Text subtitleLabel;

        [Tooltip("좋은 엔딩일 때 왕관/리본/색종이를 보여준다.")]
        [SerializeField] private GameObject[] goodOnlyObjects;

        private bool _skip;
        private bool _good = true;
        private Coroutine _seq;

        // 에디터에서 배치한 원래 좌표. 연출은 "0으로 이동"이 아니라
        // "배치된 자리로 복귀"여야 한다. 예전엔 목적지를 0으로 하드코딩해서
        // 버튼줄(y=220)·주인공(y=560)·왕관(y=900)이 전부 화면 바닥에 처박혔다.
        private Vector2 _baseHero, _baseCrown, _baseButtons;
        private bool _basesCaptured;

        private void Awake()
        {
            CaptureBases();
            if (root != null) root.SetActive(false);
        }

        /// <summary>
        /// 배치 좌표를 한 번만 저장한다. Play() 가 여러 번 호출돼도
        /// 이전 연출이 남긴 좌표를 기준으로 삼지 않도록 플래그로 막는다.
        /// </summary>
        private void CaptureBases()
        {
            if (_basesCaptured) return;
            _basesCaptured = true;
            if (hero != null) _baseHero = hero.anchoredPosition;
            if (crown != null) _baseCrown = crown.anchoredPosition;
            if (buttonRow != null) _baseButtons = buttonRow.anchoredPosition;

            // 엔딩은 SafeAreaRoot 밖에 있어서 제스처 바에 버튼이 먹힌다.
            // 하단 인셋만큼 버튼줄을 캔버스 단위로 밀어 올린다.
            if (buttonRow != null)
            {
                var canvas = buttonRow.GetComponentInParent<Canvas>();
                float scale = canvas != null ? canvas.scaleFactor : 1f;
                if (scale > 0f)
                {
                    float bottomInsetPx = Screen.safeArea.y;
                    _baseButtons.y += bottomInsetPx / scale;
                }
            }
        }

        /// <summary>
        /// 엔딩을 재생한다.
        /// </summary>
        /// <param name="totalScore">최종 총점</param>
        /// <param name="maxScore">이론상 최대 점수</param>
        /// <param name="good">좋은 엔딩 조건 충족 여부 (총점이 최대의 90% 이상)</param>
        public void Play(int totalScore, int maxScore, bool good, int retries, int roundsCleared)
        {
            _good = good;
            if (root != null) root.SetActive(true);
            _skip = false;
            if (_seq != null) StopCoroutine(_seq);
            _seq = StartCoroutine(Sequence(totalScore, maxScore, retries, roundsCleared));
        }

        /// <summary>화면 탭 시 호출. 남은 연출을 즉시 끝낸다.</summary>
        public void Skip() => _skip = true;

        private IEnumerator Sequence(int totalScore, int maxScore, int retries, int roundsCleared)
        {
            Prepare();

            // 나쁜 엔딩에서는 왕관·리본·색종이를 아예 띄우지 않는다.
            if (goodOnlyObjects != null)
                foreach (var g in goodOnlyObjects) if (g != null) g.SetActive(_good);

            if (titleLabel != null) titleLabel.color = _good ? sun : new Color(0.776f, 0.624f, 0.647f);
            if (subtitleLabel != null) subtitleLabel.text = _good ? goodSubtitle : badSubtitle;

            if (_good && stingerSource != null && stingerClip != null)
                stingerSource.PlayOneShot(stingerClip);

            // 0.00s — 화이트 플래시
            yield return Tween(0.06f, u => SetAlpha(flash, paper, u));
            yield return Tween(0.24f, u => SetAlpha(flash, paper, 1f - u));

            // 0.10s — 암전
            yield return Tween(0.20f, u => SetAlpha(dim, night, u * 0.75f));

            // 0.60s — 스포트라이트 개막
            yield return Tween(0.375f, u =>
            {
                float e = 1f - Mathf.Pow(1f - u, 3f);
                if (beamLeft != null) beamLeft.localScale = new Vector3(Mathf.Lerp(0.6f, 1f, e), e, 1f);
                if (beamRight != null) beamRight.localScale = new Vector3(Mathf.Lerp(0.6f, 1f, e), e, 1f);
            });

            // 0.85s — 주인공 등장 (오버슛)
            yield return Tween(0.30f, u =>
            {
                float e = EaseOutBack(u);
                if (hero != null)
                    hero.anchoredPosition = new Vector2(_baseHero.x, Mathf.Lerp(_baseHero.y + 640f, _baseHero.y, e));
            });

            // 1.15s — 착지 임팩트
            screenShaker?.AddTrauma(0.45f);
            Feedback.HapticService.Medium();

            if (_good)
            {
                // 1.15s — 왕관 낙하
                yield return Tween(0.35f, u =>
                {
                    if (crown == null) return;
                    // 가속 낙하 — 배치된 자리로 떨어진다
                    crown.anchoredPosition = new Vector2(_baseCrown.x, Mathf.Lerp(_baseCrown.y + 680f, _baseCrown.y, u * u));
                });

                // 1.50s — 왕관 착용 + 색종이 1차
                Burst(confettiLeft, 90);
                Burst(confettiRight, 90);
                yield return Tween(0.28f, u =>
                {
                    if (crown != null) crown.localScale = Vector3.one * (1f + 0.30f * Mathf.Sin(u * Mathf.PI));
                });
            }

            // 1.55s — 칭호
            if (titleLabel != null) titleLabel.text = _good ? goodTitle : badTitle;
            if (ribbon != null) ribbon.gameObject.SetActive(true);
            yield return Tween(0.225f, u =>
            {
                float s = Mathf.Lerp(3f, 1f, EaseOutBack(u));
                if (ribbon != null) ribbon.localScale = Vector3.one * s;
                if (titleLabel != null) titleLabel.rectTransform.localScale = Vector3.one * s;
            });

            // 2.35s — 통계
            if (statsLabel != null)
            {
                float ratio = maxScore <= 0 ? 0f : totalScore / (float)maxScore;
                statsLabel.text = $"{roundsCleared}교시 완주   달성률 {ratio * 100f:0}%";
                yield return Tween(0.225f, u => SetTextAlpha(statsLabel, u));
            }

            // 2.55s — 점수 카운트업
            if (scoreLabel != null)
            {
                yield return Tween(0.90f, u =>
                {
                    float e = 1f - Mathf.Pow(1f - u, 2f);
                    scoreLabel.text = Mathf.FloorToInt(totalScore * e).ToString("N0");
                });
                scoreLabel.text = totalScore.ToString("N0");
            }

            // 4.00s — 별 등급
            // 별은 "몇 교시를 깼나"가 아니라 "얼마나 잘했나"로 준다.
            //   90% 이상 → 별 3 (좋은 엔딩)
            //   70~89%   → 별 2
            //   그 이하   → 별 1
            float achieved = maxScore <= 0 ? 0f : totalScore / (float)maxScore;
            int starCount = achieved >= 0.90f ? 3 : achieved >= 0.70f ? 2 : 1;

            for (int i = 0; i < stars.Length; i++)
            {
                if (starImages != null && i < starImages.Length && starImages[i] != null)
                    starImages[i].color = i < starCount ? sun : dusk;
                int idx = i;
                yield return Tween(0.20f, u =>
                {
                    if (stars[idx] == null) return;
                    float s = u < 0.6f ? Mathf.Lerp(0f, 1.25f, u / 0.6f) : Mathf.Lerp(1.25f, 1f, (u - 0.6f) / 0.4f);
                    stars[idx].localScale = Vector3.one * s;
                });
            }

            // 4.50s — 색종이 2차 (좋은 엔딩에서만)
            if (_good)
            {
                Burst(confettiLeft, 40);
                Burst(confettiRight, 40);
            }

            // 4.90s — 버튼
            if (buttonRow != null)
            {
                buttonRow.gameObject.SetActive(true);
                buttonRow.SetAsLastSibling();   // 항상 최상단에서 클릭을 받는다
                yield return Tween(0.225f, u =>
                    buttonRow.anchoredPosition = new Vector2(
                        _baseButtons.x, Mathf.Lerp(_baseButtons.y - 240f, _baseButtons.y, EaseOutBack(u))));
                buttonRow.anchoredPosition = _baseButtons;
            }

            EnableButtons();
            _seq = null;
        }

        /// <summary>
        /// 연출이 도중에 끊겨도 버튼만은 반드시 살아 있어야 한다.
        /// 여기서 막히면 플레이어가 게임을 빠져나갈 방법이 없어진다.
        /// </summary>
        private void EnableButtons()
        {
            if (buttonRow != null)
            {
                buttonRow.gameObject.SetActive(true);
                buttonRow.anchoredPosition = _baseButtons;
                buttonRow.SetAsLastSibling();
            }
            if (retryButton != null) retryButton.interactable = true;
            if (quitButton != null) quitButton.interactable = true;
            if (homeButton != null) homeButton.interactable = true;
        }

        private void Prepare()
        {
            if (canvasGroup != null) canvasGroup.alpha = 1f;
            SetAlpha(flash, paper, 0f);
            SetAlpha(dim, night, 0f);
            if (beamLeft != null) beamLeft.localScale = new Vector3(0.6f, 0f, 1f);
            if (beamRight != null) beamRight.localScale = new Vector3(0.6f, 0f, 1f);
            if (hero != null) hero.anchoredPosition = new Vector2(_baseHero.x, _baseHero.y + 640f);
            if (crown != null) { crown.anchoredPosition = new Vector2(_baseCrown.x, _baseCrown.y + 680f); crown.localScale = Vector3.one; }
            if (ribbon != null) { ribbon.gameObject.SetActive(false); ribbon.localScale = Vector3.one * 3f; }
            if (titleLabel != null) { titleLabel.text = ""; titleLabel.rectTransform.localScale = Vector3.one * 3f; }
            if (statsLabel != null) SetTextAlpha(statsLabel, 0f);
            if (scoreLabel != null) scoreLabel.text = "0";
            foreach (var s in stars) if (s != null) s.localScale = Vector3.zero;
            if (buttonRow != null)
            {
                buttonRow.gameObject.SetActive(false);
                buttonRow.anchoredPosition = new Vector2(_baseButtons.x, _baseButtons.y - 240f);
            }
            if (retryButton != null) retryButton.interactable = false;
            if (quitButton != null) quitButton.interactable = false;
            if (homeButton != null) homeButton.interactable = false;
        }

        private IEnumerator Tween(float dur, System.Action<float> step)
        {
            if (_skip || dur <= 0f) { step(1f); yield break; }
            float t = 0f;
            while (t < dur)
            {
                if (_skip) { step(1f); yield break; }
                t += Time.unscaledDeltaTime;
                step(Mathf.Clamp01(t / dur));
                yield return null;
            }
            step(1f);
        }

        private static void Burst(ParticleSystem ps, int count)
        {
            if (ps == null) return;
            ps.Emit(count);
        }

        private static void SetAlpha(Image img, Color c, float a)
        {
            if (img == null) return;
            c.a = a; img.color = c;
        }

        private static void SetTextAlpha(TMP_Text t, float a)
        {
            if (t == null) return;
            var c = t.color; c.a = a; t.color = c;
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        public void OnRetryPressed()
        {
            if (root != null) root.SetActive(false);
            var flow = FindFirstObjectByType<Core.GameFlow>();
            flow?.StartSession();
        }

        /// <summary>'메인화면' — 엔딩을 닫고 타이틀로 돌아간다.</summary>
        public void OnHomePressed()
        {
            if (root != null) root.SetActive(false);
            HomePressed?.Invoke();
        }

        /// <summary>메인화면으로 돌아가 달라는 요청. FlowUIBinder 가 구독한다.</summary>
        public event System.Action HomePressed;
    }
}
