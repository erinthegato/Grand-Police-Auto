using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace GPA.EditorTools
{
    /// <summary>
    /// Offline clean-up for sound effects (16-bit PCM WAV in, 16-bit PCM WAV out):
    /// repairs clipped peaks (Catmull-Rom across each clipped run), trims silence before the sound
    /// (and after it, for one-shots), short fades so the cuts don't click, and peak-normalises so
    /// nothing clips. Loops keep their full length so they stay seamless.
    /// </summary>
    public static class AudioCleanup
    {
        public struct Stats
        {
            public float PeakDb, RmsDb, LeadMs, Seconds;
            public int Clipped, Channels, Rate;
            public override string ToString() =>
                $"{Seconds:0.00}s {Channels}ch peak {PeakDb:0.0}dB rms {RmsDb:0.0}dB clipped {Clipped} lead {LeadMs:0}ms";
        }

        public const float SilenceThreshold = 0.01f;   // -40 dBFS: where a sound 'starts'
        public const float TailThreshold = 0.0018f;    // -55 dBFS: where a one-shot has died away

        public static Stats Analyze(string path)
        {
            float[] d = Read(path, out int ch, out int rate);
            return Measure(d, ch, rate);
        }

        public static Stats Measure(float[] d, int ch, int rate)
        {
            var s = new Stats { Channels = ch, Rate = rate, Seconds = d.Length / (float)ch / rate };
            float peak = 0f; double sum = 0; int first = -1;
            for (int i = 0; i < d.Length; i++)
            {
                float a = Math.Abs(d[i]);
                if (a > peak) peak = a;
                if (a >= 0.999f) s.Clipped++;
                sum += a * a;
                if (first < 0 && a > SilenceThreshold) first = i;
            }
            s.PeakDb = Db(peak);
            s.RmsDb = Db((float)Math.Sqrt(sum / Math.Max(1, d.Length)));
            s.LeadMs = first < 0 ? 0 : first / ch * 1000f / rate;
            return s;
        }

        /// <param name="oneShot">Trim the tail and normalise; false for loops (only declip and cap the peak).</param>
        /// <param name="maxSeconds">Optional cap for one-shots (0 = none); the end gets a longer fade.</param>
        public static Stats Process(string src, string dst, bool oneShot, float targetPeakDb = -1f, float maxSeconds = 0f)
        {
            float[] d = Read(src, out int ch, out int rate);
            Declip(d, ch);

            int frames = d.Length / ch;
            int start = 0, end = frames;
            if (oneShot)
            {
                int first = FirstFrameAbove(d, ch, SilenceThreshold);
                int last = TailEnd(d, ch, rate);
                if (first >= 0 && last > first)
                {
                    start = Math.Max(0, first - rate / 1000);              // keep 1 ms of run-up
                    end = Math.Min(frames, last + rate * 20 / 1000);         // and 20 ms of tail
                    if (maxSeconds > 0f) end = Math.Min(end, start + (int)(maxSeconds * rate));
                }
            }
            var o = new float[(end - start) * ch];
            Array.Copy(d, start * ch, o, 0, o.Length);
            int outFrames = o.Length / ch;

            if (oneShot)
            {
                Fade(o, ch, 0, Math.Min(outFrames, rate / 2000), true);                       // 0.5 ms in
                int fadeOut = Math.Min(outFrames, Math.Max(rate * 30 / 1000, Math.Min(rate * 300 / 1000, outFrames / 6)));   // 30-300 ms out
                Fade(o, ch, outFrames - fadeOut, fadeOut, false);
            }

            float peak = 0f;
            foreach (float v in o) peak = Math.Max(peak, Math.Abs(v));
            float target = (float)Math.Pow(10, targetPeakDb / 20f);
            if (peak > 0f && (oneShot || peak > target))
            {
                float g = target / peak;
                for (int i = 0; i < o.Length; i++) o[i] *= g;
            }

            Write(dst, o, ch, rate);
            return Measure(o, ch, rate);
        }

        // ------------------------------------------------------------------ DSP

        private static void Declip(float[] d, int ch)
        {
            int frames = d.Length / ch;
            for (int c = 0; c < ch; c++)
            {
                int f = 0;
                while (f < frames)
                {
                    if (Math.Abs(d[f * ch + c]) < 0.999f) { f++; continue; }
                    int s = f;
                    while (f < frames && Math.Abs(d[f * ch + c]) >= 0.999f) f++;
                    int e = f - 1;                                   // clipped run [s, e]
                    if (s < 2 || e > frames - 3) continue;
                    float p0 = d[(s - 2) * ch + c], p1 = d[(s - 1) * ch + c], p2 = d[(e + 1) * ch + c], p3 = d[(e + 2) * ch + c];
                    int n = e - s + 2;
                    for (int k = s; k <= e; k++)
                    {
                        float t = (k - s + 1) / (float)n;
                        float v = CatmullRom(p0, p1, p2, p3, t);
                        float clipped = d[k * ch + c];
                        // never pull a clipped sample back toward zero: the true peak was at least this big
                        d[k * ch + c] = Math.Sign(clipped) * Math.Max(Math.Abs(clipped), Math.Abs(v));
                    }
                }
            }
        }

        private static float CatmullRom(float p0, float p1, float p2, float p3, float t)
        {
            // Wide tangents so a flat-topped run is rebuilt as a rounded peak.
            float m1 = (p2 - p0) * 0.5f + Math.Sign(p1) * Math.Abs(p1 - p0) * 2f;
            float m2 = (p3 - p1) * 0.5f - Math.Sign(p2) * Math.Abs(p3 - p2) * 2f;
            float t2 = t * t, t3 = t2 * t;
            return (2 * t3 - 3 * t2 + 1) * p1 + (t3 - 2 * t2 + t) * m1 + (-2 * t3 + 3 * t2) * p2 + (t3 - t2) * m2;
        }

        private static int FirstFrameAbove(float[] d, int ch, float th)
        {
            for (int i = 0; i < d.Length; i++) if (Math.Abs(d[i]) > th) return i / ch;
            return -1;
        }

        /// <summary>
        /// End of a one-shot: the last 10 ms window whose RMS is within 40 dB of the loudest window
        /// (and above -55 dBFS), so a steady noise floor or long room tail doesn't keep it going.
        /// </summary>
        private static int TailEnd(float[] d, int ch, int rate)
        {
            int win = Math.Max(1, rate / 100), frames = d.Length / ch, windows = frames / win;
            if (windows == 0) return LastFrameAbove(d, ch, TailThreshold);
            var rms = new float[windows];
            float loudest = 0f;
            for (int w = 0; w < windows; w++)
            {
                double sum = 0;
                for (int i = w * win * ch; i < (w + 1) * win * ch; i++) sum += d[i] * d[i];
                rms[w] = (float)Math.Sqrt(sum / (win * ch));
                loudest = Math.Max(loudest, rms[w]);
            }
            float th = Math.Max(TailThreshold, loudest * 0.01f);
            for (int w = windows - 1; w >= 0; w--) if (rms[w] > th) return (w + 1) * win;
            return -1;
        }

        private static int LastFrameAbove(float[] d, int ch, float th)
        {
            for (int i = d.Length - 1; i >= 0; i--) if (Math.Abs(d[i]) > th) return i / ch;
            return -1;
        }

        private static void Fade(float[] d, int ch, int from, int count, bool fadeIn)
        {
            if (count <= 0) return;
            for (int f = 0; f < count; f++)
            {
                float g = (f + 0.5f) / count;
                if (!fadeIn) g = 1f - g;
                for (int c = 0; c < ch; c++) d[(from + f) * ch + c] *= g;
            }
        }

        private static float Db(float a) => 20f * (float)Math.Log10(Math.Max(a, 1e-9f));

        // ------------------------------------------------------------------ WAV I/O

        public static float[] Read(string path, out int ch, out int rate)
        {
            byte[] b = File.ReadAllBytes(path);
            ch = 0; rate = 0;
            int pos = 12, bits = 16, format = 1;
            float[] data = null;
            while (pos + 8 <= b.Length)
            {
                string id = Encoding.ASCII.GetString(b, pos, 4);
                int size = BitConverter.ToInt32(b, pos + 4);
                int body = pos + 8;
                if (id == "fmt ")
                {
                    format = BitConverter.ToInt16(b, body);
                    ch = BitConverter.ToInt16(b, body + 2);
                    rate = BitConverter.ToInt32(b, body + 4);
                    bits = BitConverter.ToInt16(b, body + 14);
                    if (format == -2) format = BitConverter.ToInt16(b, body + 24);   // WAVE_FORMAT_EXTENSIBLE
                }
                else if (id == "data")
                {
                    size = Math.Min(size, b.Length - body);
                    int bps = bits / 8, n = size / bps;
                    data = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        int o = body + i * bps;
                        if (format == 3 && bits == 32) data[i] = BitConverter.ToSingle(b, o);
                        else if (bits == 16) data[i] = BitConverter.ToInt16(b, o) / 32768f;
                        else if (bits == 24) data[i] = (b[o] | (b[o + 1] << 8) | ((sbyte)b[o + 2] << 16)) / 8388608f;
                        else if (bits == 32) data[i] = BitConverter.ToInt32(b, o) / 2147483648f;
                        else data[i] = (b[o] - 128) / 128f;
                    }
                }
                pos = body + size + (size & 1);
            }
            if (data == null || ch == 0) throw new InvalidDataException("Not a readable WAV: " + path);
            return data;
        }

        public static void Write(string path, float[] d, int ch, int rate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int bytes = d.Length * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)ch);
                w.Write(rate); w.Write(rate * ch * 2); w.Write((short)(ch * 2)); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(bytes);
                foreach (float v in d) w.Write((short)Math.Round(Math.Max(-1f, Math.Min(1f, v)) * 32767f));
            }
        }
    }
}
