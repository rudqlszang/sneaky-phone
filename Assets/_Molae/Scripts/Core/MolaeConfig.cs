using UnityEngine;

namespace Molae.Core
{
    /// <summary>
    /// 게임의 모든 튜닝 수치를 담는 단일 진실 공급원(SSOT).
    /// Assets > Create > Molae > Config 로 생성한 뒤 GameDirector 에 물린다.
    /// 난이도 관련 값은 전부 여기서 인스펙터로 조정한다.
    /// </summary>
    [CreateAssetMenu(menuName = "Molae/Config", fileName = "MolaeConfig")]
    public class MolaeConfig : ScriptableObject
    {
        // ────────────────────────────────────────── 세션
        [Header("세션")]
        [Tooltip("한 교시 길이(초). 이 시간을 버티면 탈출 성공.")]
        [SerializeField] private float sessionDuration = 50f;

        [Tooltip("목표 프레임레이트. Android 기본값은 30이므로 명시 설정이 필수다.")]
        [SerializeField] private int targetFrameRate = 60;

        // ────────────────────────────────────────── 선생님 상태 타이밍
        [Header("판서(안전) 구간 — sqrt 보간으로 완만히 감소")]
        [SerializeField] private float writingDurationStart = 4.0f;
        [SerializeField] private float writingDurationEnd = 1.6f;
        [Tooltip("구간 길이에 곱해지는 랜덤 폭. 0.25 = ±25%")]
        [SerializeField, Range(0f, 0.6f)] private float writingRandomness = 0.25f;

        [Header("예고(Warning) 구간 — linear 보간, 하한 고정")]
        [Tooltip("세션 초반의 예고 시간. 길수록 쉽다.")]
        [SerializeField] private float telegraphStart = 0.90f;
        [Tooltip("세션 후반의 예고 시간.")]
        [SerializeField] private float telegraphEnd = 0.55f;
        [Tooltip("인간 반응시간(0.25s) + 시선 이동 + 추적 지연을 감안한 절대 하한. 이 아래는 불공정.")]
        [SerializeField] private float telegraphMinimum = 0.50f;

        [Header("정면 응시(위험) 구간 — pow 보간으로 후반 가중")]
        [SerializeField] private float watchingDurationStart = 1.2f;
        [SerializeField] private float watchingDurationEnd = 2.6f;
        [SerializeField, Range(1f, 4f)] private float watchingCurvePower = 2f;

        [Header("복귀(Returning) 구간")]
        [SerializeField] private float returningDuration = 0.35f;

        // ────────────────────────────────────────── 페이크 모션
        [Header("페이크 모션 (돌 것처럼 하다 마는 패턴)")]
        [Tooltip("이 시간(초) 이후부터 페이크가 등장한다.")]
        [SerializeField] private float fakeUnlockTime = 30f;
        [Tooltip("예고 시작 시 페이크로 빠질 확률.")]
        [SerializeField, Range(0f, 1f)] private float fakeChance = 0.35f;
        [Tooltip("예고 모션을 몇 %까지 진행하다 되돌리는지.")]
        [SerializeField, Range(0.2f, 0.95f)] private float fakeProgress = 0.65f;
        [Tooltip("페이크 후 판서 자세로 되돌아가는 시간.")]
        [SerializeField] private float fakeRecoverDuration = 0.4f;

        // ────────────────────────────────────────── 숨 돌리기(톱니파)
        [Header("숨 돌리기 — 지정 시각에 안전 구간을 늘려 완급을 만든다")]
        [SerializeField] private float[] reliefTimes = { 15f, 30f, 45f };
        [SerializeField] private float reliefMultiplier = 1.6f;
        [Tooltip("지정 시각 앞뒤로 이 시간(초) 안이면 숨 돌리기로 인정.")]
        [SerializeField] private float reliefTolerance = 1.5f;

        // ────────────────────────────────────────── 적발 판정
        [Header("적발 판정 — 반응 유예 + 연속 인지도")]
        [Tooltip("위험 전환 직후 이만큼은 무조건 봐준다. 요구사항의 0.3~0.5초.")]
        [SerializeField, Range(0f, 1.5f)] private float graceDuration = 0.4f;
        [Tooltip("위험 상태에서 폰을 볼 때 인지도 상승 속도(초당). 0.5면 2초 만에 적발.")]
        [SerializeField] private float suspicionRise = 0.5f;
        [Tooltip("시선을 뗐을 때 인지도 감소 속도(초당). 상승보다 빨라야 회피가 보상된다.")]
        [SerializeField] private float suspicionFall = 1.2f;
        [Tooltip("켜면 유예 종료 즉시 게임오버(원안 그대로). 끄면 인지도 누적 방식으로 오탐 즉사를 막는다.")]
        [SerializeField] private bool instantFailAfterGrace = false;

        // ────────────────────────────────────────── 점수 / 콤보
        [Header("점수")]
        [Tooltip("이 간격(초)마다 1틱. 0.1이면 1초에 10점, 50초 완주 시 약 500점.")]
        [SerializeField] private float scoreTickInterval = 0.1f;
        [SerializeField] private int scorePerTick = 1;

