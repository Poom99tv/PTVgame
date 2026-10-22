using System.Linq;
using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Drawing;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

class BridgeRuntime {
 public const string Version="2026.10.23.9";
 public Process TunnelProcess;
 public string PairCode="";
 volatile string tunnelUrl="";
 StringBuilder tunnelErrors=new StringBuilder();
 public string DirectoryPath,Url,Token;
 public Process OwnedProcess;
 string lastNetworkError="";
 StringBuilder processErrors=new StringBuilder();
 public Dictionary<string,object> Config;
 static JavaScriptSerializer Json=new JavaScriptSerializer();
 public BridgeRuntime(string dir){DirectoryPath=dir;}
 public void Prepare(int port){
  Directory.CreateDirectory(DirectoryPath);
  string marker=Path.Combine(DirectoryPath,"version.txt");
  if(!File.Exists(marker)||File.ReadAllText(marker)!=Version||!File.Exists(Path.Combine(DirectoryPath,"node.exe"))){
   using(Stream resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("bridge.payload"))
   using(ZipArchive zip=new ZipArchive(resource,ZipArchiveMode.Read)){
    string root=Path.GetFullPath(DirectoryPath)+Path.DirectorySeparatorChar;
    foreach(ZipArchiveEntry entry in zip.Entries){
     string target=Path.GetFullPath(Path.Combine(DirectoryPath,entry.FullName));
     if(!target.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid package path");
     if(String.IsNullOrEmpty(entry.Name)){Directory.CreateDirectory(target);continue;}
     Directory.CreateDirectory(Path.GetDirectoryName(target));
     using(Stream input=entry.Open())using(FileStream output=File.Create(target))input.CopyTo(output);
    }
   }
   File.WriteAllText(marker,Version);
  }
  string configFile=Path.Combine(DirectoryPath,"config.local.json");
  if(!File.Exists(configFile)){
   string seed=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"tiktok-race-bridge","config.local.json");
   if(File.Exists(seed))File.Copy(seed,configFile);
   else{
    string token=Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N");
    File.WriteAllText(configFile,Json.Serialize(new {token=token,port=port,tiktokUsername=""}),Encoding.UTF8);
   }
  }
  Config=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(configFile));
  Config["port"]=port;File.WriteAllText(configFile,Json.Serialize(Config),new UTF8Encoding(false));
  Token=Convert.ToString(Config["token"]);Url="http://127.0.0.1:"+port;
 }
 public Dictionary<string,object> Request(string path,object body=null){
  HttpWebRequest req=(HttpWebRequest)WebRequest.Create(Url+path);req.Proxy=null;req.Timeout=4000;req.ReadWriteTimeout=4000;req.Headers["Authorization"]="Bearer "+Token;
  if(body!=null){req.Method="POST";req.ContentType="application/json";byte[] bytes=Encoding.UTF8.GetBytes(Json.Serialize(body));req.ContentLength=bytes.Length;using(Stream stream=req.GetRequestStream())stream.Write(bytes,0,bytes.Length);}
  using(HttpWebResponse response=(HttpWebResponse)req.GetResponse())using(StreamReader reader=new StreamReader(response.GetResponseStream()))return Json.Deserialize<Dictionary<string,object>>(reader.ReadToEnd());
 }
 public bool Healthy(){try{Request("/health");return true;}catch(Exception e){lastNetworkError=e.Message;return false;}}
 public void EnsureBackend(){
  if(Healthy())return;
  ProcessStartInfo start=new ProcessStartInfo(Path.Combine(DirectoryPath,"node.exe"),"server.mjs --manual");start.WorkingDirectory=DirectoryPath;start.UseShellExecute=false;start.CreateNoWindow=true;start.WindowStyle=ProcessWindowStyle.Hidden;start.RedirectStandardError=true;start.RedirectStandardOutput=true;
  OwnedProcess=new Process();OwnedProcess.StartInfo=start;
  OwnedProcess.OutputDataReceived+=delegate{};
  OwnedProcess.ErrorDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data!=null)lock(processErrors){if(processErrors.Length>3000)processErrors.Clear();processErrors.AppendLine(e.Data);}};
  OwnedProcess.Start();OwnedProcess.BeginOutputReadLine();OwnedProcess.BeginErrorReadLine();
  for(int i=0;i<150;i++){Thread.Sleep(100);if(Healthy())return;if(OwnedProcess.HasExited)break;}
  throw new IOException(AppLanguage.T("ไม่สามารถเริ่มตัวรับข้อมูล TikTok ได้: ")+processErrors.ToString()+" "+lastNetworkError);
 }
 public void EnsureTunnel(){
  if(TunnelProcess!=null&&!TunnelProcess.HasExited&&PairCode!="")return;
  PairCode="";tunnelUrl="";
  var start=new ProcessStartInfo(Path.Combine(DirectoryPath,"cloudflared.exe"),"tunnel --no-autoupdate --protocol http2 --url "+Url+" --http-host-header "+new Uri(Url).Authority);
  start.WorkingDirectory=DirectoryPath;start.UseShellExecute=false;start.CreateNoWindow=true;start.WindowStyle=ProcessWindowStyle.Hidden;start.RedirectStandardError=true;start.RedirectStandardOutput=true;
  TunnelProcess=new Process();TunnelProcess.StartInfo=start;
  DataReceivedEventHandler line=delegate(object sender,DataReceivedEventArgs e){if(e.Data==null)return;var m=System.Text.RegularExpressions.Regex.Match(e.Data,@"https://([a-z0-9-]+)\.trycloudflare\.com");if(m.Success)tunnelUrl=m.Value;lock(tunnelErrors){if(tunnelErrors.Length>3000)tunnelErrors.Clear();tunnelErrors.AppendLine(e.Data);}};
  TunnelProcess.OutputDataReceived+=line;TunnelProcess.ErrorDataReceived+=line;TunnelProcess.Start();TunnelProcess.BeginOutputReadLine();TunnelProcess.BeginErrorReadLine();
  for(int i=0;i<450;i++){if(tunnelUrl!=""){PairCode="IR7|"+new Uri(tunnelUrl).Host.Replace(".trycloudflare.com","")+"|"+Token;return;}if(TunnelProcess.HasExited)break;Thread.Sleep(100);}
  if(!TunnelProcess.HasExited)TunnelProcess.Kill();
  throw new IOException(AppLanguage.T("เปิด HTTPS ไม่สำเร็จ กรุณาลองเชื่อมต่ออีกครั้ง: ")+tunnelErrors.ToString());
 }
 public void Stop(){if(TunnelProcess!=null&&!TunnelProcess.HasExited){try{TunnelProcess.Kill();}catch{}}if(OwnedProcess!=null&&!OwnedProcess.HasExited){try{OwnedProcess.Kill();OwnedProcess.WaitForExit(2000);}catch{}}}
}

