using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PTVgame {
 public class Game { public string id,name,description,manifestUrl,category; public string placeId,cover; }
 public class Manifest { public string version,url,sha256,executable; public long size; }
 public class Installed { public string version,folder,executable; }
 public class Settings { public string theme="Midnight"; public bool autoUpdate=true; public List<string> favorites=new List<string>(); }
 public static class Storage {
  public static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PTVgame");
  public static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
  public static T Read<T>(string path,T fallback) { try { return Json.Deserialize<T>(File.ReadAllText(path)); } catch { return fallback; } }
  public static void Save(string path,object value) { Directory.CreateDirectory(Path.GetDirectoryName(path)); string temp=path+".tmp"; File.WriteAllText(temp,Json.Serialize(value),Encoding.UTF8); if(File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path); }
  public static string GameRoot(Game g) { if(string.IsNullOrWhiteSpace(g.id)||g.id.Any(c=>!char.IsLetterOrDigit(c)&&c!='-'&&c!='_')) throw new Exception("Game ID ไม่ถูกต้อง"); return Path.Combine(Root,"games",g.id); }
  public static Installed Current(Game g) { return Read<Installed>(Path.Combine(GameRoot(g),"installed.json"),null); }
 }
 public class Updater {
  static readonly HttpClient Http=CreateClient();
  static HttpClient CreateClient() { ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12; var h=new HttpClient(); h.Timeout=TimeSpan.FromMinutes(30); h.DefaultRequestHeaders.UserAgent.ParseAdd("PTVgame/1.0"); return h; }
  public static void Https(string url) { Uri u; if(!Uri.TryCreate(url,UriKind.Absolute,out u)||u.Scheme!="https") throw new Exception("ต้องใช้ URL แบบ HTTPS"); }
  public static async Task<Manifest> Check(Game g,CancellationToken token) {
   Https(g.manifestUrl);
   using(var r=await Http.GetAsync(g.manifestUrl,HttpCompletionOption.ResponseHeadersRead,token)) {
    r.EnsureSuccessStatusCode(); Https(r.RequestMessage.RequestUri.ToString());
    using(var s=await r.Content.ReadAsStreamAsync()) using(var m=new MemoryStream()) {
     var b=new byte[4096]; int n; while((n=await s.ReadAsync(b,0,b.Length,token))>0) { if(m.Length+n>65536) throw new Exception("Manifest มีขนาดใหญ่เกินไป"); m.Write(b,0,n); }
     var v=Storage.Json.Deserialize<Manifest>(Encoding.UTF8.GetString(m.ToArray()).TrimStart('\uFEFF')); Validate(v); return v;
    }
   }
  }
  public static void Validate(Manifest m) {
   Version v; if(m==null||!Version.TryParse(m.version,out v)) throw new Exception("เวอร์ชันต้องเป็นตัวเลข เช่น 1.0.0");
   Https(m.url); if(m.size<=0||m.size>20L*1024*1024*1024) throw new Exception("ขนาด ZIP ใน manifest ไม่ถูกต้อง (สูงสุด 20 GB)");
   if(m.sha256==null||m.sha256.Length!=64||m.sha256.Any(c=>!Uri.IsHexDigit(c))) throw new Exception("ต้องระบุ SHA-256 ของ ZIP");
   if(string.IsNullOrWhiteSpace(m.executable)||Path.IsPathRooted(m.executable)||m.executable.Contains(":")) throw new Exception("ตำแหน่ง executable ไม่ถูกต้อง");
   SafePath(Path.GetTempPath(),m.executable); if(!m.executable.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)) throw new Exception("ต้องระบุไฟล์ .exe");
  }
  public static string SafePath(string root,string entry) {
   string full=Path.GetFullPath(Path.Combine(root,entry.Replace('/',Path.DirectorySeparatorChar)));
   string prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
   if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||entry.Contains(":")) throw new Exception("ZIP มีตำแหน่งไฟล์ที่ไม่ปลอดภัย"); return full;
  }
  public static bool Newer(Manifest m,Installed i) { return i==null||new Version(m.version)>new Version(i.version); }
  public static async Task Install(Game g,Manifest m,IProgress<string> progress,CancellationToken token) {
   Validate(m); string root=Storage.GameRoot(g); Directory.CreateDirectory(root);
   using(var gate=new FileStream(Path.Combine(root,"update.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
    string stage=Path.Combine(root,"stage-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
    string zip=Path.Combine(stage,"download.zip"),payload=Path.Combine(stage,"payload"); Directory.CreateDirectory(payload);
    try {
     var drive=new DriveInfo(Path.GetPathRoot(root)); if(drive.AvailableFreeSpace<m.size*3) throw new Exception("พื้นที่ว่างไม่เพียงพอ");
     using(var r=await Http.GetAsync(m.url,HttpCompletionOption.ResponseHeadersRead,token)) {
      r.EnsureSuccessStatusCode(); Https(r.RequestMessage.RequestUri.ToString());
      if(r.Content.Headers.ContentLength.HasValue&&r.Content.Headers.ContentLength.Value!=m.size) throw new Exception("ขนาดไฟล์บนเซิร์ฟเวอร์ไม่ตรงกับ manifest");
      using(var input=await r.Content.ReadAsStreamAsync()) using(var output=File.Create(zip)) {
       byte[] buffer=new byte[81920]; int n; long done=0;
       while((n=await input.ReadAsync(buffer,0,buffer.Length,token))>0) { done+=n; if(done>m.size) throw new Exception("ไฟล์ดาวน์โหลดมีขนาดเกินกำหนด"); await output.WriteAsync(buffer,0,n,token); progress.Report("กำลังดาวน์โหลด  "+(done*100/m.size)+"%  •  "+(done/1048576)+" MB"); }
       if(done!=m.size) throw new Exception("ไฟล์ดาวน์โหลดไม่ครบ");
      }
     }
     progress.Report("กำลังตรวจสอบ SHA-256…");
     string hash=await Task.Run(()=> { using(var sha=SHA256.Create()) using(var f=File.OpenRead(zip)) return BitConverter.ToString(sha.ComputeHash(f)).Replace("-",""); },token);
     if(!hash.Equals(m.sha256,StringComparison.OrdinalIgnoreCase)) throw new Exception("SHA-256 ไม่ตรงกัน ยกเลิกการติดตั้ง");
     progress.Report("กำลังติดตั้ง…");
     await Task.Run(()=>Extract(zip,payload,token),token);
     string exe=SafePath(payload,m.executable); if(!File.Exists(exe)) throw new Exception("ไม่พบไฟล์เกม: "+m.executable);
     token.ThrowIfCancellationRequested();
     string dest=Path.Combine(root,"build-"+m.version+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
     Directory.Move(payload,dest);
     Storage.Save(Path.Combine(root,"installed.json"),new Installed { version=m.version,folder=dest,executable=m.executable });
     progress.Report("ติดตั้งสำเร็จ • v"+m.version);
    } finally { try { Directory.Delete(stage,true); } catch {} }
   }
  }
  public static void Extract(string zip,string payload,CancellationToken token) {
   using(var archive=ZipFile.OpenRead(zip)) {
    if(archive.Entries.Count>100000) throw new Exception("ZIP มีไฟล์มากเกินไป"); long total=0;
    foreach(var entry in archive.Entries) {
     token.ThrowIfCancellationRequested(); total+=entry.Length; if(total>40L*1024*1024*1024) throw new Exception("ZIP แตกไฟล์เกิน 40 GB");
     string target=SafePath(payload,entry.FullName);
     if((entry.ExternalAttributes>>16&0xF000)==0xA000) throw new Exception("ไม่รองรับ symbolic link ใน ZIP");
     if(string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
     Directory.CreateDirectory(Path.GetDirectoryName(target));
     using(var input=entry.Open()) using(var output=new FileStream(target,FileMode.CreateNew)) {
      byte[] b=new byte[81920]; int n; while((n=input.Read(b,0,b.Length))>0) { token.ThrowIfCancellationRequested(); output.Write(b,0,n); }
     }
    }
   }
  }
  public static void Play(Game g) {
   var i=Storage.Current(g); if(i==null) throw new Exception("ยังไม่ได้ติดตั้งเกม");
   string root=Storage.GameRoot(g)+Path.DirectorySeparatorChar;
   if(!Path.GetFullPath(i.folder).StartsWith(root,StringComparison.OrdinalIgnoreCase)) throw new Exception("ตำแหน่งติดตั้งไม่ถูกต้อง");
   string exe=SafePath(i.folder,i.executable); if(!File.Exists(exe)) throw new Exception("ไม่พบไฟล์เกม กรุณาติดตั้งใหม่");
   Process.Start(new ProcessStartInfo(exe) { WorkingDirectory=Path.GetDirectoryName(exe),UseShellExecute=false });
  }
 }
 public class Launcher : Window {
  Settings settings; List<Game> games; WrapPanel cards; TextBlock status; TextBox search; string page="หน้าแรก"; CancellationTokenSource cts; bool busy; ComboBox themes;
  Brush accent=new SolidColorBrush(Color.FromRgb(39,137,255));
  string SettingsPath { get { return Path.Combine(Storage.Root,"settings.json"); } }
  public Launcher() {
   settings=Storage.Read(SettingsPath,new Settings()); if(settings.favorites==null) settings.favorites=new List<string>();
   games=Storage.Read(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"catalog.json"),new List<Game>());
   Title="PTVgame • Game Launcher"; Width=1180; Height=780; MinWidth=860; MinHeight=620; WindowStartupLocation=WindowStartupLocation.CenterScreen;
   FontFamily=new FontFamily("Segoe UI"); Foreground=Brushes.White; Background=new SolidColorBrush(Color.FromRgb(7,16,31));
   Resources.Add(typeof(Button),System.Windows.Markup.XamlReader.Parse("<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border Background='{TemplateBinding Background}' CornerRadius='6' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter Property='Opacity' Value='0.8'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>"));
   var layout=new Grid(); layout.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(204) }); layout.ColumnDefinitions.Add(new ColumnDefinition()); Content=layout;
   var side=new StackPanel { Background=new SolidColorBrush(Color.FromRgb(5,13,25)) }; layout.Children.Add(side);
   side.Children.Add(new TextBlock { Text="◈  PTVgame",FontSize=27,FontWeight=FontWeights.Bold,Margin=new Thickness(22,28,0,36),Foreground=accent });
   foreach(string label in new[]{"หน้าแรก","ไลบรารี","รายการโปรด","การตั้งค่า"}) { string p=label; var b=Button(label); b.Margin=new Thickness(12,4,12,4); b.Click+=(s,e)=>{ page=p; Render(); }; side.Children.Add(b); }
   side.Children.Add(new TextBlock { Text="WINDOWS LAUNCHER\n\nเกมของคุณ พร้อมเล่นเสมอ",Margin=new Thickness(24,40,12,0),Foreground=Brushes.SlateGray,FontSize=12 });
   var main=new DockPanel { Margin=new Thickness(28,20,28,20) }; Grid.SetColumn(main,1); layout.Children.Add(main);
   var top=new DockPanel { Margin=new Thickness(0,0,0,24) }; DockPanel.SetDock(top,Dock.Top); main.Children.Add(top);
   var title=new TextBlock { Text="ค้นพบเกมที่คุณรัก",FontSize=23,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center }; top.Children.Add(title);
   search=new TextBox { Width=230,Margin=new Thickness(25,0,0,0),Padding=new Thickness(12,9,12,9),Background=new SolidColorBrush(Color.FromRgb(18,34,54)),Foreground=Brushes.White,BorderBrush=Brushes.SlateGray,ToolTip="ค้นหาเกม" }; top.Children.Add(search); search.TextChanged+=(s,e)=>Render();
   status=new TextBlock { Text="พร้อมใช้งาน • เลือกเกมเพื่อติดตั้งหรือเล่น",Foreground=Brushes.LightSteelBlue,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,0) }; DockPanel.SetDock(status,Dock.Bottom); main.Children.Add(status);
   var scroll=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto }; main.Children.Add(scroll); cards=new WrapPanel(); scroll.Content=cards;
   ApplyTheme(); Render(); Loaded+=async(s,e)=> { if(settings.autoUpdate) await AutoUpdate(); };
   Closing+=(s,e)=> { if(busy) { e.Cancel=true; cts.Cancel(); status.Text="กำลังยกเลิก กรุณารอแล้วปิดอีกครั้ง"; } };
  }
  Button Button(string text) { return new Button { Content=text,Padding=new Thickness(14,10,14,10),Margin=new Thickness(0,5,8,0),Background=new SolidColorBrush(Color.FromRgb(22,47,78)),Foreground=Brushes.White,BorderThickness=new Thickness(0),Cursor=System.Windows.Input.Cursors.Hand,HorizontalContentAlignment=HorizontalAlignment.Left }; }
  void ApplyTheme() { string t=settings.theme; Color c=t=="Ember"?Color.FromRgb(255,91,34):t=="Neon"?Color.FromRgb(187,66,255):t=="Frost"?Color.FromRgb(72,151,192):Color.FromRgb(39,137,255); accent=new SolidColorBrush(c); Background=new SolidColorBrush(t=="Ember"?Color.FromRgb(26,14,15):t=="Neon"?Color.FromRgb(17,8,35):t=="Frost"?Color.FromRgb(28,47,62):Color.FromRgb(7,16,31)); }
  void SaveSettings() { Storage.Save(SettingsPath,settings); }
  void Render() {
   if(cards==null) return; cards.Children.Clear();
   if(page=="การตั้งค่า") {
    var p=new StackPanel { Width=550 }; cards.Children.Add(p); p.Children.Add(new TextBlock { Text="การตั้งค่า",FontSize=28,Margin=new Thickness(0,0,0,24) });
    p.Children.Add(new TextBlock { Text="ธีมสี",Margin=new Thickness(0,0,0,8) }); themes=new ComboBox { ItemsSource=new[]{"Midnight","Ember","Frost","Neon"},SelectedItem=settings.theme,Width=230,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,24) }; p.Children.Add(themes);
    themes.SelectionChanged+=(s,e)=> { settings.theme=(string)themes.SelectedItem; SaveSettings(); ApplyTheme(); };
    var auto=new CheckBox { Content="อัปเดตเกมที่ติดตั้งโดยอัตโนมัติเมื่อเปิด Launcher",IsChecked=settings.autoUpdate,Foreground=Brushes.White }; p.Children.Add(auto); auto.Click+=(s,e)=> { settings.autoUpdate=auto.IsChecked==true; SaveSettings(); };
    var folder=Button("เปิดโฟลเดอร์เกมและการตั้งค่า"); p.Children.Add(folder); folder.Click+=(s,e)=> { Directory.CreateDirectory(Storage.Root); Process.Start("explorer.exe",Storage.Root); };
    p.Children.Add(new TextBlock { Text="ตั้งค่าเกมและ GitHub manifest ใน catalog.json ข้างโปรแกรม\nเกมเก่าเก็บไว้ในโฟลเดอร์ build เพื่อใช้กู้คืนได้\nระบบนี้อัปเดตตัวเกม ยังไม่อัปเดตตัว Launcher",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightSteelBlue,Margin=new Thickness(0,24,0,0) }); return;
   }
   var filtered=games.Where(g=> (g.name??"").IndexOf(search.Text,StringComparison.OrdinalIgnoreCase)>=0);
   if(page=="ไลบรารี") filtered=filtered.Where(g=>Storage.Current(g)!=null); if(page=="รายการโปรด") filtered=filtered.Where(g=>settings.favorites.Contains(g.id));
   foreach(var g in filtered) AddCard(g);
   if(cards.Children.Count==0) cards.Children.Add(new TextBlock { Text="ยังไม่มีเกมในหน้านี้",FontSize=22,Foreground=Brushes.LightSteelBlue,Margin=new Thickness(8,40,0,0) });
  }
  void AddCard(Game g) {
   var box=new Border { Width=264,Margin=new Thickness(0,0,18,20),CornerRadius=new CornerRadius(14),Background=new SolidColorBrush(Color.FromRgb(16,32,51)),BorderBrush=new SolidColorBrush(Color.FromRgb(35,58,82)),BorderThickness=new Thickness(1),ClipToBounds=true };
   var p=new StackPanel(); box.Child=p; cards.Children.Add(box);
   var hero=new Grid { Height=145,Background=new LinearGradientBrush(((SolidColorBrush)accent).Color,Color.FromRgb(7,16,31),45) }; p.Children.Add(hero);
   hero.Children.Add(new TextBlock { Text=g.category=="Roblox"?"▧":"◈",FontSize=86,FontWeight=FontWeights.Light,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Opacity=.8 });
   var detail=new StackPanel { Margin=new Thickness(16) }; p.Children.Add(detail);
   detail.Children.Add(new TextBlock { Text=g.name,FontSize=22,FontWeight=FontWeights.Bold });
   detail.Children.Add(new TextBlock { Text=g.description,Height=52,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightSteelBlue,Margin=new Thickness(0,6,0,8) });
   var installed=Storage.Current(g); detail.Children.Add(new TextBlock { Text=g.category=="Roblox"?"ROBLOX • เปิดผ่านแอป Roblox":installed==null?"ยังไม่ได้ติดตั้ง":"ติดตั้งแล้ว • v"+installed.version,FontSize=12,Foreground=Brushes.LightSteelBlue });
   var action=Button(g.category=="Roblox"?"▶  เปิด Roblox":installed==null?"↓  ติดตั้งเกม":"▶  เล่นเกม"); action.Background=accent; detail.Children.Add(action);
   action.IsEnabled=!busy&&(g.category=="Roblox"?!string.IsNullOrWhiteSpace(g.placeId):installed!=null||!string.IsNullOrWhiteSpace(g.manifestUrl));
   action.Click+=async(s,e)=> { if(g.category=="Roblox") { long id; if(!long.TryParse(g.placeId,out id)||id<=0) { status.Text="กรุณาระบุ placeId ใน catalog.json"; return; } try { Process.Start("roblox://placeId="+id); } catch(Exception ex) { status.Text=ex.Message; } } else if(Storage.Current(g)!=null) { try { Updater.Play(g); status.Text="เปิด "+g.name+" แล้ว"; } catch(Exception ex) { status.Text=ex.Message; } } else await Update(g,false); };
   var row=new StackPanel { Orientation=Orientation.Horizontal }; detail.Children.Add(row);
   var update=Button("ตรวจอัปเดต"); update.FontSize=11; update.IsEnabled=!busy&&!string.IsNullOrWhiteSpace(g.manifestUrl)&&g.category!="Roblox"; row.Children.Add(update); update.Click+=async(s,e)=>await Update(g,true);
   var fav=Button(settings.favorites.Contains(g.id)?"♥":"♡"); row.Children.Add(fav); fav.Click+=(s,e)=> { if(!settings.favorites.Remove(g.id)) settings.favorites.Add(g.id); SaveSettings(); Render(); };
   if(g.category!="Roblox"&&string.IsNullOrWhiteSpace(g.manifestUrl)) detail.Children.Add(new TextBlock { Text="รอตั้งค่า GitHub manifest",Foreground=Brushes.SlateGray,FontSize=11,Margin=new Thickness(0,8,0,0) });
  }
  async Task Update(Game g,bool checkOnly) {
   if(busy) return; busy=true; cts=new CancellationTokenSource(); Render();
   var cancel=Button("ยกเลิกดาวน์โหลด"); cards.Children.Insert(0,cancel); cancel.Click+=(s,e)=>cts.Cancel();
   try {
    status.Text="กำลังตรวจเวอร์ชัน "+g.name+"…"; var m=await Updater.Check(g,cts.Token); var i=Storage.Current(g);
    if(!Updater.Newer(m,i)) { status.Text=g.name+" เป็นเวอร์ชันล่าสุดแล้ว"; return; }
    if(checkOnly&&MessageBox.Show("ติดตั้ง "+g.name+" เวอร์ชัน "+m.version+" ?","PTVgame",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return;
    await Updater.Install(g,m,new Progress<string>(t=>status.Text=t),cts.Token);
   } catch(OperationCanceledException) { status.Text="ยกเลิกแล้ว • เวอร์ชันเดิมยังใช้งานได้"; } catch(Exception ex) { status.Text="อัปเดตไม่สำเร็จ: "+ex.Message; } finally { busy=false; cts.Dispose(); cts=null; Render(); }
  }
  async Task AutoUpdate() { foreach(var g in games.Where(g=>g.category!="Roblox"&&!string.IsNullOrWhiteSpace(g.manifestUrl)&&Storage.Current(g)!=null)) await Update(g,false); }
  [STAThread] public static void Main(string[] args) { if(args.Contains("--self-test")) { try { Tests.Run(); } catch(Exception ex) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-results.txt"),ex.ToString()); Environment.ExitCode=1; } return; } var app=new Application(); app.DispatcherUnhandledException+=(s,e)=> { MessageBox.Show(e.Exception.Message,"PTVgame"); e.Handled=true; }; app.Run(new LauncherShell()); }
 }
 public static class Tests {
  static void Assert(bool b,string name) { if(!b) throw new Exception("FAIL: "+name); }
  public static void Run() {
   string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"PTV-test-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
   try {
    Assert(Updater.SafePath(root,"Game/Game.exe").StartsWith(root),"valid path");
    foreach(string bad in new[]{"../escape.exe","/escape.exe","C:\\escape.exe","file.exe:stream"}) { bool blocked=false; try { Updater.SafePath(root,bad); } catch { blocked=true; } Assert(blocked,"block "+bad); }
    var m=new Manifest { version="1.2.0",url="https://example.org/game.zip",sha256=new string('a',64),size=100,executable="Game.exe" }; Updater.Validate(m);
    Assert(Updater.Newer(m,new Installed { version="1.1.0" }),"new version"); Assert(!Updater.Newer(m,new Installed { version="1.3.0" }),"prevent downgrade");
    string zip=Path.Combine(root,"bad.zip"); using(var a=ZipFile.Open(zip,ZipArchiveMode.Create)) { using(var w=new StreamWriter(a.CreateEntry("../escape.txt").Open())) w.Write("bad"); }
    bool rejected=false; try { Updater.Extract(zip,Path.Combine(root,"out"),CancellationToken.None); } catch { rejected=true; } Assert(rejected,"zip traversal");
    string good=Path.Combine(root,"good.zip"); using(var a=ZipFile.Open(good,ZipArchiveMode.Create)) { using(var w=new StreamWriter(a.CreateEntry("Game/Data.txt").Open())) w.Write("ok"); }
    Updater.Extract(good,Path.Combine(root,"ok"),CancellationToken.None); Assert(File.ReadAllText(Path.Combine(root,"ok","Game","Data.txt"))=="ok","valid extraction");
    var file=Path.Combine(root,"state.json"); Storage.Save(file,new Installed { version="1.0.0" }); Storage.Save(file,new Installed { version="2.0.0" }); Assert(Storage.Read<Installed>(file,null).version=="2.0.0","atomic state");
    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-results.txt"),"PASS: safe paths, ZIP traversal rejection, extraction, versions, downgrade prevention, manifest validation, atomic state writes.");
   } finally { Directory.Delete(root,true); }
  }
 }
}

