using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PTVgame;

public static class FeatureCheck {
 public static int DownCount;
 static readonly BindingFlags F=BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly;
 static object Get(object o,string n){return o.GetType().GetField(n,F).GetValue(o);}
 static object Call(object o,string n,params object[] args){return o.GetType().GetMethod(n,F).Invoke(o,args);}
 static void Check(bool b,string message){if(!b)throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);}
 static IEnumerable<DependencyObject> Tree(DependencyObject o){yield return o;foreach(object c in LogicalTreeHelper.GetChildren(o)){var d=c as DependencyObject;if(d!=null)foreach(var x in Tree(d))yield return x;}}
 static void Snapshot(Window w,string name){var root=(FrameworkElement)w.Content;root.Measure(new Size(1160,900));root.Arrange(new Rect(0,0,1160,900));root.UpdateLayout();var image=new RenderTargetBitmap(1160,900,96,96,PixelFormats.Pbgra32);image.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var f=File.Create(name))png.Save(f);}
 [STAThread] public static int Main(){
  try{
   string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"dist","feature-settings-"+Guid.NewGuid().ToString("N"));
   typeof(Storage).GetField("Root",BindingFlags.Public|BindingFlags.Static).SetValue(null,root);
   AccountConfig.Current=new AccountConfig();AccountService.Current=null;
   GteEngine.LastError="Offline test: LIVE engine is not started";
   var app=new Application();var shell=new LauncherShell();
   typeof(LauncherShell).GetField("page",F).SetValue(shell,"Game To Earn");
   Call(shell,"Render");
   var games=(List<Game>)Get(shell,"games");
   Check(GteRouting.SignalOnly(games.Single(g=>g.id=="ptvrunner")),"PTVRunner uses raw signals");
   Check(!GteRouting.SignalOnly(games.First(g=>g.category=="Roblox"&&g.id!="ptvrunner")),"other maps retain presets");
   string presetLabel=AppLanguage.T("Preset และคำสั่ง");
   Check(!Tree((DependencyObject)shell.Content).OfType<Button>().Any(b=>Convert.ToString(b.Content).Contains(presetLabel)),"PTVRunner sidebar hides Presets & commands");
   Check(Tree((DependencyObject)shell.Content).OfType<ComboBox>().Count()==1,"PTVRunner LIVE page only has the map selector, no preset selector");
   var state=(Dictionary<string,object>)Get(shell,"gteState");state["win"]=new Dictionary<string,object>{{"count",0},{"max",1000}};
   var rows=(List<GteRow>)Get(shell,"gteRows");rows.Clear();rows.Add(new GteRow{name="Rose",key="v",win=true,delta=1});
   var presets=(List<Dictionary<string,object>>)Get(shell,"gtePresets");var p=presets[(int)Get(shell,"gteIdx")];
   p["like_enabled"]=true;p["like_target"]=1;p["like_seq"]="v";p["follow_enabled"]=true;p["follow_seq"]="r";
   Call(shell,"GteHandle",new Dictionary<string,object>{{"kind","gift"},{"giftName","Rose"},{"amount",300},{"diamonds",300},{"viewer","tester"}});
   Call(shell,"GteHandle",new Dictionary<string,object>{{"kind","like"},{"amount",1000},{"viewer","tester"}});
   Call(shell,"GteHandle",new Dictionary<string,object>{{"kind","follow"},{"viewer","tester"}});
   Thread.Sleep(150);
   Check(DownCount==0,"gift/like/follow produce zero key presses for PTVRunner");
   Check(GteData.Int(GteData.Sub(state,"win"),"count",0)==0,"PTVRunner ignores preset Win actions");
   Check((int)Get(shell,"gteGifts")==300,"PTVRunner overlay keeps all 300 gifts");
   Snapshot(shell,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"dist","signal-page.png"));
   var other=games.First(g=>g.category=="Roblox"&&g.id!="ptvrunner");
   ((System.Threading.Tasks.Task)Call(shell,"GteSetTarget",other)).GetAwaiter().GetResult();Call(shell,"Render");
   Check(Tree((DependencyObject)shell.Content).OfType<Button>().Any(b=>Convert.ToString(b.Content).Contains(presetLabel)),"other-map sidebar still has Presets & commands");
   Check(Tree((DependencyObject)shell.Content).OfType<ComboBox>().Count()==2,"other maps keep their preset selector");
   Call(shell,"GteHandle",new Dictionary<string,object>{{"kind","gift"},{"giftName","Rose"},{"amount",1},{"viewer","tester"}});
   for(int n=0;n<100&&DownCount==0;n++)Thread.Sleep(5);
   Check(DownCount>0,"other-map gift presets still execute (input mocked)");
   Check(GteData.Int(GteData.Sub(state,"win"),"count",0)==1,"other-map Win actions still execute");
   GteKeys.Clear();Thread.Sleep(200);
   GteKeys.Enqueue("x",100,30);int start=DownCount;
   for(int n=0;n<100&&DownCount==start;n++)Thread.Sleep(5);
   GteKeys.Clear();Thread.Sleep(100);int stopped=DownCount;Thread.Sleep(200);
   Check(DownCount==stopped,"clearing the queue also cancels the active job");
   Console.WriteLine("All PTVRunner routing/UI checks passed. Windows input was mocked.");app.Shutdown();return 0;
  }catch(Exception e){Console.WriteLine(e);return 1;}
 }
}
