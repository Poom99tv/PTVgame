using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

public static class TabsLanguage
{
    static readonly Dictionary<string,Dictionary<string,string>> tables=new Dictionary<string,Dictionary<string,string>>();
    public static event Action Changed;
    public static string Current {get{return TabsArena.I&&TabsArena.I.Settings!=null&&TabsArena.I.Settings.language=="th"?"th":"en";}}
    static Dictionary<string,string> Table(string language){
        Dictionary<string,string> table;if(tables.TryGetValue(language,out table))return table;
        using(var stream=typeof(TabsLanguage).Assembly.GetManifestResourceStream("TabsLocales."+language+".json"))
        using(var reader=new StreamReader(stream))table=JsonConvert.DeserializeObject<Dictionary<string,string>>(reader.ReadToEnd());
        string path=Path.Combine(TabsArena.Root,"TikTokUI","Locales",language+".json");
        try{if(File.Exists(path)){var external=JsonConvert.DeserializeObject<Dictionary<string,string>>(File.ReadAllText(path));if(external!=null)foreach(var entry in external)if(entry.Value!=null)table[entry.Key]=entry.Value;}}catch{}
        tables[language]=table;return table;
    }
    public static bool Has(string key){return key!=null&&Table("en").ContainsKey(key);}
    public static string T(string key,params object[] args){
        string text;if(!Table(Current).TryGetValue(key,out text)&&!Table("en").TryGetValue(key,out text))text=key;
        return args==null||args.Length==0?text:string.Format(CultureInfo.InvariantCulture,text,args);
    }
    public static void Toggle(){Set(Current=="en"?"th":"en");}
    public static void Set(string language){
        language=language=="th"?"th":"en";if(Current==language)return;
        TabsArena.I.Settings.language=language;
        File.WriteAllText(TabsArena.ConfigPath,JsonConvert.SerializeObject(TabsArena.I.Settings,Formatting.Indented));
        if(Changed!=null)Changed();
    }
    public static void Bind(Text text,string key){text.gameObject.AddComponent<TabsLocalizedLabel>().Bind(text,key);}
}

public sealed class TabsLocalizedLabel : MonoBehaviour
{
    Text label;string key;
    public void Bind(Text target,string value){label=target;key=value;Refresh();}
    void OnEnable(){TabsLanguage.Changed+=Refresh;Refresh();}
    void OnDisable(){TabsLanguage.Changed-=Refresh;}
    void Refresh(){if(label&&key!=null)label.text=TabsLanguage.T(key);}
}
