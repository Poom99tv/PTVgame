using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class Recovery {
 [DllImport("shell32.dll",SetLastError=true)]static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)]string command,out int count);
 [DllImport("kernel32.dll")]static extern IntPtr LocalFree(IntPtr memory);
 static string[] Parse(string command){int count;var ptr=CommandLineToArgvW(command,out count);if(ptr==IntPtr.Zero)throw new Exception("อ่านคำสั่งอัปเดตไม่สำเร็จ");try{var a=new string[count];for(int i=0;i<count;i++)a[i]=Marshal.PtrToStringUni(Marshal.ReadIntPtr(ptr,i*IntPtr.Size));return a;}finally{LocalFree(ptr);}}
 static bool Within(string path,string root){return Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}
 static bool ParentGone(int pid){try{using(var p=Process.GetProcessById(pid))return p.WaitForExit(1000);}catch(ArgumentException){return true;}}
 static void Validate(string[] a){
  if(a.Length!=5||a[1]!="--apply-launcher")throw new Exception("รูปแบบคำสั่งไม่ถูกต้อง");
  int pid;if(!int.TryParse(a[2],out pid)||pid<=0)throw new Exception("PID ไม่ถูกต้อง");
  if(!Within(a[0],Path.GetTempPath())||Path.GetFileName(a[0])!="Updater.exe"||!Path.GetFileName(Path.GetDirectoryName(a[0])).StartsWith("PTVgame-update-",StringComparison.Ordinal))throw new Exception("ไม่ใช่ Updater ของ PTVgame");
  string cache=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PTVgame","games","launcher");
  if(!Within(a[4],cache)||!Path.GetFileName(a[4]).StartsWith("build-",StringComparison.Ordinal))throw new Exception("ไม่ใช่แพ็กเกจที่ Launcher ดาวน์โหลดไว้");
  if(!File.Exists(a[0])||!File.Exists(Path.Combine(a[3],"PTVgame-Launcher.exe"))||!File.Exists(Path.Combine(a[4],"PTVgame-Launcher.exe"))||!File.Exists(Path.Combine(a[4],"catalog.json")))throw new Exception("ไฟล์อัปเดตไม่ครบ กรุณาเปิดตัวเก่าแล้วกดตรวจอัปเดตอีกครั้ง");
 }
 [STAThread]static int Main(string[] args){
  if(args.Length>0&&args[0]=="--self-test"){
   var parsed=Parse("\"C:\\Some Folder\\Updater.exe\" --apply-launcher 123 \"C:\\Games Folder\" \"C:\\Cache Folder\"");
   bool ok=parsed.Length==5&&parsed[3]=="C:\\Games Folder"&&ParentGone(int.MaxValue)&&!Within("C:\\safe-other\\file","C:\\safe");
   if(args.Length>1)File.WriteAllText(args[1],ok?"Recovery parser/exit/path tests: PASS":"FAIL");return ok?0:1;
  }
  Application.EnableVisualStyles();
  try{
   string[] command=null;int updaterPid=0;
   using(var search=new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='Updater.exe'")){
    foreach(ManagementObject item in search.Get()){
     string text=Convert.ToString(item["CommandLine"]);if(!text.Contains("--apply-launcher"))continue;
     var candidate=Parse(text);try{Validate(candidate);}catch{continue;}
     if(command!=null)throw new Exception("พบหลายรายการอัปเดต กรุณาปิดให้เหลือหน้าต่างข้อผิดพลาดของ PTVgame เพียงรายการเดียว");
     command=candidate;updaterPid=Convert.ToInt32(item["ProcessId"]);
    }
   }
   if(command==null)throw new Exception("กรุณาเปิด Launcher ตัวเก่าและกดอัปเดตก่อน เมื่อขึ้นหน้าต่างข้อผิดพลาด ให้เปิดไฟล์ซ่อมนี้โดยปล่อยหน้าต่างข้อผิดพลาดไว้");
   if(!ParentGone(int.Parse(command[2])))throw new Exception("กรุณาปิดหน้าต่าง Launcher ตัวหลักก่อน แล้วเปิดไฟล์ซ่อมอีกครั้ง ปล่อยหน้าต่าง Updater ที่แจ้งข้อผิดพลาดไว้");
   using(var old=Process.GetProcessById(updaterPid)){old.CloseMainWindow();if(!old.WaitForExit(10000))throw new Exception("กรุณากด OK ที่หน้าต่างข้อผิดพลาดของ Updater แล้วลองใหม่");}
   // The original signed-in-user updater and downloaded payload are reused.
   // No files are patched, no process memory is read, and no process is killed.
   // Parent is confirmed exited; an absent PID bypasses the old faulty lookup.
   if(!ParentGone(int.MaxValue))throw new Exception("โปรดลองใหม่");
   string arguments="--apply-launcher "+int.MaxValue+" \""+command[3]+"\" \""+command[4]+"\"";
   Process.Start(new ProcessStartInfo(command[0],arguments){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(command[0])});
   return 0;
  }catch(Exception e){MessageBox.Show(e.Message,"PTVgame • กู้การอัปเดต",MessageBoxButtons.OK,MessageBoxIcon.Information);return 1;}
 }
}