static class PairCodeClipboard {
 public static async Task<bool> TryCopy(string text,Action<string> write){
  for(int attempt=0;attempt<5;attempt++){
   try{write(text);return true;}
   catch(System.Runtime.InteropServices.ExternalException){}
   catch(ThreadStateException){return false;}
   if(attempt<4)await Task.Delay(150);
  }
  return false;
 }
}

class BridgeWindow:Form {
 BridgeRuntime runtime;
 TextBox account;
 Button connect,disconnect;
 Button copyCode;
 Label gameState;
 Label accountLabel;ComboBox language;bool applyingLanguage;
 TextBox pairingCode;
 DateTime copyMessageUntil=DateTime.MinValue;
 Label state,detail,count;
 System.Windows.Forms.Timer timer;
 bool polling=false;
 public BridgeWindow(){
  Text="TikTok LIVE • Island Race v7.0.2";ClientSize=new Size(470,430);MinimumSize=new Size(486,469);MaximumSize=MinimumSize;FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;
  Font=new Font("Segoe UI",10);BackColor=Color.FromArgb(12,20,35);ForeColor=Color.White;
  Label title=new Label(){Text="TikTok LIVE",Location=new Point(24,20),Size=new Size(280,35),Font=new Font("Segoe UI",22,FontStyle.Bold)};Controls.Add(title);
  Label label=new Label(){Text=AppLanguage.T("บัญชี TikTok"),Location=new Point(24,68),Size=new Size(200,24)};Controls.Add(label);accountLabel=label;
  language=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Location=new Point(332,27),Size=new Size(112,28)};language.Items.AddRange(new object[]{"English","ไทย"});language.SelectedIndex=0;Controls.Add(language);language.SelectedIndexChanged+=delegate{if(applyingLanguage)return;AppLanguage.Language=language.SelectedIndex==1?"th":"en";if(runtime!=null){try{File.WriteAllText(Path.Combine(runtime.DirectoryPath,"ui-language.txt"),AppLanguage.Language);}catch(Exception e){detail.Text=e.Message;}}ApplyLanguage();};
  account=new TextBox(){Location=new Point(24,96),Size=new Size(420,30),Font=new Font("Segoe UI",13),BackColor=Color.FromArgb(25,39,57),ForeColor=Color.White,BorderStyle=BorderStyle.FixedSingle};Controls.Add(account);
  connect=new Button(){Text=AppLanguage.T("เชื่อมต่อ"),Location=new Point(24,140),Size=new Size(204,40),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(36,216,188),ForeColor=Color.FromArgb(4,32,32),Enabled=false};connect.FlatAppearance.BorderSize=0;Controls.Add(connect);
  disconnect=new Button(){Text=AppLanguage.T("ตัดการเชื่อมต่อ"),Location=new Point(240,140),Size=new Size(204,40),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(43,58,78),ForeColor=Color.White,Enabled=false};disconnect.FlatAppearance.BorderSize=0;Controls.Add(disconnect);
  state=new Label(){Text=AppLanguage.T("กำลังเตรียมโปรแกรม…"),Location=new Point(24,196),Size=new Size(420,27),Font=new Font("Segoe UI",11,FontStyle.Bold),ForeColor=Color.FromArgb(255,191,64)};Controls.Add(state);
  count=new Label(){Location=new Point(24,226),Size=new Size(420,22),ForeColor=Color.FromArgb(174,194,217)};Controls.Add(count);
  detail=new Label(){Location=new Point(24,250),Size=new Size(420,32),Font=new Font("Segoe UI",8),ForeColor=Color.FromArgb(174,194,217),AutoEllipsis=true};Controls.Add(detail);
  copyCode=new Button(){Text=AppLanguage.T("คัดลอกรหัสจับคู่เกม"),Location=new Point(24,290),Size=new Size(420,36),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(43,58,78),Enabled=false};Controls.Add(copyCode);
  pairingCode=new TextBox(){Name="PairingCode",ReadOnly=true,Location=new Point(24,337),Size=new Size(420,28),Font=new Font("Consolas",10),BackColor=Color.FromArgb(25,39,57),ForeColor=Color.White,BorderStyle=BorderStyle.FixedSingle};Controls.Add(pairingCode);
  pairingCode.Click+=delegate{pairingCode.SelectAll();};
  gameState=new Label(){Text=AppLanguage.T("เปิดห้องส่วนตัว → รูปเฟือง → เชื่อม TikTokBridge"),Location=new Point(24,380),Size=new Size(420,42),ForeColor=Color.FromArgb(174,194,217)};Controls.Add(gameState);
  copyCode.Click+=async delegate{
   if(runtime==null||runtime.PairCode=="")return;
   string code=runtime.PairCode;pairingCode.Text=code;copyCode.Enabled=false;
   bool copied=false;
   try{copied=await PairCodeClipboard.TryCopy(code,value=>Clipboard.SetDataObject(value,true,2,80));}
   catch(Exception){copied=false;}
   finally{copyCode.Enabled=runtime!=null&&runtime.PairCode!="";}
   copyMessageUntil=DateTime.UtcNow.AddSeconds(10);
   gameState.Text=copied?AppLanguage.T("คัดลอกแล้ว • วางรหัสในรูปเฟืองของห้องส่วนตัว"):AppLanguage.T("คลิปบอร์ดไม่ว่าง • คลิกช่องรหัสด้านบน แล้วกด Ctrl+C อีกครั้ง");
   if(!copied){pairingCode.Focus();pairingCode.SelectAll();}
  };
  connect.Click+=async delegate{await Connect();};disconnect.Click+=async delegate{await Disconnect();};
  FormClosing+=delegate{if(timer!=null)timer.Stop();if(runtime!=null)runtime.Stop();};
  Shown+=async delegate{await Initialize();};
 }
 void ApplyLanguage(){accountLabel.Text=AppLanguage.T("บัญชี TikTok");connect.Text=AppLanguage.T("เชื่อมต่อ");disconnect.Text=AppLanguage.T("ตัดการเชื่อมต่อ");copyCode.Text=AppLanguage.T("คัดลอกรหัสจับคู่เกม");gameState.Text=AppLanguage.T("เปิดห้องส่วนตัว → รูปเฟือง → เชื่อม TikTokBridge");copyMessageUntil=DateTime.MinValue;if(timer==null)state.Text=AppLanguage.T("กำลังเตรียมโปรแกรม…");if(runtime!=null&&timer!=null){var pending=Poll();}}
 async Task Initialize(){try{
  string dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IslandRaceTikTokBridgeV7");
  runtime=new BridgeRuntime(dir);
  string preference=Path.Combine(dir,"ui-language.txt");AppLanguage.Language=AppLanguage.Normalize(File.Exists(preference)?File.ReadAllText(preference).Trim():"en");applyingLanguage=true;language.SelectedIndex=AppLanguage.Language=="th"?1:0;applyingLanguage=false;ApplyLanguage();state.Text=AppLanguage.T("กำลังเตรียมโปรแกรม…");
  await Task.Run(delegate{runtime.Prepare(8768);runtime.EnsureBackend();});
  object name;if(runtime.Config.TryGetValue("tiktokUsername",out name))account.Text=Convert.ToString(name);
  connect.Enabled=true;disconnect.Enabled=true;
  timer=new System.Windows.Forms.Timer(){Interval=1500};timer.Tick+=async delegate{await Poll();};timer.Start();await Poll();
 }catch(Exception e){state.Text=AppLanguage.T("เปิดโปรแกรมไม่สำเร็จ");detail.Text=e.Message;}}
 async Task Connect(){
  string name=account.Text.Trim().TrimStart('@');
  if(!System.Text.RegularExpressions.Regex.IsMatch(name,"^[A-Za-z0-9_.]{2,24}$")){state.Text=AppLanguage.T("กรุณาใส่ชื่อ TikTok ให้ถูกต้อง");return;}
  connect.Enabled=false;state.Text=AppLanguage.T("กำลังเปิดการเชื่อมต่อ HTTPS…");try{await Task.Run(delegate{runtime.EnsureBackend();runtime.EnsureTunnel();runtime.Request("/ui/connect",new {username=name});});pairingCode.Text=runtime.PairCode;copyCode.Enabled=true;await Poll();}catch(Exception e){state.Text=AppLanguage.T("เชื่อมต่อไม่สำเร็จ");detail.Text=e.Message;}finally{connect.Enabled=true;}
 }
 async Task Disconnect(){disconnect.Enabled=false;try{await Task.Run(delegate{runtime.Request("/ui/disconnect",new {});});await Poll();}catch(Exception e){state.Text=AppLanguage.T("ตัดการเชื่อมต่อไม่สำเร็จ");detail.Text=e.Message;}finally{disconnect.Enabled=true;}}
 async Task Poll(){if(polling||runtime==null)return;polling=true;try{
  Dictionary<string,object> data=await Task.Run(delegate{return runtime.Request("/ui/state");});
  string status=Convert.ToString(data["status"]);
  state.Text=status=="Connected"?AppLanguage.T("เชื่อมต่อแล้ว • กำลังรับข้อมูล LIVE"):status=="Connecting"?AppLanguage.T("กำลังเชื่อมต่อ…"):status=="Disconnected"?AppLanguage.T("ยังไม่เชื่อมต่อ"):status=="ConnectionError"?AppLanguage.T("เชื่อมต่อไม่สำเร็จ • กำลังลองใหม่"):status;
  state.ForeColor=status=="Connected"?Color.FromArgb(36,216,188):Color.FromArgb(255,191,64);
  object seq;data.TryGetValue("sequence",out seq);count.Text=AppLanguage.T("รับข้อมูลแล้ว ")+Convert.ToString(seq??0)+AppLanguage.T(" รายการ");
  object reason;data.TryGetValue("detail",out reason);detail.Text=Convert.ToString(reason);
  object room;if(DateTime.UtcNow>=copyMessageUntil&&data.TryGetValue("game",out room)){var g=room as Dictionary<string,object>;if(g!=null&&g.ContainsKey("paired")&&Convert.ToBoolean(g["paired"]))gameState.Text=AppLanguage.T("เกมเชื่อมแล้ว • รับข้อมูลแยกเฉพาะห้องของคุณ");else if(runtime.PairCode!="")gameState.Text=AppLanguage.T("รอจับคู่เกม • คัดลอกรหัสไปวางในรูปเฟือง");}
  if(runtime.TunnelProcess!=null&&runtime.TunnelProcess.HasExited){copyCode.Enabled=false;gameState.Text=AppLanguage.T("HTTPS หยุดทำงาน • กดเชื่อมต่ออีกครั้งแล้วจับคู่ใหม่");}
 }catch{state.Text=AppLanguage.T("ตัวรับข้อมูลหยุดทำงาน");detail.Text=AppLanguage.T("กดเชื่อมต่อเพื่อเริ่มใหม่");}finally{polling=false;}}
}

