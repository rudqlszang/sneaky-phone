using System;
using UnityEngine;
using Molae.Core;

namespace Molae.Scoring
{
    /// <summary>
    /// 점수 누적기. 선생님이 안전(판서/복귀/페이크) 상태이고 플레이어가 폰을 응시 중일 때만
    /// 시간에 비례해 점수가 쌓이고, 콤보 배율이 곱해진다.
    ///
    /// 점수 단위는 원조 플래시 게임 규칙을 따른다: 0.1초 = 1점 → 50초 완주 시 약 500점.
    /// </summary>
    public class ScoreManager : MonoBehaviour
    {
        [SerializeField] private MolaeConfig config;

        [Header("디버그(읽기 전용)")]
        [SerializeField] private int debugScore;
        [SerializeField] private float debugMultiplier;
        [SerializeField] private int debugCombo;

        private readonly ComboSystem _combo = new ComboSystem();
        private float _tickAccumulator;
        private float _fractionalScore;

        /// <summary>현재 총점.</summary>
        public int Score { get; private set; }

        /// <summary>안전 상태에서 폰을 응시한 누적 시간(초). 결과 화면 통계용.</summary>
        public float SafeWatchSeconds { get; private set; }

        /// <summary>이번 세션 최고 콤보 단계.</summary>
        public int BestCombo { get; private set; }

        public ComboSystem Combo => _combo;
        public float Multiplier => _combo.Multiplier;
        public int ComboStep => _combo.Step;

        /// <summary>점수 틱이 발생했을 때. 인자는 (이번에 더해진 점수, 누적 총점). 사운드/팝업이 구독한다.</summary>
        public event Action<int, int> Ticked;

        /// <summary>보너스 점수가 들어왔을 때. 인자는 (보너스, 사유).</summary>
        public event Action<int, string> BonusAwarded;

        public void Configure(MolaeConfig cfg)
        {
            config = cfg;
            _combo.Configure(cfg);
        }

        private void Awake()
        {
            if (config != null) _combo.Configure(config);
        }

        public void ResetSession()
        {
            Score = 0;
            SafeWatchSeconds = 0f;
            BestCombo = 0;
            _tickAccumulator = 0f;
            _fractionalScore = 0f;
            _combo.Reset();
            PushDebug();
        }

        /// <summary>
        /// GameDirector가 매 프레임 호출한다.
        /// </summary>
        /// <param name="deltaTime">경과 시간</param>
        /// <param name="isSafe">선생님이 안전 상태인지</param>
        /// <param name="gazeOnPhone">플레이어가 폰을 응시 중인지</param>
        public void Tick(float deltaTime, bool isSafe, bool gazeOnPhone)
        {
            if (config == null) return;

            bool scoring = isSafe && gazeOnPhone;

            _combo.Tick(deltaTime, scoring);
            if (_combo.Step > BestCombo) BestCombo = _combo.Step;

            if (!scoring)
            {
                PushDebug();
                return;
            }

            SafeWatchSeconds += deltaTime;
            _tickAccumulator += deltaTime;

            while (_tickAccumulator >= config.ScoreTickInterval)
            {
                _tickAccumulator -= config.ScoreTickInterval;

                // 배율이 소수라 잔여분을 누적해야 점수가 새지 않는다.
                _fractionalScore += config.ScorePerTick * _combo.Multiplier;
                int gained = Mathf.FloorToInt(_fractionalScore);
                if (gained <= 0) continue;

                _fractionalScore -= gained;
                Score += gained;
                Ticked?.Invoke(gained, Score);
            }

            PushDebug();
        }

        /// <summary>아슬아슬 회피 등 일회성 보너스.</summary>
        public void AddBonus(int amount, string reason = "")
        {
            if (amount == 0) return;
            Score += amount;
            BonusAwarded?.Invoke(amount, reason);
            PushDebug();
        }

        /// <summary>적발/위험 전환 시 콤보를 끊는다.</summary>
        public void BreakCombo() => _combo.Break();

        private void PushDebug()
        {
            debugScore = Score;
            debugMultiplier = _combo.Multiplier;
            debugCombo = _combo.Step;
        }
    }
}
