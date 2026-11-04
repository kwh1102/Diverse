using UnityEngine;
using UnityEngine.UI;

namespace Diverse
{
    public class WeaponRewardView : MonoBehaviour
    {
        [SerializeField] InputField prompt;
        [SerializeField] Image hero,weapon,projectile;
        [SerializeField] Text info,status;
        [SerializeField] Button generate,claim,cancel;
        [SerializeField] InventoryShapePreview inventoryPreview;
        Game g;
        DesignPack shown;
        bool initialized;
        public bool Typing=>prompt!=null && prompt.isFocused;
        public void Init(Game game)
        {
            g=game;
            UIKit.Bind(generate,()=>g.CreateRewardWeapon(prompt.text));
            UIKit.Bind(claim,()=>g.ClaimRewardWeapon());UIKit.Bind(cancel,()=>g.CloseOverlay());
        }
        public void Opened() { prompt.text="";shown=null;initialized=false; }
        void OnDisable() { if(Controls.TypingField==prompt) Controls.TypingField=null; }
        void Update()
        {
            if(g?.Player==null) return;
            if(Typing) Controls.TypingField=prompt;else if(Controls.TypingField==prompt) Controls.TypingField=null;
            UIKit.Sprite(hero,Art.Rabbit(g.Player.Costume,Pose.Idle0));inventoryPreview.Show(g.Player.MovementType);
            var pack=g.WeaponDraft;
            if(!initialized || shown!=pack)
            {
                initialized=true;
                shown=pack;
                if(pack!=null)
                {
                    var w=DesignCombat.For(pack)??DB.W(DesignRuntime.Chassis(pack));
                    UIKit.Sprite(weapon,DesignStore.Sprite(pack.weapon.image,new Vector2(.5f,.5f))??DesignArt.Weapon(pack.weapon,centered:true)??Art.Weapon(w.kind));
                    UIKit.Sprite(projectile,DesignStore.Sprite(pack.attack.projectile.image,new Vector2(.5f,.5f))??DesignArt.Projectile(pack.attack.projectile,pack.weapon));
                    UIKit.Set(info,$"<b>{w.name}</b>\n{w.desc}\n\n"+string.Join("\n",System.Linq.Enumerable.Select(System.Linq.Enumerable.Range(0,4),i=>$"{ "QWER"[i]} · {SkillDB.Get(w.skills[i])?.name}")));
                }
                else {UIKit.Sprite(weapon,null);UIKit.Sprite(projectile,null);UIKit.Set(info,"원하는 무기의 외형과 공격 방식을 말해 주세요.\n새 무기와 QWER 스킬 아이템을 함께 받아요.");}
            }
            UIKit.Set(status,g.InventoryStatus??"원하는 무기를 설명해 주세요.");
            prompt.interactable=!g.InventoryBusy;generate.interactable=!g.InventoryBusy && !string.IsNullOrWhiteSpace(prompt.text);
            claim.interactable=!g.InventoryBusy && pack!=null;cancel.interactable=!g.InventoryBusy;
            UIKit.Label(claim,g.WeaponRewardCost>0?$"이 무기 받기 · {g.WeaponRewardCost}G":"이 무기 받기");
        }
    }
}