static class Program {
 [STAThread]static int Main(string[] args){
  if(args.Length>0&&args[0]=="--clipboard-test"){
   int attempts=0;bool recovered=PairCodeClipboard.TryCopy("test",delegate(string value){attempts++;if(attempts<3)throw new System.Runtime.InteropServices.ExternalException("busy");}).GetAwaiter().GetResult();
   int failures=0;bool failed=PairCodeClipboard.TryCopy("test",delegate(string value){failures++;throw new System.Runtime.InteropServices.ExternalException("busy");}).GetAwaiter().GetResult();
   bool immediate=PairCodeClipboard.TryCopy("test",delegate(string value){}).GetAwaiter().GetResult();
   bool ok=recovered&&attempts==3&&!failed&&failures==5&&immediate;
   ok=ok&&AppLanguage.Language=="en"&&AppLanguage.T("เชื่อมต่อ")=="Connect"&&AppLanguage.Normalize(null)=="en";
   AppLanguage.Language="th";ok=ok&&AppLanguage.T("เชื่อมต่อ")=="เชื่อมต่อ";AppLanguage.Language="en";
   using(var window=new BridgeWindow()){
    var selector=window.Controls.OfType<ComboBox>().Single();ok=ok&&selector.SelectedIndex==0&&window.Controls.OfType<Button>().Any(b=>b.Text=="Connect");
    selector.SelectedIndex=1;ok=ok&&window.Controls.OfType<Button>().Any(b=>b.Text=="เชื่อมต่อ");selector.SelectedIndex=0;ok=ok&&window.Controls.OfType<Button>().Any(b=>b.Text=="Disconnect");
   }
   if(args.Length>1)File.WriteAllText(args[1],"Clipboard retry/fallback and EN/TH language: "+(ok?"PASS":"FAIL"));return ok?0:1;
  }
  if(args.Length>0&&args[0]=="--smoke-test"){
   string dir=args[1],report=args[2];BridgeRuntime r=new BridgeRuntime(dir);
   try{r.Prepare(18867);r.EnsureBackend();var health=r.Request("/health");var skills=r.Request("/ui/skills",new {version=3,skills=new {rose=new {giftOn=true,gift="1dc1fd9347876fbd",giftCount=1}}});var follow=r.Request("/test-event",new {kind="follow",viewer="smoke"});var events=r.Request("/events");var connect=r.Request("/ui/connect",new {username="smoke_test"});r.Request("/ui/disconnect",new {});File.WriteAllText(report,new JavaScriptSerializer().Serialize(new {ok=true,status=health["status"],settingsSaved=skills["ok"],rawEvents=follow["ok"],connectOk=connect["ok"],bundledNode=File.Exists(Path.Combine(dir,"node.exe"))}));return 0;}
   catch(Exception e){File.WriteAllText(report,new JavaScriptSerializer().Serialize(new {ok=false,error=e.Message}));return 1;}finally{r.Stop();}
  }
  bool first;using(Mutex mutex=new Mutex(true,"Local\\IslandRaceTikTokBridgeV7",out first)){
   if(!first){MessageBox.Show(AppLanguage.T("TikTok Bridge เปิดอยู่แล้ว"),"TikTok LIVE");return 0;}
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new BridgeWindow());return 0;
  }
 }
}

