using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PTVgame {
 public static class LauncherUpdate {
  public const string Version="1.4.0";
  public const string ManifestUrl="https://raw.githubusercontent.com/Poom99tv/PTVgame/main/release-manifests/launcher.json";
  public static bool IsNewer(string version) { return new System.Version(version)>new System.Version(Version); }
  public static async Task<bool> Check(IProgress<string> progress,CancellationToken token) {
   var game=new Game {id="launcher",manifestUrl=ManifestUrl+"?check="+Guid.NewGuid().ToString("N")};
   progress.Report("กำลังตรวจอัปเดต Launcher…");
   var manifest=await Updater.Check(game,token);
   if(!IsNewer(manifest.version)){progress.Report("Launcher "+Version+" เป็นเวอร์ชันล่าสุดแล้ว");return false;}
   if(manifest.executable!="PTVgame-Launcher.exe")throw new Exception("ไฟล์ Launcher ใน manifest ไม่ถูกต้อง");
   await Updater.Install(game,manifest,progress,token);
   var installed=Storage.Current(game);
   string target=AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
   string work=Path.Combine(Path.GetTempPath(),"PTVgame-update-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
   string helper=Path.Combine(work,"Updater.exe");File.Copy(Process.GetCurrentProcess().MainModule.FileName,helper);
   Process.Start(new ProcessStartInfo(helper,"--apply-launcher "+Process.GetCurrentProcess().Id+" "+Quote(target)+" "+Quote(installed.folder)) {UseShellExecute=false,WorkingDirectory=work});
   return true;
  }
  static string Quote(string value){return "\""+value+"\"";}
  public static bool Handle(string[] args) {
   if(args.Length==0||args[0]!="--apply-launcher")return false;
   try {
    if(args.Length!=4)throw new Exception("Invalid update arguments");
    int pid=int.Parse(args[1]);string target=Path.GetFullPath(args[2]),source=Path.GetFullPath(args[3]);
    try {using(var parent=Process.GetProcessById(pid)){if(!string.Equals(Path.GetDirectoryName(parent.MainModule.FileName),target,StringComparison.OrdinalIgnoreCase))throw new Exception("Launcher process mismatch");if(!parent.WaitForExit(60000))throw new Exception("Launcher ยังไม่ปิด กรุณาลองใหม่");}}catch(ArgumentException){}
    Apply(source,target);
    Process.Start(new ProcessStartInfo(Path.Combine(target,"PTVgame-Launcher.exe")){WorkingDirectory=target,UseShellExecute=false});
   }catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"update-error.txt"),ex.ToString());MessageBox.Show("อัปเดต Launcher ไม่สำเร็จ รุ่นเดิมยังอยู่\n"+ex.Message,"PTVgame");}
   return true;
  }
  public static void Apply(string source,string target) {
   if(!File.Exists(Path.Combine(source,"PTVgame-Launcher.exe"))||!File.Exists(Path.Combine(source,"catalog.json")))throw new Exception("แพ็กเกจ Launcher ไม่ครบ");
   var files=Directory.GetFiles(source,"*",SearchOption.AllDirectories);
   string prefix=Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
   // Only launcher payload files are replaced. Games and user settings live elsewhere.
   foreach(string file in files){string rel=file.Substring(prefix.Length);if(rel!="PTVgame-Launcher.exe"&&rel!="catalog.json"&&rel!="README.md"&&!rel.StartsWith("assets"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Unexpected launcher file: "+rel);}
   string backup=Path.Combine(target,"launcher-backup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(backup);
   var changed=new System.Collections.Generic.List<string>();
   try {
    foreach(string file in files){string rel=file.Substring(prefix.Length),dest=Updater.SafePath(target,rel),saved=Updater.SafePath(backup,rel);Directory.CreateDirectory(Path.GetDirectoryName(dest));if(File.Exists(dest)){Directory.CreateDirectory(Path.GetDirectoryName(saved));File.Copy(dest,saved);File.Copy(file,dest+".update",true);File.Replace(dest+".update",dest,null);}else File.Copy(file,dest);changed.Add(rel);}
   }catch{foreach(string rel in changed.AsEnumerable().Reverse()){string dest=Updater.SafePath(target,rel),saved=Updater.SafePath(backup,rel);if(File.Exists(saved))File.Copy(saved,dest,true);else if(File.Exists(dest))File.Delete(dest);}throw;}
  }
  public static void Test(string root) {
   if(!IsNewer("1.5.0")||IsNewer("1.4.0")||IsNewer("1.3.0"))throw new Exception("Self-update version policy failed");
   string source=Path.Combine(root,"payload"),target=Path.Combine(root,"launcher");Directory.CreateDirectory(source);Directory.CreateDirectory(target);
   File.WriteAllText(Path.Combine(source,"PTVgame-Launcher.exe"),"new");File.WriteAllText(Path.Combine(source,"catalog.json"),"new catalog");
   File.WriteAllText(Path.Combine(target,"PTVgame-Launcher.exe"),"old");File.WriteAllText(Path.Combine(target,"catalog.json"),"old catalog");
   bool failed=false;using(var locked=new FileStream(Path.Combine(target,"catalog.json"),FileMode.Open,FileAccess.ReadWrite,FileShare.None)){try{Apply(source,target);}catch{failed=true;}}
   if(!failed||File.ReadAllText(Path.Combine(target,"PTVgame-Launcher.exe"))!="old")throw new Exception("Self-update rollback failed");
   Apply(source,target);if(File.ReadAllText(Path.Combine(target,"PTVgame-Launcher.exe"))!="new")throw new Exception("Self-update replacement failed");
   File.WriteAllText(Path.Combine(source,"unexpected.dll"),"bad");failed=false;try{Apply(source,target);}catch{failed=true;}if(!failed)throw new Exception("Unexpected update file accepted");
  }
 }
}
