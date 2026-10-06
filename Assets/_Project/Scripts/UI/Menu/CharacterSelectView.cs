using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Diverse
{
    /// <summary>Character select (MainMenu scene): costume + starting weapon, then load the Game scene.</summary>
    public class CharacterSelectView : MonoBehaviour
    {
        [SerializeField] Image preview, previewWeapon;
        [SerializeField] Button costumeTemplate;     // one slot per costume, cloned at runtime
        [SerializeField] Button weaponTemplate;      // one button per weapon
        [SerializeField] Text costumeInfo, weaponDesc;
        [SerializeField] Text[] skillLines = new Text[4];
        [SerializeField] Button startButton, backButton;
        [SerializeField] Color selectedTint = new Color(1f, 0.95f, 0.7f);

        readonly List<Button> costumes = new List<Button>();
        readonly List<Button> weapons = new List<Button>();
        int selCostume, selWeapon;
        MenuUI menu;

        public void Init(MenuUI m)
        {
            menu = m;
            UIKit.Pool(costumeTemplate, costumes, DB.Costumes.Count);
            for (int i = 0; i < costumes.Count; i++)
            {
                int k = i;
                costumes[i].onClick.RemoveAllListeners();
                costumes[i].onClick.AddListener(() => { selCostume = k; Sfx.Play("ui"); });
                var icon = costumes[i].transform.Find("Icon")?.GetComponent<Image>();
                UIKit.Sprite(icon, Art.Rabbit(DB.Costumes[i], Pose.Idle0));
            }
            int nw = System.Enum.GetValues(typeof(WeaponKind)).Length;
            UIKit.Pool(weaponTemplate, weapons, nw);
            for (int i = 0; i < weapons.Count; i++)
            {
                int k = i;
                weapons[i].onClick.RemoveAllListeners();
                weapons[i].onClick.AddListener(() => { selWeapon = k; Sfx.Play("ui"); });
                UIKit.Label(weapons[i], DB.W((WeaponKind)i).name);
            }
            UIKit.Bind(startButton, Begin);
            UIKit.Bind(backButton, () => menu.Show(MenuUI.Screen.Title));
        }

        void OnEnable()
        {
            var w = Session.World;
            if (w == null) return;
            selCostume = Mathf.Max(0, DB.Costumes.FindIndex(c => c.id == w.lastCostume));
            selWeapon = Mathf.Clamp(w.lastWeapon, 0, System.Enum.GetValues(typeof(WeaponKind)).Length - 1);
        }

        void Begin() => Session.NewLife(DB.Costumes[selCostume], (WeaponKind)selWeapon);

        void Update()
        {
            if (Session.World == null) return;
            var c = DB.Costumes[selCostume];
            var w = DB.W((WeaponKind)selWeapon);
            float t = Time.unscaledTime;
            var run = (Pose)((int)Pose.Run0 + (int)(t * 8) % 4);
            UIKit.Sprite(preview, Art.Rabbit(c, (int)(t * 0.5f) % 2 == 0 ? run : Pose.Idle0));
            UIKit.Sprite(previewWeapon, Art.Weapon(w.kind));

            for (int i = 0; i < costumes.Count; i++) costumes[i].image.color = i == selCostume ? selectedTint : Color.white;
            for (int i = 0; i < weapons.Count; i++) weapons[i].image.color = i == selWeapon ? selectedTint : Color.white;

            UIKit.Set(costumeInfo, $"<b>{c.name}</b>  <color=#fff2b3>{c.perk}</color>");
            UIKit.Set(weaponDesc, w.desc);
            string[] keys = { "Q", "W", "E", "R" };
            for (int i = 0; i < skillLines.Length && i < w.skills.Length; i++)
                UIKit.Set(skillLines[i], $"<color=#c9b8ff>{keys[i]}</color> {SkillDB.Get(w.skills[i]).name}");

            if (Controls.KeyDown(Key.Enter)) Begin();
        }
    }
}
