using UnityEngine;
using UnityEngine.Audio;

namespace Molae.Audio
{
    /// <summary>
    /// 적응형 BGM + 효과음 총괄.
    ///
    /// 구조 (vertical remixing / 레이어드):
    ///   L0 Vinyl   비닐 크래클 — 상시
    ///   L1 Keys    로즈 코드 루프 — 상시(베이스 레이어)
    ///   L2 Drums   붐뱁 드럼 — 난이도 1단계부터
    ///   L3 Bass    업라이트 베이스 — 2단계부터
    ///   L4 Tension 셰이커/패드 — 3단계부터
    ///
    /// 모든 stem은 동일 길이·동일 BPM·동일 키여야 하고 반드시 같이 재생을 시작한 뒤
    /// 볼륨만 페이드해야 한다. 재생 시작은 PlayScheduled(dspTime + lead)로 잡아야
    /// 프레임레이트와 무관하게 샘플 단위로 동기화된다.
    ///
    /// 권장 루프 규격: 76.8 BPM 4/4, 16마디 = 정확히 50.000초 (세션 길이와 일치)
    ///   1비트 = 0.78125초 / 1마디 = 3.125초 / 64비트 = 50초
    ///
    /// 파라미터 제어 규칙 (섞어 쓰면 깨진다):
    ///   게임 상태(안전/위험)  → AudioMixerSnapshot.TransitionTo
    ///   유저 볼륨 슬라이더     → AudioMixer.SetFloat (다른 파라미터)
    ///   난이도 레이어 페이드   → AudioSource.volume (믹서를 건드리지 않는다)
    /// 노출 파라미터에 SetFloat을 한 번이라도 호출하면 그 파라미터는 스냅샷 제어권을
    /// 영구히 잃고, ClearFloat()을 불러야만 복구된다.
    /// </summary>
    public class AudioDirector : MonoBehaviour
    {
        [System.Serializable]
        public class MusicLayer
        {
            [Tooltip("이 레이어를 재생할 AudioSource. Mixer Group을 Music에 연결해 둘 것.")]
            public AudioSource source;
            [Tooltip("이 레이어가 켜지는 난이도 단계. 0이면 항상 켜짐.")]
            public int unlockStage;
            [Tooltip("이 레이어가 켜졌을 때의 목표 볼륨. 런타임에 덮어쓰지 않는다.")]
            [Range(0f, 1f)] public float targetVolume = 1f;
            [Tooltip("페이드 시간(초). 1마디(3.125s) 또는 1비트(0.78125s) 단위 권장.")]
            public float fadeDuration = 3.125f;

            [HideInInspector] public float currentVolume;
        }

        [Header("믹서")]
        [SerializeField] private AudioMixer mixer;
        [Tooltip("판서(안전) 상태 스냅샷. 로우패스 열림.")]
        [SerializeField] private AudioMixerSnapshot safeSnapshot;
        [Tooltip("위험 상태 스냅샷. 로우패스를 800Hz 근처로 닫아 '벽 너머' 긴장감을 만든다.")]
        [SerializeField] private AudioMixerSnapshot dangerSnapshot;
        [Tooltip("게임오버/결과 스냅샷.")]
        [SerializeField] private AudioMixerSnapshot mutedSnapshot;
        [Tooltip("스냅샷 전환 시간(초). 1비트=0.78초 권장.")]
        [SerializeField] private float snapshotTransition = 0.4f;

        [Header("유저 볼륨 — 노출 파라미터 이름 (스냅샷과 반드시 분리)")]
        [SerializeField] private string masterVolumeParam = "MasterVolume";
        [SerializeField] private string musicVolumeParam = "MusicVolume";
        [SerializeField] private string sfxVolumeParam = "SfxVolume";

        [Header("BGM 레이어")]
        [SerializeField] private MusicLayer[] layers;
        [Tooltip("PlayScheduled 리드 타임(초). 0.1~0.2 권장.")]
        [SerializeField] private float scheduleLead = 0.15f;

        [Header("난이도 단계 — 이 시각(초)에 다음 레이어가 들어온다")]
        [SerializeField] private float[] stageTimes = { 15f, 30f, 45f };

        [Header("효과음")]
        [SerializeField] private AudioSource sfxSource;
        [SerializeField] private AudioClip scoreTickClip;
        [SerializeField] private AudioClip comboUpClip;
        [SerializeField] private AudioClip closeCallClip;
        [SerializeField] private AudioClip gameOverClip;
        [SerializeField] private AudioClip chalkLoopClip;

