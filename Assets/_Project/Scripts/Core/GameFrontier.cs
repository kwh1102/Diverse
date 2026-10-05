using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Frontier rings: the world is infinite, but the Mist of Oblivion only lets proven heroes go farther.
    /// A ring opens with the hero's level AND the number of bosses the world has seen fall (shared across lives).
    /// Each ring is noticeably more dangerous (WorldGen.TierAt, elites) and has its own landmarks (obelisks, fortresses).
    /// </summary>
    public partial class Game
    {
        int lastRing = -1, lastOpenRing = -1;
        float mistWarnT;

        void UpdateFrontier(float dt)
        {
            var p = Player;
            int open = WorldGen.OpenRing(p.Level, World.bossKills);
            if (lastOpenRing >= 0 && open > lastOpenRing)
            {
                Ui.Toast($"망각의 안개가 걷혔다 — 이제 [{WorldGen.Rings[open].name}]까지 갈 수 있다.");
                World.Log($"{Ko.I(HeroName)} 증명한 덕분에 [{WorldGen.Rings[open].name}]으로 가는 안개가 걷혔다.", "world");
                Sfx.Play("levelup", 0.6f, 0.8f);
            }
            lastOpenRing = open;

            int ring = WorldGen.RingAt(p.Pos.magnitude);
            if (lastRing >= 0 && ring > lastRing)
                Ui.Toast($"[{WorldGen.Rings[ring].name}]에 들어섰다. 몬스터가 한층 강해진다.");
            lastRing = ring;

            // Mist warning when pressing against the edge
            float bound = p.Bound;
            mistWarnT -= dt;
            if (bound > 0 && p.Pos.magnitude > bound - 1.5f && mistWarnT <= 0)
            {
                mistWarnT = 6;
                Ui.Toast("짙은 망각의 안개가 길을 막는다. " + FrontierHint());
            }
            MistFx(bound);
        }

        /// <summary>What it takes to open the next ring (or a note that the world is fully open).</summary>
        public string FrontierHint()
        {
            int lvl = Player != null ? Player.Level : 1;
            int open = WorldGen.OpenRing(lvl, World.bossKills);
            if (open >= WorldGen.Rings.Length - 1) return "더 이상 세계를 가로막는 안개는 없다.";
            var next = WorldGen.Rings[open + 1];
            string need = "";
            if (lvl < next.level) need += $"레벨 {next.level}";
            if (World.bossKills < next.bosses) need += (need.Length > 0 ? " · " : "") + $"보스 처치 {next.bosses}회 (현재 {World.bossKills})";
            return $"[{next.name}]으로 가려면: {need}";
        }

        /// <summary>Drifting mist particles at the edge of the open area, in front of the hero.</summary>
        void MistFx(float bound)
        {
            var p = Player;
            if (bound <= 0 || Fx.I == null) return;
            float d = p.Pos.magnitude;
            if (d < bound - 14) return;
            if (Random.value > 0.5f) return;
            var dir = p.Pos.SafeNormal(Vector2.up);
            var side = new Vector2(-dir.y, dir.x);
            var at = dir * (bound + Random.Range(0f, 2f)) + side * (Vector2.Dot(p.Pos, side) + Random.Range(-12f, 12f));
            Fx.I.Burst(at, new Color32(220, 214, 236, 255), 1, 0.6f, 1.6f, 0.4f, 3, 360, 0, false, true);
        }
    }
}
