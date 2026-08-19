using System;
using UnityEngine;
using Molae.Core;

namespace Molae.Gameplay
{
    /// <summary>
    /// 적발 판정기. "위험 전환 순간에 아직 폰을 보고 있으면 게임오버"라는 요구사항을
    /// 반응 유예 + 연속 인지도(0~1) 두 단계로 구현한다.
    ///
    /// 왜 이진 판정이 아닌가: SeeSo는 30FPS로 샘플을 주고 1~2프레임짜리 오탐이 존재한다.
    /// 이진 판정이면 그 한 프레임에 즉사한다. 인지도 누적은 오탐을 흡수하면서도
    /// "계속 보고 있으면 반드시 걸린다"는 규칙은 그대로 지킨다.
    ///
    /// config.InstantFailAfterGrace 를 켜면 유예 종료 즉시 게임오버(원안 그대로)가 된다.
    /// </summary>
    public class SuspicionMeter : MonoBehaviour
    {
        [SerializeField] private MolaeConfig config;

        [Header("디버그(읽기 전용)")]
        [SerializeField, Range(0f, 1f)] private float debugSuspicion;
        [SerializeField] private float debugGraceRemaining;

        private float _graceRemaining;
        private bool _dangerActive;
        private float _dangerElapsed;
        private bool _closeCallAwarded;
        /// <summary>위험 전환 순간에 실제로 폰을 보고 있었는지. 아슬아슬 보너스의 전제 조건.</summary>
        private bool _wasWatchingWhenDangerBegan;

        /// <summary>0 = 안 들킴, 1 = 적발.</summary>
        public float Suspicion { get; private set; }

        /// <summary>지금 반응 유예 시간 안인지.</summary>
        public bool InGracePeriod => _graceRemaining > 0f;

        /// <summary>유예 잔여 시간(초).</summary>
        public float GraceRemaining => _graceRemaining;

        /// <summary>적발됐을 때 발생.</summary>
        public event Action Caught;

        /// <summary>위험 전환 후 규정 시간 안에 시선을 뗐을 때 발생(아슬아슬 보너스).</summary>
        public event Action CloseCall;

        public void Configure(MolaeConfig cfg) => config = cfg;

        /// <summary>라운드 설정. 있으면 유예/인지도 상승률을 이 값으로 덮어쓴다.</summary>
        private RoundConfig _round;
        public void ApplyRound(RoundConfig round) => _round = round;

        private float Grace => _round != null ? _round.graceSec : config.GraceDuration;
        private float Rise => _round != null ? _round.awarenessRatePerSec : config.SuspicionRise;

        public void ResetSession()
        {
            Suspicion = 0f;
            _graceRemaining = 0f;
            _dangerActive = false;
            _dangerElapsed = 0f;
            _closeCallAwarded = false;
            _wasWatchingWhenDangerBegan = false;
        }

        /// <summary>
        /// TeacherAI.DangerBegan 에 연결한다. 반응 유예 타이머를 시작한다.
        /// gazeOnPhone 은 "지금 폰을 보고 있는가"로, 아슬아슬 보너스의 자격 판정에 쓰인다.
        /// </summary>
        public void OnDangerBegan(bool gazeOnPhone)
        {
            _dangerActive = true;
            _dangerElapsed = 0f;
            _closeCallAwarded = false;
            _wasWatchingWhenDangerBegan = gazeOnPhone;
            _graceRemaining = config != null ? Grace : 0.4f;
        }

        /// <summary>TeacherAI.DangerEnded 에 연결한다.</summary>
        public void OnDangerEnded()
        {
            _dangerActive = false;
            _graceRemaining = 0f;
            _wasWatchingWhenDangerBegan = false;
        }

        /// <summary>GameDirector가 매 프레임 호출한다.</summary>
        public void Tick(float deltaTime, bool gazeOnPhone)
        {
            if (config == null) return;

            if (_graceRemaining > 0f) _graceRemaining = Mathf.Max(0f, _graceRemaining - deltaTime);

            if (_dangerActive)
            {
                _dangerElapsed += deltaTime;

                // 아슬아슬 보너스: 위험 전환 순간에 폰을 보고 있었고,
                // 규정 시간 안에 시선을 뗀 경우에만 준다.
                // 애초에 안 보고 있던 플레이어에게 회피 보상을 주면 소극적 플레이가 최적해가 된다.
                if (!_closeCallAwarded
                    && _wasWatchingWhenDangerBegan
                    && !gazeOnPhone
                    && _dangerElapsed <= config.CloseCallWindow)
                {
                    _closeCallAwarded = true;
                    CloseCall?.Invoke();
                }
            }

            bool accruing = _dangerActive && gazeOnPhone && _graceRemaining <= 0f;

            if (accruing)
            {
                if (config.InstantFailAfterGrace)
                {
                    Suspicion = 1f;
                }
                else
                {
                    Suspicion += Rise * deltaTime;
                }
            }
            else
            {
                Suspicion -= config.SuspicionFall * deltaTime;
            }

            Suspicion = Mathf.Clamp01(Suspicion);

            debugSuspicion = Suspicion;
            debugGraceRemaining = _graceRemaining;

            if (Suspicion >= 1f) Caught?.Invoke();
        }
    }
}
