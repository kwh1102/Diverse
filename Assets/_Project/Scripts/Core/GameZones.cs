using System.Collections;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// The goal of a normal-mode world: beat a pure zone boss → its gate opens to that zone's deep layer →
    /// beat the deep layer's master → this life's journey is complete (a statue, back to character select).
    /// Dying in a deep layer sends the next life back to the five-zone surface.
    /// </summary>
    public partial class Game
    {
        /// <summary>A camp / boss site was cleared (World.OnCampEnemyKilled → OnCampCleared).</summary>
        void OnZoneSiteCleared(StructureSpec spec)
        {
            if (spec == null || spec.kind != StructKind.ZoneBoss || spec.zone == null) return;
            var z = spec.zone;
            if (Diverse.World.I.Gen.Deep)
            {
                StartCoroutine(JourneyEndRoutine(z));
                return;
            }
            World.Log($"{Ko.I(HeroName)} {Ko.Eul(z.name + "의 주인")} 쓰러뜨렸다. {z.deepName}로 가는 문이 열렸다.", "deed");
            Ui.Toast($"{z.name}의 주인을 쓰러뜨렸다! [{z.deepName}]의 문이 열렸다.");
            Diverse.World.I.BuildZoneGate(spec);
        }

        /// <summary>Go down through a cleared zone boss's gate.</summary>
        public void EnterDeepLayer(ZoneDef z)
        {
            World.layer = 1;
            World.deepZone = z.style.ToString();
            // Every descent is a fresh abyss: what earlier lives cleared down there (camps, chests, its master) comes back
            string pre = $"L{ZoneMap.LayerKey(World)}:";
            World.clearedCamps.RemoveAll(id => id.StartsWith(pre));
            World.discovered.RemoveAll(id => id.StartsWith(pre));
            World.flags.RemoveAll(f => f.Contains(":" + pre));
            World.Log($"{Ko.I(HeroName)} {z.deepName}로 내려갔다.", "world");
            SwitchLayer($"[{z.deepName}] — 이곳의 주인을 쓰러뜨리면 여정이 끝난다.");
        }

        public void LeaveDeepLayer()
        {
            World.layer = 0;
            World.deepZone = null;
            SwitchLayer("지상으로 돌아왔다.");
        }

        /// <summary>Swap the whole world for another layer and put the hero at its spawn.</summary>
        void SwitchLayer(string toast)
        {
            var p = Player;
            p.StopMoving();
            Fx.I?.Play(Art.Shockwave(Pal.Hex("c9b8ff"), 30), p.Pos, 0, 18, 1, null, true);
            Diverse.World.I.ResetForRun();
            Diverse.World.I.Init(World.seed);
            Fx.I?.ClearAll();
            Boss = null;
            lastRing = lastOpenRing = -1;
            var start = new Vector2(0, -1.5f);
            WorldStreamer.I.Warm(start);
            p.Pos = WorldStreamer.I.NearestWalkable(start, p.radius);
            CameraRig.I?.Snap(p.Pos);
            Fx.I?.Burst(p.Pos + Vector2.up * 0.5f, Pal.Hex("c9b8ff"), 24, 5, 0.6f, -4);
            Sfx.Play("boss_roar", 0.5f, 1.3f);
            Ui.Toast(toast);
            SaveSystem.SaveWorld(World);
            SaveRun();
        }

        IEnumerator JourneyEndRoutine(ZoneDef z)
        {
            yield return new WaitForSecondsRealtime(2.2f);
            if (Player == null || !Player.Alive) yield break;
            if (!World.conquered.Contains(z.style.ToString())) World.conquered.Add(z.style.ToString());
            World.Log($"{Ko.I(HeroName)} {z.deepName}의 주인을 쓰러뜨리고 여정을 마쳤다.", "deed");
            SaveSystem.SaveWorld(World);
            Dialogue.ShowJourneyEnd(this, z);
        }

        /// <summary>Victory: the hero retires as a statue in the plaza and the next life begins at character select.</summary>
        public void EndJourney(ZoneDef z)
        {
            var p = Player;
            State = GameState.Dead;
            var at = MathX.Dir(World.lifeCount * 47f) * 19f;
            var grave = new GraveRecord
            {
                life = World.lifeCount, heroName = HeroName, costume = p.Costume.id, weapon = (int)p.Weapon.kind, level = p.Level, kills = p.Kills,
                x = at.x, y = at.y, layer = 0, cause = z.deepName + " 정복", statue = true,
                epitaph = $"{HeroName} — {z.deepName}의 주인을 쓰러뜨리고 여정을 마쳤다. 레벨 {p.Level}, 처치한 몬스터 {p.Kills}마리.",
            };
            foreach (var g in p.Abilities.Owned.Where(a => a.tier >= 1).OrderByDescending(a => a.power).Take(2)) grave.abilities.Add(AbilityRecord.From(g));
            World.graves.Add(grave);
            World.gold += p.Gold;
            World.layer = 0;
            World.deepZone = null;
            Session.PendingRun = null;
            SaveSystem.DeleteRun();
            SaveSystem.SaveWorld(World);
            GameTime.ClearPauses();
            Session.ToMenu(true);
        }

        float zoneAmbientT;

        /// <summary>The zone's drawn motes drift over the view (embers in the volcano, snow on the glacier, spores in the bog…).</summary>
        void TickZoneAmbient(float dt)
        {
            var zones = Diverse.World.I != null ? Diverse.World.I.Gen?.Zones : null;
            if (zones == null || Fx.I == null || Player.Pos.magnitude < DB.ZoneRules.centerRadius) return;
            zoneAmbientT -= dt;
            if (zoneAmbientT > 0) return;
            zoneAmbientT = 0.16f / GraphicsQuality.EffectScale;
            var view = CameraRig.I != null ? CameraRig.I.ViewRect() : new Rect(Player.Pos.x - 12, Player.Pos.y - 7, 24, 15);
            var at = new Vector2(Random.Range(view.xMin, view.xMax), Random.Range(view.yMin, view.yMax));
            var z = zones.ZoneAt(at, Random.value);
            var mote = Art.HandSprite(ZoneArtDef.FxKey(z.style, "mote"));
            if (mote == null) return;
            Vector2 drift = z.art.ambient switch
            {
                "snow" => new Vector2(0.3f, -0.8f), "embers" => new Vector2(0.15f, 0.9f), "spores" => new Vector2(0.1f, 0.35f),
                "leaves" => new Vector2(0.7f, -0.5f), "sparkles" => new Vector2(0, 0.15f), "pollen" => new Vector2(0.35f, 0.2f),
                "mist" => new Vector2(0.4f, 0f), _ => new Vector2(1.3f, 0.05f),
            };
            float s = Random.Range(0.5f, 0.9f);
            Fx.I.Pop(mote, at, Random.Range(2.2f, 3.4f), s, s, 0, z.art.ambient is "embers" or "sparkles", drift * Random.Range(0.7f, 1.3f), new Color(1, 1, 1, 0.8f));
        }

        /// <summary>A new life always starts on the surface (a hero who died below doesn't pull the next one down).</summary>
        void EnsureSurface()
        {
            if (World.layer == 0) return;
            World.layer = 0;
            World.deepZone = null;
            Diverse.World.I.Init(World.seed);
        }
    }
}
