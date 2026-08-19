using UnityEngine;

namespace Molae.EditorTools
{
    /// <summary>
    /// 캐릭터 스프라이트 절차 생성기.
    ///
    /// 핵심 원칙 (아트 스펙):
    ///  - 검정 1px 풀아웃라인 금지. 광원측(좌상단) 외곽선은 생략, 그림자측(우하단)만 잉크 1px.
    ///  - 캐릭터당 색 상한: 학생 6색, 선생님 8색.
    ///  - 팔은 몸통과 1px 배경 갭을 남겨 실루엣이 한 덩어리로 뭉치는 걸 막는다.
    ///  - 학생 뒷모습은 목 피부를 3px 노출해야 머리와 몸이 붙어 보이지 않는다.
    ///  - 선생님 3상태는 전체 폭이 44/38/48로 갈라져 색맹·저조도에서도 상태가 읽힌다.
    /// </summary>
    public static class CharacterGenerator
    {
        public enum TeacherState { Writing, Suspect, Watching }

        // ══════════════════════════════════════════ 선생님 48x112

        public const int TW = 48, TH = 112;
        private static int TY(int specY) => TH - 1 - specY;

        private static void R(PixelCanvas c, int x0, int y0, int x1, int y1, Color col, int height)
        {
            for (int sy = y0; sy < y1; sy++)
                for (int x = x0; x < x1; x++)
                    c.Blend(x, height - 1 - sy, col);
        }

