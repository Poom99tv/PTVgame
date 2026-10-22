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
 public const string Version="2026.10.23.7";
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
  throw new IOException("ไม่สามารถเริ่มตัวรับข้อมูล TikTok ได้: "+processErrors.ToString()+" "+lastNetworkError);
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
  throw new IOException("เปิด HTTPS ไม่สำเร็จ กรุณาลองเชื่อมต่ออีกครั้ง: "+tunnelErrors.ToString());
 }
 public void Stop(){if(TunnelProcess!=null&&!TunnelProcess.HasExited){try{TunnelProcess.Kill();}catch{}}if(OwnedProcess!=null&&!OwnedProcess.HasExited){try{OwnedProcess.Kill();OwnedProcess.WaitForExit(2000);}catch{}}}
}

class BridgeWindow:Form {
 BridgeRuntime runtime;
 TextBox account;
 Button connect,disconnect;
 Button copyCode;
 Label gameState;
 Label state,detail,count;
 System.Windows.Forms.Timer timer;
 bool polling=false;
 public BridgeWindow(){
  Text="TikTok LIVE • Island Race v7";ClientSize=new Size(470,382);MinimumSize=new Size(486,421);MaximumSize=MinimumSize;FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;
  Font=new Font("Segoe UI",10);BackColor=Color.FromArgb(12,20,35);ForeColor=Color.White;
  Label title=new Label(){Text="TikTok LIVE",Location=new Point(24,20),Size=new Size(410,35),Font=new Font("Segoe UI",22,FontStyle.Bold)};Controls.Add(title);
  Label label=new Label(){Text="บัญชี TikTok",Location=new Point(24,68),Size=new Size(200,24)};Controls.Add(label);
  account=new TextBox(){Location=new Point(24,96),Size=new Size(420,30),Font=new Font("Segoe UI",13),BackColor=Color.FromArgb(25,39,57),ForeColor=Color.White,BorderStyle=BorderStyle.FixedSingle};Controls.Add(account);
  connect=new Button(){Text="เชื่อมต่อ",Location=new Point(24,140),Size=new Size(204,40),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(36,216,188),ForeColor=Color.FromArgb(4,32,32),Enabled=false};connect.FlatAppearance.BorderSize=0;Controls.Add(connect);
  disconnect=new Button(){Text="ตัดการเชื่อมต่อ",Location=new Point(240,140),Size=new Size(204,40),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(43,58,78),ForeColor=Color.White,Enabled=false};disconnect.FlatAppearance.BorderSize=0;Controls.Add(disconnect);
  state=new Label(){Text="กำลังเตรียมโปรแกรม…",Location=new Point(24,196),Size=new Size(420,27),Font=new Font("Segoe UI",11,FontStyle.Bold),ForeColor=Color.FromArgb(255,191,64)};Controls.Add(state);
  count=new Label(){Location=new Point(24,226),Size=new Size(420,22),ForeColor=Color.FromArgb(174,194,217)};Controls.Add(count);
  detail=new Label(){Location=new Point(24,250),Size=new Size(420,32),Font=new Font("Segoe UI",8),ForeColor=Color.FromArgb(174,194,217),AutoEllipsis=true};Controls.Add(detail);
  copyCode=new Button(){Text="คัดลอกรหัสจับคู่เกม",Location=new Point(24,290),Size=new Size(420,36),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(43,58,78),Enabled=false};Controls.Add(copyCode);
  gameState=new Label(){Text="เปิดห้องส่วนตัว → รูปเฟือง → เชื่อม TikTokBridge",Location=new Point(24,338),Size=new Size(420,34),ForeColor=Color.FromArgb(174,194,217)};Controls.Add(gameState);
  copyCode.Click+=delegate{if(runtime!=null&&runtime.PairCode!=""){Clipboard.SetText(runtime.PairCode);gameState.Text="คัดลอกแล้ว • วางรหัสในรูปเฟืองของห้องส่วนตัว";}};
  connect.Click+=async delegate{await Connect();};disconnect.Click+=async delegate{await Disconnect();};
  FormClosing+=delegate{if(timer!=null)timer.Stop();if(runtime!=null)runtime.Stop();};
  Shown+=async delegate{await Initialize();};
 }
 async Task Initialize(){try{
  string dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IslandRaceTikTokBridgeV7");
  runtime=new BridgeRuntime(dir);
  await Task.Run(delegate{runtime.Prepare(8768);runtime.EnsureBackend();});
  object name;if(runtime.Config.TryGetValue("tiktokUsername",out name))account.Text=Convert.ToString(name);
  connect.Enabled=true;disconnect.Enabled=true;
  timer=new System.Windows.Forms.Timer(){Interval=1500};timer.Tick+=async delegate{await Poll();};timer.Start();await Poll();
 }catch(Exception e){state.Text="เปิดโปรแกรมไม่สำเร็จ";detail.Text=e.Message;}}
 async Task Connect(){
  string name=account.Text.Trim().TrimStart('@');
  if(!System.Text.RegularExpressions.Regex.IsMatch(name,"^[A-Za-z0-9_.]{2,24}$")){state.Text="กรุณาใส่ชื่อ TikTok ให้ถูกต้อง";return;}
  connect.Enabled=false;state.Text="กำลังเปิดการเชื่อมต่อ HTTPS…";try{await Task.Run(delegate{runtime.EnsureBackend();runtime.EnsureTunnel();runtime.Request("/ui/connect",new {username=name});});copyCode.Enabled=true;await Poll();}catch(Exception e){state.Text="เชื่อมต่อไม่สำเร็จ";detail.Text=e.Message;}finally{connect.Enabled=true;}
 }
 async Task Disconnect(){disconnect.Enabled=false;try{await Task.Run(delegate{runtime.Request("/ui/disconnect",new {});});await Poll();}catch(Exception e){state.Text="ตัดการเชื่อมต่อไม่สำเร็จ";detail.Text=e.Message;}finally{disconnect.Enabled=true;}}
 async Task Poll(){if(polling||runtime==null)return;polling=true;try{
  Dictionary<string,object> data=await Task.Run(delegate{return runtime.Request("/ui/state");});
  string status=Convert.ToString(data["status"]);
  state.Text=status=="Connected"?"เชื่อมต่อแล้ว • กำลังรับข้อมูล LIVE":status=="Connecting"?"กำลังเชื่อมต่อ…":status=="Disconnected"?"ยังไม่เชื่อมต่อ":status=="ConnectionError"?"เชื่อมต่อไม่สำเร็จ • กำลังลองใหม่":status;
  state.ForeColor=status=="Connected"?Color.FromArgb(36,216,188):Color.FromArgb(255,191,64);
  object seq;data.TryGetValue("sequence",out seq);count.Text="รับข้อมูลแล้ว "+Convert.ToString(seq??0)+" รายการ";
  object reason;data.TryGetValue("detail",out reason);detail.Text=Convert.ToString(reason);
  object room;if(data.TryGetValue("game",out room)){var g=room as Dictionary<string,object>;if(g!=null&&g.ContainsKey("paired")&&Convert.ToBoolean(g["paired"]))gameState.Text="เกมเชื่อมแล้ว • รับข้อมูลแยกเฉพาะห้องของคุณ";else if(runtime.PairCode!="")gameState.Text="รอจับคู่เกม • คัดลอกรหัสไปวางในรูปเฟือง";}
  if(runtime.TunnelProcess!=null&&runtime.TunnelProcess.HasExited){copyCode.Enabled=false;gameState.Text="HTTPS หยุดทำงาน • กดเชื่อมต่ออีกครั้งแล้วจับคู่ใหม่";}
 }catch{state.Text="ตัวรับข้อมูลหยุดทำงาน";detail.Text="กดเชื่อมต่อเพื่อเริ่มใหม่";}finally{polling=false;}}
}