        [Header("콤보")]
        [Tooltip("연속 응시가 이 시간(초) 유지될 때마다 콤보 1단계 상승.")]
        [SerializeField] private float comboStepInterval = 1.0f;
        [Tooltip("콤보 1단계당 배율 증가분.")]
        [SerializeField] private float comboGainPerStep = 0.25f;
        [SerializeField] private float comboMaxMultiplier = 4f;
        [Tooltip("시선을 뗀 뒤 이 시간 안에 다시 보면 콤보가 유지된다.")]
        [SerializeField] private float comboGraceDuration = 0.35f;

        [Header("아슬아슬 보너스")]
        [Tooltip("위험 전환 후 이 시간 안에 시선을 떼면 보너스.")]
        [SerializeField] private float closeCallWindow = 0.5f;
        [SerializeField] private int closeCallBonus = 25;

        // ────────────────────────────────────────── 시선 판정
        [Header("시선 판정")]
        [Tooltip("응시 히트박스를 폰 화면보다 이만큼(px, 1080 기준) 넓힌다. 시선 오차 보정용.")]
        [SerializeField] private float gazePadding = 144f;
        [Tooltip("최근 N개 샘플의 다수결로 판정을 안정화한다.")]
        [SerializeField, Range(1, 15)] private int gazeVoteWindow = 5;
        [Tooltip("LOW_CONFIDENCE 시선도 유효로 취급할지. 끄면 판정이 자주 끊긴다.")]
        [SerializeField] private bool acceptLowConfidence = true;
        [Tooltip("FIXATION 상태에서만 점수를 준다. 스쳐 지나가는 시선 제외.")]
        [SerializeField] private bool requireFixation = false;
        [Tooltip("얼굴 미검출이 이 시간(초) 이상 지속되면 일시정지.")]
        [SerializeField] private float faceMissingPauseDelay = 0.5f;

        // ────────────────────────────────────────── 캘리브레이션
        [Header("캘리브레이션")]
        [Tooltip("응시점 개수. 1점은 오프셋만 보정해 화면 가장자리에서 크게 어긋난다. " +
                 "5점은 스케일·기울기까지 잡아 정확도가 확연히 좋아진다.")]
        [SerializeField] private CalibrationPoints calibrationPoints = CalibrationPoints.Five;

        [Tooltip("점을 응시할 시간(초). 요구사항의 2초.")]
        [SerializeField] private float calibrationGazeDuration = 2f;
        [Tooltip("점이 나타난 뒤 샘플 수집을 시작하기까지의 대기(초).")]
        [SerializeField] private float calibrationSettleDelay = 0.6f;
        [Tooltip("캘리브레이션 결과를 저장해 다음 실행에서 건너뛴다.")]
        [SerializeField] private bool cacheCalibration = true;

        // ────────────────────────────────────────── 등급
        [Header("등급 — 생존 시간(초) 기준. 반드시 내림차순으로 정렬해 둘 것")]
        [SerializeField]
        private GradeThreshold[] grades =
        {
            new GradeThreshold { minSeconds = 45f, label = "S", title = "완전범죄",  stars = 3 },
            new GradeThreshold { minSeconds = 30f, label = "A", title = "베테랑",    stars = 3 },
            new GradeThreshold { minSeconds = 15f, label = "B", title = "그럭저럭",  stars = 2 },
            new GradeThreshold { minSeconds =  5f, label = "C", title = "초보",      stars = 1 },
            new GradeThreshold { minSeconds =  0f, label = "D", title = "바로 걸림", stars = 0 },
        };

        [SerializeField] private string clearLabel = "CLEAR";
        [SerializeField] private string clearTitle = "탈출 성공";

        // ────────────────────────────────────────── 읽기 전용 접근자
        public float SessionDuration => sessionDuration;
        public int TargetFrameRate => targetFrameRate;

        public float WritingDurationStart => writingDurationStart;
        public float WritingDurationEnd => writingDurationEnd;
        public float WritingRandomness => writingRandomness;

        public float TelegraphStart => telegraphStart;
        public float TelegraphEnd => telegraphEnd;
        public float TelegraphMinimum => telegraphMinimum;

        public float WatchingDurationStart => watchingDurationStart;
        public float WatchingDurationEnd => watchingDurationEnd;
        public float WatchingCurvePower => watchingCurvePower;

        public float ReturningDuration => returningDuration;

        public float FakeUnlockTime => fakeUnlockTime;
        public float FakeChance => fakeChance;
        public float FakeProgress => fakeProgress;
        public float FakeRecoverDuration => fakeRecoverDuration;

        public float[] ReliefTimes => reliefTimes;
        public float ReliefMultiplier => reliefMultiplier;

        public float GraceDuration => graceDuration;
        public float SuspicionRise => suspicionRise;
        public float SuspicionFall => suspicionFall;
        public bool InstantFailAfterGrace => instantFailAfterGrace;

        public float ScoreTickInterval => scoreTickInterval;
        public int ScorePerTick => scorePerTick;

