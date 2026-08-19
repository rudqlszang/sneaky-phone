using System;
using UnityEngine;
using Molae.Core;

namespace Molae.Gameplay
{
    public enum TeacherState
    {
        /// <summary>판서 중. 안전. 점수가 오른다.</summary>
        Writing,
        /// <summary>돌아보려는 예고 동작. 아직 안전하지만 곧 위험.</summary>
        Warning,
        /// <summary>정면 응시. 위험. 폰을 보면 인지도가 쌓인다.</summary>
        Watching,
        /// <summary>칠판으로 되돌아가는 중. 안전.</summary>
        Returning,
        /// <summary>돌 것처럼 하다 마는 페이크. 끝까지 안전.</summary>
        Faking,
    }

    /// <summary>
    /// 선생님 상태 전환 스테이트 머신.
    /// 판서 → (예고 | 페이크) → 정면 응시 → 복귀 → 판서 를 반복한다.
    ///
    /// GameDirector가 Tick()을 호출한다. Update()를 쓰지 않으므로 일시정지가 정확하다.
    /// </summary>
    public class TeacherAI : MonoBehaviour
    {
        [SerializeField] private MolaeConfig config;

        [Header("디버그(읽기 전용)")]
        [SerializeField] private TeacherState debugState;
        [SerializeField, Range(0f, 1f)] private float debugTurnAmount;

        private float _stateTimer;
        private float _stateDuration;
        private float _sessionElapsed;
        private float _sessionProgress;

        // 페이크 진행 관리
        private bool _fakeReturning;

        /// <summary>현재 상태.</summary>
        public TeacherState State { get; private set; } = TeacherState.Writing;

        /// <summary>0 = 완전히 칠판 향함, 1 = 완전히 정면 응시. 스프라이트/연출 보간에 쓴다.</summary>
        public float TurnAmount { get; private set; }

        /// <summary>지금 폰을 보면 위험한 상태인지.</summary>
        public bool IsDangerous => State == TeacherState.Watching;

        /// <summary>플레이어에게 "곧 돌아본다"를 알려야 하는 상태인지.</summary>
        public bool IsTelegraphing => State == TeacherState.Warning || State == TeacherState.Faking;

        /// <summary>현재 상태의 진행도 0~1.</summary>
        public float StateProgress => _stateDuration <= 0f ? 1f : Mathf.Clamp01(_stateTimer / _stateDuration);

        /// <summary>상태가 바뀔 때 발생. (이전 상태, 새 상태)</summary>
        public event Action<TeacherState, TeacherState> StateChanged;

        /// <summary>정면 응시(위험)로 전환되는 순간. 반응 유예 타이머가 여기서 시작된다.</summary>
        public event Action DangerBegan;

        /// <summary>위험이 끝나고 안전해지는 순간.</summary>
        public event Action DangerEnded;

        public void Configure(MolaeConfig cfg) => config = cfg;

        /// <summary>
        /// 라운드 설정을 적용한다. 적용 후에는 MolaeConfig 의 난이도 곡선 대신
        /// 이 라운드의 고정값을 쓴다(런타임 계산 금지 원칙).
        /// </summary>
        private RoundConfig _round;
        private bool _lastWasFake;

        public void ApplyRound(RoundConfig round)
        {
            _round = round;
            _lastWasFake = false;
        }

        /// <summary>라운드가 지정돼 있으면 그 값을, 아니면 기존 곡선을 쓴다.</summary>
        private float RoundSafe(float t)
        {
            if (_round == null) return config.GetWritingDuration(t);
            float v = _round.safeChalkboardSec;
            // 페이크 직후에는 안전 구간을 절반으로 줄여 '속았다 → 바로 진짜' 리듬을 만든다
            if (_lastWasFake) { v *= 0.5f; _lastWasFake = false; }
            // 각 라운드 첫 사이클은 최소 2초를 보장해 저강도 밸리를 만든다
            if (_sessionElapsed < 0.1f) v = Mathf.Max(v, 2.0f);
            float jitter = 1f + UnityEngine.Random.Range(-config.WritingRandomness, config.WritingRandomness);
            return Mathf.Max(0.3f, v * jitter);
        }

        private float RoundTelegraph(float t) =>
            _round == null ? config.GetTelegraphDuration(t) : _round.EffectiveTelegraph;

        private float RoundDanger(float t) =>
            _round == null ? config.GetWatchingDuration(t) : _round.dangerStareSec;

