using System.Collections;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Warp stones: bought from the merchant like the raft, set up anywhere in the field, shown on the map.
    /// Standing near one, click another on the map to travel there instantly.
    /// The town has a stone of its own from the start, so a single placed stone is already a round trip.
    /// </summary>
    public partial class Game
    {
        public const string TownWarpId = "warp_town";
        static readonly Vector2 TownWarpPos = new Vector2(0, -9.5f);   // south edge of the plaza, clear of the well and the start point

        /// <summary>All warp stones, the town's included.</summary>
        public System.Collections.Generic.IEnumerable<WarpStone> WarpStones()
        {
            yield return new WarpStone { id = TownWarpId, name = "마을", x = TownWarpPos.x, y = TownWarpPos.y };
            foreach (var w in World.warpStones) yield return w;
        }

        /// <summary>The stone the hero is standing next to (can travel from here), or null.</summary>
        public WarpStone WarpStoneInReach()
        {
            if (Player == null) return null;
            float range = DB.Progression.warpRange;
            WarpStone best = null; float bd = range;
            foreach (var w in WarpStones())
            {
                float d = Vector2.Distance(w.Pos, Player.Pos);
                if (d <= bd) { bd = d; best = w; }
            }
            return best;
        }

        public bool CanPlaceWarpStone(out string why)
        {
            why = null;
            var p = Player;
            if (p == null || p.WarpStones <= 0) { why = "가진 워프 스톤이 없다"; return false; }
            if (p.OnWater) { why = "물 위에는 세울 수 없다"; return false; }
            if (p.Pos.magnitude < 16) { why = "마을에는 이미 워프 스톤이 있다"; return false; }
            if (WarpStones().Any(w => Vector2.Distance(w.Pos, p.Pos) < 12)) { why = "가까운 곳에 이미 워프 스톤이 있다"; return false; }
            if (Actor.Nearest(p.Pos, 8, Team.Enemy) != null) { why = "몬스터가 근처에 있으면 세울 수 없다"; return false; }
            return true;
        }

        /// <summary>Set up a carried warp stone at the hero's feet.</summary>
        public bool PlaceWarpStone()
        {
            if (!CanPlaceWarpStone(out var why)) { Ui.Toast(why); return false; }
            var p = Player;
            var pos = WorldStreamer.I.NearestWalkable(p.Pos + p.Facing.SafeNormal(Vector2.right) * 1.2f, 0.4f);
            p.WarpStones--;
            var stone = new WarpStone
            {
                id = "warp_" + System.Guid.NewGuid().ToString("N").Substring(0, 6),
                name = Diverse.World.I.Gen.RegionName(pos),
                x = pos.x, y = pos.y,
            };
            World.warpStones.Add(stone);
            Diverse.World.I.BuildWarpStone(stone);
            World.Log($"{Ko.I(HeroName)} {stone.name}에 워프 스톤을 세웠다.", "event");
            Fx.I?.Play(Art.Shockwave(Pal.Hex("8fe3ff"), 26), pos, 0, 18, 1, null, true);
            Fx.I?.Burst(pos + Vector2.up * 0.8f, Pal.Hex("8fe3ff"), 18, 4, 0.7f, -3);
            Sfx.Play("evolve", 0.5f, 1.4f);
            Ui.Toast($"워프 스톤을 세웠다: {stone.name} (지도에서 다른 워프 스톤을 눌러 이동)");
            SaveSystem.SaveWorld(World);
            return true;
        }

        /// <summary>Map click on a warp stone. Fails (with a reason) unless the hero is standing at another stone.</summary>
        public bool TryWarp(WarpStone to, out string why)
        {
            why = null;
            var from = WarpStoneInReach();
            if (from == null) { why = $"워프 스톤 근처({DB.Progression.warpRange:0}m 이내)에서만 이동할 수 있다"; return false; }
            if (from.id == to.id) { why = "이미 이 워프 스톤에 있다"; return false; }
            if (Player.Bound > 0 && to.Pos.magnitude > Player.Bound) { why = "아직 그곳까지 갈 수 없다 (안개 경계 밖)"; return false; }
            if (State == GameState.Map) CloseOverlay();
            StartCoroutine(WarpRoutine(to));
            return true;
        }

        IEnumerator WarpRoutine(WarpStone to)
        {
            var p = Player;
            p.StopMoving();
            Fx.I?.Play(Art.Shockwave(Pal.Hex("8fe3ff"), 22), p.Pos, 0, 18, 1, null, true);
            Fx.I?.Burst(p.Pos + Vector2.up * 0.5f, Pal.Hex("8fe3ff"), 20, 5, 0.5f, -4);
            Sfx.Play("dash", 0.6f, 0.5f);
            yield return null;
            var dst = to.Pos + Vector2.down * 1.2f;
            WorldStreamer.I.Warm(dst);
            p.Pos = WorldStreamer.I.NearestWalkable(dst, p.radius, 6, p.HasRaft);
            CameraRig.I?.Snap(p.Pos);
            Fx.I?.Play(Art.Shockwave(Pal.Hex("8fe3ff"), 22), p.Pos, 0, 18, 1, null, true);
            Fx.I?.Burst(p.Pos + Vector2.up * 0.5f, Pal.Hex("8fe3ff"), 20, 5, 0.5f, -4);
            Ui.Toast($"{Ko.Ro(to.name)} 이동했다");
        }
    }
}