        public static PixelCanvas BuildTeacher(TeacherState state)
        {
            var c = new PixelCanvas(TW, TH);
            bool front = state == TeacherState.Watching;
            bool back = state == TeacherState.Writing;

            // ── 하의 y[74,112], 다리 사이 1px 갭 x=24
            R(c, 12, 74, 24, 112, ArtRamps.Hair[ArtRamps.BASE], TH);
            R(c, 25, 74, 37, 112, ArtRamps.Hair[ArtRamps.BASE], TH);
            R(c, 12, 74, 15, 112, ArtRamps.Hair[ArtRamps.HI], TH);      // 좌상 HI
            R(c, 34, 74, 37, 112, ArtRamps.Hair[ArtRamps.S1], TH);      // 우하 S1

            // ── 상의 y[34,74].
            // 몸통 자체는 x[11,37](폭 26)이고, 어깨선 폭 42는 몸통 + 양쪽 팔(각 8px)을 합친 값이다.
            // 몸통을 42폭으로 통째로 칠하면 팔이 들어갈 자리가 없어져 밋밋한 사다리꼴이 된다.
            for (int sy = 34; sy < 74; sy++)
            {
                float t = (sy - 34) / 40f;
                // 어깨(넓음) → 허리(좁음). 사람 실루엣은 위가 넓다.
                float halfW = Mathf.Lerp(13.5f, 11.5f, t);

                for (int x = 0; x < TW; x++)
                {
                    int dx = Mathf.Abs(x - 24);
                    // 어깨 슬로프: x가 8px 갈 때마다 y+1 (직선 어깨 금지)
                    int shoulderY = 34 + dx / 8;
                    if (sy < shoulderY) continue;
                    if (dx > halfW) continue;

                    Color col = ArtRamps.Dusk[ArtRamps.BASE];
                    if (x < 24 - halfW + 4 && sy < 46) col = ArtRamps.Dusk[ArtRamps.HI];  // 좌상 HI
                    else if (x > 24 + halfW - 4) col = ArtRamps.Dusk[ArtRamps.S1];        // 우하 S1
                    if (sy > 66) col = ArtRamps.Dusk[ArtRamps.S1];                        // 허리 그늘
                    c.Blend(x, TY(sy), col);
                }
            }
            // 어깨 윗면 하이라이트 1px — 몸통과 팔이 하나로 뭉치는 걸 막는 분리선
            R(c, 12, 34, 36, 35, ArtRamps.Dusk[ArtRamps.HI], TH);

            // ── 셔츠 칼라 V자 + 넥타이 (앞모습에서만)
            if (!back)
            {
                R(c, 20, 34, 28, 37, ArtRamps.Paper, TH);
                R(c, 22, 35, 26, 38, ArtRamps.Blush[ArtRamps.BASE], TH);
                R(c, 23, 38, 26, 48, ArtRamps.Blush[ArtRamps.BASE], TH);
                R(c, 23, 38, 24, 48, ArtRamps.Blush[ArtRamps.HI], TH);
            }

            // ── 팔 (몸통과 1px 갭). 상태별로 위치가 다르다.
            DrawTeacherArms(c, state);

            // ── 목 x[17,31] y[30,34]
            R(c, 17, 30, 31, 34, ArtRamps.Skin[ArtRamps.S1], TH);
            R(c, 17, 32, 31, 34, ArtRamps.Skin[ArtRamps.S2], TH);   // 턱밑 그림자

            // ── 머리 실루엣 x[11,37] y[0,30] (26x30 라운드사각, 상단 2행은 폭 20)
            // 뒷모습(판서)에서는 얼굴이 아니라 뒤통수이므로 살색을 깔지 않는다.
            Color headFill = back ? ArtRamps.Hair[ArtRamps.BASE] : ArtRamps.Skin[ArtRamps.BASE];
            Color headHi = back ? ArtRamps.Hair[ArtRamps.HI] : ArtRamps.Skin[ArtRamps.HI];
            Color headLo = back ? ArtRamps.Hair[ArtRamps.S1] : ArtRamps.Skin[ArtRamps.S1];

            for (int sy = 0; sy < 30; sy++)
            {
                int x0 = 11, x1 = 37;
                if (sy < 2) { x0 = 14; x1 = 34; }
                int inset = 0;
                if (sy < 4) inset = 4 - sy;
                else if (sy > 25) inset = sy - 25;
                x0 += inset; x1 -= inset;

                for (int x = x0; x < x1; x++)
                {
                    Color col = headFill;
                    if (x < x0 + 3 && sy < 12) col = headHi;
                    else if (x > x1 - 4) col = headLo;
                    c.Blend(x, TY(sy), col);
                }
            }

            // ── 머리카락 매스. 뒷모습은 뒤통수 전체를, 앞모습은 이마 위만 덮는다.
            int hairBottom = back ? 27 : 14;
            for (int sy = 0; sy < hairBottom; sy++)
            {
                int inset = sy < 4 ? 4 - sy : 0;
                int hx0 = 10 + inset, hx1 = 38 - inset;
                if (back && sy > 22) { int s = (sy - 22) * 2; hx0 += s; hx1 -= s; }
                for (int x = hx0; x < hx1; x++)
                {
                    Color col = ArtRamps.Hair[ArtRamps.BASE];
                    if (x > 33) col = ArtRamps.Hair[ArtRamps.S1];
                    c.Blend(x, TY(sy), col);
                }
            }
            R(c, 13, 1, 22, 3, ArtRamps.Hair[ArtRamps.HI], TH);   // 좌상 1px HI 띠 (2px 이상 금지)
            if (!back)
            {
                R(c, 10, 14, 13, 22, ArtRamps.Hair[ArtRamps.BASE], TH);
                R(c, 35, 14, 38, 22, ArtRamps.Hair[ArtRamps.S1], TH);
                // 귀 — 앞모습에서 머리 폭을 깨서 실루엣이 원형으로 뭉치는 걸 막는다
                R(c, 9, 18, 11, 23, ArtRamps.Skin[ArtRamps.S1], TH);
                R(c, 37, 18, 39, 23, ArtRamps.Skin[ArtRamps.S2], TH);
            }
            else
            {
                // 뒷모습에서는 목덜미 살을 3px 노출해야 머리와 몸이 한 덩어리로 붙지 않는다
                R(c, 19, 27, 29, 31, ArtRamps.Skin[ArtRamps.S1], TH);
            }

            // ── 얼굴 (판서 상태는 뒷모습이므로 얼굴 픽셀 0개)
            if (!back) DrawTeacherFace(c, state);

            // ── 셀렉티브 아웃라인 + 림라이트
            SelectiveOutline(c);
            RimLight(c, 2, 20);        // 머리
            RimLight(c, 36, 60);       // 어깨~팔

            return c;
        }

