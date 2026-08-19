using System;
using System.Collections;
using UnityEngine;
using Molae.Audio;
using Molae.Feedback;
using Molae.Gameplay;
using Molae.Gaze;
using Molae.Scoring;

namespace Molae.Core
{
    /// <summary>
    /// 3라운드 진행을 총괄한다. 기존 GameDirector 의 단일 세션 흐름 위에 얹혀서
    /// 라운드 시작 → 클리어/실패 → 인터미션 → 다음 라운드 → 엔딩 을 관리한다.
    ///
    /// 설계 원칙(리서치 근거):
    ///  - 실패는 씬 리로드가 아니라 상태 리셋이다. 캘리브레이션을 다시 시키면 안 된다.
    ///  - 사망~재개는 2초 이내. 실패 연출에 3초 이상 쓰면 이탈한다.
    ///  - 인터미션 동안 시선 판정을 끈다. 인터미션 화면은 봐야 하는 화면인데
    ///    판정이 살아 있으면 '폰 보는 중'으로 잡혀 모순이 생긴다.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class GameFlow : MonoBehaviour
    {
        [Header("연결")]
        [SerializeField] private GameDirector director;
        [SerializeField] private RoundManager rounds;
        [SerializeField] private GazeService gaze;
        [SerializeField] private AudioDirector audioDirector;
        [SerializeField] private ScoreManager score;

        [Header("타이밍 (초)")]
        [Tooltip("인터미션 총 길이. 리서치 권장 3.0초 고정.")]
        [SerializeField] private float intermissionSec = 3.0f;
        [Tooltip("이 시간 이후 탭으로 인터미션을 건너뛸 수 있다.")]
        [SerializeField] private float intermissionSkippableAfter = 0.8f;
        [Tooltip("라운드 시작 카운트다운.")]
        [SerializeField] private float countdownSec = 1.0f;
        [Tooltip("적발 후 연출 시간. 사망~재개 2초 예산의 일부.")]
        [SerializeField] private float caughtStingerSec = 1.2f;

        [Header("디버그(읽기 전용)")]
        [SerializeField] private string debugState = "-";

        /// <summary>인터미션/카운트다운 중에는 false. 시선 판정을 멈춘다.</summary>
        public bool GazeJudgeEnabled { get; private set; } = true;

        /// <summary>현재 라운드 번호(1부터).</summary>
        public int RoundNumber => rounds != null ? rounds.RoundNumber : 1;

        /// <summary>인터미션 표시 요청. (끝난 라운드 번호, 다음 라운드 설정, 이번 라운드 점수)</summary>
        public event Action<int, RoundConfig, int> IntermissionRequested;

        /// <summary>카운트다운 표시 요청. (라운드 번호, 남은 시간 0~1)</summary>
        public event Action<int, float> CountdownTick;

        /// <summary>적발 연출 요청. (라운드 번호, 남은 라이프)</summary>
        public event Action<int, int> CaughtStinger;

        /// <summary>
        /// 적발 다이얼로그가 열려 있는 동안 true 를 반환하도록 UI 가 물려준다.
        /// 이게 false 가 될 때까지 다음 라운드를 시작하지 않는다.
        /// </summary>
        public Func<bool> WaitingForRetryDecision;

        /// <summary>타이틀 화면이 '시작하기'를 누를 때까지 true 를 반환한다.</summary>
        public Func<bool> WaitingForTitle;

        /// <summary>모든 라운드 클리어 → 엔딩.</summary>
        public event Action<int> EndingRequested;

        /// <summary>적발로 판이 끝났다. (도달 교시, 최종 점수)</summary>
        public event Action<int, int> SessionFailed;

        /// <summary>세션이 타이틀로 돌아갔다. UI 가 타이틀을 다시 띄운다.</summary>
        public event Action ReturnedToTitle;

        private bool _returnToTitle;

        /// <summary>UI 가 '메인화면으로'를 눌렀을 때 호출한다.</summary>
        public void RequestReturnToTitle()
        {
            _returnToTitle = true;
            ReturnedToTitle?.Invoke();
        }

        private Coroutine _flow;

        private void Awake()
        {
            if (director == null) director = FindFirstObjectByType<GameDirector>();
            if (rounds == null) rounds = FindFirstObjectByType<RoundManager>();
            if (gaze == null) gaze = FindFirstObjectByType<GazeService>();
            if (audioDirector == null) audioDirector = FindFirstObjectByType<AudioDirector>();
            if (score == null) score = FindFirstObjectByType<ScoreManager>();
        }

        /// <summary>세션(3라운드 전체)을 시작한다.</summary>
        public void StartSession()
        {
            if (_flow != null) StopCoroutine(_flow);
            rounds.ResetSession();
            _flow = StartCoroutine(RunSession());
        }

        private IEnumerator RunSession()
        {
            // 타이틀 화면에서 '시작하기'를 누를 때까지 대기
            debugState = "타이틀";
            GazeJudgeEnabled = false;
            while (WaitingForTitle != null && WaitingForTitle())
                yield return null;

            audioDirector?.StartMusic();

            while (true)
            {
                RoundConfig cfg = rounds.Current;
                if (cfg == null) break;

                // ── 카운트다운 ──
                debugState = $"R{rounds.RoundNumber} 카운트다운";
                GazeJudgeEnabled = false;
                gaze?.StartTracking();         // 엔딩/적발에서 껐던 카메라를 다시 켠다
                float t = 0f;
                while (t < countdownSec)
                {
                    t += Time.unscaledDeltaTime;
                    CountdownTick?.Invoke(rounds.RoundNumber, Mathf.Clamp01(t / countdownSec));
                    yield return null;
                }
                GazeJudgeEnabled = true;

                // ── 라운드 진행 ──
                debugState = $"R{rounds.RoundNumber} 진행";
                rounds.BeginCurrentRound();
                director.BeginRound(cfg);

                bool caught = false;
                while (director.RoundRunning)
                {
                    if (director.RoundCaught) { caught = true; break; }
                    yield return null;
                }

                if (caught)
                {
                    // ── 적발 = 즉시 패배 ──
                    // 몇 교시든 한 번 걸리면 그 판은 거기서 끝난다. 라이프도 라운드 재시도도 없다.
                    // 긴장의 근거가 "걸리면 끝"이므로, 재시도로 무마하면 위험 자체가 값을 잃는다.
                    debugState = $"R{rounds.RoundNumber} 적발 → 패배";
                    GazeJudgeEnabled = false;
                    gaze?.StopTracking();          // 선택창이 떠 있는 동안은 카메라를 쉬게 한다
                    audioDirector?.PlayGameOver();

                    int reached = rounds.RoundNumber;
                    // 걸린 시점까지 번 점수는 기록으로 남긴다(이번 교시 점수 포함).
                    int finalScore = rounds.BankedScore + director.RoundScore;

                    yield return new WaitForSecondsRealtime(caughtStingerSec);

                    rounds.FailCurrentRound();
                    SessionFailed?.Invoke(reached, finalScore);
                    CaughtStinger?.Invoke(reached, 0);

                    // 플레이어가 '다시하기' 또는 '메인화면'을 고를 때까지 대기
                    while (WaitingForRetryDecision != null && WaitingForRetryDecision())
                        yield return null;

                    if (_returnToTitle) { _returnToTitle = false; yield break; }

                    // 처음(1교시)부터 다시. 씬을 다시 로드하지 않으므로 캘리브레이션은 유지된다.
                    rounds.ResetSession();
                    continue;
                }

                // ── 클리어 ──
                int roundScore = director.RoundScore;
                int endedRound = rounds.RoundNumber;
                bool hasNext = rounds.CompleteCurrentRound(roundScore);

                if (!hasNext)
                {
                    debugState = "엔딩";
                    // 엔딩에서는 카메라를 끈다. 볼 이유가 없는데 켜두면 배터리·발열만 먹고,
                    // 사용자 입장에서도 "게임 끝났는데 왜 아직 얼굴을 보고 있나" 가 된다.
                    GazeJudgeEnabled = false;
                    gaze?.StopTracking();
                    EndingRequested?.Invoke(rounds.BankedScore);
                    yield break;
                }

                // ── 인터미션 ──
                debugState = "인터미션";
                GazeJudgeEnabled = false;
                IntermissionRequested?.Invoke(endedRound, rounds.Current, roundScore);

                float it = 0f;
                while (it < intermissionSec)
                {
                    it += Time.unscaledDeltaTime;
                    if (it > intermissionSkippableAfter && WasTapped()) break;
                    yield return null;
                }
            }
        }

        private static bool WasTapped()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began) return true;
            if (Input.GetMouseButtonDown(0)) return true;
#endif
            return false;
        }

        public void StopSession()
        {
            if (_flow != null) { StopCoroutine(_flow); _flow = null; }
            debugState = "정지";
        }
    }
}
