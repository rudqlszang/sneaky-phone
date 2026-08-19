using UnityEngine;

namespace Molae.Core
{
    /// <summary>
    /// 한 교시(라운드)의 난이도 정의.
    ///
    /// 난이도는 런타임 계산이 아니라 라운드별 상수로 박는다. 곡선 공식으로 만들면
    /// "2교시가 왜 이렇게 어렵지"를 추적할 수 없고, 한 라운드만 손보는 것도 불가능해진다.
    ///
    /// 난이도 스칼라는 라운드당 +30%(D(n) = 1.30^(n-1))를 채택했다.
    /// 게임 난이도 변화의 최소 식별 차이(JND)가 약 10%이므로 30%면 전원이 체감한다.
    /// </summary>
    [CreateAssetMenu(menuName = "Molae/Round Config", fileName = "RoundConfig")]
    public class RoundConfig : ScriptableObject
    {
        [Header("식별")]
        [Tooltip("1부터 시작. 화면 표시와 보너스 계산에 쓴다.")]
        public int roundIndex = 1;

        [Tooltip("인터미션에서 보여줄 문구. 난이도가 올랐다는 사실을 문장으로 선언한다.")]
        [TextArea] public string bannerText = "1교시 시작";

        [Header("길이")]
        [Tooltip("이 라운드를 버텨야 하는 시간(초).")]
        public float durationSec = 20f;

        [Header("선생님 사이클 (초)")]
        [Tooltip("판서(안전) 구간 길이.")]
        public float safeChalkboardSec = 3.20f;
        [Tooltip("돌아보기 예고 시간. 짧을수록 어렵다. 하한 0.45초로 클램프된다.")]
        public float telegraphSec = 0.90f;
        [Tooltip("정면 응시(위험) 지속 시간.")]
        public float dangerStareSec = 1.80f;
        [Tooltip("칠판으로 되돌아가는 시간. 전 라운드 고정.")]
        public float returnSec = 0.60f;

        [Header("적발 판정")]
        [Tooltip("위험 전환 후 무조건 봐주는 유예(초).")]
        public float graceSec = 0.40f;
        [Tooltip("위험 상태에서 폰을 볼 때 인지도 상승 속도(초당). 0.5면 2초 만에 적발.")]
        public float awarenessRatePerSec = 0.50f;

        [Header("페이크")]
        [Tooltip("예고 진입 시 페이크로 빠질 확률. 1교시는 0으로 둬서 규칙부터 배우게 한다.")]
        [Range(0f, 1f)] public float fakeChance = 0f;

        [Header("점수")]
        [Tooltip("이 라운드의 점수 배율.")]
        public float scoreMultiplier = 1f;
        [Tooltip("라운드 클리어 보너스.")]
        public int clearBonus = 100;

        [Header("연출")]
        [Tooltip("교실 상단 그라디언트 색. 라운드가 진행될수록 해가 진다.")]
        public Color skyTint = new Color(0.988f, 0.839f, 0.553f); // #FCD68D

        /// <summary>
        /// 예고 시간 하한. 0.45초 = 60fps 27프레임으로, 인간 단순반응(265ms)의 1.7배다.
        /// 이보다 짧으면 물리적으로 반응이 불가능해 "불공정"으로 느껴진다.
        /// </summary>
        public float EffectiveTelegraph => Mathf.Max(telegraphSec, 0.45f);

        /// <summary>한 사이클 길이(초).</summary>
        public float CycleLength => safeChalkboardSec + EffectiveTelegraph + dangerStareSec + returnSec;

        /// <summary>
        /// 위험 노출률 — 라운드 전체에서 "위험 상태"인 시간의 비율.
        /// 목표: 1교시 0.28 / 2교시 0.38 / 3교시 0.50
        /// 이 값이 실제 체감 난이도를 결정한다.
        /// </summary>
        public float DangerExposure => CycleLength <= 0f ? 0f : dangerStareSec / CycleLength;

        /// <summary>적발까지 걸리는 시간(초). 인지도 1.0 기준.</summary>
        public float TimeToCaught => awarenessRatePerSec <= 0f ? 999f : 1f / awarenessRatePerSec;

        private void OnValidate()
        {
            durationSec = Mathf.Max(5f, durationSec);
            safeChalkboardSec = Mathf.Max(0.5f, safeChalkboardSec);
            dangerStareSec = Mathf.Max(0.3f, dangerStareSec);
            returnSec = Mathf.Max(0.1f, returnSec);
            awarenessRatePerSec = Mathf.Max(0.05f, awarenessRatePerSec);

            // 위험 노출률이 설계 목표에서 크게 벗어나면 경고한다.
            float[] target = { 0.279f, 0.382f, 0.502f };
            int i = Mathf.Clamp(roundIndex - 1, 0, target.Length - 1);
            if (Mathf.Abs(DangerExposure - target[i]) > 0.05f)
            {
                Debug.LogWarning(
                    $"[Molae] {name}: 위험 노출률 {DangerExposure:0.000} (목표 {target[i]:0.000}). " +
                    $"사이클 {CycleLength:0.00}초 — 난이도 체감이 설계와 어긋날 수 있습니다.", this);
            }
        }
    }
}
