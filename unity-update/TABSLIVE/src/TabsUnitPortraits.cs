using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using Landfall.TABS;

// Painted card sprites are indexed by the game's stable unit key, never by card position.
public static class TabsUnitPortraits
{
    [Serializable] public sealed class Entry {public string unitKey,atlas;public int cell;}
    [Serializable] public sealed class Index {public int columns=4,rows=2;public Entry[] entries;}
    static Dictionary<string,Entry> entries;
    static readonly Dictionary<string,Texture2D> atlases=new Dictionary<string,Texture2D>();
    static readonly Dictionary<string,Sprite> sprites=new Dictionary<string,Sprite>();
    static string Root {get{return Path.Combine(TabsArena.Root,"TikTokUI","Painted");}}
    static void Load(){
        entries=new Dictionary<string,Entry>(StringComparer.OrdinalIgnoreCase);
        string path=Path.Combine(Root,"portraits.json");if(!File.Exists(path))return;
        var index=JsonConvert.DeserializeObject<Index>(File.ReadAllText(path));
        if(index==null||index.entries==null)return;foreach(var entry in index.entries)entries[entry.unitKey]=entry;
    }
    public static Sprite Get(UnitBlueprint blueprint){
        if(!blueprint)return null;if(entries==null)Load();string key=blueprint.Entity.Name;Sprite sprite;
        if(sprites.TryGetValue(key,out sprite))return sprite;Entry entry;if(!entries.TryGetValue(key,out entry))return null;
        Texture2D texture;
        if(!atlases.TryGetValue(entry.atlas,out texture)){
            string path=Path.Combine(Root,entry.atlas+".png");if(!File.Exists(path))return null;
            texture=new Texture2D(2,2,TextureFormat.RGBA32,false);if(!texture.LoadImage(File.ReadAllBytes(path)))return null;
            texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Bilinear;atlases[entry.atlas]=texture;
        }
        float width=texture.width/4f,height=texture.height/2f;
        var rect=new Rect((entry.cell%4)*width,(1-entry.cell/4)*height,width,height);
        sprite=Sprite.Create(texture,rect,new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect);sprites[key]=sprite;return sprite;
    }
}
