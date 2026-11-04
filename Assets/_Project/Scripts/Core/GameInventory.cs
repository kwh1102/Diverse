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
        readonly System.Collections.Generic.HashSet<string> visualAttempts=new System.Collections.Generic.HashSet<string>();
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

        public void RequestPartVisuals(string selected=null)
        {
            if(PartVisualBusy || !AiClient.Enabled || Player==null) return;
            var parts=Player.Inventory.items.Where(i=>i.kind==ItemKind.Part && i.Ability!=null && !PartPresentation.Ready(i.Ability)
                && (selected==null?!visualAttempts.Contains(i.id):i.id==selected)).ToList();
            if(parts.Count>0) StartCoroutine(StoredVisuals(parts));
        }
        IEnumerator StoredVisuals(System.Collections.Generic.List<InventoryItem> parts)
        {
            var owner=Player;PartVisualBusy=true;
            foreach(var item in parts) visualAttempts.Add(item.id);
            void Apply()
            {
                if(Player!=owner || owner==null) return;
                foreach(var item in parts.Where(i=>owner.Inventory.Find(i.id)==i))
                {
                    item.part=AbilityRecord.From(item.Ability);PartArt.Describe(item);
                }
                owner.Inventory.Notify();
            }
            try
            {
                yield return PartPresentation.Prepare(this,parts.Select(i=>i.Ability).ToList(),s=>Apply(),
                    owner.Inventory.items.Except(parts).Where(i=>i.kind==ItemKind.Part).Select(i=>i.name));
                Apply(); if(Player==owner) SaveRun();
            }
            finally { PartVisualBusy=false; }
        }

        public void StartFusion(string a, string b)
        {
            if (InventoryBusy || FusionPreview != null) return;
            var first = Player.Inventory.Find(a); var second = Player.Inventory.Find(b);
            if (first?.kind != ItemKind.Part || second?.kind != ItemKind.Part || first == second || first.complexity != second.complexity)
            { InventoryStatus = "같은 복잡도의 파츠 두 개를 골라 줘."; return; }
            if (first.complexity >= InventoryState.Rules.maxPartTier) { InventoryStatus = "최고 복잡도에 도달한 파츠야."; return; }
            if (Player.Gold < InventoryState.Rules.FusionCost(first.complexity)) { InventoryStatus = "합성할 골드가 부족해."; return; }
            if (!AiClient.Enabled) { InventoryStatus = "AI 합성을 사용하려면 설정에서 AI 연결을 켜 줘. 재료와 골드는 보존돼."; return; }
            fusionA = a; fusionB = b;
            StartCoroutine(FusionRoutine(first, second));
        }

        IEnumerator FusionRoutine(InventoryItem first, InventoryItem second)
        {
            InventoryBusy = true; InventoryStatus = "두 파츠를 엮는 중…";
            var owner = Player;
            var ctx = GenerationContext.Build(owner, owner.Telemetry, rnd);
            // Consumed materials must not count as duplicate candidates against their own result.
            ctx.build.owned.RemoveAll(a => a == first.Ability || a == second.Ability);
            ctx.owned.RemoveAll(a => a == first.Ability || a == second.Ability);
            AbilityDef result = null;
            yield return AiClient.Fuse(ctx, first.Ability, second.Ability, a => result = a);
            InventoryBusy = false;
            if (owner != Player || !owner.Alive) yield break;
            if (result == null) { InventoryStatus = "합성안을 만들지 못했어. 재료와 골드는 보존돼. " + AiClient.LastStatus; yield break; }
            var validation = RuleValidator.Validate(result, ctx.build);
            if (!validation.ok || !Reflects(result,first.Ability) || !Reflects(result,second.Ability))
            { InventoryStatus = "두 재료를 반영하면서 세계 규칙에 맞는 합성안이 필요해. 재료와 골드는 보존돼."; yield break; }
            InventoryBusy=true;
            yield return PartPresentation.Prepare(this,new[]{result},s=>InventoryStatus=s,owner.Inventory.items.Where(i=>i.kind==ItemKind.Part).Select(i=>i.name));
            InventoryBusy=false;
            if(owner!=Player || !owner.Alive) yield break;
            FusionPreview = InventoryItem.Part(result);
            FusionPreview.complexity = first.complexity + 1;
            FusionPreview.acquisitionApplied = first.acquisitionApplied || second.acquisitionApplied;
            FusionPreview.ancestor = string.Join(" → ", new[] { first.ancestor, second.ancestor }.Where(s => !string.IsNullOrEmpty(s)).Distinct());
            InventoryStatus = "결과를 확인하고 합성을 확정해 줘. 취소하면 재료와 골드는 그대로야.";
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

        static bool Reflects(AbilityDef result,AbilityDef material) => material.rules.SelectMany(r=>r.ops).Any(m=>
            result.rules.SelectMany(r=>r.ops).Any(o=>o.op==m.op && (string.IsNullOrEmpty(m.element)||o.element==m.element) && (string.IsNullOrEmpty(m.stat)||o.stat==m.stat)));

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
            // New weapons come with skill items; slots still determine which weapon uses them.
            for (int slot=0;slot<4;slot++)
            {
                var skill = SkillDB.Get(w.skills[slot]);
                if (skill == null) continue;
                item.learnedSkillMask |= 1 << slot;
                var s = new InventoryItem { kind = ItemKind.Skill, skillId = skill.id, skillSlot = slot, sourceWeapon = item.id,
                    name = result.weapon.skillNames != null && slot < result.weapon.skillNames.Length ? result.weapon.skillNames[slot] : skill.name };
                owner.Inventory.items.Add(s);
                var d = InventoryState.Slots[slot];
                if (!item.Placed || !owner.Inventory.Move(s.id, item.x+d.x, item.y+d.y, out _)) owner.Inventory.PlaceFirst(s);
            }
            owner.Gold -= cost; World.SetFlag("weapon:" + key);
            World.Log($"{HeroName}: [{item.name}] 무기를 발견했다.", "deed");
            WeaponRewardKey = null; WeaponRewardCost = 0;
            WeaponDraft=null;
            InventoryStatus = item.Placed ? "새 무기와 QWER 스킬을 얻었어. 무기를 선택해서 사용할 수 있어." : "새 무기는 수령 대기 중이야. 3×3 공간을 확보해 배치해 줘.";
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
