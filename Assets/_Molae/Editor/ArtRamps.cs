using UnityEngine;

namespace Molae.EditorTools
{
    /// <summary>
    /// 아트 디렉션 스펙에서 확정된 재질별 명암 램프.
    ///
    /// 값은 전부 실측/계산으로 확정된 것이라 코드에서 임의로 색을 만들지 않는다.
    /// 램프 순서는 항상 [HI, BASE, S1, S2, S3, AO] (밝은 → 어두운).
    ///
    /// 휴 시프트 원칙: 어두워질수록 차가운 쪽(청보라 238°), 밝아질수록 따뜻한 쪽(황 40°).
    /// 인접 단계의 Rec.709 상대휘도 차이는 12~22 사이를 유지한다
    /// (12 미만이면 단계가 안 보이고, 22 초과면 밴딩이 생긴다).
    /// </summary>
    public static class ArtRamps
    {
        public static Color H(string hex) => Palette.Hex(hex);

        // ── Oil 6 원본 (단일 명도 램프) ──
        public static readonly Color[] Oil6 = {
            H("#FBF5EF"), H("#F2D3AB"), H("#C69FA5"), H("#8B6D9C"), H("#494D7E"), H("#272744")
        };

        // ── 재질 램프 [HI, BASE, S1, S2, S3, AO] ──

        /// <summary>피부/크림. HI/BASE/S1 3단계만 주로 쓰고 S3/AO는 턱밑 그림자 전용.</summary>
        public static readonly Color[] Skin = {
            H("#F5D9AB"), H("#F2D3AB"), H("#BE9FA1"), H("#856D7F"), H("#52455D"), H("#2E283E")
        };

        /// <summary>머리카락/잉크. HI는 좌상단 1px 띠에만.</summary>
        public static readonly Color[] Hair = {
            H("#36385E"), H("#272744"), H("#1F1D40"), H("#151432"), H("#0D0D28"), H("#070719")
        };

        /// <summary>나무 — 교탁·책상·의자·문·프레임 전부 이 램프.</summary>
        public static readonly Color[] Wood = {
            H("#BD8957"), H("#AD7757"), H("#885A52"), H("#5F3E40"), H("#3A272F"), H("#21161F")
        };

        /// <summary>마루/바닥. 나무보다 한 단계 어둡게 파생해 가구와 명도로 분리.</summary>
        public static readonly Color[] Floor = {
            H("#AE7C49"), H("#9A6849"), H("#794E45"), H("#553636"), H("#342228"), H("#1D141A")
        };

        /// <summary>칠판. HI가 황록으로 튀는 것이 정상(웜 키라이트가 초록 유광면에 닿은 색).</summary>
        public static readonly Color[] Board = {
            H("#526443"), H("#274C43"), H("#1F393F"), H("#152732"), H("#0D1924"), H("#070E18")
        };

        /// <summary>벽. 300lx 균일 조도라 S2 이하를 쓰지 않는다.</summary>
        public static readonly Color[] Wall = {
            H("#F0D4AA"), H("#ECCDAA"), H("#B99AA0"), H("#826A7E"), H("#5A4A63"), H("#2C273D")
        };

        /// <summary>분필.</summary>
        public static readonly Color[] Chalk = {
            H("#EDEFEC"), H("#E9EDEC"), H("#B7B2DE"), H("#807BAF"), H("#4F4D80"), H("#33325E")
        };

        /// <summary>블러시 — 교복 변형 / 교과서 표지.</summary>
        public static readonly Color[] Blush = {
            H("#D6B9BB"), H("#C69FA5"), H("#9B789B"), H("#6D527A"), H("#433459"), H("#2A2039")
        };

        /// <summary>헤이즈 — 교복 변형 / 대기 베일.</summary>
        public static readonly Color[] Haze = {
            H("#A98BB8"), H("#8B6D9C"), H("#6D5293"), H("#4C3874"), H("#2F2354"), H("#1D1637")
        };

