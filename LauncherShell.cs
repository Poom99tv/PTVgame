using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Input;

namespace PTVgame {
 public class LauncherShell : Window {
  readonly string settingsPath=System.IO.Path.Combine(Storage.Root,"settings.json");
  Settings settings; List<Game> games; StackPanel sections; Grid root; Border main,header; TextBox search; TextBlock status; ProgressBar progress; Button cancel; Dictionary<string,Button> nav=new Dictionary<string,Button>();
  string page="หน้าแรก",filter=""; bool busy; CancellationTokenSource cancellation; Brush accent,muted; string selected=null;
  Brush ColorBrush(string hex) { return (Brush)new BrushConverter().ConvertFromString(hex); }
  public LauncherShell() {
   settings=Storage.Read(settingsPath,new Settings()); if(settings.favorites==null) settings.favorites=new List<string>();
   games=Storage.Read(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"catalog.json"),new List<Game>());
   Width=1160; Height=760; MinWidth=920; MinHeight=660; Title="PTVgame"; WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.CanResizeWithGrip; WindowStartupLocation=WindowStartupLocation.CenterScreen;
   FontFamily=new FontFamily("Segoe UI"); FontSize=14; Foreground=Brushes.White;
   Resources.Add(typeof(Button),System.Windows.Markup.XamlReader.Parse("<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='8' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.42'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter Property='Opacity' Value='0.78'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>"));
   Build(); Loaded+=async(s,e)=> { if(await UpdateLauncher())return; if(settings.autoUpdate) foreach(var g in games.Where(g=>g.category!="Roblox"&&Storage.Current(g)!=null&&!string.IsNullOrWhiteSpace(g.manifestUrl))) await Install(g,false); };
   Closing+=(s,e)=> { if(busy) { e.Cancel=true; cancellation.Cancel(); status.Text="กำลังยกเลิก กรุณารอแล้วปิดอีกครั้ง"; } };
  }
  TextBlock Text(string value,double size,Brush color=null) { return new TextBlock { Text=value,FontSize=size,Foreground=color??Foreground,VerticalAlignment=VerticalAlignment.Center }; }
  Button Btn(string value,Brush background=null) { return new Button { Content=value,Background=background??Brushes.Transparent,Foreground=Foreground,BorderThickness=new Thickness(0),Padding=new Thickness(12,8,12,8),Cursor=Cursors.Hand }; }
  void Build() {
   bool frost=settings.theme=="Frost"; accent=ColorBrush(settings.theme=="Ember"?"#FF7438":settings.theme=="Neon"?"#BF48FF":"#268EFF"); muted=ColorBrush(frost?"#57748E":"#88AAC9"); Foreground=frost?ColorBrush("#102B48"):Brushes.White;
   root=new Grid { Background=ColorBrush(frost?"#ECF5FF":"#00101F") }; root.RowDefinitions.Add(new RowDefinition { Height=new GridLength(72) }); root.RowDefinitions.Add(new RowDefinition());
   var frame=new Border { BorderBrush=ColorBrush("#27435C"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(15),ClipToBounds=true,Child=root }; Content=frame;
   header=new Border { Background=ColorBrush(frost?"#F4F8FF":"#00101E"),BorderBrush=ColorBrush("#183550"),BorderThickness=new Thickness(0,0,0,1) }; root.Children.Add(header);
   var top=new Grid(); top.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(255) }); top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(190) }); header.Child=top;
   var brand=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(22,0,0,0),VerticalAlignment=VerticalAlignment.Center }; brand.Children.Add(GameIcon(38,false)); var brandPtv=Text(" PTV",29);brandPtv.FontWeight=FontWeights.Bold;brand.Children.Add(brandPtv); var brandGame=Text("game",29,accent); brandGame.FontWeight=FontWeights.Bold; brand.Children.Add(brandGame); top.Children.Add(brand);
   var searchFrame=new Border { CornerRadius=new CornerRadius(20),BorderBrush=ColorBrush("#22547E"),BorderThickness=new Thickness(1),Background=ColorBrush(frost?"#E6EFF9":"#031A2F"),Height=37,MaxWidth=420,Margin=new Thickness(20,0,35,0),VerticalAlignment=VerticalAlignment.Center };
   var searchGrid=new Grid(); searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(38) }); searchGrid.ColumnDefinitions.Add(new ColumnDefinition()); searchGrid.Children.Add(Text("⌕",25,muted));
   search=new TextBox { Text=filter,BorderThickness=new Thickness(0),Background=Brushes.Transparent,Foreground=Foreground,VerticalContentAlignment=VerticalAlignment.Center,FontSize=13,ToolTip="ค้นหาเกม…",Padding=new Thickness(0,0,10,0) }; Grid.SetColumn(search,1); searchGrid.Children.Add(search);
   var placeholder=Text("ค้นหาเกม…",13,muted); placeholder.IsHitTestVisible=false; Grid.SetColumn(placeholder,1); placeholder.Visibility=string.IsNullOrEmpty(filter)?Visibility.Visible:Visibility.Collapsed; searchGrid.Children.Add(placeholder);
   search.TextChanged+=(s,e)=> { filter=search.Text; placeholder.Visibility=string.IsNullOrEmpty(filter)?Visibility.Visible:Visibility.Collapsed; Render(); }; searchFrame.Child=searchGrid; Grid.SetColumn(searchFrame,1); top.Children.Add(searchFrame);
   var controls=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0) };
   var notice=Btn("♧"); notice.ToolTip="ตรวจอัปเดตเกมที่ติดตั้ง"; notice.Click+=async(s,e)=> { foreach(var g in games.Where(g=>g.category!="Roblox"&&Storage.Current(g)!=null)) await Install(g,false); }; controls.Children.Add(notice);
   var minimize=Btn("—"); minimize.Click+=(s,e)=>WindowState=WindowState.Minimized; controls.Children.Add(minimize); var maximize=Btn("□"); maximize.Click+=(s,e)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized; controls.Children.Add(maximize); var close=Btn("✕"); close.Click+=(s,e)=>Close(); controls.Children.Add(close); Grid.SetColumn(controls,2); top.Children.Add(controls);
   header.MouseLeftButtonDown+=(s,e)=> { if(e.OriginalSource==top||e.OriginalSource==header) { if(e.ClickCount==2) maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); else DragMove(); } };
   var body=new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(182) }); body.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetRow(body,1); root.Children.Add(body);
   var side=new Grid { Background=ColorBrush(frost?"#E5EFFB":"#00101F") }; body.Children.Add(side);
   var menus=new StackPanel { Margin=new Thickness(10,22,10,0) }; side.Children.Add(menus); nav.Clear();
   string[] labels={"หน้าแรก","ไลบรารี","รายการโปรด","การตั้งค่า"},icons={"⌂","▣","♥","⚙"}; for(int n=0;n<labels.Length;n++) { string key=labels[n]; var b=Btn(icons[n]+"   "+key); b.HorizontalContentAlignment=HorizontalAlignment.Left; b.Padding=new Thickness(17,13,8,13); b.Margin=new Thickness(0,0,0,7); b.Click+=(s,e)=> { page=key; selected=null; Render(); }; menus.Children.Add(b); nav[key]=b; }
   var profile=new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(17,0,0,29) }; var avatar=new Border { Width=42,Height=42,CornerRadius=new CornerRadius(21),Background=ColorBrush("#214766"),BorderBrush=muted,BorderThickness=new Thickness(1),Child=Text("●",25,ColorBrush("#B6D1EE")) }; ((TextBlock)avatar.Child).HorizontalAlignment=HorizontalAlignment.Center; profile.Children.Add(avatar); var profileText=new StackPanel { Margin=new Thickness(9,0,0,0) }; profileText.Children.Add(Text("ผู้เล่น",12)); profileText.Children.Add(Text("●  พร้อมใช้งาน",11,ColorBrush("#48DDAC"))); profile.Children.Add(profileText); side.Children.Add(profile);
   main=new Border { CornerRadius=new CornerRadius(15,0,0,0),BorderBrush=ColorBrush("#153C5B"),BorderThickness=new Thickness(1,1,0,0),Background=new LinearGradientBrush(frost?System.Windows.Media.Color.FromRgb(221,241,255):System.Windows.Media.Color.FromRgb(3,34,58),frost?System.Windows.Media.Color.FromRgb(247,250,255):System.Windows.Media.Color.FromRgb(0,17,31),70) }; Grid.SetColumn(main,1); body.Children.Add(main);
   var contentGrid=new Grid { Margin=new Thickness(25,18,24,14) }; contentGrid.RowDefinitions.Add(new RowDefinition()); contentGrid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); main.Child=contentGrid;
   var mountain=Mountain(); mountain.IsHitTestVisible=false; mountain.VerticalAlignment=VerticalAlignment.Stretch; contentGrid.Children.Add(mountain);
   var scroll=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled }; sections=new StackPanel(); scroll.Content=sections; contentGrid.Children.Add(scroll);
   var bottom=new StackPanel { Margin=new Thickness(0,8,0,0) }; status=Text(busy?"กำลังดำเนินการ…":"พร้อมเล่น • เลือกเกมที่คุณชอบ",12,muted); status.TextWrapping=TextWrapping.Wrap; bottom.Children.Add(status);
   progress=new ProgressBar { Height=3,IsIndeterminate=true,Foreground=accent,Visibility=busy?Visibility.Visible:Visibility.Collapsed,Margin=new Thickness(0,7,0,0) }; bottom.Children.Add(progress); cancel=Btn("ยกเลิกดาวน์โหลด"); cancel.Visibility=busy?Visibility.Visible:Visibility.Collapsed; cancel.HorizontalAlignment=HorizontalAlignment.Left; cancel.Click+=(s,e)=>{if(cancellation!=null)cancellation.Cancel();}; bottom.Children.Add(cancel); Grid.SetRow(bottom,1); contentGrid.Children.Add(bottom);
   Render();
  }
  FrameworkElement GameIcon(double size,bool roblox) {
   var canvas=new Canvas { Width=48,Height=42 };
   if(roblox) {
    var shape=new System.Windows.Shapes.Path { Data=Geometry.Parse("M7,2 L43,10 35,42 -1,34 Z M18,16 L15,27 26,30 29,19 Z"),Fill=new LinearGradientBrush(Color.FromRgb(220,248,255),Color.FromRgb(80,168,238),70),Stroke=ColorBrush("#93D9FF"),StrokeThickness=.7 };
    canvas.Children.Add(shape);
   } else {
    canvas.Children.Add(new System.Windows.Shapes.Path { Data=Geometry.Parse("M13,8 C8,8 5,12 4,19 L1,32 C-1,41 6,44 12,36 L17,30 31,30 36,36 C42,44 49,41 47,32 L44,19 C43,12 40,8 35,8 C31,8 29,11 24,11 C19,11 17,8 13,8 Z"),Fill=new LinearGradientBrush(Color.FromRgb(80,215,255),Color.FromRgb(29,77,245),90),Stroke=ColorBrush("#6FD7FF"),StrokeThickness=1 });
    canvas.Children.Add(new System.Windows.Shapes.Path { Data=Geometry.Parse("M12,16 L16,16 16,20 20,20 20,24 16,24 16,28 12,28 12,24 8,24 8,20 12,20 Z"),Fill=ColorBrush("#092451") });
    foreach(var point in new[]{new Point(33,17),new Point(38,22),new Point(33,27),new Point(28,22)}) {var dot=new Ellipse {Width=4,Height=4,Fill=ColorBrush("#112C65")};Canvas.SetLeft(dot,point.X);Canvas.SetTop(dot,point.Y);canvas.Children.Add(dot);}
    canvas.Children.Add(new System.Windows.Shapes.Path {Data=Geometry.Parse("M18,15 L22,14"),Stroke=ColorBrush("#A5EDFF"),StrokeThickness=1.5});
   }
   return new Viewbox { Width=size,Height=size,Child=canvas,VerticalAlignment=VerticalAlignment.Center };
  }
  FrameworkElement Mountain() {
   var view=new Viewbox { Stretch=Stretch.Fill,Opacity=.22,HorizontalAlignment=HorizontalAlignment.Right,Width=240 };
   var canvas=new Canvas { Width=240,Height=560 }; canvas.Children.Add(new System.Windows.Shapes.Path { Data=Geometry.Parse("M0,560 L55,330 83,390 110,230 132,300 158,145 176,245 202,70 218,180 240,150 240,560 Z"),Fill=ColorBrush("#0B4D73") });
   canvas.Children.Add(new System.Windows.Shapes.Path { Data=Geometry.Parse("M50,560 L100,410 127,465 166,280 195,370 225,220 240,270 240,560 Z"),Fill=ColorBrush("#001323") }); view.Child=canvas; return view;
  }
  void Render() {
   if(sections==null)return; sections.Children.Clear(); foreach(var pair in nav) { pair.Value.Background=pair.Key==page?ColorBrush("#093A70"):Brushes.Transparent; pair.Value.Foreground=pair.Key==page?Brushes.White:muted; }
   if(page=="การตั้งค่า") { SettingsPage(); return; }
   if(selected!=null) { Detail(games.First(g=>g.id==selected)); return; }
   var list=games.Where(g=>(g.name??"").IndexOf(filter,StringComparison.OrdinalIgnoreCase)>=0);
   if(page=="ไลบรารี")list=list.Where(g=>g.category=="Roblox"||Storage.Current(g)!=null);
   if(page=="รายการโปรด")list=list.Where(g=>settings.favorites.Contains(g.id));
   var regular=list.Where(g=>g.category!="Roblox").ToList(); var roblox=list.Where(g=>g.category=="Roblox").ToList();
   if(regular.Count>0) Group("🎮","เกมทั่วไป",regular,3); if(roblox.Count>0) Group("▧","เกม Roblox",roblox,4);
   if(regular.Count+roblox.Count==0) sections.Children.Add(Text("ยังไม่มีเกมในหน้านี้",22,muted));
  }
  void Group(string icon,string title,List<Game> items,int columns) {
   var heading=new DockPanel { Margin=new Thickness(0,0,0,14) }; var all=Btn("ดูทั้งหมด  ›"); all.Foreground=muted; all.FontSize=12; DockPanel.SetDock(all,Dock.Right); heading.Children.Add(all); all.Click+=(s,e)=> { page="หน้าแรก"; filter=""; search.Text=""; Render(); }; var headingContent=new StackPanel { Orientation=Orientation.Horizontal };headingContent.Children.Add(GameIcon(34,title=="เกม Roblox"));var label=Text("  "+title,27); label.FontWeight=FontWeights.Bold; headingContent.Children.Add(label);heading.Children.Add(headingContent); sections.Children.Add(heading);
   var grid=new System.Windows.Controls.Primitives.UniformGrid { Columns=columns,Margin=new Thickness(-6,0,-6,0) }; foreach(var g in items)grid.Children.Add(Card(g,columns==3?190:155)); sections.Children.Add(grid);
   sections.Children.Add(new Border { Height=1,Background=ColorBrush("#18405D"),Margin=new Thickness(0,19,0,19) });
  }
  Image Picture(Game g) {
   var image=new Image { Stretch=Stretch.UniformToFill }; try { string file=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,g.cover??("assets/"+g.id+".png")); if(File.Exists(file)) { var bitmap=new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption=BitmapCacheOption.OnLoad; bitmap.UriSource=new Uri(file); bitmap.EndInit(); bitmap.Freeze(); image.Source=bitmap; } } catch {} return image;
  }
  UIElement Card(Game g,double height) {
   var panel=new StackPanel { Margin=new Thickness(6,0,6,0) }; var art=new Grid { Height=height };
   var cover=new Border { CornerRadius=new CornerRadius(9),ClipToBounds=true,BorderBrush=ColorBrush("#528CBB"),BorderThickness=new Thickness(1),Background=ColorBrush("#0A2D4B"),Child=Picture(g) }; cover.SizeChanged+=(s,e)=> { cover.Clip=new RectangleGeometry(new Rect(0,0,cover.ActualWidth,cover.ActualHeight),9,9); }; art.Children.Add(cover);
   var overlay=new Border { VerticalAlignment=VerticalAlignment.Bottom,Height=52,Background=new LinearGradientBrush(Colors.Transparent,Color.FromArgb(175,0,10,22),90),CornerRadius=new CornerRadius(0,0,8,8),IsHitTestVisible=false }; art.Children.Add(overlay);
   var play=Btn("▶",ColorBrush("#B000203B")); play.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Background='{TemplateBinding Background}' BorderBrush='White' BorderThickness='2' CornerRadius='18'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border></ControlTemplate>"); play.Width=36;play.Height=36;play.Padding=new Thickness(0);play.HorizontalAlignment=HorizontalAlignment.Right;play.VerticalAlignment=VerticalAlignment.Bottom;play.Margin=new Thickness(0,0,10,10);play.BorderBrush=Brushes.White;play.BorderThickness=new Thickness(2);play.ToolTip=g.category=="Roblox"?"เปิดแมพ Roblox":"เปิดรายละเอียดและเล่นเกม"; art.Children.Add(play); play.Click+=(s,e)=> { if(g.category=="Roblox")OpenRoblox(g);else { selected=g.id;Render(); } };
   cover.MouseLeftButtonUp+=(s,e)=>{selected=g.id;Render();};cover.Cursor=Cursors.Hand;
   var fav=Btn(settings.favorites.Contains(g.id)?"♥":"♡",ColorBrush("#9900172B"));fav.Width=29;fav.Height=29;fav.Padding=new Thickness(0);fav.HorizontalAlignment=HorizontalAlignment.Left;fav.VerticalAlignment=VerticalAlignment.Top;fav.Margin=new Thickness(8);fav.Click+=(s,e)=>ToggleFavorite(g);art.Children.Add(fav);
   panel.Children.Add(art);var name=Text(g.name,16);name.FontWeight=FontWeights.SemiBold;name.HorizontalAlignment=HorizontalAlignment.Center;name.Margin=new Thickness(0,7,0,2);panel.Children.Add(name);
   if(g.category!="Roblox") { var installed=Storage.Current(g); var action=Btn(installed==null?"↓  ติดตั้งเกม":"▶  เล่นเกม",installed==null?ColorBrush("#104875"):accent);action.IsEnabled=!busy;action.HorizontalContentAlignment=HorizontalAlignment.Center;action.FontSize=12;action.Margin=new Thickness(0,6,0,0);action.Click+=async(s,e)=>{if(Storage.Current(g)==null)await Install(g,false);else await Play(g);};panel.Children.Add(action); }
   return panel;
  }
  void ToggleFavorite(Game g) { if(!settings.favorites.Remove(g.id))settings.favorites.Add(g.id);Save();Render(); }
  void Detail(Game g) {
   var back=Btn("‹  กลับไปหน้าเกม");back.Foreground=muted;back.HorizontalAlignment=HorizontalAlignment.Left;back.Click+=(s,e)=>{selected=null;Render();};sections.Children.Add(back);
   var image=new Border { Height=280,CornerRadius=new CornerRadius(12),ClipToBounds=true,Margin=new Thickness(0,15,0,18),Child=Picture(g) };sections.Children.Add(image);var title=Text(g.name,32);title.FontWeight=FontWeights.Bold;sections.Children.Add(title);
   var desc=Text(g.description,15,muted);desc.Margin=new Thickness(0,12,0,15);desc.TextWrapping=TextWrapping.Wrap;sections.Children.Add(desc);
   var installed=Storage.Current(g);sections.Children.Add(Text(g.category=="Roblox"?"ROBLOX EXPERIENCE":installed==null?"ยังไม่ได้ติดตั้ง":"ติดตั้งแล้ว • v"+installed.version,12,muted));
   var row=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,15,0,0) };sections.Children.Add(row);var play=Btn(g.category=="Roblox"?"▶  เปิด Roblox":installed==null?"↓  ติดตั้งเกม":"▶  เล่นเกม",accent);play.IsEnabled=!busy;row.Children.Add(play);play.Click+=async(s,e)=>{if(g.category=="Roblox")OpenRoblox(g);else if(Storage.Current(g)==null)await Install(g,false);else await Play(g);};
   if(g.category!="Roblox") { var update=Btn("ตรวจสอบอัปเดต",ColorBrush("#193B58"));update.Margin=new Thickness(10,0,0,0);update.IsEnabled=!busy;row.Children.Add(update);update.Click+=async(s,e)=>await Install(g,true); }
   var favorite=Btn(settings.favorites.Contains(g.id)?"♥  นำออกจากรายการโปรด":"♡  เพิ่มรายการโปรด");row.Children.Add(favorite);favorite.Click+=(s,e)=>ToggleFavorite(g);
  }
  void SettingsPage() {
   sections.Children.Add(Text("⚙  การตั้งค่า",28));sections.Children.Add(new TextBlock { Text="ธีมโปรแกรม",Margin=new Thickness(0,25,0,10),Foreground=muted });
   var themes=new ComboBox { ItemsSource=new[]{"Midnight","Ember","Frost","Neon"},SelectedItem=settings.theme,Width=250,HorizontalAlignment=HorizontalAlignment.Left };sections.Children.Add(themes);themes.SelectionChanged+=(s,e)=>{settings.theme=(string)themes.SelectedItem;Save();Build();};
   var auto=new CheckBox { Content="อัปเดตเกมที่ติดตั้งเมื่อเปิด Launcher",IsChecked=settings.autoUpdate,Foreground=Foreground,Margin=new Thickness(0,25,0,20) };sections.Children.Add(auto);auto.Click+=(s,e)=>{settings.autoUpdate=auto.IsChecked==true;Save();};
   var launcherUpdate=Btn("ตรวจอัปเดต Launcher",accent);sections.Children.Add(launcherUpdate);launcherUpdate.Click+=async(s,e)=>await UpdateLauncher();
   var folder=Btn("เปิดโฟลเดอร์เกม",ColorBrush("#193B58"));folder.HorizontalAlignment=HorizontalAlignment.Left;sections.Children.Add(folder);folder.Click+=(s,e)=>{Directory.CreateDirectory(Storage.Root);Process.Start("explorer.exe",Storage.Root);};
   sections.Children.Add(new TextBlock { Text="PTVgame • Launcher 1.4.0\nภาพปก Roblox จากหน้าเกมจริง\nดาวน์โหลดเกมผ่าน GitHub Releases",Foreground=muted,Margin=new Thickness(0,30,0,0),TextWrapping=TextWrapping.Wrap });
  }
  async Task<bool> UpdateLauncher() { if(busy)return false;busy=true;cancellation=new CancellationTokenSource(TimeSpan.FromMinutes(5));try { bool restart=await LauncherUpdate.Check(new Progress<string>(t=>status.Text=t),cancellation.Token);if(restart){busy=false;Application.Current.Shutdown();return true;} }catch(Exception ex){status.Text="ตรวจอัปเดต Launcher ไม่สำเร็จ: "+ex.Message;}finally{busy=false;cancellation.Dispose();}return false; }
  void Save() { try { Storage.Save(settingsPath,settings); }catch(Exception ex){status.Text="บันทึกการตั้งค่าไม่สำเร็จ: "+ex.Message;} }
  void OpenRoblox(Game g) { long id;if(!long.TryParse(g.placeId,out id)||id<=0)return;try{Process.Start("roblox://placeId="+id);status.Text="กำลังเปิด "+g.name+" ใน Roblox";}catch(Exception ex){status.Text="เปิด Roblox ไม่สำเร็จ: "+ex.Message;} }
  async Task Play(Game g) {
   if(busy)return;busy=true;cancellation=new CancellationTokenSource();progress.Visibility=Visibility.Visible;cancel.Visibility=Visibility.Visible;Render();
   try {
    status.Text="กำลังตรวจเวอร์ชันก่อนเล่น "+g.name+"…";
    var manifest=await Updater.Check(g,cancellation.Token);
    if(Updater.Newer(manifest,Storage.Current(g))) {
     status.Text="จำเป็นต้องอัปเดต "+g.name+" ก่อนเล่น…";
     await Updater.Install(g,manifest,new Progress<string>(t=>status.Text=t),cancellation.Token);
    }
    cancellation.Token.ThrowIfCancellationRequested();
    var current=Storage.Current(g);
    if(current==null||new Version(current.version)!=new Version(manifest.version)||!string.Equals(current.executable,manifest.executable,StringComparison.OrdinalIgnoreCase))throw new Exception("ต้องติดตั้งเวอร์ชันที่เซิร์ฟเวอร์กำหนดก่อนเล่น");
    if(!string.IsNullOrEmpty(current.sha256)&&!string.Equals(current.sha256,manifest.sha256,StringComparison.OrdinalIgnoreCase))throw new Exception("ไฟล์เวอร์ชันที่ติดตั้งไม่ตรงกับเซิร์ฟเวอร์ กรุณาอัปเดต");
    Updater.Play(g);status.Text="เปิด "+g.name+" v"+current.version+" แล้ว";
   }catch(OperationCanceledException){status.Text="ยกเลิกแล้ว • ต้องอัปเดตให้สำเร็จก่อนเล่น";}
   catch(Exception ex){status.Text="ยังเปิดเกมไม่ได้: "+ex.Message+" • ต้องเชื่อมต่อและตรวจเวอร์ชันให้สำเร็จ";}
   finally{busy=false;cancellation.Dispose();cancellation=null;progress.Visibility=Visibility.Collapsed;cancel.Visibility=Visibility.Collapsed;Render();}
  }
  async Task Install(Game g,bool ask) {
   if(busy)return;busy=true;cancellation=new CancellationTokenSource();progress.Visibility=Visibility.Visible;cancel.Visibility=Visibility.Visible;Render();
   try { status.Text="กำลังตรวจเวอร์ชัน "+g.name+"…";var manifest=await Updater.Check(g,cancellation.Token);if(!Updater.Newer(manifest,Storage.Current(g))){status.Text=g.name+" เป็นเวอร์ชันล่าสุดแล้ว";return;}
    if(ask&&MessageBox.Show("ติดตั้ง "+g.name+" v"+manifest.version+" ?","PTVgame",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
    await Updater.Install(g,manifest,new Progress<string>(t=>status.Text=t),cancellation.Token);
   }catch(OperationCanceledException){status.Text="ยกเลิกแล้ว • เวอร์ชันเดิมยังใช้งานได้";}catch(Exception ex){status.Text="ติดตั้งไม่สำเร็จ: "+ex.Message;}finally{busy=false;cancellation.Dispose();cancellation=null;progress.Visibility=Visibility.Collapsed;cancel.Visibility=Visibility.Collapsed;Render();}
  }
 }
}



