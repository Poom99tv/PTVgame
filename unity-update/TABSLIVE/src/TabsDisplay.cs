using UnityEngine;
using UnityEngine.UI;

public static class TabsDisplay
{
    public static bool Portrait {get{return Screen.width<Screen.height;}}
    public static void TikTok(){
        // Keep the client area exactly 9:16 and leave room for the Windows title bar.
        int maxWidth=Mathf.Max(180,Screen.currentResolution.width-80);
        int maxHeight=Mathf.Max(320,Screen.currentResolution.height-120);
        int unit=Mathf.Max(20,Mathf.Min(120,Mathf.Min(maxWidth/9,maxHeight/16)));
        Screen.SetResolution(unit*9,unit*16,FullScreenMode.Windowed);
    }
    public static void Top(RectTransform rect,Vector2 position){rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,1);rect.anchoredPosition=position;}
}

public sealed class TabsResponsiveCanvas : MonoBehaviour
{
    CanvasScaler scaler;
    void Awake(){scaler=GetComponent<CanvasScaler>();}
    void Update(){
        if(!scaler||scaler.uiScaleMode!=CanvasScaler.ScaleMode.ScaleWithScreenSize)return;
        if(scaler.screenMatchMode!=CanvasScaler.ScreenMatchMode.Expand)scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
        var reference=TabsDisplay.Portrait?new Vector2(900,1600):new Vector2(1600,900);
        if(scaler.referenceResolution!=reference)scaler.referenceResolution=reference;
    }
}