        /// <summary>더스크 — 주 교복색.</summary>
        public static readonly Color[] Dusk = {
            H("#605889"), H("#494D7E"), H("#393A77"), H("#28285D"), H("#1B1B45"), H("#12122F")
        };

        /// <summary>앰버 — 강조/버튼.</summary>
        public static readonly Color[] Amber = {
            H("#F3B468"), H("#F0A868"), H("#BC7E62"), H("#84574D"), H("#513738"), H("#2F2024")
        };

        /// <summary>햇살 — 광선/보상.</summary>
        public static readonly Color[] Sun = {
            H("#FDE4B4"), H("#FCD68D"), H("#C6A185"), H("#8A6F69"), H("#57464A"), H("#332A2F")
        };

        // ── 단색 상수 ──
        public static readonly Color Paper = H("#FBF5EF");
        public static readonly Color Ink = H("#272744");
        public static readonly Color Night = H("#1B1D33");   // 폰 화면 바탕 전용 (램프 없음)
        public static readonly Color Clear = new Color(0, 0, 0, 0);

        // ── 인덱스 별칭 ──
        public const int HI = 0, BASE = 1, S1 = 2, S2 = 3, S3 = 4, AO = 5;

        /// <summary>주광원 방향 — 화면 좌상단 창문 하나. 하이라이트=좌상단, 그림자=우하단.</summary>
        public static readonly Vector2 LightDir = new Vector2(-0.707f, -0.707f);

        /// <summary>광선(godray) 축 — 화면 수직축에서 시계방향 26°.</summary>
        public static readonly Vector2 RayDir = new Vector2(0.4384f, 0.8988f);
        public static readonly Vector2 RayNormal = new Vector2(0.8988f, -0.4384f);

        // ── 유틸 ──

        /// <summary>곱셈 합성. 반올림은 반드시 Round(내림 아님).</summary>
        public static Color Mul(Color b, Color t) => new Color(b.r * t.r, b.g * t.g, b.b * t.b, b.a);

        /// <summary>스크린 합성.</summary>
        public static Color Scr(Color b, Color t) =>
            new Color(1f - (1f - b.r) * (1f - t.r), 1f - (1f - b.g) * (1f - t.g), 1f - (1f - b.b) * (1f - t.b), b.a);

        public static Color A(Color c, float alpha) { c.a = alpha; return c; }

        /// <summary>Rec.709 상대휘도 (0~100 스케일). 램프 검증용.</summary>
        public static float Luma(Color c)
        {
            System.Func<float, float> f = v => v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
            return (0.2126729f * f(c.r) + 0.7151522f * f(c.g) + 0.0721750f * f(c.b)) * 100f;
        }

        /// <summary>
        /// 램프 건전성 검사. 인접 단계 휘도차가 12 미만이면 단계가 안 보이고,
        /// 22 초과면 밴딩이 생긴다. 개발 중 램프를 손볼 때 호출한다.
        /// </summary>
        public static string Validate(string name, Color[] ramp, int upto = 4)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Mathf.Min(upto, ramp.Length - 1); i++)
            {
                float d = Luma(ramp[i]) - Luma(ramp[i + 1]);
                string flag = d < 12f ? "  [단계 안 보임]" : d > 22f ? "  [밴딩 위험]" : "";
                sb.AppendLine($"{name}[{i}->{i + 1}] ΔY={d:0.0}{flag}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 램버트 내적 d로 램프 인덱스를 고른다. 스펙의 임계값을 그대로 쓴다.
        /// d>0.82 HI / 0.45~0.82 BASE / 0.05~0.45 S1 / -0.35~0.05 S2 / 그 아래 AO
        /// </summary>
        public static int IndexFromLambert(float d)
        {
            if (d > 0.82f) return HI;
            if (d > 0.45f) return BASE;
            if (d > 0.05f) return S1;
            if (d > -0.35f) return S2;
            return AO;
        }
    }
}
