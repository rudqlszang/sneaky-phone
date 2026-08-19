using System;
using UnityEngine;

namespace Molae.Core
{
    /// <summary>
    /// 라운드(교시) 진행 상태 관리자.
    ///
    /// 설계 원칙:
    ///  - 실패해도 씬을 다시 로드하지 않는다. SeeSo 캘리브레이션을 다시 하게 만들면 안 되기 때문이다.
    ///    상태만 되돌려서 사망~재개를 2초 안에 끝낸다.
    ///  - 실패는 전체 재시작이 아니라 "해당 라운드만 재시도"다. 짧은 세션에서 전체 재시작은
    ///    이탈로 직결된다.
    ///  - 라이프가 0이 되어도 되돌리지 않는다. 대신 등급 상한만 내린다(시간 페널티 0, 등급 페널티만).
    /// </summary>
    public class RoundManager : MonoBehaviour
    {
        [Header("라운드 정의 — 순서대로 3개")]
        [SerializeField] private RoundConfig[] rounds = new RoundConfig[3];

        [Header("라이프")]
        [Tooltip("세션 시작 시 라이프. 0이 되면 리필하되 등급 상한이 내려간다.")]
        [SerializeField] private int startingLives = 3;

        [Header("보너스")]
        [Tooltip("적발 0회로 라운드를 깨면 주는 보너스. roundIndex 를 곱한다.")]
        [SerializeField] private int flawlessBonusPerRound = 150;

        [Header("디버그(읽기 전용)")]
        [SerializeField] private int debugRoundIndex;
        [SerializeField] private int debugLives;
        [SerializeField] private int debugTotalScore;

        private int _index;                 // 0-based
        private int _lives;
        private int _retryCount;
        private int _caughtThisRound;
        private int _scoreBeforeRound;      // 라운드 재시도 시 여기로 되돌린다

        /// <summary>현재 라운드 설정. 없으면 null.</summary>
        public RoundConfig Current => (rounds != null && _index >= 0 && _index < rounds.Length) ? rounds[_index] : null;

        /// <summary>1부터 시작하는 라운드 번호.</summary>
        public int RoundNumber => _index + 1;

        /// <summary>전체 라운드 수.</summary>
        public int TotalRounds => rounds != null ? rounds.Length : 0;

        public int Lives => _lives;
        public int RetryCount => _retryCount;
        public int CaughtThisRound => _caughtThisRound;

        /// <summary>재시도를 3회 이상 했으면 S등급을 영구 차단한다.</summary>
        public bool SRankLocked { get; private set; }

        /// <summary>마지막 라운드까지 전부 깼는지.</summary>
        public bool AllCleared { get; private set; }

        /// <summary>이전 라운드까지 누적된 점수(현재 라운드 점수는 미포함).</summary>
        public int BankedScore { get; private set; }

        /// <summary>라운드가 시작될 때. 인자는 해당 라운드 설정.</summary>
        public event Action<RoundConfig> RoundStarted;

        /// <summary>라운드를 클리어했을 때. (설정, 이번 라운드 획득 점수, 무피격 여부)</summary>
        public event Action<RoundConfig, int, bool> RoundCleared;

        /// <summary>라운드에서 적발됐을 때. (설정, 남은 라이프)</summary>
        public event Action<RoundConfig, int> RoundFailed;

        /// <summary>모든 라운드를 깼을 때.</summary>
        public event Action AllRoundsCleared;

        // ───────────────────────────────────────────── 수명주기

        public void ResetSession()
        {
            _index = 0;
            _lives = startingLives;
            _retryCount = 0;
            _caughtThisRound = 0;
            _scoreBeforeRound = 0;
            BankedScore = 0;
            SRankLocked = false;
            AllCleared = false;
            PushDebug();
        }

        /// <summary>현재 라운드를 시작한다(또는 재시작한다).</summary>
        public void BeginCurrentRound()
        {
            _caughtThisRound = 0;
            _scoreBeforeRound = BankedScore;
            RoundStarted?.Invoke(Current);
            PushDebug();
        }

        /// <summary>
        /// 라운드를 클리어했다. 점수를 적립하고 다음 라운드로 넘긴다.
        /// </summary>
        /// <param name="roundScore">이번 라운드에서 번 생존 점수(배율 적용 후)</param>
        /// <returns>다음 라운드가 있으면 true, 전부 끝났으면 false</returns>
        public bool CompleteCurrentRound(int roundScore)
        {
            RoundConfig cfg = Current;
            bool flawless = _caughtThisRound == 0;

            int gained = roundScore;
            if (cfg != null) gained += cfg.clearBonus;
            if (flawless && cfg != null) gained += flawlessBonusPerRound * cfg.roundIndex;

            BankedScore += gained;
            RoundCleared?.Invoke(cfg, gained, flawless);

            _index++;
            PushDebug();

            if (_index >= TotalRounds)
            {
                AllCleared = true;
                AllRoundsCleared?.Invoke();
                return false;
            }
            return true;
        }

