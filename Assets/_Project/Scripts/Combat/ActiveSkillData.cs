using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    public enum WeaponType { Sword, Bow, Gun, Staff, Dagger }

    [Serializable]
    public class WeaponFootprint
    {
        public WeaponType type;
        public Color outlineColor = Color.white;
        public Vector2Int[] skills = (Vector2Int[])InventoryState.Slots.Clone();
        public Vector2Int[] parts;
        public IEnumerable<Vector2Int> Cells => new[] { Vector2Int.zero }.Concat(skills).Concat(parts ?? Array.Empty<Vector2Int>());
        public bool Contains(int x, int y) => Cells.Contains(new Vector2Int(x, y));
    }

    /// <summary>AI supplies structure only. The runtime calculates every gameplay number from ActiveRules.</summary>
    [Serializable]
    public class ActiveStep
    {
        public string delivery = "arc"; // arc, line, projectile, nova, field, dash, retreat, blink, guard, trap, wall
        public string pattern = "single"; // single, fan, ring, chain, return, pierce, seek, curve, bounce, converge
        public string effect = "damage"; // damage, shield, heal, pull, push, stun, mark, block
        public string element = "None";
        public string condition = "always"; // always, marked, burning, chilled, poisoned
        public string timing = "instant"; // instant, delayed, repeat
        public string origin = "caster"; // caster, aim, previous, impact
        public string trigger = "after"; // after, hit, arrive, expire, recast
        public bool consume;
    }

    [Serializable]
    public class ActiveSkillData
    {
        public string id = Guid.NewGuid().ToString("N");
        public string name, intent, source = "local", drawback = "recovery", input = "tap";
        public string role, designReason, artStatus;
        public int iconRevision;
        public int effectRevision, effectFrames;
        public string effectSourceImage, effectSourceSignature;
        public int[] effectSourceRows;
        public int effectProcessedRevision, effectPromptRevision, effectSourcePromptRevision;
        public WeaponType weaponType;
        public int slot;
        public List<ActiveStep> steps = new List<ActiveStep>();
        // A hard dependency requires the actual companion instance, never just another skill in that slot.
        public int linkedMask;
        public string linkId;
        // An optional forward link uses an event from an earlier slot on the same weapon.
        public int linkSlot = -1;
        public string linkEvent, linkAction;
        public bool SeekingRupture => steps?.Count == 1 && steps[0].delivery == "projectile" &&
            steps[0].pattern == "seek" && steps[0].effect == "damage";
        public bool HasSynergy => !string.IsNullOrEmpty(linkEvent) && !string.IsNullOrEmpty(linkAction);
        public string iconPrompt, projectilePrompt, effectPrompt;
        public string iconImage, projectileImage;
        public string[] effectImages;
        public string color = "c9b8ff";
        public string[] visualPalette;
        public bool ArtReady => !string.IsNullOrEmpty(iconImage) && iconRevision >= 2;
        public string Signature() => input + ":" + string.Join("|", steps.Select(s => $"{s.delivery}:{s.pattern}:{s.effect}:{s.condition}:{s.timing}:{s.consume}:{s.origin}:{s.trigger}"))
            + (HasSynergy ? $"@{linkSlot}:{linkEvent}:{linkAction}" : "");
        public string Links => string.Concat(Enumerable.Range(0, 4).Where(i => (linkedMask & (1 << i)) != 0).Select(i => "QWER"[i]));
    }

    [Serializable]
    public class ActiveRules
    {
        public int maxSteps = 4, maxLinkedSlots = 2, maxTargets = 8, maxProjectiles = 5, maxPulses = 3;
        public float chargeDuration = 1.1f, chargeBonus = .5f, channelDuration = 1.5f, recastWindow = 3;
        public float curveStrength = 65, maxCastDuration = 8, anchorWidth = .6f;
        public int maxConcurrentCasts = 4, generationAttempts = 3, recentSignatures = 32;
        public bool prefetchSkills = true;
        public int skillPrefetchLeadLevels = 4;
        public int iconPixels = 32, projectilePixels = 16, effectPixels = 48, artParallel = 2;
        public float baseCooldown = 7, ultimateCooldown = 22, minCooldown = 4, maxCooldown = 35;
        public float basePower = 4.6f, ultimatePower = 10, maxDamage = 12;
        public float range = 5, meleeRange = 2.5f, radius = 2, projectileSpeed = 11;
        public float lockTime = .25f, recoveryPenalty = .3f, cooldownPenalty = 1.3f, shortRangeFactor = .55f;
        public float effectDuration = 2, statusDuration = 3, stunDuration = .65f, pulseInterval = .45f, delay = .6f;
        public float trapDuration = 15, trapArmTime = .25f, trapRecharge = 7, trapRadius = .85f, trapRootTime = .75f;
        public int trapCharges = 2, trapLimit = 3;
        public float linkWindow = 5, linkReach = 7, linkRadius = 1.4f, linkDamage = .85f, skillBuffer = .12f;
        public float seekingBurstRadius = 1.15f, seekingBurstDamage = .65f;
        public float movementDistance = 3, movementDuration = .2f, knockback = 4, shieldScale = 5, healScale = 2;
        public float utilityStepCost = .55f, conditionalBonus = .35f, patternCost = .45f, controlCost = .65f, movementCost = .45f;
        public float areaCost = .35f, rangeCost = .3f, timingCost = .35f, defenseCost = .65f;
        public float markBonus = .25f, passiveBonusCap = .65f, projectileRadius = .2f;
        public float guardInvulnerability = .25f;
        public string imageModel = "gpt-image-2.5-flare";
        public string iconQuality = "low", effectQuality = "medium";
        public int effectMinPixels = 8;
        public float effectFramesPerSecond = 20;
        public float castVisualDuration = .34f, impactVisualDuration = .22f, actionHeight = .45f, movementTrailSpacing = .35f;
        public int weaponSwingPixels = 32, impactPixels = 24;
        public float arcTrailDegrees = 105, arcThickness = .4f;
        public float effectSolidLimit = .8f;
        public float effectEdgeLimit = .08f, effectMatteTolerance = 48;
        public float effectMaxNativeCoverage = .48f, effectAnchorTolerance = .26f;
        public int visualFrames = 6, maxVisualPixels = 128, visualCacheLimit = 192, iconPaletteColors = 12;
        public float minChoiceDistance = .38f, iconBorderLimit = .08f, iconSolidLimit = .92f;
        public float inputDistanceWeight = .15f, roleDistanceWeight = .25f;
        public float iconMatteTolerance = 70, iconMatteBorderShare = .12f;
        public float controlCooldown = 1.1f, guardCooldown = 1.3f, mobilityCooldown = .85f, trapCooldown = 1.15f;
    }
}
