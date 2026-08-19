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
    public enum GamePhase
    {
        Boot,
        /// <summary>SeeSo 초기화 대기 / 카메라 권한 요청 중</summary>
        Preparing,
        /// <summary>1포인트 캘리브레이션 진행 중</summary>
        Calibrating,
        /// <summary>시작 대기(카운트다운)</summary>
        Ready,
        Playing,
        /// <summary>얼굴 미검출로 일시정지. 게임오버가 아니다.</summary>
        Paused,
        /// <summary>적발됨</summary>
        GameOver,
        /// <summary>50초 완주</summary>
        Cleared,
    }

    /// <summary>한 세션의 최종 결과.</summary>
    public struct SessionResult
    {
        public int Score;
        public float SurvivedSeconds;
        public bool Cleared;
        public int BestCombo;
        public float SafeWatchSeconds;
        public GradeThreshold Grade;

        /// <summary>안전 구간 중 폰을 응시한 비율 0~1. 결과 화면 보조 통계.</summary>
        public float GazeRatio;
    }

    /// <summary>
    /// 게임 전체를 묶는 상태 머신이자 유일한 Tick 소유자.
    ///
    /// 하위 시스템(TeacherAI, SuspicionMeter, ScoreManager)은 Update()를 쓰지 않고
    /// 여기서 Tick()을 받는다. 그래야 일시정지가 정확하고, 얼굴 미검출로 멈춘 동안
    /// 선생님 타이머나 점수가 몰래 흐르는 사고가 없다.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class GameDirector : MonoBehaviour
    {
        [Header("설정")]
        [SerializeField] private MolaeConfig config;

        [Header("시스템")]
        [SerializeField] private GazeService gaze;
        [SerializeField] private TeacherAI teacher;
        [SerializeField] private SuspicionMeter suspicion;
        [SerializeField] private ScoreManager score;
        [SerializeField] private PlayerPoseController playerPose;

        [Header("연출")]
        [SerializeField] private AudioDirector audioDirector;
        [SerializeField] private EdgeGlowController edgeGlow;
        [SerializeField] private ScreenShaker screenShaker;
        [SerializeField] private ScorePopAnimator scorePop;

        [Header("흐름")]
        [Tooltip("캘리브레이션을 건너뛴다(에디터 테스트용).")]
        [SerializeField] private bool skipCalibration;
        [Tooltip("Ready 단계에서 시작까지 대기 시간(초).")]
        [SerializeField] private float readyCountdown = 1.5f;
        [Tooltip("적발 후 결과 화면으로 넘어가기까지 연출 시간(초).")]
        [SerializeField] private float gameOverDelay = 1.2f;

        [Header("디버그(읽기 전용)")]
        [SerializeField] private GamePhase debugPhase;
        [SerializeField] private float debugElapsed;

        private float _elapsed;
        private bool _resultDispatched;

        // ───────────────────────────────────────────── 공개 상태

        public GamePhase Phase { get; private set; } = GamePhase.Boot;
        public float Elapsed => _elapsed;
        public float Remaining => config == null ? 0f : Mathf.Max(0f, config.SessionDuration - _elapsed);
        public float NormalizedProgress => config == null ? 0f : Mathf.Clamp01(_elapsed / config.SessionDuration);
        public SessionResult LastResult { get; private set; }

        public MolaeConfig Config => config;
        public GazeService Gaze => gaze;
        public TeacherAI Teacher => teacher;
        public SuspicionMeter Suspicion => suspicion;
        public ScoreManager Score => score;

        // ───────────────────────────────────────────── 라운드 모드
        //
        // GameFlow 가 3라운드를 돌릴 때 쓰는 API. 단일 세션 모드(BeginSession)와 공존한다.

        private RoundConfig _round;
        private bool _roundRunning;
        private bool _roundCaught;
        private float _roundElapsed;

        /// <summary>라운드가 진행 중인지.</summary>
        public bool RoundRunning => _roundRunning;

        /// <summary>이번 라운드에서 적발됐는지.</summary>
        public bool RoundCaught => _roundCaught;

        /// <summary>이번 라운드에서 번 점수(배율 적용 후).</summary>
        public int RoundScore { get; private set; }

        /// <summary>현재 라운드 설정. 없으면 단일 세션 모드.</summary>
        public RoundConfig CurrentRound => _round;

        /// <summary>라운드 진행도 0~1.</summary>
        public float RoundProgress => _round == null ? 0f : Mathf.Clamp01(_roundElapsed / _round.durationSec);

        /// <summary>라운드 남은 시간(초).</summary>
        public float RoundRemaining => _round == null ? 0f : Mathf.Max(0f, _round.durationSec - _roundElapsed);

        /// <summary>GameFlow 참조. 인터미션 중 시선 판정을 끄기 위해 본다.</summary>
        [SerializeField] private GameFlow flow;

        /// <summary>라운드를 시작한다. 난이도를 config 대신 이 라운드 설정으로 덮어쓴다.</summary>
        public void BeginRound(RoundConfig round)
        {
            _round = round;
            _roundRunning = true;
            _roundCaught = false;
            _roundElapsed = 0f;
            RoundScore = 0;

            teacher?.ResetSession();
            teacher?.ApplyRound(round);
            suspicion?.ResetSession();
            suspicion?.ApplyRound(round);
            score?.ResetSession();
            edgeGlow?.ResetVisuals();
            screenShaker?.StopImmediately();
            scorePop?.SetScoreImmediate(0);
            playerPose?.SnapTo(PlayerPoseController.Pose.Upright);

            audioDirector?.SetDangerState(false);
            gaze?.StartTracking();

            SetPhase(GamePhase.Playing);
        }

        public event Action<GamePhase> PhaseChanged;
        public event Action<SessionResult> SessionFinished;
        /// <summary>캘리브레이션 진행도 0~1.</summary>
        public event Action<float> CalibrationProgress;
        /// <summary>캘리브레이션 점 위치(스크린 좌표).</summary>
        public event Action<Vector2> CalibrationPointShown;

        // ───────────────────────────────────────────── 수명주기

        private void Awake()
        {
            if (config == null)
            {
                Debug.LogError("[Molae] MolaeConfig가 연결되지 않았습니다. GameDirector를 비활성화합니다.", this);
                enabled = false;
                return;
            }

            FrameRateBootstrap.Apply(config.TargetFrameRate);
            NormalizeOptionalReferences();

            teacher?.Configure(config);
            suspicion?.Configure(config);
            score?.Configure(config);

            WireEvents();
        }

        /// <summary>
        /// 인스펙터에서 비어 있는 참조를 진짜 null 로 바꿔 둔다.
        ///
        /// Unity의 UnityEngine.Object 는 네이티브 객체가 파괴/미할당이어도 C# 참조 자체는
        /// 살아 있는 "fake null" 상태가 된다. 이 상태에서는 ?. 연산자가 null 병합을 하지 않고
        /// 그대로 멤버를 호출해 UnassignedReferenceException 이 터진다.
        /// Unity가 오버로드한 == 로 한 번 걸러 실제 null 을 대입해 두면 이후 ?. 가 정상 동작한다.
        /// </summary>
        private void NormalizeOptionalReferences()
        {
            if (gaze == null) gaze = null;
            if (teacher == null) teacher = null;
            if (suspicion == null) suspicion = null;
            if (score == null) score = null;
            if (playerPose == null) playerPose = null;
            if (audioDirector == null) audioDirector = null;
            if (edgeGlow == null) edgeGlow = null;
            if (screenShaker == null) screenShaker = null;
            if (scorePop == null) scorePop = null;
        }

        private void Start() => StartCoroutine(RunFlow());

        private void OnDestroy() => UnwireEvents();

        private void WireEvents()
        {
            if (teacher != null)
            {
                teacher.DangerBegan += HandleDangerBegan;
                teacher.DangerEnded += HandleDangerEnded;
                teacher.StateChanged += HandleTeacherStateChanged;
            }

            if (suspicion != null)
            {
                suspicion.Caught += HandleCaught;
                suspicion.CloseCall += HandleCloseCall;
            }

            if (score != null)
            {
                score.Ticked += HandleScoreTicked;
                score.Combo.StepUp += HandleComboStepUp;
            }

            if (gaze != null) gaze.CalibrationEventReceived += HandleCalibrationEvent;
        }

        private void UnwireEvents()
        {
            if (teacher != null)
            {
                teacher.DangerBegan -= HandleDangerBegan;
                teacher.DangerEnded -= HandleDangerEnded;
                teacher.StateChanged -= HandleTeacherStateChanged;
            }

            if (suspicion != null)
            {
                suspicion.Caught -= HandleCaught;
                suspicion.CloseCall -= HandleCloseCall;
            }

            if (score != null)
            {
                score.Ticked -= HandleScoreTicked;
                score.Combo.StepUp -= HandleComboStepUp;
            }

            if (gaze != null) gaze.CalibrationEventReceived -= HandleCalibrationEvent;
        }

        // ───────────────────────────────────────────── 흐름

        private IEnumerator RunFlow()
        {
            SetPhase(GamePhase.Preparing);

            // 대기하기 전에 먼저 "추적을 원한다"는 의도를 등록한다.
            // GazeService가 이 의도를 기억했다가 프로바이더가 Ready가 되는 순간 자동으로 켠다.
            // (권한 팝업을 늦게 눌러 초기화가 타임아웃 뒤에 끝나는 경우를 살리기 위함)
            gaze?.StartTracking();

            // 시선 프로바이더 초기화 대기. Mock이면 즉시 통과한다.
            float timeout = 10f;
            while (gaze != null
                   && gaze.State != GazeProviderState.Ready
                   && gaze.State != GazeProviderState.Tracking
                   && gaze.State != GazeProviderState.Failed
                   && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }

            if (gaze != null && gaze.State == GazeProviderState.Failed)
            {
                Debug.LogWarning($"[Molae] 시선 추적 초기화 실패: {gaze.LastError}");
            }

            gaze?.StartTracking();

            if (!skipCalibration && gaze != null && !gaze.TryRestoreCalibration())
            {
                yield return RunCalibration();
            }

            SetPhase(GamePhase.Ready);
            yield return new WaitForSecondsRealtime(readyCountdown);

            BeginSession();
        }

        private IEnumerator RunCalibration()
        {
            SetPhase(GamePhase.Calibrating);
            _calibrationDone = false;
            _calibrationFailed = false;

            gaze.StartCalibration();

            // 점이 표시되기를 기다린다.
            float wait = 3f;
            while (!_calibrationPointReceived && !_calibrationFailed && wait > 0f)
            {
                wait -= Time.unscaledDeltaTime;
                yield return null;
            }

            if (_calibrationFailed)
            {
                Debug.LogWarning("[Molae] 캘리브레이션을 사용할 수 없어 건너뜁니다.");
                yield break;
            }

            // 사용자가 점에 시선을 고정할 시간을 준 뒤 샘플 수집을 시작한다.
            yield return new WaitForSecondsRealtime(config.CalibrationSettleDelay);
            gaze.CollectCalibrationSamples();

            float limit = config.CalibrationGazeDuration + 5f;
            while (!_calibrationDone && !_calibrationFailed && limit > 0f)
            {
                limit -= Time.unscaledDeltaTime;
                yield return null;
            }

            if (_calibrationDone) gaze.PersistCalibration();
        }

        private bool _calibrationDone;
        private bool _calibrationFailed;
        private bool _calibrationPointReceived;

        private void HandleCalibrationEvent(CalibrationEvent evt)
        {
            switch (evt.Type)
            {
                case CalibrationEventType.NextPoint:
                    _calibrationPointReceived = true;
                    CalibrationPointShown?.Invoke(evt.Point);
                    break;
                case CalibrationEventType.Progress:
                    CalibrationProgress?.Invoke(evt.Progress);
                    break;
                case CalibrationEventType.Finished:
                    _calibrationDone = true;
                    break;
                case CalibrationEventType.Failed:
                    _calibrationFailed = true;
                    break;
            }
        }

        /// <summary>세션을 시작한다. 결과 화면의 '다시하기'도 여기로 들어온다.</summary>
        public void BeginSession()
        {
            _elapsed = 0f;
            _resultDispatched = false;

            teacher?.ResetSession();
            suspicion?.ResetSession();
            score?.ResetSession();
            edgeGlow?.ResetVisuals();
            screenShaker?.StopImmediately();
            scorePop?.SetScoreImmediate(0);
            playerPose?.SnapTo(PlayerPoseController.Pose.Upright);

            audioDirector?.ResetSession();
            audioDirector?.StartMusic();

            gaze?.StartTracking();

            SetPhase(GamePhase.Playing);
        }

        // ───────────────────────────────────────────── 메인 루프

        private void Update()
        {
            if (config == null) return;

            debugPhase = Phase;
            debugElapsed = _elapsed;

            // 얼굴 미검출은 게임오버가 아니라 일시정지다.
            if (Phase == GamePhase.Playing && gaze != null && gaze.IsFaceMissing)
            {
                SetPhase(GamePhase.Paused);
            }
            else if (Phase == GamePhase.Paused && (gaze == null || !gaze.IsFaceMissing))
            {
                SetPhase(GamePhase.Playing);
            }

            if (Phase != GamePhase.Playing) return;

            float dt = Time.deltaTime;
            _elapsed += dt;

            // 인터미션/카운트다운 중에는 시선 판정을 멈춘다.
            // 그 화면은 반드시 봐야 하는 화면인데 판정이 살아 있으면 '폰 보는 중'으로 잡혀 모순이 된다.
            bool judge = flow == null || flow.GazeJudgeEnabled;
            bool gazeOnPhone = judge && gaze != null && gaze.IsLookingAtPhone;

            teacher?.Tick(dt, _elapsed);

            bool dangerous = teacher != null && teacher.IsDangerous;
            bool safe = !dangerous;

            suspicion?.Tick(dt, gazeOnPhone);
            score?.Tick(dt, safe, gazeOnPhone);

            playerPose?.SetLookingAtPhone(gazeOnPhone);

            edgeGlow?.Tick(
                score != null ? score.ComboStep : 0,
                dangerous,
                teacher != null && teacher.IsTelegraphing,
                teacher != null ? teacher.TurnAmount : 0f);

            audioDirector?.SetChalkActive(teacher != null && teacher.State == TeacherState.Writing);

            // ── 라운드 모드 ──
            if (_roundRunning && _round != null)
            {
                _roundElapsed += dt;
                audioDirector?.UpdateDifficultyByRound(_round.roundIndex);

                if (_roundElapsed >= _round.durationSec)
                {
                    // 라운드 클리어. 점수에 라운드 배율을 적용한다.
                    RoundScore = Mathf.RoundToInt((score != null ? score.Score : 0) * _round.scoreMultiplier);
                    _roundRunning = false;
                }
                return;
            }

            // ── 단일 세션 모드(구 동작 유지) ──
            audioDirector?.UpdateDifficulty(_elapsed);
            if (_elapsed >= config.SessionDuration) FinishSession(cleared: true);
        }

        // ───────────────────────────────────────────── 이벤트 핸들러

        private void HandleDangerBegan()
        {
            // 위험 전환 순간의 응시 여부를 넘겨야 아슬아슬 보너스 자격을 올바로 판정할 수 있다.
            suspicion?.OnDangerBegan(gaze != null && gaze.IsLookingAtPhone);
            score?.BreakCombo();

            // 스냅샷 전환은 timeScale의 영향을 받으므로 timeScale을 건드리기 전에 호출한다.
            audioDirector?.SetDangerState(true);
            screenShaker?.ShakeWeak();
            HapticService.Light();
        }

        private void HandleDangerEnded()
        {
            suspicion?.OnDangerEnded();
            audioDirector?.SetDangerState(false);
        }

        private void HandleTeacherStateChanged(TeacherState previous, TeacherState next)
        {
            // 예고 시작 = 분필 소리가 끊기는 순간. 청각이 시각보다 빠르므로 가장 강력한 경고다.
            if (next == TeacherState.Warning || next == TeacherState.Faking)
            {
                audioDirector?.SetChalkActive(false);
            }
        }

        private void HandleScoreTicked(int gained, int total)
        {
            scorePop?.OnScoreTicked(gained, total);
            audioDirector?.PlayScoreTick(score != null ? score.ComboStep : 0);
        }

        private void HandleComboStepUp(int step)
        {
            audioDirector?.PlayComboUp();
            scorePop?.Punch(step);
            HapticService.Light();
        }

        private void HandleCloseCall()
        {
            score?.AddBonus(config.CloseCallBonus, "아슬아슬!");
            audioDirector?.PlayCloseCall();
        }

        private void HandleCaught()
        {
            if (Phase != GamePhase.Playing) return;

            // 라운드 모드에서는 세션을 끝내지 않는다. GameFlow 가 해당 라운드만 재시도시킨다.
            if (_roundRunning)
            {
                _roundCaught = true;
                _roundRunning = false;
                RoundScore = 0;                        // 이번 라운드 점수는 버린다
                screenShaker?.ShakeStrong();
                edgeGlow?.Flash();
                HapticService.Heavy();
                gaze?.StopTracking();
                return;
            }

            FinishSession(cleared: false);
        }

        // ───────────────────────────────────────────── 종료

        private void FinishSession(bool cleared)
        {
            if (_resultDispatched) return;
            _resultDispatched = true;

            SetPhase(cleared ? GamePhase.Cleared : GamePhase.GameOver);

            float survived = Mathf.Min(_elapsed, config.SessionDuration);
            GradeThreshold grade = config.Evaluate(survived, cleared);

            LastResult = new SessionResult
            {
                Score = score != null ? score.Score : 0,
                SurvivedSeconds = survived,
                Cleared = cleared,
                BestCombo = score != null ? score.BestCombo : 0,
                SafeWatchSeconds = score != null ? score.SafeWatchSeconds : 0f,
                Grade = grade,
                GazeRatio = survived <= 0f || score == null ? 0f : Mathf.Clamp01(score.SafeWatchSeconds / survived),
            };

            // 결과 화면 프레임 드랍을 막기 위해 시선 추적을 즉시 멈춘다.
            gaze?.StopTracking();

            // 오디오 전환을 timeScale 변경 전에 먼저 건다.
            audioDirector?.SetMuted();
            audioDirector?.SetChalkActive(false);

            if (!cleared)
            {
                screenShaker?.ShakeStrong();
                edgeGlow?.Flash();
                audioDirector?.PlayGameOver();
                HapticService.Heavy();
            }

            StartCoroutine(DispatchResult());
        }

        private IEnumerator DispatchResult()
        {
            yield return new WaitForSecondsRealtime(gameOverDelay);
            audioDirector?.StopMusic();
            SessionFinished?.Invoke(LastResult);
        }

        private void SetPhase(GamePhase phase)
        {
            if (Phase == phase) return;
            Phase = phase;
            debugPhase = phase;
            PhaseChanged?.Invoke(phase);
        }

        /// <summary>결과 화면의 '다시하기' 버튼이 호출한다.</summary>
        public void Retry()
        {
            StopAllCoroutines();
            BeginSession();
        }
    }
}
