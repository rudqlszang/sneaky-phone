using UnityEngine;

namespace Molae.Feedback
{
    /// <summary>
    /// Perlin noise 기반 화면 흔들림. 순수 랜덤은 프레임마다 값이 튀어 지저분해 보이므로
    /// 연속적인 노이즈를 쓰고, 축마다 다른 시드를 줘서 X/Y가 같이 움직이는 부자연스러움을 없앤다.
    ///
    /// trauma(0~1)를 누적하고 실제 강도는 trauma^exponent 로 계산한다.
    /// exponent를 2로 두면 감쇠 후반이 급격히 조용해져 "흔들림이 질척이는" 느낌이 사라진다.
    /// </summary>
    public class ScreenShaker : MonoBehaviour
    {
        [Header("강도")]
        [Tooltip("trauma = 1 일 때의 최대 이동량(월드 유닛).")]
        [SerializeField] private float maxOffset = 0.35f;
        [Tooltip("trauma = 1 일 때의 최대 회전(도).")]
        [SerializeField] private float maxRoll = 2.5f;

        [Header("곡선")]
        [Tooltip("실제 강도 = trauma^exponent. 2 권장.")]
        [SerializeField, Range(1f, 3f)] private float traumaExponent = 2f;
        [Tooltip("초당 trauma 감소량.")]
        [SerializeField] private float recoverySpeed = 1.5f;
        [Tooltip("노이즈 진행 속도. 높을수록 빠르게 떤다.")]
        [SerializeField] private float frequency = 25f;

        [Header("프리셋")]
        [SerializeField, Range(0f, 1f)] private float weakTrauma = 0.25f;
        [SerializeField, Range(0f, 1f)] private float strongTrauma = 1f;

        private float _trauma;
        private float _noiseTime;
        private Vector3 _basePosition;
        private Quaternion _baseRotation;
        private float _seedX, _seedY, _seedRoll;

        private void Awake()
        {
            _basePosition = transform.localPosition;
            _baseRotation = transform.localRotation;

            _seedX = Random.value * 100f;
            _seedY = Random.value * 100f;
            _seedRoll = Random.value * 100f;
        }

        /// <summary>약한 흔들림(위험 전환 경고 등).</summary>
        public void ShakeWeak() => AddTrauma(weakTrauma);

        /// <summary>강한 흔들림(게임오버).</summary>
        public void ShakeStrong() => AddTrauma(strongTrauma);

        public void AddTrauma(float amount)
        {
            _trauma = Mathf.Clamp01(_trauma + amount);
        }

        /// <summary>즉시 멈추고 원위치로 되돌린다.</summary>
        public void StopImmediately()
        {
            _trauma = 0f;
            transform.localPosition = _basePosition;
            transform.localRotation = _baseRotation;
        }

        private void LateUpdate()
        {
            if (_trauma <= 0f)
            {
                transform.localPosition = _basePosition;
                transform.localRotation = _baseRotation;
                return;
            }

            // 게임오버 연출에서 timeScale을 건드려도 흔들림은 계속 돌아야 한다.
            float dt = Time.unscaledDeltaTime;
            _noiseTime += dt * frequency;

            float shake = Mathf.Pow(_trauma, traumaExponent);

            float offsetX = SignedNoise(_seedX) * maxOffset * shake;
            float offsetY = SignedNoise(_seedY) * maxOffset * shake;
            float roll = SignedNoise(_seedRoll) * maxRoll * shake;

            transform.localPosition = _basePosition + new Vector3(offsetX, offsetY, 0f);
            transform.localRotation = _baseRotation * Quaternion.Euler(0f, 0f, roll);

            _trauma = Mathf.Max(0f, _trauma - recoverySpeed * dt);
        }

        /// <summary>PerlinNoise의 0~1 출력을 -1~1로 매핑한다.</summary>
        private float SignedNoise(float seed) => Mathf.PerlinNoise(seed, _noiseTime) * 2f - 1f;
    }
}
