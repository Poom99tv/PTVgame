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
    sealed class Row {public RectTransform panel,pick,field;public Text title;public readonly List<RectTransform> toggles=new List<RectTransform>();public readonly List<Text> captions=new List<Text>();}
    readonly List<Row> rows=new List<Row>();readonly List<Text> headings=new List<Text>();
    Text settingsTitle,settingsHelp,settingsNote,pickerTitle;RectTransform languageButton,saveButton,backButton,previousButton,nextButton;
    Vector2 lastArea;bool lastPortrait;int PageSize {get{return TabsDisplay.Portrait?18:24;}}
    Color dark=new Color(.055f,.075f,.12f,.99f),green=new Color(.08f,.42f,.33f),blue=new Color(.12f,.32f,.67f);
    public bool Visible {get{return canvas&&canvas.gameObject.activeSelf;}}
    void OnEnable(){TabsLanguage.Changed+=RefreshLanguage;}
    void OnDisable(){TabsLanguage.Changed-=RefreshLanguage;}
    void RefreshLanguage(){if(picker&&picker.activeSelf)RenderPage();}
    public void Open(){if(!canvas)Build();GetComponent<TabsArenaUI>().SetVisible(false);canvas.gameObject.SetActive(true);Refresh();}
    void Close(){Save();canvas.gameObject.SetActive(false);GetComponent<TabsArenaUI>().SetVisible(true);}
    void Save(){File.WriteAllText(TabsArena.ConfigPath,JsonConvert.SerializeObject(TabsArena.I.Settings,Formatting.Indented));}
    void Build(){
        canvas=ArenaUI.Canvas("TikTok Gift Settings",300);canvas.gameObject.AddComponent<GraphicRaycaster>();
        var panel=ArenaUI.Image(canvas.transform,Vector2.zero,new Vector2(1320,820),dark);panel.raycastTarget=true;settings=panel.gameObject;
        languageButton=(RectTransform)ArenaUI.Button(panel.transform,"EN / ไทย",new Vector2(565,365),new Vector2(120,38),green,TabsLanguage.Toggle).transform;
        settingsTitle=ArenaUI.Text(panel.transform,"@loc:settings.title",new Vector2(-60,365),new Vector2(1050,50),32,Color.white);
        settingsHelp=ArenaUI.Text(panel.transform,"@loc:settings.help",new Vector2(0,323),new Vector2(1240,34),18,new Color(.65f,.78f,.9f));
        string[] headers={"@loc:settings.gift","@loc:settings.likes","@loc:settings.follow","Heart Me","@loc:settings.threshold"};float[] xs={90,230,370,510,-20};
        headings.Add(ArenaUI.Text(panel.transform,"@loc:settings.hero",new Vector2(-390,278),new Vector2(400,30),19,Color.white));
        for(int h=0;h<headers.Length;h++)headings.Add(ArenaUI.Text(panel.transform,headers[h],new Vector2(xs[h],278),new Vector2(h==3?130:100,30),18,Color.white));
        building=true;
        for(int i=0;i<7;i++){
            int slot=i;float y=215-i*64;var rule=TabsArena.I.Settings.giftRules[i];
            var background=ArenaUI.Image(panel.transform,new Vector2(0,y),new Vector2(1240,58),new Color(.94f,.81f,.59f));
            var row=new Row{panel=background.rectTransform};rows.Add(row);
            var pick=ArenaUI.Button(background.transform,"",new Vector2(-567,0),new Vector2(42,40),blue,()=>OpenPicker(slot));
            var icon=ArenaUI.Image(pick.transform,Vector2.zero,new Vector2(36,36),Color.white);icon.preserveAspect=true;previews.Add(icon);
            var title=ArenaUI.Text(background.transform,"",new Vector2(-333,0),new Vector2(394,40),17,new Color(.18f,.08f,.025f));title.alignment=TextAnchor.MiddleLeft;labels.Add(title);
            row.pick=(RectTransform)pick.transform;row.title=title;
            row.toggles.Add(MakeToggle(background.transform,new Vector2(90,0),rule.triggerGift,v=>{TabsArena.I.Settings.giftRules[slot].triggerGift=v;Save();}));
            row.toggles.Add(MakeToggle(background.transform,new Vector2(230,0),rule.triggerLikes,v=>{TabsArena.I.Settings.giftRules[slot].triggerLikes=v;Save();}));
            row.toggles.Add(MakeToggle(background.transform,new Vector2(370,0),rule.triggerFollow,v=>{TabsArena.I.Settings.giftRules[slot].triggerFollow=v;Save();}));
            row.toggles.Add(MakeToggle(background.transform,new Vector2(510,0),rule.triggerHeart,v=>{TabsArena.I.Settings.giftRules[slot].triggerHeart=v;Save();}));
            var field=Field(background.transform,new Vector2(-20,0),new Vector2(76,34),Math.Max(1,rule.likesRequired).ToString());field.contentType=InputField.ContentType.IntegerNumber;
            row.field=(RectTransform)field.transform;
            for(int c=0;c<5;c++){var caption=ArenaUI.Text(background.transform,headers[c],Vector2.zero,new Vector2(130,24),16,WoodlandAssets.Ink);caption.gameObject.SetActive(false);row.captions.Add(caption);}
            field.onEndEdit.AddListener(v=>{int n;rule.likesRequired=int.TryParse(v,out n)?Mathf.Clamp(n,1,1000000):100;field.text=rule.likesRequired.ToString();Save();});
        }
        building=false;
        settingsNote=ArenaUI.Text(panel.transform,"@loc:settings.note",new Vector2(0,-280),new Vector2(1240,32),18,new Color(1,.81f,.46f));
        saveButton=(RectTransform)ArenaUI.Button(panel.transform,"@loc:settings.save",new Vector2(0,-351),new Vector2(360,50),green,Close).transform;
        BuildPicker();canvas.gameObject.SetActive(false);
    }
    RectTransform MakeToggle(Transform parent,Vector2 pos,bool value,Action<bool> changed){
        var bg=ArenaUI.Image(parent,pos,new Vector2(42,24),new Color(.30f,.17f,.065f));bg.sprite=ArenaUI.Disc;bg.raycastTarget=true;
        var toggle=bg.gameObject.AddComponent<Toggle>();toggle.targetGraphic=bg;toggle.isOn=value;
        var thumb=ArenaUI.Image(bg.transform,new Vector2(value?9:-9,0),new Vector2(18,18),WoodlandAssets.Cream);thumb.sprite=ArenaUI.Disc;
        bg.color=value?new Color(.25f,.65f,.11f):new Color(.30f,.17f,.065f);
        toggle.onValueChanged.AddListener(v=>{thumb.rectTransform.anchoredPosition=new Vector2(v?9:-9,0);bg.color=v?new Color(.25f,.65f,.11f):new Color(.30f,.17f,.065f);if(!building)changed(v);});
        return bg.rectTransform;
    }

    InputField Field(Transform parent,Vector2 pos,Vector2 size,string value){
        var bg=ArenaUI.Image(parent,pos,size,new Color(.22f,.105f,.035f));bg.raycastTarget=true;
        var field=bg.gameObject.AddComponent<InputField>();var text=ArenaUI.Text(bg.transform,"",Vector2.zero,size-new Vector2(12,4),18,Color.white);text.alignment=TextAnchor.MiddleLeft;field.textComponent=text;field.text=value;return field;
    }
    void Refresh(){for(int i=0;i<7;i++){var r=TabsArena.I.Settings.giftRules[i];previews[i].sprite=TabsGiftCatalog.Icon(r.gift);previews[i].enabled=previews[i].sprite;labels[i].text=(i+1)+". "+r.unit+"  →  "+r.gift;}}
    void BuildPicker(){
        var p=ArenaUI.Image(canvas.transform,Vector2.zero,new Vector2(1320,820),dark);p.raycastTarget=true;picker=p.gameObject;
        pickerTitle=ArenaUI.Text(p.transform,"@loc:picker.title",new Vector2(0,365),new Vector2(900,45),30,Color.white);
        search=Field(p.transform,new Vector2(-150,310),new Vector2(800,42),"");search.onValueChanged.AddListener(v=>{page=0;RenderPage();});
        backButton=(RectTransform)ArenaUI.Button(p.transform,"@loc:picker.back",new Vector2(480,310),new Vector2(180,42),blue,()=>{picker.SetActive(false);settings.SetActive(true);}).transform;
        cells=ArenaUI.Rect("Gifts",p.transform,new Vector2(0,20),new Vector2(1240,530));
        previousButton=(RectTransform)ArenaUI.Button(p.transform,"@loc:picker.previous",new Vector2(-300,-352),new Vector2(210,44),blue,()=>{if(page>0){page--;RenderPage();}}).transform;
        pageLabel=ArenaUI.Text(p.transform,"",new Vector2(0,-352),new Vector2(330,44),20,Color.white);
        nextButton=(RectTransform)ArenaUI.Button(p.transform,"@loc:picker.next",new Vector2(300,-352),new Vector2(210,44),blue,()=>{if((page+1)*PageSize<Filtered().Length){page++;RenderPage();}}).transform;
        picker.SetActive(false);
    }
    TabsGiftCatalog.Entry[] Filtered(){string q=(search.text??"").Trim();return TabsGiftCatalog.Entries.Where(e=>q.Length==0||e.name.IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0||(e.listedName??"").IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0||e.price.ToString()==q).ToArray();}
    void OpenPicker(int slot){selected=slot;page=0;search.text="";settings.SetActive(false);picker.SetActive(true);RenderPage();}
    void RenderPage(){
        foreach(var tile in tiles)Destroy(tile);tiles.Clear();var entries=Filtered();int size=PageSize;page=Mathf.Clamp(page,0,Math.Max(0,(entries.Length-1)/size));pageLabel.text=TabsLanguage.T("picker.page",page+1,Math.Max(1,(entries.Length+size-1)/size),entries.Length);
        for(int i=0;i<size&&page*size+i<entries.Length;i++){
            var e=entries[page*size+i];float x=TabsDisplay.Portrait?-255+(i%3)*255:-510+(i%6)*204,y=TabsDisplay.Portrait?450-(i/3)*160:195-(i/6)*134;
            var b=ArenaUI.Button(cells,"",new Vector2(x,y),new Vector2(192,122),new Color(.42f,.24f,.10f),()=>{
                var rule=TabsArena.I.Settings.giftRules[selected];if(TabsGiftCatalog.Key(rule.gift)!=TabsGiftCatalog.Key(e.name))rule.giftIds=new string[0];rule.gift=e.name;Save();Refresh();picker.SetActive(false);settings.SetActive(true);
            });tiles.Add(b.gameObject);
            var im=ArenaUI.Image(b.transform,new Vector2(0,19),new Vector2(64,64),Color.white);im.sprite=TabsGiftCatalog.Icon(e.name);im.preserveAspect=true;
            ArenaUI.Text(b.transform,TabsLanguage.T("picker.gift",e.name,e.price),new Vector2(0,-35),new Vector2(186,44),15,Color.white);
        }
    }
    static void Place(RectTransform rect,Vector2 position,Vector2 size){rect.anchoredPosition=position;rect.sizeDelta=size;}
    void Layout(){
        if(!canvas)return;var area=((RectTransform)canvas.transform).rect.size;bool p=TabsDisplay.Portrait;
        if(area==lastArea&&p==lastPortrait)return;lastArea=area;lastPortrait=p;
        Vector2 size=p?new Vector2(860,1480):new Vector2(1320,820);float scale=Mathf.Min(1,Mathf.Min((area.x-28)/size.x,(area.y-28)/size.y));
        foreach(var go in new[]{settings,picker}){var rect=(RectTransform)go.transform;rect.sizeDelta=size;rect.localScale=Vector3.one*scale;}
        Place(settingsTitle.rectTransform,p?new Vector2(-65,685):new Vector2(-60,365),p?new Vector2(650,50):new Vector2(1050,50));
        Place(languageButton,p?new Vector2(330,685):new Vector2(565,365),new Vector2(120,38));
        Place(settingsHelp.rectTransform,new Vector2(0,p?610:323),p?new Vector2(800,70):new Vector2(1240,34));
        foreach(var heading in headings)heading.gameObject.SetActive(!p);
        for(int i=0;i<rows.Count;i++){var row=rows[i];Place(row.panel,new Vector2(0,p?500-i*154:215-i*64),p?new Vector2(800,136):new Vector2(1240,58));
            Place(row.pick,p?new Vector2(-345,28):new Vector2(-567,0),p?new Vector2(64,58):new Vector2(42,40));
            Place(row.title.rectTransform,p?new Vector2(0,33):new Vector2(-333,0),p?new Vector2(600,42):new Vector2(394,40));row.title.fontSize=p?20:17;
            Place(row.field,p?new Vector2(-275,-26):new Vector2(-20,0),new Vector2(76,34));
            for(int j=0;j<4;j++){float x=p?-100+j*145:90+j*140;Place(row.toggles[j],new Vector2(x,p?-26:0),new Vector2(p?54:42,p?30:24));row.captions[j].gameObject.SetActive(p);row.captions[j].rectTransform.anchoredPosition=new Vector2(x,-55);}
            row.captions[4].gameObject.SetActive(p);row.captions[4].rectTransform.anchoredPosition=new Vector2(-275,-55);
        }
        Place(settingsNote.rectTransform,new Vector2(0,p?-600:-280),p?new Vector2(800,65):new Vector2(1240,32));
        Place(saveButton,new Vector2(0,p?-690:-351),new Vector2(360,50));
        Place(pickerTitle.rectTransform,new Vector2(0,p?685:365),p?new Vector2(780,45):new Vector2(900,45));
        Place((RectTransform)search.transform,p?new Vector2(-90,610):new Vector2(-150,310),p?new Vector2(560,42):new Vector2(800,42));search.textComponent.rectTransform.sizeDelta=new Vector2(p?548:788,38);
        Place(backButton,p?new Vector2(300,610):new Vector2(480,310),new Vector2(180,42));
        Place((RectTransform)cells,p?new Vector2(0,0):new Vector2(0,20),p?new Vector2(800,1080):new Vector2(1240,530));
        Place(previousButton,new Vector2(p?-280:-300,p?-690:-352),new Vector2(210,44));Place(nextButton,new Vector2(p?280:300,p?-690:-352),new Vector2(210,44));
        Place(pageLabel.rectTransform,new Vector2(0,p?-690:-352),new Vector2(p?310:330,44));
        if(picker.activeSelf)RenderPage();
    }
    void Update(){if(Visible&&Input.GetKeyDown(KeyCode.Escape))Close();}
    void LateUpdate(){Layout();}
    void OnDestroy(){if(canvas)Destroy(canvas.gameObject);}
}

