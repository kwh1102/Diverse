using System;
using UnityEngine;

namespace Diverse
{
    /// <summary>The named rule owns its silhouettes. World coordinates come from the action that emitted the cue.</summary>
    public static class TechniqueFx
    {
        public enum Phase { Marker, Resolve, Path, Flight }
        const int Tile = 48;
        const int PathWidth = 48;

        public static Sprite Icon(ActiveSkillData skill) => Frames(skill.technique, Phase.Marker, Pal.Hex(skill.color))[0];
        public static Sprite[] Flight(ActiveSkillData skill) => Frames(skill.technique, Phase.Flight, Pal.Hex(skill.color));

        public static void Show(ActiveSkillData skill, Vector2 at, Vector2 direction, float diameter, Phase phase = Phase.Resolve)
        {
            var frames = Frames(skill.technique, phase, Pal.Hex(skill.color));
            var library = CombatVisuals.Library;
            float seconds = library != null ? library.skillMainSeconds : ActiveSkillGenerator.Rules.castVisualDuration;
            if (skill.HasTechnique && skill.slot == 3 && library != null) seconds *= library.skillUltimateScale;
            Fx.I?.Play(frames, at, direction.Angle(), frames.Length / seconds,
                diameter * Art.PPU / 38, order: Art.SortY(at.y) + 12);
            if (phase == Phase.Resolve && skill.HasTechnique)
            {
                var accent = ActiveEffectArt.TechniqueAccent(skill);
                if (accent != null) Fx.I?.Play(accent, at, direction.Angle(), accent.Length / seconds,
                    diameter * Art.PPU / accent[0].rect.width, new Color(1, 1, 1, .8f), order: Art.SortY(at.y) + 13);
            }
        }

        public static Fx.HeldAnimation Hold(ActiveSkillData skill, Vector2 at, Vector2 dir, float diameter, float seconds = 0) =>
            Fx.I?.Hold(Frames(skill.technique, Phase.Marker, Pal.Hex(skill.color)), at, dir.Angle(),
                diameter * Art.PPU / 38, Art.SortY(at.y) - 1, seconds);

        public static Fx.HeldAnimation HoldPath(ActiveSkillData skill, Vector2 from, Vector2 to)
        {
            var trace = Fx.I?.Hold(Frames(skill.technique, Phase.Path, Pal.Hex(skill.color)), from, 0, 1, Art.SortY(from.y) + 3);
            SetPath(trace, from, to);
            return trace;
        }

        public static void SetPath(Fx.HeldAnimation trace, Vector2 from, Vector2 to)
        {
            Vector2 d = to - from;
            trace?.Set(from, d.Angle(), new Vector3(d.magnitude * Art.PPU / (PathWidth - 1), 1, 1));
        }

        public static void Trace(ActiveSkillData skill, Vector2 from, Vector2 to)
        {
            Vector2 d = to - from;
            if (d.sqrMagnitude <= Mathf.Epsilon) return;
            var frames = Frames(skill.technique, Phase.Path, Pal.Hex(skill.color));
            var sr = Fx.I?.Play(frames, from, d.Angle(), frames.Length / CombatVisuals.Library.skillAfterSeconds,
                order: Art.SortY(from.y) + 8);
            if (sr != null) sr.transform.localScale = new Vector3(d.magnitude * Art.PPU / (PathWidth - 1), 1, 1);
        }

        public static void Warm(ActiveSkillData skill)
        {
            var color = Pal.Hex(skill.color);
            foreach (Phase phase in Enum.GetValues(typeof(Phase))) Frames(skill.technique, phase, color);
            ActiveEffectArt.TechniqueAccent(skill);
        }

        internal static Sprite[] Frames(Technique id, Phase phase, Color32 color) =>
            Art.GetAnim($"technique_{id}_{phase}_{color}", () => Build(id, phase, color));

