using UnityEngine;
using UnityEngine.UI;

// Keep native Text layout/Thai shaping, but rasterize glyphs at the displayed scale.
public sealed class TabsCrispText : Text
{
    float previousScale=-1;
    public static float LocalScale(Text text){
        float scale=1;var root=text.canvas?text.canvas.transform:null;
        for(var node=text.transform;node&&node!=root;node=node.parent)scale*=Mathf.Max(Mathf.Abs(node.localScale.x),Mathf.Abs(node.localScale.y));
        return scale;
    }
    public static float ScreenScale(Text text){return (text.canvas?text.canvas.scaleFactor:1)*LocalScale(text);}
    public static void FontPixels(Text __instance,ref float __result){
        if(!(__instance is TabsCrispText)||!__instance.font||!__instance.font.dynamic)return;
        // Text uses this same value for glyph generation and reciprocal mesh scaling.
        __result*=Mathf.Clamp(LocalScale(__instance)*2,2,4);
    }
    void LateUpdate(){
        float scale=ScreenScale(this);if(Mathf.Abs(scale-previousScale)<.001f)return;
        previousScale=scale;cachedTextGenerator.Invalidate();SetVerticesDirty();
    }
}