        private float RoundReturn() =>
            _round == null ? config.ReturningDuration : _round.returnSec;

        private float RoundFakeChance() =>
            _round == null ? config.FakeChance : _round.fakeChance;

        /// <summary>세션 시작 시 초기화.</summary>
        public void ResetSession()
        {
            _sessionElapsed = 0f;
            _sessionProgress = 0f;
            _fakeReturning = false;
            TurnAmount = 0f;
            State = TeacherState.Writing;
            _stateTimer = 0f;
            _stateDuration = GetWritingDuration();
            debugState = State;
        }

        /// <summary>GameDirector가 매 프레임 호출한다.</summary>
        public void Tick(float deltaTime, float sessionElapsed)
        {
            if (config == null) return;

            _sessionElapsed = sessionElapsed;
            _sessionProgress = Mathf.Clamp01(sessionElapsed / config.SessionDuration);

            _stateTimer += deltaTime;
            UpdateTurnAmount();

            if (_stateTimer < _stateDuration) return;

            AdvanceState();
        }

        private void AdvanceState()
        {
            switch (State)
            {
                case TeacherState.Writing:
                    // 페이크 판정. 라운드가 있으면 라운드 확률을, 없으면 기존 해금 규칙을 쓴다.
                    bool fakeAllowed = _round != null
                        ? _round.fakeChance > 0f
                        : config.IsFakeUnlocked(_sessionElapsed);
                    if (fakeAllowed && UnityEngine.Random.value < RoundFakeChance())
                    {
                        _fakeReturning = false;
                        Transition(TeacherState.Faking, RoundTelegraph(_sessionProgress) * config.FakeProgress);
                    }
                    else
                    {
                        Transition(TeacherState.Warning, RoundTelegraph(_sessionProgress));
                    }
                    break;

                case TeacherState.Warning:
                    Transition(TeacherState.Watching, RoundDanger(_sessionProgress));
                    DangerBegan?.Invoke();
                    break;

                case TeacherState.Watching:
                    Transition(TeacherState.Returning, RoundReturn());
                    DangerEnded?.Invoke();
                    break;

                case TeacherState.Returning:
                    Transition(TeacherState.Writing, GetWritingDuration());
                    break;

                case TeacherState.Faking:
                    if (!_fakeReturning)
                    {
                        // 정점에 도달 → 되돌아가기 시작
                        _fakeReturning = true;
                        Transition(TeacherState.Faking, config.FakeRecoverDuration);
                    }
                    else
                    {
                        _fakeReturning = false;
                        _lastWasFake = true;   // 다음 안전 구간을 절반으로 줄인다
                        Transition(TeacherState.Writing, GetWritingDuration());
                    }
                    break;
            }
        }

        private void Transition(TeacherState next, float duration)
        {
            TeacherState prev = State;
            State = next;
            _stateTimer = 0f;
            _stateDuration = Mathf.Max(0.01f, duration);
            debugState = next;

            if (prev != next) StateChanged?.Invoke(prev, next);
        }

        /// <summary>판서 구간 길이. 숨 돌리기 구간이면 길게 늘려 완급을 만든다.</summary>
        private float GetWritingDuration()
        {
            if (_round != null) return RoundSafe(_sessionProgress);

            float duration = config.GetWritingDuration(_sessionProgress);
            if (config.IsReliefWindow(_sessionElapsed)) duration *= config.ReliefMultiplier;
            return duration;
        }

        /// <summary>상태별로 고개 회전량(0~1)을 보간한다. 스프라이트 전환과 연출이 이 값을 쓴다.</summary>
        private void UpdateTurnAmount()
        {
            float p = StateProgress;

            switch (State)
            {
                case TeacherState.Writing:
                    TurnAmount = 0f;
                    break;

                case TeacherState.Warning:
                    // 급격한 회전이 눈에 띄어야 하므로 ease-out
                    TurnAmount = 1f - Mathf.Pow(1f - p, 3f);
                    break;

                case TeacherState.Watching:
                    TurnAmount = 1f;
                    break;

                case TeacherState.Returning:
                    TurnAmount = 1f - p;
                    break;

                case TeacherState.Faking:
                    float peak = config.FakeProgress;
                    TurnAmount = _fakeReturning
                        ? Mathf.Lerp(peak, 0f, p)
                        : Mathf.Lerp(0f, peak, 1f - Mathf.Pow(1f - p, 3f));
                    break;
            }

            debugTurnAmount = TurnAmount;
        }
    }
}
