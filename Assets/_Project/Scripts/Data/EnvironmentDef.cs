using System;
using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    public enum EnvironmentMotion { Still, Foliage, Cloth, Shimmer }
    public enum EnvironmentEffect { None, Flame, Smoke, Bubble, Portal }

    [Serializable]
    public class EnvironmentObjectDef
    {
        public string sprite;
        public EnvironmentMotion motion;
        public EnvironmentEffect effect;
        public Vector2 offset;
        public float scale = 1;
        public float interval = 4;
        public bool Matches(string key) => sprite.EndsWith("*") ? key.StartsWith(sprite.Substring(0, sprite.Length - 1)) : key == sprite;
    }

    [Serializable]
    public class EnvironmentThemeDef
    {
        public float wind = 1, cloth = 1.5f, glint = .12f;
        public Color accent = new Color(.55f, .85f, .85f, 1);
        public Color dust = new Color(.65f, .75f, .55f, .3f);
        public float weatherRate = 1, weatherSize = .08f, weatherLife = 3;
        public Vector2 weatherVelocity = new Vector2(.3f, -.2f);
    }

    [Serializable]
    public class EnvironmentDef
    {
        public bool enabled;
        [Range(1, 20)] public float framesPerSecond = 8;
        public float windSpeed = 1.2f, windSpatialScale = .13f;
        public float waterStrength = .1f, liquidSpeed = .6f;
        public float tickInterval = .1f, cullMargin = 2;
        public int maxParticles = 64, emissionBudget = 2, emitterScanBudget = 24;
        public float smokeLife = 2.5f, smokeSize = .4f, smokeRise = .45f;
        public float bubbleLife = 1.2f, bubbleSize = .35f, bubbleRise = .08f;
        public float portalLife = .7f, portalRadius = .7f, portalParticleSize = .07f;
        public float liquidInterval = 6;
        public List<EnvironmentObjectDef> objects = new List<EnvironmentObjectDef>();
    }
}
