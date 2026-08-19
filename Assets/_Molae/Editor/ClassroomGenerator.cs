using UnityEditor;
using UnityEngine;

namespace Molae.EditorTools
{
    /// <summary>
    /// 교실 배경 절차 생성기.
    ///
    /// 해상도 규약: 네이티브 270x480 에서 그리고 Unity에서 정확히 x4 확대해 1080x1920을 만든다.
    /// (1080/270 = 4, 1920/480 = 4 — Slynyrd 정수배 규칙. 임의 배율은 픽셀 정사각형을 망가뜨린다)
    /// 임포트: Filter=Point, Compression=None, PPU=64, 카메라 Orthographic Size = 480/(2*64) = 3.75
    ///
    /// 스펙 좌표는 좌상단 원점(y 아래로)이고 PixelCanvas는 좌하단 원점이므로 Y()로 변환한다.
    /// </summary>
    public static class ClassroomGenerator
    {
        public const int W = 270;
        public const int H = 480;

        /// <summary>스펙 좌표(좌상단 원점) → 캔버스 좌표(좌하단 원점)</summary>
        private static int Y(int specY) => H - 1 - specY;

        /// <summary>스펙의 y[a,b] 사각형을 캔버스 좌표 사각형으로.</summary>
        private static void RectSpec(PixelCanvas c, int x0, int y0, int x1, int y1, Color col)
        {
            for (int sy = y0; sy < y1; sy++)
                for (int sx = x0; sx < x1; sx++)
                    c.Blend(sx, Y(sy), col);
        }

        private static void HLine(PixelCanvas c, int x0, int x1, int specY, Color col)
        {
            for (int x = x0; x < x1; x++) c.Blend(x, Y(specY), col);
        }

        private static void VLine(PixelCanvas c, int specX, int y0, int y1, Color col)
        {
            for (int y = y0; y < y1; y++) c.Blend(specX, Y(y), col);
        }

        // ════════════════════════════════════════════════ 배경(뒤): 벽·칠판·소품

        public static PixelCanvas BuildBack(bool retroStage)
        {
            var c = new PixelCanvas(W, H);

            DrawWall(c, retroStage);
            DrawCeiling(c);
            DrawFrames(c);              // 교훈 / 태극기 / 급훈 — 이게 빠지면 국적 불명이 된다
            DrawBoardLight(c);
            DrawBlackboard(c);
            DrawChalkTray(c);
            DrawTimetable(c);
            DrawDoor(c);
            DrawFloor(c);
            DrawPodium(c);
            DrawLectern(c);
            if (retroStage) DrawStove(c);

            return c;
        }

        private static void DrawWall(PixelCanvas c, bool retro)
        {
            // 현대 스테이지는 벽이 밝다. 벽색 스왑만으로 시대 전환이 성립한다.
            Color baseWall = retro ? ArtRamps.Wall[ArtRamps.BASE] : ArtRamps.Paper;
            Color hi = retro ? ArtRamps.Wall[ArtRamps.HI] : Color.Lerp(ArtRamps.Paper, Color.white, 0.35f);
            Color lo = ArtRamps.Wall[ArtRamps.S1];

            // 벽은 균일 조도(균제도 1:3 이내)라 S2 이하를 쓰지 않는다.
            for (int sy = 0; sy < 335; sy++)
            {
                // 창측(좌상단)이 밝고 우하단이 어둡다
                for (int x = 0; x < W; x++)
                {
                    float t = Mathf.Clamp01((x / (float)W) * 0.55f + (sy / 335f) * 0.45f);
                    Color col = t < 0.42f ? hi : (t < 0.86f ? baseWall : lo);
                    c.Blend(x, Y(sy), col);
                }
            }

            // 벽 몰딩 세로선 — 폭 1px, 간격 23px, 벽색보다 6% 어둡게
            Color molding = Color.Lerp(baseWall, ArtRamps.Ink, 0.06f);
            for (int x = 11; x < W; x += 23) VLine(c, x, 31, 335, molding);
        }

