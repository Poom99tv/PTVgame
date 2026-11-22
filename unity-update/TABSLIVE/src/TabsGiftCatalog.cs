using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

// PVZ's local gift catalog and canonical aliases, with gift-ID and picture identity.
public static class TabsGiftCatalog
{
    [Serializable] public class Entry { public string name,listedName;public int price; }
    [Serializable] class Index { public Entry[] entries; }
    [Serializable] class Identity { public string id,gift; }
    [Serializable] class Identities { public List<Identity> entries=new List<Identity>(); }
    static Entry[] entries;
    static readonly Dictionary<string,Sprite> icons=new Dictionary<string,Sprite>();
    static readonly Dictionary<string,string> ids=new Dictionary<string,string>();
    static readonly Dictionary<string,float[]> signatures=new Dictionary<string,float[]>();
    static bool idsLoaded;
    static Dictionary<string,string> aliases;
    static string Folder { get { return Path.Combine(TabsArena.Root,"TikTokGifts"); } }
    static string IdPath { get { return Path.Combine(TabsArena.Root,"TikTokGiftIdentities.json"); } }
    public static Entry[] Entries { get {
        if(entries==null) { try { entries=JsonConvert.DeserializeObject<Index>(File.ReadAllText(Path.Combine(Folder,"GiftPriceIndex.json"))).entries.OrderBy(x=>x.price).ThenBy(x=>x.name).ToArray(); } catch { entries=new Entry[0]; } }
        return entries;
    } }
    public static string Key(string value) {
        string token=new string((value??"").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        if(aliases==null){aliases=new Dictionary<string,string>();foreach(var e in Entries){aliases[Token(e.name)]=Token(e.name);aliases[Token(e.listedName)]=Token(e.name);}aliases["donut"]="doughnut";aliases["handheart"]="handhearts";aliases["friendship"]="friendshipnecklace";}
        string canonical;return aliases.TryGetValue(token,out canonical)?canonical:token;
    }
    static string Token(string s) { return new string((s??"").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray()); }
    public static Sprite Icon(string name) {
        if(string.IsNullOrEmpty(name))return null;Sprite sprite;if(icons.TryGetValue(name,out sprite))return sprite;
        var entry=Entries.FirstOrDefault(e=>Key(e.name)==Key(name));string file=Path.Combine(Folder,(entry==null?name:entry.name)+".png");
        if(!File.Exists(file))return null;
        var t=new Texture2D(2,2,TextureFormat.RGBA32,false);if(!t.LoadImage(File.ReadAllBytes(file))){UnityEngine.Object.Destroy(t);return null;}
        t.wrapMode=TextureWrapMode.Clamp;sprite=Sprite.Create(t,new Rect(0,0,t.width,t.height),new Vector2(.5f,.5f));icons[name]=sprite;return sprite;
    }
    static void Remember(string id,string gift) {
        if(string.IsNullOrEmpty(id)||string.IsNullOrEmpty(gift))return;ids[id]=gift;
        var save=new Identities();foreach(var p in ids)save.entries.Add(new Identity{id=p.Key,gift=p.Value});
        try{File.WriteAllText(IdPath,JsonConvert.SerializeObject(save,Formatting.Indented));}catch{}
    }
    static float[] Signature(Texture2D image) {
        int left=image.width,right=0,bottom=image.height,top=0;var pixels=image.GetPixels32();
        for(int y=0;y<image.height;y++)for(int x=0;x<image.width;x++)if(pixels[y*image.width+x].a>24){left=Math.Min(left,x);right=Math.Max(right,x);bottom=Math.Min(bottom,y);top=Math.Max(top,y);}
        if(right<=left||top<=bottom){left=bottom=0;right=image.width-1;top=image.height-1;}
        var values=new float[16*16*3];int i=0;
        for(int y=0;y<16;y++)for(int x=0;x<16;x++) {
            Color c=image.GetPixelBilinear((left+(right-left)*(x+.5f)/16)/image.width,(bottom+(top-bottom)*(y+.5f)/16)/image.height);
            values[i++]=c.r*c.a+1-c.a;values[i++]=c.g*c.a+1-c.a;values[i++]=c.b*c.a+1-c.a;
        }
        return values;
    }
    public static IEnumerator Match(PTVTikTokLive.Message m,Action<string> ready) {
        if(!idsLoaded){idsLoaded=true;try{if(File.Exists(IdPath))foreach(var e in JsonConvert.DeserializeObject<Identities>(File.ReadAllText(IdPath)).entries)ids[e.id]=e.gift;}catch{}}
        string gift;
        if(!string.IsNullOrEmpty(m.giftId)&&ids.TryGetValue(m.giftId,out gift)){ready(gift);yield break;}
        var named=Entries.FirstOrDefault(e=>Key(e.name)==Key(m.giftName));
        if(named!=null){Remember(m.giftId,named.name);ready(named.name);yield break;}
        if(!string.IsNullOrEmpty(m.giftImageUrl)) {
            using(var request=UnityWebRequestTexture.GetTexture(m.giftImageUrl,false)) {
                request.timeout=8;yield return request.SendWebRequest();
                if(!request.isNetworkError&&!request.isHttpError&&request.downloadedBytes<=4194304) {
                    var image=DownloadHandlerTexture.GetContent(request);
                    if(image&&image.width<=2048&&image.height<=2048) {
                        var incoming=Signature(image);float best=float.MaxValue,second=float.MaxValue;string winner=null;
                        var names=TabsArena.I.Settings.giftRules.Select(r=>r.gift).Concat(new[]{"Heart Me"}).Where(n=>!string.IsNullOrEmpty(n)).Distinct();
                        foreach(string name in names) {
                            float[] sig;if(!signatures.TryGetValue(name,out sig)){var icon=Icon(name);if(!icon)continue;signatures[name]=sig=Signature(icon.texture);}
                            float error=0;for(int i=0;i<sig.Length;i++){float d=sig[i]-incoming[i];error+=d*d;}error/=sig.Length;
                            if(error<best){second=best;best=error;winner=name;}else second=Math.Min(second,error);
                        }
                        UnityEngine.Object.Destroy(image);
                        if(best<.018f&&(best<.0005f||second-best>.003f)){Remember(m.giftId,winner);ready(winner);yield break;}
                    }else if(image)UnityEngine.Object.Destroy(image);
                }
            }
        }
        ready(m.giftName);
    }
}

