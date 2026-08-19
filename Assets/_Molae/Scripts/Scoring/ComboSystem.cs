using System;
using UnityEngine;
using Molae.Core;

namespace Molae.Scoring
{
    /// <summary>
    /// 콤보 배율 계산기. 연속 득점이 유지되는 시간에 비례해 단계가 오르고,
    /// 시선을 뗀 뒤 짧은 유예 안에 돌아오면 유지된다.
    ///
    /// MonoBehaviour가 아닌 순수 클래스라 테스트가 쉽고, ScoreManager가 소유한다.
    /// </summary>
    [Serializable]
    public class ComboSystem
    {
        private MolaeConfig _config;

        private float _accumulated;   // 현재 단계 안에서 쌓인 시간
        private float _breakTimer;    // 득점이 끊긴 뒤 경과 시간

        /// <summary>현재 콤보 단계. 0부터 시작.</summary>
        public int Step { get; private set; }

        /// <summary>점수에 곱해지는 배율. 최소 1.</summary>
        public float Multiplier { get; private set; } = 1f;

        /// <summary>다음 단계까지의 진행도 0~1. UI 게이지에 쓴다.</summary>
        public float StepProgress { get; private set; }

        /// <summary>콤보 단계가 올랐을 때 발생. 인자는 새 단계.</summary>
        public event Action<int> StepUp;

        /// <summary>콤보가 끊겼을 때 발생.</summary>
        public event Action Broken;

        public void Configure(MolaeConfig config) => _config = config;

        public void Reset()
        {
            Step = 0;
            Multiplier = 1f;
            _accumulated = 0f;
            _breakTimer = 0f;
            StepProgress = 0f;
        }

        /// <summary>매 프레임 호출. scoring이 true면 콤보가 쌓인다.</summary>
        public void Tick(float deltaTime, bool scoring)
        {
            if (_config == null) return;

            if (scoring)
            {
                _breakTimer = 0f;
                _accumulated += deltaTime;

                while (_accumulated >= _config.ComboStepInterval)
                {
                    _accumulated -= _config.ComboStepInterval;
                    Step++;
                    Recalculate();
                    StepUp?.Invoke(Step);
                }

                StepProgress = Mathf.Clamp01(_accumulated / _config.ComboStepInterval);
            }
            else
            {
                if (Step == 0 && _accumulated <= 0f) return;

                _breakTimer += deltaTime;
                if (_breakTimer < _config.ComboGraceDuration) return;

                if (Step > 0 || _accumulated > 0f)
                {
                    Reset();
                    Broken?.Invoke();
                }
            }
        }

        /// <summary>즉시 콤보를 끊는다(적발, 위험 전환 등).</summary>
        public void Break()
        {
            if (Step == 0 && _accumulated <= 0f) return;
            Reset();
            Broken?.Invoke();
        }

        private void Recalculate()
        {
            float raw = 1f + Step * _config.ComboGainPerStep;
            Multiplier = Mathf.Min(raw, _config.ComboMaxMultiplier);
        }
    }
}
