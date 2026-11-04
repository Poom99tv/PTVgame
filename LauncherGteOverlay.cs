// Game To Earn overlays drawn natively in WPF: live preview on the OBS page and pop-out windows
// for TikTok LIVE Studio (Window Capture) or OBS (Window Capture). The same data feeds the
// browser-source overlays served by the engine (/overlay/...).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PTVgame {
 public static class GteOverlayHub {
  static readonly List<GteOverlayView> views=new List<GteOverlayView>();
  static DispatcherTimer timer; static bool busy; static long after; static int jarClear=-1;
  public static Dictionary<string,object> Last;
  public static void Add(GteOverlayView v) {
   if(!views.Contains(v)) views.Add(v);
   if(timer==null) { timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(500) }; timer.Tick+=async(s,e)=>await Tick(); }
   if(!timer.IsEnabled) timer.Start();
   if(Last!=null) v.Update(Last,null,false);
  }
  public static void Remove(GteOverlayView v) { views.Remove(v); if(views.Count==0&&timer!=null) timer.Stop(); }
  static async Task Tick() {
   if(busy||!GteEngine.Running) return; busy=true;
   try {
    var s=await GteEngine.Get("overlay/state?after="+after); Last=s;
    var drops=new List<Dictionary<string,object>>(); object d;
    if(s.TryGetValue("drops",out d)&&d is IEnumerable) foreach(var x in (IEnumerable)d) { var dd=x as Dictionary<string,object>; if(dd==null) continue; drops.Add(dd); after=Math.Max(after,GteData.Int(dd,"id",0)); }
    int clear=GteData.Int(s,"jarClear",0); bool cleared=jarClear>=0&&clear!=jarClear; jarClear=clear;
    foreach(var v in views.ToList()) v.Update(s,drops,cleared);
   } catch {}
   finally { busy=false; }
  }
 }

 public class GteOverlayView : Grid {
  public readonly string Kind; string signature="";
  readonly Canvas canvas; readonly StackPanel list; DispatcherTimer physics;
  class Ball { public double x,y,vx,vy,r; public Image img; }
  readonly List<Ball> balls=new List<Ball>(); readonly Queue<string> queue=new Queue<string>(); int tick; double scale=1;
  TextBlock jarCount; Path jarShape;
  public GteOverlayView(string kind) {
   Kind=kind; ClipToBounds=true;
   if(kind=="jar") {
    canvas=new Canvas(); Children.Add(canvas);
    jarShape=new Path { Stroke=new SolidColorBrush(Color.FromArgb(200,220,240,255)),StrokeThickness=5,Fill=new SolidColorBrush(Color.FromArgb(28,200,230,255)) }; canvas.Children.Add(jarShape);
    jarCount=new TextBlock { Foreground=Brushes.White,FontWeight=FontWeights.Black,FontSize=17,Effect=new System.Windows.Media.Effects.DropShadowEffect { BlurRadius=6,ShadowDepth=0 } }; canvas.Children.Add(jarCount);
    SizeChanged+=(s,e)=>DrawJar();
   } else { list=new StackPanel { Margin=new Thickness(12) }; Children.Add(list); }
   Loaded+=(s,e)=>{ GteOverlayHub.Add(this); if(Kind=="jar"&&physics==null) { physics=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(16) }; physics.Tick+=(a,b)=>Step(); physics.Start(); } };
   Unloaded+=(s,e)=>{ GteOverlayHub.Remove(this); if(physics!=null) { physics.Stop(); physics=null; } };
  }
  static TextBlock T(string text,double size,Brush color,FontWeight weight) { return new TextBlock { Text=text,FontSize=size,Foreground=color,FontWeight=weight,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,Effect=new System.Windows.Media.Effects.DropShadowEffect { BlurRadius=4,ShadowDepth=1,Opacity=.7 } }; }
  static List<Dictionary<string,object>> Rows(Dictionary<string,object> s,string key) { var r=new List<Dictionary<string,object>>(); object v; if(s!=null&&s.TryGetValue(key,out v)&&v is IEnumerable) foreach(var x in (IEnumerable)v) { var d=x as Dictionary<string,object>; if(d!=null) r.Add(d); } return r; }
  public void Update(Dictionary<string,object> s,List<Dictionary<string,object>> drops,bool cleared) {
   var cfg=GteData.Sub(s,"config"); var win=GteData.Sub(cfg,"win"); var totals=GteData.Sub(s,"totals");
   if(Kind=="jar") {
    scale=Math.Max(.4,GteData.Int(GteData.Sub(cfg,"jar"),"scale",100)/100.0);
    if(cleared) { foreach(var b in balls) canvas.Children.Remove(b.img); balls.Clear(); queue.Clear(); }
    if(drops!=null) foreach(var d in drops) { int n=Math.Min(50,GteData.Int(d,"count",1)); for(int i=0;i<n;i++) queue.Enqueue(GteData.Str(d,"file","")); }
    DrawJar(); return;
   }
   if(Kind=="likers"||Kind=="gifters") {
    var rows=Rows(s,Kind); var sig=string.Join("|",rows.Select(r=>GteData.Str(r,"viewer","")+":"+GteData.Str(r,"score","")+":"+GteData.Str(r,"avatar","").Length));
    if(sig==signature&&list.Children.Count>0) return; signature=sig; list.Children.Clear();
    list.Children.Add(T(Kind=="likers"?"❤️ TOP LIKERS":"🎁 TOP GIFTERS",22,Brushes.White,FontWeights.Black));
    if(rows.Count==0) { var e=T(AppLanguage.T("รอข้อมูลจาก LIVE…"),14,new SolidColorBrush(Color.FromArgb(200,255,255,255)),FontWeights.SemiBold); e.Margin=new Thickness(0,10,0,0); list.Children.Add(e); }
    var rankBg=new[]{"#FFC94A","#C9D6E3","#E39B5B","#33FFFFFF","#33FFFFFF"}; var rankFg=new[]{"#2A1C00","#102030","#2A1400","#FFFFFF","#FFFFFF"};
    for(int i=0;i<rows.Count&&i<5;i++) {
     var r=rows[i]; var g=new Grid { Margin=new Thickness(0,8,0,0) };
     foreach(var w in new[]{34.0,52,0,0}) g.ColumnDefinitions.Add(new ColumnDefinition { Width=w==0?GridLength.Auto:new GridLength(w) });
     g.ColumnDefinitions[2].Width=new GridLength(1,GridUnitType.Star);
     var bg=new Border { Background=new SolidColorBrush(Color.FromArgb(150,0,0,0)),CornerRadius=new CornerRadius(14) }; Grid.SetColumnSpan(bg,4); g.Children.Add(bg);
     var rank=new Border { Width=26,Height=26,CornerRadius=new CornerRadius(13),Background=Ui.B(rankBg[i]),Margin=new Thickness(8,8,0,8),Child=new TextBlock { Text=(i+1).ToString(),FontWeight=FontWeights.Black,Foreground=Ui.B(rankFg[i]),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center } }; g.Children.Add(rank);
     var av=Ui.Avatar(GteData.Str(r,"avatar",""),GteData.Str(r,"name",GteData.Str(r,"viewer","?")),40); av.Margin=new Thickness(6,0,0,0); Grid.SetColumn(av,1); g.Children.Add(av);
     var nm=T(GteData.Str(r,"name","")!=""?GteData.Str(r,"name",""):GteData.Str(r,"viewer",""),16,Brushes.White,FontWeights.Bold); nm.Margin=new Thickness(8,0,8,0); Grid.SetColumn(nm,2); g.Children.Add(nm);
     var sc=T((Kind=="likers"?"❤ ":"💎 ")+GteData.Int(r,"score",0).ToString("#,0"),16,Brushes.White,FontWeights.Black); sc.Margin=new Thickness(0,0,12,0); Grid.SetColumn(sc,3); g.Children.Add(sc);
     list.Children.Add(g);
    }
    return;
   }
   if(Kind=="win") {
    var sig=GteData.Str(win,"count","")+"/"+GteData.Str(win,"max","")+GteData.Str(win,"color","")+GteData.Str(win,"size",""); if(sig==signature) return; signature=sig; list.Children.Clear();
    int max=GteData.Int(win,"max",10); Brush c; try { c=Ui.B(GteData.Str(win,"color","#ffffff")); } catch { c=Brushes.White; }
    var t=T(GteData.Str(win,"label","WIN")+" "+GteData.Int(win,"count",0)+(max>0?" / "+max:""),Math.Max(20,GteData.Int(win,"size",64)),c,FontWeights.Black);
    t.HorizontalAlignment=HorizontalAlignment.Center; t.Effect=new System.Windows.Media.Effects.DropShadowEffect { BlurRadius=12,ShadowDepth=2,Opacity=.9 };
    list.VerticalAlignment=VerticalAlignment.Center; list.Children.Add(new Viewbox { Child=t,StretchDirection=StretchDirection.DownOnly }); return;
   }
   // stats
   var sig2=GteData.Str(totals,"likes","")+GteData.Str(totals,"gifts","")+GteData.Str(totals,"diamonds","")+GteData.Str(win,"count",""); if(sig2==signature) return; signature=sig2; list.Children.Clear();
   var row=new WrapPanel(); list.Children.Add(row);
   foreach(var p in new[]{"❤️ "+GteData.Int(totals,"likes",0).ToString("#,0"),"🎁 "+GteData.Int(totals,"gifts",0).ToString("#,0"),"💎 "+GteData.Int(totals,"diamonds",0).ToString("#,0"),"🏆 "+GteData.Int(win,"count",0)})
    row.Children.Add(new Border { Background=new SolidColorBrush(Color.FromArgb(170,0,0,0)),CornerRadius=new CornerRadius(14),Padding=new Thickness(14,8,14,8),Margin=new Thickness(0,0,10,10),Child=T(p,20,Brushes.White,FontWeights.Black) });
  }

  Rect Jar() {
   double W=Math.Max(80,ActualWidth),H=Math.Max(80,ActualHeight);
   double w=Math.Min(W*0.82,260*scale),h=Math.Min(H*0.82,330*scale);
   return new Rect((W-w)/2,H-h-8,w,h);
  }
  double R { get { return Math.Max(10,Math.Min(22*scale,Jar().Width/7)); } }
  void DrawJar() {
   if(jarShape==null) return; var j=Jar();
   jarShape.Data=Geometry.Parse(string.Format(System.Globalization.CultureInfo.InvariantCulture,"M{0},{1} L{2},{3} Q{2},{4} {5},{4} L{6},{4} Q{7},{4} {7},{3} L{8},{1}",j.X-6,j.Y-10,j.X,j.Bottom-22,j.Bottom,j.X+22,j.Right-22,j.Right,j.Right+6));
   jarCount.Text="🎁 "+balls.Count; Canvas.SetLeft(jarCount,j.X+j.Width/2-30); Canvas.SetTop(jarCount,Math.Max(0,j.Y-36));
  }
  static readonly Random rnd=new Random();
  void Step() {
   var j=Jar(); double r=R; tick++;
   if(queue.Count>0&&tick%4==0) {
    var file=queue.Dequeue(); var src=GteData.Image(file);
    var img=new Image { Width=r*2,Height=r*2,Source=src }; if(src==null) img.Source=null;
    var b=new Ball { x=j.X+r+rnd.NextDouble()*Math.Max(1,j.Width-2*r),y=j.Y-40,vx=(rnd.NextDouble()-.5)*2,vy=0,r=r,img=img };
    balls.Add(b); canvas.Children.Add(img);
    if(balls.Count>220) { canvas.Children.Remove(balls[0].img); balls.RemoveAt(0); }
   }
   foreach(var b in balls) {
    b.r=r; b.vy+=0.45; b.x+=b.vx; b.y+=b.vy; b.vx*=0.985;
    if(b.y+b.r>j.Bottom) { b.y=j.Bottom-b.r; b.vy*=-0.2; b.vx*=0.85; }
    if(b.y>j.Y) { if(b.x-b.r<j.X) { b.x=j.X+b.r; b.vx*=-0.3; } if(b.x+b.r>j.Right) { b.x=j.Right-b.r; b.vx*=-0.3; } }
   }
   for(int k=0;k<3;k++) for(int a=0;a<balls.Count;a++) for(int c=a+1;c<balls.Count;c++) {
    var p=balls[a]; var q=balls[c]; double dx=q.x-p.x,dy=q.y-p.y,d=Math.Sqrt(dx*dx+dy*dy),m=p.r+q.r;
    if(d>0&&d<m) { double o=(m-d)/2,nx=dx/d,ny=dy/d; p.x-=nx*o; p.y-=ny*o; q.x+=nx*o; q.y+=ny*o; double rv=(q.vx-p.vx)*nx+(q.vy-p.vy)*ny; if(rv<0) { double imp=rv*0.6; p.vx+=imp*nx; p.vy+=imp*ny; q.vx-=imp*nx; q.vy-=imp*ny; } }
   }
   foreach(var b in balls) { b.img.Width=b.r*2; b.img.Height=b.r*2; Canvas.SetLeft(b.img,b.x-b.r); Canvas.SetTop(b.img,b.y-b.r); }
   if(tick%10==0) DrawJar();
  }
 }

 // Borderless window for TikTok LIVE Studio / OBS "Window Capture".
 public class GteOverlayWindow : Window {
  static readonly Dictionary<string,GteOverlayWindow> open=new Dictionary<string,GteOverlayWindow>();
  public static void Show(string kind,string title,string background) {
   GteOverlayWindow w; if(open.TryGetValue(kind,out w)) { w.Activate(); return; }
   w=new GteOverlayWindow(kind,title,background); open[kind]=w; w.Closed+=(s,e)=>open.Remove(kind); w.Show();
  }
  public static void CloseAll() { foreach(var w in open.Values.ToList()) w.Close(); }
  GteOverlayWindow(string kind,string title,string background) {
   Title="PTV Overlay - "+title; WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.CanResizeWithGrip; ShowInTaskbar=true; FontFamily=new FontFamily("Segoe UI");
   if(kind=="win") { Width=460; Height=170; } else if(kind=="stats") { Width=560; Height=110; } else { Width=380; Height=kind=="jar"?480:430; }
   Brush bg; try { bg=Ui.B(string.IsNullOrEmpty(background)?"#00FF00":background); } catch { bg=Brushes.Lime; } Background=bg;
   Content=new GteOverlayView(kind);
   MouseLeftButtonDown+=(s,e)=>{ try { DragMove(); } catch {} };
   var menu=new ContextMenu();
   var top=new MenuItem { Header=AppLanguage.T("อยู่บนสุดเสมอ"),IsCheckable=true }; top.Click+=(s,e)=>Topmost=top.IsChecked; menu.Items.Add(top);
   foreach(var c in new[]{ new[]{"พื้นหลังเขียว (Chroma)","#00FF00"},new[]{"พื้นหลังน้ำเงิน (Chroma)","#0000FF"},new[]{"พื้นหลังดำ","#000000"},new[]{"พื้นหลังกรมท่า","#0B2238"} }) {
    var color=c[1]; var mi=new MenuItem { Header=AppLanguage.T(c[0]) }; mi.Click+=(s,e)=>Background=Ui.B(color); menu.Items.Add(mi);
   }
   menu.Items.Add(new Separator()); var close=new MenuItem { Header=AppLanguage.T("ปิดหน้าต่างนี้") }; close.Click+=(s,e)=>Close(); menu.Items.Add(close);
   ContextMenu=menu;
  }
 }
}
