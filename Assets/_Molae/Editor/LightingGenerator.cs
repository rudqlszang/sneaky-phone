using UnityEngine;

namespace Molae.EditorTools
{
    /// <summary>
    /// 런타임 라이팅/포스트프로세싱 없이 스프라이트에 구워 넣는 조명 오버레이.
    ///
    /// 저사양 60fps 목표에서 URP 2D 라이트는 라이트 하나당 별도 렌더 텍스처를 만들기 때문에
    /// 쓰지 않는다. 대신 미리 구운 알파 오버레이 몇 장으로 같은 인상을 만든다.
    ///
    /// 씬 전체 광원은 화면 좌상단 창문 하나뿐이고, 광선축은 수직에서 시계방향 26°다.
    /// </summary>
    public static class LightingGenerator
    {
        private const int W = MolaeArtBuild.W;   // 270
        private const int H = MolaeArtBuild.H;   // 480
        private static int Y(int specY) => H - 1 - specY;

        private static float Smoothstep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / Mathf.Max(1e-5f, b - a));
            return t * t * (3f - 2f * t);
        }

        private static float Hash(float x, float y)
        {
            float n = Mathf.Sin(x * 127.1f + y * 311.7f) * 43758.5453f;
            return n - Mathf.Floor(n);
        }

        private static float ValueNoise(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;
            float u = xf * xf * (3f - 2f * xf), v = yf * yf * (3f - 2f * yf);
            float a = Hash(xi, yi), b = Hash(xi + 1, yi), c2 = Hash(xi, yi + 1), d = Hash(xi + 1, yi + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c2, d, u), v);
        }

        // ══════════════════════════════════════════ 햇살 광선

        private static readonly float[] RayX0 = { -30f, 15f, 59f, 108f, 160f, 225f };
        private static readonly float[] RayW = { 33f, 20f, 49f, 26f, 40f, 22f };

        public static PixelCanvas BuildGodray()
        {
            var c = new PixelCanvas(W, H);
            Color sun = ArtRamps.Sun[ArtRamps.BASE];

            for (int sy = 0; sy < H; sy++)
            {
                float v = sy / (float)H;
                // 길이 감쇠 — y≈384에서 완전 소멸
                float L = Smoothstep(0f, 0.06f, v) * (1f - Smoothstep(0.34f, 0.80f, v));
                if (L <= 0f) continue;

                for (int x = 0; x < W; x++)
                {
                    float best = 0f;
                    for (int r = 0; r < RayX0.Length; r++)
                    {
                        // 광선축(26°)에 수직인 거리
                        float dist = Mathf.Abs(0.8988f * (x - RayX0[r]) - 0.4384f * sy);
                        float t = dist / (RayW[r] * 0.5f);
                        float e = 1f - Smoothstep(0.55f, 1.0f, t);
                        if (e <= 0f) continue;

                        // 세로 주파수를 가로의 1/20로 둬 광선축 방향으로만 늘린다
                        float n = ValueNoise(dist / 11.0f, (0.4384f * x + 0.8988f * sy) / 225.0f);
                        float noise = 0.88f + 0.24f * n;
                        best = Mathf.Max(best, e * noise);
                    }
                    if (best <= 0f) continue;

                    float a = Mathf.Clamp(0.22f * best * L, 0f, 0.34f);
                    c.Blend(x, Y(sy), ArtRamps.A(sun, a));
                }
            }
            return c;
        }

        public static PixelCanvas BuildDust()
        {
            var c = new PixelCanvas(W, H);
            var rnd = new System.Random(1234);
            Color dust = Palette.Hex("#FFF3D2");

            int placed = 0, guard = 0;
            while (placed < 70 && guard++ < 20000)
            {
                int x = rnd.Next(0, W);
                int sy = rnd.Next(10, 400);

                // 광선 안쪽에만 뿌린다
                float best = 0f;
                for (int r = 0; r < RayX0.Length; r++)
                {
                    float dist = Mathf.Abs(0.8988f * (x - RayX0[r]) - 0.4384f * sy);
                    best = Mathf.Max(best, 1f - Smoothstep(0.55f, 1.0f, dist / (RayW[r] * 0.5f)));
                }
                if (best < 0.35f) continue;

                float n = Hash(x * 3.7f, sy * 1.9f);
                float a = 0.28f + 0.37f * n;
                int size = n > 0.72f ? 2 : 1;
                for (int dy = 0; dy < size; dy++)
                    for (int dx = 0; dx < size; dx++)
                        c.Blend(x + dx, Y(sy + dy), ArtRamps.A(dust, Mathf.Min(a, 0.65f)));
                placed++;
            }
            return c;
        }

        // ══════════════════════════════════════════ 비네트

        /// <summary>
        /// 상태별 비네트. 1080x1920은 종횡비가 극단적이라 정원이 아니라
        /// rx/ry를 분리한 타원(175/270)을 써야 위아래가 과하게 어두워지지 않는다.
        /// 중심 y는 화면 중앙(240)보다 25px 아래로 내려 시선을 폰 쪽으로 끈다.
        /// </summary>
        public static PixelCanvas BuildVignette(float intensity, float inner, float outer)
        {
            var c = new PixelCanvas(W, H);
            for (int sy = 0; sy < H; sy++)
            {
                for (int x = 0; x < W; x++)
                {
                    float dx = (x - 135f) / 175f;
                    float dy = (sy - 265f) / 270f;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Smoothstep(inner, outer, dist) * intensity;
                    if (a <= 0.002f) continue;
                    c.Set(x, Y(sy), ArtRamps.A(ArtRamps.Ink, a));
                }
            }
            return c;
        }

        // ══════════════════════════════════════════ 종이 그레인

        /// <summary>
        /// 단색 면이 조악해 보이는 가장 큰 이유는 완전히 균일한 색면이다.
        /// 아주 낮은 세기의 그레인을 씌우면 색은 그대로 두고 인쇄물 같은 질감만 생긴다.
        /// 128x128 타일로 구워 화면 전체에 반복한다.
        /// </summary>
        public static PixelCanvas BuildGrain()
        {
            const int T = 128;
            var c = new PixelCanvas(T, T);
            for (int y = 0; y < T; y++)
            {
                for (int x = 0; x < T; x++)
                {
                    // 5옥타브, 셀 6px
                    float n = 0f, amp = 1f, freq = 1f / 6f, norm = 0f;
                    for (int o = 0; o < 5; o++)
                    {
                        n += ValueNoise(x * freq, y * freq) * amp;
                        norm += amp; amp *= 0.5f; freq *= 2f;
                    }
                    n = n / norm;                       // 0~1
                    float dev = (n - 0.5f) * 2f;        // -1~1
                    float a = Mathf.Abs(dev) * 0.09f;
                    Color col = dev > 0f ? ArtRamps.Paper : ArtRamps.Ink;
                    c.Set(x, y, ArtRamps.A(col, a));
                }
            }
            return c;
        }
    }
}