        private static void DrawTeacherArms(PixelCanvas c, TeacherState state)
        {
            Color arm = ArtRamps.Dusk[ArtRamps.BASE];
            Color armHi = ArtRamps.Dusk[ArtRamps.HI];
            Color armS1 = ArtRamps.Dusk[ArtRamps.S1];
            Color skin = ArtRamps.Skin[ArtRamps.BASE];

            if (state == TeacherState.Writing)
            {
                // 오른팔을 칠판 쪽으로 — 손끝이 어깨선 y34보다 12px 위(y=22). 전체 폭 44.
                DrawTaperArm(c, 36, 38, 42, 24, 3.5f, 2.5f, armS1, arm);
                R(c, 40, 19, 45, 24, skin, TH);                     // 손
                R(c, 41, 13, 43, 20, ArtRamps.Paper, TH);           // 분필
                DrawTaperArm(c, 12, 38, 9, 66, 3.5f, 2.5f, arm, armHi);   // 왼팔은 내림
                R(c, 6, 64, 11, 69, skin, TH);
            }
            else if (state == TeacherState.Suspect)
            {
                // 팔 내림. 전체 폭 38 — 세 상태 중 가장 좁다.
                DrawTaperArm(c, 13, 38, 11, 64, 3.5f, 2.5f, arm, armHi);
                DrawTaperArm(c, 35, 38, 37, 64, 3.5f, 2.5f, armS1, arm);
                R(c, 8, 62, 13, 67, skin, TH);
                R(c, 35, 62, 40, 67, skin, TH);
            }
            else // Watching — 양팔을 몸통에서 3px씩 벌린다. 전체 폭 48.
            {
                DrawTaperArm(c, 12, 38, 6, 64, 4f, 3f, arm, armHi);
                DrawTaperArm(c, 36, 38, 42, 64, 4f, 3f, armS1, arm);
                R(c, 2, 62, 8, 68, skin, TH);
                R(c, 40, 62, 46, 68, skin, TH);
            }
        }

        /// <summary>어깨폭에서 손목폭으로 테이퍼되는 팔.</summary>
        private static void DrawTaperArm(PixelCanvas c, int xTop, int yTop, int xBot, int yBot,
                                         float hwTop, float hwBot, Color body, Color hi)
        {
            for (int sy = yTop; sy < yBot; sy++)
            {
                float t = (sy - yTop) / (float)(yBot - yTop);
                float cx = Mathf.Lerp(xTop, xBot, t);
                float hw = Mathf.Lerp(hwTop, hwBot, t);
                for (int x = Mathf.RoundToInt(cx - hw); x <= Mathf.RoundToInt(cx + hw); x++)
                    c.Blend(x, TY(sy), x < cx ? hi : body);
            }
        }

        private static void DrawTeacherFace(PixelCanvas c, TeacherState state)
        {
            bool caught = state == TeacherState.Watching;

            // 눈 4x5 두 개 — y[19,24]는 머리 높이 30의 63~80% 지점(치비 보정)
            DrawEye(c, 18, 19);
            DrawEye(c, 26, 19);

            // 눈썹 4x1 y=17
            if (caught)
            {
                // 발각: 안쪽 끝 2px 하강 + 바깥쪽 1px 상승
                R(c, 18, 18, 21, 19, ArtRamps.Hair[ArtRamps.BASE], TH);
                R(c, 21, 19, 22, 20, ArtRamps.Hair[ArtRamps.BASE], TH);
                R(c, 27, 19, 28, 20, ArtRamps.Hair[ArtRamps.BASE], TH);
                R(c, 28, 18, 31, 19, ArtRamps.Hair[ArtRamps.BASE], TH);
            }
            else
            {
                // 의심: 안쪽 끝 1px 하강
                R(c, 18, 17, 22, 18, ArtRamps.Hair[ArtRamps.BASE], TH);
                R(c, 21, 18, 22, 19, ArtRamps.Hair[ArtRamps.BASE], TH);
                R(c, 26, 18, 27, 19, ArtRamps.Hair[ArtRamps.BASE], TH);
                R(c, 26, 17, 30, 18, ArtRamps.Hair[ArtRamps.BASE], TH);
            }

            // 입 — 발각 시에만 열린 사각형
            if (caught)
            {
                R(c, 22, 26, 26, 29, ArtRamps.Ink, TH);
                R(c, 23, 27, 25, 28, ArtRamps.Blush[ArtRamps.BASE], TH);
            }
            else
            {
                R(c, 23, 27, 25, 28, ArtRamps.Ink, TH);
            }
            // 코는 그리지 않는다.
        }

