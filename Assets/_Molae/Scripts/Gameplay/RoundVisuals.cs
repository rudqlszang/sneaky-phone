using UnityEngine;
using Molae.Core;

namespace Molae.Gameplay
{
    /// <summary>
    /// 교시가 올라갈수록 교실의 시간대를 바꿔 난이도를 색으로 읽히게 한다.
    ///
    /// ── 외부 레퍼런스에서 가져온 원칙 ──
    ///
    /// 1) 낮 → 석양 → 밤은 긴장 상승의 표준 문법이다(BotW, WarioWare 계열).
    ///    "같은 교실인데 시간만 흘렀다"가 되어야 하므로 칠판·나무 같은 고유색은 바꾸지 않고
    ///    전체에 얹히는 색보정만 바꾼다.
    ///
    /// 2) 채도가 높을수록 정서가는 긍정, 낮을수록 공포로 기운다(색채 심리 연구).
    ///    그래서 3교시는 어둡게만 만들지 않고 '채도를 빼서' 불안을 만든다.
    ///
    /// 3) 그림자는 차갑게, 하이라이트는 따뜻하게 흐른다(픽셀아트 휴 시프팅).
    ///    2교시는 앰버로 따뜻하게, 3교시는 인디고로 차갑게 민다.
    ///
    /// 4) 하이퍼캐주얼은 가독성이 생명이다. 여기가 핵심 제약이다.
    ///    이 게임은 "선생님이 지금 어느 쪽을 보나"를 못 읽으면 게임이 성립하지 않는다.
    ///    그래서 배경과 인물을 같은 양으로 어둡게 만들면 안 된다.
    ///    배경은 많이(0.58), 인물은 조금만(0.80) 눌러서 어두워질수록 오히려
    ///    인물이 배경에서 더 떠오르게 만든다. 분위기는 어두워지되 판독성은 올라간다.
    ///
    /// 주의: 스프라이트의 알파는 절대 건드리지 않는다.
    /// 선생님/플레이어 스프라이트는 알파 크로스페이드로 상태를 전환하므로
    /// 알파를 덮어쓰면 두 포즈가 동시에 보이거나 아예 사라진다.
    /// </summary>
    public class RoundVisuals : MonoBehaviour
    {
        [Header("연결")]
        [SerializeField] private RoundManager rounds;
        [Tooltip("화면 전체를 덮는 색보정 오버레이. 월드 위·HUD 아래에 있어야 한다.")]
        [SerializeField] private UnityEngine.UI.Image tintOverlay;

        [Header("어둡게 할 대상")]
        [Tooltip("배경. 많이 어두워진다.")]
        [SerializeField] private SpriteRenderer[] backgroundSprites;
        [Tooltip("선생님·플레이어 등 읽어야 하는 대상. 조금만 어두워진다.")]
        [SerializeField] private SpriteRenderer[] focusSprites;

        [System.Serializable]
        public struct RoundTone
        {
            [Tooltip("화면에 얹는 색과 세기(알파).")]
            public Color tint;
            [Range(0f, 1f)] public float backgroundDim;
            [Range(0f, 1f)] public float focusDim;
            [Tooltip("0=원본 채도, 1=완전 흑백.")]
            [Range(0f, 1f)] public float desaturate;
        }

        [Header("교시별 톤")]
        [Tooltip("1교시 — 오후 햇살. 기준값이라 보정 없음.")]
        [SerializeField] private RoundTone round1 = new RoundTone
        { tint = new Color(0.988f, 0.839f, 0.553f, 0.00f), backgroundDim = 1.00f, focusDim = 1.00f, desaturate = 0.00f };

        [Tooltip("2교시 — 석양. 따뜻한 앰버, 채도 유지.")]
        [SerializeField] private RoundTone round2 = new RoundTone
        { tint = new Color(0.949f, 0.651f, 0.353f, 0.20f), backgroundDim = 0.90f, focusDim = 0.97f, desaturate = 0.00f };