        private static void DrawCeiling(PixelCanvas c)
        {
            // 천장은 벽보다 한 단계 어둡되 분홍기가 돌면 안 되므로 벽색을 잉크로 눌러 만든다.
            Color ceil = Color.Lerp(ArtRamps.Wall[ArtRamps.BASE], ArtRamps.Ink, 0.28f);
            RectSpec(c, 0, 0, W, 31, ceil);
            HLine(c, 0, W, 31, Color.Lerp(ceil, ArtRamps.Ink, 0.35f));

            // 천장 형광등 2줄
            RectSpec(c, 60, 24, 210, 28, ArtRamps.Chalk[ArtRamps.BASE]);
            HLine(c, 60, 210, 28, ArtRamps.Haze[ArtRamps.BASE]);
            RectSpec(c, 60, 12, 210, 15, ArtRamps.Chalk[ArtRamps.S1]);
        }

        private static void DrawBlackboard(PixelCanvas c)
        {
            // 프레임 x[20,235] y[62,139], 두께 4px
            RectSpec(c, 20, 62, 235, 139, ArtRamps.Wood[ArtRamps.BASE]);
            HLine(c, 20, 235, 62, ArtRamps.Wood[ArtRamps.HI]);          // 상 1px HI
            VLine(c, 20, 62, 139, ArtRamps.Wood[ArtRamps.HI]);          // 좌 1px HI
            RectSpec(c, 20, 137, 235, 139, ArtRamps.Wood[ArtRamps.S1]); // 하 2px S1
            RectSpec(c, 233, 62, 235, 139, ArtRamps.Wood[ArtRamps.S1]); // 우 2px S1

            // 판면 x[24,231] y[66,135] = 207x69 = 정확히 3.00:1 (실측 3600x1200mm)
            int bx0 = 24, by0 = 66, bx1 = 231, by1 = 135;

            // (1) 단색 채움
            RectSpec(c, bx0, by0, bx1, by1, ArtRamps.Board[ArtRamps.BASE]);

            // (2) 중심 (127,99) 반경 130 방사 그라디언트 #2F5A50 alpha 0.35 -> 0
            Color glow = Palette.Hex("#2F5A50");
            for (int sy = by0; sy < by1; sy++)
                for (int x = bx0; x < bx1; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, sy), new Vector2(127, 99)) / 130f;
                    float a = Mathf.Clamp01(1f - d) * 0.35f;
                    if (a > 0f) c.Blend(x, Y(sy), ArtRamps.A(glow, a));
                }

            // (3) 네 모서리 안쪽 10px 비네트
            Color corner = ArtRamps.A(ArtRamps.Board[ArtRamps.S1], 0.25f);
            for (int sy = by0; sy < by1; sy++)
                for (int x = bx0; x < bx1; x++)
                {
                    int dx = Mathf.Min(x - bx0, bx1 - 1 - x);
                    int dy = Mathf.Min(sy - by0, by1 - 1 - sy);
                    if (dx < 10 && dy < 10)
                    {
                        float a = (1f - dx / 10f) * (1f - dy / 10f);
                        c.Blend(x, Y(sy), ArtRamps.A(ArtRamps.Board[ArtRamps.S1], 0.25f * a));
                    }
                }

            // (4) 지우개 자국 — 이게 없으면 그냥 초록 사각형으로 보인다.
            //     가로 45 x 세로 10 납작한 상향 아치, alpha 0.07, 전부 같은 방향(좌→우 상향)
            var rnd = new System.Random(20260819);
            for (int i = 0; i < 10; i++)
            {
                int sx = rnd.Next(bx0 + 4, bx1 - 50);
                int sy = rnd.Next(75, 125);
                for (int k = 0; k < 45; k++)
                {
                    float u = k / 44f;
                    int yy = sy - Mathf.RoundToInt(Mathf.Sin(u * Mathf.PI) * 5f) - Mathf.RoundToInt(u * 3f);
                    for (int t = 0; t < 10; t++)
                        c.Blend(sx + k, Y(yy + t), ArtRamps.A(ArtRamps.Chalk[ArtRamps.BASE], 0.07f));
                }
            }