        static Sprite[] Build(Technique id, Phase phase, Color32 color)
        {
            var result = new Sprite[phase == Phase.Marker ? 1 : 5];
            var dark = Pal.Hex("473447");
            var light = Pal.Lerp(color, Pal.White, .7f);
            for (int frame = 0; frame < result.Length; frame++)
            {
                float t = frame / 4f;
                if (phase == Phase.Flight)
                {
                    var shot = new PixelCanvas(24, 14);
                    if (id == Technique.BladeReflect)
                    {
                        shot.ThickLine(new Vector2(4, 4), new Vector2(17, 7), 2, dark);
                        shot.ThickLine(new Vector2(4, 10), new Vector2(17, 7), 2, dark);
                        shot.Line(4, 4, 17, 7, color); shot.Line(4, 10, 17, 7, light);
                    }
                    else
                    {
                        shot.ThickLine(new Vector2(5, 7), new Vector2(17, 7), 3, dark);
                        shot.ThickLine(new Vector2(7, 7), new Vector2(17, 7), 1.5f, color);
                        shot.Line(10, 7 + frame % 2, 19, 7, light);
                    }
                    result[frame] = shot.ToSpriteCentered();
                    continue;
                }
                if (phase == Phase.Path)
                {
                    result[frame] = PathFrame(id, frame, dark, color, light);
                    continue;
                }

                var c = new PixelCanvas(Tile, Tile);
                float spread = phase == Phase.Marker ? .77f : Mathf.Lerp(.7f, 1.15f, t);
                Color32 front = t < .73f ? light : color;
                byte opacity = frame < 3 ? (byte)255 : frame == 3 ? (byte)170 : (byte)65;
                Vector2 center = new(24, 24);
                Vector2 P(float x, float y) => center + new Vector2(x, y) * spread;
                void Line(float ax, float ay, float bx, float by, float weight = 1)
                {
                    var a = P(ax, ay); var b = P(bx, by);
                    c.ThickLine(a, b, weight + 1, Pal.A(dark, opacity));
                    c.ThickLine(a, b, weight, Pal.A(color, opacity));
                    c.Line(Mathf.RoundToInt(a.x), Mathf.RoundToInt(a.y), Mathf.RoundToInt(b.x), Mathf.RoundToInt(b.y), Pal.A(front, opacity));
                }
                void Arc(float x, float y, float r, float a, float b)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        var from = new Vector2(x,y) + MathX.Dir(Mathf.Lerp(a,b,i/6f)) * r;
                        var to = new Vector2(x,y) + MathX.Dir(Mathf.Lerp(a,b,(i+1)/6f)) * r;
                        Line(from.x,from.y,to.x,to.y);
                    }
                }
                void Cross(float x, float y, float r) { Line(x-r,y,x+r,y); Line(x,y-r,x,y+r); }
                void Diamond(float x, float y, float w, float h)
                { Line(x-w,y,x,y+h); Line(x,y+h,x+w,y); Line(x+w,y,x,y-h); Line(x,y-h,x-w,y); }
                void Shard(float x, float y, float vx, float vy, float size)
                {
                    var tip = P(x + vx * t, y + vy * t);
                    var tail = tip - new Vector2(vx, vy).normalized * size;
                    float width = Mathf.Max(.75f, size * .5f * (1 - t));
                    c.ThickLine(tail, tip, width + .5f, Pal.A(dark, opacity));
                    c.ThickLine(tail, tip, width, Pal.A(color, opacity));
                    c.Set(Mathf.RoundToInt(tip.x), Mathf.RoundToInt(tip.y), Pal.A(front, opacity));
                }

