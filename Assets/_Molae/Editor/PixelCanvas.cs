using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Molae.EditorTools
{
    /// <summary>
    /// 스프라이트를 코드로 찍기 위한 픽셀 드로잉 캔버스.
    ///
    /// 좌표계는 좌하단 원점(Unity 텍스처와 동일). 알파 블렌딩을 지원하고,
    /// 픽셀아트 기법(컬러 램프 + 휴 시프트, 셀렉티브 아웃라인, 디더링)을 내장한다.
    /// </summary>
    public class PixelCanvas
    {
        public readonly int Width;
        public readonly int Height;
        private readonly Color[] _buf;

        public PixelCanvas(int width, int height)
        {
            Width = width;
            Height = height;
            _buf = new Color[width * height];
        }

        public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

        public Color Get(int x, int y) => InBounds(x, y) ? _buf[y * Width + x] : Color.clear;

        /// <summary>알파 블렌딩으로 한 픽셀을 찍는다.</summary>
        public void Blend(int x, int y, Color c)
        {
            if (!InBounds(x, y) || c.a <= 0f) return;
            int i = y * Width + x;
            Color dst = _buf[i];
            float a = c.a + dst.a * (1f - c.a);
            if (a <= 0f) { _buf[i] = Color.clear; return; }
            Color rgb = (c * c.a + dst * dst.a * (1f - c.a)) / a;
            _buf[i] = new Color(rgb.r, rgb.g, rgb.b, a);
        }

        /// <summary>블렌딩 없이 덮어쓴다.</summary>
        public void Set(int x, int y, Color c)
        {
            if (!InBounds(x, y)) return;
            _buf[y * Width + x] = c;
        }

        public void Clear(Color c)
        {
            for (int i = 0; i < _buf.Length; i++) _buf[i] = c;
        }

        // ───────────────────────────────────────────── 셰이프

        public void FillRect(int x, int y, int w, int h, Color c)
        {
            for (int yy = y; yy < y + h; yy++)
                for (int xx = x; xx < x + w; xx++)
                    Blend(xx, yy, c);
        }

        /// <summary>모서리가 둥근 사각형. radius 0이면 일반 사각형.</summary>
        public void FillRoundRect(int x, int y, int w, int h, int radius, Color c)
        {
            radius = Mathf.Clamp(radius, 0, Mathf.Min(w, h) / 2);
            for (int yy = 0; yy < h; yy++)
            {
                for (int xx = 0; xx < w; xx++)
                {
                    if (radius > 0)
                    {
                        int dx = 0, dy = 0;
                        if (xx < radius) dx = radius - xx;
                        else if (xx >= w - radius) dx = xx - (w - radius) + 1;
                        if (yy < radius) dy = radius - yy;
                        else if (yy >= h - radius) dy = yy - (h - radius) + 1;
                        if (dx > 0 && dy > 0 && dx * dx + dy * dy > radius * radius) continue;
                    }
                    Blend(x + xx, y + yy, c);
                }
            }
        }

        /// <summary>중심 (cx,cy), 반지름 rx/ry 의 타원.</summary>
        public void FillEllipse(float cx, float cy, float rx, float ry, Color c)
        {
            int x0 = Mathf.FloorToInt(cx - rx) - 1, x1 = Mathf.CeilToInt(cx + rx) + 1;
            int y0 = Mathf.FloorToInt(cy - ry) - 1, y1 = Mathf.CeilToInt(cy + ry) + 1;
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float nx = (x + 0.5f - cx) / Mathf.Max(0.0001f, rx);
                    float ny = (y + 0.5f - cy) / Mathf.Max(0.0001f, ry);
                    if (nx * nx + ny * ny <= 1f) Blend(x, y, c);
                }
            }
        }

        /// <summary>두께가 있는 선분. 캡은 둥글다.</summary>
        public void Line(float ax, float ay, float bx, float by, float thickness, Color c)
        {
            float half = thickness * 0.5f;
            int x0 = Mathf.FloorToInt(Mathf.Min(ax, bx) - half) - 1;
            int x1 = Mathf.CeilToInt(Mathf.Max(ax, bx) + half) + 1;
            int y0 = Mathf.FloorToInt(Mathf.Min(ay, by) - half) - 1;
            int y1 = Mathf.CeilToInt(Mathf.Max(ay, by) + half) + 1;

            float vx = bx - ax, vy = by - ay;
            float len2 = vx * vx + vy * vy;

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float t = len2 <= 0.0001f ? 0f : Mathf.Clamp01(((px - ax) * vx + (py - ay) * vy) / len2);
                    float qx = ax + vx * t, qy = ay + vy * t;
                    float d = Mathf.Sqrt((px - qx) * (px - qx) + (py - qy) * (py - qy));
                    if (d <= half) Blend(x, y, c);
                }
            }
        }

        /// <summary>볼록/오목 다각형 채우기 (짝수-홀수 규칙).</summary>
        public void FillPolygon(Vector2[] pts, Color c)
        {
            if (pts == null || pts.Length < 3) return;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in pts) { minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); }

            var xs = new List<float>();
            for (int y = Mathf.FloorToInt(minY); y <= Mathf.CeilToInt(maxY); y++)
            {
                float sy = y + 0.5f;
                xs.Clear();
                for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
                {
                    if ((pts[i].y > sy) == (pts[j].y > sy)) continue;
                    float t = (sy - pts[i].y) / (pts[j].y - pts[i].y);
                    xs.Add(pts[i].x + t * (pts[j].x - pts[i].x));
                }
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int sx = Mathf.RoundToInt(xs[k]);
                    int ex = Mathf.RoundToInt(xs[k + 1]);
                    for (int x = sx; x < ex; x++) Blend(x, y, c);
                }
            }
        }

        // ───────────────────────────────────────────── 효과

        /// <summary>
        /// 불투명 픽셀의 바깥 경계에 아웃라인을 두른다.
        /// selective=true 면 아래쪽/바깥쪽에만 굵게 넣어 무게중심을 만든다.
        /// </summary>
        public void Outline(Color color, bool selective = false, float alphaThreshold = 0.35f)
        {
            var add = new List<Vector2Int>();
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (Get(x, y).a > alphaThreshold) continue;
                    bool near =
                        Get(x + 1, y).a > alphaThreshold || Get(x - 1, y).a > alphaThreshold ||
                        Get(x, y + 1).a > alphaThreshold || Get(x, y - 1).a > alphaThreshold;
                    if (!near) continue;
                    // 셀렉티브: 위쪽(빛 받는 면)의 아웃라인은 생략해 가볍게 만든다
                    if (selective && Get(x, y - 1).a <= alphaThreshold && Get(x, y + 1).a > alphaThreshold) continue;
                    add.Add(new Vector2Int(x, y));
                }
            }
            foreach (var p in add) Set(p.x, p.y, color);
        }

        /// <summary>불투명 영역 아래로 드리우는 그림자. 별도 캔버스에 그려 아래에 깐다.</summary>
        public PixelCanvas MakeDropShadow(int offsetX, int offsetY, int blur, Color shadowColor, float alphaThreshold = 0.35f)
        {
            var sh = new PixelCanvas(Width, Height);
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    if (Get(x, y).a > alphaThreshold)
                        sh.Set(x + offsetX, y + offsetY, shadowColor);

            for (int i = 0; i < blur; i++) sh.BoxBlurAlpha();
            return sh;
        }

        /// <summary>알파 채널만 3x3 박스 블러. 그림자 부드럽게 하는 용도.</summary>
        public void BoxBlurAlpha()
        {
            var copy = new float[Width * Height];
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    float sum = 0f; int n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (!InBounds(x + dx, y + dy)) continue;
                            sum += Get(x + dx, y + dy).a; n++;
                        }
                    copy[y * Width + x] = n == 0 ? 0f : sum / n;
                }
            for (int i = 0; i < _buf.Length; i++) _buf[i].a = copy[i];
        }

        /// <summary>다른 캔버스를 이 캔버스 위에 합성한다.</summary>
        public void Composite(PixelCanvas other, int offsetX = 0, int offsetY = 0)
        {
            for (int y = 0; y < other.Height; y++)
                for (int x = 0; x < other.Width; x++)
                    Blend(x + offsetX, y + offsetY, other.Get(x, y));
        }

        /// <summary>
        /// 두 색 사이를 베이어 4x4 디더로 섞는다. 픽셀아트에서 그라데이션을 표현하는 정석.
        /// t=0이면 a, t=1이면 b.
        /// </summary>
        public void DitherRect(int x, int y, int w, int h, Color a, Color b, System.Func<int, int, float> tFunc)
        {
            int[,] bayer = {
                {  0,  8,  2, 10 },
                { 12,  4, 14,  6 },
                {  3, 11,  1,  9 },
                { 15,  7, 13,  5 }
            };
            for (int yy = 0; yy < h; yy++)
            {
                for (int xx = 0; xx < w; xx++)
                {
                    float t = Mathf.Clamp01(tFunc(xx, yy));
                    float threshold = (bayer[yy & 3, xx & 3] + 0.5f) / 16f;
                    Blend(x + xx, y + yy, t > threshold ? b : a);
                }
            }
        }

        // ───────────────────────────────────────────── 저장

        public Texture2D ToTexture()
        {
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            tex.SetPixels(_buf);
            tex.Apply();
            return tex;
        }

        /// <summary>PNG로 저장하고 AssetDatabase에 반영한다. 경로는 Assets/ 상대.</summary>
        public void SavePng(string assetPath)
        {
            var tex = ToTexture();
            string full = System.IO.Path.GetFullPath(assetPath);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
            System.IO.File.WriteAllBytes(full, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
