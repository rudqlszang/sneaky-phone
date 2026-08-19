using UnityEngine;
using UnityEngine.UI;

namespace Molae.Feedback
{
    /// <summary>
    /// 화면 가장자리 글로우 + 위험 비네트.
    ///
    /// 설계 근거: 플레이어의 시선은 화면 하단 폰에 고정돼 있고, 위험 신호는 주변시로만
    /// 들어온다. 주변시는 응시점 2° 밖에서 텍스트·작은 아이콘·색상 구분을 거의 못 하고
    /// 모션·명도 변화·큰 도형에만 반응한다. 그래서 경고는 반드시 화면 전체 명도 변화와
    /// 큰 면적의 펄스로 전달해야 한다.
    ///
    /// 색각이상 대응: 안전/위험을 색 하나로만 구분하지 않는다.
    /// 안전 = 따뜻한 앰버(#F0A868) + 밝음, 위험 = 차가운 남색(#1B1D33) + 어두움.
    /// 주황↔파랑 축은 3대 색각이상 유형 모두에서 구분되고, 명도차가 8.27:1이라
    /// 완전색맹에서도 밝기만으로 판별된다.
    /// </summary>
    public class EdgeGlowController : MonoBehaviour
    {
        [Header("타겟")]
        [Tooltip("콤보에 따라 밝아지는 가장자리 글로우 이미지(테두리 스프라이트 권장).")]
        [SerializeField] private Image comboGlow;
        [Tooltip("위험 상태에서 화면을 덮는 비네트 이미지.")]
        [SerializeField] private Image dangerVignette;
        [Tooltip("적발/게임오버 순간 번쩍이는 플래시 이미지.")]
        [SerializeField] private Image flash;

        [Header("색")]
        [SerializeField] private Color safeColor = new Color(0.941f, 0.655f, 0.408f, 1f);   // #F0A868 amber
        [SerializeField] private Color dangerColor = new Color(0.106f, 0.114f, 0.200f, 1f); // #1B1D33 night
        [SerializeField] private Color flashColor = new Color(0.980f, 0.961f, 0.937f, 1f);  // #FBF5EF paper

        [Header("콤보 글로우")]
        [Tooltip("이 콤보 단계에서 글로우가 최대 밝기가 된다.")]
        [SerializeField] private int comboForMaxGlow = 8;
        [SerializeField, Range(0f, 1f)] private float comboGlowMaxAlpha = 0.55f;
        [Tooltip("콤보가 쌓일수록 빨라지는 맥동 속도(Hz).")]
        [SerializeField] private float pulseHzAtMinCombo = 0.8f;
        [SerializeField] private float pulseHzAtMaxCombo = 2.4f;

        [Header("위험 비네트")]
        [SerializeField, Range(0f, 1f)] private float dangerMaxAlpha = 0.72f;
        [Tooltip("예고 단계에서 도달하는 비율. 위험 전에 미리 어두워지기 시작한다.")]
        [SerializeField, Range(0f, 1f)] private float telegraphAlphaRatio = 0.45f;
        [Tooltip("비네트가 목표값을 따라가는 속도.")]
        [SerializeField] private float vignetteLerpSpeed = 8f;

        [Header("플래시")]
        [Tooltip("플래시 지속시간(초). 50~100ms가 최적, 30ms는 지각 불가, 150ms는 흐려진다.")]
        [SerializeField, Range(0.03f, 0.2f)] private float flashDuration = 0.08f;

        private float _targetVignetteAlpha;
        private float _currentVignetteAlpha;
        private float _flashTimer;
        private float _comboNormalized;

        private void Awake()
        {
            if (comboGlow != null) comboGlow.color = WithAlpha(safeColor, 0f);
            if (dangerVignette != null) dangerVignette.color = WithAlpha(dangerColor, 0f);
            if (flash != null) flash.color = WithAlpha(flashColor, 0f);
        }

        /// <summary>매 프레임 호출. 콤보 단계와 선생님 위험도를 넘긴다.</summary>
        /// <param name="comboStep">현재 콤보 단계</param>
        /// <param name="isDangerous">정면 응시 중인지</param>
        /// <param name="isTelegraphing">예고 동작 중인지</param>
        /// <param name="turnAmount">선생님 회전량 0~1</param>
        public void Tick(int comboStep, bool isDangerous, bool isTelegraphing, float turnAmount)
        {
            _comboNormalized = comboForMaxGlow <= 0
                ? 0f
                : Mathf.Clamp01(comboStep / (float)comboForMaxGlow);

            UpdateComboGlow();

            if (isDangerous) _targetVignetteAlpha = dangerMaxAlpha;
            else if (isTelegraphing) _targetVignetteAlpha = dangerMaxAlpha * telegraphAlphaRatio * turnAmount;
            else _targetVignetteAlpha = 0f;

            _currentVignetteAlpha = Mathf.Lerp(
                _currentVignetteAlpha, _targetVignetteAlpha, 1f - Mathf.Exp(-vignetteLerpSpeed * Time.deltaTime));

            if (dangerVignette != null) dangerVignette.color = WithAlpha(dangerColor, _currentVignetteAlpha);

            UpdateFlash();
        }

        private void UpdateComboGlow()
        {
            if (comboGlow == null) return;

            if (_comboNormalized <= 0f)
            {
                comboGlow.color = WithAlpha(safeColor, 0f);
                return;
            }

            float hz = Mathf.Lerp(pulseHzAtMinCombo, pulseHzAtMaxCombo, _comboNormalized);
            // 0~1 사이를 오가는 맥동. 완전히 꺼지지 않도록 0.55~1.0 범위로 눌러준다.
            float pulse = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(Time.time * hz * Mathf.PI * 2f));
            comboGlow.color = WithAlpha(safeColor, comboGlowMaxAlpha * _comboNormalized * pulse);
        }

        private void UpdateFlash()
        {
            if (flash == null || _flashTimer <= 0f) return;

            _flashTimer -= Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_flashTimer / flashDuration);
            flash.color = WithAlpha(flashColor, t);

            if (_flashTimer <= 0f) flash.color = WithAlpha(flashColor, 0f);
        }

        /// <summary>화면 플래시를 터뜨린다.</summary>
        public void Flash() => _flashTimer = flashDuration;

        /// <summary>세션 리셋 시 모든 연출을 끈다.</summary>
        public void ResetVisuals()
        {
            _targetVignetteAlpha = 0f;
            _currentVignetteAlpha = 0f;
            _flashTimer = 0f;
            _comboNormalized = 0f;

            if (comboGlow != null) comboGlow.color = WithAlpha(safeColor, 0f);
            if (dangerVignette != null) dangerVignette.color = WithAlpha(dangerColor, 0f);
            if (flash != null) flash.color = WithAlpha(flashColor, 0f);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