static class Program {
 [STAThread]static int Main(string[] args){
  if(args.Length>0&&args[0]=="--smoke-test"){
   string dir=args[1],report=args[2];BridgeRuntime r=new BridgeRuntime(dir);
   try{r.Prepare(18867);r.EnsureBackend();var health=r.Request("/health");var skills=r.Request("/ui/skills",new {version=3,skills=new {rose=new {giftOn=true,gift="1dc1fd9347876fbd",giftCount=1}}});var follow=r.Request("/test-event",new {kind="follow",viewer="smoke"});var events=r.Request("/events");var connect=r.Request("/ui/connect",new {username="smoke_test"});r.Request("/ui/disconnect",new {});File.WriteAllText(report,new JavaScriptSerializer().Serialize(new {ok=true,status=health["status"],settingsSaved=skills["ok"],rawEvents=follow["ok"],connectOk=connect["ok"],bundledNode=File.Exists(Path.Combine(dir,"node.exe"))}));return 0;}
   catch(Exception e){File.WriteAllText(report,new JavaScriptSerializer().Serialize(new {ok=false,error=e.Message}));return 1;}finally{r.Stop();}
  }
  bool first;using(Mutex mutex=new Mutex(true,"Local\\IslandRaceTikTokBridgeV7",out first)){
   if(!first){MessageBox.Show("TikTok Bridge เปิดอยู่แล้ว","TikTok LIVE");return 0;}
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new BridgeWindow());return 0;
  }
 }
}
