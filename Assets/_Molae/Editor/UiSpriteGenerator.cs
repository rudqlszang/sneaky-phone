using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Molae.EditorTools
{
    /// <summary>
    /// 9-slice UI 스프라이트 생성기.
    ///
    /// 패널은 정확히 4패스로 그린다 (이 4패스가 "밋밋한 사각형"과 "게임 UI"를 가른다):
    ///   1) 외곽선 1px  = 램프 idx+2
    ///   2) 내부 하이라이트 1px = idx-1  (상단 가로 전체 + 좌측 세로 전체에만)
    ///   3) 면          = idx
    ///   4) 하단 립 2px = idx+1
    ///
    /// 반경은 6개만 허용: R = {0, 3, 6, 9, 12, 21} 네이티브 px
    /// (Material 3의 0/4/8/12/16/28dp를 네이티브로 환산한 값)
    /// </summary>
    public static class UiSpriteGenerator
    {
        private const string Dir = "Assets/_Molae/Art/Sprites/UI";

        private struct NineSlice
        {
            public string name;
            public int w, h, radius;
            public int bl, br, bt, bb;          // 9-slice border
            public Color outline, highlight, face, lip;
        }

        public static void BuildAll(List<string> written)
        {
            var specs = new List<NineSlice>
            {
                // 버튼 — 하단만 립 2px 크게(border B=9)
                new NineSlice { name="btn_primary",   w=24,h=24,radius=6, bl=7,br=7,bt=7,bb=9,
                    outline=ArtRamps.Ink,               highlight=ArtRamps.Sun[ArtRamps.BASE],
                    face=ArtRamps.Amber[ArtRamps.BASE], lip=ArtRamps.Wood[ArtRamps.BASE] },

                new NineSlice { name="btn_secondary", w=24,h=24,radius=6, bl=7,br=7,bt=7,bb=9,
                    outline=ArtRamps.Dusk[ArtRamps.BASE], highlight=ArtRamps.Paper,
                    face=ArtRamps.Wall[ArtRamps.BASE],    lip=ArtRamps.Blush[ArtRamps.BASE] },

                new NineSlice { name="btn_danger",    w=24,h=24,radius=6, bl=7,br=7,bt=7,bb=9,
                    outline=ArtRamps.Ink,                 highlight=ArtRamps.Blush[ArtRamps.HI],
                    face=ArtRamps.Blush[ArtRamps.BASE],   lip=ArtRamps.Blush[ArtRamps.S1] },

                // 패널
                new NineSlice { name="panel_paper",   w=24,h=24,radius=9, bl=9,br=9,bt=9,bb=9,
                    outline=ArtRamps.Dusk[ArtRamps.BASE], highlight=ArtRamps.Paper,
                    face=ArtRamps.Wall[ArtRamps.BASE],    lip=ArtRamps.Blush[ArtRamps.BASE] },

                new NineSlice { name="panel_board",   w=24,h=24,radius=9, bl=9,br=9,bt=9,bb=9,
                    outline=ArtRamps.Board[ArtRamps.S3],  highlight=ArtRamps.Board[ArtRamps.HI],
                    face=ArtRamps.Board[ArtRamps.BASE],   lip=ArtRamps.Board[ArtRamps.S1] },

                new NineSlice { name="panel_sheet",   w=32,h=32,radius=12, bl=12,br=12,bt=12,bb=12,
                    outline=ArtRamps.Ink,                 highlight=ArtRamps.Paper,
                    face=ArtRamps.Wall[ArtRamps.BASE],    lip=ArtRamps.Blush[ArtRamps.BASE] },

                new NineSlice { name="popup_dialog",  w=40,h=40,radius=12, bl=14,br=14,bt=14,bb=14,
                    outline=ArtRamps.Ink,                 highlight=ArtRamps.Paper,
                    face=ArtRamps.Wall[ArtRamps.HI],      lip=ArtRamps.Blush[ArtRamps.BASE] },
            };

            foreach (var s in specs)
            {
                var c = DrawPanel(s);
                string path = $"{Dir}/{s.name}.png";
                c.SavePng(path);
                written.Add(path);
            }

            // 게이지 트랙/필 — 12x6, 세로 border 0
            BuildBar("bar_track", ArtRamps.A(ArtRamps.Ink, 0.55f), ArtRamps.Ink, written);
            BuildBar("bar_fill_sun", ArtRamps.Sun[ArtRamps.BASE], ArtRamps.Amber[ArtRamps.S1], written);
            BuildBar("bar_fill_danger", ArtRamps.Blush[ArtRamps.BASE], ArtRamps.Blush[ArtRamps.S2], written);

            // 단순 도형
            BuildDot(written);
            BuildRing(written);
            BuildStar(written);
            BuildPixel(written);

            AssetDatabase.Refresh();

            // 9-slice border 는 임포트 후에 설정해야 한다
            foreach (var s in specs) SetBorder($"{Dir}/{s.name}.png", s.bl, s.bb, s.br, s.bt);
            SetBorder($"{Dir}/bar_track.png", 4, 0, 4, 0);
            SetBorder($"{Dir}/bar_fill_sun.png", 4, 0, 4, 0);
            SetBorder($"{Dir}/bar_fill_danger.png", 4, 0, 4, 0);
        }

        /// <summary>스펙의 4패스 패널 드로잉.</summary>
        private static PixelCanvas DrawPanel(NineSlice s)
        {
            var c = new PixelCanvas(s.w, s.h);

            // 3) 면 (라운드 사각형 전체)
            c.FillRoundRect(0, 0, s.w, s.h, s.radius, s.face);

            // 4) 하단 립 2px
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < s.w; x++)
                    if (c.Get(x, y).a > 0.5f) c.Set(x, y, s.lip);
            // 립이 라운드 코너를 따라가도록 한 줄 더
            for (int x = 0; x < s.w; x++)
                if (c.Get(x, 2).a > 0.5f && c.Get(x, 1).a <= 0.5f) c.Set(x, 2, s.lip);

            // 2) 내부 하이라이트 1px — 상단 가로 전체 + 좌측 세로 전체
            for (int x = 0; x < s.w; x++)
            {
                for (int y = s.h - 1; y >= 0; y--)
                    if (c.Get(x, y).a > 0.5f) { c.Set(x, y, s.highlight); break; }
            }
            for (int y = 2; y < s.h - 1; y++)
            {
                for (int x = 0; x < s.w; x++)
                    if (c.Get(x, y).a > 0.5f) { c.Set(x, y, s.highlight); break; }
            }

            // 1) 외곽선 1px — 하이라이트 바깥으로 한 겹 더
            var outline = new List<Vector2Int>();
            for (int y = 0; y < s.h; y++)
                for (int x = 0; x < s.w; x++)
                {
                    if (c.Get(x, y).a > 0.5f) continue;
                    bool near = c.Get(x + 1, y).a > 0.5f || c.Get(x - 1, y).a > 0.5f
                             || c.Get(x, y + 1).a > 0.5f || c.Get(x, y - 1).a > 0.5f;
                    if (near) outline.Add(new Vector2Int(x, y));
                }
            foreach (var p in outline) c.Set(p.x, p.y, s.outline);

            return c;
        }

        private static void BuildBar(string name, Color face, Color lip, List<string> written)
        {
            var c = new PixelCanvas(12, 6);
            c.FillRoundRect(0, 0, 12, 6, 2, face);
            for (int x = 0; x < 12; x++) if (c.Get(x, 0).a > 0.5f) c.Set(x, 0, lip);
            for (int x = 0; x < 12; x++) if (c.Get(x, 5).a > 0.5f) c.Set(x, 5, ArtRamps.A(ArtRamps.Paper, 0.35f));
            string path = $"{Dir}/{name}.png";
            c.SavePng(path);
            written.Add(path);
        }

        private static void BuildDot(List<string> written)
        {
            var c = new PixelCanvas(16, 16);
            c.FillEllipse(8, 8, 7.5f, 7.5f, ArtRamps.Paper);
            c.FillEllipse(8, 8, 5f, 5f, ArtRamps.Sun[ArtRamps.BASE]);
            string p = $"{Dir}/dot.png"; c.SavePng(p); written.Add(p);
        }

        private static void BuildRing(List<string> written)
        {
            var c = new PixelCanvas(32, 32);
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(16, 16));
                    if (d <= 15.5f && d >= 12f) c.Set(x, y, ArtRamps.Sun[ArtRamps.BASE]);
                }
            string p = $"{Dir}/ring.png"; c.SavePng(p); written.Add(p);
        }

        private static void BuildStar(List<string> written)
        {
            var c = new PixelCanvas(24, 24);
            var pts = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float ang = Mathf.PI / 2f + i * Mathf.PI / 5f;
                float r = (i % 2 == 0) ? 11f : 4.6f;
                pts[i] = new Vector2(12f + Mathf.Cos(ang) * r, 12f + Mathf.Sin(ang) * r);
            }
            c.FillPolygon(pts, ArtRamps.Sun[ArtRamps.BASE]);
            // 좌상단 하이라이트 / 우하단 그림자
            for (int y = 0; y < 24; y++)
                for (int x = 0; x < 24; x++)
                {
                    if (c.Get(x, y).a < 0.5f) continue;
                    if (x < 10 && y > 13) c.Set(x, y, ArtRamps.Sun[ArtRamps.HI]);
                    else if (x > 14 && y < 10) c.Set(x, y, ArtRamps.Amber[ArtRamps.S1]);
                }
            string p = $"{Dir}/star.png"; c.SavePng(p); written.Add(p);
        }

        private static void BuildPixel(List<string> written)
        {
            var c = new PixelCanvas(4, 4);
            c.Clear(Color.white);
            string p = $"{Dir}/px_white.png"; c.SavePng(p); written.Add(p);
        }

        /// <summary>스프라이트의 9-slice border를 설정한다. Vector4는 (left, bottom, right, top).</summary>
        private static void SetBorder(string path, int l, int b, int r, int t)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;

            MolaeArtBuild.ApplyImportSettings(path);

            var so = new SerializedObject(imp);
            var border = so.FindProperty("m_SpriteBorder");
            if (border != null)
            {
                border.vector4Value = new Vector4(l, b, r, t);
                so.ApplyModifiedProperties();
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
        }
    }
}
