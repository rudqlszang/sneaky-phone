using System.Collections;
using TMPro;
using UnityEngine;

namespace Molae.Feedback
{
    /// <summary>
    /// 점수 숫자 바운스 애니메이션.
    ///
    /// 점수가 오를 때마다 스케일을 튕기고, 콤보 단계가 높을수록 튕김이 커진다.
    /// 트윈 라이브러리(DOTween 등) 의존 없이 코루틴으로 구현했다.
    ///
    /// 타이밍 근거: 버튼/미세 피드백은 80~120ms, 개별 트윈은 400ms를 넘기지 않는다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class ScorePopAnimator : MonoBehaviour
    {
        [Header("타겟")]
        [SerializeField] private TMP_Text scoreLabel;

        [Header("바운스")]
        [Tooltip("기본 튕김 크기. 1.0 기준에서 이만큼 커졌다 돌아온다.")]
        [SerializeField, Range(0f, 0.6f)] private float basePunch = 0.14f;
        [Tooltip("콤보 최대일 때 추가되는 튕김 크기.")]
        [SerializeField, Range(0f, 0.6f)] private float comboPunchBonus = 0.16f;
        [Tooltip("이 콤보 단계에서 튕김이 최대가 된다.")]
        [SerializeField] private int comboForMaxPunch = 8;
        [Tooltip("튕김 지속시간(초).")]
        [SerializeField, Range(0.05f, 0.4f)] private float punchDuration = 0.12f;

        [Header("색")]
        [SerializeField] private Color normalColor = new Color(0.914f, 0.929f, 0.925f, 1f); // #E9EDEC chalk
        [SerializeField] private Color comboColor = new Color(0.988f, 0.839f, 0.553f, 1f);  // #FCD68D sun

        [Header("표시")]
        [Tooltip("천 단위 구분 기호 사용.")]
        [SerializeField] private bool useThousandsSeparator = true;

        private RectTransform _rect;
        private Vector3 _baseScale;
        private Coroutine _punchRoutine;
        private int _displayedScore;

        private void Awake()
        {
            _rect = GetComponent<RectTransform>();
            _baseScale = _rect.localScale;
            if (scoreLabel == null) scoreLabel = GetComponent<TMP_Text>();
            Render(0);
        }

        /// <summary>ScoreManager.Ticked 에 연결한다.</summary>
        public void OnScoreTicked(int gained, int total)
        {
            _displayedScore = total;
            Render(total);
            Punch(0);
        }

        /// <summary>콤보 단계를 반영해 튕긴다.</summary>
        public void Punch(int comboStep)
        {
            if (!isActiveAndEnabled) return;

            float t = comboForMaxPunch <= 0 ? 0f : Mathf.Clamp01(comboStep / (float)comboForMaxPunch);
            float amount = basePunch + comboPunchBonus * t;

            if (scoreLabel != null) scoreLabel.color = Color.Lerp(normalColor, comboColor, t);

            if (_punchRoutine != null) StopCoroutine(_punchRoutine);
            _punchRoutine = StartCoroutine(PunchRoutine(amount));
        }

        /// <summary>점수를 즉시 지정 값으로 표시한다(리셋용).</summary>
        public void SetScoreImmediate(int score)
        {
            _displayedScore = score;
            Render(score);
            _rect.localScale = _baseScale;
            if (scoreLabel != null) scoreLabel.color = normalColor;
        }

        private IEnumerator PunchRoutine(float amount)
        {
            float elapsed = 0f;

            while (elapsed < punchDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(elapsed / punchDuration);

                // 0 → 1 → 0 으로 한 번 부풀었다 돌아오는 곡선.
                float curve = Mathf.Sin(p * Mathf.PI);
                // ease-out 느낌을 주기 위해 앞쪽을 더 빠르게.
                curve *= 1f - p * 0.35f;

                _rect.localScale = _baseScale * (1f + amount * curve);
                yield return null;
            }

            _rect.localScale = _baseScale;
            _punchRoutine = null;
        }

        private void Render(int value)
        {
            if (scoreLabel == null) return;
            scoreLabel.text = useThousandsSeparator ? value.ToString("N0") : value.ToString();
        }
    }
}