            // 판서 흔적 몇 줄 (분필 텍스트 느낌)
            int[] rows = { 78, 92, 106, 120 };
            int[] lens = { 120, 96, 140, 74 };
            for (int i = 0; i < rows.Length; i++)
            {
                for (int k = 0; k < lens[i]; k++)
                {
                    if ((k / 3 + i) % 7 == 0) continue;  // 끊긴 획으로 글씨처럼
                    c.Blend(bx0 + 12 + k, Y(rows[i]), ArtRamps.A(ArtRamps.Chalk[ArtRamps.BASE], 0.55f));
                    if (k % 5 != 0) c.Blend(bx0 + 12 + k, Y(rows[i] + 1), ArtRamps.A(ArtRamps.Chalk[ArtRamps.S1], 0.30f));
                }
            }
        }

        private static void DrawChalkTray(PixelCanvas c)
        {
            // 분필받이 x[18,237] y[139,145]
            HLine(c, 18, 237, 139, ArtRamps.Wood[ArtRamps.HI]);
            RectSpec(c, 18, 140, 237, 145, ArtRamps.Wood[ArtRamps.S1]);
            HLine(c, 18, 237, 145, ArtRamps.Paper);

            // 분필 3개 (반드시 왼쪽)
            int[] cx = { 45, 52, 59 };
            foreach (int x in cx) RectSpec(c, x, 137, x + 2, 143, ArtRamps.Paper);

            // 칠판지우개 (반드시 오른쪽)
            RectSpec(c, 175, 137, 188, 141, ArtRamps.Dusk[ArtRamps.BASE]);
            RectSpec(c, 175, 141, 188, 143, ArtRamps.Chalk[ArtRamps.BASE]);
        }

        private static void DrawBoardLight(PixelCanvas c)
        {
            // 칠판등 바 x[45,210] y[57,61]
            RectSpec(c, 45, 57, 210, 61, ArtRamps.Chalk[ArtRamps.BASE]);
            HLine(c, 45, 210, 60, ArtRamps.Haze[ArtRamps.BASE]);

            // 광원 콘 — 상변 165 -> 하변 225 사다리꼴, alpha 0.18 -> 0
            for (int sy = 61; sy < 88; sy++)
            {
                float t = (sy - 61) / 27f;
                int halfW = Mathf.RoundToInt(Mathf.Lerp(165f, 225f, t) * 0.5f);
                int cxx = 127;
                float a = Mathf.Lerp(0.18f, 0f, t);
                for (int x = cxx - halfW; x <= cxx + halfW; x++)
                    c.Blend(x, Y(sy), ArtRamps.A(ArtRamps.Sun[ArtRamps.BASE], a));
            }
        }

        private static void DrawFrames(PixelCanvas c)
        {
            // 좌 교훈 액자 / 중앙 태극기 / 우 급훈 — 3개가 다 있어야 한국 교실로 읽힌다.
            DrawPlaque(c, 32, 40, 100, 54);
            DrawPlaque(c, 170, 40, 238, 54);
            DrawTaegukgi(c);
        }

        private static void DrawPlaque(PixelCanvas c, int x0, int y0, int x1, int y1)
        {
            // 바닥 그림자 y+1
            RectSpec(c, x0 + 1, y0 + 1, x1 + 1, y1 + 1, ArtRamps.A(ArtRamps.Haze[ArtRamps.BASE], 0.20f));
            RectSpec(c, x0, y0, x1, y1, ArtRamps.Wood[ArtRamps.S1]);            // 프레임 2px
            RectSpec(c, x0 + 2, y0 + 2, x1 - 2, y1 - 2, ArtRamps.Amber[ArtRamps.BASE]); // 안쪽 1px
            RectSpec(c, x0 + 3, y0 + 3, x1 - 3, y1 - 3, ArtRamps.Paper);        // 지면

            // 글자 자리 — 4글자 블록으로 암시 (실제 글자는 TMP로 얹지 않고 아트로 처리)
            int inner = (x1 - 3) - (x0 + 3);
            int cell = inner / 4;
            for (int i = 0; i < 4; i++)
            {
                int gx = x0 + 3 + i * cell + cell / 2 - 3;
                for (int r = 0; r < 3; r++)
                    RectSpec(c, gx, y0 + 5 + r * 2, gx + 6, y0 + 6 + r * 2, ArtRamps.Ink);
            }
        }

        private static void DrawTaegukgi(PixelCanvas c)
        {
            // 외곽 x[115,155] y[32,60], 프레임 2px → 깃면 36x24 (정확히 3:2)
            RectSpec(c, 116, 33, 156, 61, ArtRamps.A(ArtRamps.Haze[ArtRamps.BASE], 0.20f));
            RectSpec(c, 115, 32, 155, 60, ArtRamps.Wood[ArtRamps.S1]);
            int fx0 = 117, fy0 = 34, fw = 36, fh = 24;
            RectSpec(c, fx0, fy0, fx0 + fw, fy0 + fh, ArtRamps.Paper);

            // 태극 원 — 중심 로컬 (18,12), 지름 12
            float ccx = fx0 + 18f, ccy = fy0 + 12f, r = 6f;
            // 경계선은 깃면 대각선과 평행 → 33.69°, 빨강이 위
            float ang = Mathf.Atan2(24f, 36f);
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            Vector2 nrm = new Vector2(-dir.y, dir.x);

            // 원색 그대로 쓰면 팔레트에서 튀므로 종이색과 15% 블렌드한 값
            Color red = Palette.Hex("#D14A54");
            Color blue = Palette.Hex("#24549F");

            for (int sy = fy0; sy < fy0 + fh; sy++)
                for (int x = fx0; x < fx0 + fw; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f - ccx, (sy + 0.5f) - ccy);
                    if (p.magnitude > r) continue;
                    float side = Vector2.Dot(p, nrm);
                    // 작은 반원 2개 (지름 6) — 중심은 경계선 위 ±3px
                    Vector2 up = new Vector2(ccx, ccy) + dir * 3f;
                    Vector2 dn = new Vector2(ccx, ccy) - dir * 3f;
                    float du = Vector2.Distance(new Vector2(x + 0.5f, sy + 0.5f), up);
                    float dd = Vector2.Distance(new Vector2(x + 0.5f, sy + 0.5f), dn);
                    bool isRed = side < 0f;
                    if (du <= 3f) isRed = true;
                    if (dd <= 3f) isRed = false;
                    c.Blend(x, Y(sy), isRed ? red : blue);
                }

            // 4괘 — 6x5, 바 3개 두께 1px, 간격 1px. 6x5에서 회전시키면 판독 불가라 축 정렬 수평 바.
            DrawTrigram(c, fx0 + 9 - 3, fy0 + 6 - 2, new[] { true, true, true });    // 건
            DrawTrigram(c, fx0 + 27 - 3, fy0 + 6 - 2, new[] { false, true, false }); // 감
            DrawTrigram(c, fx0 + 9 - 3, fy0 + 18 - 2, new[] { true, false, true });  // 이
            DrawTrigram(c, fx0 + 27 - 3, fy0 + 18 - 2, new[] { false, false, false });// 곤
        }

        private static void DrawTrigram(PixelCanvas c, int x, int y, bool[] solid)
        {
            for (int i = 0; i < 3; i++)
            {
                int by = y + i * 2;
                if (solid[i]) RectSpec(c, x, by, x + 6, by + 1, ArtRamps.Ink);
                else
                {
                    RectSpec(c, x, by, x + 2, by + 1, ArtRamps.Ink);
                    RectSpec(c, x + 4, by, x + 6, by + 1, ArtRamps.Ink);
                }
            }
        }

        private static void DrawTimetable(PixelCanvas c)
        {
            // A4 시간표 x[203,229] y[72,108] + 자석 4개
            RectSpec(c, 204, 73, 230, 109, ArtRamps.A(ArtRamps.Board[ArtRamps.S1], 0.30f));
            RectSpec(c, 203, 72, 229, 108, ArtRamps.Paper);
            RectSpec(c, 203, 72, 229, 76, ArtRamps.Dusk[ArtRamps.BASE]);   // 헤더행

            Color grid = ArtRamps.Haze[ArtRamps.BASE];
            for (int i = 1; i < 6; i++) VLine(c, 203 + 4 + (i - 1) * 4, 72, 108, ArtRamps.A(grid, 0.5f));
            for (int r = 1; r < 8; r++) HLine(c, 203, 229, 76 + r * 4, ArtRamps.A(grid, 0.5f));

            Color[] mag = { ArtRamps.Blush[ArtRamps.BASE], ArtRamps.Amber[ArtRamps.BASE], ArtRamps.Haze[ArtRamps.BASE], ArtRamps.Sun[ArtRamps.BASE] };
            int[,] mp = { { 204, 73 }, { 227, 73 }, { 204, 106 }, { 227, 106 } };
            for (int i = 0; i < 4; i++)
                c.FillEllipse(mp[i, 0], Y(mp[i, 1]), 1.5f, 1.5f, mag[i]);
        }

        private static void DrawDoor(PixelCanvas c)
        {
            // 교사용 앞문 — 화면 오른쪽 끝
            RectSpec(c, 239, 75, 270, 181, ArtRamps.Wood[ArtRamps.S1]);
            RectSpec(c, 241, 77, 268, 181, ArtRamps.Wood[ArtRamps.BASE]);
            VLine(c, 241, 77, 181, ArtRamps.Wood[ArtRamps.HI]);

            // 유리창
            RectSpec(c, 244, 80, 266, 108, ArtRamps.Chalk[ArtRamps.BASE]);
            RectSpec(c, 244, 80, 266, 108, ArtRamps.A(ArtRamps.Haze[ArtRamps.BASE], 0.12f));
            for (int k = 0; k < 22; k++)
            {
                c.Blend(244 + k, Y(80 + k), ArtRamps.A(ArtRamps.Paper, 0.6f));
                if (k < 14) c.Blend(250 + k, Y(80 + k), ArtRamps.A(ArtRamps.Paper, 0.4f));
            }

            // 손잡이
            RectSpec(c, 246, 130, 249, 137, ArtRamps.Chalk[ArtRamps.BASE]);
        }

        private static void DrawFloor(PixelCanvas c)
        {
            // 마루 — 교단 아래부터
            for (int sy = 181; sy < H; sy++)
            {
                float t = Mathf.InverseLerp(181, H, sy);
                Color col = t < 0.35f ? ArtRamps.Floor[ArtRamps.S1] : ArtRamps.Floor[ArtRamps.BASE];
                for (int x = 0; x < W; x++) c.Blend(x, Y(sy), col);
            }
            // 판자 이음선
            for (int x = 0; x < W; x += 24) VLine(c, x, 181, H, ArtRamps.Floor[ArtRamps.S2]);
            HLine(c, 0, W, 181, ArtRamps.Floor[ArtRamps.S3]);
        }

        private static void DrawPodium(PixelCanvas c)
        {
            // 교단 x[10,235] y[181,188] 사다리꼴
            RectSpec(c, 10, 181, 235, 188, ArtRamps.Floor[ArtRamps.BASE]);
            HLine(c, 10, 235, 181, ArtRamps.Floor[ArtRamps.HI]);
            RectSpec(c, 10, 185, 235, 188, ArtRamps.Floor[ArtRamps.S1]);
        }

        private static void DrawLectern(PixelCanvas c)
        {
            // 교탁 상판 + 몸통
            RectSpec(c, 92, 150, 178, 156, ArtRamps.Wood[ArtRamps.HI]);
            HLine(c, 92, 178, 150, ArtRamps.Paper);
            RectSpec(c, 97, 156, 172, 188, ArtRamps.Wood[ArtRamps.BASE]);
            VLine(c, 119, 156, 188, ArtRamps.Wood[ArtRamps.S1]);
            VLine(c, 150, 156, 188, ArtRamps.Wood[ArtRamps.S1]);
            RectSpec(c, 97, 156, 101, 188, ArtRamps.A(ArtRamps.Wood[ArtRamps.S1], 0.35f));

            // 바닥 접지 그림자
            for (int k = 0; k < 3; k++)
                RectSpec(c, 97 - k, 188 + k, 172 + k, 189 + k, ArtRamps.A(ArtRamps.Dusk[ArtRamps.BASE], 0.18f));
        }

        private static void DrawStove(PixelCanvas c)
        {
            // 90년대 확정 소품 — 난로 + 연통. 이거 하나로 시대가 확정된다.
            //
            // 배치 주의: 칠판 프레임이 x[20,235] y[62,139], 액자가 y[40,54]를 차지한다.
            // 연통은 칠판 왼쪽(x<20)으로 올린 뒤 천장과 액자 사이의 빈 띠 y[32,38]로만 지나가야
            // 칠판을 관통하지 않는다.
            RectSpec(c, 6, 252, 30, 283, ArtRamps.Ink);
            RectSpec(c, 8, 254, 28, 281, ArtRamps.Hair[ArtRamps.S1]);
            VLine(c, 8, 254, 281, ArtRamps.Hair[ArtRamps.HI]);

            // 화구 — 안쪽에서 새어나오는 불빛
            for (int sy = 262; sy < 271; sy++)
                for (int x = 12; x < 24; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, sy), new Vector2(18, 266)) / 6f;
                    if (d > 1f) continue;
                    c.Blend(x, Y(sy), Color.Lerp(ArtRamps.Sun[ArtRamps.BASE], ArtRamps.Amber[ArtRamps.BASE], Mathf.Clamp01(d)));
                }

            // 연통 — 수직 구간 (칠판 왼쪽)
            RectSpec(c, 10, 38, 16, 252, ArtRamps.Dusk[ArtRamps.BASE]);
            VLine(c, 15, 38, 252, ArtRamps.Haze[ArtRamps.BASE]);
            // 연통 — 수평 구간 (천장과 액자 사이 빈 띠)
            RectSpec(c, 10, 32, 270, 38, ArtRamps.Dusk[ArtRamps.BASE]);
            HLine(c, 10, 270, 32, ArtRamps.Haze[ArtRamps.BASE]);
        }

        // ════════════════════════════════════════════════ 배경(앞): 학생·책상

        public static PixelCanvas BuildFront()
        {
            var c = new PixelCanvas(W, H);

            // 앞줄 학생 3명 — x중심 47 / 135 / 222, y[230,292]
            int[] cx = { 47, 135, 222 };
            int[] dy = { -1, 0, 1 };
            for (int i = 0; i < 3; i++)
            {
                var s = CharacterGenerator.BuildStudent(i);
                c.Composite(s, cx[i] - 28, Y(292 + dy[i]));
            }

            DrawPlayerDesk(c);
            return c;
        }

        private static void DrawPlayerDesk(PixelCanvas c)
        {
            // 상판 사다리꼴 — 윗변 x[28,243] @301, 아랫변 x[-5,275] @334 (45° 측면)
            for (int sy = 301; sy <= 334; sy++)
            {
                float t = (sy - 301) / 33f;
                int x0 = Mathf.RoundToInt(Mathf.Lerp(28, -5, t));
                int x1 = Mathf.RoundToInt(Mathf.Lerp(243, 275, t));
                Color col = sy < 304 ? ArtRamps.Paper : (sy > 331 ? ArtRamps.Wood[ArtRamps.S1] : ArtRamps.Wood[ArtRamps.HI]);
                for (int x = x0; x < x1; x++) c.Blend(x, Y(sy), col);
            }
            // 앞모서리 띠
            RectSpec(c, -5, 331, 275, 334, ArtRamps.Wood[ArtRamps.S1]);

            // 책상 몸통 — 단색 큰 면은 조악해 보이므로 나뭇결과 아래쪽 감쇠를 넣는다
            for (int sy = 334; sy < H; sy++)
            {
                float t = Mathf.InverseLerp(334, H, sy);
                Color band = Color.Lerp(ArtRamps.Wood[ArtRamps.S2], ArtRamps.Wood[ArtRamps.S3], t * 0.85f);
                for (int x = -5; x < 275; x++) c.Blend(x, Y(sy), band);
            }
            // 세로 나뭇결 — 50% 체커 디더로 두 톤을 섞는다 (밴딩 방지)
            for (int x = 6; x < 275; x += 17)
            {
                for (int sy = 336; sy < H; sy++)
                {
                    if (((x + sy) & 1) == 0) continue;
                    c.Blend(x, Y(sy), ArtRamps.A(ArtRamps.Wood[ArtRamps.S3], 0.45f));
                    c.Blend(x + 1, Y(sy), ArtRamps.A(ArtRamps.Wood[ArtRamps.S1], 0.18f));
                }
            }
            // 상판과 몸통이 만나는 접지 AO 2px
            RectSpec(c, -5, 334, 275, 336, ArtRamps.A(ArtRamps.Wood[ArtRamps.AO], 0.35f));

            // 책상 위 소품 — 교과서 + 필통
            RectSpec(c, 30, 293, 64, 303, ArtRamps.Blush[ArtRamps.BASE]);
            RectSpec(c, 30, 293, 33, 303, ArtRamps.Paper);
            HLine(c, 30, 64, 293, ArtRamps.Blush[ArtRamps.S1]);
            RectSpec(c, 210, 296, 236, 303, ArtRamps.Dusk[ArtRamps.BASE]);
            HLine(c, 210, 236, 296, ArtRamps.Dusk[ArtRamps.HI]);
            HLine(c, 210, 236, 299, ArtRamps.Chalk[ArtRamps.BASE]);

            // 캐스트 그림자 — 광원이 좌상단이므로 우하단으로, 길이 = 높이 x 0.6, 단색 1톤
            RectSpec(c, 64, 297, 70, 303, ArtRamps.A(ArtRamps.Wood[ArtRamps.S2], 0.55f));
            RectSpec(c, 236, 300, 240, 303, ArtRamps.A(ArtRamps.Wood[ArtRamps.S2], 0.55f));
        }
    }
}
