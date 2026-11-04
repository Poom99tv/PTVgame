// PTVgame – Game To Earn, built into the Launcher (C# 5 / WPF).
//  • TikTok LIVE connection + gift recognition by picture run in a hidden Node engine
//    (assets/gametoearn-engine.zip, same image matching as TikTokBridge).
//  • Presets, key presses (SendInput scan codes, works with Roblox), chat voice and the OBS overlays
//    are controlled from the Launcher page: เชื่อมต่อ LIVE / Preset และคำสั่ง / หน้าจอ OBS / บันทึกเหตุการณ์.
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PTVgame {
 // ───────────────────────── files: presets / state / gift catalog ─────────────────────────
 public static class GteData {
  public static string Dir { get { return Path.Combine(Storage.Root,"GameToEarn"); } }
  public static string PresetsFile { get { return Path.Combine(Dir,"presets.json"); } }
  public static string StateFile { get { return Path.Combine(Dir,"state.json"); } }
  public static string AssetsDir { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets"); } }
  public static string GiftsDir { get { var a=Path.Combine(AssetsDir,"gifts"); if(Directory.Exists(a)) return a; var b=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"gifts"); return Directory.Exists(b)?b:a; } }
  static Dictionary<string,object> bundled;
  static Dictionary<string,object> Bundled { get {
   if(bundled==null) bundled=Storage.Read<Dictionary<string,object>>(Path.Combine(AssetsDir,"gametoearn-data.json"),null)??new Dictionary<string,object>();
   return bundled; } }
  static List<Dictionary<string,object>> Copy(object o) { try { return Storage.Json.Deserialize<List<Dictionary<string,object>>>(Storage.Json.Serialize(o)); } catch { return null; } }
  public static Dictionary<string,object> NewPreset(string name) {
   return new Dictionary<string,object>{{"name",name},{"gift_map",new Dictionary<string,object>()},{"follow_enabled",true},{"follow_seq","r"},{"follow_once",true},{"like_enabled",true},{"like_seq","v"},{"like_target",100},{"key_delay_ms",130}};
  }
  public static List<Dictionary<string,object>> LoadPresets() {
   var list=Storage.Read<List<Dictionary<string,object>>>(PresetsFile,null);
   if(list!=null&&list.Count>0) return list;
   object d; list=Bundled.TryGetValue("defaultPresets",out d)?Copy(d):null;
   return list!=null&&list.Count>0?list:new List<Dictionary<string,object>>{ NewPreset("Preset 1") };
  }
  public static Dictionary<string,object> LoadState() { return Storage.Read<Dictionary<string,object>>(StateFile,null)??new Dictionary<string,object>(); }
  public static void Save(List<Dictionary<string,object>> presets,Dictionary<string,object> state) { Directory.CreateDirectory(Dir); Storage.Save(PresetsFile,presets); Storage.Save(StateFile,state); }
  public class Gift { public string key,name,file; public int price; }
  static List<Gift> gifts;
  public static List<Gift> Gifts { get {
   if(gifts==null) {
    gifts=new List<Gift>(); object g;
    if(Bundled.TryGetValue("gifts",out g)&&g is IEnumerable) foreach(var item in (IEnumerable)g) {
     var d=item as Dictionary<string,object>; if(d==null) continue;
     gifts.Add(new Gift { key=Str(d,"key",""),name=Str(d,"name",""),file=Str(d,"file",""),price=Int(d,"price",0) });
    }
   }
   return gifts; } }
  public static string Norm(string s) { if(s==null) return ""; var sb=new StringBuilder(); foreach(char c in s.ToLowerInvariant()) if(char.IsLetterOrDigit(c)) sb.Append(c); return sb.ToString(); }
  public static Gift FindGift(string name) { var n=Norm(name); return n==""?null:Gifts.FirstOrDefault(x=>Norm(x.name)==n); }
  static readonly Dictionary<string,BitmapImage> images=new Dictionary<string,BitmapImage>();
  public static BitmapImage Image(string file) {
   if(string.IsNullOrEmpty(file)) return null; BitmapImage b;
   if(images.TryGetValue(file,out b)) return b;
   try { var path=Path.Combine(GiftsDir,file); if(!File.Exists(path)||Path.GetFileName(file)!=file) return null; b=new BitmapImage(); b.BeginInit(); b.CacheOption=BitmapCacheOption.OnLoad; b.DecodePixelWidth=64; b.UriSource=new Uri(path); b.EndInit(); b.Freeze(); } catch { b=null; }
   images[file]=b; return b;
  }
  public static string Str(Dictionary<string,object> d,string key,string fallback) { object v; return d!=null&&d.TryGetValue(key,out v)&&v!=null?v.ToString():fallback; }
  public static bool Bool(Dictionary<string,object> d,string key,bool fallback) { object v; if(d==null||!d.TryGetValue(key,out v)||v==null) return fallback; if(v is bool) return (bool)v; bool b; return bool.TryParse(v.ToString(),out b)?b:fallback; }
  public static int Int(Dictionary<string,object> d,string key,int fallback) { object v; decimal n; return d!=null&&d.TryGetValue(key,out v)&&v!=null&&decimal.TryParse(v.ToString(),System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.InvariantCulture,out n)?(int)n:fallback; }
  public static Dictionary<string,object> Sub(Dictionary<string,object> d,string key) { object v; var r=d!=null&&d.TryGetValue(key,out v)?v as Dictionary<string,object>:null; if(r==null&&d!=null) { r=new Dictionary<string,object>(); d[key]=r; } return r; }
 }

 // ───────────────────────── hidden engine (node + engine.mjs) ─────────────────────────
 public static class GteEngine {
  const string NodeVersion="24.22.2";
  public static Process Proc; public static int Port; public static string Token=""; public static string LastError="";
  static HttpClient http; static Task startTask; static bool exitHooked;
  static readonly Queue<string> output=new Queue<string>();
  public static bool Running { get { try { return Proc!=null&&!Proc.HasExited&&http!=null; } catch { return false; } } }
  static string Base { get { return AppDomain.CurrentDomain.BaseDirectory; } }

  public static Task Start(string token,int port,IProgress<string> progress) {
   if(Running) return Task.FromResult(0);
   if(startTask==null||startTask.IsCompleted) startTask=StartCore(token,port,progress);
   return startTask;
  }
  static async Task StartCore(string token,int port,IProgress<string> progress) {
   if(!exitHooked&&Application.Current!=null) { exitHooked=true; Application.Current.Exit+=(s,e)=>Kill(); }
   progress.Report("กำลังเตรียมระบบ Game To Earn…");
   string dir=await Task.Run(()=>EngineDir());
   string node=await NodePath(dir,progress);
   Port=FreePort(port); Token=token;
   var psi=new ProcessStartInfo(node,"\""+Path.Combine(dir,"engine.mjs")+"\"") { WorkingDirectory=dir,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
   psi.EnvironmentVariables["GTE_PORT"]=Port.ToString(); psi.EnvironmentVariables["GTE_TOKEN"]=Token;
   psi.EnvironmentVariables["GTE_GIFTS"]=GteData.GiftsDir; psi.EnvironmentVariables["GTE_DATA"]=GteData.Dir;
   psi.EnvironmentVariables["GTE_PARENT"]=Process.GetCurrentProcess().Id.ToString();
   Directory.CreateDirectory(GteData.Dir);
   Proc=Process.Start(psi);
   Proc.OutputDataReceived+=(s,e)=>Remember(e.Data); Proc.ErrorDataReceived+=(s,e)=>Remember(e.Data);
   Proc.BeginOutputReadLine(); Proc.BeginErrorReadLine();
   var client=new HttpClient(); client.Timeout=TimeSpan.FromSeconds(20); client.BaseAddress=new Uri("http://127.0.0.1:"+Port+"/");
   client.DefaultRequestHeaders.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",Token);
   for(int i=0;i<60;i++) {
    if(Proc.HasExited) throw new Exception(AppLanguage.T("ระบบ Game To Earn หยุดทำงาน: ")+Tail());
    try { var r=await client.GetAsync("health"); if(r.IsSuccessStatusCode) { http=client; return; } } catch {}
    await Task.Delay(250);
   }
   Kill(); throw new Exception(AppLanguage.T("ระบบ Game To Earn ไม่ตอบสนอง ")+Tail());
  }
  static void Remember(string line) { if(string.IsNullOrEmpty(line)) return; lock(output) { output.Enqueue(line); while(output.Count>20) output.Dequeue(); } }
  static string Tail() { lock(output) return string.Join(" ",output.Reverse().Take(3).Reverse()); }
  public static void Kill() { try { if(Proc!=null&&!Proc.HasExited) Proc.Kill(); } catch {} http=null; }
  static int FreePort(int preferred) {
   for(int p=preferred;p<preferred+30;p++) { try { var l=new TcpListener(IPAddress.Loopback,p); l.Start(); l.Stop(); return p; } catch {} }
   var any=new TcpListener(IPAddress.Loopback,0); any.Start(); int port=((IPEndPoint)any.LocalEndpoint).Port; any.Stop(); return port;
  }

  // engine scripts: <Launcher>\GameToEarn\engine.mjs (developer copy) or assets\gametoearn-engine.zip extracted to %LOCALAPPDATA%\PTVgame\GameToEarn\engine
  static string EngineDir() {
   var dev=Path.Combine(Base,"GameToEarn"); if(File.Exists(Path.Combine(dev,"engine.mjs"))&&Directory.Exists(Path.Combine(dev,"node_modules"))) return dev;
   var zip=Path.Combine(GteData.AssetsDir,"gametoearn-engine.zip"); var target=Path.Combine(GteData.Dir,"engine");
   if(!File.Exists(zip)) { if(File.Exists(Path.Combine(target,"engine.mjs"))) return target; throw new Exception(AppLanguage.T("ไม่พบไฟล์ระบบ Game To Earn (assets\\gametoearn-engine.zip)")); }
   string version; using(var z=ZipFile.OpenRead(zip)) { var v=z.GetEntry("version.txt"); version=v==null?"":new StreamReader(v.Open()).ReadToEnd().Trim(); }
   var marker=Path.Combine(target,"version.txt");
   if(File.Exists(Path.Combine(target,"engine.mjs"))&&File.Exists(marker)&&File.ReadAllText(marker).Trim()==version&&version!="") return target;
   var temp=target+".new"; if(Directory.Exists(temp)) Directory.Delete(temp,true); Directory.CreateDirectory(temp);
   string root=Path.GetFullPath(temp)+Path.DirectorySeparatorChar;
   using(var z=ZipFile.OpenRead(zip)) foreach(var entry in z.Entries) {
    var dest=Path.GetFullPath(Path.Combine(temp,entry.FullName)); if(!dest.StartsWith(root,StringComparison.OrdinalIgnoreCase)) throw new Exception("Invalid engine package");
    if(string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(dest); continue; }
    Directory.CreateDirectory(Path.GetDirectoryName(dest)); entry.ExtractToFile(dest,true);
   }
   if(Directory.Exists(target)) Directory.Delete(target,true);
   Directory.Move(temp,target); return target;
  }
  static bool NodeOk(string path) {
   try {
    if(path==null||!File.Exists(path)) return false;
    var p=Process.Start(new ProcessStartInfo(path,"-v") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true });
    var text=p.StandardOutput.ReadToEnd(); p.WaitForExit(5000);
    var m=Regex.Match(text,@"v(\d+)\."); return m.Success&&int.Parse(m.Groups[1].Value)>=20;
   } catch { return false; }
  }
  static async Task<string> NodePath(string engineDir,IProgress<string> progress) {
   var local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
   var runtime=Path.Combine(Storage.Root,"runtime","node-v"+NodeVersion,"node.exe");
   var candidates=new List<string>{ Path.Combine(engineDir,"node.exe"),runtime,Path.Combine(local,"IslandRaceTikTokBridgeV7","node.exe") };
   foreach(var d in (Environment.GetEnvironmentVariable("PATH")??"").Split(';')) { try { if(d.Trim()!="") candidates.Add(Path.Combine(d.Trim().Trim('"'),"node.exe")); } catch {} }
   foreach(var c in candidates) if(await Task.Run(()=>NodeOk(c))) return c;
   // download the official Node.js runtime once and verify it against nodejs.org SHASUMS256.txt
   progress.Report("กำลังดาวน์โหลดตัวช่วย Node.js (ครั้งแรกครั้งเดียว ~30 MB)…");
   ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
   using(var h=new HttpClient()) {
    h.Timeout=TimeSpan.FromMinutes(15);
    string baseUrl="https://nodejs.org/dist/v"+NodeVersion+"/";
    var sums=await h.GetStringAsync(baseUrl+"SHASUMS256.txt");
    var m=Regex.Match(sums,@"(?m)^([a-f0-9]{64})\s+win-x64/node\.exe\s*$"); if(!m.Success) throw new Exception("Node.js checksum not found");
    Directory.CreateDirectory(Path.GetDirectoryName(runtime)); var temp=runtime+".download";
    using(var r=await h.GetAsync(baseUrl+"win-x64/node.exe",HttpCompletionOption.ResponseHeadersRead)) { r.EnsureSuccessStatusCode(); using(var s=await r.Content.ReadAsStreamAsync()) using(var f=File.Create(temp)) await s.CopyToAsync(f); }
    string hash; using(var sha=SHA256.Create()) using(var f=File.OpenRead(temp)) hash=BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant();
    if(hash!=m.Groups[1].Value) { File.Delete(temp); throw new Exception("Node.js checksum mismatch"); }
    if(File.Exists(runtime)) File.Delete(runtime); File.Move(temp,runtime);
   }
   return runtime;
  }
  public static async Task<Dictionary<string,object>> Get(string path) {
   var h=http; if(h==null) throw new Exception(AppLanguage.T("ระบบ Game To Earn ยังไม่พร้อม"));
   var text=await h.GetStringAsync(path); return Storage.Json.Deserialize<Dictionary<string,object>>(text);
  }
  public static async Task<Dictionary<string,object>> Post(string path,object body) {
   var h=http; if(h==null) throw new Exception(AppLanguage.T("ระบบ Game To Earn ยังไม่พร้อม"));
   var r=await h.PostAsync(path,new StringContent(Storage.Json.Serialize(body??new Dictionary<string,object>()),Encoding.UTF8,"application/json"));
   var text=await r.Content.ReadAsStringAsync(); var d=Storage.Json.Deserialize<Dictionary<string,object>>(text);
   if(!r.IsSuccessStatusCode) throw new Exception(GteData.Str(d,"error","HTTP "+(int)r.StatusCode));
   return d;
  }
  public static async Task<byte[]> Bytes(string path) { var h=http; if(h==null) return null; var r=await h.GetAsync(path); return r.IsSuccessStatusCode?await r.Content.ReadAsByteArrayAsync():null; }
  public static string OverlayUrl(string kind,string bg) { return "http://127.0.0.1:"+Port+"/overlay/"+kind+"?t="+Token+(string.IsNullOrEmpty(bg)?"":"&bg="+Uri.EscapeDataString(bg)); }
 }

 // ───────────────────────── key presses (scan codes, like pydirectinput) ─────────────────────────
 public static class GteKeys {
  [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx,dy; public uint mouseData,dwFlags,time; public IntPtr dwExtraInfo; }
  [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk,wScan; public uint dwFlags,time; public IntPtr dwExtraInfo; }
  [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
  [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion u; }
  [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint n,INPUT[] inputs,int size);
  [DllImport("user32.dll")] static extern short VkKeyScan(char c);
  [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code,uint type);
  const uint KEYUP=0x2,SCANCODE=0x8,EXTENDED=0x1;
  static readonly Dictionary<string,ushort> Named=new Dictionary<string,ushort>{
   {"up",0xE048},{"down",0xE050},{"left",0xE04B},{"right",0xE04D},{"space",0x39},{"enter",0x1C},{"tab",0x0F},{"esc",0x01},{"escape",0x01},{"backspace",0x0E},
   {"shift",0x2A},{"ctrl",0x1D},{"alt",0x38},{"f1",0x3B},{"f2",0x3C},{"f3",0x3D},{"f4",0x3E},{"f5",0x3F},{"f6",0x40},{"f7",0x41},{"f8",0x42},{"f9",0x43},{"f10",0x44},{"f11",0x57},{"f12",0x58}};
  const string Plain="abcdefghijklmnopqrstuvwxyz1234567890-=[];'`\\,./ ";
  const string Shifted="ABCDEFGHIJKLMNOPQRSTUVWXYZ!@#$%^&*()_+{}:\"~|<>? ";
  static readonly ushort[] Codes={0x1E,0x30,0x2E,0x20,0x12,0x21,0x22,0x23,0x17,0x24,0x25,0x26,0x32,0x31,0x18,0x19,0x10,0x13,0x1F,0x14,0x16,0x2F,0x11,0x2D,0x15,0x2C,
   0x02,0x03,0x04,0x05,0x06,0x07,0x08,0x09,0x0A,0x0B,0x0C,0x0D,0x1A,0x1B,0x27,0x28,0x29,0x2B,0x33,0x34,0x35,0x39};
  static bool Scan(char c,out ushort code,out bool shift) {
   int i=Plain.IndexOf(c); if(i>=0) { code=Codes[i]; shift=false; return true; }
   i=Shifted.IndexOf(c); if(i>=0) { code=Codes[i]; shift=c!=' '; return true; }
   short v=VkKey(c); code=0; shift=false; if(v==-1) return false; code=(ushort)MapVirtualKey((uint)(v&0xFF),0); shift=((v>>8)&1)==1; return code!=0;
  }
  class Job { public string seq; public int count,delay; }
  static readonly BlockingCollection<Job> jobs=new BlockingCollection<Job>(); static Thread worker;
  public static int Pending { get { return jobs.Count; } }
  public static void Enqueue(string seq,int count,int delayMs) {
   if(string.IsNullOrWhiteSpace(seq)||count<=0) return;
   if(worker==null) { worker=new Thread(Run) { IsBackground=true,Name="GTE keys" }; worker.Start(); }
   if(jobs.Count<500) jobs.Add(new Job { seq=seq.Trim(),count=Math.Min(count,200),delay=Math.Max(10,Math.Min(2000,delayMs)) });
  }
  public static void Clear() { Job j; while(jobs.TryTake(out j)) {} }
  // "yy" = y twice • "a b" or "a,b" = a then b • named keys: up down left right space enter tab esc f1..f12 (or {up})
  public static List<ushort[]> Parse(string seq) {
   var keys=new List<ushort[]>();
   foreach(var raw in Regex.Split(seq??"",@"[\s,]+")) {
    if(raw=="") continue; var t=raw.Trim('{','}').ToLowerInvariant();
    ushort sc; if(t.Length>1&&Named.TryGetValue(t,out sc)) { keys.Add(new ushort[]{sc,0}); continue; }
    foreach(char c in raw) { ushort scan; bool shift; if(Scan(c,out scan,out shift)) keys.Add(new ushort[]{scan,(ushort)(shift?1:0)}); }
   }
   return keys;
  }
  static short VkKey(char c) { try { return VkKeyScan(c); } catch { return -1; } }
  static void Send(ushort scan,bool up) {
   uint flags=SCANCODE|(up?KEYUP:0); if((scan&0xE000)==0xE000) flags|=EXTENDED;
   var input=new INPUT { type=1 }; input.u.ki=new KEYBDINPUT { wScan=(ushort)(scan&0xFF),dwFlags=flags };
   SendInput(1,new[]{input},Marshal.SizeOf(typeof(INPUT)));
  }
  static void Run() {
   foreach(var job in jobs.GetConsumingEnumerable()) {
    try {
     var keys=Parse(job.seq);
     for(int i=0;i<job.count;i++) foreach(var k in keys) {
      if(k[1]==1) Send(0x2A,false);
      Send(k[0],false); Thread.Sleep(45); Send(k[0],true);
      if(k[1]==1) Send(0x2A,true);
      Thread.Sleep(job.delay);
     }
    } catch {}
   }
  }
 }

 // ───────────────────────── chat voice (Edge neural voices via engine, SAPI fallback) ─────────────────────────
 public static class GteVoice {
  static readonly BlockingCollection<string> queue=new BlockingCollection<string>(); static Thread worker; public static string Voice="off";
  static DateTime last=DateTime.MinValue;
  public static void Say(string text,bool force) {
   if(Voice=="off"&&!force) return;
   text=Regex.Replace(text??"",@"https?://\S+|www\.\S+","").Trim(); if(text.Length>120) text=text.Substring(0,120);
   if(text=="") return;
   if(!force&&(DateTime.UtcNow-last).TotalSeconds<1.5) return; last=DateTime.UtcNow;
   if(worker==null) { worker=new Thread(Run) { IsBackground=true,Name="GTE voice" }; worker.SetApartmentState(ApartmentState.STA); worker.Start(); }
   if(queue.Count<5) queue.Add(text);
  }
  static void Run() {
   foreach(var text in queue.GetConsumingEnumerable()) {
    try {
     string voice=Voice=="off"?"th-TH-PremwadeeNeural":Voice;
     byte[] mp3=null; try { mp3=GteEngine.Bytes("tts?voice="+Uri.EscapeDataString(voice)+"&text="+Uri.EscapeDataString(text)).Result; } catch {}
     if(mp3!=null&&mp3.Length>100) {
      var file=Path.Combine(Path.GetTempPath(),"ptv-gte-tts.mp3"); File.WriteAllBytes(file,mp3);
      var done=new ManualResetEventSlim(false);
      Application.Current.Dispatcher.BeginInvoke(new Action(()=>{
       var p=new MediaPlayer(); p.MediaEnded+=(s,e)=>{ p.Close(); done.Set(); }; p.MediaFailed+=(s,e)=>{ p.Close(); done.Set(); };
       p.Open(new Uri(file)); p.Play();
      }));
      done.Wait(TimeSpan.FromSeconds(30));
     } else {
      var t=Type.GetTypeFromProgID("SAPI.SpVoice"); if(t==null) continue; var v=Activator.CreateInstance(t);
      t.InvokeMember("Speak",System.Reflection.BindingFlags.InvokeMethod,null,v,new object[]{text,0});
     }
    } catch {}
   }
  }
 }

 public class GteRow { public string name="",key=""; public bool win; public int delta=1; }
 public class GteLog { public DateTime at; public string text,color,cat; }

 // ───────────────────────── Launcher page ─────────────────────────
 public partial class LauncherShell {
  const string PageGte="Game To Earn";
  const string SubLive="เชื่อมต่อ LIVE",SubPreset="Preset และคำสั่ง",SubObs="หน้าจอ OBS",SubLog="บันทึกเหตุการณ์";
  static readonly string[][] GteVoices={ new[]{"off","ปิดเสียงอ่าน"},new[]{"th-TH-PremwadeeNeural","ไทย หญิง (Premwadee)"},new[]{"th-TH-NiwatNeural","ไทย ชาย (Niwat)"},new[]{"en-US-JennyNeural","English (Jenny)"} };
  List<Dictionary<string,object>> gtePresets; Dictionary<string,object> gteState; List<GteRow> gteRows; int gteIdx=-1; bool gteDirty;
  string gteSub=SubLive,gteFilter="all",gteStatus="Disconnected",gteDetail="";
  readonly List<GteLog> gteLogs=new List<GteLog>();
  long gteSeq; int gteEnginePid; DispatcherTimer gteTimer; bool gtePolling;
  int gteLikes,gteGifts,gteDiamonds,gteLikeCounter; readonly HashSet<string> gteFollowed=new HashSet<string>();
  StackPanel gteLogPanel; TextBlock gtePreview,gteSideStatus,gteJarCount; Border gtePill; int gteJarDrops;

  void GteEnsureLoaded() {
   if(gtePresets!=null) return;
   gtePresets=GteData.LoadPresets(); gteState=GteData.LoadState();
   if(GteData.Str(gteState,"token","").Length<32) { var b=new byte[24]; using(var r=RandomNumberGenerator.Create()) r.GetBytes(b); gteState["token"]=BitConverter.ToString(b).Replace("-","").ToLowerInvariant(); }
   if(GteData.Int(gteState,"port",0)<1024) gteState["port"]=8767;
   var v=GteData.Str(gteState,"voice","th-TH-PremwadeeNeural"); if(v=="thai_soft") v="th-TH-PremwadeeNeural"; if(v=="thai") v="th-TH-NiwatNeural"; gteState["voice"]=v; GteVoice.Voice=v;
   GteSelect(Math.Max(0,Math.Min(GteData.Int(gteState,"preset",0),gtePresets.Count-1)));
   try { GteData.Save(gtePresets,gteState); } catch {}
  }
  Dictionary<string,object> GtePreset { get { return gtePresets[Math.Max(0,Math.Min(gteIdx,gtePresets.Count-1))]; } }
  void GteSelect(int idx) {
   GteFlush(); gteIdx=idx; gteRows=new List<GteRow>();
   object m; var map=gtePresets[idx].TryGetValue("gift_map",out m)?m as IDictionary<string,object>:null;
   if(map!=null) foreach(var pair in map) {
    var row=new GteRow { name=pair.Key }; var g=GteData.FindGift(pair.Key); if(g!=null) row.name=g.name;
    var v=pair.Value as Dictionary<string,object>;
    if(v!=null) { row.key=GteData.Str(v,"key",""); row.win=GteData.Bool(v,"win_enabled",false); row.delta=GteData.Int(v,"win_delta",1); } else row.key=pair.Value==null?"":pair.Value.ToString();
    gteRows.Add(row);
   }
  }
  void GteFlush() {
   if(gteRows==null||gteIdx<0||gteIdx>=gtePresets.Count) return;
   var map=new Dictionary<string,object>();
   foreach(var r in gteRows) { var n=(r.name??"").Trim().ToLowerInvariant(); if(n=="") continue; map[n]=new Dictionary<string,object>{{"key",(r.key??"").Trim()},{"win_enabled",r.win},{"win_delta",r.delta}}; }
   gtePresets[gteIdx]["gift_map"]=map;
  }
  void GteSave(bool quiet) {
   try { GteFlush(); gteState["preset"]=gteIdx; GteData.Save(gtePresets,gteState); gteDirty=false; if(!quiet) status.Text=AppLanguage.T("บันทึกการตั้งค่า Game To Earn แล้ว"); }
   catch(Exception ex) { status.Text=AppLanguage.T("บันทึกไม่สำเร็จ: "+ex.Message); }
  }

  // ── engine + polling ──
  async Task GteStartEngine() {
   GteEnsureLoaded();
   if(GteEngine.Running) return;
   await GteEngine.Start(GteData.Str(gteState,"token",""),GteData.Int(gteState,"port",8767),new Progress<string>(t=>status.Text=AppLanguage.T(t)));
   if(gteEnginePid==GteEngine.Proc.Id) return;   // another caller already finished the start
   gteEnginePid=GteEngine.Proc.Id; gteSeq=0; gteStatus="Disconnected";
   if(GteEngine.Port!=GteData.Int(gteState,"port",8767)) { gteState["port"]=GteEngine.Port; GteSave(true); }
   if(gteTimer==null) { gteTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(300) }; gteTimer.Tick+=async(s,e)=>await GtePoll(); gteTimer.Start(); }
   GtePushOverlay();
   GteAddLog("ระบบ Game To Earn พร้อมใช้งาน","#88AAC9","live");
   status.Text=AppLanguage.T("Game To Earn พร้อมใช้งาน");
  }
  async void GteBoot() { try { await GteStartEngine(); } catch(Exception ex) { GteEngine.LastError=ex.Message; status.Text=AppLanguage.T("เริ่มระบบ Game To Earn ไม่สำเร็จ: ")+ex.Message; GteAddLog(AppLanguage.T("เริ่มระบบไม่สำเร็จ: ")+ex.Message,"#FF6B6B","live"); } if(page==PageGte&&gteSub!=SubPreset) Render(); }
  async Task GtePoll() {
   if(gtePolling||!GteEngine.Running) return; gtePolling=true;
   try {
    var d=await GteEngine.Get("events?after="+gteSeq);
    object evs; if(d!=null&&d.TryGetValue("events",out evs)&&evs is IEnumerable) foreach(var item in (IEnumerable)evs) { var e=item as Dictionary<string,object>; if(e==null) continue; gteSeq=Math.Max(gteSeq,GteData.Int(e,"seq",0)); GteHandle(e); }
   } catch {}
   finally { gtePolling=false; }
  }
  string Who(Dictionary<string,object> e) { var n=GteData.Str(e,"name",""); return n!=""?n:GteData.Str(e,"viewer","?"); }
  void GteHandle(Dictionary<string,object> e) {
   var kind=GteData.Str(e,"kind",""); var p=GtePreset; int delay=GteData.Int(p,"key_delay_ms",130);
   if(GteData.Bool(e,"demo",false)) { if(kind=="like") gteLikes+=GteData.Int(e,"amount",0); if(kind=="gift") { gteGifts+=GteData.Int(e,"amount",0); gteDiamonds+=GteData.Int(e,"diamonds",0); gteJarDrops+=GteData.Int(e,"amount",0); } GteRefreshPreview(); return; }
   if(kind=="status") {
    var old=gteStatus; gteStatus=GteData.Str(e,"status",gteStatus); gteDetail=GteData.Str(e,"detail","");
    GteAddLog(GteStatusText()+(gteDetail!=""?" • "+gteDetail:""),gteStatus=="Connected"?"#48DDAC":gteStatus=="Error"?"#FF6B6B":"#88AAC9","live");
    if(gteStatus=="Connected"&&old!="Connected") { gteLikeCounter=0; gteFollowed.Clear(); }
    GteLiveWeb(gteStatus=="Connected");
    GteRefreshStatus(); return;
   }
   if(kind=="gift") {
    int amount=Math.Max(1,GteData.Int(e,"amount",1)); var name=GteData.Str(e,"giftName","Gift"); var raw=GteData.Str(e,"rawName","");
    gteGifts+=amount; gteDiamonds+=GteData.Int(e,"diamonds",0); gteJarDrops+=amount;
    var method=GteData.Str(e,"method",""); string how=method.StartsWith("image")?" 🖼":"";
    GteAddLog("🎁 "+Who(e)+" ส่ง "+name+" x"+amount+how+(raw!=""&&GteData.Norm(raw)!=GteData.Norm(name)?" ("+raw+")":""),"#FFD84D","live");
    var row=gteRows.FirstOrDefault(r=>GteData.Norm(r.name)==GteData.Norm(name))??gteRows.FirstOrDefault(r=>raw!=""&&GteData.Norm(r.name)==GteData.Norm(raw));
    if(row!=null) {
     if(!string.IsNullOrWhiteSpace(row.key)) { GteKeys.Enqueue(row.key,amount,delay); GteAddLog("⌨ "+name+" x"+amount+" → กด '"+row.key+"'","#25F4EE","cmd"); }
     if(row.win&&row.delta!=0) GteWin(row.delta*amount);
    }
    GteRefreshPreview(); return;
   }
   if(kind=="like") {
    int n=GteData.Int(e,"amount",0); gteLikes+=n;
    if(GteData.Bool(p,"like_enabled",true)) {
     int target=Math.Max(1,GteData.Int(p,"like_target",100)); var seq=GteData.Str(p,"like_seq","v"); gteLikeCounter+=n; int presses=0;
     while(gteLikeCounter>=target) { gteLikeCounter-=target; presses++; }
     if(presses>0&&seq.Trim()!="") { GteKeys.Enqueue(seq,presses,delay); GteAddLog("❤ Like ครบ "+target+" → กด '"+seq+"' x"+presses,"#FF6B9A","cmd"); }
    }
    GteRefreshPreview(); return;
   }
   if(kind=="follow") {
    var viewer=GteData.Str(e,"viewer",""); GteAddLog("👤 "+Who(e)+" ติดตามแล้ว","#B6D1EE","live");
    if(!GteData.Bool(p,"follow_enabled",true)) return;
    if(GteData.Bool(p,"follow_once",true)) { if(gteFollowed.Contains(viewer)) return; gteFollowed.Add(viewer); }
    var seq=GteData.Str(p,"follow_seq","r"); if(seq.Trim()!="") { GteKeys.Enqueue(seq,1,delay); GteAddLog("⌨ ติดตาม → กด '"+seq+"'","#25F4EE","cmd"); }
    return;
   }
   if(kind=="chat") { var text=GteData.Str(e,"comment",""); GteAddLog("💬 "+Who(e)+": "+text,"#D9E8F7","live"); GteVoice.Say(text,false); }
  }
  // website "กำลังไลฟ์ตอนนี้": heartbeat every minute while connected (Supabase live_heartbeat)
  DispatcherTimer gteBeat; bool gteListed;
  void GteLiveWeb(bool live) {
   if(gteBeat==null) { gteBeat=new DispatcherTimer { Interval=TimeSpan.FromSeconds(60) }; gteBeat.Tick+=(s,e)=>GteBeat(); }
   if(live&&AccountsOn&&GteData.Bool(gteState,"show_on_web",true)) { GteBeat(); gteBeat.Start(); }
   else { gteBeat.Stop(); if(gteListed) GteUnlist(); }
  }
  async void GteBeat() {
   if(!AccountsOn||gteStatus!="Connected"||!GteData.Bool(gteState,"show_on_web",true)) return;
   try {
    var st=await GteEngine.Get("state"); var host=GteData.Sub(st,"host"); var a=AccountService.Current;
    var map=games.FirstOrDefault(g=>g.id==GteData.Str(gteState,"roblox",""));
    await AccountService.Rpc("live_heartbeat",new Dictionary<string,object>{
     {"p_username",GteData.Str(st,"username",GteData.Str(gteState,"username",""))},
     {"p_display",GteData.Str(host,"name","")!=""?GteData.Str(host,"name",""):a.displayName},
     {"p_avatar",GteData.Str(host,"avatar","")!=""?GteData.Str(host,"avatar",""):(a.avatarUrl??"")},
     {"p_game",map!=null?map.name:"Game To Earn"},{"p_title",GteData.Str(host,"title","")},{"p_viewers",GteData.Int(host,"viewers",0)}});
    gteListed=true;
   } catch {}
  }
  async void GteUnlist() { gteListed=false; try { await AccountService.Rpc("live_stop",null); } catch {} }

  void GteWin(int delta) {
   var w=GteData.Sub(gteState,"win"); int max=GteData.Int(w,"max",10),count=GteData.Int(w,"count",0)+delta;
   if(count<0) count=0; if(max>0&&count>max) count=max; w["count"]=count;
   GteAddLog("🏆 Win "+(delta>0?"+":"")+delta+" = "+count+(max>0?"/"+max:""),"#FFC94A","cmd"); GtePushOverlay(); GteRefreshPreview();
  }
  async void GtePushOverlay() {
   if(!GteEngine.Running) return; var w=GteData.Sub(gteState,"win");
   try { await GteEngine.Post("overlay/config",new Dictionary<string,object>{{"win",new Dictionary<string,object>{{"count",GteData.Int(w,"count",0)},{"max",GteData.Int(w,"max",10)},{"color",GteData.Str(w,"color","#ffffff")},{"size",GteData.Int(w,"size",64)},{"label",GteData.Str(w,"label","WIN")}}},{"jar",new Dictionary<string,object>{{"scale",GteData.Int(gteState,"jar_scale",100)}}}}); } catch {}
  }
  void GteAddLog(string text,string color,string cat) {
   var item=new GteLog { at=DateTime.Now,text=AppLanguage.T(text),color=color,cat=cat }; gteLogs.Add(item); if(gteLogs.Count>600) gteLogs.RemoveAt(0);
   if(gteLogPanel!=null&&page==PageGte&&gteSub==SubLog&&(gteFilter=="all"||gteFilter==cat)) { gteLogPanel.Children.Add(GteLogLine(item)); if(gteLogPanel.Children.Count>400) gteLogPanel.Children.RemoveAt(0); var sv=gteLogPanel.Parent as ScrollViewer; if(sv!=null) sv.ScrollToEnd(); }
  }
  string GteStatusText() { switch(gteStatus) { case "Connected": return AppLanguage.T("เชื่อมต่อแล้ว")+" @"+GteData.Str(gteState,"username",""); case "Connecting": return AppLanguage.T("กำลังเชื่อมต่อ…"); case "Error": return AppLanguage.T("เชื่อมต่อไม่สำเร็จ • จะลองใหม่อัตโนมัติ"); case "Ended": return AppLanguage.T("LIVE จบแล้ว"); default: return AppLanguage.T("ยังไม่เชื่อมต่อ"); } }
  Brush GteStatusBrush() { return gteStatus=="Connected"?Ui.Green:gteStatus=="Connecting"?Ui.Gold:gteStatus=="Error"?Ui.Red:muted; }
  void GteRefreshStatus() {
   if(page!=PageGte) return;
   if(gteSub==SubLive) { Render(); return; }
   if(gteSideStatus!=null) { gteSideStatus.Text=GteSideText(); gteSideStatus.Foreground=GteStatusBrush(); }
  }
  string GteSideText() { return GteEngine.Running?"● "+AppLanguage.T("ระบบ: ")+(gteStatus=="Connected"?GteStatusText():AppLanguage.T("พร้อมใช้งาน")):"○ "+AppLanguage.T("ระบบ: ยังไม่เริ่ม"); }
  void GteRefreshPreview() {
   if(gtePreview!=null) gtePreview.Text=GtePreviewText();
   if(gteJarCount!=null) gteJarCount.Text=gteJarDrops+" gifts";
  }
  string GtePreviewText() { var w=GteData.Sub(gteState,"win"); return "❤  Like: "+gteLikes.ToString("#,0")+"\n🎁  Gift: "+gteGifts.ToString("#,0")+"   💎 "+gteDiamonds.ToString("#,0")+"\n🏆  Win: "+GteData.Int(w,"count",0)+(GteData.Int(w,"max",10)>0?" / "+GteData.Int(w,"max",10):""); }

  async void GteConnect() {
   GteEnsureLoaded();
   var user=GteData.Str(gteState,"username","").Trim().TrimStart('@');
   if(!Regex.IsMatch(user,@"^[A-Za-z0-9_.]{2,24}$")) { status.Text=AppLanguage.T("กรุณาใส่ชื่อ TikTok เช่น @poomtv99"); return; }
   GteSave(true);
   try { await GteStartEngine(); gteStatus="Connecting"; GteRefreshStatus(); await GteEngine.Post("connect",new Dictionary<string,object>{{"username",user}}); }
   catch(Exception ex) { gteStatus="Error"; gteDetail=ex.Message; status.Text=AppLanguage.T("เชื่อมต่อไม่สำเร็จ: ")+ex.Message; GteRefreshStatus(); }
  }
  async void GteDisconnect() { try { await GteEngine.Post("disconnect",null); } catch {} GteKeys.Clear(); }
  async void GteTest(string kind,string gift,int amount) {
   try { await GteStartEngine(); await GteEngine.Post("test",new Dictionary<string,object>{{"kind",kind},{"giftName",gift},{"amount",amount},{"comment","ทดสอบเสียงอ่านแชท"}}); }
   catch(Exception ex) { status.Text=AppLanguage.T("ทดสอบไม่สำเร็จ: ")+ex.Message; }
  }
  // called by OpenRoblox: get the engine ready and connect with the saved TikTok name
  async Task GteOnRobloxOpen() {
   GteEnsureLoaded();
   if(!settings.gameToEarnWithRoblox) return;
   await GteStartEngine();
   if(gteStatus!="Connected"&&gteStatus!="Connecting"&&GteData.Str(gteState,"username","")!="") GteConnect();
  }

  // ── UI helpers ──
  Border GCard(UIElement child) { return new Border { Background=Light?Ui.B("#FFFFFF"):Ui.B("#CC0A2640"),BorderBrush=Light?Ui.B("#C9DCEF"):Ui.B("#24506F"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(14),Padding=new Thickness(18,14,18,16),Margin=new Thickness(0,0,0,12),Child=child }; }
  TextBlock GTitle(string text) { var t=Ui.Bold(Line(AppLanguage.T(text),15)); t.Margin=new Thickness(0,0,0,10); return t; }
  TextBox GInput(string text,double width,Action<string> changed) { var t=Ui.Input(text,width); t.Margin=new Thickness(0,0,10,0); t.VerticalAlignment=VerticalAlignment.Center; t.TextChanged+=(s,e)=>{ changed(t.Text); gteDirty=true; }; return t; }
  Button GBtn(string text,Brush bg,Action click) { var b=Btn(text,bg); b.FontSize=13; b.Padding=new Thickness(14,8,14,8); b.Margin=new Thickness(0,0,8,0); b.Click+=(s,e)=>click(); return b; }
  CheckBox GCheck(string text,bool value,Action<bool> changed) { var c=new CheckBox { Content=AppLanguage.T(text),IsChecked=value,Foreground=Foreground,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,4,16,4) }; c.Click+=(s,e)=>{ changed(c.IsChecked==true); gteDirty=true; }; return c; }
  StackPanel GRow(params UIElement[] items) { var p=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,4,0,4) }; foreach(var i in items) if(i!=null) p.Children.Add(i); return p; }
  Grid GCols(UIElement left,UIElement right,double rightWidth) {
   var g=new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(12) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width=rightWidth>0?new GridLength(rightWidth):new GridLength(1,GridUnitType.Star) });
   g.Children.Add(left); Grid.SetColumn(right,2); g.Children.Add(right); return g;
  }
  FrameworkElement GiftIcon(string name,double size) {
   var g=GteData.FindGift(name); var img=g==null?null:GteData.Image(g.file);
   if(img!=null) return new Image { Source=img,Width=size,Height=size,Margin=new Thickness(0,0,8,0),VerticalAlignment=VerticalAlignment.Center };
   var t=Line("🎁",size*0.7); t.Margin=new Thickness(0,0,8,0); return t;
  }

  void GameToEarnPage() {
   GteEnsureLoaded();
   if(!GteEngine.Running&&string.IsNullOrEmpty(GteEngine.LastError)) GteBoot();
   gteLogPanel=null; gtePreview=null; gteJarCount=null; gtePill=null;
   var shell=new Grid(); shell.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(196) }); shell.ColumnDefinitions.Add(new ColumnDefinition());
   // inner sidebar
   var side=new Border { Background=Light?Ui.B("#E9F1FB"):Ui.B("#9900101E"),BorderBrush=Light?Ui.B("#C9DCEF"):Ui.B("#1E4566"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(14),Padding=new Thickness(10),Margin=new Thickness(0,0,16,0),MinHeight=560 };
   var sideGrid=new Grid(); side.Child=sideGrid; var menu=new StackPanel(); sideGrid.Children.Add(menu);
   var head=new StackPanel { Margin=new Thickness(8,4,0,14) }; head.Children.Add(Ui.Bold(Line("Game To Earn",16))); head.Children.Add(Line("TikTok LIVE → Roblox",11,muted)); menu.Children.Add(head);
   foreach(var item in new[]{ new[]{"((•))",SubLive},new[]{"☰",SubPreset},new[]{"▭",SubObs},new[]{"≡",SubLog} }) {
    string key=item[1]; bool on=gteSub==key;
    var b=Btn(item[0]+"   "+AppLanguage.T(key),on?accent:Brushes.Transparent); b.HorizontalContentAlignment=HorizontalAlignment.Left; b.Padding=new Thickness(14,11,8,11); b.Margin=new Thickness(0,0,0,6); b.FontWeight=on?FontWeights.Bold:FontWeights.Normal;
    if(on) b.Foreground=Brushes.White;
    b.Click+=(s,e)=>{ if(gteSub==SubPreset&&gteDirty) GteSave(true); gteSub=key; Render(); }; menu.Children.Add(b);
   }
   gteSideStatus=Line(GteSideText(),12,GteStatusBrush()); gteSideStatus.VerticalAlignment=VerticalAlignment.Bottom; gteSideStatus.Margin=new Thickness(10,0,0,6); gteSideStatus.TextWrapping=TextWrapping.Wrap; sideGrid.Children.Add(gteSideStatus);
   shell.Children.Add(side);
   // content
   var content=new StackPanel(); Grid.SetColumn(content,1); shell.Children.Add(content);
   var crumb=Line("⌂  ›  "+AppLanguage.T(gteSub),12,muted); crumb.Margin=new Thickness(2,2,0,4); content.Children.Add(crumb);
   var icons=new Dictionary<string,string>{{SubLive,"((•))"},{SubPreset,"☰"},{SubObs,"▭"},{SubLog,"≡"}};
   var title=Ui.Bold(Line(icons[gteSub]+"  "+AppLanguage.T(gteSub),26)); title.Margin=new Thickness(0,0,0,14); content.Children.Add(title);
   if(!GteEngine.Running&&!string.IsNullOrEmpty(GteEngine.LastError)) {
    var warn=new StackPanel(); warn.Children.Add(Line(AppLanguage.T("เริ่มระบบ Game To Earn ไม่สำเร็จ: ")+GteEngine.LastError,13,Ui.Red));
    warn.Children.Add(GRow(GBtn("↻  ลองอีกครั้ง",accent,()=>{ GteEngine.LastError=""; GteBoot(); Render(); }))); content.Children.Add(GCard(warn));
   }
   if(gteSub==SubLive) GteLivePage(content); else if(gteSub==SubPreset) GtePresetPage(content); else if(gteSub==SubObs) GteObsPage(content); else GteLogPage(content);
   sections.Children.Add(shell);
  }

  void GteLivePage(StackPanel content) {
   bool connected=gteStatus=="Connected"||gteStatus=="Connecting";
   var conn=new StackPanel(); conn.Children.Add(GTitle("เชื่อมต่อ TikTok LIVE"));
   var user=GInput(GteData.Str(gteState,"username","")==""?"":"@"+GteData.Str(gteState,"username",""),260,v=>gteState["username"]=v.Trim().TrimStart('@'));
   user.ToolTip="@ชื่อผู้ใช้"; user.IsEnabled=!connected; user.KeyDown+=(s,e)=>{ if(e.Key==Key.Enter) GteConnect(); };
   var go=GBtn(connected?"ตัดการเชื่อมต่อ":"เชื่อมต่อ",connected?Ui.B("#7F1D1D"):accent,()=>{ if(gteStatus=="Connected"||gteStatus=="Connecting") GteDisconnect(); else GteConnect(); });
   go.FontWeight=FontWeights.Bold; go.Padding=new Thickness(22,9,22,9);
   conn.Children.Add(GRow(user,go));
   var st=new StackPanel(); st.Children.Add(GTitle("สถานะการเชื่อมต่อ"));
   gtePill=new Border { CornerRadius=new CornerRadius(10),BorderBrush=GteStatusBrush(),BorderThickness=new Thickness(1),Padding=new Thickness(12,7,12,7),HorizontalAlignment=HorizontalAlignment.Left,Child=Line((gteStatus=="Connected"?"● ":"○ ")+GteStatusText(),13,GteStatusBrush()) };
   st.Children.Add(gtePill); if(gteDetail!=""&&gteStatus=="Error") { var d=Line(gteDetail,11,muted); d.Margin=new Thickness(0,6,0,0); st.Children.Add(d); }
   content.Children.Add(GCols(GCard(conn),GCard(st),250));

   var pre=new StackPanel(); pre.Children.Add(GTitle("Preset ที่เลือก"));
   var pick=new ComboBox { Width=260,HorizontalAlignment=HorizontalAlignment.Left };
   for(int i=0;i<gtePresets.Count;i++) pick.Items.Add((i+1)+". "+GteData.Str(gtePresets[i],"name","Preset"));
   pick.SelectedIndex=gteIdx; pick.SelectionChanged+=(s,e)=>{ if(pick.SelectedIndex>=0&&pick.SelectedIndex!=gteIdx) { GteSelect(pick.SelectedIndex); GteSave(true); GteAddLog("Preset: "+GteData.Str(GtePreset,"name",""),"#25F4EE","cmd"); Render(); } };
   pre.Children.Add(pick);
   var target=new StackPanel(); target.Children.Add(GTitle("เป้าหมาย (Roblox)"));
   var maps=games.Where(g=>g.category=="Roblox").ToList(); var mapBox=new ComboBox { Width=165,Margin=new Thickness(0,0,8,0) };
   foreach(var g in maps) mapBox.Items.Add(g.name); var lastMap=GteData.Str(gteState,"roblox",""); mapBox.SelectedIndex=Math.Max(0,maps.FindIndex(g=>g.id==lastMap));
   mapBox.SelectionChanged+=(s,e)=>{ if(mapBox.SelectedIndex>=0) { gteState["roblox"]=maps[mapBox.SelectedIndex].id; GteSave(true); } };
   var open=GBtn("▶ เปิด",accent,()=>{ if(mapBox.SelectedIndex>=0) OpenRoblox(maps[mapBox.SelectedIndex]); });
   target.Children.Add(GRow(GameIcon(26,true),mapBox,open));
   content.Children.Add(GCols(GCard(pre),GCard(target),330));

   var check=new StackPanel(); check.Children.Add(GTitle("ก่อนเริ่ม LIVE"));
   var c1=GCheck("เชื่อมต่อ TikTok LIVE ด้วย @ชื่อผู้ใช้",gteStatus=="Connected",v=>{}); c1.IsEnabled=false; check.Children.Add(c1);
   var c2=GCheck("เลือก Preset ที่ต้องการใช้งาน",GteData.Bool(gteState,"chk_preset",false),v=>{ gteState["chk_preset"]=v; GteSave(true); }); check.Children.Add(c2);
   var c3=GCheck("ตั้งค่าระยะหน่วงและเสียงอ่าน (ถ้าต้องการ)",GteData.Bool(gteState,"chk_voice",false),v=>{ gteState["chk_voice"]=v; GteSave(true); }); check.Children.Add(c3);
   var c4=GCheck("เปิดหน้า OBS และทดสอบก่อนเริ่ม LIVE",GteData.Bool(gteState,"chk_obs",false),v=>{ gteState["chk_obs"]=v; GteSave(true); }); check.Children.Add(c4);
   var web=GCheck("แสดงบนเว็บ PTVgame ว่ากำลังไลฟ์ (รูปโปรไฟล์ + ลิงก์ไปหน้าไลฟ์)",GteData.Bool(gteState,"show_on_web",true),v=>{ gteState["show_on_web"]=v; GteSave(true); GteLiveWeb(gteStatus=="Connected"); }); web.Margin=new Thickness(0,10,0,0); check.Children.Add(web);
   var auto=GCheck("เปิดแมพ Roblox แล้วเชื่อมต่อ TikTok ให้อัตโนมัติ",settings.gameToEarnWithRoblox,v=>{ settings.gameToEarnWithRoblox=v; Save(); }); auto.Margin=new Thickness(0,2,0,0); check.Children.Add(auto);
   content.Children.Add(GCard(check));

   var test=new StackPanel(); test.Children.Add(GTitle("ทดสอบโดยไม่ต้องไลฟ์"));
   var hint=Line(AppLanguage.T("คลิกหน้าต่าง Roblox ภายใน 3 วินาทีหลังกด เพื่อดูว่าคีย์ทำงาน"),12,muted); hint.Margin=new Thickness(0,0,0,8); test.Children.Add(hint);
   test.Children.Add(GRow(
    GBtn("🎁 ของขวัญ",Ui.B("#3B2A00"),()=>{ var r=gteRows.FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x.key)); GteDelayed(()=>GteTest("gift",r!=null?r.name:"Rose",1)); }),
    GBtn("❤ Like",Ui.B("#4A1030"),()=>GteDelayed(()=>GteTest("like","",Math.Max(1,GteData.Int(GtePreset,"like_target",100))))),
    GBtn("👤 ติดตาม",Ui.B("#193B58"),()=>GteDelayed(()=>GteTest("follow","",1))),
    GBtn("💬 เสียงอ่าน",Ui.B("#193B58"),()=>GteVoice.Say(AppLanguage.T("ทดสอบเสียงอ่านแชท"),true))));
   content.Children.Add(GCard(test));
  }
  async void GteDelayed(Action a) { status.Text=AppLanguage.T("คลิกหน้าต่างเกมเลย… ส่งใน 3 วินาที"); await Task.Delay(3000); a(); }

  void GtePresetPage(StackPanel content) {
   var p=GtePreset;
   var left=new StackPanel(); left.Children.Add(GTitle("รายการ Preset"));
   var add=GBtn("+  สร้าง Preset",accent,()=>{ GteFlush(); gtePresets.Add(GteData.NewPreset("Preset "+(gtePresets.Count+1))); GteSelect(gtePresets.Count-1); GteSave(true); Render(); }); add.HorizontalAlignment=HorizontalAlignment.Stretch; add.HorizontalContentAlignment=HorizontalAlignment.Center; add.Margin=new Thickness(0,0,0,10); left.Children.Add(add);
   for(int i=0;i<gtePresets.Count;i++) {
    int idx=i; bool on=i==gteIdx;
    var b=Btn((i+1)+". "+GteData.Str(gtePresets[i],"name","Preset")+(on?"   ✓":""),on?accent:Ui.B("#14FFFFFF")); b.HorizontalContentAlignment=HorizontalAlignment.Left; b.Padding=new Thickness(12,9,10,9); b.Margin=new Thickness(0,0,0,6);
    if(on) b.Foreground=Brushes.White;
    b.Click+=(s,e)=>{ if(idx!=gteIdx) { GteSelect(idx); GteSave(true); Render(); } }; left.Children.Add(b);
   }
   left.Children.Add(GRow(
    GBtn("⧉ สำเนา",Ui.B("#3B0764"),()=>{ GteFlush(); var c=Storage.Json.Deserialize<Dictionary<string,object>>(Storage.Json.Serialize(GtePreset)); c["name"]=GteData.Str(c,"name","Preset")+" (copy)"; gtePresets.Add(c); GteSelect(gtePresets.Count-1); GteSave(true); Render(); }),
    GBtn("ลบ",Ui.B("#7F1D1D"),()=>{ if(gtePresets.Count<=1) { status.Text=AppLanguage.T("ต้องมีอย่างน้อย 1 Preset"); return; } if(MessageBox.Show(AppLanguage.T("ลบ Preset นี้?"),"Game To Earn",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return; gteRows=null; gtePresets.RemoveAt(gteIdx); GteSelect(Math.Min(gteIdx,gtePresets.Count-1)); GteSave(true); Render(); })));

   var right=new StackPanel();
   right.Children.Add(GTitle(AppLanguage.T("แก้ไข Preset: ")+(gteIdx+1)+". "+GteData.Str(p,"name","")));
   right.Children.Add(GRow(Line(AppLanguage.T("ชื่อ Preset"),13,muted),new Border { Width=8 },GInput(GteData.Str(p,"name",""),280,v=>p["name"]=v)));
   var cmdBox=new StackPanel { Margin=new Thickness(0,8,0,0) };
   var head=Line(AppLanguage.T("คำสั่ง (เหตุการณ์ → ปุ่มกด)"),13,muted); head.Margin=new Thickness(0,0,0,6); cmdBox.Children.Add(head);
   // follow / like rows
   var fr=GteCmdRow(Line("👤",18),Line(AppLanguage.T("ติดตาม"),14),GInput(GteData.Str(p,"follow_seq","r"),80,v=>p["follow_seq"]=v.Trim()),
     GCheck("เปิดใช้",GteData.Bool(p,"follow_enabled",true),v=>p["follow_enabled"]=v),GCheck("ครั้งเดียว/คน",GteData.Bool(p,"follow_once",true),v=>p["follow_once"]=v),null);
   cmdBox.Children.Add(fr);
   var likeN=GInput(GteData.Int(p,"like_target",100).ToString(),70,v=>{ int n; if(int.TryParse(v,out n)&&n>0) p["like_target"]=n; });
   var lr=GteCmdRow(Line("❤",18,Ui.B("#FF6B9A")),GRow(Line("Like",14),new Border { Width=6 },likeN),GInput(GteData.Str(p,"like_seq","v"),80,v=>p["like_seq"]=v.Trim()),
     GCheck("เปิดใช้",GteData.Bool(p,"like_enabled",true),v=>p["like_enabled"]=v),null,null);
   cmdBox.Children.Add(lr);
   foreach(var row in gteRows.ToList()) {
    var r=row;
    var nameBtn=Btn(string.IsNullOrEmpty(r.name)?AppLanguage.T("เลือกของขวัญ…"):r.name,Ui.B("#14FFFFFF")); nameBtn.Padding=new Thickness(8,5,8,5); nameBtn.MinWidth=130; nameBtn.HorizontalContentAlignment=HorizontalAlignment.Left;
    nameBtn.Click+=(s,e)=>{ var picked=GtePickGift(r.name); if(picked!=null) { r.name=picked.name; gteDirty=true; Render(); } };
    var g=GteData.FindGift(r.name); var price=g!=null&&g.price>0?Line("💎"+g.price,11,muted):null;
    var winBox=GRow(GCheck("Win",r.win,v=>r.win=v),GInput(r.delta.ToString(),46,v=>{ int n; if(int.TryParse(v,out n)) r.delta=n; }));
    var del=GBtn("🗑",Brushes.Transparent,()=>{ gteRows.Remove(r); gteDirty=true; Render(); }); del.Padding=new Thickness(8,4,8,4);
    cmdBox.Children.Add(GteCmdRow(GiftIcon(r.name,28),GRow(nameBtn,new Border { Width=6 },price),GInput(r.key,80,v=>r.key=v),winBox,null,del));
   }
   var addGift=GBtn("+ เพิ่มของขวัญ",Ui.B("#166534"),()=>{ var picked=GtePickGift(""); if(picked!=null) { gteRows.Add(new GteRow { name=picked.name }); gteDirty=true; Render(); } }); addGift.Margin=new Thickness(0,8,0,0); addGift.HorizontalAlignment=HorizontalAlignment.Left;
   cmdBox.Children.Add(addGift);
   var keysHint=Line(AppLanguage.T("คีย์: yy = กด y สองครั้ง • a b = กด a แล้ว b • ปุ่มพิเศษ: up down left right space enter"),11,muted); keysHint.Margin=new Thickness(0,8,0,0); cmdBox.Children.Add(keysHint);
   right.Children.Add(cmdBox);
   var delay=GInput(GteData.Int(p,"key_delay_ms",130).ToString(),90,v=>{ int n; if(int.TryParse(v,out n)&&n>=10&&n<=2000) p["key_delay_ms"]=n; });
   var voice=new ComboBox { Width=220,VerticalAlignment=VerticalAlignment.Center };
   foreach(var v in GteVoices) voice.Items.Add(AppLanguage.T(v[1]));
   voice.SelectedIndex=Math.Max(0,Array.FindIndex(GteVoices,v=>v[0]==GteData.Str(gteState,"voice","off")));
   voice.SelectionChanged+=(s,e)=>{ if(voice.SelectedIndex>=0) { gteState["voice"]=GteVoices[voice.SelectedIndex][0]; GteVoice.Voice=GteVoices[voice.SelectedIndex][0]; gteDirty=true; } };
   var opts=new Grid { Margin=new Thickness(0,14,0,0) }; opts.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto }); opts.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(20) }); opts.ColumnDefinitions.Add(new ColumnDefinition());
   var o1=new StackPanel(); o1.Children.Add(Line(AppLanguage.T("ระยะหน่วง (มิลลิวินาที)"),12,muted)); o1.Children.Add(delay); opts.Children.Add(o1);
   var o2=new StackPanel(); o2.Children.Add(Line(AppLanguage.T("เสียงอ่านข้อความ"),12,muted)); o2.Children.Add(voice); Grid.SetColumn(o2,2); opts.Children.Add(o2);
   right.Children.Add(opts);
   var save=GBtn("💾  บันทึก",accent,()=>{ GteSave(false); Render(); }); save.HorizontalAlignment=HorizontalAlignment.Stretch; save.HorizontalContentAlignment=HorizontalAlignment.Center; save.FontWeight=FontWeights.Bold; save.FontSize=15; save.Padding=new Thickness(12,11,12,11); save.Margin=new Thickness(0,16,0,0);
   right.Children.Add(save);
   content.Children.Add(GCols(GCard(left),GCard(right),-1).WithLeft(200));
  }
  Grid GteCmdRow(UIElement icon,UIElement label,UIElement key,UIElement a,UIElement b,UIElement del) {
   var g=new Grid { Margin=new Thickness(0,3,0,3) };
   foreach(var w in new[]{36.0,0,24,96,0,40}) g.ColumnDefinitions.Add(new ColumnDefinition { Width=w==0?GridLength.Auto:new GridLength(w) });
   g.ColumnDefinitions[1].Width=new GridLength(1,GridUnitType.Star);
   var arrow=Line("→",16,muted);
   var items=new UIElement[]{icon,label,arrow,key,GRow(a,b),del};
   for(int i=0;i<items.Length;i++) if(items[i]!=null) { Grid.SetColumn(items[i],i); var fe=items[i] as FrameworkElement; if(fe!=null) fe.VerticalAlignment=VerticalAlignment.Center; g.Children.Add(items[i]); }
   return new GridWrap(g,Light);
  }

  GteData.Gift GtePickGift(string current) {
   GteData.Gift result=null;
   var win=new Window { Title=AppLanguage.T("เลือกของขวัญ"),Width=760,Height=620,WindowStartupLocation=WindowStartupLocation.CenterOwner,Owner=this,Background=Light?Ui.B("#F4F8FF"):Ui.B("#00101F"),Foreground=Foreground,FontFamily=FontFamily };
   var root=new DockPanel { Margin=new Thickness(14) }; win.Content=root;
   var search=Ui.Input("",double.NaN); search.Margin=new Thickness(0,0,0,10); DockPanel.SetDock(search,Dock.Top); root.Children.Add(search);
   var scroll=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto }; var wrap=new WrapPanel(); scroll.Content=wrap; root.Children.Add(scroll);
   Action fill=()=>{
    wrap.Children.Clear(); var q=GteData.Norm(search.Text); int n=0;
    foreach(var g in GteData.Gifts) {
     if(q!=""&&!GteData.Norm(g.name).Contains(q)) continue; if(++n>240) break; var gift=g;
     var tile=new StackPanel { Width=104 }; var img=GteData.Image(g.file);
     if(img!=null) tile.Children.Add(new Image { Source=img,Width=56,Height=56,Margin=new Thickness(0,2,0,4) }); else tile.Children.Add(Line("🎁",36));
     var nm=Line(g.name,11); nm.TextAlignment=TextAlignment.Center; nm.HorizontalAlignment=HorizontalAlignment.Center; nm.MaxHeight=30; tile.Children.Add(nm);
     if(g.price>0) { var pr=Line("💎 "+g.price,10,muted); pr.HorizontalAlignment=HorizontalAlignment.Center; tile.Children.Add(pr); }
     var b=new Button { Content=tile,Margin=new Thickness(4),Padding=new Thickness(4,6,4,6),Background=GteData.Norm(g.name)==GteData.Norm(current)?accent:Ui.B("#14FFFFFF"),Foreground=Foreground,BorderThickness=new Thickness(0),Cursor=Cursors.Hand };
     b.Click+=(s,e)=>{ result=gift; win.Close(); }; wrap.Children.Add(b);
    }
   };
   search.TextChanged+=(s,e)=>fill(); fill(); search.Focus();
   win.ShowDialog(); return result;
  }

  static readonly string[][] GteWidgets={ new[]{"likers","❤  ยอดกด Like","#25F4EE","#002A2A"},new[]{"gifters","🎁  ยอดส่งของขวัญ","#FFD700","#2A2000"},new[]{"win","🏆  นับ Win","#2A2200","#FFD700"},new[]{"jar","🫙  แก้วโหล","#1A0B2E","#CC88FF"},new[]{"stats","📊  สรุปยอด","#193B58","#FFFFFF"} };
  static Brush Checker() {
   var g=new DrawingGroup(); g.Children.Add(new GeometryDrawing(Ui.B("#2A3440"),null,new RectangleGeometry(new Rect(0,0,20,20))));
   g.Children.Add(new GeometryDrawing(Ui.B("#202833"),null,new RectangleGeometry(new Rect(0,0,10,10)))); g.Children.Add(new GeometryDrawing(Ui.B("#202833"),null,new RectangleGeometry(new Rect(10,10,10,10))));
   return new DrawingBrush(g) { TileMode=TileMode.Tile,Viewport=new Rect(0,0,20,20),ViewportUnits=BrushMappingMode.Absolute };
  }
  async void GteDemo() {
   try {
    await GteStartEngine();
    var people=new[]{ new[]{"mali","Mali 🌸"},new[]{"somchai","Somchai"},new[]{"nok","Nok"},new[]{"ploy","Ploy ✨"},new[]{"ton","Ton"} };
    var gifts=new[]{"Rose","Heart Me","Doughnut","GG","Corgi"}; var rnd=new Random();
    for(int i=0;i<people.Length;i++) {
     await GteEngine.Post("test",new Dictionary<string,object>{{"kind","like"},{"viewer",people[i][0]},{"name",people[i][1]},{"amount",(5-i)*120+rnd.Next(40)},{"demo",true}});
     await GteEngine.Post("test",new Dictionary<string,object>{{"kind","gift"},{"viewer",people[i][0]},{"name",people[i][1]},{"giftName",gifts[i]},{"amount",(5-i)*3},{"demo",true}});
    }
    status.Text=AppLanguage.T("ใส่ข้อมูลตัวอย่างแล้ว (ไม่กดคีย์ในเกม)");
   } catch(Exception ex) { status.Text=AppLanguage.T("ทดสอบไม่สำเร็จ: ")+ex.Message; }
  }
  async void GteResetOverlay(string what) {
   if(what!="jar") { gteLikes=0; gteGifts=0; gteDiamonds=0; } else gteJarDrops=0; GteRefreshPreview();
   try { await GteEngine.Post("overlay/reset",new Dictionary<string,object>{{"what",what}}); } catch {}
  }

  void GteObsPage(StackPanel content) {
   string bg=GteData.Str(gteState,"obs_bg",""); string sel=GteData.Str(gteState,"obs_preview","likers");
   if(!GteWidgets.Any(x=>x[0]==sel)) sel="likers";
   // widgets
   var widgets=new StackPanel(); widgets.Children.Add(GTitle("วิดเจ็ต"));
   foreach(var wd in GteWidgets) {
    string kind=wd[0],label=AppLanguage.T(wd[1]); bool on=kind==sel;
    var g=new Grid { Margin=new Thickness(0,0,0,8) }; g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
    var pick=Btn(label,Ui.B(wd[2])); pick.Foreground=Ui.B(wd[3]); pick.FontWeight=FontWeights.Bold; pick.Padding=new Thickness(12,10,12,10); pick.HorizontalContentAlignment=HorizontalAlignment.Left;
    pick.BorderBrush=on?Brushes.White:Ui.B(wd[3]); pick.BorderThickness=new Thickness(on?2.5:1); pick.ToolTip=AppLanguage.T("ดูตัวอย่าง");
    pick.Click+=(s,e)=>{ gteState["obs_preview"]=kind; GteSave(true); Render(); }; g.Children.Add(pick);
    var link=GBtn("🔗 ลิงก์",Ui.B("#193B58"),()=>{ if(!GteEngine.Running) { status.Text=AppLanguage.T("ระบบ Game To Earn ยังไม่พร้อม"); return; } try { Clipboard.SetText(GteEngine.OverlayUrl(kind,bg)); gteState["chk_obs"]=true; GteSave(true); status.Text=AppLanguage.T("คัดลอกลิงก์แล้ว: ")+label+AppLanguage.T(" • วางใน OBS (Browser) หรือ TikTok LIVE Studio (Link)"); } catch {} });
    link.Margin=new Thickness(8,0,0,0); link.ToolTip=AppLanguage.T("คัดลอกลิงก์สำหรับ OBS Browser Source / TikTok LIVE Studio Link"); Grid.SetColumn(link,1); g.Children.Add(link);
    var win=GBtn("🪟 หน้าต่าง",Ui.B("#14375A"),()=>{ GteOverlayWindow.Show(kind,label.Trim(),bg==""?"#00FF00":bg); gteState["chk_obs"]=true; GteSave(true); status.Text=AppLanguage.T("เปิดหน้าต่างแล้ว • ใน TikTok LIVE Studio เพิ่ม Window Capture → เลือก PTV Overlay"); });
    win.Margin=new Thickness(6,0,0,0); win.ToolTip=AppLanguage.T("เปิดเป็นหน้าต่างให้ TikTok LIVE Studio / OBS จับภาพ (Window Capture) • คลิกขวาที่หน้าต่างเพื่อเปลี่ยนพื้นหลัง/ปิด"); Grid.SetColumn(win,2); g.Children.Add(win);
    widgets.Children.Add(g);
   }
   var bgBox=new ComboBox { Width=200 }; bgBox.Items.Add(AppLanguage.T("พื้นหลังโปร่งใส")); bgBox.Items.Add(AppLanguage.T("พื้นหลังเขียว (Chroma)")); bgBox.SelectedIndex=bg==""?0:1;
   bgBox.SelectionChanged+=(s,e)=>{ gteState["obs_bg"]=bgBox.SelectedIndex==1?"#00ff00":""; GteSave(true); Render(); };
   widgets.Children.Add(GRow(Line(AppLanguage.T("พื้นหลังลิงก์"),12,muted),new Border { Width=8 },bgBox));
   var tip=Line(AppLanguage.T("หน้าต่างใช้พื้นเขียวเสมอ (คลิกขวาเปลี่ยนได้) • ใช้ Chroma Key ลบสีเขียวในโปรแกรมไลฟ์"),11,muted); tip.TextWrapping=TextWrapping.Wrap; tip.Margin=new Thickness(0,6,0,0); widgets.Children.Add(tip);

   // live preview of the selected widget
   var pv=new StackPanel(); var wdSel=GteWidgets.First(x=>x[0]==sel);
   pv.Children.Add(GTitle(AppLanguage.T("ตัวอย่างผลลัพธ์: ")+AppLanguage.T(wdSel[1]).Trim()));
   double h=sel=="win"?160:sel=="stats"?120:sel=="jar"?400:380;
   var view=new GteOverlayView(sel);
   pv.Children.Add(new Border { Height=h,CornerRadius=new CornerRadius(10),ClipToBounds=true,BorderBrush=Ui.B("#1E4566"),BorderThickness=new Thickness(1),Background=bg==""?Checker():Ui.B(bg),Child=view });
   var note=Line(GteEngine.Running?AppLanguage.T("แสดงผลจริงจาก LIVE • อัปเดตทุก 0.5 วินาที"):AppLanguage.T("ระบบยังไม่พร้อม • รอสักครู่"),11,muted); note.Margin=new Thickness(0,6,0,0); pv.Children.Add(note);
   pv.Children.Add(GRow(GBtn("✨ ใส่ข้อมูลตัวอย่าง",accent,GteDemo),GBtn("🗑 ล้างยอด",Ui.B("#4A0000"),()=>{ GteResetOverlay("likers"); GteResetOverlay("gifters"); GteResetOverlay("jar"); })));
   content.Children.Add(GCols(GCard(widgets),GCard(pv),360));

   // win + jar settings
   var w=GteData.Sub(gteState,"win");
   var wc=new StackPanel(); wc.Children.Add(GTitle("นับ Win"));
   gtePreview=new TextBlock { Text=GtePreviewText(),FontSize=14,FontWeight=FontWeights.Bold,Foreground=Foreground,LineHeight=24,Margin=new Thickness(0,0,0,8) }; wc.Children.Add(gtePreview);
   wc.Children.Add(GRow(GBtn("−",Ui.B("#193B58"),()=>GteWin(-1)),GBtn("+",Ui.B("#193B58"),()=>GteWin(1)),GBtn("รีเซ็ต",Ui.B("#4A0000"),()=>{ w["count"]=0; GteSave(true); GtePushOverlay(); GteRefreshPreview(); })));
   var max=GInput(GteData.Int(w,"max",10).ToString(),56,v=>{ int n; if(int.TryParse(v,out n)&&n>=0) { w["max"]=n; GteSave(true); GtePushOverlay(); GteRefreshPreview(); } });
   var size=GInput(GteData.Int(w,"size",64).ToString(),56,v=>{ int n; if(int.TryParse(v,out n)&&n>=12&&n<=200) { w["size"]=n; GteSave(true); GtePushOverlay(); } });
   wc.Children.Add(GRow(Line(AppLanguage.T("สูงสุด"),12,muted),new Border { Width=6 },max,Line(AppLanguage.T("ขนาด"),12,muted),new Border { Width=6 },size));
   var colors=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,6,0,0) }; colors.Children.Add(Line(AppLanguage.T("สี"),12,muted));
   foreach(var c in new[]{"#FFFFFF","#FFD700","#FF4F9A","#25F4EE","#48DDAC","#FF6B3D"}) {
    var col=c; bool cur=GteData.Str(w,"color","#ffffff").Equals(c,StringComparison.OrdinalIgnoreCase);
    var sw=new Button { Width=26,Height=26,Margin=new Thickness(8,0,0,0),Background=Ui.B(c),BorderBrush=cur?accent:Brushes.Transparent,BorderThickness=new Thickness(3),Cursor=Cursors.Hand };
    sw.Click+=(s,e)=>{ w["color"]=col; GteSave(true); GtePushOverlay(); Render(); }; colors.Children.Add(sw);
   }
   wc.Children.Add(colors);
   var jar=new StackPanel(); jar.Children.Add(GTitle("แก้วโหลของขวัญ"));
   var pct=Line(GteData.Int(gteState,"jar_scale",100)+"%",15,Ui.Gold); pct.FontWeight=FontWeights.Bold; pct.Width=56;
   var slider=new Slider { Minimum=40,Maximum=200,Value=GteData.Int(gteState,"jar_scale",100),Width=200,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(10,0,12,0) };
   slider.ValueChanged+=(s,e)=>{ gteState["jar_scale"]=(int)slider.Value; pct.Text=(int)slider.Value+"%"; };
   slider.PreviewMouseUp+=(s,e)=>{ GteSave(true); GtePushOverlay(); };
   jar.Children.Add(GRow(Line("Scale",13,muted),slider,pct));
   gteJarCount=Line(gteJarDrops+" gifts",13,muted);
   jar.Children.Add(GRow(
    GBtn("⚗ Test Drop",Ui.B("#14375A"),async()=>{ try { await GteStartEngine(); var r=gteRows.FirstOrDefault(); await GteEngine.Post("overlay/drop",new Dictionary<string,object>{{"giftName",r!=null?r.name:"Rose"},{"count",5}}); gteJarDrops+=5; GteRefreshPreview(); } catch(Exception ex){ status.Text=ex.Message; } }),
    GBtn("🗑 Clear",Ui.B("#5A1010"),()=>GteResetOverlay("jar")),gteJarCount));
   content.Children.Add(GCols(GCard(wc),GCard(jar),360));

   // how to use with TikTok LIVE Studio / OBS
   var how=new StackPanel(); how.Children.Add(GTitle("ใช้กับ TikTok LIVE Studio / OBS"));
   foreach(var t in new[]{
    "TikTok LIVE Studio (แนะนำ): กด 🪟 หน้าต่าง → ใน LIVE Studio เพิ่มแหล่ง \"Window Capture / จับหน้าต่าง\" → เลือก PTV Overlay → ใช้ Chroma Key สีเขียว",
    "TikTok LIVE Studio แบบลิงก์: กด 🔗 ลิงก์ → เพิ่มแหล่ง \"Link\" วางลิงก์ (ถ้าภาพเล็กหรือไม่ขึ้น ให้ใช้แบบหน้าต่างแทน)",
    "OBS: กด 🔗 ลิงก์ → Sources → Browser → วางลิงก์ กว้าง 400 สูง 600 (พื้นหลังโปร่งใสใช้ได้เลย)",
    "ต้องเปิด Launcher ค้างไว้ระหว่างไลฟ์ ลิงก์และหน้าต่างจะอัปเดตเองตาม LIVE"}) { var l=Line("• "+AppLanguage.T(t),13,muted); l.TextWrapping=TextWrapping.Wrap; l.Margin=new Thickness(0,3,0,3); how.Children.Add(l); }
   content.Children.Add(GCard(how));
  }

  UIElement GteLogLine(GteLog l) {
   var t=new TextBlock { FontFamily=new FontFamily("Consolas"),FontSize=13,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,2,0,2) };
   t.Inlines.Add(new System.Windows.Documents.Run("["+l.at.ToString("HH:mm:ss")+"] ") { Foreground=Ui.B("#7F9BB5") });
   Brush c; try { c=Ui.B(l.color); } catch { c=Brushes.White; }
   t.Inlines.Add(new System.Windows.Documents.Run(l.text) { Foreground=c });
   return t;
  }
  void GteLogPage(StackPanel content) {
   var bar=new Grid { Margin=new Thickness(0,0,0,10) };
   var chips=new StackPanel { Orientation=Orientation.Horizontal }; chips.Children.Add(Line(AppLanguage.T("ตัวกรอง:"),13,muted));
   foreach(var f in new[]{ new[]{"all","ทั้งหมด"},new[]{"live","LIVE"},new[]{"cmd","คำสั่ง"} }) {
    string key=f[0]; bool on=gteFilter==key;
    var b=Btn(f[1],on?accent:Brushes.Transparent); b.BorderBrush=on?accent:Ui.B("#3A6A92"); b.BorderThickness=new Thickness(1); b.Padding=new Thickness(16,6,16,6); b.Margin=new Thickness(10,0,0,0); if(on) b.Foreground=Brushes.White;
    b.Click+=(s,e)=>{ gteFilter=key; Render(); }; chips.Children.Add(b);
   }
   bar.Children.Add(chips);
   var clear=Btn("🗑  ล้าง",Brushes.Transparent); clear.BorderBrush=Ui.B("#3A6A92"); clear.BorderThickness=new Thickness(1); clear.HorizontalAlignment=HorizontalAlignment.Right; clear.Padding=new Thickness(14,6,14,6);
   clear.Click+=(s,e)=>{ gteLogs.Clear(); Render(); }; bar.Children.Add(clear);
   content.Children.Add(bar);
   gteLogPanel=new StackPanel();
   foreach(var l in gteLogs.Where(x=>gteFilter=="all"||x.cat==gteFilter).Skip(Math.Max(0,gteLogs.Count-400))) gteLogPanel.Children.Add(GteLogLine(l));
   if(gteLogs.Count==0) gteLogPanel.Children.Add(Line(AppLanguage.T("ยังไม่มีเหตุการณ์ • เชื่อมต่อ LIVE หรือกดทดสอบในหน้าเชื่อมต่อ LIVE"),13,muted));
   var sv=new ScrollViewer { Content=gteLogPanel,Height=470,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
   content.Children.Add(new Border { Background=Ui.B("#05101A"),BorderBrush=Ui.B("#1E4566"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(16,12,16,12),Child=sv });
   sv.Loaded+=(s,e)=>sv.ScrollToEnd();
  }
 }

 // row container for the command list
 public class GridWrap : Grid {
  public GridWrap(Grid inner,bool light) { Children.Add(new Border { Background=light?Ui.B("#F1F6FC"):Ui.B("#66001426"),BorderBrush=light?Ui.B("#D5E3F1"):Ui.B("#1E4566"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Padding=new Thickness(8,4,6,4),Child=inner }); Margin=new Thickness(0,3,0,3); }
 }
 public static class GteGridExt {
  public static Grid WithLeft(this Grid g,double width) { g.ColumnDefinitions[0].Width=new GridLength(width); g.ColumnDefinitions[2].Width=new GridLength(1,GridUnitType.Star); return g; }
 }
}