        [Tooltip("3교시 — 야자. 차가운 인디고, 채도를 빼서 불안하게.")]
        [SerializeField] private RoundTone round3 = new RoundTone
        { tint = new Color(0.137f, 0.165f, 0.333f, 0.42f), backgroundDim = 0.58f, focusDim = 0.80f, desaturate = 0.35f };

        [Header("전환")]
        [Tooltip("인터미션(3초) 안에 끝나야 다음 교시가 새 색으로 시작한다.")]
        [SerializeField] private float transitionSec = 2.2f;

        private RoundTone _current;
        private RoundTone _target;
        private int _appliedRound = -1;
        private float _t = 1f;

        // 원본 색을 기억해 둔다. 매 프레임 곱하면 색이 계속 어두워져 검게 죽는다.
        private Color[] _bgBase;
        private Color[] _focusBase;

        private void Awake()
        {
            if (rounds == null) rounds = FindFirstObjectByType<RoundManager>();
            CacheBaseColors();
            _current = round1;
            _target = round1;
            Apply(_current);
        }

        private void CacheBaseColors()
        {
            if (backgroundSprites != null)
            {
                _bgBase = new Color[backgroundSprites.Length];
                for (int i = 0; i < backgroundSprites.Length; i++)
                    _bgBase[i] = backgroundSprites[i] != null ? backgroundSprites[i].color : Color.white;
            }
            if (focusSprites != null)
            {
                _focusBase = new Color[focusSprites.Length];
                for (int i = 0; i < focusSprites.Length; i++)
                    _focusBase[i] = focusSprites[i] != null ? focusSprites[i].color : Color.white;
            }
        }

        private void Update()
        {
            int r = rounds != null ? rounds.RoundNumber : 1;
            if (r != _appliedRound)
            {
                _appliedRound = r;
                _current = _target;                       // 진행 중이던 보간 지점에서 이어간다
                _target = r >= 3 ? round3 : r == 2 ? round2 : round1;
                _t = 0f;
            }

            if (_t >= 1f) return;
            _t = transitionSec <= 0f ? 1f : Mathf.Min(1f, _t + Time.unscaledDeltaTime / transitionSec);
            float e = _t * _t * (3f - 2f * _t);           // smoothstep — 시작·끝이 부드럽다
            Apply(Lerp(_current, _target, e));
        }

        private static RoundTone Lerp(RoundTone a, RoundTone b, float u) => new RoundTone
        {
            tint = Color.Lerp(a.tint, b.tint, u),
            backgroundDim = Mathf.Lerp(a.backgroundDim, b.backgroundDim, u),
            focusDim = Mathf.Lerp(a.focusDim, b.focusDim, u),
            desaturate = Mathf.Lerp(a.desaturate, b.desaturate, u),
        };

        private void Apply(RoundTone tone)
        {
            if (tintOverlay != null) tintOverlay.color = tone.tint;
            ApplyTo(backgroundSprites, _bgBase, tone.backgroundDim, tone.desaturate);
            ApplyTo(focusSprites, _focusBase, tone.focusDim, tone.desaturate);
        }

        private static void ApplyTo(SpriteRenderer[] arr, Color[] baseColors, float dim, float desat)
        {
            if (arr == null || baseColors == null) return;
            for (int i = 0; i < arr.Length && i < baseColors.Length; i++)
            {
                var sr = arr[i];
                if (sr == null) continue;

                Color b = baseColors[i];
                // Rec.709 휘도. 채도를 뺄 때 눈이 느끼는 밝기를 유지한다.
                float y = 0.2126f * b.r + 0.7152f * b.g + 0.0722f * b.b;
                Color c = new Color(
                    Mathf.Lerp(b.r, y, desat) * dim,
                    Mathf.Lerp(b.g, y, desat) * dim,
                    Mathf.Lerp(b.b, y, desat) * dim,
                    sr.color.a);          // ← 알파는 현재 값을 그대로. 크로스페이드를 깨지 않는다.
                sr.color = c;
            }
        }
    }
}
