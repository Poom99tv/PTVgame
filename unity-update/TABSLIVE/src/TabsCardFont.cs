using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
public static class TabsCardFont
{
    [DllImport("gdi32.dll",EntryPoint="AddFontResourceExW",CharSet=CharSet.Unicode,ExactSpelling=true)]
    static extern int AddFontResourceEx(string file,uint flags,IntPtr reserved);
    static bool prepared;static Font font;
    public static void Prepare(){
        if(prepared)return;prepared=true;
        string path=Path.Combine(TabsArena.Root,"TikTokUI","Fonts","BreeSerif-Regular.ttf");
        if(File.Exists(path))try{AddFontResourceEx(path,0x10,IntPtr.Zero);}catch{}
    }
    public static Font Font {get{
        if(!font){Prepare();font=Font.CreateDynamicFontFromOSFont(new[]{"Bree Serif","Georgia"},32);}return font;
    }}
}
