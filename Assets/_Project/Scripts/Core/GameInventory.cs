using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    public partial class Game
    {
        public bool InventoryBusy { get; private set; }
        public string InventoryStatus { get; private set; }
        public InventoryItem FusionPreview { get; private set; }
        string fusionA, fusionB;
        public string WeaponRewardKey { get; private set; }
        public int WeaponRewardCost { get; private set; }
        public DesignPack WeaponDraft { get; private set; }
        public bool PartVisualBusy { get; private set; }
        string inventoryBuild;
        public bool CanDiscoverWeapon => Player != null && Player.Level >= InventoryState.Rules.weaponUnlockLevel && World.bossKills >= InventoryState.Rules.weaponUnlockBosses;
        public bool HasWeaponReward => !string.IsNullOrEmpty(WeaponRewardKey);
        public string WeaponUnlockHint => $"레벨 {InventoryState.Rules.weaponUnlockLevel} · 보스 처치 {InventoryState.Rules.weaponUnlockBosses}회 이후";

        public void InventoryChanged()
        {
            string build=Player.Inventory.activeWeapon+"|"+string.Join("|",Player.Abilities.Owned.Select(a=>a.id).OrderBy(id=>id))+"|"+string.Join("|",Player.Skills.Select(s=>s.def?.id));
            if(build!=inventoryBuild) { inventoryBuild=build;ResetPrefetch();RequestPrefetch(); }
            SaveRun();
        }

        public void OpenFusion(string selected=null)
        {
            if(InventoryBusy) return;
            InventoryStatus=null;OpenOverlay(GameState.PartFusion,selected);
        }

        public void RequestPartVisuals(string selected = null)
        {
            var item = Player?.Inventory.Find(selected);
            if (item?.active != null && !item.generatingActive && !item.active.ArtReady && AiClient.Enabled && !InventoryBusy)
                StartCoroutine(RedrawActive(item));
        }
        IEnumerator RedrawActive(InventoryItem item)
        {
            var owner = Player; InventoryBusy = true; PartVisualBusy = true;
            try
            {
                yield return ActiveSkillArt.Prepare(item.active, s => InventoryStatus = s);
                if (Player == owner) { owner.Inventory.Notify(); SaveRun(); }
            }
            finally { InventoryBusy = false; PartVisualBusy = false; }
        }
        public void StartFusion(string a, string b)
        {
            if (InventoryBusy || FusionPreview != null) return;
            var first = Player.Inventory.Find(a); var second = Player.Inventory.Find(b);
            if (first?.kind != ItemKind.Part || second?.kind != ItemKind.Part || first == second || first.complexity != second.complexity || first.Ability.catalogId != second.Ability.catalogId)
            { InventoryStatus = "같은 종류·같은 등급의 파츠 두 개를 골라 주세요."; return; }
            if (first.complexity >= InventoryState.Rules.maxPartTier) { InventoryStatus = "최고 등급의 파츠입니다."; return; }
            if (Player.Gold < InventoryState.Rules.FusionCost(first.complexity)) { InventoryStatus = "골드가 부족합니다."; return; }
            fusionA = a; fusionB = b;
            FusionPreview = InventoryItem.Part(PartCatalog.Create(first.Ability.catalogId, first.complexity + 1));
            FusionPreview.ancestor = first.ancestor ?? second.ancestor;
            InventoryStatus = "같은 파츠의 등급을 올립니다. 결과를 확인하고 확정해 주세요.";
        }

        public void ConfirmFusion()
        {
            if (InventoryBusy || FusionPreview == null) return;
            var a = Player.Inventory.Find(fusionA); var b = Player.Inventory.Find(fusionB);
            if (a == null || b == null || a.complexity != b.complexity) { CancelFusion(); return; }
            int cost = InventoryState.Rules.FusionCost(a.complexity);
            if (Player.Gold < cost) { InventoryStatus = "골드가 부족해."; return; }
            var result = FusionPreview;
            if (!Player.Inventory.Fuse(fusionA, fusionB, result, out string why)) { InventoryStatus = why; return; }
            Player.Gold -= cost;
            World.Log($"{HeroName}: [{a.name}]과 [{b.name}]을 합쳐 [{result.name}]을 만들었다.", "evolve");
            FusionPreview = null; InventoryStatus = "합성이 완료됐어.";
            InventoryChanged(); SaveSystem.SaveWorld(World);
        }

        public void CancelFusion() { if (InventoryBusy) return; FusionPreview = null; InventoryStatus = null; }

        public void OpenWeaponReward(string key, int cost = 0)
        {
            if (InventoryBusy || !CanDiscoverWeapon || World.Flag("weapon:" + key) || Player.Gold < cost) return;
            WeaponRewardKey = key; WeaponRewardCost = cost; WeaponDraft=null;InventoryStatus = "발견한 무기의 모습을 말해 줘.";
            Dialogue.Current = null;
            OpenOverlay(GameState.WeaponDesign);
        }

        public void CreateRewardWeapon(string prompt)
        {
            if (Player.Inventory.items.Count(i=>i.kind==ItemKind.Weapon) >= InventoryState.Rules.maxWeapons)
            { InventoryStatus="무기 보관 한도에 도달했어. 기존 무기를 버리고 다시 시도해 줘."; return; }
            if (InventoryBusy || !HasWeaponReward || !CanDiscoverWeapon || Player.Gold < WeaponRewardCost || string.IsNullOrWhiteSpace(prompt)) return;
            StartCoroutine(WeaponRewardRoutine(prompt.Trim().Substring(0, Math.Min(prompt.Trim().Length, DesignGuard.MaxPrompt))));
        }

        IEnumerator WeaponRewardRoutine(string prompt)
        {
            InventoryBusy = true;
            var owner = Player; string key = WeaponRewardKey; int cost = WeaponRewardCost;
            var seed = (DesignStore.PackOfCostume(owner.Costume.id) ?? DesignPack.Default()).Clone();
            seed.travelSelected = true; seed.travelType = owner.MovementType;
            DesignPack result = null;
            if (DesignAi.Available)
            {
                yield return DesignAi.Generate(this, seed, "무기만 디자인해 줘: " + prompt, false, false,
                    s => InventoryStatus = s, (p, s) => { result = p; InventoryStatus = s; },weaponOnly:true);
                World.designCalls++; World.designCostUsd+=DesignAi.LastCostUsd;
            }
            if (result == null)
            {
                seed.weapon.image=null; seed.attack.projectile.image=null; seed.attack.effectImages=null;
                result=DesignLocal.Generate(seed, "무기 " + prompt, false, false);
            }
            InventoryBusy = false;
            if (owner != Player || !owner.Alive || World.Flag("weapon:" + key) || owner.Gold < cost) yield break;
            WeaponDraft=result;
            InventoryStatus="무기 미리보기를 확인해 줘. 다시 요청하거나 이 무기를 받을 수 있어.";
        }

        public void CancelWeaponReward() { if(InventoryBusy) return;WeaponRewardKey=null;WeaponDraft=null;WeaponRewardCost=0; }

        public void ClaimRewardWeapon()
        {
            if(InventoryBusy || WeaponDraft==null || !HasWeaponReward || Player==null || Player.Gold<WeaponRewardCost || World.Flag("weapon:"+WeaponRewardKey)) return;
            var owner=Player;var result=WeaponDraft;int cost=WeaponRewardCost;string key=WeaponRewardKey;
            if(owner.Inventory.items.Count(i=>i.kind==ItemKind.Weapon)>=InventoryState.Rules.maxWeapons) {InventoryStatus="무기 보관 한도에 도달했어. 기존 무기를 정리한 뒤 다시 찾아와 줘.";return;}
            var w = (DesignCombat.For(result) ?? DB.W(DesignRuntime.Chassis(result))).Clone();
            var item = new InventoryItem { kind = ItemKind.Weapon, weapon = w, name = w.name, design = result };
            owner.Inventory.Receive(item);
            // Skill generation is a separate acquisition event. Weapon art contains only basic attacks.
            owner.QueueWeaponSkills(item, new[] { 0, 1, 2, 3 });
            owner.Gold -= cost; World.SetFlag("weapon:" + key);
            World.Log($"{HeroName}: [{item.name}] 무기를 발견했다.", "deed");
            WeaponRewardKey = null; WeaponRewardCost = 0;
            WeaponDraft=null;
            InventoryStatus = item.Placed ? "새 무기를 얻었어. QWER 전용 능력과 이미지를 생성하는 중이야." : "새 무기는 수령 대기 중이야. 무기 유형에 맞는 주변 공간을 확보해 배치해 줘.";
            InventoryChanged(); SaveSystem.SaveWorld(World);
            OpenOverlay(GameState.Inventory);
        }

        public void SelectLegacy(GraveRecord grave, string id)
        {
            var part = grave.parts.FirstOrDefault(i => i.id == id);
            if (part == null || part.complexity > InventoryState.Rules.LegacyTier(World)) return;
            var copy = part.Copy(); copy.x = copy.y = -1;
            copy.ancestor = string.IsNullOrEmpty(copy.ancestor) ? grave.heroName : copy.ancestor + " → " + grave.heroName;
            World.legacyPart = copy;
            // A selected legacy cannot also be collected from the same grave.
            grave.recovered = true;
            SaveSystem.SaveWorld(World);
        }

        public bool TryStrikeRelic(Vector2 origin,Vector2 direction,float range,float arc)
        {
            if (!CanDiscoverWeapon || State!=GameState.Playing) return false;
            foreach (var relic in Interactable.All)
            {
                if (relic == null || relic.kind!="weapon_relic" || World.Flag("weapon:"+relic.id)) continue;
                Vector2 delta=relic.Pos-origin;
                if (delta.magnitude>range || arc<359 && Vector2.Angle(direction,delta)>arc*.5f) continue;
                OpenWeaponReward(relic.id); return true;
            }
            return false;
        }
    }
}
