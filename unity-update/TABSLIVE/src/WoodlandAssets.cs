using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public static class WoodlandAssets
{
    static readonly Dictionary<string,Sprite> cache=new Dictionary<string,Sprite>();
    public static Color Cream=new Color(1,.89f,.66f), Ink=new Color(.19f,.085f,.025f);
    public static Sprite Get(string name,bool slice=false){
        string key=name+(slice?"-slice":"");Sprite sprite;if(cache.TryGetValue(key,out sprite))return sprite;
        string path=Path.Combine(TabsArena.Root,"TikTokUI",name+".png");if(!File.Exists(path))return null;
        var tex=new Texture2D(2,2,TextureFormat.RGBA32,true);if(!tex.LoadImage(File.ReadAllBytes(path)))return null;
        tex.wrapMode=TextureWrapMode.Clamp;tex.filterMode=FilterMode.Trilinear;tex.anisoLevel=4;tex.Apply(true,false);
        var pixels=tex.GetPixels32();int xmin=tex.width,ymin=tex.height,xmax=0,ymax=0;
        for(int y=0;y<tex.height;y++)for(int x=0;x<tex.width;x++)if(pixels[y*tex.width+x].a>80){xmin=Math.Min(xmin,x);xmax=Math.Max(xmax,x);ymin=Math.Min(ymin,y);ymax=Math.Max(ymax,y);}
        if(xmax<=xmin)return null;
        var rect=new Rect(xmin,ymin,xmax-xmin+1,ymax-ymin+1);
        float edge=slice?Math.Min(rect.width,rect.height)*.06f:0;
        sprite=Sprite.Create(tex,rect,new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,new Vector4(edge,edge,edge,edge));cache[key]=sprite;return sprite;
    }
    public static void Skin(Image image,string name,bool slice=true){
        var s=Get(name,slice);if(!s)return;image.sprite=s;image.type=slice?Image.Type.Sliced:Image.Type.Simple;image.color=Color.white;image.pixelsPerUnitMultiplier=8;
    }
    public static Image Art(Transform parent,string name,Vector2 pos,Vector2 size){var image=ArenaUI.Image(parent,pos,size,Color.white);Skin(image,name,false);return image;}
}
