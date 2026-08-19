using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Molae.Core;
using Molae.Feedback;

namespace Molae.UI
{
    /// <summary>
    /// 결과 화면.
    ///
    /// 연출 타임라인 (총 약 1.85초, 화면을 탭하면 즉시 스킵):
    ///   0.00s  패널 진입          300ms  ease-out
    ///   0.30s  헤더 등장          225ms  overshoot
    ///   0.55s  스코어 카운트업    900ms  ease-out  → 끝에 펀치
    ///   1.65s  등급 도장 낙하     200ms  ease-in + 사운드 + 진동 + 셰이크
    ///   1.85s  별 stagger         120ms 간격
    ///   +      신기록 배지 / 버튼
    ///
    /// 근거: Material Design 모바일 표준 전환은 300ms(진입 225 / 퇴장 195)이고
    /// 400ms를 넘는 개별 트윈은 '너무 느리다'로 규정된다. NN/g도 100~400ms를 권장하며
    /// 500ms부터 답답하게 느껴진다고 본다. 카운트업만 정보 전달 목적으로 예외를 둔다.
    ///
    /// 빈 별은 반드시 회색으로 자리를 차지하게 남긴다 — 재도전 동기의 실제 원천은
    /// '획득한 별'이 아니라 '비어 있는 별'이다.
    /// </summary>
    public class ResultView : MonoBehaviour
    {
        [Header("연결")]
        [SerializeField] private GameDirector director;

        [Header("루트")]
        [SerializeField] private GameObject root;
        [SerializeField] private RectTransform panel;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("요소")]
        [SerializeField] private RectTransform header;
        [SerializeField] private TMP_Text headerLabel;
        [SerializeField] private TMP_Text scoreLabel;
        [SerializeField] private RectTransform gradeStamp;
        [SerializeField] private TMP_Text gradeLabel;
        [SerializeField] private TMP_Text gradeTitleLabel;
        [SerializeField] private TMP_Text statsLabel;
        [SerializeField] private TMP_Text nextGradeLabel;
        [SerializeField] private RectTransform newRecordBadge;
        [SerializeField] private RectTransform buttonRow;

        [Header("별")]
        [Tooltip("획득한 별. 인덱스 0부터 채워진다.")]
        [SerializeField] private RectTransform[] stars;
        [Tooltip("빈 별은 반드시 회색으로 자리를 남겨야 재도전 동기가 생긴다.")]
        [SerializeField] private Image[] starImages;
        [SerializeField] private Color starEarnedColor = new Color(0.988f, 0.839f, 0.553f, 1f); // #FCD68D
        [SerializeField] private Color starEmptyColor = new Color(0.286f, 0.302f, 0.494f, 0.45f);

        [Header("연출")]
        [SerializeField] private ScreenShaker screenShaker;
        [SerializeField] private float panelSlideFrom = -1200f;

        [Header("타이밍")]
        [SerializeField] private float tPanelIn = 0.300f;
        [SerializeField] private float tElementIn = 0.225f;
        [SerializeField] private float tCountUp = 0.900f;
        [SerializeField] private float tStarStagger = 0.120f;
        [SerializeField] private float tStampDrop = 0.200f;

        private const string BestScoreKey = "Molae.BestScore";

        private Coroutine _routine;
        private bool _skipRequested;
        private SessionResult _result;

        private void Reset() => director = FindFirstObjectByType<GameDirector>();

        private void Awake()
        {
            if (director == null) director = FindFirstObjectByType<GameDirector>();
            if (root != null) root.SetActive(false);
        }

        private void OnEnable()
        {
            if (director != null) director.SessionFinished += Show;
        }

        private void OnDisable()
        {
            if (director != null) director.SessionFinished -= Show;
        }

        public void Show(SessionResult result)
        {
            _result = result;
            _skipRequested = false;

            if (root != null) root.SetActive(true);
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(PlaySequence());
        }

        /// <summary>화면 탭 시 연출을 즉시 끝낸다.</summary>
        public void SkipAnimation() => _skipRequested = true;

