using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Molae.EditorTools
{
    /// <summary>
    /// 절차적 오디오 합성기. 외부 음원 없이 코드로 BGM 과 효과음을 만든다.
    ///
    /// 왜 절차적인가: 라이선스 리스크가 0이고, 파일을 구하러 갈 필요가 없고,
    /// BPM·조성·길이를 게임 로직(50초 세션, 라운드 전환)에 정확히 맞출 수 있다.
    ///
    /// 핵심 규약:
    ///  - 44100Hz 모노. 75 BPM 이면 1비트 = 35280샘플로 정수로 떨어진다(661500/75 = 8820 = 16분음표).
    ///  - 루프는 8마디 = 25.6초 = 1,128,960샘플.
    ///  - 지속음의 주파수는 반드시 Snap() 을 통과시킨다. 그래야 루프 끝에서 위상이 0으로 돌아와
    ///    이음매에서 클릭이 안 난다(제로크로싱 탐색 불필요).
    ///  - 스냅이 불가능한 성분(노이즈, 드럼 꼬리)은 마지막 512샘플을 앞머리와 등파워 크로스페이드.
    /// </summary>
    public static class ProcAudio
    {
        public const int SR = 44100;
        public const float BPM = 75f;
        public const float BEAT = 60f / BPM;          // 0.8초
        public const float BAR = BEAT * 4f;           // 3.2초
        public const float STEP16 = BEAT / 4f;        // 0.2초
        public const int LOOP_SAMPLES = 1128960;      // 8마디 = 25.6초
        public const float LOOP_SEC = LOOP_SAMPLES / (float)SR;

        /// <summary>루프 길이에 대응하는 주파수 분해능. 이 배수여야 위상이 정확히 닫힌다.</summary>
        public const float FBIN = (float)SR / LOOP_SAMPLES;   // 0.0390625 Hz

        private const string OutDir = "Assets/_Molae/Audio/Generated";

        // ───────────────────────────────────────────── 유틸

        public static float Snap(float f) => Mathf.Round(f / FBIN) * FBIN;

        /// <summary>MIDI 노트 → 주파수.</summary>
        public static float Mtof(int n) => 440f * Mathf.Pow(2f, (n - 69) / 12f);

        private static System.Random _rng = new System.Random(20260819);
        private static float Rand() => (float)_rng.NextDouble();
        private static float RandBi() => (float)(_rng.NextDouble() * 2.0 - 1.0);

        /// <summary>1폴 로우패스. a = exp(-2*pi*fc/SR)</summary>
        private static void LowPass(float[] buf, float fc)
        {
            float a = Mathf.Exp(-2f * Mathf.PI * fc / SR);
            float y = 0f;
            for (int i = 0; i < buf.Length; i++) { y = (1f - a) * buf[i] + a * y; buf[i] = y; }
        }

        /// <summary>1폴 하이패스.</summary>
        private static void HighPass(float[] buf, float fc)
        {
            float a = Mathf.Exp(-2f * Mathf.PI * fc / SR);
            float y = 0f, prev = 0f;
            for (int i = 0; i < buf.Length; i++)
            {
                y = a * (y + buf[i] - prev);
                prev = buf[i];
                buf[i] = y;
            }
        }

        /// <summary>바이쿼드 밴드패스 (RBJ cookbook, constant 0 dB peak gain).</summary>
        private static void BandPass(float[] buf, float fc, float q)
        {
            float w0 = 2f * Mathf.PI * fc / SR;
            float alpha = Mathf.Sin(w0) / (2f * q);
            float b0 = alpha, b1 = 0f, b2 = -alpha;
            float a0 = 1f + alpha, a1 = -2f * Mathf.Cos(w0), a2 = 1f - alpha;
            b0 /= a0; b1 /= a0; b2 /= a0; a1 /= a0; a2 /= a0;

            float x1 = 0f, x2 = 0f, y1 = 0f, y2 = 0f;
            for (int i = 0; i < buf.Length; i++)
            {
                float x = buf[i];
                float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                buf[i] = y;
            }
        }

        /// <summary>버퍼의 [at] 위치에 src 를 gain 배로 더한다. 범위를 넘으면 잘라낸다.</summary>
        private static void Mix(float[] dst, float[] src, int at, float gain)
        {
            for (int i = 0; i < src.Length; i++)
            {
                int j = at + i;
                if (j < 0 || j >= dst.Length) continue;
                dst[j] += src[i] * gain;
            }
        }

        /// <summary>루프 이음매 봉합. 마지막 512샘플을 앞 512샘플과 등파워 크로스페이드.</summary>
        private static void SealLoop(float[] buf, int fade = 512)
        {
            int n = buf.Length;
            if (n < fade * 2) return;
            for (int i = 0; i < fade; i++)
            {
                float u = i / (float)(fade - 1);
                float w1 = Mathf.Cos(0.5f * Mathf.PI * u);
                float w2 = Mathf.Sin(0.5f * Mathf.PI * u);
                int t = n - fade + i;
                buf[t] = buf[t] * w1 + buf[i] * w2;
            }
            // 앞뒤 64샘플 선형 페이드로 DC 클릭 제거
            for (int i = 0; i < 64; i++)
            {
                float u = i / 63f;
                buf[i] *= u;
                buf[n - 1 - i] *= u;
            }
        }

        /// <summary>마스터 리미터. -1.0 dBFS 에서 소프트 클립.</summary>
        private static void Limit(float[] buf)
        {
            const float ceil = 0.891f;
            for (int i = 0; i < buf.Length; i++)
                buf[i] = (float)Math.Tanh(buf[i] / ceil) * ceil;
        }

        // ───────────────────────────────────────────── 악기

        /// <summary>로즈 일렉트릭 피아노. FM 비 14:1 + 디튠 2보이스.</summary>
        private static float[] Rhodes(float freq, float lenSec, bool wobble)
        {
            int n = Mathf.RoundToInt(lenSec * SR);
            var b = new float[n];
            float f = Snap(freq);
            float f2 = Snap(freq * 1.0035f);

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                // 테이프 워블 — 로파이의 핵심 질감. 리듬 악기에는 절대 쓰지 않는다.
                float tw = wobble
                    ? t * (1f + 0.0035f * Mathf.Sin(2f * Mathf.PI * 0.6f * t)
                             + 0.0012f * Mathf.Sin(2f * Mathf.PI * 6.3f * t))
                    : t;

                float I = 2.4f * Mathf.Exp(-t / 0.09f);           // FM 인덱스 감쇠
                float m1 = Mathf.Sin(2f * Mathf.PI * 14f * f * tw);
                float m2 = Mathf.Sin(2f * Mathf.PI * 14f * f2 * tw);
                float v = Mathf.Sin(2f * Mathf.PI * f * tw + I * m1)
                        + 0.5f * Mathf.Sin(2f * Mathf.PI * f2 * tw + I * m2);

                float env = Mathf.Exp(-t / 0.85f);
                if (t < 0.004f) env *= t / 0.004f;                 // 어택 4ms
                float tail = lenSec - t;
                if (tail < 0.012f) env *= Mathf.Max(0f, tail / 0.012f);
                b[i] = v * env;
            }
            LowPass(b, 3200f);
            return b;
        }

        private static float[] Bass(float freq, float lenSec)
        {
            int n = Mathf.RoundToInt(lenSec * SR);
            var b = new float[n];
            float f = Snap(freq);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float v = Mathf.Sin(2f * Mathf.PI * f * t) + 0.25f * Mathf.Sin(4f * Mathf.PI * f * t);
                float env = Mathf.Exp(-t / 0.55f);
                if (t < 0.008f) env *= t / 0.008f;
                b[i] = v * env;
            }
            LowPass(b, 220f);
            return b;
        }

        private static float[] Kick()
        {
            int n = Mathf.RoundToInt(0.45f * SR);
            var b = new float[n];
            float ph = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float f = 48f + (110f - 48f) * Mathf.Exp(-t / 0.035f);   // 피치 스윕
                ph += 2f * Mathf.PI * f / SR;
                float amp = Mathf.Exp(-t / 0.22f);
                if (i < 44) amp *= i / 44f;
                float v = Mathf.Sin(ph) * amp;
                if (t < 0.002f) v += RandBi() * 0.35f * (1f - t / 0.002f);  // 어택 클릭
                b[i] = v;
            }
            for (int i = 0; i < n; i++) b[i] = (float)Math.Tanh(1.6f * b[i]);
            return b;
        }

        private static float[] Snare()
        {
            int n = Mathf.RoundToInt(0.22f * SR);
            var tone = new float[n];
            var noise = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                tone[i] = Mathf.Sin(2f * Mathf.PI * 200f * t) * Mathf.Exp(-t / 0.060f) * 0.45f
                        + Mathf.Sin(2f * Mathf.PI * 330f * t) * Mathf.Exp(-t / 0.045f) * 0.22f;
                noise[i] = RandBi();
            }
            BandPass(noise, 1800f, 0.9f);
            HighPass(noise, 900f);
            var b = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float env = Mathf.Exp(-t / 0.090f);
                if (t < 0.001f) env *= t / 0.001f;
                b[i] = tone[i] + noise[i] * env * 0.55f;
            }
            return b;
        }

        private static float[] Hat(bool open)
        {
            float len = open ? 0.320f : 0.050f;
            float dec = open ? 0.11f : 0.012f;
            int n = Mathf.RoundToInt(len * SR);
            var b = new float[n];
            float[] fs = { 205.3f, 304.4f, 369.6f, 522.7f, 540.0f, 800.0f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float s = 0f;
                for (int k = 0; k < fs.Length; k++) s += Mathf.Sign(Mathf.Sin(2f * Mathf.PI * fs[k] * t));
                b[i] = s / fs.Length;
            }
            BandPass(b, 3440f, 1.2f);
            BandPass(b, 7100f, 1.2f);
            HighPass(b, 6000f);
            for (int i = 0; i < n; i++) b[i] *= Mathf.Exp(-(i / (float)SR) / dec);
            return b;
        }

        // ───────────────────────────────────────────── 레이어

        private static readonly int[][] Chords = {
            new[]{60,64,67,71},  // Cmaj7
            new[]{57,60,64,67},  // Am7
            new[]{62,65,69,72},  // Dm7
            new[]{55,59,62,65},  // G7
        };
        private static readonly int[] BassRoots = { 36, 33, 38, 31 };
        private static readonly float[] KeyOffsets = { 0.000f, 2.400f, 3.200f, 5.600f };
        private static readonly float[] KeyVels = { 1.00f, 0.55f, 0.80f, 0.45f };

        private static float[] BuildKeys()
        {
            var buf = new float[LOOP_SAMPLES];
            for (int c = 0; c < 4; c++)
            {
                float blockStart = c * 6.4f;
                for (int s = 0; s < KeyOffsets.Length; s++)
                {
                    float at = blockStart + KeyOffsets[s];
                    foreach (int note in Chords[c])
                    {
                        var v = Rhodes(Mtof(note), 2.60f, true);
                        Mix(buf, v, Mathf.RoundToInt(at * SR), KeyVels[s] * 0.25f);
                    }
                }
            }
            for (int i = 0; i < buf.Length; i++) buf[i] *= 0.1995f;
            SealLoop(buf);
            return buf;
        }

        private static float[] BuildBass()
        {
            var buf = new float[LOOP_SAMPLES];
            float[] offs = { 0.000f, 2.000f, 3.200f, 4.800f };
            float[] lens = { 1.20f, 0.60f, 1.20f, 0.50f };
            for (int c = 0; c < 4; c++)
            {
                float blockStart = c * 6.4f;
                for (int s = 0; s < offs.Length; s++)
                {
                    int note = (s == 3) ? BassRoots[c] + 7 : BassRoots[c];
                    var v = Bass(Mtof(note), lens[s]);
                    Mix(buf, v, Mathf.RoundToInt((blockStart + offs[s]) * SR), 1f);
                }
            }
            for (int i = 0; i < buf.Length; i++) buf[i] *= 0.2239f;
            SealLoop(buf);
            return buf;
        }

        private static float[] BuildDrums()
        {
            var buf = new float[LOOP_SAMPLES];
            var kick = Kick();
            var snare = Snare();
            var hatC = Hat(false);

            int bars = Mathf.RoundToInt(LOOP_SEC / BAR);   // 8
            int[] kickSteps = { 0, 6, 10 };
            int[] snareSteps = { 4, 12 };
            int[] hatSteps = { 0, 2, 4, 6, 8, 10, 12, 14 };
            int[] ghostSteps = { 7, 15 };

            for (int bar = 0; bar < bars; bar++)
            {
                float barT = bar * BAR;

                foreach (int st in kickSteps)
                    Mix(buf, kick, Mathf.RoundToInt((barT + st * STEP16) * SR), 0.3548f);
                if (bar % 2 == 1)
                    Mix(buf, kick, Mathf.RoundToInt((barT + 14 * STEP16) * SR), 0.3548f);

                foreach (int st in snareSteps)
                    Mix(buf, snare, Mathf.RoundToInt((barT + st * STEP16) * SR), 0.2512f);

                foreach (int st in hatSteps)
                {
                    // 스윙: 뒷박(2/6/10/14)을 58% 지점으로 밀어 로파이 그루브를 만든다
                    float swing = (st % 4 == 2) ? 0.064f : 0f;
                    float vel = (st % 4 == 0) ? 1.00f : 0.60f;
                    Mix(buf, hatC, Mathf.RoundToInt((barT + st * STEP16 + swing) * SR), 0.1122f * vel);
                }
                foreach (int st in ghostSteps)
                    Mix(buf, hatC, Mathf.RoundToInt((barT + st * STEP16 + 0.032f) * SR), 0.1122f * 0.35f);
            }
            SealLoop(buf);
            return buf;
        }

        private static float[] BuildVinyl()
        {
            var buf = new float[LOOP_SAMPLES];
            // (1) 표면 노이즈
            for (int i = 0; i < buf.Length; i++) buf[i] = RandBi();
            BandPass(buf, 1500f, 0.7f);
            for (int i = 0; i < buf.Length; i++) buf[i] *= 0.030f;

            // (2) 클릭 — 초당 약 420회
            var clicks = new float[LOOP_SAMPLES];
            for (int i = 0; i < clicks.Length; i++)
                if (Rand() < 420f / SR)
                    clicks[i] += (0.10f + Rand() * 0.25f) * (Rand() < 0.5f ? -1f : 1f);
            LowPass(clicks, 8000f);
            for (int i = 0; i < buf.Length; i++) buf[i] += clicks[i];

            // (3) 더스트 — 짧은 노이즈 버스트
            for (int i = 0; i < buf.Length; i++)
            {
                if (Rand() >= 8f / SR) continue;
                int len = 44 + (int)(Rand() * 177);
                for (int k = 0; k < len && i + k < buf.Length; k++)
                    buf[i + k] += RandBi() * 0.18f * (1f - k / (float)len);
            }

            for (int i = 0; i < buf.Length; i++) buf[i] *= 0.0501f;
            SealLoop(buf);
            return buf;
        }

        /// <summary>위험 상태용 긴장 레이어. 2비트(1.6초) 짧은 루프.</summary>
        private static float[] BuildTension()
        {
            int n = Mathf.RoundToInt(1.6f * SR);
            var buf = new float[n];
            // 삼전음(tritone) 드론 — D4 와 G#4
            float f1 = Snap(Mtof(62));
            float f2 = Snap(Mtof(68));
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float trem = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 6.0f * t);
                buf[i] = (Mathf.Sin(2f * Mathf.PI * f1 * t) * 0.5f
                        + Mathf.Sin(2f * Mathf.PI * f2 * t) * 0.5f) * trem;
            }
            for (int i = 0; i < n; i++) buf[i] *= 0.1778f;
            SealLoop(buf, 256);
            return buf;
        }

        // ───────────────────────────────────────────── 효과음

        private static float[] SfxTick()
        {
            int n = Mathf.RoundToInt(0.030f * SR);
            var b = new float[n];
            float f = Snap(Mtof(96));
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float ph = Mathf.Repeat(f * t, 1f);
                float v = ph < 0.125f ? 1f : -1f;             // 듀티 12.5% 스퀘어
                float env = Mathf.Exp(-t / 0.008f);
                if (t < 0.001f) env *= t / 0.001f;
                b[i] = v * env * 0.1000f;
            }
            return b;
        }

        private static float[] SfxCombo()
        {
            int n = Mathf.RoundToInt(0.220f * SR);
            var b = new float[n];
            int[] notes = { 72, 76, 79 };
            float[] onsets = { 0.000f, 0.055f, 0.110f };
            for (int k = 0; k < notes.Length; k++)
            {
                float f = Mtof(notes[k]);
                int at = Mathf.RoundToInt(onsets[k] * SR);
                int len = Mathf.RoundToInt(0.075f * SR);
                for (int i = 0; i < len && at + i < n; i++)
                {
                    float t = i / (float)SR;
                    float ph = Mathf.Repeat(f * t, 1f);
                    float v = ph < 0.25f ? 1f : -1f;
                    b[at + i] += v * Mathf.Exp(-t / 0.020f);
                }
            }
            for (int i = 0; i < n; i++) b[i] *= 0.2512f;
            return b;
        }

        private static float[] SfxCloseCall()
        {
            int n = Mathf.RoundToInt(0.350f * SR);
            var b = new float[n];
            var noise = new float[n];
            for (int i = 0; i < n; i++) noise[i] = RandBi();
            BandPass(noise, 900f, 1.2f);

            float ph = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float f = 1046.502f * Mathf.Pow(0.5f, t / 0.28f);   // 하강 스윕
                ph += 2f * Mathf.PI * f / SR;
                float trem = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 14f * t);
                b[i] = Mathf.Sin(ph) * trem * 0.6f + noise[i] * Mathf.Exp(-t / 0.10f) * 0.4f;
            }
            for (int i = 0; i < n; i++) b[i] *= 0.3162f;
            return b;
        }

        private static float[] SfxGameOver()
        {
            int n = Mathf.RoundToInt(0.900f * SR);
            var b = new float[n];
            int[] notes = { 67, 63, 60, 55 };
            float[] onsets = { 0.00f, 0.12f, 0.24f, 0.36f };
            for (int k = 0; k < notes.Length; k++)
            {
                float f = Mtof(notes[k]);
                if (k == 3) f *= Mathf.Pow(2f, -35f / 1200f);      // 마지막 음만 -35센트 (고장난 테이프)
                int at = Mathf.RoundToInt(onsets[k] * SR);
                int len = Mathf.RoundToInt(0.14f * SR);
                for (int i = 0; i < len && at + i < n; i++)
                {
                    float t = i / (float)SR;
                    float v = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * f * t));
                    b[at + i] += v * Mathf.Exp(-t / 0.09f);
                }
            }
            // 크래시
            int cAt = Mathf.RoundToInt(0.36f * SR);
            int cLen = n - cAt;
            var crash = new float[cLen];
            for (int i = 0; i < cLen; i++) crash[i] = RandBi();
            BandPass(crash, 3440f, 1.0f);
            for (int i = 0; i < cLen; i++) crash[i] *= Mathf.Exp(-(i / (float)SR) / 0.35f) * 0.5f;
            Mix(b, crash, cAt, 1f);

            for (int i = 0; i < n; i++) b[i] *= 0.5012f;
            return b;
        }

        private static float[] SfxChalk()
        {
            int n = Mathf.RoundToInt(0.130f * SR);
            var b = new float[n];
            for (int i = 0; i < n; i++) b[i] = RandBi();
            BandPass(b, 3200f, 3.0f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float grain = Mathf.Abs(Mathf.Sin(2f * Mathf.PI * 31f * t));
                float env = 1f;
                if (t < 0.008f) env = t / 0.008f;
                float tail = (n - i) / (float)SR;
                if (tail < 0.040f) env *= tail / 0.040f;
                b[i] *= grain * env * 0.1259f;
            }
            return b;
        }

        private static float[] SfxEnding()
        {
            int n = Mathf.RoundToInt(5.40f * SR);
            var b = new float[n];
            int[] notes = { 60, 64, 67, 71, 74 };           // Cmaj9
            for (int k = 0; k < notes.Length; k++)
            {
                var v = Rhodes(Mtof(notes[k]), 5.0f, false);
                Mix(b, v, Mathf.RoundToInt((k * 0.06f) * SR), 0.22f);
            }
            // 크래시 2회 — 색종이 버스트와 일치시킨다
            foreach (float at in new[] { 0.00f, 3.30f })
            {
                int cAt = Mathf.RoundToInt(at * SR);
                int cLen = Mathf.Min(Mathf.RoundToInt(1.5f * SR), n - cAt);
                if (cLen <= 0) continue;
                var crash = new float[cLen];
                for (int i = 0; i < cLen; i++) crash[i] = RandBi();
                BandPass(crash, 3440f, 1.0f);
                for (int i = 0; i < cLen; i++) crash[i] *= Mathf.Exp(-(i / (float)SR) / 0.90f) * 0.45f;
                Mix(b, crash, cAt, 1f);
            }
            return b;
        }

        // ───────────────────────────────────────────── WAV 출력

        /// <summary>float[-1,1] 배열을 16bit PCM 모노 WAV 로 저장한다.</summary>
        private static void WriteWav(string assetPath, float[] data)
        {
            Limit(data);
            string full = Path.GetFullPath(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));

            using (var fs = new FileStream(full, FileMode.Create))
            using (var w = new BinaryWriter(fs))
            {
                int dataBytes = data.Length * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' });
                w.Write(36 + dataBytes);
                w.Write(new[] { 'W', 'A', 'V', 'E' });
                w.Write(new[] { 'f', 'm', 't', ' ' });
                w.Write(16);                        // fmt 청크 크기
                w.Write((short)1);                  // PCM
                w.Write((short)1);                  // 모노
                w.Write(SR);
                w.Write(SR * 2);                    // 바이트레이트
                w.Write((short)2);                  // 블록 얼라인
                w.Write((short)16);                 // 비트뎁스
                w.Write(new[] { 'd', 'a', 't', 'a' });
                w.Write(dataBytes);
                for (int i = 0; i < data.Length; i++)
                    w.Write((short)(Mathf.Clamp(data[i], -1f, 1f) * 32767f));
            }
        }

        // ───────────────────────────────────────────── 엔트리

        [MenuItem("Molae/Rebuild All Audio")]
        public static void RebuildAll()
        {
            _rng = new System.Random(20260819);   // 결정적 생성
            Directory.CreateDirectory(Path.GetFullPath(OutDir));

            var made = new System.Collections.Generic.List<string>();

            void Bake(string name, float[] d)
            {
                string p = $"{OutDir}/{name}.wav";
                WriteWav(p, d);
                made.Add(p);
            }

            Bake("bgm_l0_vinyl", BuildVinyl());
            Bake("bgm_l1_keys", BuildKeys());
            Bake("bgm_l2_drums", BuildDrums());
            Bake("bgm_l3_bass", BuildBass());
            Bake("bgm_l4_tension", BuildTension());

            Bake("sfx_tick", SfxTick());
            Bake("sfx_combo", SfxCombo());
            Bake("sfx_closecall", SfxCloseCall());
            Bake("sfx_gameover", SfxGameOver());
            Bake("sfx_chalk", SfxChalk());
            Bake("sfx_ending", SfxEnding());

            AssetDatabase.Refresh();

            foreach (var p in made)
            {
                var imp = AssetImporter.GetAtPath(p) as AudioImporter;
                if (imp == null) continue;
                var s = imp.defaultSampleSettings;
                // 짧은 효과음은 메모리 상주(DecompressOnLoad), 긴 BGM 은 압축 유지
                bool isBgm = p.Contains("bgm_");
                s.loadType = isBgm ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.7f;
                imp.defaultSampleSettings = s;
                imp.forceToMono = true;
                imp.loadInBackground = isBgm;
                AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Molae] 오디오 생성 완료: {made.Count}개 " +
                      $"(BGM 루프 {LOOP_SEC:0.0}초 @ {BPM:0} BPM, {SR}Hz 모노)");
        }
    }
}
