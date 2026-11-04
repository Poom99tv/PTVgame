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
  readonly StackPanel list; GteJarCanvas jar;
  public GteOverlayView(string kind) {
   Kind=kind; ClipToBounds=true;
   if(kind=="jar") { jar=new GteJarCanvas(); Children.Add(jar);
   } else { list=new StackPanel { Margin=new Thickness(12) }; Children.Add(list); }
   Loaded+=(s,e)=>GteOverlayHub.Add(this);
   Unloaded+=(s,e)=>GteOverlayHub.Remove(this);
  }
  static TextBlock T(string text,double size,Brush color,FontWeight weight) { return new TextBlock { Text=text,FontSize=size,Foreground=color,FontWeight=weight,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,Effect=new System.Windows.Media.Effects.DropShadowEffect { BlurRadius=4,ShadowDepth=1,Opacity=.7 } }; }
  static List<Dictionary<string,object>> Rows(Dictionary<string,object> s,string key) { var r=new List<Dictionary<string,object>>(); object v; if(s!=null&&s.TryGetValue(key,out v)&&v is IEnumerable) foreach(var x in (IEnumerable)v) { var d=x as Dictionary<string,object>; if(d!=null) r.Add(d); } return r; }
  public void Update(Dictionary<string,object> s,List<Dictionary<string,object>> drops,bool cleared) {
   var cfg=GteData.Sub(s,"config"); var win=GteData.Sub(cfg,"win"); var totals=GteData.Sub(s,"totals");
   if(Kind=="jar") {
    jar.Scale=Math.Max(.4,GteData.Int(GteData.Sub(cfg,"jar"),"scale",100)/100.0);
    if(cleared) jar.Clear();
    if(drops!=null) foreach(var d in drops) { int n=Math.Min(50,GteData.Int(d,"count",1)); for(int i=0;i<n;i++) jar.Queue(GteData.Str(d,"file","")); }
    return;
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
 }

 // Gift jar physics — C# port of gametoearn/engine/jar-physics.mjs (keep both in sync).
 // Jar shape/sizes follow the PTV Game To Earn PyQt jar; logical space 500 x 580.
 public class GteJarSim {
  public const double W=500,H=580,Cx=250,Jt=290,Jb=Jt+280,Jx1=Cx-110,Jx2=Cx+110,Nx1=Cx-86,Nx2=Cx+86,NeckH=32,Size=32,R=15;
  public const double InX1=Jx1+16,InX2=Jx2-16,FloorIn=Jb-16,Ground=Jb,G=0.42; public const int Max=170,Iter=10;
  public class P { public double x,y,px,py,rot,vrot,finalRot,r=R,alpha=1; public string img; public bool overflow,sleep,dying; public int still,rim; }
  public readonly List<P> Items=new List<P>(); readonly Random rnd=new Random();
  public void Add(string img) {
   double x=Nx1+22+rnd.NextDouble()*(Nx2-Nx1-44);
   double y0=-Size-rnd.NextDouble()*40;
   Items.Add(new P { x=x,y=y0,px=x,py=y0-3,rot=(rnd.NextDouble()-.5)*70,vrot=(rnd.NextDouble()-.5)*3,finalRot=(rnd.NextDouble()-.5)*60,img=img });
   foreach(var q in Items) if(q.sleep&&q.y<Jt+90) { q.sleep=false; q.still=0; }
   // too many: the oldest spilled gift fades out (never pops away)
   if(Items.Count(q=>!q.dying)>Max) { var old=Items.FirstOrDefault(q=>q.overflow&&!q.dying)??Items.FirstOrDefault(q=>!q.dying); if(old!=null) old.dying=true; }
  }
  public void Clear() { Items.Clear(); }
  public static void Walls(double y,out double a,out double b) {
   double top=Jt+NeckH; if(y<top) { a=Nx1+4; b=Nx2-4; return; }
   double t=Math.Min(1,(y-top)/36),e=1-(1-t)*(1-t); a=Nx1+4+(InX1-Nx1-4)*e; b=Nx2-4+(InX2-Nx2+4)*e;
  }
  static void Inside(P q) {
   if(q.y+q.r>FloorIn) q.y=FloorIn-q.r;
   if(q.y+q.r*.5>Jt) { double a,b; Walls(q.y,out a,out b); if(q.x-q.r<a) q.x=a+q.r; else if(q.x+q.r>b) q.x=b-q.r; }
   foreach(var cxr in new[]{InX1+22,InX2-22}) {
    double cy=FloorIn-22,dx=q.x-cxr,dy=q.y-cy;
    if(dy>0&&(cxr<Cx?dx<0:dx>0)) { double d=Math.Sqrt(dx*dx+dy*dy),lim=22-q.r; if(d>lim&&lim>0) { q.x=cxr+dx/d*lim; q.y=cy+dy/d*lim; } }
   }
  }
  static readonly double[][] Boxes={ new[]{Nx1-4,Jt,Nx2+4,Jt+NeckH},new[]{Jx1,Jt+NeckH,Jx2,Jb} };
  static bool Outside(P q) {
   bool onTop=false;
   foreach(var bx in Boxes) {
    double x1=bx[0],y1=bx[1],x2=bx[2],y2=bx[3];
    double cx=Math.Max(x1,Math.Min(q.x,x2)),cy=Math.Max(y1,Math.Min(q.y,y2)),dx=q.x-cx,dy=q.y-cy,d=Math.Sqrt(dx*dx+dy*dy);
    if(d>=q.r) continue;
    if(d==0) { double l=q.x-x1,r=x2-q.x,t=q.y-y1,m=q.py<y1?t:Math.Min(l,Math.Min(r,t)); if(m==t) { q.y=y1-q.r; onTop=true; } else if(m==l) q.x=x1-q.r; else q.x=x2+q.r; continue; }
    q.x+=dx/d*(q.r-d); q.y+=dy/d*(q.r-d); if(dy/d< -0.6) onTop=true;
   }
   if(q.y+q.r>Ground) q.y=Ground-q.r;
   return onTop;
  }
  public void Step() {
   bool removed=false;
   foreach(var q in Items) if(q.dying) { q.alpha-=1/40.0; if(q.alpha<=0) removed=true; }
   if(removed) { Items.RemoveAll(q=>q.alpha<=0); foreach(var q in Items) { q.sleep=false; q.still=0; } }
   var ps=Items; int n=ps.Count;
   foreach(var q in ps) {
    if(q.sleep) { q.px=q.x; q.py=q.y; continue; }
    double vx=(q.x-q.px)*(q.overflow?0.985:0.94),vy=(q.y-q.py)*0.995+G,sp=Math.Sqrt(vx*vx+vy*vy);
    if(sp>14) { vx*=14/sp; vy*=14/sp; }
    q.px=q.x; q.py=q.y; q.x+=vx; q.y+=vy; q.rot+=q.vrot; q.vrot*=0.92;
   }
   const double cell=32; var grid=new Dictionary<long,List<int>>();
   for(int i=0;i<n;i++) { long k=(long)Math.Floor(ps[i].x/cell)*1000+(long)Math.Floor(ps[i].y/cell); List<int> l; if(!grid.TryGetValue(k,out l)) grid[k]=l=new List<int>(); l.Add(i); }
   var pairs=new List<int>();
   for(int i=0;i<n;i++) {
    long gx=(long)Math.Floor(ps[i].x/cell),gy=(long)Math.Floor(ps[i].y/cell);
    for(int ox=-1;ox<=1;ox++) for(int oy=-1;oy<=1;oy++) { List<int> l; if(grid.TryGetValue((gx+ox)*1000+gy+oy,out l)) foreach(var j in l) if(j>i) { pairs.Add(i); pairs.Add(j); } }
   }
   for(int it=0;it<Iter;it++) {
    for(int k=0;k<pairs.Count;k+=2) {
     var a=ps[pairs[k]]; var b=ps[pairs[k+1]];
     double dx=a.x-b.x,dy=a.y-b.y,d=Math.Sqrt(dx*dx+dy*dy); if(d==0) d=0.01; double min=a.r+b.r;
     if(d>=min||(a.sleep&&b.sleep&&min-d<3)) continue;
     double ov=min-d,nx=dx/d,ny=dy/d;
     double wa=a.sleep?0.05:(a.y<b.y?0.65:0.35),wb=b.sleep?0.05:1-(a.sleep?0.05:wa); if(a.sleep) wb=0.95; double s=wa+wb; wa/=s; wb/=s;
     a.x+=nx*ov*wa; a.y+=ny*ov*wa; b.x-=nx*ov*wb; b.y-=ny*ov*wb;
     if(ov>3) { if(a.sleep) { a.sleep=false; a.still=0; } if(b.sleep) { b.sleep=false; b.still=0; } }
    }
    foreach(var q in ps) {
     if(q.overflow) { if(Outside(q)&&it==Iter-1) q.x+=q.x<Cx?-0.6:0.6; } else Inside(q);
     if(q.x-q.r<0) q.x=q.r; else if(q.x+q.r>W) q.x=W-q.r;
    }
   }
   foreach(var q in ps) {
    if(q.sleep) continue;
    if(q.y-q.py< -2.5) q.py=q.y+2.5;
    double hx=q.x-q.px; if(Math.Abs(hx)>5) q.px=q.x-Math.Sign(hx)*5;
    if(!q.overflow) {
     // above the rim but outside the opening = lying on the glass edge -> spills over (no snapping inside)
     if(q.y<Jt+q.r&&(q.x<Nx1+4||q.x>Nx2-4)) { q.overflow=true; q.rim=0; }
     else { bool slow=Math.Abs(q.y-q.py)<0.8; q.rim=q.y<Jt-4&&slow?q.rim+1:0; if(q.rim>6) { q.overflow=true; q.px=q.x-(q.x<Cx?-1.2:1.2); } }
    } else if(q.x>Nx1+4+q.r&&q.x<Nx2-4-q.r&&q.y>Jt-q.r+6&&q.y<Jt+NeckH) { q.overflow=false; q.rim=0; }
    double moving=Math.Sqrt((q.x-q.px)*(q.x-q.px)+(q.y-q.py)*(q.y-q.py));
    bool restless=q.overflow?q.y<Jt+6&&q.x>Nx1-24&&q.x<Nx2+24:q.y<Jt;
    if(!restless&&moving<0.12) { if(++q.still>8) q.sleep=true; } else q.still=0;
    if(q.sleep||moving<0.3) { double df=q.finalRot-q.rot; q.rot+=Math.Abs(df)>0.5?df*0.15:df; q.vrot=0; }
   }
  }
 }

 // Draws the glass jar (same look as the original PyQt jar) and runs the simulation at 60 fps.
 public class GteJarCanvas : FrameworkElement {
  readonly GteJarSim sim=new GteJarSim(); readonly Queue<string> queue=new Queue<string>(); public double Scale=1; int tick;
  static readonly Geometry Body,Clip,FloorLine,Lid,Shine1,Shine2; static readonly Brush BodyFill,LidFill; static readonly Pen BodyPen,FloorPen,LidPen,StripePen,ShinePen1,ShinePen2;
  static Color A(byte a,byte r,byte g,byte b) { return Color.FromArgb(a,r,g,b); }
  static Geometry Geo(string d) { var g=Geometry.Parse(d); g.Freeze(); return g; }
  static Pen Pn(Color c,double w,bool round) { var p=new Pen(new SolidColorBrush(c),w); if(round) { p.StartLineCap=PenLineCap.Round; p.EndLineCap=PenLineCap.Round; } p.Freeze(); return p; }
  static GteJarCanvas() {
   var ci=System.Globalization.CultureInfo.InvariantCulture; Func<string,object[],string> F=(f,a)=>string.Format(ci,f,a);
   double cx=GteJarSim.Cx,jt=GteJarSim.Jt,jb=GteJarSim.Jb,jx1=GteJarSim.Jx1,jx2=GteJarSim.Jx2,nx1=GteJarSim.Nx1,nx2=GteJarSim.Nx2,nh=GteJarSim.NeckH;
   Body=Geo(F("M{0},{1} Q{2},{3} {2},{4} L{2},{5} Q{2},{6} {7},{6} L{8},{6} Q{9},{6} {9},{5} L{9},{4} Q{9},{3} {10},{1} Z",new object[]{nx1,jt+nh,jx1+3,jt+nh+10,jt+nh+40,jb-35,jb,jx1+35,jx2-35,jx2-3,nx2}));
   Clip=Geo(F("M-500,-2000 L1000,-2000 L1000,{0} L{1},{2} L{1},{3} Q{1},{4} {5},{4} L{6},{4} Q{7},{4} {7},{3} L{7},{2} L-500,{0} Z",new object[]{jt+32,jx2-14,jt+45,jb-24,jb-15,jx2-35,jx1+35,jx1+14}));
   FloorLine=Geo(F("M{0},{1} Q{2},{3} {4},{1}",new object[]{jx1+14,jb-18,cx,jb-10,jx2-14}));
   Lid=new RectangleGeometry(new Rect(nx1-3,jt+4,nx2-nx1+6,nh-4),5,5); Lid.Freeze();
   Shine1=Geo(F("M{0},{1} Q{2},{3} {4},{5}",new object[]{jx1+18,jt+nh+25,jx1+22,jt+nh+100,jx1+19,jb-45}));
   Shine2=Geo(F("M{0},{1} Q{2},{3} {4},{5}",new object[]{jx2-18,jt+nh+30,jx2-20,jt+nh+80,jx2-19,jt+nh+130}));
   var bf=new LinearGradientBrush { StartPoint=new Point(0,0),EndPoint=new Point(1,0) };
   foreach(var st in new[]{ new[]{0,120},new[]{10,50},new[]{30,15},new[]{50,5},new[]{70,15},new[]{90,50},new[]{100,120} }) bf.GradientStops.Add(new GradientStop(A((byte)st[1],230,245,255),st[0]/100.0));
   bf.Freeze(); BodyFill=bf;
   var lf=new LinearGradientBrush { StartPoint=new Point(0,0),EndPoint=new Point(0,1) };
   lf.GradientStops.Add(new GradientStop(A(140,195,200,205),0)); lf.GradientStops.Add(new GradientStop(A(160,245,248,250),.4)); lf.GradientStops.Add(new GradientStop(A(140,165,170,175),1)); lf.Freeze(); LidFill=lf;
   BodyPen=Pn(A(195,215,235,255),2.5,false); FloorPen=Pn(A(130,235,245,255),1.5,false); LidPen=Pn(A(150,145,150,155),2,false); StripePen=Pn(A(120,125,130,135),1.5,false);
   ShinePen1=Pn(A(140,255,255,255),6,true); ShinePen2=Pn(A(90,255,255,255),3,true);
  }
  public GteJarCanvas() {
   EventHandler tickHandler=(s,e)=>{ tick++; if(queue.Count>0&&tick%5==0) sim.Add(queue.Dequeue()); if(sim.Items.Count>0||queue.Count>0) { sim.Step(); InvalidateVisual(); } };
   Loaded+=(s,e)=>CompositionTarget.Rendering+=tickHandler;
   Unloaded+=(s,e)=>CompositionTarget.Rendering-=tickHandler;
  }
  public void Queue(string file) { if(queue.Count<600) queue.Enqueue(file); }
  public void Clear() { sim.Clear(); queue.Clear(); InvalidateVisual(); }
  void Gift(DrawingContext dc,GteJarSim.P q) {
   var img=GteData.Image(q.img); bool fade=q.alpha<1; if(fade) dc.PushOpacity(Math.Max(0,q.alpha)); dc.PushTransform(new RotateTransform(q.rot,q.x,q.y));
   if(img!=null) dc.DrawImage(img,new Rect(q.x-GteJarSim.Size/2,q.y-GteJarSim.Size/2,GteJarSim.Size,GteJarSim.Size)); else dc.DrawEllipse(Brushes.HotPink,null,new Point(q.x,q.y),12,12);
   dc.Pop(); if(fade) dc.Pop();
  }
  protected override void OnRender(DrawingContext dc) {
   double w=ActualWidth,h=ActualHeight; if(w<10||h<10) return;
   dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,w,h));
   double sc=Math.Min(w/GteJarSim.W,h/GteJarSim.H)*Scale;
   var tg=new TransformGroup(); tg.Children.Add(new ScaleTransform(sc,sc)); tg.Children.Add(new TranslateTransform((w-GteJarSim.W*sc)/2,h-GteJarSim.H*sc));
   dc.PushTransform(tg);
   dc.PushClip(Clip); foreach(var q in sim.Items) if(!q.overflow) Gift(dc,q); dc.Pop();
   dc.DrawGeometry(BodyFill,null,Body); dc.DrawGeometry(null,BodyPen,Body); dc.DrawGeometry(null,FloorPen,FloorLine);
   dc.DrawGeometry(LidFill,LidPen,Lid);
   for(int i=1;i<3;i++) { double y=GteJarSim.Jt+4+i*Math.Floor((GteJarSim.NeckH-4)/3); dc.DrawLine(StripePen,new Point(GteJarSim.Nx1-1,y),new Point(GteJarSim.Nx2+1,y)); }
   dc.DrawGeometry(null,ShinePen1,Shine1); dc.DrawGeometry(null,ShinePen2,Shine2);
   foreach(var q in sim.Items) if(q.overflow) Gift(dc,q);
   dc.Pop();
   var ft=new FormattedText("🎁 "+sim.Items.Count(q=>!q.dying),System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(new FontFamily("Segoe UI"),FontStyles.Normal,FontWeights.Black,FontStretches.Normal),16,Brushes.White);
   dc.DrawText(ft,new Point((w-ft.Width)/2,4));
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
   if(kind=="win") { Width=460; Height=170; } else if(kind=="stats") { Width=560; Height=110; } else { Width=kind=="jar"?420:380; Height=kind=="jar"?500:430; }
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