        public float ComboStepInterval => comboStepInterval;
        public float ComboGainPerStep => comboGainPerStep;
        public float ComboMaxMultiplier => comboMaxMultiplier;
        public float ComboGraceDuration => comboGraceDuration;

        public float CloseCallWindow => closeCallWindow;
        public int CloseCallBonus => closeCallBonus;

        public float GazePadding => gazePadding;
        public int GazeVoteWindow => gazeVoteWindow;
        public bool AcceptLowConfidence => acceptLowConfidence;
        public bool RequireFixation => requireFixation;
        public float FaceMissingPauseDelay => faceMissingPauseDelay;

        public CalibrationPoints CalibrationPointCount => calibrationPoints;
        public float CalibrationGazeDuration => calibrationGazeDuration;
        public float CalibrationSettleDelay => calibrationSettleDelay;
        public bool CacheCalibration => cacheCalibration;

        public GradeThreshold[] Grades => grades;
        public string ClearLabel => clearLabel;
        public string ClearTitle => clearTitle;

        /// <summary>세션 진행도 t(0~1)에 따른 판서 구간 길이. sqrt 보간으로 초반 체감을 완만하게.</summary>
        public float GetWritingDuration(float t)
        {
            float baseValue = Mathf.Lerp(writingDurationStart, writingDurationEnd, Mathf.Sqrt(Mathf.Clamp01(t)));
            float jitter = 1f + Random.Range(-writingRandomness, writingRandomness);
            return Mathf.Max(0.3f, baseValue * jitter);
        }

        /// <summary>세션 진행도 t(0~1)에 따른 예고 시간. linear 보간 후 하한 클램프.</summary>
        public float GetTelegraphDuration(float t)
        {
            float value = Mathf.Lerp(telegraphStart, telegraphEnd, Mathf.Clamp01(t));
            return Mathf.Max(telegraphMinimum, value);
        }

        /// <summary>세션 진행도 t(0~1)에 따른 위험 지속 시간. pow 보간으로 후반에 몰아준다.</summary>
        public float GetWatchingDuration(float t)
        {
            float k = Mathf.Pow(Mathf.Clamp01(t), watchingCurvePower);
            return Mathf.Lerp(watchingDurationStart, watchingDurationEnd, k);
        }

        /// <summary>현재 경과 시간이 숨 돌리기 구간에 걸리는지.</summary>
        public bool IsReliefWindow(float elapsed)
        {
            if (reliefTimes == null) return false;
            for (int i = 0; i < reliefTimes.Length; i++)
            {
                if (Mathf.Abs(elapsed - reliefTimes[i]) <= reliefTolerance) return true;
            }
            return false;
        }

        /// <summary>페이크 모션이 해금됐는지.</summary>
        public bool IsFakeUnlocked(float elapsed) => elapsed >= fakeUnlockTime;

        /// <summary>생존 시간으로 등급을 판정한다. 완주했으면 CLEAR.</summary>
        public GradeThreshold Evaluate(float survivedSeconds, bool cleared)
        {
            if (cleared)
            {
                return new GradeThreshold
                {
                    minSeconds = sessionDuration,
                    label = clearLabel,
                    title = clearTitle,
                    stars = 3,
                };
            }

            if (grades != null)
            {
                for (int i = 0; i < grades.Length; i++)
                {
                    if (survivedSeconds >= grades[i].minSeconds) return grades[i];
                }
            }

            return new GradeThreshold { minSeconds = 0f, label = "D", title = "바로 걸림", stars = 0 };
        }

        private void OnValidate()
        {
            sessionDuration = Mathf.Max(1f, sessionDuration);
            telegraphMinimum = Mathf.Max(0.25f, telegraphMinimum);
            telegraphStart = Mathf.Max(telegraphMinimum, telegraphStart);
            telegraphEnd = Mathf.Max(telegraphMinimum, telegraphEnd);
            scoreTickInterval = Mathf.Max(0.01f, scoreTickInterval);
            comboStepInterval = Mathf.Max(0.05f, comboStepInterval);
            suspicionRise = Mathf.Max(0.01f, suspicionRise);
            suspicionFall = Mathf.Max(0.01f, suspicionFall);
        }
    }

    /// <summary>
    /// 캘리브레이션 응시점 개수.
    ///
    /// One  — 오프셋만 보정한다. 빠르지만 화면 가장자리로 갈수록 오차가 커진다.
    /// Five — 중앙 + 네 모서리. 스케일과 기울기까지 잡아 화면 전역 정확도가 확연히 좋아진다.
    /// Six  — 가장 정확하지만 그만큼 오래 걸린다.
    /// </summary>
    public enum CalibrationPoints
    {
        One = 1,
        Five = 5,
        Six = 6,
    }

    /// <summary>생존 시간 구간 하나에 대응하는 등급 정의.</summary>
    [System.Serializable]
    public struct GradeThreshold
    {
        [Tooltip("이 등급을 받기 위한 최소 생존 시간(초).")]
        public float minSeconds;
        [Tooltip("등급 문자. S / A / B / C / D")]
        public string label;
        [Tooltip("등급 아래 표시되는 한 줄 문구.")]
        public string title;
        [Range(0, 3)] public int stars;
    }
}
