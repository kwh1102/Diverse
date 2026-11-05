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
        [SerializeField] Button chooseFirst,chooseSecond,generate,confirm,cancel,back;
        readonly List<PartListItem> rows=new List<PartListItem>();
        Game g;
        string first,second;
        bool selectingFirst=true;
        int revision=-1;
        public void Init(Game game)
        {
            g=game;
            UIKit.Bind(chooseFirst,()=>selectingFirst=true); UIKit.Bind(chooseSecond,()=>selectingFirst=false);
            UIKit.Bind(generate,()=>g.StartFusion(first,second));
            UIKit.Bind(confirm,()=>{g.ConfirmFusion();first=second=null;revision=-1;});
            UIKit.Bind(cancel,()=>g.CancelFusion());
            UIKit.Bind(back,()=>g.CloseOverlay());
        }
        public void Opened(string selected=null)
        {
            first=g.Player.Inventory.Find(selected)?.kind==ItemKind.Part?selected:null;
            second=null; selectingFirst=first==null; revision=-1;
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
                        if(g.InventoryBusy || g.FusionPreview!=null) return;
                        if(selectingFirst) {first=item.id;selectingFirst=false;} else second=item.id;
                        revision=-1;
                    });
                }
            }
            var a=bag.Find(first);var b=bag.Find(second);var result=g.FusionPreview;
            UIKit.Sprite(firstIcon,PartArt.For(a));UIKit.Sprite(secondIcon,PartArt.For(b));UIKit.Sprite(resultIcon,PartArt.For(result));
            UIKit.Set(firstText,a==null?"재료 A를 선택해 주세요":$"{a.name}\n등급 T{a.complexity}");
            UIKit.Set(secondText,b==null?"재료 B를 선택해 주세요":$"{b.name}\n등급 T{b.complexity}");
            UIKit.Set(resultText,result==null?"같은 종류·같은 등급의 파츠 두 개로\n해당 파츠의 등급을 올려요.\n\n결과를 확인한 뒤 확정하면\n재료와 골드가 소모돼요.":InventoryView.Description(result));
            UIKit.Set(status,g.InventoryStatus??"재료 A와 B를 고른 뒤 합성안을 요청해 주세요.");
            bool editable=!g.InventoryBusy && result==null;
            foreach(var row in rows) row.SetInteractable(editable);
            chooseFirst.interactable=chooseSecond.interactable=editable;
            UIKit.Label(chooseFirst,selectingFirst?"A 선택 중":"재료 A 바꾸기");UIKit.Label(chooseSecond,selectingFirst?"재료 B 바꾸기":"B 선택 중");
            generate.interactable=editable && a!=null && b!=null && a!=b && a.Ability.catalogId==b.Ability.catalogId && a.complexity==b.complexity && a.complexity<InventoryState.Rules.maxPartTier && g.Player.Gold>=InventoryState.Rules.FusionCost(a.complexity);
            UIKit.Label(generate,a==null?"합성안 만들기":$"합성안 만들기 · {InventoryState.Rules.FusionCost(a.complexity)}G");
            UIKit.Show(confirm,result!=null);UIKit.Show(cancel,result!=null);back.interactable=!g.InventoryBusy;
        }
    }
}