        /// <summary>
        /// 적발됐다. 라운드 점수는 버리고 같은 라운드를 다시 시작할 수 있게 되돌린다.
        /// 씬을 다시 로드하지 않으므로 캘리브레이션은 유지된다.
        /// </summary>
        public void FailCurrentRound()
        {
            _caughtThisRound++;
            _retryCount++;
            _lives--;

            // 이번 라운드에서 번 점수는 날린다. 이전 라운드 누적은 보존한다.
            BankedScore = _scoreBeforeRound;

            if (_retryCount >= 3) SRankLocked = true;

            if (_lives <= 0)
            {
                // 되돌리지 않는다. 라이프만 리필하고 등급 상한을 내린다.
                _lives = startingLives;
                SRankLocked = true;
            }

            RoundFailed?.Invoke(Current, _lives);
            PushDebug();
        }

        /// <summary>현재 라운드를 처음부터 다시. 상태만 되돌린다.</summary>
        public void ResetCurrentRound() => BeginCurrentRound();

        private void PushDebug()
        {
            debugRoundIndex = RoundNumber;
            debugLives = _lives;
            debugTotalScore = BankedScore;
        }

        /// <summary>인스펙터에서 라운드 배열을 코드로 채울 때 쓴다(에디터 전용 헬퍼).</summary>
        public void SetRounds(RoundConfig[] configs) => rounds = configs;

        // ───────────────────────────────────────────── 이론상 최대 점수

        [Header("엔딩 판정")]
        [Tooltip("이론상 최대 점수의 이 비율 이상이어야 좋은 엔딩. 0.9 = 상위 10% 안.")]
        [SerializeField, Range(0.5f, 1f)] private float goodEndingRatio = 0.90f;

        [Tooltip("점수 계산에 쓰는 틱 간격. MolaeConfig 의 값과 같아야 한다.")]
        [SerializeField] private float scoreTickInterval = 0.1f;
        [SerializeField] private float comboStepInterval = 1.0f;
        [SerializeField] private float comboGainPerStep = 0.25f;
        [SerializeField] private float comboMaxMultiplier = 4f;

        /// <summary>
        /// 완벽하게 플레이했을 때 나올 수 있는 최대 총점.
        ///
        /// 계산 근거: 점수는 "선생님이 안전 상태 + 폰 응시 중"일 때만 오른다.
        /// 위험 구간에서는 아무리 잘해도 점수가 안 오르므로, 라운드 길이가 아니라
        /// (라운드 길이 × 안전 비율) 만큼만 득점 가능하다.
        /// 콤보는 연속 득점 시간에 비례해 오르므로 시뮬레이션으로 적분한다.
        /// </summary>
        public int TheoreticalMaxScore
        {
            get
            {
                if (rounds == null) return 0;
                int total = 0;

                foreach (var cfg in rounds)
                {
                    if (cfg == null) continue;

                    // 이 라운드에서 점수를 벌 수 있는 시간
                    float safeRatio = 1f - cfg.DangerExposure;
                    float scorableSec = cfg.durationSec * safeRatio;

                    // 콤보를 적분한다. 위험 구간마다 콤보가 끊기므로 사이클 단위로 리셋.
                    float cycles = Mathf.Max(1f, cfg.durationSec / cfg.CycleLength);
                    float secPerCycle = scorableSec / cycles;

                    float perCycle = 0f;
                    float t = 0f;
                    while (t < secPerCycle)
                    {
                        int step = Mathf.FloorToInt(t / comboStepInterval);
                        float mul = Mathf.Min(1f + step * comboGainPerStep, comboMaxMultiplier);
                        perCycle += (scoreTickInterval / scoreTickInterval) * mul;  // 틱 1개당 1점 × 배율
                        t += scoreTickInterval;
                    }

                    float roundBase = perCycle * cycles;
                    int roundScore = Mathf.RoundToInt(roundBase * cfg.scoreMultiplier);

                    total += roundScore;
                    total += cfg.clearBonus;
                    total += flawlessBonusPerRound * cfg.roundIndex;   // 무피격 보너스
                }
                return total;
            }
        }

        /// <summary>좋은 엔딩 커트라인 점수.</summary>
        public int GoodEndingThreshold => Mathf.RoundToInt(TheoreticalMaxScore * goodEndingRatio);

        /// <summary>이번 플레이가 좋은 엔딩 조건을 만족하는지.</summary>
        public bool QualifiesForGoodEnding => BankedScore >= GoodEndingThreshold;

        /// <summary>이론상 최대 대비 달성률 0~1.</summary>
        public float ScoreRatio
        {
            get { int max = TheoreticalMaxScore; return max <= 0 ? 0f : Mathf.Clamp01(BankedScore / (float)max); }
        }
    }
}
