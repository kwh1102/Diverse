using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// 효과음을 코드로 합성한다(칩튠 스타일). 외부 사운드를 쓰려면 Resources/Sfx/&lt;이름&gt;.wav 를 두면 우선 사용.
    /// </summary>
    public class Sfx : MonoBehaviour
    {
        public static Sfx I { get; private set; }
        const int Rate = 44100;
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly List<AudioSource> sources = new List<AudioSource>();
        readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();
        AudioSource music;
        public static float Volume = 0.6f;
        public static float MusicVolume = 0.35f;

        public static Sfx Create()
        {
            var go = new GameObject("Sfx");
            I = go.AddComponent<Sfx>();
            for (int i = 0; i < 16; i++)
            {
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                I.sources.Add(s);
            }
            I.music = go.AddComponent<AudioSource>();
            I.music.loop = true;
            I.music.playOnAwake = false;
            return I;
        }

        public static void Play(string name, float vol = 1f, float pitch = 1f, float pitchJitter = 0.06f)
        {
            if (I == null) return;
            // 같은 소리가 한 프레임에 수십 번 겹치지 않게
            if (I.lastPlayed.TryGetValue(name, out var t) && Time.unscaledTime - t < 0.03f) return;
            I.lastPlayed[name] = Time.unscaledTime;
            var clip = I.Clip(name);
            if (clip == null) return;
            AudioSource src = null;
            foreach (var s in I.sources) if (!s.isPlaying) { src = s; break; }
            src ??= I.sources[Random.Range(0, I.sources.Count)];
            src.pitch = pitch * (1 + Random.Range(-pitchJitter, pitchJitter));
            src.PlayOneShot(clip, vol * Volume);
        }

        public static void PlayMusic(bool on)
        {
            if (I == null) return;
            if (!on) { I.music.Stop(); return; }
            if (I.music.isPlaying) return;
            I.music.clip = I.Clip("music_field");
            I.music.volume = MusicVolume;
            I.music.Play();
        }

        public static void SetMusicVolume(float v) { MusicVolume = v; if (I != null) I.music.volume = v; }

        AudioClip Clip(string name)
        {
            if (clips.TryGetValue(name, out var c)) return c;
            c = Resources.Load<AudioClip>("Sfx/" + name) ?? Synth(name);
            clips[name] = c;
            return c;
        }

        // ───────────────── 합성기 ─────────────────

        static float Noise(ref uint s) { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFF) / 32768f - 1f; }
        static float Square(float ph) => (ph % 1f) < 0.5f ? 1f : -1f;
        static float Tri(float ph) { float p = ph % 1f; return 4f * Mathf.Abs(p - 0.5f) - 1f; }

        delegate float Gen(float t, float dur, ref uint seed);

        static AudioClip Make(string name, float dur, Gen g)
        {
            int n = Mathf.CeilToInt(dur * Rate);
            var data = new float[n];
            uint seed = Hash.Str(name) | 1;
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(g(i / (float)Rate, dur, ref seed), -1, 1);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Env(float t, float dur, float attack = 0.003f)
        {
            if (t < attack) return t / attack;
            float k = 1 - (t - attack) / (dur - attack);
            return Mathf.Max(0, k * k);
        }

        AudioClip Synth(string name)
        {
            switch (name)
            {
                case "swing":
                case "swing_light":
                case "swing_heavy":
                {
                    float dur = name == "swing_heavy" ? 0.24f : name == "swing_light" ? 0.09f : 0.14f;
                    float f0 = name == "swing_heavy" ? 900 : name == "swing_light" ? 3200 : 2000;
                    float lp = 0;
                    return Make(name, dur, (float t, float d, ref uint s) =>
                    {
                        float k = t / d;
                        float n = Noise(ref s);
                        float cutoff = Mathf.Lerp(0.05f, 0.5f, Mathf.Sin(k * Mathf.PI)) * f0 / 2000f;
                        lp += (n - lp) * Mathf.Clamp01(cutoff);
                        return lp * Mathf.Sin(k * Mathf.PI) * 0.9f;
                    });
                }
                case "hit":
                case "hit_light":
                case "hit_heavy":
                case "hit_metal":
                case "hit_slice":
                {
                    float dur = name == "hit_heavy" ? 0.22f : name == "hit_light" ? 0.08f : 0.13f;
                    float f0 = name == "hit_heavy" ? 90 : name == "hit_light" ? 260 : 160;
                    float ring = name == "hit_metal" ? 1 : 0;
                    float slice = name == "hit_slice" ? 1 : 0;
                    float ph = 0;
                    return Make(name, dur, (float t, float d, ref uint s) =>
                    {
                        float f = f0 * Mathf.Lerp(2.2f, 0.6f, t / d);
                        ph += f / Rate;
                        float body = Square(ph) * 0.35f + Mathf.Sin(ph * Mathf.PI * 2) * 0.5f;
                        float crack = Noise(ref s) * Mathf.Exp(-t * 60f) * 0.9f;
                        float metal = ring * Mathf.Sin(t * 2 * Mathf.PI * 1850) * Mathf.Exp(-t * 18f) * 0.35f;
                        float sl = slice * Noise(ref s) * Mathf.Exp(-t * 25f) * Mathf.Sin(t * 2 * Mathf.PI * 4000) * 0.5f;
                        return (body * Env(t, d) + crack + metal + sl) * 0.75f;
                    });
                }
                case "crit":
                {
                    float ph = 0;
                    return Make(name, 0.2f, (float t, float d, ref uint s) =>
                    {
                        ph += Mathf.Lerp(1400, 2400, t / d) / Rate;
                        return (Square(ph) * 0.25f + Noise(ref s) * Mathf.Exp(-t * 40) * 0.6f) * Env(t, d);
                    });
                }
                case "shoot":
                {
                    float ph = 0;
                    return Make(name, 0.12f, (float t, float d, ref uint s) =>
                    {
                        ph += Mathf.Lerp(700, 180, t / d) / Rate;
                        return (Tri(ph) * 0.5f + Noise(ref s) * Mathf.Exp(-t * 50) * 0.7f) * Env(t, d);
                    });
                }
                case "dash":
                {
                    float lp = 0;
                    return Make(name, 0.16f, (float t, float d, ref uint s) =>
                    {
                        lp += (Noise(ref s) - lp) * Mathf.Lerp(0.6f, 0.08f, t / d);
                        return lp * Env(t, d, 0.01f) * 0.9f;
                    });
                }
                case "hurt":
                {
                    float ph = 0;
                    return Make(name, 0.2f, (float t, float d, ref uint s) =>
                    {
                        ph += Mathf.Lerp(520, 160, t / d) / Rate;
                        return (Square(ph) * 0.4f + Noise(ref s) * 0.25f) * Env(t, d);
                    });
                }
                case "enemy_die":
                {
                    float ph = 0;
                    return Make(name, 0.28f, (float t, float d, ref uint s) =>
                    {
                        ph += Mathf.Lerp(600, 90, t / d) / Rate;
                        return (Square(ph) * 0.3f + Noise(ref s) * Mathf.Exp(-t * 14) * 0.5f) * Env(t, d);
                    });
                }
                case "explode":
                {
                    float lp = 0;
                    return Make(name, 0.5f, (float t, float d, ref uint s) =>
                    {
                        lp += (Noise(ref s) - lp) * Mathf.Lerp(0.5f, 0.03f, t / d);
                        return (lp * 1.4f + Mathf.Sin(t * 2 * Mathf.PI * Mathf.Lerp(80, 30, t / d)) * 0.6f) * Env(t, d);
                    });
                }
                case "thunder":
                {
                    float lp = 0;
                    return Make(name, 0.45f, (float t, float d, ref uint s) =>
                    {
                        float n = Noise(ref s);
                        lp += (n - lp) * 0.25f;
                        float crackle = (t < 0.06f ? n : lp) * (1 + 0.5f * Square(t * 37));
                        return crackle * Env(t, d) * 0.9f;
                    });
                }
                case "pickup":
                case "coin":
                {
                    float ph = 0;
                    float a = name == "coin" ? 1320 : 990, b = name == "coin" ? 1760 : 1480;
                    return Make(name, 0.12f, (float t, float d, ref uint s) =>
                    {
                        ph += (t < 0.04f ? a : b) / Rate;
                        return Square(ph) * 0.25f * Env(t, d);
                    });
                }
                case "levelup":
                {
                    float ph = 0;
                    float[] notes = { 523, 659, 784, 1047 };
                    return Make(name, 0.6f, (float t, float d, ref uint s) =>
                    {
                        int i = Mathf.Min(3, (int)(t / 0.1f));
                        ph += notes[i] / Rate;
                        return (Square(ph) * 0.2f + Tri(ph * 2) * 0.15f) * Mathf.Clamp01(1 - (t - 0.3f) / 0.3f);
                    });
                }
                case "evolve":
                {
                    float ph = 0;
                    return Make(name, 1.1f, (float t, float d, ref uint s) =>
                    {
                        float f = 330 * Mathf.Pow(2, Mathf.Floor(t / 0.09f) % 8 / 12f * 4);
                        ph += f / Rate;
                        return (Tri(ph) * 0.3f + Square(ph * 0.5f) * 0.08f) * Env(t, d, 0.05f);
                    });
                }
                case "ui":
                case "ui_select":
                {
                    float ph = 0;
                    float f = name == "ui" ? 880 : 1320;
                    return Make(name, 0.05f, (float t, float d, ref uint s) => { ph += f / Rate; return Square(ph) * 0.18f * Env(t, d); });
                }
                case "guard":
                {
                    return Make(name, 0.25f, (float t, float d, ref uint s) =>
                        (Mathf.Sin(t * 2 * Mathf.PI * 1200) * 0.4f + Mathf.Sin(t * 2 * Mathf.PI * 1810) * 0.3f + Noise(ref s) * Mathf.Exp(-t * 60) * 0.6f) * Env(t, d));
                }
                case "charge":
                {
                    float ph = 0;
                    return Make(name, 0.35f, (float t, float d, ref uint s) =>
                    {
                        ph += Mathf.Lerp(200, 900, t / d) / Rate;
                        return Tri(ph) * 0.3f * Mathf.Sin(t / d * Mathf.PI);
                    });
                }
                case "door":
                case "chest":
                {
                    float ph = 0;
                    return Make(name, 0.3f, (float t, float d, ref uint s) =>
                    {
                        ph += Mathf.Lerp(180, 320, t / d) / Rate;
                        return (Square(ph) * 0.2f + Noise(ref s) * 0.1f) * Env(t, d, 0.02f);
                    });
                }
                case "boss_roar":
                {
                    float ph = 0, lp = 0;
                    return Make(name, 0.9f, (float t, float d, ref uint s) =>
                    {
                        ph += (90 + Mathf.Sin(t * 30) * 15) / Rate;
                        lp += (Noise(ref s) - lp) * 0.1f;
                        return (Square(ph) * 0.35f + lp * 0.8f) * Env(t, d, 0.08f);
                    });
                }
                case "death":
                {
                    float ph = 0;
                    float[] notes = { 392, 330, 262, 196 };
                    return Make(name, 1.2f, (float t, float d, ref uint s) =>
                    {
                        int i = Mathf.Min(3, (int)(t / 0.25f));
                        ph += notes[i] / Rate;
                        return Tri(ph) * 0.35f * Mathf.Clamp01(1 - t / d);
                    });
                }
                case "music_field": return Music();
            }
            return null;
        }

        /// <summary>16마디 루프 BGM (오카리나풍 멜로디 + 베이스 + 아르페지오).</summary>
        static AudioClip Music()
        {
            float bpm = 96;
            float beat = 60f / bpm;
            int bars = 16;
            float dur = bars * 4 * beat;
            int[] chords = { 0, 5, 3, 4, 0, 5, 1, 4, 0, 3, 5, 4, 3, 4, 0, 0 };   // I vi IV V ...
            int[] scale = { 0, 2, 4, 5, 7, 9, 11, 12, 14, 16 };
            int[] melody =
            {
                4,-1,5,4, 2,-1,0,-1, 1,2,4,-1, 2,-1,-1,-1,
                4,-1,5,7, 5,4,2,-1, 1,-1,2,1, 0,-1,-1,-1,
                7,-1,5,4, 5,-1,4,2, 4,-1,2,0, 1,-1,-1,-1,
                2,4,5,4, 2,-1,1,-1, 0,-1,-1,-1, -1,-1,-1,-1,
            };
            float root = 220f;
            float Freq(int semis) => root * Mathf.Pow(2, semis / 12f);
            float pm = 0, pb = 0, pa = 0;
            return Make("music_field", dur, (float t, float d, ref uint s) =>
            {
                float b = t / beat;
                int bar = Mathf.Min(bars - 1, (int)(b / 4));
                int ch = chords[bar];
                int chordRoot = scale[ch % 7];
                // 멜로디 (8분음)
                int step = (int)(b * 2) % 64;
                int note = melody[step];
                float mel = 0;
                if (note >= 0)
                {
                    int start = step;
                    while (start > 0 && melody[start] == -1) start--;
                    float since = (b * 2) - (int)(b * 2) + (step - start);
                    pm += Freq(scale[note] + 12) / Rate;
                    float vib = 1 + Mathf.Sin(t * 30) * 0.004f;
                    mel = (Tri(pm * vib) * 0.7f + Mathf.Sin(pm * Mathf.PI * 4) * 0.15f) * Mathf.Exp(-since * 0.9f) * 0.22f;
                }
                // 베이스 (4분음)
                pb += Freq(chordRoot - 12) / Rate;
                float bf = b % 1f;
                float bass = Tri(pb) * 0.18f * Mathf.Exp(-bf * 2.5f);
                // 아르페지오 (16분음)
                int arpI = (int)(b * 4) % 4;
                int[] arpNotes = { 0, 2, 4, 2 };
                int an = scale[(ch + arpNotes[arpI]) % 7] + (ch + arpNotes[arpI] >= 7 ? 12 : 0);
                pa += Freq(an) / Rate;
                float af = (b * 4) % 1f;
                float arp = Square(pa) * 0.045f * Mathf.Exp(-af * 5);
                // 하이햇
                float hat = (int)(b * 2) % 2 == 1 ? Noise(ref s) * 0.03f * Mathf.Exp(-((b * 2) % 1f) * 20) : 0;
                return mel + bass + arp + hat;
            });
        }
    }
}
