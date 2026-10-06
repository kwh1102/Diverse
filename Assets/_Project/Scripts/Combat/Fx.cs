using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// 이펙트 풀. 매 타격마다 오브젝트를 만들고 지우지 않도록 재활용한다.
    /// 모든 이펙트는 unscaled 시간 기반이 아니라 게임 시간(Time.deltaTime) 기반 → 히트스톱 중에 같이 멈춘다.
    /// </summary>
    public class Fx : MonoBehaviour
    {
        public static Fx I { get; private set; }

        class Anim
        {
            public GameObject go;
            public SpriteRenderer sr;
            public Sprite[] frames;
            public float t, frameTime;
            public bool active;
            public Transform follow;
            public Vector3 followOffset;
        }

        class Particle
        {
            public GameObject go;
            public SpriteRenderer sr;
            public Vector2 pos, vel;
            public float t, life, gravity, drag, z, vz;
            public Color32 col;
            public bool active, ground;
        }

        class Ghost
        {
            public GameObject go;
            public SpriteRenderer sr;
            public float t, life;
            public Color col;
            public bool active;
        }

        class Popup
        {
            public GameObject go;
            public TextMesh tm;
            public MeshRenderer mr;
            public Vector2 pos;
            public float t, life, vy;
            public Color col;
            public float scale;
            public bool active;
        }

        readonly List<Anim> anims = new List<Anim>();
        readonly List<Particle> parts = new List<Particle>();
        readonly List<Ghost> ghosts = new List<Ghost>();
        readonly List<Popup> pops = new List<Popup>();
        [SerializeField] Font font;       // damage numbers (Galmuri11-Bold)

        void Awake() => I = this;
        void OnDestroy() { if (I == this) I = null; }

        // ───────── 스프라이트 애니메이션 ─────────

        public SpriteRenderer Play(Sprite[] frames, Vector2 pos, float angle = 0, float fps = 24, float scale = 1, Color? tint = null, bool additive = false, int order = 0, Transform follow = null, bool flipY = false)
        {
            if (frames == null || frames.Length == 0) return null;
            Anim a = null;
            foreach (var x in anims) if (!x.active) { a = x; break; }
            if (a == null)
            {
                var go = new GameObject("fx");
                go.transform.SetParent(transform, false);
                a = new Anim { go = go, sr = go.AddComponent<SpriteRenderer>() };
                anims.Add(a);
            }
            a.active = true;
            a.frames = frames;
            a.t = 0;
            a.frameTime = 1f / fps;
            a.follow = follow;
            a.followOffset = follow != null ? (Vector3)pos - follow.position : Vector3.zero;
            a.go.SetActive(true);
            a.go.transform.position = new Vector3(pos.x, pos.y, 0);
            a.go.transform.rotation = Quaternion.Euler(0, 0, angle);
            a.go.transform.localScale = new Vector3(scale, flipY ? -scale : scale, 1);
            a.sr.sprite = frames[0];
            a.sr.color = tint ?? Color.white;
            a.sr.sharedMaterial = additive ? Art.AddMat : Art.SpriteMat;
            a.sr.sortingOrder = order != 0 ? order : Art.SortY(pos.y) + 40;
            return a.sr;
        }

        // ───────── 파티클 (도트 사각형) ─────────

        public void Burst(Vector2 pos, Color32 col, int count, float speed, float life = 0.45f, float gravity = 0f, int size = 2, float spreadDeg = 360, float dirDeg = 0, bool ground = false, bool additive = false)
        {
            for (int i = 0; i < count; i++)
            {
                var p = GetParticle(size, additive);
                float a = (dirDeg + Random.Range(-spreadDeg / 2, spreadDeg / 2)) * Mathf.Deg2Rad;
                float sp = speed * Random.Range(0.4f, 1.1f);
                p.pos = pos;
                p.vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * sp;
                p.life = life * Random.Range(0.6f, 1.2f);
                p.t = 0;
                p.gravity = gravity;
                p.drag = 4f;
                p.col = col;
                p.ground = ground;
                p.z = ground ? Random.Range(0.05f, 0.3f) : 0;
                p.vz = ground ? Random.Range(2f, 5f) : 0;
                p.sr.color = col;
            }
        }

        Particle GetParticle(int size, bool additive)
        {
            Particle p = null;
            foreach (var x in parts) if (!x.active) { p = x; break; }
            if (p == null)
            {
                var go = new GameObject("p");
                go.transform.SetParent(transform, false);
                p = new Particle { go = go, sr = go.AddComponent<SpriteRenderer>() };
                parts.Add(p);
            }
            p.active = true;
            p.go.SetActive(true);
            p.sr.sprite = Art.Particle(size);
            p.sr.sharedMaterial = additive ? Art.AddMat : Art.SpriteMat;
            return p;
        }

        // ───────── 잔상 ─────────

        public void Afterimage(SpriteRenderer src, Color col, float life = 0.25f, bool silhouette = true)
        {
            if (src == null || src.sprite == null) return;
            Ghost g = null;
            foreach (var x in ghosts) if (!x.active) { g = x; break; }
            if (g == null)
            {
                var go = new GameObject("ghost");
                go.transform.SetParent(transform, false);
                g = new Ghost { go = go, sr = go.AddComponent<SpriteRenderer>() };
                ghosts.Add(g);
            }
            g.active = true;
            g.go.SetActive(true);
            g.go.transform.SetPositionAndRotation(src.transform.position, src.transform.rotation);
            g.go.transform.localScale = src.transform.lossyScale;
            g.sr.sprite = silhouette ? Art.Silhouette(src.sprite) : src.sprite;
            g.sr.flipX = src.flipX;
            g.sr.sharedMaterial = Art.AddMat;
            g.sr.sortingOrder = src.sortingOrder - 1;
            g.col = col;
            g.sr.color = col;
            g.t = 0; g.life = life;
        }

        // ───────── 데미지 숫자 ─────────

        public void Number(Vector2 pos, string text, Color col, float scale = 1f, float life = 0.75f)
        {
            Popup p = null;
            foreach (var x in pops) if (!x.active) { p = x; break; }
            if (p == null)
            {
                var go = new GameObject("num");
                go.transform.SetParent(transform, false);
                var tm = go.AddComponent<TextMesh>();
                tm.font = font;
                tm.fontSize = 32;
                tm.characterSize = 0.045f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                var mr = go.GetComponent<MeshRenderer>();
                if (font != null) mr.sharedMaterial = font.material;
                if (font != null) font.material.mainTexture.filterMode = FilterMode.Point;
                mr.sortingOrder = 32000;
                p = new Popup { go = go, tm = tm, mr = mr };
                pops.Add(p);
            }
            p.active = true;
            p.go.SetActive(true);
            p.tm.text = text;
            p.col = col;
            p.tm.color = col;
            p.pos = pos + new Vector2(Random.Range(-0.2f, 0.2f), 0.6f);
            p.vy = 3.2f;
            p.t = 0; p.life = life; p.scale = scale;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float udt = Time.unscaledDeltaTime;

            foreach (var a in anims)
            {
                if (!a.active) continue;
                a.t += dt;
                int f = (int)(a.t / a.frameTime);
                if (f >= a.frames.Length) { a.active = false; a.go.SetActive(false); continue; }
                a.sr.sprite = a.frames[f];
                if (a.follow != null) a.go.transform.position = a.follow.position + a.followOffset;
            }

            foreach (var p in parts)
            {
                if (!p.active) continue;
                p.t += dt;
                if (p.t >= p.life) { p.active = false; p.go.SetActive(false); continue; }
                p.vel *= Mathf.Exp(-p.drag * dt);
                p.vel.y -= p.gravity * dt;
                p.pos += p.vel * dt;
                if (p.ground)
                {
                    p.vz -= 18f * dt; p.z += p.vz * dt;
                    if (p.z < 0) { p.z = 0; p.vz *= -0.4f; }
                }
                float k = 1 - p.t / p.life;
                var c = (Color)p.col; c.a = k > 0.3f ? 1 : k / 0.3f;
                p.sr.color = c;
                Vector2 render = p.pos + new Vector2(0, p.z);
                // 픽셀 격자에 스냅
                render.x = Mathf.Round(render.x * Art.PPU) / Art.PPU;
                render.y = Mathf.Round(render.y * Art.PPU) / Art.PPU;
                p.go.transform.position = render;
                p.sr.sortingOrder = Art.SortY(p.pos.y) + 30;
            }

            foreach (var g in ghosts)
            {
                if (!g.active) continue;
                g.t += dt;
                if (g.t >= g.life) { g.active = false; g.go.SetActive(false); continue; }
                var c = g.col; c.a *= 1 - g.t / g.life;
                g.sr.color = c;
            }

            foreach (var p in pops)
            {
                if (!p.active) continue;
                p.t += udt;
                if (p.t >= p.life) { p.active = false; p.go.SetActive(false); continue; }
                p.vy = Mathf.Max(0, p.vy - 9f * udt);
                p.pos.y += p.vy * udt;
                float pop = p.t < 0.08f ? Mathf.Lerp(1.6f, 1f, p.t / 0.08f) : 1f;
                p.go.transform.position = new Vector3(p.pos.x, p.pos.y, 0);
                p.go.transform.localScale = Vector3.one * p.scale * pop;
                var c = p.col;
                float k = p.t / p.life;
                c.a = k < 0.7f ? 1 : 1 - (k - 0.7f) / 0.3f;
                p.tm.color = c;
            }
        }

        public void ClearAll()
        {
            foreach (var a in anims) { a.active = false; a.go.SetActive(false); }
            foreach (var p in parts) { p.active = false; p.go.SetActive(false); }
            foreach (var g in ghosts) { g.active = false; g.go.SetActive(false); }
            foreach (var p in pops) { p.active = false; p.go.SetActive(false); }
        }

        // ───────── 자주 쓰는 조합 ─────────

        public void HitSpark(Vector2 pos, Vector2 dir, Color32 col, bool crit, float scale = 1f)
        {
            Play(Art.HitSpark(col, crit ? 12 : 9), pos, Random.Range(0, 360f), 28, scale, null, true);
            Burst(pos, col, crit ? 12 : 7, crit ? 9 : 7, 0.35f, 0, 2, 70, dir.Angle());
            Burst(pos, Pal.White, crit ? 6 : 3, 6, 0.25f, 0, 1, 120, dir.Angle());
        }

        public void DustPuff(Vector2 pos, bool flip)
        {
            Play(Art.Dust(), pos, 0, 18, flip ? -1 : 1);
        }
    }
}