        private static void DrawEye(PixelCanvas c, int x, int y)
        {
            R(c, x, y, x + 4, y + 5, ArtRamps.Paper, TH);              // 흰자 바탕
            R(c, x, y, x + 3, y + 4, ArtRamps.Ink, TH);                // 동공 3x4
            c.Blend(x, TY(y), ArtRamps.Paper);                          // 좌상단 1px 하이라이트
        }

        // ══════════════════════════════════════════ 학생 56x62

        public const int SW = 56, SH = 62;

        public static PixelCanvas BuildStudent(int variant)
        {
            var c = new PixelCanvas(SW, SH);

            Color[] uniforms = { ArtRamps.Dusk[ArtRamps.BASE], ArtRamps.Haze[ArtRamps.BASE], ArtRamps.Hair[ArtRamps.BASE] };
            Color[] uniformS1 = { ArtRamps.Dusk[ArtRamps.S1], ArtRamps.Haze[ArtRamps.S1], ArtRamps.Hair[ArtRamps.S1] };
            Color uni = uniforms[variant % 3];
            Color uniS = uniformS1[variant % 3];

            // ── 어깨 y[33,58]. y=58부터는 책상이 완전히 가리므로 안 그린다.
            for (int sy = 33; sy < 58; sy++)
            {
                float t = (sy - 33) / 25f;
                int halfW = Mathf.RoundToInt(Mathf.Lerp(20f, 26f, Mathf.Sqrt(t)));
                for (int x = 28 - halfW; x < 28 + halfW; x++)
                {
                    Color col = uni;
                    if (x < 28 - halfW + 4 && sy < 42) col = Color.Lerp(uni, ArtRamps.Paper, 0.18f);
                    else if (x > 28 + halfW - 5) col = uniS;
                    c.Blend(x, SH - 1 - sy, col);
                }
            }

            // ── 칼라: y=34에 20x1 + 양 끝 2x2 위로 꺾인 탭
            for (int x = 18; x < 38; x++) c.Blend(x, SH - 1 - 34, ArtRamps.Paper);
            for (int k = 0; k < 2; k++)
                for (int j = 0; j < 2; j++)
                {
                    c.Blend(17 + k, SH - 1 - (33 + j), ArtRamps.Paper);
                    c.Blend(37 + k, SH - 1 - (33 + j), ArtRamps.Paper);
                }

            // ── 목 x[23,33] y[30,33] — 이 피부 노출이 없으면 머리와 몸이 한 덩어리가 된다.
            for (int sy = 30; sy < 33; sy++)
                for (int x = 23; x < 33; x++)
                    c.Blend(x, SH - 1 - sy, sy > 31 ? ArtRamps.Skin[ArtRamps.S2] : ArtRamps.Skin[ArtRamps.S1]);

            // ── 머리(뒤통수형) x[12,44] y[0,30], y[26,30]에서 폭 32→18
            for (int sy = 0; sy < 30; sy++)
            {
                int x0 = 12, x1 = 44;
                if (sy >= 26) { int shrink = (sy - 26) * 2 + 1; x0 += shrink; x1 -= shrink; }
                int inset = sy < 4 ? 4 - sy : 0;
                x0 += inset; x1 -= inset;

                for (int x = x0; x < x1; x++)
                {
                    Color col = ArtRamps.Hair[ArtRamps.BASE];
                    if (x > x1 - 5) col = ArtRamps.Hair[ArtRamps.S1];
                    c.Blend(x, SH - 1 - sy, col);
                }
            }
            for (int x = 16; x < 30; x++) c.Blend(x, SH - 1 - 1, ArtRamps.Hair[ArtRamps.HI]);  // 상단 1px HI

            // ── 헤어 실루엣 변형
            ApplyHairVariant(c, variant);

            // 의자 등받이는 그리지 않는다.
            // 학생 어깨가 이미 넓어서 한쪽 끝에만 삐죽 보이고, 그 비대칭이 노이즈로 읽힌다.
            // 어차피 y=58부터는 책상 스프라이트가 완전히 가린다 — 안 그린 픽셀은 망칠 수 없다.

            SelectiveOutlineOn(c);
            return c;
        }

