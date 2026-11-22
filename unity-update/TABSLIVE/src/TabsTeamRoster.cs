using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public sealed class TabsTeamRoster : MonoBehaviour
{
    Canvas canvas;readonly RectTransform[] panels=new RectTransform[2],contents=new RectTransform[2];
    readonly ScrollRect[] scroll=new ScrollRect[2];readonly int[] counts=new int[2];
    readonly Dictionary<string,TabsSquarePortrait> portraits=new Dictionary<string,TabsSquarePortrait>();int version=-1;
    void Start(){
        canvas=ArenaUI.Canvas("TikTok Team Profiles",160);canvas.GetComponent<CanvasScaler>().uiScaleMode=CanvasScaler.ScaleMode.ConstantPixelSize;canvas.gameObject.AddComponent<GraphicRaycaster>();
        for(int team=0;team<2;team++){
            panels[team]=ArenaUI.Rect("Team profile list",canvas.transform,Vector2.zero,new Vector2(46,400));
            var hit=panels[team].gameObject.AddComponent<Image>();hit.color=new Color(0,0,0,.02f);hit.raycastTarget=true;
            panels[team].gameObject.AddComponent<RectMask2D>();scroll[team]=panels[team].gameObject.AddComponent<ScrollRect>();scroll[team].horizontal=false;scroll[team].vertical=true;scroll[team].scrollSensitivity=24;scroll[team].movementType=ScrollRect.MovementType.Clamped;
            contents[team]=ArenaUI.Rect("Selected viewers",panels[team],Vector2.zero,new Vector2(46,0));contents[team].anchorMin=contents[team].anchorMax=contents[team].pivot=new Vector2(.5f,1);
            scroll[team].viewport=panels[team];scroll[team].content=contents[team];
        }
    }
    void Sync(){
        for(int t=0;t<2;t++)panels[t].gameObject.SetActive(true);
        var director=TabsArena.I.Director;var keep=new HashSet<string>();counts[0]=counts[1]=0;
        foreach(var member in director.TeamMembers){
            keep.Add(member.id);TabsSquarePortrait portrait;if(!portraits.TryGetValue(member.id,out portrait)){
                var row=ArenaUI.Rect("Selected profile",contents[member.team],Vector2.zero,new Vector2(42,42));portrait=row.gameObject.AddComponent<TabsSquarePortrait>();portraits[member.id]=portrait;
            }
            var rect=(RectTransform)portrait.transform;rect.SetParent(contents[member.team],false);TabsDisplay.Top(rect,new Vector2(0,-counts[member.team]*48));counts[member.team]++;portrait.SetViewer(member.viewer,member.team);
        }
        foreach(var key in portraits.Keys.Where(key=>!keep.Contains(key)).ToArray()){Destroy(portraits[key].gameObject);portraits.Remove(key);}
        for(int t=0;t<2;t++){contents[t].sizeDelta=new Vector2(46,counts[t]*48);scroll[t].verticalNormalizedPosition=counts[t]>0?0:1;}
    }
    void LateUpdate(){
        if(!canvas||!TabsArena.I)return;var director=TabsArena.I.Director;
        if(version!=director.TeamSelectionVersion){version=director.TeamSelectionVersion;Sync();}
        float uiScale=Mathf.Min(Screen.width/(TabsDisplay.Portrait?900f:1600f),Screen.height/(TabsDisplay.Portrait?1600f:900f));
        float top=Mathf.Max(90,(TabsDisplay.Portrait?198:142)*uiScale+8),bottom=(TabsDisplay.Portrait?500:210)*uiScale+20;
        float height=Mathf.Max(48,Screen.height-top-bottom);
        for(int t=0;t<2;t++){
            panels[t].anchorMin=panels[t].anchorMax=panels[t].pivot=new Vector2(t==0?0:1,1);panels[t].anchoredPosition=new Vector2(t==0?8:-8,-top);panels[t].sizeDelta=new Vector2(46,height);
            panels[t].gameObject.SetActive(director.CanSpawn&&counts[t]>0&&!(TabsRoundFlow.I&&TabsRoundFlow.I.BetweenRounds));
        }
    }
    void OnDestroy(){if(canvas)Destroy(canvas.gameObject);}
}

public sealed class TabsSquarePortrait : MonoBehaviour
{
    RawImage photo;Image backing;GameObject placeholder;Texture retained;string signature;int request;
    void Awake(){
        var frame=gameObject.AddComponent<Image>();frame.color=Color.black;frame.raycastTarget=false;
        backing=ArenaUI.Rect("Team",transform,Vector2.zero,new Vector2(36,36)).gameObject.AddComponent<Image>();backing.raycastTarget=false;
        placeholder=ArenaUI.Rect("Profile loading",transform,Vector2.zero,new Vector2(36,36)).gameObject;
        var head=ArenaUI.Image(placeholder.transform,new Vector2(0,6),new Vector2(12,12),Color.white);head.sprite=ArenaUI.Disc;
        var body=ArenaUI.Image(placeholder.transform,new Vector2(0,-8),new Vector2(24,16),Color.white);body.sprite=ArenaUI.Disc;
        photo=ArenaUI.Rect("Profile image",transform,Vector2.zero,new Vector2(36,36)).gameObject.AddComponent<RawImage>();photo.raycastTarget=false;photo.enabled=false;
    }
    public void SetViewer(PTVTikTokLive.Message viewer,int team){
        backing.color=team==0?new Color(.55f,.1f,.15f):new Color(.1f,.25f,.55f);
        var urls=new List<string>();if(!string.IsNullOrEmpty(viewer.profilePictureUrl))urls.Add(viewer.profilePictureUrl);if(viewer.profilePictureUrls!=null)foreach(var url in viewer.profilePictureUrls)if(!urls.Contains(url))urls.Add(url);
        string next=string.Join("|",urls.ToArray());if(next==signature)return;signature=next;int serial=++request;if(urls.Count==0)return;
        StartCoroutine(PTVProfileCache.Get().Fetch(urls,texture=>{
            if(!this||serial!=request||!texture)return;if(retained!=texture){if(retained)PTVProfileCache.Get().Release(retained);retained=texture;PTVProfileCache.Get().Retain(texture);}
            photo.texture=texture;float aspect=(float)texture.width/texture.height;photo.uvRect=aspect>1?new Rect((1-1/aspect)/2,0,1/aspect,1):new Rect(0,(1-aspect)/2,1,aspect);photo.enabled=true;placeholder.SetActive(false);
        }));
    }
    void OnDestroy(){request++;if(retained&&PTVProfileCache.Existing)PTVProfileCache.Existing.Release(retained);}
}