        [Header("점수 틱 피치")]
        [Tooltip("콤보 1단계당 올릴 반음 수.")]
        [SerializeField, Range(0f, 3f)] private float semitonesPerCombo = 1f;
        [Tooltip("최대 상승 반음. 12 = 한 옥타브(pitch 2.0).")]
        [SerializeField, Range(1f, 24f)] private float maxSemitones = 12f;
        [Tooltip("점수 틱 사운드가 너무 잦지 않도록 최소 간격(초).")]
        [SerializeField] private float tickMinInterval = 0.09f;

        [Header("분필 소리")]
        [Tooltip("판서 중 루프되는 분필 소리. 예고 순간 끊기는 것이 가장 강력한 경고다.")]
        [SerializeField] private AudioSource chalkSource;
        [SerializeField] private float chalkFadeDuration = 0.12f;

        private int _currentStage = -1;
        private float _lastTickTime;
        private float _chalkTarget;
        private bool _musicStarted;

        // 반음 계산 상수. 2^(1/12)
        private const float TwelfthRootOfTwo = 1.0594631f;

        // ───────────────────────────────────────────── 수명주기

        private void Awake()
        {
            if (chalkSource != null && chalkLoopClip != null)
            {
                chalkSource.clip = chalkLoopClip;
                chalkSource.loop = true;
                chalkSource.volume = 0f;
            }

            if (layers == null) return;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].source == null) continue;
                layers[i].source.loop = true;
                layers[i].source.volume = 0f;
                layers[i].currentVolume = 0f;
            }
        }

        private void Update()
        {
            TickLayerFades();
            TickChalk();
        }

        // ───────────────────────────────────────────── BGM

        /// <summary>모든 레이어를 샘플 단위로 동기화해 동시에 재생 시작한다.</summary>
        public void StartMusic()
        {
            if (_musicStarted || layers == null) return;

            double startAt = AudioSettings.dspTime + scheduleLead;

            for (int i = 0; i < layers.Length; i++)
            {
                AudioSource src = layers[i].source;
                if (src == null || src.clip == null) continue;

                src.volume = 0f;
                layers[i].currentVolume = 0f;
                src.PlayScheduled(startAt);
            }

            _musicStarted = true;
            _currentStage = -1;
            SetStage(0);
        }

        public void StopMusic()
        {
            if (layers == null) return;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].source != null) layers[i].source.Stop();
            }
            _musicStarted = false;
        }

        /// <summary>경과 시간에 따라 난이도 단계를 갱신하고 레이어를 페이드인한다.</summary>
        public void UpdateDifficulty(float sessionElapsed)
        {
            int stage = 0;
            if (stageTimes != null)
            {
                for (int i = 0; i < stageTimes.Length; i++)
                {
                    if (sessionElapsed >= stageTimes[i]) stage = i + 1;
                }
            }

            if (stage != _currentStage) SetStage(stage);
        }

        /// <summary>
        /// 라운드 번호로 레이어를 켠다. 라운드 모드에서는 경과 시간이 아니라 라운드가 단계를 결정한다.
        ///  1교시 = 비닐 + 키즈 + 드럼 / 2교시 = + 베이스 / 3교시 = + 텐션
        /// </summary>
        public void UpdateDifficultyByRound(int roundIndex)
        {
            int stage = Mathf.Clamp(roundIndex - 1, 0, 3);
            if (stage != _currentStage) SetStage(stage);
        }

        /// <summary>
        /// 단계를 바꾼다.
        ///
        /// 주의: targetVolume 을 여기서 0으로 덮어쓰면 안 된다. 한 번 0이 되면
        /// 다시 켜질 때 되돌릴 원래 값이 사라져 그 레이어가 영원히 무음이 된다.
        /// 켜고 끄는 판단은 TickLayerFades() 에서 unlockStage 로만 하고,
        /// targetVolume 은 인스펙터 설정값 그대로 유지한다.
        /// </summary>
        private void SetStage(int stage)
        {
            _currentStage = stage;
        }

        private void TickLayerFades()
        {
            if (layers == null) return;

            for (int i = 0; i < layers.Length; i++)
            {
                MusicLayer layer = layers[i];
                if (layer.source == null) continue;

                bool active = layer.unlockStage <= _currentStage;
                float goal = active ? layer.targetVolume : 0f;

                if (Mathf.Approximately(layer.currentVolume, goal)) continue;

                float step = layer.fadeDuration <= 0f
                    ? 1f
                    : Time.unscaledDeltaTime / layer.fadeDuration;

                layer.currentVolume = Mathf.MoveTowards(layer.currentVolume, goal, step);
                layer.source.volume = layer.currentVolume;
            }
        }

        // ───────────────────────────────────────────── 상태 스냅샷

        /// <summary>
        /// 안전/위험 스냅샷 전환.
        /// 주의: TransitionTo는 Time.timeScale의 영향을 받는다. 게임오버에서 슬로우모션을
        /// 쓸 계획이라면 timeScale을 건드리기 전에 이 함수를 먼저 호출해야 한다.
        /// </summary>
        public void SetDangerState(bool dangerous)
        {
            AudioMixerSnapshot target = dangerous ? dangerSnapshot : safeSnapshot;
            // 주의: Unity의 "fake null" 때문에 target?.TransitionTo() 를 쓰면 안 된다.
            // 인스펙터에서 비어 있는 참조는 C# 기준으로는 null이 아니라서 ?. 가 통과해버리고,
            // 그대로 TransitionTo 가 호출되어 UnassignedReferenceException 이 터진다.
            // Unity가 오버로드한 == 연산자를 쓰는 명시적 비교만이 안전하다.
            if (target != null) target.TransitionTo(snapshotTransition);
        }

        public void SetMuted()
        {
            if (mutedSnapshot != null) mutedSnapshot.TransitionTo(snapshotTransition);
        }

        // ───────────────────────────────────────────── 분필 소리

        /// <summary>
        /// 판서 중이면 분필 소리를 켠다.
        /// 예고 순간 이 소리가 끊기는 것이 어떤 UI 경고보다 강력한 신호다.
        /// 청각 반응이 시각 반응보다 빠르기 때문이다.
        /// </summary>
        public void SetChalkActive(bool active) => _chalkTarget = active ? 1f : 0f;

        private void TickChalk()
        {
            if (chalkSource == null) return;

            if (_chalkTarget > 0f && !chalkSource.isPlaying) chalkSource.Play();

            float step = chalkFadeDuration <= 0f ? 1f : Time.unscaledDeltaTime / chalkFadeDuration;
            chalkSource.volume = Mathf.MoveTowards(chalkSource.volume, _chalkTarget, step);

            if (_chalkTarget <= 0f && chalkSource.volume <= 0f && chalkSource.isPlaying) chalkSource.Stop();
        }

        // ───────────────────────────────────────────── 효과음

        /// <summary>점수 틱 사운드. 콤보가 쌓일수록 피치가 반음씩 올라간다.</summary>
        public void PlayScoreTick(int comboStep)
        {
            if (sfxSource == null || scoreTickClip == null) return;
            if (Time.unscaledTime - _lastTickTime < tickMinInterval) return;

            _lastTickTime = Time.unscaledTime;

            float semitones = Mathf.Min(comboStep * semitonesPerCombo, maxSemitones);
            float pitch = Mathf.Pow(TwelfthRootOfTwo, semitones);

            // AudioSource.pitch 유효 범위는 [-3, 3]
            sfxSource.pitch = Mathf.Clamp(pitch, 0.1f, 3f);
            sfxSource.PlayOneShot(scoreTickClip);
        }

        public void PlayComboUp()
        {
            if (sfxSource == null || comboUpClip == null) return;
            sfxSource.pitch = 1f;
            sfxSource.PlayOneShot(comboUpClip);
        }

        public void PlayCloseCall()
        {
            if (sfxSource == null || closeCallClip == null) return;
            sfxSource.pitch = 1f;
            sfxSource.PlayOneShot(closeCallClip);
        }

        public void PlayGameOver()
        {
            if (sfxSource == null || gameOverClip == null) return;
            sfxSource.pitch = 1f;
            sfxSource.PlayOneShot(gameOverClip);
        }

        // ───────────────────────────────────────────── 유저 볼륨

        /// <summary>
        /// 선형 슬라이더값(0.0001~1)을 dB로 변환해 믹서에 넣는다.
        /// 선형값을 그대로 쓰면 안 되고, 슬라이더 최소값은 반드시 0.0001이어야 한다
        /// (0이면 Log10이 -무한대가 되어 깨진다). 0.0001~1 이 정확히 -80dB~0dB에 대응한다.
        /// </summary>
        public void SetMasterVolume(float linear) => SetVolumeParam(masterVolumeParam, linear);
        public void SetMusicVolume(float linear) => SetVolumeParam(musicVolumeParam, linear);
        public void SetSfxVolume(float linear) => SetVolumeParam(sfxVolumeParam, linear);

        private void SetVolumeParam(string param, float linear)
        {
            if (mixer == null || string.IsNullOrEmpty(param)) return;
            float clamped = Mathf.Clamp(linear, 0.0001f, 1f);
            mixer.SetFloat(param, Mathf.Log10(clamped) * 20f);
        }

        /// <summary>dB → 선형. 저장된 값을 슬라이더에 되돌릴 때 쓴다.</summary>
        public static float DecibelToLinear(float db) => Mathf.Pow(10f, db / 20f);

        /// <summary>세션 리셋.</summary>
        public void ResetSession()
        {
            _currentStage = -1;
            _lastTickTime = 0f;
            _chalkTarget = 0f;
            SetDangerState(false);
        }
    }
}
