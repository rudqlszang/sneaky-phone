using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Molae.EditorTools
{
    /// <summary>
    /// 모든 아트 에셋을 굽는 엔트리 포인트.  메뉴: Molae / Rebuild All Art
    ///
    /// 해상도 규약: 네이티브 270x480 → Unity에서 정확히 x4 = 1080x1920.
    /// 임포트 설정도 여기서 강제한다 (Point / None / PPU 64 / mipmap off).
    /// </summary>
    public static class MolaeArtBuild
    {
        public const int W = ClassroomGenerator.W;   // 270
        public const int H = ClassroomGenerator.H;   // 480
        public const int PPU = 64;

        private const string Root = "Assets/_Molae/Art/Sprites";
        private static int Y(int specY) => H - 1 - specY;

        [MenuItem("Molae/Rebuild All Art")]
        public static void RebuildAll()
        {
            var written = new List<string>();

            // ── 씬 레이어 ──
            Save(ClassroomGenerator.BuildBack(retroStage: true), $"{Root}/Classroom/bg_back.png", written);
            Save(ClassroomGenerator.BuildFront(), $"{Root}/Classroom/bg_front.png", written);

            // ── 선생님 3상태 ──
            Save(CharacterGenerator.BuildTeacher(CharacterGenerator.TeacherState.Writing), $"{Root}/Teacher/teacher_writing.png", written);
            Save(CharacterGenerator.BuildTeacher(CharacterGenerator.TeacherState.Suspect), $"{Root}/Teacher/teacher_suspect.png", written);
            Save(CharacterGenerator.BuildTeacher(CharacterGenerator.TeacherState.Watching), $"{Root}/Teacher/teacher_watching.png", written);

            // ── 플레이어 2포즈 ──
            Save(BuildPlayer(upright: true), $"{Root}/Player/player_upright.png", written);
            Save(BuildPlayer(upright: false), $"{Root}/Player/player_phone.png", written);

            // ── 폰 + 손 ──
            Save(BuildPhone(), $"{Root}/Player/phone_hands.png", written);

            // ── 조명 오버레이 ──
            Save(LightingGenerator.BuildGodray(), $"{Root}/UI/fx_godray.png", written);
            Save(LightingGenerator.BuildDust(), $"{Root}/UI/fx_dust.png", written);
            Save(LightingGenerator.BuildVignette(0.22f, 0.62f, 1.28f), $"{Root}/UI/fx_vignette_safe.png", written);
            Save(LightingGenerator.BuildVignette(0.48f, 0.42f, 1.15f), $"{Root}/UI/fx_vignette_danger.png", written);
            Save(LightingGenerator.BuildGrain(), $"{Root}/UI/fx_grain.png", written);

            // ── UI 9-slice ──
            UiSpriteGenerator.BuildAll(written);

            AssetDatabase.Refresh();
            foreach (var p in written) ApplyImportSettings(p);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Molae] 아트 재생성 완료: {written.Count}개 스프라이트 (네이티브 {W}x{H} → x4)");
        }

        private static void Save(PixelCanvas c, string path, List<string> written)
        {
            c.SavePng(path);
            written.Add(path);
        }

        // ══════════════════════════════════════════ 플레이어 (2포즈)

        private const int PW = 72, PH = 84;

        /// <summary>
        /// 플레이어 캐릭터 — 책상에 앉은 뒷모습.
        /// upright = 고개 들고 칠판 보는 정자세 / false = 고개 숙이고 폰 보는 자세.
        /// 실루엣 차이(머리 y 위치 + 목 노출량)만으로 두 상태가 즉시 구분된다.
        /// </summary>
        public static PixelCanvas BuildPlayer(bool upright)
        {
            var c = new PixelCanvas(PW, PH);
            // 고개를 숙이면 머리가 내려오고 목이 가려진다. 이 두 변화가 겹쳐야
            // 작은 화면에서도 두 포즈가 한눈에 구분된다.
            int headTop = upright ? 2 : 13;
            int neckShow = upright ? 6 : 0;

            System.Action<int, int, int, int, Color> R = (x0, y0, x1, y1, col) =>
            {
                for (int sy = y0; sy < y1; sy++)
                    for (int x = x0; x < x1; x++)
                        c.Blend(x, PH - 1 - sy, col);
            };

            // 어깨 y[46,84]
            for (int sy = 46; sy < 84; sy++)
            {
                float t = (sy - 46) / 38f;
                int halfW = Mathf.RoundToInt(Mathf.Lerp(24f, 34f, Mathf.Sqrt(t)));
                for (int x = 36 - halfW; x < 36 + halfW; x++)
                {
                    Color col = ArtRamps.Dusk[ArtRamps.BASE];
                    if (x < 36 - halfW + 5 && sy < 58) col = ArtRamps.Dusk[ArtRamps.HI];
                    else if (x > 36 + halfW - 6) col = ArtRamps.Dusk[ArtRamps.S1];
                    c.Blend(x, PH - 1 - sy, col);
                }
            }

            // 칼라
            for (int x = 24; x < 48; x++) c.Blend(x, PH - 1 - 47, ArtRamps.Paper);

            // 목 — 자세에 따라 노출량이 다르다
            int neckTop = headTop + 38;
            R(30, neckTop, 42, neckTop + neckShow, ArtRamps.Skin[ArtRamps.S1]);

            // 머리 (뒤통수) — 34x34. 이전 40x40은 어깨 대비 너무 커서 헬멧처럼 보였다.
            const int HW = 34, HH = 34;
            int hx = 36 - HW / 2;
            for (int sy = headTop; sy < headTop + HH; sy++)
            {
                int local = sy - headTop;
                int x0 = hx, x1 = hx + HW;
                // 아래쪽에서 턱선을 향해 좁아진다 (정사각 실루엣 금지)
                if (local >= 26) { int s = (local - 26) * 2 + 1; x0 += s; x1 -= s; }
                int inset = local < 5 ? 5 - local : 0;
                x0 += inset; x1 -= inset;
                for (int x = x0; x < x1; x++)
                {
                    Color col = ArtRamps.Hair[ArtRamps.BASE];
                    if (x > x1 - 6) col = ArtRamps.Hair[ArtRamps.S1];
                    c.Blend(x, PH - 1 - sy, col);
                }
            }
            // 가르마 — 정수리에서 갈라지는 1px 홈. 뒤통수가 민무늬 덩어리로 보이는 걸 막는다.
            for (int k = 0; k < 9; k++)
                c.Blend(30 + k / 3, PH - 1 - (headTop + 3 + k), ArtRamps.Hair[ArtRamps.S2]);
            // 좌상단 1px 하이라이트 띠
            for (int x = hx + 5; x < hx + 18; x++) c.Blend(x, PH - 1 - (headTop + 1), ArtRamps.Hair[ArtRamps.HI]);

            // 귀 — 좌우 실루엣을 깨서 원형 덩어리로 뭉치는 걸 막는다
            R(hx - 2, headTop + 17, hx, headTop + 23, ArtRamps.Skin[ArtRamps.S1]);
            R(hx + HW, headTop + 17, hx + HW + 2, headTop + 23, ArtRamps.Skin[ArtRamps.S2]);

            CharacterGenerator.SelectiveOutlineOn(c);
            return c;
        }

        // ══════════════════════════════════════════ 폰 + 손

        /// <summary>
        /// 화면 하단의 몰래폰과 그것을 감싼 손.
        /// 캔버스는 씬과 같은 270x480이라 배치 오차가 생기지 않는다.
        /// </summary>
        public static PixelCanvas BuildPhone()
        {
            var c = new PixelCanvas(W, H);

            // 접지 그림자 — 사각형이 아니라 타원 (폭 = 폰폭 x 0.92)
            c.FillEllipse(135, Y(474), 67f, 8f, ArtRamps.A(ArtRamps.Ink, 0.35f));

            // 손등 (폰 뒤)
            DrawHandBack(c, 52, 360, 74, 448);
            DrawHandBack(c, 196, 360, 218, 448);

            // 폰 바디 x[62,208] y[295,480], 라운드 21
            RoundRectSpec(c, 62, 295, 208, 480, 21, ArtRamps.Dusk[ArtRamps.BASE]);
            RoundRectSpec(c, 63, 296, 207, 480, 20, ArtRamps.Ink);

            // 좌상단 모서리 스페큘러 1~2px
            for (int k = 0; k < 14; k++)
            {
                c.Blend(70 + k, Y(297 + (13 - k) / 2), ArtRamps.A(ArtRamps.Paper, 0.75f));
            }

            // 화면 x[67,203] y[300,480], 라운드 15
            RoundRectSpec(c, 67, 300, 203, 480, 15, ArtRamps.Night);
            // 상단 상태바 12px
            for (int sy = 300; sy < 312; sy++)
                for (int x = 67; x < 203; x++)
                    c.Blend(x, Y(sy), ArtRamps.A(ArtRamps.Ink, 0.47f));

            // 화면 콘텐츠 — 메신저 말풍선 느낌 (실제 UI는 위에 얹힌다)
            var rnd = new System.Random(7);
            int by = 322;
            for (int i = 0; i < 7 && by < 470; i++)
            {
                bool mine = rnd.Next(2) == 0;
                int bw = rnd.Next(46, 104);
                int bh = rnd.Next(14, 22);
                int bx = mine ? 203 - 8 - bw : 67 + 8;
                Color col = mine ? ArtRamps.Amber[ArtRamps.S1] : ArtRamps.Dusk[ArtRamps.BASE];
                RoundRectSpec(c, bx, by, bx + bw, by + bh, 5, col);
                for (int r = 0; r < bh / 7; r++)
                    for (int x = bx + 6; x < bx + bw - 6; x++)
                        c.Blend(x, Y(by + 5 + r * 6), ArtRamps.A(ArtRamps.Paper, 0.30f));
                by += bh + 8;
            }

            // 화면 발광 (차가운 보랏빛 — 4300K 웜 교실과 대비되어 폰이 시선을 끈다)
            for (int sy = 295; sy < 480; sy++)
                for (int x = 62; x < 208; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, sy), new Vector2(135, 390)) / 110f;
                    float a = Mathf.Clamp01(1f - d) * 0.10f;
                    if (a > 0f) c.Blend(x, Y(sy), ArtRamps.A(ArtRamps.Haze[ArtRamps.BASE], a));
                }

            // 감싸는 손가락 (폰 앞) — 각 폭 11 x 길이 30 x 라운드 5
            for (int i = 0; i < 4; i++)
            {
                int fy = 368 + i * 19;
                DrawFinger(c, 56, fy, 11, 30, false);
                DrawFinger(c, 203, fy, 11, 30, true);
            }

            return c;
        }

        private static void DrawHandBack(PixelCanvas c, int x0, int y0, int x1, int y1)
        {
            RoundRectSpec(c, x0, y0, x1, y1, 8, ArtRamps.Skin[ArtRamps.BASE]);
            for (int sy = y0; sy < y1; sy++)
                c.Blend(x0, Y(sy), ArtRamps.Skin[ArtRamps.HI]);
        }

        private static void DrawFinger(PixelCanvas c, int x, int y, int w, int h, bool rightSide)
        {
            RoundRectSpec(c, x, y, x + w, y + h, 5, ArtRamps.Skin[ArtRamps.BASE]);
            // 좌상 HI 1px / 우하 S1
            for (int sy = y + 2; sy < y + h - 2; sy++) c.Blend(x, Y(sy), ArtRamps.Skin[ArtRamps.HI]);
            for (int sy = y + 2; sy < y + h - 2; sy++) c.Blend(x + w - 1, Y(sy), ArtRamps.Skin[ArtRamps.S1]);
            // 폰 모서리와 닿는 1px는 AO
            int contact = rightSide ? x : x + w - 1;
            for (int sy = y; sy < y + h; sy++) c.Blend(contact, Y(sy), ArtRamps.Skin[ArtRamps.S3]);
            // 손가락 사이 1px 배경 갭
            for (int xx = x; xx < x + w; xx++) c.Set(xx, Y(y + h), ArtRamps.Clear);
        }

        /// <summary>스펙 좌표(좌상단 원점)로 라운드 사각형을 그린다.</summary>
        private static void RoundRectSpec(PixelCanvas c, int x0, int y0, int x1, int y1, int r, Color col)
        {
            int w = x1 - x0, h = y1 - y0;
            r = Mathf.Clamp(r, 0, Mathf.Min(w, h) / 2);
            for (int sy = 0; sy < h; sy++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (r > 0)
                    {
                        int dx = 0, dy = 0;
                        if (x < r) dx = r - x;
                        else if (x >= w - r) dx = x - (w - r) + 1;
                        if (sy < r) dy = r - sy;
                        else if (sy >= h - r) dy = sy - (h - r) + 1;
                        if (dx > 0 && dy > 0 && dx * dx + dy * dy > r * r) continue;
                    }
                    c.Blend(x0 + x, Y(y0 + sy), col);
                }
            }
        }

        // ══════════════════════════════════════════ 임포트 설정

        public static void ApplyImportSettings(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;

            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spritePixelsPerUnit = PPU;
            imp.filterMode = FilterMode.Point;          // 픽셀아트 필수 — 엔진 AA를 끈다
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.mipmapEnabled = false;                  // 화면 크기 고정 2D — 밉맵은 디스크/메모리 +33%
            imp.alphaIsTransparency = true;
            imp.isReadable = false;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.npotScale = TextureImporterNPOTScale.None;

            var so = new SerializedObject(imp);
            var meshType = so.FindProperty("m_SpriteMeshType");
            if (meshType != null) { meshType.intValue = 0; so.ApplyModifiedProperties(); }  // Full Rect — 9-slice 필수

            // Android: 픽셀아트는 압축하면 도트가 깨지므로 무압축 유지
            var s = new TextureImporterPlatformSettings
            {
                name = "Android",
                overridden = true,
                maxTextureSize = 2048,
                format = TextureImporterFormat.RGBA32,
                textureCompression = TextureImporterCompression.Uncompressed,
            };
            imp.SetPlatformTextureSettings(s);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
    }
}
