using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>좌표 기반 결정적 해시. 같은 시드+좌표는 항상 같은 값을 돌려준다 (월드 재생성의 핵심).</summary>
    public static class Hash
    {
        public static uint U(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u + 0x7F4A7C15u;
                h ^= (uint)x * 0x85EBCA77u; h = (h << 13) | (h >> 19); h *= 0xC2B2AE3Du;
                h ^= (uint)y * 0x27D4EB2Fu; h = (h << 17) | (h >> 15); h *= 0x165667B1u;
                h ^= h >> 15; h *= 0x85EBCA77u; h ^= h >> 13; h *= 0xC2B2AE3Du; h ^= h >> 16;
                return h;
            }
        }

        public static float F(int x, int y, int seed) => (U(x, y, seed) & 0xFFFFFF) / 16777216f;

        public static uint Str(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in s) { h ^= c; h *= 16777619; }
                return h;
            }
        }
    }

    /// <summary>xorshift 난수. 시드를 고정하면 결과가 재현된다.</summary>
    public class Rng
    {
        uint s;
        public Rng(uint seed) { s = seed == 0 ? 0x1234567u : seed; Next(); Next(); }
        public uint Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s; }
        public float Value => (Next() & 0xFFFFFF) / 16777216f;
        public int Range(int minInclusive, int maxExclusive) => maxExclusive <= minInclusive ? minInclusive : minInclusive + (int)(Next() % (uint)(maxExclusive - minInclusive));
        public float Range(float a, float b) => a + (b - a) * Value;
        public bool Chance(float p) => Value < p;
        public T Pick<T>(IList<T> list) => list[Range(0, list.Count)];

        public T Weighted<T>(IList<T> items, System.Func<T, float> weight)
        {
            float total = 0;
            foreach (var it in items) total += Mathf.Max(0, weight(it));
            if (total <= 0) return items[Range(0, items.Count)];
            float r = Value * total;
            foreach (var it in items)
            {
                r -= Mathf.Max(0, weight(it));
                if (r <= 0) return it;
            }
            return items[items.Count - 1];
        }
    }

    public static class Noise
    {
        public static float Perlin(float x, float y, int seed, float freq)
        {
            float ox = (seed % 9973) * 1.7310f + 311.7f;
            float oy = (seed % 7919) * 2.1130f + 127.1f;
            return Mathf.PerlinNoise(x * freq + ox, y * freq + oy);
        }

        public static float Fractal(float x, float y, int seed, float freq, int octaves = 3)
        {
            float sum = 0, amp = 1, norm = 0;
            for (int i = 0; i < octaves; i++)
            {
                sum += Perlin(x, y, seed + i * 1013, freq) * amp;
                norm += amp; amp *= 0.5f; freq *= 2f;
            }
            return sum / norm;
        }
    }

    /// <summary>한국어 조사 처리 (을/를, 이/가 …).</summary>
    public static class Ko
    {
        public static bool HasBatchim(string w)
        {
            if (string.IsNullOrEmpty(w)) return false;
            char c = w[w.Length - 1];
            if (c >= '0' && c <= '9') return "013678".IndexOf(c) >= 0;
            if (c < 0xAC00 || c > 0xD7A3) return false;
            return (c - 0xAC00) % 28 != 0;
        }
        static bool RieulBatchim(string w)
        {
            char c = w[w.Length - 1];
            return c >= 0xAC00 && c <= 0xD7A3 && (c - 0xAC00) % 28 == 8;
        }
        public static string Eul(string w) => w + (HasBatchim(w) ? "을" : "를");
        public static string I(string w) => w + (HasBatchim(w) ? "이" : "가");
        public static string Eun(string w) => w + (HasBatchim(w) ? "은" : "는");
        public static string Wa(string w) => w + (HasBatchim(w) ? "과" : "와");
        public static string Ro(string w) => w + (HasBatchim(w) && !RieulBatchim(w) ? "으로" : "로");
    }

    public static class MathX
    {
        public static Vector2 Rotate(this Vector2 v, float deg)
        {
            float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }
        public static float Angle(this Vector2 v) => Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
        public static Vector2 Dir(float deg) => new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));
        public static Vector2 SafeNormal(this Vector2 v, Vector2 fallback) => v.sqrMagnitude > 1e-6f ? v.normalized : fallback;
        public static Vector3 V3(this Vector2 v) => new Vector3(v.x, v.y, 0);
        public static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
        public static int FloorToTile(float v) => Mathf.FloorToInt(v);
        public static float EaseOutCubic(float t) { t = 1 - Mathf.Clamp01(t); return 1 - t * t * t; }
        public static float EaseOutBack(float t) { t = Mathf.Clamp01(t); float c1 = 1.70158f, c3 = c1 + 1; return 1 + c3 * Mathf.Pow(t - 1, 3) + c1 * Mathf.Pow(t - 1, 2); }
    }
}
