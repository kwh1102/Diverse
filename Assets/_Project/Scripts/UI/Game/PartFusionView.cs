using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse
{
    public class PartFusionView : MonoBehaviour
    {
        [SerializeField] PartListItem partTemplate;
        [SerializeField] Image firstIcon,secondIcon,resultIcon;
        [SerializeField] Text firstText,secondText,resultText,status;
        [SerializeField] Text firstTraits,secondTraits;
        [SerializeField] Button chooseFirst,chooseSecond,generate,confirm,cancel,back;
        readonly List<PartListItem> rows=new List<PartListItem>();
        Game g;
        string first,second;
        bool selectingFirst=true;
        int revision=-1;
        readonly HashSet<ScrollRect> resetDescriptions=new HashSet<ScrollRect>();
        public void Init(Game game)
        {
            g=game;
            UIKit.Bind(chooseFirst,()=>selectingFirst=true); UIKit.Bind(chooseSecond,()=>selectingFirst=false);
            UIKit.Bind(generate,()=>g.StartFusion(first,second));
            UIKit.Bind(confirm,()=>{g.ConfirmFusion();if(g.FusionPreview==null) {first=second=null;selectingFirst=true;} revision=-1;});
            UIKit.Bind(cancel,()=>{g.CancelFusion();first=second=null;selectingFirst=true;revision=-1;});
            UIKit.Bind(back,()=>g.CloseOverlay());
        }
        public void Opened(string selected=null)
        {
            first=g.Player.Inventory.Find(selected)?.kind==ItemKind.Part?selected:null;
            second=null; selectingFirst=first==null; revision=-1;
            g.CancelFusion();
        }
        void Update()
        {
            if(g?.Player==null) return;
            var bag=g.Player.Inventory;
            if(revision!=bag.revision)
            {
                revision=bag.revision;
                var parts=bag.items.Where(i=>i.kind==ItemKind.Part).ToList();
                UIKit.Pool(partTemplate,rows,parts.Count);
                for(int i=0;i<parts.Count;i++)
                {
                    var item=parts[i]; rows[i].Bind(item,item.id==first || item.id==second,()=>
                    {
                        if(g.InventoryBusy) return;
                        if(selectingFirst) {first=item.id;selectingFirst=false;} else second=item.id;
                        g.StartFusion(first,second);
                        revision=-1;
                    });
                }
            }
            var a=bag.Find(first);var b=bag.Find(second);var result=g.FusionPreview;
            UIKit.Sprite(firstIcon,PartArt.For(a));UIKit.Sprite(secondIcon,PartArt.For(b));UIKit.Sprite(resultIcon,PartArt.For(result));
            UIKit.Set(firstText,a==null?"재료 A를 선택해 주세요":$"{a.name}\n등급 T{a.complexity}");
            UIKit.Set(secondText,b==null?"재료 B를 선택해 주세요":$"{b.name}\n등급 T{b.complexity}");
            SetDescription(firstTraits,a?.Ability?.Explain()??"선택한 재료의 고유 능력이 표시됩니다.");
            SetDescription(secondTraits,b?.Ability?.Explain()??"선택한 재료의 고유 능력이 표시됩니다.");
            SetDescription(resultText,result==null?"같은 등급의 모든 파츠를 합성할 수 있어요.\n두 재료의 능력을 계승하고 등급이 1 올라요.\nT5는 최종 등급이에요.\n\n확정할 때만 재료와 골드가 소모돼요.":InventoryView.Description(result));
            string why=g.FusionUnavailableReason(first,second);
            int cost=a==null?0:InventoryState.Rules.FusionCost(a.complexity);
            UIKit.Set(status,(g.InventoryStatus??why??"합성 가능 · 확정할 때만 재료와 골드가 소모됩니다.")+$"\n보유 {g.Player.Gold}G"+(result!=null?$" / 필요 {cost}G":""));
            bool editable=!g.InventoryBusy;
            foreach(var row in rows) row.SetInteractable(editable);
            chooseFirst.interactable=chooseSecond.interactable=editable;
            UIKit.Label(chooseFirst,selectingFirst?"A 선택 중":"재료 A 바꾸기");UIKit.Label(chooseSecond,selectingFirst?"재료 B 바꾸기":"B 선택 중");
            UIKit.Show(generate,false);
            confirm.interactable=result!=null && why==null;
            UIKit.Label(confirm,$"합성 확정 · {cost}G");
            UIKit.Show(confirm,result!=null);UIKit.Show(cancel,result!=null);back.interactable=!g.InventoryBusy;
        }
        void SetDescription(Text target,string value)
        {
            if(target==null || target.text==value) return;
            UIKit.Set(target,value);
            var scroll=target.GetComponentInParent<ScrollRect>();
            if(scroll!=null) resetDescriptions.Add(scroll);
        }
        void LateUpdate()
        {
            foreach(var scroll in resetDescriptions)
            {
                if(scroll==null || scroll.content==null) continue;
                LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
                scroll.StopMovement();scroll.verticalNormalizedPosition=1;
            }
            resetDescriptions.Clear();
        }
    }
}
