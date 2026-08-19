using System.Collections;
using TMPro;
using UnityEngine;
using Molae.Core;

namespace Molae.Feedback
{
    /// <summary>
    /// 콤보가 오를 때 화면 가운데에 "COMBO x2!" 를 터뜨린다.
    ///
    /// 상단 HUD 의 작은 숫자는 플레이어가 못 본다 — 시선이 화면 하단 폰에 고정돼 있고,
    /// 주변시는 응시점 2° 밖에서 작은 텍스트를 읽지 못하기 때문이다.
    /// 그래서 보상 피드백은 크게, 가운데에, 짧게 터뜨려야 주변시로도 인지된다.
    ///
    /// 대신 오래 남기지 않는다(0.6초). 플레이 화면을 가리면 안 된다.
    /// </summary>
    public class ComboPopup : MonoBehaviour
    {
        [Header("연결")]
        [SerializeField] private GameDirector director;
        [SerializeField] private TMP_Text label;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform rect;

        [Header("연출")]
        [Tooltip("표시 시간(초). 길면 시야를 가린다.")]
        [SerializeField] private float lifetime = 0.60f;
        [Tooltip("튀어오르는 정도.")]
        [SerializeField] private float punchScale = 0.45f;
        [Tooltip("위로 떠오르는 거리(화면 px).")]
        [SerializeField] private float riseDistance = 60f;

        [Header("색")]
        [SerializeField] private Color lowColor = new Color(0.988f, 0.839f, 0.553f);   // #FCD68D
        [SerializeField] private Color highColor = new Color(0.941f, 0.655f, 0.408f);  // #F0A868
        [Tooltip("이 콤보 단계에서 색이 최대로 뜨거워진다.")]
        [SerializeField] private int comboForMaxColor = 8;

        private Coroutine _anim;
        private Vector2 _basePos;
        private int _lastStep = -1;

        private void Awake()
        {
            if (director == null) director = FindFirstObjectByType<GameDirector>();
            if (rect != null) _basePos = rect.anchoredPosition;
            if (canvasGroup != null) canvasGroup.alpha = 0f;
        }

        private void OnEnable()
        {
            if (director != null && director.Score != null)
                director.Score.Combo.StepUp += OnStepUp;
        }

        private void OnDisable()
        {
            if (director != null && director.Score != null)
                director.Score.Combo.StepUp -= OnStepUp;
        }

        private void Update()
        {
            // 이벤트를 놓치는 경우(씬 재구성 등)에 대비한 폴백
            if (director == null || director.Score == null) return;
            int step = director.Score.ComboStep;
            if (step > _lastStep && step > 0) OnStepUp(step);
            _lastStep = step;
        }

        private void OnStepUp(int step)
        {
            if (step <= 0 || label == null) return;

            float mul = director != null && director.Score != null ? director.Score.Multiplier : 1f;
            label.text = $"COMBO  x{mul:0.##}";
            label.color = Color.Lerp(lowColor, highColor,
                comboForMaxColor <= 0 ? 0f : Mathf.Clamp01(step / (float)comboForMaxColor));

            if (_anim != null) StopCoroutine(_anim);
            _anim = StartCoroutine(Pop());
        }

        private IEnumerator Pop()
        {
            float t = 0f;
            while (t < lifetime)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / lifetime);

                // 앞 25%는 커지며 등장, 나머지는 떠오르며 사라진다
                float scale = u < 0.25f
                    ? Mathf.Lerp(0.5f, 1f + punchScale, u / 0.25f)
                    : Mathf.Lerp(1f + punchScale, 1f, (u - 0.25f) / 0.35f);
                scale = Mathf.Max(1f, scale);
                if (rect != null)
                {
                    rect.localScale = Vector3.one * scale;
                    rect.anchoredPosition = _basePos + new Vector2(0f, riseDistance * u);
                }
                if (canvasGroup != null)
                    canvasGroup.alpha = u < 0.15f ? u / 0.15f : 1f - Mathf.Pow((u - 0.15f) / 0.85f, 2f);

                yield return null;
            }
            if (canvasGroup != null) canvasGroup.alpha = 0f;
            if (rect != null) { rect.localScale = Vector3.one; rect.anchoredPosition = _basePos; }
            _anim = null;
        }
    }
}
