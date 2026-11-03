using System;
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
 // It replaces TikTokBridge for Roblox maps and is managed by the Launcher like a game:
 //  1) <Launcher folder>\GameToEarn\PTVGameToEarn.exe  (local / bundled copy, used first)
 //  2) otherwise downloaded + updated from release-manifests/gametoearn.json into %LOCALAPPDATA%\PTVgame\games\gametoearn
 public static class GameToEarn {
  public const string ProcessName="PTVGameToEarn";
  public static readonly Game Tool=new Game { id="gametoearn",name="Game To Earn",category="Tool",manifestUrl="https://raw.githubusercontent.com/Poom99tv/PTVgame/main/release-manifests/gametoearn.json" };
  public static string LocalExe { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"GameToEarn",ProcessName+".exe"); } }
  public static bool HasLocal { get { return File.Exists(LocalExe); } }
  public static bool Running { get { try { return Process.GetProcessesByName(ProcessName).Length>0; } catch { return false; } } }
  public static Installed Installed { get { try { return Storage.Current(Tool); } catch { return null; } } }
  public static bool Available { get { return HasLocal||Installed!=null; } }
  public static string VersionText { get { if(HasLocal) return AppLanguage.T("ติดตั้งพร้อม Launcher"); var i=Installed; return i==null?AppLanguage.T("ยังไม่ได้ติดตั้ง"):AppLanguage.T("เวอร์ชัน ")+i.version; } }
  static void StartLocal() { Process.Start(new ProcessStartInfo(LocalExe) { WorkingDirectory=Path.GetDirectoryName(LocalExe),UseShellExecute=false }); }
  // Installs/updates when needed, then starts it (no second copy if it is already open).
  public static async Task<bool> Open(IProgress<string> progress,CancellationToken token,bool update) {
   if(Running) return false;
   if(HasLocal) { StartLocal(); return true; }
   if(update||Installed==null) {
    progress.Report("กำลังตรวจ Game To Earn…");
    try {
     var manifest=await Updater.Check(Tool,token);
     if(Updater.Newer(manifest,Installed)) await Updater.Install(Tool,manifest,progress,token);
    } catch(OperationCanceledException) { throw; }
    catch(Exception) { if(Installed==null) throw; }   // offline: keep the installed copy
   }
   token.ThrowIfCancellationRequested();
   Updater.Play(Tool); return true;
  }
 }

 public partial class LauncherShell {
  const string PageGte="Game To Earn";

  async Task OpenGameToEarn(bool fromRoblox) {
   if(GameToEarn.Running) { if(!fromRoblox) status.Text=AppLanguage.T("Game To Earn เปิดอยู่แล้ว"); return; }
   await GameToEarn.Open(new Progress<string>(t=>status.Text=AppLanguage.T(t)),cancellation==null?CancellationToken.None:cancellation.Token,true);
   if(!fromRoblox) status.Text=AppLanguage.T("เปิด Game To Earn แล้ว • ใส่ชื่อ TikTok แล้วกดเชื่อมต่อ");
  }

  async void GteButton() {
   if(busy||!Gate(GameToEarn.Tool)) return;
   busy=true; cancellation=new CancellationTokenSource(); progress.Visibility=Visibility.Visible; cancel.Visibility=Visibility.Visible; Render();
   try { await OpenGameToEarn(false); }
   catch(OperationCanceledException) { status.Text=AppLanguage.T("ยกเลิกแล้ว"); }
   catch(Exception ex) { status.Text=AppLanguage.T("เปิด Game To Earn ไม่สำเร็จ: "+ex.Message); }
   finally { busy=false; cancellation.Dispose(); cancellation=null; progress.Visibility=Visibility.Collapsed; cancel.Visibility=Visibility.Collapsed; Render(); }
  }

  void GameToEarnPage() {
   var head=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,4,0,18) };
   var icon=new Border { Width=74,Height=74,CornerRadius=new CornerRadius(18),Background=new LinearGradientBrush(Color.FromRgb(37,244,238),Color.FromRgb(254,44,85),45),Child=new TextBlock { Text="♪",FontSize=40,FontWeight=FontWeights.Bold,Foreground=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center } };
   head.Children.Add(icon);
   var who=new StackPanel { Margin=new Thickness(18,0,0,0),VerticalAlignment=VerticalAlignment.Center }; head.Children.Add(who);
   who.Children.Add(Ui.Bold(Line("Game To Earn",30)));
   who.Children.Add(Line(AppLanguage.T("เชื่อม TikTok LIVE ภายนอก → กดคีย์ในเกม Roblox อัตโนมัติ"),15,muted));
   var pills=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,9,0,0) }; who.Children.Add(pills);
   pills.Children.Add(Ui.Pill(GameToEarn.Running?"● กำลังเปิดอยู่":GameToEarn.VersionText,GameToEarn.Running?ColorBrush("#0F4A2E"):ColorBrush("#193B58"),GameToEarn.Running?ColorBrush("#5CF29B"):Brushes.White));
   if(Locked(GameToEarn.Tool)) pills.Children.Add(Ui.Pill("♛ VIP",Ui.Gold,Ui.B("#2A1C00")));
   sections.Children.Add(head);

   var actions=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,18) };
   bool locked=Locked(GameToEarn.Tool);
   var open=Btn(locked?"♛  ปลดล็อกด้วย VIP":GameToEarn.Running?"✓  เปิดอยู่แล้ว":GameToEarn.Available?"▶  เปิด Game To Earn":"⇩  ติดตั้งและเปิด",locked?Ui.Gold:accent);
   open.FontSize=16; open.FontWeight=FontWeights.Bold; open.Padding=new Thickness(26,12,26,12); open.IsEnabled=!busy;
   if(locked) open.Foreground=Ui.B("#2A1C00");
   open.Click+=(s,e)=>{ if(Locked(GameToEarn.Tool)) Go(PageStore); else GteButton(); };
   actions.Children.Add(open);
   var auto=new CheckBox { Content=AppLanguage.T("เปิดอัตโนมัติเมื่อเล่นแมพ Roblox"),IsChecked=settings.gameToEarnWithRoblox,Foreground=Foreground,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(20,0,0,0) };
   auto.Click+=(s,e)=>{ settings.gameToEarnWithRoblox=auto.IsChecked==true; Save(); };
   actions.Children.Add(auto);
   sections.Children.Add(actions);

   var how=new StackPanel();
   how.Children.Add(Ui.Bold(Line(AppLanguage.T("วิธีใช้"),17)));
   var steps=new[]{
    "1. เปิดแมพ Roblox จากหน้าแรก (Game To Earn จะเปิดตามให้อัตโนมัติ)",
    "2. ใส่ชื่อ TikTok (@username) ที่กำลังไลฟ์ แล้วกด เชื่อมต่อ",
    "3. เลือก Preset ของแมพ • ตั้งค่าว่าของขวัญ / ถูกใจ / ติดตาม จะกดคีย์อะไร",
    "4. คลิกหน้าต่าง Roblox ให้ Active ก่อน เพื่อให้รับคีย์ได้ชัวร์",
    "5. หน้าจอซ้อนสำหรับ OBS: ยอดกด Like, ยอดส่งของขวัญ, นับ Win, แก้วโหลของขวัญ"};
   foreach(var t in steps) { var l=Line(AppLanguage.T(t),14,muted); l.Margin=new Thickness(0,7,0,0); l.TextWrapping=TextWrapping.Wrap; how.Children.Add(l); }
   sections.Children.Add(Box(how));
   var note=Line(AppLanguage.T("ไม่ต้องใช้ License แยกแล้ว • สิทธิ์ใช้งานตามบัญชี PTVgame • ธีมและภาษาตาม Launcher"),12,muted);
   note.Margin=new Thickness(0,14,0,0); note.TextWrapping=TextWrapping.Wrap; sections.Children.Add(note);
  }
 }
}