        private IEnumerator PlaySequence()
        {
            int best = PlayerPrefs.GetInt(BestScoreKey, 0);
            bool isNewRecord = _result.Score > best;
            if (isNewRecord)
            {
                PlayerPrefs.SetInt(BestScoreKey, _result.Score);
                PlayerPrefs.Save();
                best = _result.Score;
            }

            PrepareInitialState(isNewRecord);

            // ── 0.00s 패널 진입
            yield return Animate(tPanelIn, t =>
            {
                float e = EaseOutCubic(t);
                if (panel != null)
                {
                    Vector2 pos = panel.anchoredPosition;
                    pos.y = Mathf.Lerp(panelSlideFrom, 0f, e);
                    panel.anchoredPosition = pos;
                }
                if (canvasGroup != null) canvasGroup.alpha = Mathf.Clamp01(t / 0.75f);
            });

            // ── 0.30s 헤더
            if (headerLabel != null)
            {
                headerLabel.text = _result.Cleared ? "종이 울렸다" : "걸렸다!";
            }
            yield return Animate(tElementIn, t =>
            {
                if (header != null) header.localScale = Vector3.one * Mathf.Lerp(0.8f, 1f, EaseOutBack(t));
            });

            // ── 0.55s 스코어 카운트업
            yield return Animate(tCountUp, t =>
            {
                float e = EaseOutCubic(t);
                if (scoreLabel != null)
                    scoreLabel.text = Mathf.FloorToInt(_result.Score * e).ToString("N0");
            });
            if (scoreLabel != null) scoreLabel.text = _result.Score.ToString("N0");

            // 카운트업 종료 펀치
            yield return Animate(0.25f, t =>
            {
                if (scoreLabel != null)
                    scoreLabel.rectTransform.localScale = Vector3.one * (1f + 0.20f * Mathf.Sin(t * Mathf.PI));
            });
            if (scoreLabel != null) scoreLabel.rectTransform.localScale = Vector3.one;

            yield return WaitScaled(0.15f);

            // ── 1.65s 등급 도장 낙하 (가속 낙하 → 착지 임팩트)
            if (gradeLabel != null) gradeLabel.text = _result.Grade.label;
            if (gradeTitleLabel != null) gradeTitleLabel.text = _result.Grade.title;

            yield return Animate(tStampDrop, t =>
            {
                if (gradeStamp != null) gradeStamp.localScale = Vector3.one * Mathf.Lerp(3f, 1f, EaseInQuad(t));
            });
            if (gradeStamp != null) gradeStamp.localScale = Vector3.one;

            screenShaker?.AddTrauma(0.35f);
            HapticService.Medium();

            // ── 1.85s 별 stagger
            int earned = Mathf.Clamp(_result.Grade.stars, 0, stars != null ? stars.Length : 0);
            for (int i = 0; i < earned; i++)
            {
                if (starImages != null && i < starImages.Length && starImages[i] != null)
                    starImages[i].color = starEarnedColor;

                int index = i;
                StartCoroutine(Animate(tElementIn, t =>
                {
                    if (stars != null && index < stars.Length && stars[index] != null)
                        stars[index].localScale = Vector3.one * Mathf.Lerp(2f, 1f, EaseOutBack(t));
                }));

                yield return WaitScaled(tStarStagger);
            }

            // ── 통계 / 신기록 / 버튼
            if (statsLabel != null)
            {
                statsLabel.text =
                    $"생존 {_result.SurvivedSeconds:0.0}초   최고 콤보 x{1f + _result.BestCombo * 0.25f:0.##}   응시율 {_result.GazeRatio * 100f:0}%";
            }

            if (nextGradeLabel != null) nextGradeLabel.text = BuildNextGradeHint();

            if (isNewRecord && newRecordBadge != null)
            {
                newRecordBadge.gameObject.SetActive(true);
                yield return Animate(tElementIn, t =>
                    newRecordBadge.localScale = Vector3.one * Mathf.Lerp(0f, 1f, EaseOutBack(t)));
            }

            if (buttonRow != null)
            {
                buttonRow.gameObject.SetActive(true);
                yield return Animate(tElementIn, t =>
                {
                    var group = buttonRow.GetComponent<CanvasGroup>();
                    if (group != null) group.alpha = EaseOutCubic(t);
                });
            }

            _routine = null;
        }

        /// <summary>
        /// 목표 구배 효과: 다음 등급까지 남은 거리를 %가 아니라 구체 단위로 보여준다.
        /// 스탬프 카드 연구에서 목표에 가까울수록 재도전 간격이 약 20% 단축됐다.
        /// </summary>
        private string BuildNextGradeHint()
        {
            if (_result.Cleared) return "완주! 더 높은 점수에 도전해보자";

            GradeThreshold[] grades = director.Config.Grades;
            if (grades == null) return string.Empty;

            // 현재 등급보다 한 단계 위의 최소 생존 시간을 찾는다.
            float best = float.MaxValue;
            string bestLabel = null;
            for (int i = 0; i < grades.Length; i++)
            {
                if (grades[i].minSeconds > _result.SurvivedSeconds && grades[i].minSeconds < best)
                {
                    best = grades[i].minSeconds;
                    bestLabel = grades[i].label;
                }
            }

            if (bestLabel == null) return string.Empty;

            float delta = best - _result.SurvivedSeconds;
            return $"{bestLabel}등급까지 {delta:0.0}초";
        }

        private void PrepareInitialState(bool isNewRecord)
        {
            if (canvasGroup != null) canvasGroup.alpha = 0f;
            if (panel != null)
            {
                Vector2 pos = panel.anchoredPosition;
                pos.y = panelSlideFrom;
                panel.anchoredPosition = pos;
            }

            if (header != null) header.localScale = Vector3.one * 0.8f;
            if (scoreLabel != null) scoreLabel.text = "0";
            if (gradeStamp != null) gradeStamp.localScale = Vector3.one * 3f;
            if (newRecordBadge != null) newRecordBadge.gameObject.SetActive(false);

            if (buttonRow != null)
            {
                var group = buttonRow.GetComponent<CanvasGroup>();
                if (group != null) group.alpha = 0f;
            }

            // 빈 별을 회색으로 미리 자리 잡아둔다.
            if (starImages != null)
            {
                for (int i = 0; i < starImages.Length; i++)
                {
                    if (starImages[i] != null) starImages[i].color = starEmptyColor;
                }
            }

            if (stars != null)
            {
                for (int i = 0; i < stars.Length; i++)
                {
                    if (stars[i] != null) stars[i].localScale = Vector3.one;
                }
            }
        }

        // ── 트윈 유틸 (Time.timeScale = 0 에서도 동작해야 하므로 unscaled 사용) ──

        private IEnumerator Animate(float duration, System.Action<float> onStep)
        {
            if (duration <= 0f)
            {
                onStep(1f);
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (_skipRequested)
                {
                    onStep(1f);
                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;
                onStep(Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            onStep(1f);
        }

        private IEnumerator WaitScaled(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                if (_skipRequested) yield break;
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        // Material Design deceleration curve ≈ cubic-bezier(0.0, 0.0, 0.2, 1)
        private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
        private static float EaseInQuad(float t) => t * t;

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        // ── 버튼 핸들러 ──

        public void OnRetryPressed()
        {
            if (root != null) root.SetActive(false);
            director?.Retry();
        }

        public void OnHomePressed()
        {
            if (root != null) root.SetActive(false);
        }
    }
}
