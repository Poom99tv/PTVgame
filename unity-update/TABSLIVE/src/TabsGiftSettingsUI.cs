using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public sealed class TabsGiftSettingsUI : MonoBehaviour
{
    Canvas canvas;GameObject settings,picker;Transform cells;InputField search;Text pageLabel;
    readonly List<GameObject> tiles=new List<GameObject>();readonly List<Image> previews=new List<Image>();readonly List<Text> labels=new List<Text>();
    int selected,page;bool building;
    Color dark=new Color(.055f,.075f,.12f,.99f),green=new Color(.08f,.42f,.33f),blue=new Color(.12f,.32f,.67f);
    public bool Visible {get{return canvas&&canvas.gameObject.activeSelf;}}
    public void Open(){if(!canvas)Build();GetComponent<TabsArenaUI>().SetVisible(false);canvas.gameObject.SetActive(true);Refresh();}
    void Close(){Save();canvas.gameObject.SetActive(false);GetComponent<TabsArenaUI>().SetVisible(true);}
    void Save(){File.WriteAllText(TabsArena.ConfigPath,JsonConvert.SerializeObject(TabsArena.I.Settings,Formatting.Indented));}
    void Build(){
        canvas=ArenaUI.Canvas("TikTok Gift Settings",300);canvas.gameObject.AddComponent<GraphicRaycaster>();
        var panel=ArenaUI.Image(canvas.transform,Vector2.zero,new Vector2(1320,820),dark);panel.raycastTarget=true;settings=panel.gameObject;
        ArenaUI.Text(panel.transform,"ตั้งค่าฮีโร่และทริกเกอร์",new Vector2(0,365),new Vector2(1200,50),32,Color.white);
        ArenaUI.Text(panel.transform,"คลิกรูปเพื่อเลือกของขวัญ · จับคู่ด้วย ID / รูป / ชื่อ · ทุกทริกเกอร์ใช้ฝั่งที่ผู้ชมพิมพ์ 1 หรือ 2",new Vector2(0,323),new Vector2(1240,34),18,new Color(.65f,.78f,.9f));
        string[] headers={"ของขวัญ","กดใจ","ติดตาม","Heart Me","ใจครบ"};float[] xs={90,230,370,510,-20};
        ArenaUI.Text(panel.transform,"ฮีโร่ / ของขวัญที่เลือก",new Vector2(-390,278),new Vector2(400,30),19,Color.white);
        for(int h=0;h<headers.Length;h++)ArenaUI.Text(panel.transform,headers[h],new Vector2(xs[h],278),new Vector2(h==3?130:100,30),18,Color.white);
        building=true;
        for(int i=0;i<7;i++){
            int slot=i;float y=215-i*64;var rule=TabsArena.I.Settings.giftRules[i];
            var background=ArenaUI.Image(panel.transform,new Vector2(0,y),new Vector2(1240,58),new Color(.94f,.81f,.59f));
            var pick=ArenaUI.Button(background.transform,"",new Vector2(-567,0),new Vector2(42,40),blue,()=>OpenPicker(slot));
            var icon=ArenaUI.Image(pick.transform,Vector2.zero,new Vector2(36,36),Color.white);icon.preserveAspect=true;previews.Add(icon);
            var title=ArenaUI.Text(background.transform,"",new Vector2(-333,0),new Vector2(394,40),17,new Color(.18f,.08f,.025f));title.alignment=TextAnchor.MiddleLeft;labels.Add(title);
            MakeToggle(background.transform,new Vector2(90,0),rule.triggerGift,v=>{TabsArena.I.Settings.giftRules[slot].triggerGift=v;Save();});
            MakeToggle(background.transform,new Vector2(230,0),rule.triggerLikes,v=>{TabsArena.I.Settings.giftRules[slot].triggerLikes=v;Save();});
            MakeToggle(background.transform,new Vector2(370,0),rule.triggerFollow,v=>{TabsArena.I.Settings.giftRules[slot].triggerFollow=v;Save();});
            MakeToggle(background.transform,new Vector2(510,0),rule.triggerHeart,v=>{TabsArena.I.Settings.giftRules[slot].triggerHeart=v;Save();});
            var field=Field(background.transform,new Vector2(-20,0),new Vector2(76,34),Math.Max(1,rule.likesRequired).ToString());field.contentType=InputField.ContentType.IntegerNumber;
            field.onEndEdit.AddListener(v=>{int n;rule.likesRequired=int.TryParse(v,out n)?Mathf.Clamp(n,1,1000000):100;field.text=rule.likesRequired.ToString();Save();});
        }
        building=false;
        ArenaUI.Text(panel.transform,"กดใจสะสมแยกคน · ติดตามนับครั้งแรกต่อการเปิดเกม · Heart Me แยกจากของขวัญที่เลือก",new Vector2(0,-280),new Vector2(1240,32),18,new Color(1,.81f,.46f));
        ArenaUI.Button(panel.transform,"บันทึกและกลับ",new Vector2(0,-351),new Vector2(360,50),green,Close);
        BuildPicker();canvas.gameObject.SetActive(false);
    }
    void MakeToggle(Transform parent,Vector2 pos,bool value,Action<bool> changed){
        var bg=ArenaUI.Image(parent,pos,new Vector2(42,24),new Color(.30f,.17f,.065f));bg.sprite=ArenaUI.Disc;bg.raycastTarget=true;
        var toggle=bg.gameObject.AddComponent<Toggle>();toggle.targetGraphic=bg;toggle.isOn=value;
        var thumb=ArenaUI.Image(bg.transform,new Vector2(value?9:-9,0),new Vector2(18,18),WoodlandAssets.Cream);thumb.sprite=ArenaUI.Disc;
        bg.color=value?new Color(.25f,.65f,.11f):new Color(.30f,.17f,.065f);
        toggle.onValueChanged.AddListener(v=>{thumb.rectTransform.anchoredPosition=new Vector2(v?9:-9,0);bg.color=v?new Color(.25f,.65f,.11f):new Color(.30f,.17f,.065f);if(!building)changed(v);});
    }

    InputField Field(Transform parent,Vector2 pos,Vector2 size,string value){
        var bg=ArenaUI.Image(parent,pos,size,new Color(.22f,.105f,.035f));bg.raycastTarget=true;
        var field=bg.gameObject.AddComponent<InputField>();var text=ArenaUI.Text(bg.transform,"",Vector2.zero,size-new Vector2(12,4),18,Color.white);text.alignment=TextAnchor.MiddleLeft;field.textComponent=text;field.text=value;return field;
    }
    void Refresh(){for(int i=0;i<7;i++){var r=TabsArena.I.Settings.giftRules[i];previews[i].sprite=TabsGiftCatalog.Icon(r.gift);previews[i].enabled=previews[i].sprite;labels[i].text=(i+1)+". "+r.unit+"  →  "+r.gift;}}
    void BuildPicker(){
        var p=ArenaUI.Image(canvas.transform,Vector2.zero,new Vector2(1320,820),dark);p.raycastTarget=true;picker=p.gameObject;
        ArenaUI.Text(p.transform,"เลือกรูปของขวัญ",new Vector2(0,365),new Vector2(900,45),30,Color.white);
        search=Field(p.transform,new Vector2(-150,310),new Vector2(800,42),"");search.onValueChanged.AddListener(v=>{page=0;RenderPage();});
        ArenaUI.Button(p.transform,"กลับ",new Vector2(480,310),new Vector2(180,42),blue,()=>{picker.SetActive(false);settings.SetActive(true);});
        cells=ArenaUI.Rect("Gifts",p.transform,new Vector2(0,20),new Vector2(1240,530));
        ArenaUI.Button(p.transform,"ก่อนหน้า",new Vector2(-300,-352),new Vector2(210,44),blue,()=>{if(page>0){page--;RenderPage();}});
        pageLabel=ArenaUI.Text(p.transform,"",new Vector2(0,-352),new Vector2(330,44),20,Color.white);
        ArenaUI.Button(p.transform,"ถัดไป",new Vector2(300,-352),new Vector2(210,44),blue,()=>{if((page+1)*24<Filtered().Length){page++;RenderPage();}});
        picker.SetActive(false);
    }
    TabsGiftCatalog.Entry[] Filtered(){string q=(search.text??"").Trim();return TabsGiftCatalog.Entries.Where(e=>q.Length==0||e.name.IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0||(e.listedName??"").IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0||e.price.ToString()==q).ToArray();}
    void OpenPicker(int slot){selected=slot;page=0;search.text="";settings.SetActive(false);picker.SetActive(true);RenderPage();}
    void RenderPage(){
        foreach(var tile in tiles)Destroy(tile);tiles.Clear();var entries=Filtered();pageLabel.text=(page+1)+" / "+Math.Max(1,(entries.Length+23)/24)+" · "+entries.Length+" รูป";
        for(int i=0;i<24&&page*24+i<entries.Length;i++){
            var e=entries[page*24+i];float x=-510+(i%6)*204,y=195-(i/6)*134;
            var b=ArenaUI.Button(cells,"",new Vector2(x,y),new Vector2(192,122),new Color(.42f,.24f,.10f),()=>{
                var rule=TabsArena.I.Settings.giftRules[selected];if(TabsGiftCatalog.Key(rule.gift)!=TabsGiftCatalog.Key(e.name))rule.giftIds=new string[0];rule.gift=e.name;Save();Refresh();picker.SetActive(false);settings.SetActive(true);
            });tiles.Add(b.gameObject);
            var im=ArenaUI.Image(b.transform,new Vector2(0,19),new Vector2(64,64),Color.white);im.sprite=TabsGiftCatalog.Icon(e.name);im.preserveAspect=true;
            ArenaUI.Text(b.transform,e.name+"\n"+e.price+" เหรียญ",new Vector2(0,-35),new Vector2(186,44),15,Color.white);
        }
    }
    void Update(){if(Visible&&Input.GetKeyDown(KeyCode.Escape))Close();}
    void OnDestroy(){if(canvas)Destroy(canvas.gameObject);}
}

