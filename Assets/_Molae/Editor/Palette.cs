using UnityEngine;

namespace Molae.EditorTools
{
    /// <summary>
    /// "선생님 몰래폰" 확정 팔레트와 명암 램프 생성기.
    ///
    /// 픽셀아트에서 단순히 명도만 낮춘 그림자는 탁하고 죽어 보인다. 실제 기법은
    /// 어두워질수록 색상을 차가운 쪽(파랑/보라)으로, 밝아질수록 따뜻한 쪽(노랑/주황)으로
    /// 돌리는 hue shifting 이다. 동시에 채도도 함께 조절해야 색이 살아난다.
    /// </summary>
    public static class Palette
    {
        public static Color Hex(string hex)
        {
            Color c;
            ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out c);
            return c;
        }

        // ── 베이스 팔레트 (Oil 6 + 도메인 색) ──
        public static readonly Color Paper = Hex("#FBF5EF");
        public static readonly Color Cream = Hex("#F2D3AB");
        public static readonly Color Blush = Hex("#C69FA5");
        public static readonly Color Haze = Hex("#8B6D9C");
        public static readonly Color Dusk = Hex("#494D7E");
        public static readonly Color Ink = Hex("#272744");

        public static readonly Color Board = Hex("#274C43");
        public static readonly Color Chalk = Hex("#E9EDEC");
        public static readonly Color Wood = Hex("#AD7757");
        public static readonly Color Sun = Hex("#FCD68D");
        public static readonly Color Amber = Hex("#F0A868");
        public static readonly Color Night = Hex("#1B1D33");

        /// <summary>
        /// 베이스 색에서 명암 램프를 만든다.
        /// </summary>
        /// <param name="baseColor">중간 톤</param>
        /// <param name="steps">단계 수 (3~5 권장). 인덱스 0이 가장 어둡고 마지막이 가장 밝다.</param>
        /// <param name="valueSpread">명도 변화 폭 (0.35 정도가 자연스럽다)</param>
        /// <param name="hueShiftDegrees">양 끝에서 돌릴 색상 각도. 어두운 쪽은 -, 밝은 쪽은 +로 적용된다.</param>
        /// <param name="satSpread">채도 변화 폭. 어두운 쪽은 채도를 살짝 올리고 밝은 쪽은 낮춰야 뜨지 않는다.</param>
        public static Color[] Ramp(Color baseColor, int steps = 4, float valueSpread = 0.35f,
                                   float hueShiftDegrees = 22f, float satSpread = 0.16f)
        {
            steps = Mathf.Max(2, steps);
            float h, s, v;
            Color.RGBToHSV(baseColor, out h, out s, out v);

            var result = new Color[steps];
            for (int i = 0; i < steps; i++)
            {
                // -1(가장 어두움) ~ +1(가장 밝음)
                float t = steps == 1 ? 0f : (i / (float)(steps - 1)) * 2f - 1f;

                float nv = Mathf.Clamp01(v + t * valueSpread);

                // 어두울수록 파랑/보라 쪽(색상환에서 음의 방향), 밝을수록 노랑 쪽(양의 방향)
                float nh = Mathf.Repeat(h + (t * hueShiftDegrees) / 360f, 1f);

                // 어두운 쪽 채도 +, 밝은 쪽 채도 −
                float ns = Mathf.Clamp01(s - t * satSpread);

                result[i] = Color.HSVToRGB(nh, ns, nv);
            }
            return result;
        }

        /// <summary>램프에서 t(0~1)에 해당하는 색을 고른다. 보간하지 않고 단계로 스냅한다(픽셀아트 원칙).</summary>
        public static Color Pick(Color[] ramp, float t)
        {
            if (ramp == null || ramp.Length == 0) return Color.magenta;
            int i = Mathf.Clamp(Mathf.RoundToInt(t * (ramp.Length - 1)), 0, ramp.Length - 1);
            return ramp[i];
        }

        /// <summary>배경으로 갈수록 대비를 죽이는 대기 원근(atmospheric perspective).</summary>
        public static Color Recede(Color c, Color atmosphere, float amount)
        {
            return Color.Lerp(c, atmosphere, Mathf.Clamp01(amount));
        }

        /// <summary>HEX 문자열 목록으로 램프를 직접 지정할 때.</summary>
        public static Color[] RampFromHex(params string[] hexes)
        {
            var r = new Color[hexes.Length];
            for (int i = 0; i < hexes.Length; i++) r[i] = Hex(hexes[i]);
            return r;
        }
    }
}
