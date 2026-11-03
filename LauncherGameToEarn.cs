using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PTVgame {
 // Game To Earn = TikTok LIVE -> key presses tool (Python/PyQt, built with gametoearn/build.ps1).
 // It replaces TikTokBridge for Roblox maps, is free for everyone and is managed by the Launcher:
 //  1) <Launcher folder>\GameToEarn\PTVGameToEarn.exe        (local / bundled build, used first)
 //  2) <Launcher folder>\gametoearn\ptv_game_to_earn.py      (source, run with pythonw when Python is installed)
 //  3) otherwise downloaded + updated from release-manifests/gametoearn.json into %LOCALAPPDATA%\PTVgame\games\gametoearn
 // Settings (presets / TikTok name / voice) live in %LOCALAPPDATA%\PTVgame\GameToEarn and can be edited
 // from the Launcher page without opening Roblox or the tool; a running tool reloads them automatically.
 public static class GameToEarn {
  public const string ProcessName="PTVGameToEarn";
  public static readonly Game Tool=new Game { id="gametoearn",name="Game To Earn",category="Tool",access="free",manifestUrl="https://raw.githubusercontent.com/Poom99tv/PTVgame/main/release-manifests/gametoearn.json" };
  static string Base { get { return AppDomain.CurrentDomain.BaseDirectory; } }
  public static string LocalExe { get { return Path.Combine(Base,"GameToEarn",ProcessName+".exe"); } }
  public static string SourceFile { get { return Path.Combine(Base,"gametoearn","ptv_game_to_earn.py"); } }
  public static bool HasLocal { get { return File.Exists(LocalExe); } }
  public static Installed Installed { get { try { return Storage.Current(Tool); } catch { return null; } } }
  public static bool Available { get { return HasLocal||Installed!=null||(File.Exists(SourceFile)&&Python()!=null); } }
  public static string VersionText { get { if(HasLocal) return "ติดตั้งพร้อม Launcher"; var i=Installed; if(i!=null) return "เวอร์ชัน "+i.version; if(File.Exists(SourceFile)&&Python()!=null) return "รันจากซอร์ส (Python)"; return "ยังไม่ได้ติดตั้ง"; } }
  static int sourcePid;
  public static bool Running { get {
   try { if(Process.GetProcessesByName(ProcessName).Length>0) return true; if(sourcePid!=0) { var p=Process.GetProcessById(sourcePid); return !p.HasExited; } } catch {}
   return false; } }
  static string Python() {
   foreach(var name in new[]{"pythonw.exe","pyw.exe"}) {
    var path=(Environment.GetEnvironmentVariable("PATH")??"").Split(';').Select(d=>{ try { return Path.Combine(d.Trim().Trim('"'),name); } catch { return null; } }).FirstOrDefault(f=>f!=null&&File.Exists(f));
    if(path!=null) return path;
   }
   return null;
  }
  public static async Task<bool> Open(IProgress<string> progress,CancellationToken token,bool update) {
   if(Running) return false;
   if(HasLocal) { Process.Start(new ProcessStartInfo(LocalExe) { WorkingDirectory=Path.GetDirectoryName(LocalExe),UseShellExecute=false }); return true; }
   string py=File.Exists(SourceFile)?Python():null;
   if(py!=null&&Installed==null) {
    var p=Process.Start(new ProcessStartInfo(py,"\""+SourceFile+"\"") { WorkingDirectory=Path.GetDirectoryName(SourceFile),UseShellExecute=false });
    sourcePid=p==null?0:p.Id; return true;
   }
   if(update||Installed==null) {
    progress.Report("กำลังตรวจ Game To Earn…");
    try {
     var manifest=await Updater.Check(Tool,token);
     if(Updater.Newer(manifest,Installed)) await Updater.Install(Tool,manifest,progress,token);
    } catch(OperationCanceledException) { throw; }
    catch(Exception) { if(Installed==null) throw new Exception(AppLanguage.T("ยังไม่มีไฟล์ Game To Earn ให้ดาวน์โหลด • build ด้วย gametoearn\\build.ps1 หรือติดตั้ง Python")); }
   }
   token.ThrowIfCancellationRequested();
   Updater.Play(Tool); return true;
  }
 }

 // Reads / writes the same JSON files the Python tool uses.
 public static class GteData {
  public static string Dir { get { return Path.Combine(Storage.Root,"GameToEarn"); } }
  public static string PresetsFile { get { return Path.Combine(Dir,"presets.json"); } }
  public static string StateFile { get { return Path.Combine(Dir,"state.json"); } }
  static Dictionary<string,object> bundled;
  static Dictionary<string,object> Bundled { get {
   if(bundled==null) bundled=Storage.Read<Dictionary<string,object>>(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","gametoearn-data.json"),null)??new Dictionary<string,object>();
   return bundled; } }
  static List<Dictionary<string,object>> Copy(object o) { try { return Storage.Json.Deserialize<List<Dictionary<string,object>>>(Storage.Json.Serialize(o)); } catch { return null; } }
  public static List<Dictionary<string,object>> Defaults() {
   object d; var list=Bundled.TryGetValue("defaultPresets",out d)?Copy(d):null;
   if(list==null||list.Count==0) list=new List<Dictionary<string,object>>{ NewPreset("Preset 1") };
   return list;
  }
  public static Dictionary<string,object> NewPreset(string name) {
   return new Dictionary<string,object>{{"name",name},{"gift_map",new Dictionary<string,object>()},{"follow_enabled",true},{"follow_seq","r"},{"follow_once",true},{"like_enabled",true},{"like_seq","v"},{"like_target",100}};
  }
  public static List<Dictionary<string,object>> LoadPresets() {
   var list=Storage.Read<List<Dictionary<string,object>>>(PresetsFile,null);
   return list==null||list.Count==0?Defaults():list;
  }
  public static Dictionary<string,object> LoadState() { return Storage.Read<Dictionary<string,object>>(StateFile,null)??new Dictionary<string,object>(); }
  public static void Save(List<Dictionary<string,object>> presets,Dictionary<string,object> state) { Directory.CreateDirectory(Dir); Storage.Save(PresetsFile,presets); Storage.Save(StateFile,state); }
  static List<string> gifts;
  public static List<string> Gifts { get {
   if(gifts==null) {
    gifts=new List<string>(); object g;
    if(Bundled.TryGetValue("gifts",out g)&&g is IEnumerable) foreach(var item in (IEnumerable)g) { var pair=item as IList; if(pair!=null&&pair.Count>0&&pair[0]!=null) gifts.Add(pair.Count>1?pair[0]+"  💎"+pair[1]:pair[0].ToString()); }
   }
   return gifts; } }
  public static string GiftName(string shown) { if(shown==null) return ""; int i=shown.IndexOf("  💎",StringComparison.Ordinal); return (i>=0?shown.Substring(0,i):shown).Trim(); }
  public static string Str(Dictionary<string,object> d,string key,string fallback) { object v; return d!=null&&d.TryGetValue(key,out v)&&v!=null?v.ToString():fallback; }
  public static bool Bool(Dictionary<string,object> d,string key,bool fallback) { object v; if(d==null||!d.TryGetValue(key,out v)||v==null) return fallback; if(v is bool) return (bool)v; bool b; return bool.TryParse(v.ToString(),out b)?b:fallback; }
  public static int Int(Dictionary<string,object> d,string key,int fallback) { object v; int n; return d!=null&&d.TryGetValue(key,out v)&&v!=null&&int.TryParse(v.ToString(),out n)?n:fallback; }
 }

 public class GteRow { public string name="",key=""; public bool win; public int delta=1; }

 public partial class LauncherShell {
  const string PageGte="Game To Earn";
  List<Dictionary<string,object>> gtePresets; Dictionary<string,object> gteState; List<GteRow> gteRows; int gteIdx=-1; bool gteDirty;

  async Task OpenGameToEarn(bool fromRoblox) {
   if(GameToEarn.Running) { if(!fromRoblox) status.Text=AppLanguage.T("Game To Earn เปิดอยู่แล้ว • ค่าที่บันทึกจะอัปเดตให้อัตโนมัติ"); return; }
   await GameToEarn.Open(new Progress<string>(t=>status.Text=AppLanguage.T(t)),cancellation==null?CancellationToken.None:cancellation.Token,true);
   if(!fromRoblox) status.Text=AppLanguage.T("เปิด Game To Earn แล้ว • ใส่ชื่อ TikTok แล้วกดเชื่อมต่อ");
  }

  async void GteButton() {
   if(busy) return;
   if(gteDirty) GteSave(false);
   busy=true; cancellation=new CancellationTokenSource(); progress.Visibility=Visibility.Visible; cancel.Visibility=Visibility.Visible; Render();
   try { await OpenGameToEarn(false); }
   catch(OperationCanceledException) { status.Text=AppLanguage.T("ยกเลิกแล้ว"); }
   catch(Exception ex) { status.Text=AppLanguage.T("เปิด Game To Earn ไม่สำเร็จ: "+ex.Message); }
   finally { busy=false; cancellation.Dispose(); cancellation=null; progress.Visibility=Visibility.Collapsed; cancel.Visibility=Visibility.Collapsed; Render(); }
  }

  // ── settings model ──
  void GteLoad() {
   gtePresets=GteData.LoadPresets(); gteState=GteData.LoadState(); gteDirty=false;
   GteSelect(Math.Max(0,Math.Min(GteData.Int(gteState,"preset",0),gtePresets.Count-1)));
  }
  void GteSelect(int idx) {
   GteFlush(); gteIdx=idx; gteRows=new List<GteRow>();
   object m; var map=gtePresets[idx].TryGetValue("gift_map",out m)?m as IDictionary<string,object>:null;
   if(map!=null) foreach(var pair in map) {
    var row=new GteRow { name=pair.Key };
    var v=pair.Value as Dictionary<string,object>;
    if(v!=null) { row.key=GteData.Str(v,"key",""); row.win=GteData.Bool(v,"win_enabled",false); row.delta=GteData.Int(v,"win_delta",1); }
    else row.key=pair.Value==null?"":pair.Value.ToString();
    gteRows.Add(row);
   }
  }
  void GteFlush() {
   if(gteRows==null||gteIdx<0||gteIdx>=gtePresets.Count) return;
   var map=new Dictionary<string,object>();
   foreach(var r in gteRows) { var n=GteData.GiftName(r.name).ToLowerInvariant(); if(n=="") continue; map[n]=new Dictionary<string,object>{{"key",(r.key??"").Trim()},{"win_enabled",r.win},{"win_delta",r.delta}}; }
   gtePresets[gteIdx]["gift_map"]=map;
  }
  void GteSave(bool render) {
   try {
    GteFlush(); gteState["preset"]=gteIdx;
    GteData.Save(gtePresets,gteState); gteDirty=false;
    status.Text=AppLanguage.T(GameToEarn.Running?"บันทึกแล้ว • Game To Earn ที่เปิดอยู่จะอัปเดตให้อัตโนมัติ":"บันทึกการตั้งค่า Game To Earn แล้ว");
   } catch(Exception ex) { status.Text=AppLanguage.T("บันทึกไม่สำเร็จ: "+ex.Message); }
   if(render) Render();
  }
  void Dirty() { gteDirty=true; }
  TextBlock GLabel(string text) { var t=Line(AppLanguage.T(text),13,muted); t.Margin=new Thickness(0,0,8,0); return t; }
  TextBox GInput(string text,double width,Action<string> changed) { var t=Ui.Input(text,width); t.Margin=new Thickness(0,2,14,2); t.TextChanged+=(s,e)=>{ changed(t.Text); Dirty(); }; return t; }
  CheckBox GCheck(string text,bool value,Action<bool> changed) { var c=new CheckBox { Content=AppLanguage.T(text),IsChecked=value,Foreground=Foreground,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,16,0) }; c.Click+=(s,e)=>{ changed(c.IsChecked==true); Dirty(); }; return c; }
  StackPanel GRow(params UIElement[] items) { var p=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,6,0,6) }; foreach(var i in items) p.Children.Add(i); return p; }
  Button GBtn(string text,Brush bg,Action click) { var b=Btn(text,bg); b.FontSize=13; b.Padding=new Thickness(12,7,12,7); b.Margin=new Thickness(0,0,8,0); b.Click+=(s,e)=>click(); return b; }

  void GameToEarnPage() {
   if(gtePresets==null) GteLoad();
   var head=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,4,0,16) };
   var icon=new Border { Width=74,Height=74,CornerRadius=new CornerRadius(18),Background=new LinearGradientBrush(Color.FromRgb(37,244,238),Color.FromRgb(254,44,85),45),Child=new TextBlock { Text="♪",FontSize=40,FontWeight=FontWeights.Bold,Foreground=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center } };
   head.Children.Add(icon);
   var who=new StackPanel { Margin=new Thickness(18,0,0,0),VerticalAlignment=VerticalAlignment.Center }; head.Children.Add(who);
   who.Children.Add(Ui.Bold(Line("Game To Earn",30)));
   who.Children.Add(Line(AppLanguage.T("เชื่อม TikTok LIVE ภายนอก → กดคีย์ในเกม Roblox อัตโนมัติ"),15,muted));
   var pills=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,9,0,0) }; who.Children.Add(pills);
   bool running=GameToEarn.Running;
   pills.Children.Add(Ui.Pill(running?"● กำลังเปิดอยู่":GameToEarn.VersionText,running?ColorBrush("#0F4A2E"):ColorBrush("#193B58"),running?ColorBrush("#5CF29B"):Brushes.White));
   pills.Children.Add(Ui.Pill("ใช้ฟรี",ColorBrush("#0F4A2E"),ColorBrush("#5CF29B")));
   sections.Children.Add(head);

   var actions=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,16) };
   var open=Btn(running?"✓  เปิดอยู่แล้ว":GameToEarn.Available?"▶  เปิด Game To Earn":"⇩  ติดตั้งและเปิด",accent);
   open.FontSize=16; open.FontWeight=FontWeights.Bold; open.Padding=new Thickness(26,12,26,12); open.IsEnabled=!busy; open.Click+=(s,e)=>GteButton();
   actions.Children.Add(open);
   var save=Btn("💾  บันทึกการตั้งค่า",ColorBrush("#166534")); save.FontSize=15; save.FontWeight=FontWeights.Bold; save.Padding=new Thickness(20,12,20,12); save.Margin=new Thickness(12,0,0,0); save.Click+=(s,e)=>GteSave(true);
   actions.Children.Add(save);
   var reload=Btn("↺  โหลดใหม่",ColorBrush("#193B58")); reload.Padding=new Thickness(16,12,16,12); reload.Margin=new Thickness(8,0,0,0); reload.Click+=(s,e)=>{ gteRows=null; GteLoad(); status.Text=AppLanguage.T("โหลดค่าล่าสุดแล้ว"); Render(); };
   actions.Children.Add(reload);
   var auto=new CheckBox { Content=AppLanguage.T("เปิดอัตโนมัติเมื่อเล่นแมพ Roblox"),IsChecked=settings.gameToEarnWithRoblox,Foreground=Foreground,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(20,0,0,0) };
   auto.Click+=(s,e)=>{ settings.gameToEarnWithRoblox=auto.IsChecked==true; Save(); };
   actions.Children.Add(auto);
   sections.Children.Add(actions);

   // connection + voice
   var conn=new StackPanel();
   conn.Children.Add(Ui.Bold(Line(AppLanguage.T("การเชื่อมต่อ"),17)));
   var user=GInput(GteData.Str(gteState,"username",""),260,v=>gteState["username"]=v.Trim().Replace("@",""));
   var voice=new ComboBox { Width=230,Margin=new Thickness(0,2,0,2),VerticalAlignment=VerticalAlignment.Center };
   var voices=new[]{"off","thai","thai_soft"}; var voiceNames=new[]{"ปิดเสียงอ่าน","ไทย ชาย (Niwat)","ไทย หญิง (Premwadee)"};
   foreach(var n in voiceNames) voice.Items.Add(AppLanguage.T(n));
   voice.SelectedIndex=Math.Max(0,Array.IndexOf(voices,GteData.Str(gteState,"voice","thai_soft")));
   voice.SelectionChanged+=(s,e)=>{ if(voice.SelectedIndex>=0){ gteState["voice"]=voices[voice.SelectedIndex]; Dirty(); } };
   conn.Children.Add(GRow(GLabel("TikTok @"),user,GLabel("เสียงอ่านแชท"),voice));
   sections.Children.Add(Box(conn));

   // preset
   var pre=new StackPanel(); var p=gtePresets[gteIdx];
   pre.Children.Add(Ui.Bold(Line("Preset",17)));
   var pick=new ComboBox { Width=260,Margin=new Thickness(0,2,10,2),VerticalAlignment=VerticalAlignment.Center };
   for(int i=0;i<gtePresets.Count;i++) pick.Items.Add((i+1)+". "+GteData.Str(gtePresets[i],"name","Preset"));
   pick.SelectedIndex=gteIdx;
   pick.SelectionChanged+=(s,e)=>{ if(pick.SelectedIndex>=0&&pick.SelectedIndex!=gteIdx){ GteSelect(pick.SelectedIndex); Dirty(); Render(); } };
   var bar=GRow(pick,
    GBtn("+ เพิ่ม",ColorBrush("#166534"),()=>{ GteFlush(); gtePresets.Add(GteData.NewPreset("Preset "+(gtePresets.Count+1))); GteSelect(gtePresets.Count-1); Dirty(); Render(); }),
    GBtn("⧉ ทำสำเนา",ColorBrush("#3B0764"),()=>{ GteFlush(); var c=Storage.Json.Deserialize<Dictionary<string,object>>(Storage.Json.Serialize(gtePresets[gteIdx])); c["name"]=GteData.Str(c,"name","Preset")+" (copy)"; gtePresets.Add(c); GteSelect(gtePresets.Count-1); Dirty(); Render(); }),
    GBtn("ลบ",ColorBrush("#7F1D1D"),()=>{ if(gtePresets.Count<=1){ status.Text=AppLanguage.T("ต้องมีอย่างน้อย 1 Preset"); return; } if(MessageBox.Show(AppLanguage.T("ลบ Preset นี้?"),"Game To Earn",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return; gteRows=null; gtePresets.RemoveAt(gteIdx); GteSelect(Math.Min(gteIdx,gtePresets.Count-1)); Dirty(); Render(); }));
   pre.Children.Add(bar);
   pre.Children.Add(GRow(GLabel("ชื่อ Preset"),GInput(GteData.Str(p,"name",""),300,v=>p["name"]=v)));
   pre.Children.Add(GRow(GCheck("ติดตาม (Follow) → กดคีย์",GteData.Bool(p,"follow_enabled",true),v=>p["follow_enabled"]=v),
    GInput(GteData.Str(p,"follow_seq","r"),90,v=>p["follow_seq"]=v.Trim()),
    GCheck("นับครั้งเดียวต่อคน",GteData.Bool(p,"follow_once",true),v=>p["follow_once"]=v)));
   pre.Children.Add(GRow(GCheck("ถูกใจ (Like) → กดคีย์",GteData.Bool(p,"like_enabled",true),v=>p["like_enabled"]=v),
    GInput(GteData.Str(p,"like_seq","v"),90,v=>p["like_seq"]=v.Trim()),
    GLabel("ทุก ๆ"),
    GInput(GteData.Int(p,"like_target",100).ToString(),80,v=>{ int n; if(int.TryParse(v,out n)&&n>0) p["like_target"]=n; }),
    GLabel("ถูกใจ")));
   sections.Children.Add(Box(pre));

   // gift map
   var gifts=new StackPanel();
   gifts.Children.Add(Ui.Bold(Line(AppLanguage.T("ของขวัญ → คีย์ที่กด")+"  ("+gteRows.Count+")",17)));
   var hint=Line(AppLanguage.T("คีย์พิมพ์ต่อกันได้ เช่น yy = กด y สองครั้ง • ติ๊ก Win เพื่อให้ของขวัญนี้นับ Win (+/-)"),12,muted); hint.Margin=new Thickness(0,4,0,8); gifts.Children.Add(hint);
   var hdr=new Grid { Margin=new Thickness(0,0,0,2) }; foreach(var w in new[]{300.0,120,90,80,50}) hdr.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(w) });
   var heads=new[]{"ชื่อของขวัญ","คีย์ที่กด","Win","Win +/-",""}; for(int i=0;i<heads.Length;i++){ var t=Line(AppLanguage.T(heads[i]),12,muted); Grid.SetColumn(t,i); hdr.Children.Add(t); }
   gifts.Children.Add(hdr);
   foreach(var row in gteRows.ToList()) {
    var r=row; var g=new Grid { Margin=new Thickness(0,3,0,3) }; foreach(var w in new[]{300.0,120,90,80,50}) g.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(w) });
    var name=new ComboBox { IsEditable=true,IsTextSearchEnabled=true,ItemsSource=GteData.Gifts,Text=r.name,Margin=new Thickness(0,0,10,0),VerticalAlignment=VerticalAlignment.Center };
    name.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,new TextChangedEventHandler((s,e)=>{ r.name=GteData.GiftName(name.Text); Dirty(); }));
    name.SelectionChanged+=(s,e)=>{ if(name.SelectedItem!=null){ r.name=GteData.GiftName(name.SelectedItem.ToString()); Dirty(); } };
    g.Children.Add(name);
    var key=GInput(r.key,105,v=>r.key=v); Grid.SetColumn(key,1); g.Children.Add(key);
    var win=GCheck("Win",r.win,v=>r.win=v); Grid.SetColumn(win,2); g.Children.Add(win);
    var delta=GInput(r.delta.ToString(),60,v=>{ int n; if(int.TryParse(v,out n)) r.delta=n; }); Grid.SetColumn(delta,3); g.Children.Add(delta);
    var del=GBtn("✕",ColorBrush("#4A0000"),()=>{ gteRows.Remove(r); Dirty(); Render(); }); Grid.SetColumn(del,4); g.Children.Add(del);
    gifts.Children.Add(g);
   }
   var add=GBtn("+ เพิ่มของขวัญ",ColorBrush("#166534"),()=>{ gteRows.Add(new GteRow()); Dirty(); Render(); }); add.Margin=new Thickness(0,10,0,0); add.HorizontalAlignment=HorizontalAlignment.Left;
   gifts.Children.Add(add);
   sections.Children.Add(Box(gifts));

   var how=new StackPanel();
   how.Children.Add(Ui.Bold(Line(AppLanguage.T("วิธีใช้"),17)));
   var steps=new[]{
    "1. ตั้งค่าในหน้านี้แล้วกด บันทึกการตั้งค่า (ไม่ต้องเปิด Roblox)",
    "2. กด เปิด Game To Earn หรือเปิดแมพ Roblox จากหน้าแรก (จะเปิดตามให้อัตโนมัติ)",
    "3. ใส่ชื่อ TikTok (@username) ที่กำลังไลฟ์ แล้วกด เชื่อมต่อ",
    "4. คลิกหน้าต่างเกมให้ Active ก่อน เพื่อให้รับคีย์ได้ชัวร์",
    "5. หน้าจอซ้อนสำหรับ OBS และค่า Win Counter แบบละเอียด ตั้งได้ในตัวโปรแกรม Game To Earn"};
   foreach(var t in steps) { var l=Line(AppLanguage.T(t),14,muted); l.Margin=new Thickness(0,7,0,0); l.TextWrapping=TextWrapping.Wrap; how.Children.Add(l); }
   sections.Children.Add(Box(how));
  }
 }
}