public static class AppLanguage {
 public static string Language="en";
 static readonly Dictionary<string,string> English=new Dictionary<string,string> {{"TikTokBridge ยังไม่พร้อม กรุณาลองใหม่","TikTokBridge is not ready. Please try again"},
{"หน้าแรก","Home"},
{"ไลบรารี","Library"},
{"รายการโปรด","Favorites"},
{"การตั้งค่า","Settings"},
{"ค้นหาเกม…","Search games…"},
{"ตรวจอัปเดตเกมที่ติดตั้ง","Check installed game updates"},
{"ผู้เล่น","Player"},
{"พร้อมใช้งาน","Ready"},
{"กำลังดำเนินการ…","Working…"},
{"พร้อมเล่น • เลือกเกมที่คุณชอบ","Ready to play • Choose a game"},
{"ยกเลิกดาวน์โหลด","Cancel download"},
{"ยังไม่มีเกมในหน้านี้","No games to show"},
{"ดูทั้งหมด","View all"},
{"เกมทั่วไป","PC games"},
{"เกม Roblox","Roblox games"},
{"เปิดแมพ Roblox","Open Roblox experience"},
{"เปิดรายละเอียดและเล่นเกม","View details and play"},
{"ติดตั้งเกม","Install game"},
{"เล่นเกม","Play game"},
{"กลับไปหน้าเกม","Back to games"},
{"ยังไม่ได้ติดตั้ง","Not installed"},
{"ติดตั้งแล้ว • v","Installed • v"},
{"เปิด Roblox","Open Roblox"},
{"ตรวจสอบอัปเดต","Check for updates"},
{"นำออกจากรายการโปรด","Remove from favorites"},
{"เพิ่มรายการโปรด","Add to favorites"},
{"ธีมโปรแกรม","Theme"},
{"อัปเดตเกมที่ติดตั้งเมื่อเปิด Launcher","Update installed games when Launcher starts"},
{"ตรวจอัปเดต Launcher","Check Launcher updates"},
{"เปิดโฟลเดอร์เกม","Open games folder"},
{"ภาพปก Roblox จากหน้าเกมจริง","Roblox artwork from the game page"},
{"ดาวน์โหลดเกมผ่าน GitHub Releases","Game downloads from GitHub Releases"},
{"กำลังยกเลิก กรุณารอแล้วปิดอีกครั้ง","Cancelling. Please wait before closing."},
{"ตรวจอัปเดต Launcher ไม่สำเร็จ: ","Could not check Launcher updates: "},
{"บันทึกการตั้งค่าไม่สำเร็จ: ","Could not save settings: "},
{"Roblox placeId ไม่ถูกต้อง","Invalid Roblox place ID"},
{"กำลังเตรียม TikTokBridge สำหรับ ","Preparing TikTokBridge for "},
{" และ TikTokBridge แล้ว • จับคู่ในห้องส่วนตัวผ่านรูปเฟือง"," and TikTokBridge opened • Pair in your private server using the gear"},
{" ใน Roblox"," in Roblox"},
{"ยกเลิกการเปิดเกมแล้ว","Game launch cancelled"},
{" ไม่สำเร็จ: "," failed: "},
{"กำลังตรวจเวอร์ชันก่อนเล่น ","Checking the version before launching "},
{"จำเป็นต้องอัปเดต ","Update required for "},
{" ก่อนเล่น…"," before playing…"},
{"ยกเลิกแล้ว • ต้องอัปเดตให้สำเร็จก่อนเล่น","Cancelled • Complete the update before playing"},
{"ยังเปิดเกมไม่ได้: ","Cannot launch the game yet: "},
{" • ต้องเชื่อมต่อและตรวจเวอร์ชันให้สำเร็จ"," • Connect and finish checking for updates"},
{"กำลังตรวจเวอร์ชัน ","Checking version for "},
{" เป็นเวอร์ชันล่าสุดแล้ว"," is up to date"},
{"ยกเลิกแล้ว • เวอร์ชันเดิมยังใช้งานได้","Cancelled • Your previous version is still available"},
{"ติดตั้งไม่สำเร็จ: ","Installation failed: "},
{"เปิด ","Opened "},
{" แล้ว"," successfully"},
{"ติดตั้ง ","Install "},
{"Game ID ไม่ถูกต้อง","Invalid game ID"},
{"ต้องใช้ URL แบบ HTTPS","An HTTPS URL is required"},
{"Manifest มีขนาดใหญ่เกินไป","Manifest is too large"},
{"เวอร์ชันต้องเป็นตัวเลข เช่น 1.0.0","Version must be numeric, for example 1.0.0"},
{"ขนาด ZIP ใน manifest ไม่ถูกต้อง (สูงสุด 20 GB)","Invalid ZIP size in manifest (maximum 20 GB)"},
{"ต้องระบุ SHA-256 ของ ZIP","A ZIP SHA-256 checksum is required"},
{"ตำแหน่ง executable ไม่ถูกต้อง","Invalid executable path"},
{"ต้องระบุไฟล์ .exe","An .exe file is required"},
{"ZIP มีตำแหน่งไฟล์ที่ไม่ปลอดภัย","ZIP contains an unsafe file path"},
{"พื้นที่ว่างไม่เพียงพอ","Not enough disk space"},
{"ขนาดไฟล์บนเซิร์ฟเวอร์ไม่ตรงกับ manifest","Server file size does not match the manifest"},
{"ไฟล์ดาวน์โหลดมีขนาดเกินกำหนด","Download exceeds the expected size"},
{"กำลังดาวน์โหลด  ","Downloading  "},
{"ไฟล์ดาวน์โหลดไม่ครบ","Download is incomplete"},
{"กำลังตรวจสอบ SHA-256…","Verifying SHA-256…"},
{"SHA-256 ไม่ตรงกัน ยกเลิกการติดตั้ง","SHA-256 mismatch. Installation cancelled"},
{"กำลังติดตั้ง…","Installing…"},
{"ไม่พบไฟล์เกม: ","Game file not found: "},
{"ติดตั้งสำเร็จ • v","Installed successfully • v"},
{"ZIP มีไฟล์มากเกินไป","ZIP contains too many files"},
{"ZIP แตกไฟล์เกิน 40 GB","ZIP expands beyond 40 GB"},
{"ไม่รองรับ symbolic link ใน ZIP","Symbolic links in ZIP files are not supported"},
{"ยังไม่ได้ติดตั้งเกม","Game is not installed"},
{"ตำแหน่งติดตั้งไม่ถูกต้อง","Invalid installation path"},
{"ไม่พบไฟล์เกม กรุณาติดตั้งใหม่","Game file not found. Please reinstall"},
{"กำลังตรวจอัปเดต Launcher…","Checking Launcher updates…"},
{"ไฟล์ Launcher ใน manifest ไม่ถูกต้อง","Invalid Launcher executable in manifest"},
{"Launcher ยังไม่ปิด กรุณาปิดแล้วลองใหม่","Launcher is still running. Close it and try again"},
{"อัปเดต Launcher ไม่สำเร็จ รุ่นเดิมยังอยู่","Launcher update failed. Your previous version is preserved"},
{"แพ็กเกจ Launcher ไม่ครบ","Launcher package is incomplete"},
{"ไม่สามารถเริ่มตัวรับข้อมูล TikTok ได้: ","Could not start the TikTok receiver: "},
{"เปิด HTTPS ไม่สำเร็จ กรุณาลองเชื่อมต่ออีกครั้ง: ","Could not start HTTPS. Please reconnect: "},
{"บัญชี TikTok","TikTok account"},
{"เชื่อมต่อ","Connect"},
{"ตัดการเชื่อมต่อ","Disconnect"},
{"กำลังเตรียมโปรแกรม…","Preparing application…"},
{"คัดลอกรหัสจับคู่เกม","Copy game pairing code"},
{"เปิดห้องส่วนตัว → รูปเฟือง → เชื่อม TikTokBridge","Open a private server → Gear → TikTokBridge"},
{"คัดลอกแล้ว • วางรหัสในรูปเฟืองของห้องส่วนตัว","Copied • Paste the code in your private server settings"},
{"คลิปบอร์ดไม่ว่าง • คลิกช่องรหัสด้านบน แล้วกด Ctrl+C อีกครั้ง","Clipboard busy • Select the code above and press Ctrl+C"},
{"เปิดโปรแกรมไม่สำเร็จ","Could not start the application"},
{"กรุณาใส่ชื่อ TikTok ให้ถูกต้อง","Please enter a valid TikTok username"},
{"กำลังเปิดการเชื่อมต่อ HTTPS…","Starting HTTPS connection…"},
{"เชื่อมต่อไม่สำเร็จ","Connection failed"},
{"ตัดการเชื่อมต่อไม่สำเร็จ","Could not disconnect"},
{"เชื่อมต่อแล้ว • กำลังรับข้อมูล LIVE","Connected • Receiving LIVE events"},
{"กำลังเชื่อมต่อ…","Connecting…"},
{"ยังไม่เชื่อมต่อ","Disconnected"},
{"เชื่อมต่อไม่สำเร็จ • กำลังลองใหม่","Connection failed • Retrying"},
{"รับข้อมูลแล้ว ","Received "},
{" รายการ"," events"},
{"เกมเชื่อมแล้ว • รับข้อมูลแยกเฉพาะห้องของคุณ","Game paired • Events are isolated to your server"},
{"รอจับคู่เกม • คัดลอกรหัสไปวางในรูปเฟือง","Waiting for game pairing • Paste the code in game settings"},
{"HTTPS หยุดทำงาน • กดเชื่อมต่ออีกครั้งแล้วจับคู่ใหม่","HTTPS stopped • Reconnect and pair your game again"},
{"ตัวรับข้อมูลหยุดทำงาน","Receiver stopped"},
{"กดเชื่อมต่อเพื่อเริ่มใหม่","Click Connect to restart"},
{"TikTok Bridge เปิดอยู่แล้ว","TikTok Bridge is already running"}};
 static readonly string[] Keys=English.Keys.OrderByDescending(k=>k.Length).ToArray();
 public static string Normalize(string value){return value=="th"?"th":"en";}
 public static string T(string value){if(value==null||Language=="th")return value;foreach(string key in Keys)value=value.Replace(key,English[key]);return value;}
}
