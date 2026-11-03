using System;
using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    public enum ExplorationKind { Hamlet, RuinedCourt, Cavern, Jungle, Gorge, Sanctuary, Quarry, Oasis, FrozenCamp, Foundry, Observatory, Wetland }
    public enum ExplorationRole { Decor, Record, Cache, Merchant, Traveler, Herb }
    public enum ExplorationShape { Rectangle, Ellipse, Diamond }
    public enum ExplorationMaterial { Fixed, Theme, Road, Liquid, Arena }

    [Serializable]
    public class ExplorationGroundDef
    {
        public Vector2 center, halfSize;
        public float cornerCut;
        public ExplorationShape shape;
        public ExplorationMaterial material;
        public Ground ground;
        public bool Contains(Vector2 p)
        {
            p -= center;
            float x = Mathf.Abs(p.x) / Mathf.Max(.1f, halfSize.x), y = Mathf.Abs(p.y) / Mathf.Max(.1f, halfSize.y);
            return shape == ExplorationShape.Ellipse ? x * x + y * y < 1 : shape == ExplorationShape.Diamond ? x + y < 1 :
                x < 1 && y < 1 && Mathf.Abs(p.x) + Mathf.Abs(p.y) < halfSize.x + halfSize.y - cornerCut;
        }
        public Ground Resolve(ThemeDef theme) => material switch
        {
            ExplorationMaterial.Theme => theme.ground, ExplorationMaterial.Road => theme.road,
            ExplorationMaterial.Liquid => theme.liquid, ExplorationMaterial.Arena => theme.arenaGround, _ => ground,
        };
    }

    [Serializable]
    public class ExplorationPropDef
    {
        public string sprite;
        public Vector2 position;
        public ExplorationRole role;
        public bool blocks = true;
        public Vector2 footprint = new Vector2(1, .6f);
    }

    [Serializable]
    public class ExplorationSiteDef
    {
        public string id, theme, displayName;
        public ExplorationKind kind;
        public Vector2 halfSize = new Vector2(14, 11);
        public float cornerCut = 3;
        [TextArea(2, 5)] public string story;
        [Tooltip("Overlaid in order; final matching shape wins. Navigation and art use these same shapes.")]
        public List<ExplorationGroundDef> grounds = new List<ExplorationGroundDef>();
        public List<ExplorationPropDef> props = new List<ExplorationPropDef>();
    }
}