        private static void ApplyHairVariant(PixelCanvas c, int variant)
        {
            switch (variant % 5)
            {
                case 1: // 포니테일 — 머리 뒤 7x17 돌출
                    for (int sy = 12; sy < 29; sy++)
                        for (int x = 44; x < 51; x++)
                            c.Blend(x, SH - 1 - sy, ArtRamps.Hair[ArtRamps.S1]);
                    break;
                case 2: // 짧은머리 — 양쪽 귀
                    for (int sy = 15; sy < 19; sy++)
                    {
                        c.Blend(10, SH - 1 - sy, ArtRamps.Skin[ArtRamps.S1]);
                        c.Blend(11, SH - 1 - sy, ArtRamps.Skin[ArtRamps.BASE]);
                        c.Blend(44, SH - 1 - sy, ArtRamps.Skin[ArtRamps.S1]);
                        c.Blend(45, SH - 1 - sy, ArtRamps.Skin[ArtRamps.S2]);
                    }
                    break;
                case 3: // 곱슬 — 외곽 1px 요철 3개
                    for (int i = 0; i < 3; i++)
                    {
                        int yy = 6 + i * 7;
                        c.Blend(11, SH - 1 - yy, ArtRamps.Hair[ArtRamps.BASE]);
                        c.Blend(44, SH - 1 - (yy + 3), ArtRamps.Hair[ArtRamps.S1]);
                    }
                    break;
                case 4: // 묶음머리 — 정수리 위 10x7 혹
                    for (int sy = -6; sy < 1; sy++)
                        for (int x = 23; x < 33; x++)
                            if (sy >= 0) c.Blend(x, SH - 1 - sy, ArtRamps.Hair[ArtRamps.BASE]);
                    break;
            }
        }

        // ══════════════════════════════════════════ 공통 후처리

        /// <summary>
        /// 셀렉티브 아웃라인. 광원 방향 L=(-0.707,-0.707)과의 내적이 양수(좌상단)면 생략,
        /// 음수(우하단)면 잉크 1px. 검정 풀아웃라인을 두르면 스티커처럼 보인다.
        /// </summary>
        public static void SelectiveOutline(PixelCanvas c) => SelectiveOutlineOn(c);

        public static void SelectiveOutlineOn(PixelCanvas c)
        {
            var add = new System.Collections.Generic.List<Vector2Int>();
            for (int y = 0; y < c.Height; y++)
            {
                for (int x = 0; x < c.Width; x++)
                {
                    if (c.Get(x, y).a > 0.35f) continue;

                    // 인접한 불투명 픽셀 방향으로 법선을 추정한다
                    float nx = 0f, ny = 0f;
                    bool near = false;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            if (c.Get(x + dx, y + dy).a <= 0.35f) continue;
                            near = true; nx -= dx; ny -= dy;
                        }
                    if (!near) continue;

                    var n = new Vector2(nx, ny).normalized;
                    // 캔버스는 좌하단 원점이므로 화면 위쪽 = +y. 광원은 좌상단 = (-1,+1) 방향.
                    float d = Vector2.Dot(n, new Vector2(-0.707f, 0.707f));
                    if (d > 0.25f) continue;   // 광원측 외곽선 생략
                    add.Add(new Vector2Int(x, y));
                }
            }
            foreach (var p in add) c.Set(p.x, p.y, ArtRamps.Ink);
        }

        /// <summary>
        /// 창가 반대편(우측) 외곽 1px 림라이트.
        /// 선생님 교복(#494D7E)과 칠판(#274C43)은 명도가 붙어 실루엣이 죽으므로 필수.
        /// 전체를 두르면 스티커로 보이니 지정 구간에만 넣는다.
        /// </summary>
        private static void RimLight(PixelCanvas c, int specY0, int specY1)
        {
            for (int sy = specY0; sy < specY1; sy++)
            {
                int y = TH - 1 - sy;
                for (int x = c.Width - 1; x >= 1; x--)
                {
                    if (c.Get(x, y).a <= 0.35f) continue;
                    if (c.Get(x, y) == ArtRamps.Ink) { c.Set(x, y, ArtRamps.Sun[ArtRamps.BASE]); }
                    else c.Set(x, y, ArtRamps.Sun[ArtRamps.BASE]);
                    break;
                }
            }
        }
    }
}