                switch (id)
                {
                    case Technique.BladeReflect:
                        Line(-13, -10, 7, 13, 2);
                        if (phase == Phase.Resolve)
                        {
                            Line(-2, 3, 8 + t * 10, 1, 2.5f);
                            Shard(4, 2, 15, 10, 5); Shard(6, -2, 13, -9, 6); Shard(1, 4, -5, 12, 4);
                        }
                        break;
                    case Technique.WallPin:
                        Line(13, -16, 13, 16, 2); Line(-16, -9, 8, 0, 3); Line(-15, 9, 8, 0, 3);
                        if (phase == Phase.Resolve) { Shard(11, 4, -10, 13, 5); Shard(12, -4, -8, -12, 5); }
                        break;
                    case Technique.Clash:
                        Line(-16, -8, 1, 4, 2); Line(16, 8, -1, -4, 2);
                        if (phase == Phase.Resolve) { Cross(0, 0, 6 + t * 8); Shard(-3, -2, -13, -11, 6); Shard(3, 3, 12, 10, 6); }
                        break;
                    case Technique.OpenGuard: Arc(-2,0,12,65,295); Line(1,13,14,7,2); Cross(11,-5,6); break;
                    case Technique.Tripwire: Diamond(-14,0,4,6); Diamond(14,0,4,6); Line(-10,1,10,1); Line(-10,-2,10,-2); break;
                    case Technique.BindPair: Arc(-8,0,7,-80,240); Arc(8,0,7,100,420); Line(-4,-5,4,5,2); break;
                    case Technique.LureArrow: Line(-4,-9,3,8,2); Line(-10,-9,6,-9); Arc(2,1,12,-40,102); Arc(2,1,17,12,72); break;
                    case Technique.RecallArrow: Line(-15,0,14,0,2); Line(-15,0,-6,8,2); Line(-15,0,-6,-8,2); Arc(5,0,13,75,222); break;
                    case Technique.CaptureRound: Line(-13,-5,9,-5,2); Line(-13,5,9,5,2); Arc(9,0,5,-100,100); Diamond(-4,0,4,4); break;
                    case Technique.WallShatter:
                        Line(-12, -14, -12, 14, 2);
                        Shard(-5, 2, 19, 9, 6); Shard(0, -6, 16, -13, 5);
                        Shard(-3, 7, 11, 13, 4); Shard(4, -1, 13, -5, 3);
                        break;
                    case Technique.IgniteCasings:
                        if (phase == Phase.Marker) { Line(-3, -9, -3, 3, 2); Line(3, -9, 3, 3, 2); }
                        else
                        {
                            Shard(-7, -4, -4, 15, 7); Shard(0, -2, 3, 19, 9); Shard(8, -5, 6, 14, 6);
                            Arc(0, -5, 5 + 9 * t, 174, 358);
                        }
                        break;
                    case Technique.Overwatch: Cross(0,0,4); Line(-16,-7,-16,7,2); Line(16,-7,16,7,2); Line(-16,7,-9,7); Line(16,7,9,7); break;
                    case Technique.ResonanceAnchor:
                        Diamond(0, 4, 10 - t * 4, 12 - t * 4);
                        Arc(0, -7, 10 + t * 9, 178, 360);
                        if (phase == Phase.Resolve) Arc(0, -7, 5 + t * 11, 10, 170);
                        break;
                    case Technique.RewindSeal: Line(-11,13,11,13); Line(-11,-13,11,-13); Line(-10,12,10,-12,2); Line(10,12,-10,-12,2); break;
                    case Technique.TwinGates:
                        Arc(0, 0, 13, 0, 360); Diamond(0, 3, 5, 8); Line(0, -4, 0, -12, 2);
                        if (phase == Phase.Resolve) { Shard(6, 5, 12, 8, 5); Shard(9, -4, 12, -9, 6); }
                        break;
                    case Technique.TideSwitch:
                        Arc(-4, -1, 10, 35 - t * 110, 225 - t * 110);
                        Arc(4, 1, 13, 205 - t * 110, 405 - t * 110);
                        Shard(-12, -7, -6, -7, 4); Shard(11, 6, 8, 9, 4);
                        break;
                    case Technique.ShadowSwap: Diamond(-6,-2,9,12); Diamond(6,4,7,10); Line(-15,-13,13,13); break;
                    case Technique.ThresholdTrap:
                        if (phase == Phase.Marker)
                        {
                            Line(-15, -8, 0, 0, 3); Line(0, 0, -15, 8, 3); Line(4, -10, 4, 10, 2);
                        }
                        else
                        {
                            Arc(0, -4, 11 + t * 5, 164, 375);
                            Shard(-7, 5, -11, 8, 7); Shard(0, 9, -2, 11, 8); Shard(8, 4, 9, 8, 7);
                            Shard(-8, -4, -11, -5, 5); Shard(8, -4, 11, -4, 6);
                            if (frame <= 1) { Cross(0, -1, 7); Line(-5, -5, 5, 3, 2); }
                        }
                        break;
                    case Technique.BreakPursuit: Line(-15,-7,-6,-2,2); Line(-7,-1,3,4,2); Diamond(9,6,5,7); break;
                    case Technique.BackLock: Arc(-4,0,10,65,295); Arc(4,0,10,-115,115); Line(-1,-11,-1,11,2); break;
                    default: Diamond(0,0,12,9); Cross(0,0,7); break;
                }
                if (phase == Phase.Resolve)
                    for (int i = 0; i < 4; i++)
                    {
                        Vector2 p = center + MathX.Dir(i * 90 + (int)id * 13) * (10 + t * 10);
                        c.Rect(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), frame < 3 ? 3 : 1, 2, Pal.A(front, opacity));
                    }
                result[frame] = c.ToSpriteCentered();
            }
            return result;
        }

        static Sprite PathFrame(Technique id, int frame, Color32 dark, Color32 color, Color32 light)
        {
            var strip = new PixelCanvas(PathWidth, 18);
            bool held = id is Technique.Tripwire or Technique.BindPair or Technique.Overwatch;
            int start = held ? 0 : Mathf.Max(0, frame - 1) * 6;
            int end = held || frame > 0 ? PathWidth : PathWidth * 3 / 4;
            byte opacity = held || frame < 2 ? (byte)255 : (byte)(255 - (frame - 1) * 62);
            for (int x = start; x < end; x++)
            {
                float fraction = x / (float)PathWidth;
                int y = 9;
                int half = 1;
                if (id is Technique.RecallArrow or Technique.WallShatter)
                    half = Mathf.Max(1, Mathf.RoundToInt(3 * Mathf.Sin(Mathf.PI * fraction)));
                if (id == Technique.BindPair) y += Mathf.RoundToInt(Mathf.Sin(x * .43f) * 2);
                if (id == Technique.RewindSeal) y += x % 10 / 4;
                if (id == Technique.BreakPursuit && x % 9 > 4) continue;
                strip.Rect(x, y - half, 1, half * 2 + 1, Pal.A(dark, opacity));
                strip.Set(x, y, Pal.A(id == Technique.Overwatch && x % 8 > 2 ? color : light, opacity));
                if (half > 1) strip.Set(x, y - 1, Pal.A(color, opacity));
                if (id == Technique.Tripwire) strip.Set(x, y + 1, Pal.A(x % 5 < 2 ? light : color, opacity));
            }
            return strip.ToSprite(new Vector2(0, 9));
        }
    }
}
