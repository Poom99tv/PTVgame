// PTVgame account UI: login window, profile, VIP store, agent page, admin console, payment dialogs.
// C# 5 only (built with the .NET Framework csc.exe).
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PTVgame {
 public static class Ui {
  public static Brush B(string hex) { var b=(Brush)new BrushConverter().ConvertFromString(hex); b.Freeze(); return b; }
  public static readonly Brush Gold=B("#FFC94A"),Green=B("#48DDAC"),Red=B("#FF6B6B"),Muted=B("#88AAC9");
  public static TextBlock Raw(string text,double size,Brush color=null) { return new TextBlock { Text=text??"",FontSize=size,Foreground=color??Brushes.White,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center }; }
  public static TextBlock T(string text,double size,Brush color=null) { return Raw(AppLanguage.T(text),size,color); }
  public static TextBlock Bold(TextBlock t) { t.FontWeight=FontWeights.Bold; return t; }
  public static Button Btn(string text,Brush background,Brush foreground=null) {
   return new Button { Content=AppLanguage.T(text),Background=background,Foreground=foreground??Brushes.White,BorderThickness=new Thickness(0),Padding=new Thickness(16,10,16,10),Margin=new Thickness(0,6,8,0),Cursor=Cursors.Hand,HorizontalContentAlignment=HorizontalAlignment.Center,FontSize=13 };
  }
  public static TextBox Input(string text="",double width=double.NaN) {
   return new TextBox { Text=text??"",Width=width,Padding=new Thickness(10,8,10,8),Background=B("#0E2A44"),Foreground=Brushes.White,CaretBrush=Brushes.White,BorderBrush=B("#2B5578"),FontSize=14,Margin=new Thickness(0,4,0,10) };
  }
  public static PasswordBox Password() { return new PasswordBox { Padding=new Thickness(10,8,10,8),Background=B("#0E2A44"),Foreground=Brushes.White,CaretBrush=Brushes.White,BorderBrush=B("#2B5578"),FontSize=14,Margin=new Thickness(0,4,0,10) }; }
  public static Border Panel(UIElement child,bool light=false) {
   return new Border { Background=light?B("#FFFFFF"):B("#CC0A2640"),BorderBrush=light?B("#C9DCEF"):B("#24506F"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(16),Padding=new Thickness(22),Margin=new Thickness(0,0,16,16),Child=child };
  }
  public static Border Pill(string text,Brush background,Brush foreground) {
   return new Border { Background=background,CornerRadius=new CornerRadius(12),Padding=new Thickness(10,3,10,3),Margin=new Thickness(0,0,8,0),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,Child=new TextBlock { Text=AppLanguage.T(text),Foreground=foreground,FontSize=12,FontWeight=FontWeights.SemiBold } };
  }
  public static BitmapImage Bitmap(byte[] bytes,int decodeWidth=0) {
   var b=new BitmapImage(); using(var ms=new MemoryStream(bytes)) { b.BeginInit(); b.CacheOption=BitmapCacheOption.OnLoad; if(decodeWidth>0) b.DecodePixelWidth=decodeWidth; b.StreamSource=ms; b.EndInit(); } b.Freeze(); return b;
  }
  public static string Initial(string name) { if(string.IsNullOrWhiteSpace(name)) return "?"; return System.Globalization.StringInfo.GetNextTextElement(name.Trim()).ToUpperInvariant(); }
  public static Border Avatar(string url,string name,double size) {
   var b=new Border { Width=size,Height=size,CornerRadius=new CornerRadius(size/2),Background=B("#214766"),BorderBrush=B("#5A8FBA"),BorderThickness=new Thickness(1.5),ClipToBounds=true };
   b.Child=new TextBlock { Text=Initial(name),FontSize=size*.42,FontWeight=FontWeights.Bold,Foreground=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center };
   LoadAvatar(b,url); return b;
  }
  static async void LoadAvatar(Border b,string url) {
   try { var bytes=await AccountService.Image(url); if(bytes==null) return; b.Background=new ImageBrush(Bitmap(bytes,256)) { Stretch=Stretch.UniformToFill }; b.Child=null; } catch {}
  }
  /// <summary>Square-crops and resizes a picture for the profile avatar (PNG, strips metadata).</summary>
  public static byte[] SquarePng(string file,int size) {
   if(new FileInfo(file).Length>15*1024*1024) throw new Exception(AppLanguage.T("ไฟล์รูปใหญ่เกิน 15 MB"));
   var src=new BitmapImage(); src.BeginInit(); src.CacheOption=BitmapCacheOption.OnLoad; src.UriSource=new Uri(file); src.EndInit();
   int side=Math.Min(src.PixelWidth,src.PixelHeight);
   var crop=new CroppedBitmap(src,new Int32Rect((src.PixelWidth-side)/2,(src.PixelHeight-side)/2,side,side));
   var scaled=new TransformedBitmap(crop,new ScaleTransform((double)size/side,(double)size/side));
   var enc=new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(scaled)); using(var ms=new MemoryStream()) { enc.Save(ms); return ms.ToArray(); }
  }
  /// <summary>Re-encodes a payment slip as JPEG (max 1600px wide) so uploads stay small and metadata-free.</summary>
  public static byte[] SlipJpeg(string file) {
   if(new FileInfo(file).Length>20*1024*1024) throw new Exception(AppLanguage.T("ไฟล์สลิปใหญ่เกิน 20 MB"));
   var src=new BitmapImage(); src.BeginInit(); src.CacheOption=BitmapCacheOption.OnLoad; src.UriSource=new Uri(file); src.EndInit();
   BitmapSource frame=src; if(src.PixelWidth>1600) frame=new TransformedBitmap(src,new ScaleTransform(1600.0/src.PixelWidth,1600.0/src.PixelWidth));
   var enc=new JpegBitmapEncoder { QualityLevel=85 }; enc.Frames.Add(BitmapFrame.Create(frame)); using(var ms=new MemoryStream()) { enc.Save(ms); return ms.ToArray(); }
  }
  public static Image Qr(string payload,double size) {
   var m=QrCode.Encode(payload); int n=m.GetLength(0),q=4; var dark=new GeometryGroup();
   for(int y=0;y<n;y++) for(int x=0;x<n;x++) if(m[y,x]) dark.Children.Add(new RectangleGeometry(new Rect(x+q,y+q,1,1)));
   var g=new DrawingGroup(); g.Children.Add(new GeometryDrawing(Brushes.White,null,new RectangleGeometry(new Rect(0,0,n+2*q,n+2*q)))); g.Children.Add(new GeometryDrawing(Brushes.Black,null,dark)); g.Freeze();
   var img=new Image { Source=new DrawingImage(g),Width=size,Height=size,Stretch=Stretch.Uniform }; RenderOptions.SetEdgeMode(img,EdgeMode.Aliased); return img;
  }
  public static string Date(DateTime? utc) { return utc==null?"-":utc.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm"); }
  public static bool Thai { get { return AppLanguage.Language=="th"; } }
  public static decimal Rate { get { var a=AccountService.Current; return a!=null&&a.usdThbRate>0?a.usdThbRate:34.5m; } }
  /// <summary>THB amount shown as ฿ in Thai, converted to $ in English.</summary>
  public static string Local(decimal thb) { return Thai?Money(thb,"THB"):Money(Math.Round(thb/Rate,2),"USD"); }
  public static string Money(decimal amount,string currency) { return currency=="THB"?"฿"+amount.ToString("#,0.00"):"$"+amount.ToString("#,0.00"); }
  public static string Status(string s) { switch(s) { case "pending": return "รอชำระ"; case "review": return "รอตรวจสลิป"; case "paid": return "ชำระสำเร็จ"; case "rejected": return "ถูกปฏิเสธ"; case "cancelled": return "ยกเลิกแล้ว"; default: return s??""; } }
  public static Brush StatusColor(string s) { return s=="paid"?Green:s=="rejected"||s=="cancelled"?Red:Gold; }
  public static string Method(string m) { switch(m) { case "promptpay": return "QR พร้อมเพย์"; case "paypal": return "PayPal"; case "card": return "บัตรเครดิต/เดบิต"; default: return m??""; } }
  public static string Role(string r) { return r=="admin"?"แอดมิน":r=="agent"?"ตัวแทน":"ผู้ใช้"; }
  public static string PlanName(string code) { return code=="yearly"?"VIP รายปี":code=="monthly"?"VIP รายเดือน":code; }
  public static void Copy(string text) { for(int i=0;i<5;i++) { try { Clipboard.SetText(text); return; } catch { Thread.Sleep(60); } } }
  public static string PickImage(string title) {
   var d=new Microsoft.Win32.OpenFileDialog { Title=AppLanguage.T(title),Filter="Images|*.png;*.jpg;*.jpeg" };
   return d.ShowDialog()==true?d.FileName:null;
  }
  public static Window Frame(string title,double width,double height) {
   var w=new Window { Title="PTVgame",Width=width,Height=height,WindowStyle=WindowStyle.None,AllowsTransparency=true,Background=Brushes.Transparent,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterScreen,FontFamily=new FontFamily("Segoe UI"),Foreground=Brushes.White,ShowInTaskbar=true };
   if(Application.Current!=null&&Application.Current.MainWindow!=null&&Application.Current.MainWindow.IsVisible) { w.Owner=Application.Current.MainWindow; w.WindowStartupLocation=WindowStartupLocation.CenterOwner; w.ShowInTaskbar=false; }
   w.Resources.Add(typeof(Button),System.Windows.Markup.XamlReader.Parse("<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='10' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter Property='Opacity' Value='0.82'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>"));
   var grid=new Grid(); var shell=new Border { Background=new LinearGradientBrush(Color.FromRgb(3,34,58),Color.FromRgb(0,16,31),70),BorderBrush=B("#27435C"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(18),Child=grid };
   grid.RowDefinitions.Add(new RowDefinition { Height=new GridLength(48) }); grid.RowDefinitions.Add(new RowDefinition());
   var bar=new DockPanel { Background=Brushes.Transparent,Margin=new Thickness(20,0,10,0) };
   var close=new Button { Content="✕",Background=Brushes.Transparent,Foreground=Muted,BorderThickness=new Thickness(0),Padding=new Thickness(10,6,10,6),Cursor=Cursors.Hand }; close.Click+=(s,e)=>w.Close(); DockPanel.SetDock(close,Dock.Right); bar.Children.Add(close);
   var head=new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center }; head.Children.Add(Bold(Raw("◈ PTV",17))); head.Children.Add(Bold(Raw("game",17,B("#268EFF")))); head.Children.Add(Raw("   "+AppLanguage.T(title),13,Muted)); bar.Children.Add(head);
   bar.MouseLeftButtonDown+=(s,e)=>{ try { w.DragMove(); } catch {} }; grid.Children.Add(bar);
   w.Content=shell; w.Tag=grid; return w;
  }
  public static void SetBody(Window w,UIElement body) { var grid=(Grid)w.Tag; Grid.SetRow(body,1); grid.Children.Add(body); }
 }

 /// <summary>Shown before the launcher: restore session, or sign in / sign up with email, Google or Facebook.</summary>
 public class LoginWindow {
  readonly Window w; StackPanel form; TextBlock status; bool signUp; CancellationTokenSource oauth; bool working;
  public LoginWindow() { w=Ui.Frame("บัญชีผู้เล่น",440,650); w.Closing+=(s,e)=>{ if(oauth!=null) oauth.Cancel(); }; var scroll=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(34,4,34,24) }; form=new StackPanel(); scroll.Content=form; Ui.SetBody(w,scroll); w.Loaded+=async(s,e)=>await TryRestore(); }
  public bool Show() { return w.ShowDialog()==true; }
  async Task TryRestore() {
   if(!File.Exists(Path.Combine(Storage.Root,"session.dat"))) { Build(); return; }
   form.Children.Clear(); form.Children.Add(Ui.T("กำลังเข้าสู่ระบบ…",16,Ui.Muted)); form.Children.Add(new ProgressBar { IsIndeterminate=true,Height=3,Margin=new Thickness(0,16,0,0) });
   try { if(await AccountService.Restore()) { w.DialogResult=true; return; } Build(); }
   catch(Exception ex) { Build(); status.Text=ex.Message; status.Foreground=Ui.Red; }
  }
  void Build() {
   form.Children.Clear();
   var title=Ui.Bold(Ui.T(signUp?"สร้างบัญชี PTVgame":"ยินดีต้อนรับกลับมา",26)); title.Margin=new Thickness(0,12,0,4); form.Children.Add(title);
   var sub=Ui.T(signUp?"สมัครครั้งเดียว ใช้ได้ทุกเกมใน PTVgame":"เข้าสู่ระบบเพื่อเล่นเกมและจัดการ VIP",13,Ui.Muted); sub.Margin=new Thickness(0,0,0,18); form.Children.Add(sub);
   var google=Ui.Btn("ดำเนินการต่อด้วย Google",Brushes.White,Ui.B("#1F1F1F")); google.Margin=new Thickness(0,4,0,0); google.Click+=async(s,e)=>await OAuth("google"); form.Children.Add(google);
   var facebook=Ui.Btn("ดำเนินการต่อด้วย Facebook",Ui.B("#1877F2")); facebook.Margin=new Thickness(0,10,0,0); facebook.Click+=async(s,e)=>await OAuth("facebook"); form.Children.Add(facebook);
   google.Visibility=Visibility.Collapsed; facebook.Visibility=Visibility.Collapsed;
   var or=new Grid { Margin=new Thickness(0,20,0,14) }; or.Children.Add(new Border { Height=1,Background=Ui.B("#24506F"),VerticalAlignment=VerticalAlignment.Center });
   or.Children.Add(new Border { Background=Ui.B("#021B30"),HorizontalAlignment=HorizontalAlignment.Center,Padding=new Thickness(10,0,10,0),Child=Ui.T("หรือใช้อีเมล",12,Ui.Muted) }); form.Children.Add(or); or.Visibility=Visibility.Collapsed; ShowProviders(google,facebook,or);
   TextBox name=null; if(signUp) { form.Children.Add(Ui.T("ชื่อที่แสดง",12,Ui.Muted)); name=Ui.Input(); name.MaxLength=40; form.Children.Add(name); }
   form.Children.Add(Ui.T("อีเมล",12,Ui.Muted)); var email=Ui.Input(); form.Children.Add(email);
   form.Children.Add(Ui.T("รหัสผ่าน",12,Ui.Muted)); var pass=Ui.Password(); form.Children.Add(pass);
   PasswordBox confirm=null; if(signUp) { form.Children.Add(Ui.T("ยืนยันรหัสผ่าน",12,Ui.Muted)); confirm=Ui.Password(); form.Children.Add(confirm); }
   var main=Ui.Btn(signUp?"สมัครสมาชิก":"เข้าสู่ระบบ",Ui.B("#268EFF")); main.Margin=new Thickness(0,6,0,0); main.IsDefault=true; form.Children.Add(main);
   main.Click+=async(s,e)=>{
    if(working) return;
    if(!email.Text.Contains("@")) { Fail("กรุณากรอกอีเมลให้ถูกต้อง"); return; }
    if(pass.Password.Length<6) { Fail("รหัสผ่านต้องยาวอย่างน้อย 6 ตัวอักษร"); return; }
    if(signUp&&pass.Password!=confirm.Password) { Fail("รหัสผ่านไม่ตรงกัน"); return; }
    if(signUp&&string.IsNullOrWhiteSpace(name.Text)) { Fail("กรุณากรอกชื่อที่แสดง"); return; }
    await Run(async()=>{
     if(!signUp) { await AccountService.SignIn(email.Text,pass.Password); w.DialogResult=true; return; }
     if(await AccountService.SignUp(email.Text,pass.Password,name.Text)) { w.DialogResult=true; return; }
     signUp=false; Build(); Ok("สมัครสำเร็จ • กรุณายืนยันอีเมลจากลิงก์ที่ส่งไป แล้วเข้าสู่ระบบ");
    });
   };
   var links=new DockPanel { Margin=new Thickness(0,12,0,0) };
   var swap=Link(signUp?"มีบัญชีแล้ว? เข้าสู่ระบบ":"ยังไม่มีบัญชี? สมัครสมาชิก"); swap.Click+=(s,e)=>{ signUp=!signUp; Build(); }; DockPanel.SetDock(swap,Dock.Left); links.Children.Add(swap);
   if(!signUp) { var forgot=Link("ลืมรหัสผ่าน?"); forgot.HorizontalAlignment=HorizontalAlignment.Right; forgot.Click+=async(s,e)=>{ if(!email.Text.Contains("@")) { Fail("กรอกอีเมลก่อน แล้วกดลืมรหัสผ่าน"); return; } await Run(async()=>{ await AccountService.ResetPassword(email.Text); Ok("ส่งลิงก์ตั้งรหัสผ่านใหม่ไปที่อีเมลแล้ว"); }); }; links.Children.Add(forgot); }
   form.Children.Add(links);
   status=Ui.Raw("",13,Ui.Muted); status.Margin=new Thickness(0,16,0,0); form.Children.Add(status);
  }
  async void ShowProviders(Button google,Button facebook,UIElement divider) {
   try { var on=await AccountService.EnabledProviders(); google.Visibility=on.Contains("google")?Visibility.Visible:Visibility.Collapsed; facebook.Visibility=on.Contains("facebook")?Visibility.Visible:Visibility.Collapsed; divider.Visibility=on.Contains("google")||on.Contains("facebook")?Visibility.Visible:Visibility.Collapsed; } catch {}
  }
  Button Link(string text) { var b=Ui.Btn(text,Brushes.Transparent,Ui.Muted); b.Padding=new Thickness(0,4,0,4); b.Margin=new Thickness(0); b.HorizontalAlignment=HorizontalAlignment.Left; return b; }
  void Fail(string m) { status.Text=AppLanguage.T(m); status.Foreground=Ui.Red; }
  void Ok(string m) { status.Text=AppLanguage.T(m); status.Foreground=Ui.Green; }
  async Task Run(Func<Task> action) {
   working=true; form.IsEnabled=false; status.Text=AppLanguage.T("กำลังดำเนินการ…"); status.Foreground=Ui.Muted;
   try { await action(); }
   catch(OperationCanceledException) { if(status!=null) Fail("ยกเลิกแล้ว"); }
   catch(Exception ex) { if(status!=null) Fail(ex.Message); }
   finally { working=false; form.IsEnabled=true; }
  }
  async Task OAuth(string provider) {
   if(working) return;
   oauth=new CancellationTokenSource();
   await Run(async()=>{
    status.Text=AppLanguage.T("เปิดเบราว์เซอร์แล้ว • เข้าสู่ระบบให้เสร็จ แล้วกลับมาที่นี่");
    await AccountService.SignInWith(provider,oauth.Token); w.DialogResult=true;
   });
   form.IsEnabled=true;
  }
 }

 /// <summary>PromptPay QR with amount + slip upload. VIP is granted when an admin approves the slip.</summary>
 public class PromptPayWindow {
  readonly Window w;
  public bool SlipSent;
  public PromptPayWindow(Dictionary<string,object> order,string accountName) {
   w=Ui.Frame("ชำระด้วย QR พร้อมเพย์",440,720); var p=new StackPanel { Margin=new Thickness(30,0,30,24) }; Ui.SetBody(w,p);
   string payload=J.S(order,"promptpay_payload"),id=J.S(order,"id"); decimal amount=J.M(order,"amount");
   p.Children.Add(Ui.Bold(Ui.T("สแกนจ่ายด้วยแอปธนาคาร",22)));
   p.Children.Add(Ui.T("คำสั่งซื้อ #"+J.S(order,"order_no")+" • "+Ui.PlanName(J.S(order,"plan_code")),13,Ui.Muted));
   var qr=new Border { Background=Brushes.White,CornerRadius=new CornerRadius(14),Padding=new Thickness(12),Margin=new Thickness(0,16,0,10),HorizontalAlignment=HorizontalAlignment.Center };
   var qrStack=new StackPanel(); qrStack.Children.Add(new TextBlock { Text="PromptPay",FontWeight=FontWeights.Bold,FontSize=16,Foreground=Ui.B("#0A3D6B"),HorizontalAlignment=HorizontalAlignment.Center });
   try { qrStack.Children.Add(Ui.Qr(payload,250)); } catch(Exception ex) { qrStack.Children.Add(new TextBlock { Text=ex.Message,Foreground=Brushes.Black }); }
   qr.Child=qrStack; p.Children.Add(qr);
   var amt=Ui.Bold(Ui.Raw(Ui.Money(amount,"THB"),34)); amt.HorizontalAlignment=HorizontalAlignment.Center; p.Children.Add(amt);
   if(!string.IsNullOrWhiteSpace(accountName)) { var n=Ui.T("ชื่อบัญชีผู้รับ: "+accountName,13,Ui.Muted); n.HorizontalAlignment=HorizontalAlignment.Center; p.Children.Add(n); }
   var steps=Ui.T("1. สแกน QR และตรวจยอดเงินกับชื่อบัญชี\n2. โอนให้ตรงยอด แล้วบันทึกสลิป\n3. กด \"แนบสลิป\" • แอดมินตรวจแล้ว VIP จะเปิดให้อัตโนมัติ",13,Ui.Muted); steps.Margin=new Thickness(0,14,0,6); p.Children.Add(steps);
   var status=Ui.Raw("",13,Ui.Muted);
   var attach=Ui.Btn("แนบสลิปการโอน",Ui.B("#268EFF")); attach.Margin=new Thickness(0,10,0,0); p.Children.Add(attach);
   attach.Click+=async(s,e)=>{
    string file=Ui.PickImage("เลือกรูปสลิป"); if(file==null) return;
    attach.IsEnabled=false; status.Text=AppLanguage.T("กำลังอัปโหลดสลิป…"); status.Foreground=Ui.Muted;
    try { await AccountService.AttachSlip(id,Ui.SlipJpeg(file),"image/jpeg"); SlipSent=true; status.Text=AppLanguage.T("ส่งสลิปแล้ว • รอแอดมินตรวจสอบ"); status.Foreground=Ui.Green; attach.Content=AppLanguage.T("ส่งสลิปใหม่"); }
    catch(Exception ex) { status.Text=ex.Message; status.Foreground=Ui.Red; }
    finally { attach.IsEnabled=true; }
   };
   var close=Ui.Btn("ปิดหน้าต่าง",Ui.B("#193B58")); close.Margin=new Thickness(0,8,0,0); close.Click+=(s,e)=>w.Close(); p.Children.Add(close);
   status.Margin=new Thickness(0,12,0,0); p.Children.Add(status);
  }
  public void Show() { w.ShowDialog(); }
 }

 public partial class LauncherShell {
  const string PageProfile="โปรไฟล์",PageStore="ร้านค้า VIP",PageAgent="ตัวแทน",PageAdmin="แอดมิน";
  int renderId; string adminTab="ภาพรวม",adminOrderFilter="review",adminSearch="";
  bool AccountsOn { get { return AccountConfig.Current.Enabled&&AccountService.Current!=null; } }
  bool Light { get { return settings.theme=="Frost"; } }
  Border Box(UIElement child) { return Ui.Panel(child,Light); }
  TextBlock Line(string text,double size,Brush color=null) { return Ui.Raw(text,size,color??Foreground); }

  void AccountNav(List<string> labels,List<string> icons) {
   if(!AccountsOn) return;
   labels.Add(PageStore); icons.Add("♛"); labels.Add(PageProfile); icons.Add("☺");
   if(AccountService.Current.IsAgent) { labels.Add(PageAgent); icons.Add("⇄"); }
   if(AccountService.Current.IsAdmin) { labels.Add(PageAdmin); icons.Add("⚑"); }
  }
  bool AccountPage() {
   if(page==PageProfile) { ProfilePage(); return true; }
   if(page==PageStore) { StorePage(); return true; }
   if(page==PageAgent&&AccountsOn&&AccountService.Current.IsAgent) { AgentPage(); return true; }
   if(page==PageAdmin&&AccountsOn&&AccountService.Current.IsAdmin) { AdminPage(); return true; }
   return false;
  }
  void Go(string target) { page=target; selected=null; Render(); }

  UIElement AccountFooter() {
   var a=AccountService.Current;
   var row=new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(14,0,8,22),Cursor=Cursors.Hand,Background=Brushes.Transparent };
   row.Children.Add(Ui.Avatar(a.avatarUrl,a.displayName,42));
   var text=new StackPanel { Margin=new Thickness(9,0,0,0),VerticalAlignment=VerticalAlignment.Center,MaxWidth=110 };
   var name=Line(a.displayName,13); name.TextWrapping=TextWrapping.NoWrap; name.TextTrimming=TextTrimming.CharacterEllipsis; text.Children.Add(name);
   text.Children.Add(Line("ID "+a.PtvIdText,11,muted));
   bool vip=a.IsAdmin||(a.vipUntil!=null&&a.vipUntil.Value>DateTime.UtcNow);
   text.Children.Add(Ui.T(a.IsAdmin?"●  แอดมิน":vip?"♛  VIP":AccountService.Offline?"●  ออฟไลน์":"●  ผู้เล่นทั่วไป",11,vip?Ui.Gold:Ui.Green));
   row.Children.Add(text); row.MouseLeftButtonUp+=(s,e)=>Go(PageProfile);
   return row;
  }

  /// <summary>Called before installing or launching: VIP games need an active VIP.</summary>
  bool Gate(Game g) {
   if(AccountService.CanPlay(g)) return true;
   status.Text=AppLanguage.T(AccountService.Current==null?"กรุณาเข้าสู่ระบบก่อนเล่น":"เกมนี้สำหรับสมาชิก VIP • สมัคร VIP ได้ที่หน้าร้านค้า");
   Go(PageStore); return false;
  }
  bool Locked(Game g) { return AccountConfig.Current.Enabled&&!AccountService.CanPlay(g); }

  async void RefreshAccount() {
   if(!AccountConfig.Current.Enabled||AccountService.Session==null) return;
   var before=AccountService.Current; string key=before==null?"":before.role+"|"+before.vipUntil+"|"+before.displayName+"|"+before.avatarUrl;
   try { var a=await AccountService.Load(); if(a.role+"|"+a.vipUntil+"|"+a.displayName+"|"+a.avatarUrl!=key) Build(); } catch {}
  }

  // ---------------------------------------------------------------- profile
  void ProfilePage() {
   var a=AccountService.Current; if(a==null) return;
   var head=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,20) };
   head.Children.Add(Ui.Avatar(a.avatarUrl,a.displayName,96));
   var who=new StackPanel { Margin=new Thickness(20,0,0,0),VerticalAlignment=VerticalAlignment.Center }; head.Children.Add(who);
   who.Children.Add(Ui.Bold(Line(a.displayName,28)));
   var idRow=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,6,0,0) }; who.Children.Add(idRow);
   idRow.Children.Add(Line(AppLanguage.T("ID ของคุณ: ")+a.PtvIdText,15,muted));
   var copy=Btn("คัดลอก",ColorBrush("#193B58")); copy.FontSize=12; copy.Padding=new Thickness(10,4,10,4); copy.Margin=new Thickness(10,0,0,0); copy.Click+=(s,e)=>{ Ui.Copy(a.PtvIdText); status.Text=AppLanguage.T("คัดลอก ID แล้ว"); }; idRow.Children.Add(copy);
   var pills=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,10,0,0) }; who.Children.Add(pills);
   pills.Children.Add(Ui.Pill(Ui.Role(a.role),ColorBrush("#193B58"),Brushes.White));
   bool vip=a.vipUntil!=null&&a.vipUntil.Value>DateTime.UtcNow;
   pills.Children.Add(Ui.Pill(vip?"VIP ถึง "+Ui.Date(a.vipUntil):a.IsAdmin?"เล่นได้ทุกเกม (แอดมิน)":"ยังไม่เป็น VIP",vip||a.IsAdmin?Ui.Gold:ColorBrush("#3A2530"),vip||a.IsAdmin?Ui.B("#2A1C00"):Ui.Red));
   sections.Children.Add(head);

   var edit=new StackPanel();
   edit.Children.Add(Ui.Bold(Text("ข้อมูลโปรไฟล์",18)));
   edit.Children.Add(Text("ชื่อที่แสดง",12,muted));
   var name=Ui.Input(a.displayName,320); name.MaxLength=40; name.HorizontalAlignment=HorizontalAlignment.Left; edit.Children.Add(name);
   var save=Btn("บันทึกชื่อ",accent); save.HorizontalAlignment=HorizontalAlignment.Left; edit.Children.Add(save);
   save.Click+=async(s,e)=>await Act(save,"บันทึกแล้ว",async()=>{ if(string.IsNullOrWhiteSpace(name.Text)) throw new Exception(AppLanguage.T("กรุณากรอกชื่อ")); await AccountService.UpdateProfile(name.Text.Trim(),a.avatarUrl); await AccountService.Load(); Build(); });
   edit.Children.Add(Ui.Bold(Text("รูปโปรไฟล์",18))); ((TextBlock)edit.Children[edit.Children.Count-1]).Margin=new Thickness(0,22,0,4);
   var pics=new WrapPanel(); edit.Children.Add(pics);
   var upload=Btn("อัปโหลดรูปใหม่",accent); pics.Children.Add(upload);
   upload.Click+=async(s,e)=>{ string file=Ui.PickImage("เลือกรูปโปรไฟล์"); if(file==null) return; await Act(upload,"เปลี่ยนรูปแล้ว",async()=>{ var png=Ui.SquarePng(file,256); var url=await AccountService.UploadAvatar(png); await AccountService.UpdateProfile(a.displayName,url); await AccountService.Load(); Build(); }); };
   var providerPic=AccountService.Session!=null?AccountService.Session.providerAvatar:null;
   if(!string.IsNullOrEmpty(providerPic)&&providerPic.StartsWith("https://")&&providerPic!=a.avatarUrl) {
    var usePic=Btn(a.provider=="facebook"?"ใช้รูปจาก Facebook":"ใช้รูปจาก Google",ColorBrush("#193B58")); pics.Children.Add(usePic);
    usePic.Click+=async(s,e)=>await Act(usePic,"เปลี่ยนรูปแล้ว",async()=>{ await AccountService.UpdateProfile(a.displayName,providerPic); await AccountService.Load(); Build(); });
   }
   if(!string.IsNullOrEmpty(a.avatarUrl)) { var remove=Btn("ลบรูป",ColorBrush("#3A2530")); pics.Children.Add(remove); remove.Click+=async(s,e)=>await Act(remove,"ลบรูปแล้ว",async()=>{ await AccountService.UpdateProfile(a.displayName,null); await AccountService.Load(); Build(); }); }
   sections.Children.Add(Box(edit));

   var info=new StackPanel();
   info.Children.Add(Ui.Bold(Text("ข้อมูลบัญชี",18)));
   foreach(var pair in new[]{ new[]{"อีเมล",a.email??"-"},new[]{"เข้าสู่ระบบด้วย",a.provider=="google"?"Google":a.provider=="facebook"?"Facebook":AppLanguage.T("อีเมล")},new[]{"ระดับบัญชี",AppLanguage.T(Ui.Role(a.role))},new[]{"สถานะ VIP",vip?AppLanguage.T("ใช้งานได้ถึง ")+Ui.Date(a.vipUntil):AppLanguage.T("ไม่มี VIP")},new[]{"ตัวแทนของคุณ",string.IsNullOrEmpty(a.agentName)?"-":a.agentName+" (ID "+a.agentPtvId.ToString("00000000")+")"+(a.agentUntil!=null?AppLanguage.T(" • ถึง ")+Ui.Date(a.agentUntil):"")} }) {
    var r=new DockPanel { Margin=new Thickness(0,8,0,0) }; var k=Text(pair[0],13,muted); k.Width=150; r.Children.Add(k); r.Children.Add(Line(pair[1],13)); info.Children.Add(r);
   }
   var buttons=new WrapPanel { Margin=new Thickness(0,14,0,0) }; info.Children.Add(buttons);
   var store=Btn(vip?"ต่ออายุ VIP":"สมัคร VIP",Ui.Gold); store.Foreground=Ui.B("#2A1C00"); store.Click+=(s,e)=>Go(PageStore); buttons.Children.Add(store);
   var logout=Btn("ออกจากระบบ",ColorBrush("#3A2530")); buttons.Children.Add(logout);
   logout.Click+=async(s,e)=>{ if(MessageBox.Show(AppLanguage.T("ออกจากระบบ PTVgame?"),"PTVgame",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return; await AccountService.SignOut(); System.Diagnostics.Process.Start(System.Reflection.Assembly.GetExecutingAssembly().Location); Application.Current.Shutdown(); };
   sections.Children.Add(Box(info));
  }

  /// <summary>Runs an account action with button state + status messages.</summary>
  async Task Act(Button b,string success,Func<Task> action) {
   b.IsEnabled=false; status.Text=AppLanguage.T("กำลังดำเนินการ…");
   try { await action(); status.Text=AppLanguage.T(success); }
   catch(Exception ex) { status.Text=AppLanguage.T("ไม่สำเร็จ: ")+ex.Message; }
   finally { b.IsEnabled=true; }
  }

  // ---------------------------------------------------------------- store
  string storeDiscountCode,storeDiscountAgent; int storeDiscountPct;
  bool DiscountActive { get { var a=AccountService.Current; return storeDiscountCode!=null&&a!=null&&!a.IsAgent&&!a.discountUsed; } }

  void StorePage() {
   if(!AccountsOn) { sections.Children.Add(Text("ระบบบัญชียังไม่ได้ตั้งค่า (account.json)",18,muted)); return; }
   var a=AccountService.Current; int id=renderId;
   var title=Ui.Bold(Text("♛  PTVgame VIP",30)); sections.Children.Add(title);
   sections.Children.Add(Text("ปลดล็อกทุกเกมใน PTVgame ด้วยไอดีเดียว ใช้ได้ทุกเครื่องที่ล็อกอิน",14,muted));
   bool vip=a.vipUntil!=null&&a.vipUntil.Value>DateTime.UtcNow;
   var state=new Border { Margin=new Thickness(0,14,0,14),HorizontalAlignment=HorizontalAlignment.Left,BorderBrush=vip?Ui.Gold:ColorBrush("#2B5578"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(20),Padding=new Thickness(16,7,16,7),
    Child=Text(vip?"แพ็กเกจปัจจุบันของคุณ • VIP ถึง "+Ui.Date(a.vipUntil)+" (ซื้อเพิ่มจะต่อเวลาจากวันเดิม)":"แพ็กเกจปัจจุบันของคุณ • ผู้เล่นทั่วไป",13,vip?Ui.Gold:muted) };
   sections.Children.Add(state);
   if(a.IsAgent) sections.Children.Add(Ui.Pill("คุณเห็นราคาตัวแทน",Ui.Gold,Ui.B("#2A1C00")));

   var extras=new WrapPanel(); sections.Children.Add(extras);
   if(!a.IsAgent&&!a.discountUsed) extras.Children.Add(DiscountBox());
   extras.Children.Add(RedeemBox());
   if(!string.IsNullOrEmpty(a.agentName)) { var who=Text("ตัวแทนของคุณ: "+a.agentName+" (ID "+a.agentPtvId.ToString("00000000")+")"+(a.agentUntil!=null?" • ถึง "+Ui.Date(a.agentUntil):""),13,muted); who.Margin=new Thickness(0,0,0,8); sections.Children.Add(who); }

   var plans=new WrapPanel { Margin=new Thickness(0,10,0,0) }; sections.Children.Add(plans); plans.Children.Add(Text("กำลังโหลดราคา…",14,muted));
   FillPlans(plans,id,null,false);
   var orders=new StackPanel { Margin=new Thickness(0,10,0,0) }; sections.Children.Add(Ui.Bold(Text("ประวัติคำสั่งซื้อ",20))); sections.Children.Add(orders);
   FillOrders(orders,id,false);
  }

  UIElement DiscountBox() {
   var p=new StackPanel { Width=330 };
   p.Children.Add(Ui.Bold(Text("โค้ดส่วนลดจากตัวแทน",16)));
   p.Children.Add(Text("ลด "+AccountService.Current.discountPct+"% สำหรับการซื้อครั้งแรก ใช้ได้ 1 ครั้งต่อไอดี",12,muted));
   if(DiscountActive) {
    var ok=Text("✓ ใช้โค้ด "+storeDiscountCode+" ของ "+storeDiscountAgent+" • ลด "+storeDiscountPct+"%",13,Ui.Green); ok.Margin=new Thickness(0,10,0,0); p.Children.Add(ok);
    var clear=Btn("ไม่ใช้โค้ดนี้",ColorBrush("#3A2530")); clear.HorizontalAlignment=HorizontalAlignment.Left; clear.FontSize=12; clear.Click+=(s,e)=>{ storeDiscountCode=null; Render(); }; p.Children.Add(clear);
   } else {
    var row=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,0) }; p.Children.Add(row);
    var box=Ui.Input("",170); box.MaxLength=12; box.CharacterCasing=CharacterCasing.Upper; row.Children.Add(box);
    var apply=Btn("ใช้โค้ด",accent); apply.Margin=new Thickness(10,0,0,6); row.Children.Add(apply);
    apply.Click+=async(s,e)=>await Act(apply,"ใช้โค้ดส่วนลดแล้ว",async()=>{
     var r=await AccountService.CheckDiscount(box.Text.Trim());
     storeDiscountCode=J.S(r,"agent_code"); storeDiscountAgent=J.S(r,"agent_name"); storeDiscountPct=(int)J.L(r,"discount_pct"); Render();
    });
   }
   return Box(p);
  }

  UIElement RedeemBox() {
   var p=new StackPanel { Width=330 };
   p.Children.Add(Ui.Bold(Text("แลกโค้ด VIP",16)));
   p.Children.Add(Text("ได้โค้ดจากตัวแทน กรอกที่นี่เพื่อเปิด VIP ทันที",12,muted));
   var row=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,0) }; p.Children.Add(row);
   var box=Ui.Input("",190); box.MaxLength=20; box.CharacterCasing=CharacterCasing.Upper; row.Children.Add(box);
   var go=Btn("แลกโค้ด",Ui.Gold); go.Foreground=Ui.B("#2A1C00"); go.Margin=new Thickness(10,0,0,6); row.Children.Add(go);
   go.Click+=async(s,e)=>{
    if(box.Text.Trim().Length<6) { status.Text=AppLanguage.T("กรุณากรอกโค้ด VIP"); return; }
    await Act(go,"แลกโค้ดสำเร็จ • VIP พร้อมใช้งานแล้ว",async()=>{ var r=await AccountService.RedeemCode(box.Text.Trim()); await AccountService.Load(); Build(); status.Text=AppLanguage.T("แลกโค้ดสำเร็จ • ได้ VIP ")+J.L(r,"days")+AppLanguage.T(" วัน"); });
   };
   return Box(p);
  }

  async void FillPlans(WrapPanel holder,int id,Func<long?> target,bool wallet) {
   List<Plan> list;
   try { list=await AccountService.Plans(); } catch(Exception ex) { if(id==renderId) { holder.Children.Clear(); holder.Children.Add(Text("โหลดราคาไม่สำเร็จ: "+ex.Message,13,Ui.Red)); } return; }
   if(id!=renderId) return; holder.Children.Clear();
   var monthly=list.FirstOrDefault(p=>p.code=="monthly");
   foreach(var p in list) holder.Children.Add(PlanCard(p,monthly,target,wallet));
  }

  UIElement PlanCard(Plan p,Plan monthly,Func<long?> target,bool wallet) {
   bool agent=AccountService.Current.IsAgent; bool yearly=p.code=="yearly"; bool disc=target==null&&DiscountActive;
   decimal usd=agent?p.agentPriceUsd:p.priceUsd,thb=agent?p.agentPriceThb:p.priceThb; decimal strike=agent?p.priceUsd:p.listPriceUsd;
   if(disc) { strike=p.priceUsd; usd=Math.Round(usd*(100-storeDiscountPct)/100m,2); thb=Math.Round(thb*(100-storeDiscountPct)/100m,2); }
   var c=new StackPanel { Width=300 };
   var top=new DockPanel(); top.Children.Add(Ui.Bold(Text(yearly?"รายปี":"รายเดือน",15)));
   if(yearly) { var best=Ui.Pill("คุ้มที่สุด",Ui.Gold,Ui.B("#2A1C00")); best.HorizontalAlignment=HorizontalAlignment.Right; top.Children.Add(best); }
   c.Children.Add(top);
   var head=Ui.Bold(Text(yearly?"เล่นยาวทั้งปี":"เริ่มต้น VIP",26)); head.Margin=new Thickness(0,14,0,2); c.Children.Add(head);
   c.Children.Add(Text(yearly?"ราคาดีที่สุดสำหรับสายสตรีมและผู้เล่นประจำ":"ทดลองปลดล็อกทุกเกม จ่ายเป็นรายเดือน",13,muted));
   var price=new WrapPanel { Margin=new Thickness(0,18,0,0) };
   decimal toThb=p.priceUsd>0?p.priceThb/p.priceUsd:Ui.Rate;
   string mainPrice=Ui.Thai?Ui.Money(thb,"THB"):Ui.Money(usd,"USD"),strikePrice=Ui.Thai?Ui.Money(Math.Round(strike*toThb/10)*10,"THB"):Ui.Money(strike,"USD");
   if(strike>usd) { var s=Line(strikePrice,18,muted); s.TextDecorations=TextDecorations.Strikethrough; s.Margin=new Thickness(0,0,8,4); s.VerticalAlignment=VerticalAlignment.Bottom; price.Children.Add(s); }
   var big=Ui.Bold(Line(mainPrice,Ui.Thai?36:40)); price.Children.Add(big);
   var per=Text(yearly?" / ปี":" / เดือน",15,muted); per.VerticalAlignment=VerticalAlignment.Bottom; per.Margin=new Thickness(4,0,0,8); price.Children.Add(per);
   c.Children.Add(price);
   c.Children.Add(Text((Ui.Thai?"หรือ "+Ui.Money(usd,"USD")+" ผ่าน PayPal / บัตร":"หรือ "+Ui.Money(thb,"THB")+" ผ่าน QR พร้อมเพย์")+(agent?" • ราคาตัวแทน":"")+(disc?" • ลด "+storeDiscountPct+"% ครั้งแรก":""),12,disc?Ui.Green:muted));
   if(yearly&&monthly!=null&&!disc) { decimal m=(agent?monthly.agentPriceUsd:monthly.priceUsd)*12; if(m>usd) c.Children.Add(Text("ถูกกว่าจ่ายรายเดือน 12 เดือน "+Math.Round((m-usd)/m*100)+"%",12,Ui.Green)); }
   var feats=new StackPanel { Margin=new Thickness(0,16,0,12) }; c.Children.Add(feats);
   var list=new List<string>{"เข้าเล่นได้ทุกเกมใน PTVgame","เกม Roblox + TikTokBridge ครบทุกแมพ","เกมใหม่เล่นได้ทันทีที่เปิดตัว","อัปเดตเกมอัตโนมัติ","ซื้อเพิ่มแล้ววันสะสมต่อจากของเดิม"};
   list.Insert(0,yearly?"VIP 365 วัน":"VIP 30 วัน");
   foreach(var f in list) { var r=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,5,0,0) }; r.Children.Add(Line("✓",14,Ui.Green)); var t=Text(f,13); t.Margin=new Thickness(10,0,0,0); r.Children.Add(t); feats.Children.Add(r); }
   if(wallet) {
    var w=Btn("จ่ายด้วยกระเป๋าเงิน • "+Ui.Local(p.agentPriceThb),Ui.Gold); w.Foreground=Ui.B("#2A1C00"); w.Margin=new Thickness(0,6,0,0); c.Children.Add(w);
    w.Click+=async(s,e)=>{ long? t; if(!Target(target,out t)) return; await PayWallet(p,"vip",t,1,w); };
   }
   var qr=Btn("ชำระด้วย QR พร้อมเพย์ • "+Ui.Money(thb,"THB"),accent); qr.Margin=new Thickness(0,wallet?8:6,0,0); c.Children.Add(qr);
   var pp=Btn("PayPal • "+Ui.Money(usd,"USD"),ColorBrush("#FFC439")); pp.Foreground=ColorBrush("#14274E"); pp.Margin=new Thickness(0,8,0,0); c.Children.Add(pp);
   var card=Btn("บัตรเครดิต / เดบิต • "+Ui.Money(usd,"USD"),ColorBrush("#193B58")); card.Margin=new Thickness(0,8,0,0); c.Children.Add(card);
   qr.Click+=async(s,e)=>{ long? t; if(!Target(target,out t)) return; await PayPromptPay(p.code,t,qr,"vip",1); };
   pp.Click+=async(s,e)=>{ long? t; if(!Target(target,out t)) return; await PayOnline(p.code,"paypal",t,"vip",1); };
   card.Click+=async(s,e)=>{ long? t; if(!Target(target,out t)) return; await PayOnline(p.code,"card",t,"vip",1); };
   var box=Box(c); if(yearly) box.BorderBrush=Ui.Gold; return box;
  }
  bool Target(Func<long?> target,out long? value) {
   value=null; if(target==null) return true; value=target();
   if(value==null) { status.Text=AppLanguage.T("กรุณาค้นหาและยืนยันไอดีผู้รับก่อน"); return false; } return true;
  }
  string DiscountFor(string kind,long? target) { return kind=="vip"&&target==null&&DiscountActive?storeDiscountCode:null; }

  async Task PayPromptPay(string plan,long? target,Button b,string kind,int quantity) {
   if(busy) return; b.IsEnabled=false; status.Text=AppLanguage.T("กำลังสร้าง QR…");
   Dictionary<string,object> order=null; string name="";
   try { order=await AccountService.CreatePromptPay(plan,target,kind,quantity,DiscountFor(kind,target)); var info=await AccountService.RpcObj("payment_info",null); name=J.S(info,"promptpay_name"); status.Text=AppLanguage.T("สร้างคำสั่งซื้อแล้ว • สแกนเพื่อชำระเงิน"); }
   catch(Exception ex) { status.Text=AppLanguage.T("สร้างคำสั่งซื้อไม่สำเร็จ: ")+ex.Message; }
   finally { b.IsEnabled=true; }
   if(order==null) return;
   var dlg=new PromptPayWindow(order,name); dlg.Show();
   if(dlg.SlipSent) status.Text=AppLanguage.T("ส่งสลิปแล้ว • VIP จะเปิดให้หลังแอดมินอนุมัติ");
   Render();
  }

  async Task PayOnline(string plan,string method,long? target,string kind,int quantity) {
   if(busy) return; busy=true; cancellation=new CancellationTokenSource(); progress.Visibility=Visibility.Visible; cancel.Visibility=Visibility.Visible; cancel.Content=AppLanguage.T("ยกเลิกการชำระเงิน");
   string result=null;
   try { result=await AccountService.PayWithPayPal(plan,method,target,new Progress<string>(t=>status.Text=AppLanguage.T(t)),cancellation.Token,kind,quantity,DiscountFor(kind,target)); }
   catch(OperationCanceledException) { status.Text=AppLanguage.T("หมดเวลาหรือยกเลิกการรอ • ถ้าจ่ายแล้ว ระบบจะยืนยันให้อัตโนมัติภายหลัง"); }
   catch(Exception ex) { status.Text=AppLanguage.T("ชำระเงินไม่สำเร็จ: ")+ex.Message; }
   finally { busy=false; cancellation.Dispose(); cancellation=null; progress.Visibility=Visibility.Collapsed; cancel.Visibility=Visibility.Collapsed; cancel.Content=AppLanguage.T("ยกเลิกดาวน์โหลด"); }
   if(result=="paid") { storeDiscountCode=null; status.Text=AppLanguage.T(kind=="codes"?"ชำระเงินสำเร็จ • โค้ด VIP อยู่ในหน้าตัวแทนแล้ว":target==null?"ชำระเงินสำเร็จ • VIP พร้อมใช้งานแล้ว":"ชำระเงินสำเร็จ • ส่ง VIP ให้ไอดีปลายทางแล้ว"); Build(); return; }
   if(result=="waiting") status.Text=AppLanguage.T("PayPal กำลังตรวจสอบรายการ • VIP จะเปิดอัตโนมัติเมื่อยืนยันแล้ว");
   else if(result=="cancelled") status.Text=AppLanguage.T("ยกเลิกการชำระเงินแล้ว");
   else if(result=="failed") status.Text=AppLanguage.T("PayPal ปฏิเสธรายการ กรุณาลองวิธีอื่น");
   Render();
  }

  async Task PayWallet(Plan p,string kind,long? target,int quantity,Button b) {
   decimal cost=p.agentPriceThb*quantity;
   string what=kind=="codes"?AppLanguage.T("โค้ด ")+AppLanguage.T(Ui.PlanName(p.code))+" x"+quantity:AppLanguage.T(Ui.PlanName(p.code))+(target==null?"":AppLanguage.T(" ให้ ID ")+target.Value.ToString("00000000"));
   if(MessageBox.Show(AppLanguage.T("จ่ายด้วยกระเป๋าเงิน ")+Ui.Local(cost)+AppLanguage.T(" สำหรับ ")+what+"?","PTVgame",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return;
   await Act(b,kind=="codes"?"สร้างโค้ด VIP แล้ว":"ส่ง VIP แล้ว",async()=>{ await AccountService.WalletBuy(p.code,kind,target,quantity); await AccountService.Load(); Render(); });
  }

  async void FillOrders(StackPanel holder,int id,bool giftsOnly) {
   holder.Children.Add(Text("กำลังโหลด…",13,muted));
   List<Dictionary<string,object>> list;
   try { list=await AccountService.MyOrders(); } catch(Exception ex) { if(id==renderId) { holder.Children.Clear(); holder.Children.Add(Text("โหลดไม่สำเร็จ: "+ex.Message,13,Ui.Red)); } return; }
   if(id!=renderId) return; holder.Children.Clear();
   long me=AccountService.Current.ptvId;
   if(giftsOnly) list=list.Where(o=>J.L(o,"target_ptv_id")!=me||J.S(o,"kind")=="codes").ToList();
   if(list.Count==0) { holder.Children.Add(Text("ยังไม่มีคำสั่งซื้อ",13,muted)); return; }
   foreach(var o in list.Take(30)) {
    var row=new Grid { Margin=new Thickness(0,0,0,8) }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
    var left=new StackPanel();
    string st=J.S(o,"status"),order=J.S(o,"id"); bool codes=J.S(o,"kind")=="codes";
    string title="#"+J.S(o,"order_no")+"  "+(codes?AppLanguage.T("โค้ด ")+AppLanguage.T(Ui.PlanName(J.S(o,"plan_code")))+" x"+J.L(o,"quantity"):AppLanguage.T(Ui.PlanName(J.S(o,"plan_code"))));
    var l1=new StackPanel { Orientation=Orientation.Horizontal }; l1.Children.Add(Ui.Pill(Ui.Status(st),Ui.StatusColor(st),Ui.B("#14202B"))); l1.Children.Add(Ui.Bold(Line(title,14))); left.Children.Add(l1);
    string to=!codes&&J.L(o,"target_ptv_id")!=me?AppLanguage.T(" • ส่งให้ ")+J.S(o,"target_name")+" (ID "+J.L(o,"target_ptv_id").ToString("00000000")+")":"";
    string disc=J.L(o,"discount_pct")>0?AppLanguage.T(" • ส่วนลด ")+J.L(o,"discount_pct")+"%":"";
    left.Children.Add(Line(Ui.Money(J.M(o,"amount"),J.S(o,"currency"))+" • "+AppLanguage.T(Ui.Method(J.S(o,"method")))+" • "+Ui.Date(J.T(o,"created_at"))+to+disc,12,muted));
    if(!string.IsNullOrEmpty(J.S(o,"note"))) left.Children.Add(Line(AppLanguage.T("หมายเหตุ: ")+J.S(o,"note"),12,Ui.Red));
    row.Children.Add(left);
    var actions=new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center }; Grid.SetColumn(actions,1); row.Children.Add(actions);
    if(J.S(o,"method")=="promptpay"&&(st=="pending"||st=="review")) {
     var show=Btn(st=="pending"?"แสดง QR / แนบสลิป":"ส่งสลิปใหม่",accent); show.FontSize=12; actions.Children.Add(show);
     var copy=o; show.Click+=async(s,e)=>{ string name=""; try { name=J.S(await AccountService.RpcObj("payment_info",null),"promptpay_name"); } catch {} var d=new PromptPayWindow(copy,name); d.Show(); Render(); };
    }
    if(st=="pending") { var c=Btn("ยกเลิกคำสั่งซื้อ",ColorBrush("#3A2530")); c.FontSize=12; actions.Children.Add(c); c.Click+=async(s,e)=>await Act(c,"ยกเลิกคำสั่งซื้อแล้ว",async()=>{ await AccountService.CancelOrder(order); Render(); }); }
    holder.Children.Add(Box(row));
   }
  }

  // ---------------------------------------------------------------- agent
  long? agentTarget; Dictionary<string,object> agentFound;
  void AgentPage() {
   int id=renderId;
   sections.Children.Add(Ui.Bold(Text("⇄  ระบบตัวแทน",30)));
   sections.Children.Add(Text("แจกรหัสตัวแทนให้ลูกค้า • ลูกค้าที่ผูกกับคุณจ่ายเองเมื่อไหร่ คุณได้ค่าคอมเข้ากระเป๋าเงิน",14,muted));
   var holder=new StackPanel { Margin=new Thickness(0,14,0,0) }; sections.Children.Add(holder); holder.Children.Add(Text("กำลังโหลด…",13,muted));
   FillAgent(holder,id);
  }

  async void FillAgent(StackPanel holder,int id) {
   Dictionary<string,object> d; List<Plan> plans;
   try { d=await AccountService.MyAgent(); plans=await AccountService.Plans(); }
   catch(Exception ex) { if(id==renderId) { holder.Children.Clear(); holder.Children.Add(Line(AppLanguage.T("โหลดไม่สำเร็จ: ")+ex.Message,13,Ui.Red)); } return; }
   if(id!=renderId) return; holder.Children.Clear();
   string code=J.S(d,"agent_code"); decimal wallet=J.M(d,"wallet_thb"); long pct=J.L(d,"discount_pct"); string link=AccountService.AgentLink(code);
   var customers=J.Arr(d,"customers"); var codes=J.Arr(d,"codes"); var ledger=J.Arr(d,"ledger"); var withdrawals=J.Arr(d,"withdrawals");

   // 1. code + link + wallet
   var top=new WrapPanel(); holder.Children.Add(top);
   var share=new StackPanel { Width=420 };
   share.Children.Add(Text("รหัสตัวแทนของคุณ",13,muted));
   var codeRow=new StackPanel { Orientation=Orientation.Horizontal }; codeRow.Children.Add(Ui.Bold(Line(code,34,Ui.Gold)));
   var copyCode=Btn("คัดลอก",ColorBrush("#193B58")); copyCode.FontSize=12; copyCode.Margin=new Thickness(12,0,0,0); copyCode.Click+=(s,e)=>{ Ui.Copy(code); status.Text=AppLanguage.T("คัดลอกรหัสแล้ว"); }; codeRow.Children.Add(copyCode);
   share.Children.Add(codeRow);
   share.Children.Add(Text("ลูกค้าใส่รหัสนี้ตอนซื้อครั้งแรก ได้ลด "+pct+"% และไอดีจะผูกกับคุณ "+J.L(d,"commission_months")+" เดือน",12,muted));
   share.Children.Add(Text("ลิงก์ดาวน์โหลดของคุณ",13,muted)); ((TextBlock)share.Children[share.Children.Count-1]).Margin=new Thickness(0,12,0,0);
   var linkBox=Ui.Input(link); linkBox.IsReadOnly=true; linkBox.FontSize=12; share.Children.Add(linkBox);
   var shareRow=new WrapPanel(); share.Children.Add(shareRow);
   var copyLink=Btn("คัดลอกลิงก์",ColorBrush("#193B58")); copyLink.FontSize=12; copyLink.Click+=(s,e)=>{ Ui.Copy(link); status.Text=AppLanguage.T("คัดลอกลิงก์แล้ว"); }; shareRow.Children.Add(copyLink);
   string msg=AppLanguage.T("โหลด PTVgame เล่นเกม TikTok Live ได้ทุกเกม: ")+link+"\n"+AppLanguage.T("ใส่โค้ด ")+code+AppLanguage.T(" ในหน้าร้านค้า VIP ลด ")+pct+AppLanguage.T("% ครั้งแรก");
   var copyMsg=Btn("คัดลอกข้อความชวนลูกค้า",accent); copyMsg.FontSize=12; copyMsg.Click+=(s,e)=>{ Ui.Copy(msg); status.Text=AppLanguage.T("คัดลอกข้อความแล้ว • วางในแชตหรือไลฟ์ได้เลย"); }; shareRow.Children.Add(copyMsg);
   share.Children.Add(Text("มีคนกดลิงก์แล้ว "+J.L(d,"clicks")+" ครั้ง • ลูกค้าที่ผูกแล้ว "+customers.Count+" คน",12,muted));
   top.Children.Add(Box(share));

   var w=new StackPanel { Width=300 };
   w.Children.Add(Text("กระเป๋าเงินตัวแทน",13,muted));
   w.Children.Add(Ui.Bold(Line(Ui.Local(wallet),38,Ui.Green)));
   w.Children.Add(Text("ใช้ซื้อ VIP/โค้ดได้ทันที หรือขอถอนเป็นเงินสด",12,muted));
   var hist=new StackPanel { Margin=new Thickness(0,10,0,0) }; w.Children.Add(hist);
   foreach(var l in ledger.Take(6)) { decimal lv=J.M(l,"amount_thb"); var r=new DockPanel { Margin=new Thickness(0,3,0,0) }; var a=Line((lv>=0?"+":"-")+Ui.Local(Math.Abs(lv)),12,lv>=0?Ui.Green:Ui.Red); DockPanel.SetDock(a,Dock.Right); r.Children.Add(a); var n=Line(Ui.Date(J.T(l,"created_at"))+"  "+(J.S(l,"note")??J.S(l,"kind")),12,muted); n.TextTrimming=TextTrimming.CharacterEllipsis; n.TextWrapping=TextWrapping.NoWrap; r.Children.Add(n); hist.Children.Add(r); }
   if(ledger.Count==0) hist.Children.Add(Text("ยังไม่มีรายการ",12,muted));
   top.Children.Add(Box(w));

   // 2. send VIP to a customer
   holder.Children.Add(Ui.Bold(Text("ส่ง VIP ให้ลูกค้า",20)));
   var find=new StackPanel();
   find.Children.Add(Text("ให้ลูกค้าเปิดหน้าโปรไฟล์ใน PTVgame แล้วแจ้ง ID 8 หลัก",12,muted));
   var row=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,0) }; find.Children.Add(row);
   var box=Ui.Input(agentTarget==null?"":agentTarget.Value.ToString("00000000"),220); box.MaxLength=8; row.Children.Add(box);
   var search=Btn("ค้นหาไอดี",accent); search.Margin=new Thickness(10,0,0,6); row.Children.Add(search);
   var result=new StackPanel { Margin=new Thickness(0,6,0,0) }; find.Children.Add(result);
   Action show=()=>{
    result.Children.Clear(); if(agentFound==null) return;
    var r=new StackPanel { Orientation=Orientation.Horizontal }; r.Children.Add(Ui.Avatar(J.S(agentFound,"avatar_url"),J.S(agentFound,"display_name"),46));
    var t=new StackPanel { Margin=new Thickness(12,0,0,0),VerticalAlignment=VerticalAlignment.Center }; t.Children.Add(Ui.Bold(Line(J.S(agentFound,"display_name"),16)));
    var until=J.T(agentFound,"vip_until"); t.Children.Add(Line("ID "+J.L(agentFound,"ptv_id").ToString("00000000")+" • "+(until!=null&&until.Value>DateTime.UtcNow?AppLanguage.T("VIP ถึง ")+Ui.Date(until):AppLanguage.T("ยังไม่เป็น VIP")),12,muted));
    r.Children.Add(t); result.Children.Add(r); result.Children.Add(Text("✓ ยืนยันผู้รับแล้ว • เลือกแพ็กเกจด้านล่าง",12,Ui.Green));
   };
   show();
   search.Click+=async(s,e)=>{
    long ptv; agentTarget=null; agentFound=null; result.Children.Clear();
    if(!long.TryParse(box.Text.Trim(),out ptv)||box.Text.Trim().Length!=8) { result.Children.Add(Text("ID ต้องเป็นตัวเลข 8 หลัก",12,Ui.Red)); return; }
    search.IsEnabled=false;
    try { var p=await AccountService.LookupPlayer(ptv); if(p==null) result.Children.Add(Text("ไม่พบไอดีนี้",12,Ui.Red)); else { agentFound=p; agentTarget=ptv; show(); } }
    catch(Exception ex) { result.Children.Add(Line(ex.Message,12,Ui.Red)); }
    finally { search.IsEnabled=true; }
   };
   holder.Children.Add(Box(find));
   var gift=new WrapPanel(); holder.Children.Add(gift);
   var monthly=plans.FirstOrDefault(p=>p.code=="monthly");
   foreach(var p in plans) gift.Children.Add(PlanCard(p,monthly,()=>agentTarget,true));

   // 3. buy VIP code batches
   holder.Children.Add(Ui.Bold(Text("ซื้อโค้ด VIP เป็นชุด (ราคาตัวแทน)",20)));
   var buy=new StackPanel { Width=520 };
   buy.Children.Add(Text("ขายโค้ดให้ลูกค้าเองได้ในราคาที่คุณตั้ง ลูกค้าแลกโค้ดที่หน้าร้านค้า VIP แล้วไอดีจะผูกกับคุณ",12,muted));
   Plan chosen=plans.FirstOrDefault(); var radios=new StackPanel { Margin=new Thickness(0,10,0,0) }; buy.Children.Add(radios);
   var qty=Ui.Input("5",90); qty.MaxLength=3; var total=Text("",14,Ui.Gold);
   Action recalc=()=>{ int q; if(chosen==null||!int.TryParse(qty.Text,out q)||q<1||q>100) { total.Text=AppLanguage.T("จำนวน 1-100 โค้ด"); return; } total.Text=AppLanguage.T("รวม ")+(Ui.Thai?Ui.Money(chosen.agentPriceThb*q,"THB"):Ui.Money(chosen.agentPriceUsd*q,"USD")); };
   foreach(var p in plans) { var plan=p; var rb=new RadioButton { GroupName="codeplan",IsChecked=plan==chosen,Foreground=Foreground,Margin=new Thickness(0,4,0,0),Content=AppLanguage.T(Ui.PlanName(plan.code))+"  •  "+(Ui.Thai?Ui.Money(plan.agentPriceThb,"THB"):Ui.Money(plan.agentPriceUsd,"USD"))+AppLanguage.T(" / โค้ด") }; rb.Checked+=(s,e)=>{ chosen=plan; recalc(); }; radios.Children.Add(rb); }
   var qrow=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,10,0,0) }; qrow.Children.Add(Text("จำนวนโค้ด",13,muted)); qty.Margin=new Thickness(12,0,12,0); qrow.Children.Add(qty); qrow.Children.Add(total); buy.Children.Add(qrow);
   qty.TextChanged+=(s,e)=>recalc(); recalc();
   var payRow=new WrapPanel { Margin=new Thickness(0,6,0,0) }; buy.Children.Add(payRow);
   Func<int> q0=()=>{ int q; return int.TryParse(qty.Text,out q)&&q>=1&&q<=100?q:0; };
   var bw=Btn("จ่ายด้วยกระเป๋าเงิน",Ui.Gold); bw.Foreground=Ui.B("#2A1C00"); payRow.Children.Add(bw); bw.Click+=async(s,e)=>{ int q=q0(); if(q==0||chosen==null) return; await PayWallet(chosen,"codes",null,q,bw); };
   var bq=Btn("QR พร้อมเพย์",accent); payRow.Children.Add(bq); bq.Click+=async(s,e)=>{ int q=q0(); if(q==0||chosen==null) return; await PayPromptPay(chosen.code,null,bq,"codes",q); };
   var bp=Btn("PayPal",ColorBrush("#FFC439")); bp.Foreground=ColorBrush("#14274E"); payRow.Children.Add(bp); bp.Click+=async(s,e)=>{ int q=q0(); if(q==0||chosen==null) return; await PayOnline(chosen.code,"paypal",null,"codes",q); };
   var bc=Btn("บัตรเครดิต / เดบิต",ColorBrush("#193B58")); payRow.Children.Add(bc); bc.Click+=async(s,e)=>{ int q=q0(); if(q==0||chosen==null) return; await PayOnline(chosen.code,"card",null,"codes",q); };
   holder.Children.Add(Box(buy));

   // 4. my codes
   var unused=codes.Where(c=>J.T(c,"redeemed_at")==null).ToList();
   var codesHead=new DockPanel(); codesHead.Children.Add(Ui.Bold(Text("โค้ดของฉัน ("+unused.Count+" ยังไม่ใช้ / "+codes.Count+" ทั้งหมด)",20)));
   if(unused.Count>0) { var all=Btn("คัดลอกโค้ดที่ยังไม่ใช้ทั้งหมด",ColorBrush("#193B58")); all.FontSize=12; all.HorizontalAlignment=HorizontalAlignment.Right; all.Click+=(s,e)=>{ Ui.Copy(string.Join("\r\n",unused.Select(c=>J.S(c,"code")+"  ("+AppLanguage.T(Ui.PlanName(J.S(c,"plan_code")))+")"))); status.Text=AppLanguage.T("คัดลอกโค้ดแล้ว"); }; codesHead.Children.Add(all); }
   holder.Children.Add(codesHead);
   var codeList=new StackPanel();
   if(codes.Count==0) codeList.Children.Add(Text("ยังไม่มีโค้ด",13,muted));
   foreach(var c in codes.Take(200)) {
    string cv=J.S(c,"code"); bool used=J.T(c,"redeemed_at")!=null;
    var r=new DockPanel { Margin=new Thickness(0,4,0,0) };
    if(!used) { var cp=Btn("คัดลอก",ColorBrush("#193B58")); cp.FontSize=11; cp.Padding=new Thickness(10,3,10,3); cp.Margin=new Thickness(0); DockPanel.SetDock(cp,Dock.Right); cp.Click+=(s,e)=>{ Ui.Copy(cv); status.Text=AppLanguage.T("คัดลอกโค้ดแล้ว"); }; r.Children.Add(cp); }
    var t=Line(cv,14,used?muted:Foreground); t.FontFamily=new FontFamily("Consolas"); t.Width=190; r.Children.Add(t);
    r.Children.Add(Line(AppLanguage.T(Ui.PlanName(J.S(c,"plan_code")))+"  •  "+(used?AppLanguage.T("ใช้แล้วโดย ")+J.S(c,"redeemed_name")+" (ID "+J.L(c,"redeemed_ptv_id").ToString("00000000")+") "+Ui.Date(J.T(c,"redeemed_at")):AppLanguage.T("ยังไม่ใช้")),12,used?muted:Ui.Green));
    codeList.Children.Add(r);
   }
   holder.Children.Add(Box(codeList));

   // 5. customers
   holder.Children.Add(Ui.Bold(Text("ลูกค้าของฉัน ("+customers.Count+")",20)));
   var custList=new StackPanel();
   if(customers.Count==0) custList.Children.Add(Text("ยังไม่มีลูกค้าผูกกับคุณ • แจกรหัสตัวแทนหรือโค้ด VIP ให้ลูกค้า",13,muted));
   foreach(var c in customers) {
    var r=new DockPanel { Margin=new Thickness(0,5,0,0) };
    var earn=Line("+"+Ui.Local(J.M(c,"earned_thb")),13,Ui.Green); DockPanel.SetDock(earn,Dock.Right); r.Children.Add(earn);
    r.Children.Add(Ui.Avatar(J.S(c,"avatar_url"),J.S(c,"display_name"),30));
    var until=J.T(c,"vip_until"); bool vip=until!=null&&until.Value>DateTime.UtcNow;
    var info=Line("  "+J.S(c,"display_name")+"  •  ID "+J.L(c,"ptv_id").ToString("00000000")+"  •  "+(J.B(c,"active")?AppLanguage.T("ผูกถึง ")+Ui.Date(J.T(c,"linked_until")):AppLanguage.T("หมดอายุการผูกแล้ว"))+"  •  "+(vip?AppLanguage.T("VIP ถึง ")+Ui.Date(until):AppLanguage.T("ไม่มี VIP")),12,muted); r.Children.Add(info);
    custList.Children.Add(r);
   }
   holder.Children.Add(Box(custList));

   // 6. withdraw
   holder.Children.Add(Ui.Bold(Text("ถอนเงิน",20)));
   var wd=new StackPanel { Width=520 };
   wd.Children.Add(Text("ขั้นต่ำ "+Ui.Local(J.M(d,"min_withdrawal_thb"))+" • แอดมินโอนให้แล้วกดยืนยันในระบบ",12,muted));
   var amtRow=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,0) }; wd.Children.Add(amtRow);
   amtRow.Children.Add(Line(Ui.Thai?"จำนวน (บาท)":"Amount (USD)",13,muted)); var amt=Ui.Input(wallet>0?(Ui.Thai?wallet:Math.Floor(wallet/Ui.Rate*100)/100).ToString("0.00",System.Globalization.CultureInfo.InvariantCulture):"",140); amt.Margin=new Thickness(12,4,12,10); amtRow.Children.Add(amt);
   var method=new ComboBox { ItemsSource=new[]{AppLanguage.T("พร้อมเพย์"),AppLanguage.T("บัญชีธนาคาร")},SelectedIndex=0,Width=150,Margin=new Thickness(0,4,0,10) }; amtRow.Children.Add(method);
   wd.Children.Add(Text("เลขพร้อมเพย์ หรือ ธนาคาร + เลขบัญชี + ชื่อบัญชี",12,muted));
   var acct=Ui.Input(); acct.MaxLength=200; wd.Children.Add(acct);
   var req=Btn("ขอถอนเงิน",accent); req.HorizontalAlignment=HorizontalAlignment.Left; wd.Children.Add(req);
   req.Click+=async(s,e)=>{
    decimal a; if(!decimal.TryParse(amt.Text.Trim(),System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out a)||a<=0) { status.Text=AppLanguage.T("จำนวนเงินไม่ถูกต้อง"); return; }
    if(acct.Text.Trim().Length<5) { status.Text=AppLanguage.T("กรุณากรอกบัญชีที่จะรับเงิน"); return; }
    decimal thb=Ui.Thai?a:Math.Round(a*Ui.Rate,2);
    if(MessageBox.Show(AppLanguage.T("ขอถอน ")+Ui.Local(thb)+AppLanguage.T(" ไปที่ ")+acct.Text.Trim()+"?","PTVgame",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return;
    await Act(req,"ส่งคำขอถอนเงินแล้ว • รอแอดมินโอน",async()=>{ await AccountService.RequestWithdrawal(thb,method.SelectedIndex==0?"promptpay":"bank",acct.Text.Trim()); Render(); });
   };
   foreach(var x in withdrawals.Take(10)) {
    string st=J.S(x,"status"); var r=new DockPanel { Margin=new Thickness(0,6,0,0) };
    var pill=Ui.Pill(st=="paid"?"โอนแล้ว":st=="rejected"?"ถูกปฏิเสธ":"รอโอน",st=="paid"?Ui.Green:st=="rejected"?Ui.Red:Ui.Gold,Ui.B("#14202B")); r.Children.Add(pill);
    r.Children.Add(Line(Ui.Local(J.M(x,"amount_thb"))+"  •  "+Ui.Date(J.T(x,"created_at"))+(string.IsNullOrEmpty(J.S(x,"admin_note"))?"":"  •  "+J.S(x,"admin_note")),12,muted));
    wd.Children.Add(r);
   }
   holder.Children.Add(Box(wd));

   holder.Children.Add(Ui.Bold(Text("ประวัติคำสั่งซื้อของตัวแทน",20)));
   var orders=new StackPanel { Margin=new Thickness(0,10,0,0) }; holder.Children.Add(orders); FillOrders(orders,id,true);
  }

  // ---------------------------------------------------------------- admin
  void AdminPage() {
   int id=renderId;
   sections.Children.Add(Ui.Bold(Text("⚑  แอดมิน",30)));
   var tabs=new WrapPanel { Margin=new Thickness(0,10,0,16) }; sections.Children.Add(tabs);
   foreach(var t in new[]{"ภาพรวม","ผู้ใช้","คำสั่งซื้อ","ถอนเงินตัวแทน","ราคาและการชำระเงิน","บันทึกระบบ"}) {
    var tab=t; var b=Btn(t,t==adminTab?accent:ColorBrush("#193B58")); b.Click+=(s,e)=>{ adminTab=tab; Render(); }; tabs.Children.Add(b);
   }
   var body=new StackPanel(); sections.Children.Add(body); body.Children.Add(Text("กำลังโหลด…",13,muted));
   if(adminTab=="ภาพรวม") AdminOverview(body,id);
   else if(adminTab=="ผู้ใช้") AdminUsers(body,id);
   else if(adminTab=="คำสั่งซื้อ") AdminOrders(body,id);
   else if(adminTab=="ราคาและการชำระเงิน") AdminPrices(body,id);
   else if(adminTab=="ถอนเงินตัวแทน") AdminWithdrawals(body,id);
   else AdminLog(body,id);
  }
  void AdminError(StackPanel body,Exception ex) { body.Children.Clear(); body.Children.Add(Line(AppLanguage.T("โหลดไม่สำเร็จ: ")+ex.Message,13,Ui.Red)); }

  async void AdminOverview(StackPanel body,int id) {
   Dictionary<string,object> s;
   try { s=await AccountService.AdminStats(); } catch(Exception ex) { if(id==renderId) AdminError(body,ex); return; }
   if(id!=renderId) return; body.Children.Clear();
   var grid=new WrapPanel(); body.Children.Add(grid);
   foreach(var tile in new[]{ new[]{"ผู้ใช้ทั้งหมด",J.S(s,"users")},new[]{"VIP ที่ใช้งานอยู่",J.S(s,"vip")},new[]{"ตัวแทน",J.S(s,"agents")},new[]{"สลิปรอตรวจ",J.S(s,"review")},
     new[]{"รอชำระ",J.S(s,"pending")},new[]{"สมัครใหม่ 7 วัน",J.S(s,"new_users_7d")},new[]{"รายได้ 30 วัน (USD)",Ui.Money(J.M(s,"revenue_usd_30d"),"USD")},new[]{"รายได้ 30 วัน (THB)",Ui.Money(J.M(s,"revenue_thb_30d"),"THB")},
     new[]{"คำขอถอนเงินรอโอน",J.S(s,"withdrawals")},new[]{"ยอดในกระเป๋าตัวแทนรวม",Ui.Money(J.M(s,"wallets_thb"),"THB")} }) {
    var p=new StackPanel { Width=170 }; p.Children.Add(Text(tile[0],12,muted)); p.Children.Add(Ui.Bold(Line(tile[1],28))); var b=Box(p); b.Padding=new Thickness(18); grid.Children.Add(b);
   }
   if(J.L(s,"withdrawals")>0) { var gw=Btn("ดูคำขอถอนเงิน",Ui.Gold); gw.Foreground=Ui.B("#2A1C00"); gw.HorizontalAlignment=HorizontalAlignment.Left; gw.Click+=(x,e)=>{ adminTab="ถอนเงินตัวแทน"; Render(); }; body.Children.Add(gw); }
   if(J.L(s,"review")>0) { var go=Btn("ตรวจสลิปที่รออยู่",Ui.Gold); go.Foreground=Ui.B("#2A1C00"); go.HorizontalAlignment=HorizontalAlignment.Left; go.Click+=(x,e)=>{ adminTab="คำสั่งซื้อ"; adminOrderFilter="review"; Render(); }; body.Children.Add(go); }
  }

  async void AdminUsers(StackPanel body,int id) {
   List<Dictionary<string,object>> users;
   try { users=await AccountService.AdminUsers(adminSearch); } catch(Exception ex) { if(id==renderId) AdminError(body,ex); return; }
   if(id!=renderId) return; body.Children.Clear();
   var bar=new StackPanel { Orientation=Orientation.Horizontal }; body.Children.Add(bar);
   var q=Ui.Input(adminSearch,300); q.ToolTip=AppLanguage.T("ค้นหาด้วย ID, ชื่อ หรืออีเมล"); bar.Children.Add(q);
   var go=Btn("ค้นหาผู้ใช้",accent); go.Margin=new Thickness(10,0,0,6); go.IsDefault=true; bar.Children.Add(go); go.Click+=(s,e)=>{ adminSearch=q.Text.Trim(); Render(); };
   body.Children.Add(Text(users.Count+" บัญชี (แสดงสูงสุด 100)",12,muted));
   foreach(var u in users) {
    string uid=J.S(u,"id"); var until=J.T(u,"vip_until"); bool vip=until!=null&&until.Value>DateTime.UtcNow; bool banned=J.B(u,"banned");
    var row=new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
    row.Children.Add(Ui.Avatar(J.S(u,"avatar_url"),J.S(u,"display_name"),44));
    var info=new StackPanel { Margin=new Thickness(12,0,12,0),VerticalAlignment=VerticalAlignment.Center }; Grid.SetColumn(info,1); row.Children.Add(info);
    var n=new StackPanel { Orientation=Orientation.Horizontal }; n.Children.Add(Ui.Bold(Line(J.S(u,"display_name"),15))); n.Children.Add(Line("   ID "+J.L(u,"ptv_id").ToString("00000000"),13,muted)); info.Children.Add(n);
    info.Children.Add(Line((J.S(u,"email")??"-")+" • "+(J.S(u,"provider")??"")+" • "+AppLanguage.T("สมัคร ")+Ui.Date(J.T(u,"created_at"))+" • "+AppLanguage.T("ล่าสุด ")+Ui.Date(J.T(u,"last_seen")),12,muted));
    var tags=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,4,0,0) }; info.Children.Add(tags);
    tags.Children.Add(Ui.Pill(Ui.Role(J.S(u,"role")),ColorBrush("#193B58"),Brushes.White));
    tags.Children.Add(Ui.Pill(vip?"VIP ถึง "+Ui.Date(until):"ไม่มี VIP",vip?Ui.Gold:ColorBrush("#2B3B4A"),vip?Ui.B("#2A1C00"):Brushes.White));
    if(banned) tags.Children.Add(Ui.Pill("ถูกระงับ",Ui.Red,Brushes.White));
    if(!string.IsNullOrEmpty(J.S(u,"agent_code"))) info.Children.Add(Line(AppLanguage.T("รหัสตัวแทน ")+J.S(u,"agent_code")+" • "+AppLanguage.T("กระเป๋า ")+Ui.Money(J.M(u,"wallet_thb"),"THB")+" • "+AppLanguage.T("ลูกค้า ")+J.L(u,"customers"),12,Ui.Gold));
    if(!string.IsNullOrEmpty(J.S(u,"agent_name"))) info.Children.Add(Line(AppLanguage.T("ผูกกับตัวแทน ")+J.S(u,"agent_name")+" (ID "+J.L(u,"agent_ptv_id").ToString("00000000")+")",12,muted));
    var act=new WrapPanel { MaxWidth=330,VerticalAlignment=VerticalAlignment.Center }; Grid.SetColumn(act,2); row.Children.Add(act);
    var role=new ComboBox { ItemsSource=new[]{"user","agent","admin"},SelectedItem=J.S(u,"role"),Width=90,Margin=new Thickness(0,6,8,0) }; act.Children.Add(role);
    var setRole=Btn("ตั้งระดับ",ColorBrush("#193B58")); setRole.FontSize=12; act.Children.Add(setRole);
    setRole.Click+=async(s,e)=>await Act(setRole,"เปลี่ยนระดับแล้ว",async()=>{ await AccountService.AdminSetRole(uid,(string)role.SelectedItem); Render(); });
    foreach(var d in new[]{30,365}) { int days=d; var add=Btn("+"+d+" วัน",Ui.Gold); add.Foreground=Ui.B("#2A1C00"); add.FontSize=12; act.Children.Add(add);
     add.Click+=async(s,e)=>{ if(MessageBox.Show(AppLanguage.T("เพิ่ม VIP ")+days+AppLanguage.T(" วัน ให้ ")+J.S(u,"display_name")+"?","PTVgame",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return; await Act(add,"เพิ่ม VIP แล้ว",async()=>{ await AccountService.AdminGrantVip(uid,days); Render(); }); }; }
    if(vip) { var rm=Btn("ลบ VIP",ColorBrush("#3A2530")); rm.FontSize=12; act.Children.Add(rm); rm.Click+=async(s,e)=>{ if(MessageBox.Show(AppLanguage.T("ลบ VIP ของ ")+J.S(u,"display_name")+"?","PTVgame",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return; await Act(rm,"ลบ VIP แล้ว",async()=>{ await AccountService.AdminGrantVip(uid,0); Render(); }); }; }
    var link=Btn("ผูกตัวแทน",ColorBrush("#193B58")); link.FontSize=12; act.Children.Add(link);
    link.Click+=async(s,e)=>{ string v=Ask("ID ตัวแทน 8 หลัก (เว้นว่าง = ยกเลิกการผูก)",""); if(v==null&&MessageBox.Show(AppLanguage.T("ยกเลิกการผูกตัวแทนของ ")+J.S(u,"display_name")+"?","PTVgame",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return; long ag; long? agent=v!=null&&long.TryParse(v,out ag)?(long?)ag:null; await Act(link,"บันทึกแล้ว",async()=>{ await AccountService.AdminSetReferrer(uid,agent); Render(); }); };
    if(!string.IsNullOrEmpty(J.S(u,"agent_code"))) { var adj=Btn("ปรับกระเป๋า",ColorBrush("#193B58")); adj.FontSize=12; act.Children.Add(adj);
     adj.Click+=async(s,e)=>{ string v=Ask("จำนวนบาทที่จะเพิ่ม (ใส่ลบเพื่อหัก เช่น -100)",""); decimal amount; if(v==null||!decimal.TryParse(v,System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out amount)||amount==0) return; await Act(adj,"ปรับยอดกระเป๋าแล้ว",async()=>{ await AccountService.AdminAdjustWallet(uid,amount,AppLanguage.T("แอดมินปรับยอด")); Render(); }); }; }
    if(uid!=AccountService.Current.id) { var ban=Btn(banned?"ปลดระงับ":"ระงับบัญชี",banned?ColorBrush("#1E5A3E"):ColorBrush("#5A1E28")); ban.FontSize=12; act.Children.Add(ban);
     ban.Click+=async(s,e)=>{ if(!banned&&MessageBox.Show(AppLanguage.T("ระงับบัญชี ")+J.S(u,"display_name")+"?","PTVgame",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return; await Act(ban,"บันทึกแล้ว",async()=>{ await AccountService.AdminSetBanned(uid,!banned); Render(); }); }; }
    body.Children.Add(Box(row));
   }
  }

  async void AdminOrders(StackPanel body,int id) {
   List<Dictionary<string,object>> orders;
   try { orders=await AccountService.AdminOrders(adminOrderFilter); } catch(Exception ex) { if(id==renderId) AdminError(body,ex); return; }
   if(id!=renderId) return; body.Children.Clear();
   var filters=new WrapPanel(); body.Children.Add(filters);
   foreach(var f in new[]{"review","pending","paid","rejected","cancelled",""}) {
    var key=f; var b=Btn(f==""?"ทั้งหมด":Ui.Status(f),f==adminOrderFilter?accent:ColorBrush("#193B58")); b.FontSize=12; b.Click+=(s,e)=>{ adminOrderFilter=key; Render(); }; filters.Children.Add(b);
   }
   if(orders.Count==0) { var none=Text("ไม่มีรายการ",14,muted); none.Margin=new Thickness(0,14,0,0); body.Children.Add(none); return; }
   foreach(var o in orders) {
    string oid=J.S(o,"id"),st=J.S(o,"status"),slip=J.S(o,"slip_path");
    var row=new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
    var info=new StackPanel(); row.Children.Add(info);
    var h=new StackPanel { Orientation=Orientation.Horizontal }; h.Children.Add(Ui.Pill(Ui.Status(st),Ui.StatusColor(st),Ui.B("#14202B"))); h.Children.Add(Ui.Bold(Line("#"+J.S(o,"order_no")+"  "+Ui.Money(J.M(o,"amount"),J.S(o,"currency"))+"  •  "+AppLanguage.T(Ui.PlanName(J.S(o,"plan_code"))),15))); info.Children.Add(h);
    string buyer=J.S(o,"buyer_name")+" (ID "+J.L(o,"buyer_ptv_id").ToString("00000000")+")",target=J.S(o,"target_name")+" (ID "+J.L(o,"target_ptv_id").ToString("00000000")+")";
    info.Children.Add(Line(AppLanguage.T("ผู้ซื้อ: ")+buyer+(J.L(o,"buyer_ptv_id")!=J.L(o,"target_ptv_id")?AppLanguage.T("  →  ผู้รับ: ")+target:"")+(J.B(o,"agent_price")?AppLanguage.T("  • ราคาตัวแทน"):""),12,muted));
    if(J.S(o,"kind")=="codes") info.Children.Add(Line(AppLanguage.T("ชุดโค้ด VIP x")+J.L(o,"quantity"),12,Ui.Gold));
    if(J.L(o,"discount_pct")>0||J.M(o,"commission_thb")>0||!string.IsNullOrEmpty(J.S(o,"commission_agent_name"))) info.Children.Add(Line((J.L(o,"discount_pct")>0?AppLanguage.T("ส่วนลด ")+J.L(o,"discount_pct")+"% ("+J.S(o,"discount_code")+")  ":"")+(string.IsNullOrEmpty(J.S(o,"commission_agent_name"))?"":AppLanguage.T("ตัวแทน ")+J.S(o,"commission_agent_name")+"  ")+(J.M(o,"commission_thb")>0?AppLanguage.T("ค่าคอม ")+Ui.Money(J.M(o,"commission_thb"),"THB"):""),12,Ui.Gold));
    info.Children.Add(Line(AppLanguage.T(Ui.Method(J.S(o,"method")))+" • "+AppLanguage.T("สร้าง ")+Ui.Date(J.T(o,"created_at"))+(J.T(o,"paid_at")!=null?AppLanguage.T(" • ชำระ ")+Ui.Date(J.T(o,"paid_at")):"")+(string.IsNullOrEmpty(J.S(o,"paypal_order_id"))?"":" • PayPal "+J.S(o,"paypal_order_id")),12,muted));
    if(!string.IsNullOrEmpty(J.S(o,"note"))) info.Children.Add(Line(AppLanguage.T("หมายเหตุ: ")+J.S(o,"note"),12,Ui.Red));
    var act=new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center }; Grid.SetColumn(act,1); row.Children.Add(act);
    if(!string.IsNullOrEmpty(slip)) { var view=Btn("ดูสลิป",ColorBrush("#193B58")); view.FontSize=12; act.Children.Add(view); view.Click+=async(s,e)=>await ShowSlip(view,slip,"#"+J.S(o,"order_no")+" • "+Ui.Money(J.M(o,"amount"),J.S(o,"currency"))); }
    if(st=="review"||st=="pending") {
     var ok=Btn("อนุมัติ",ColorBrush("#1E7A52")); ok.FontSize=12; act.Children.Add(ok);
     ok.Click+=async(s,e)=>{ if(MessageBox.Show(AppLanguage.T("ยืนยันว่าได้รับเงิน ")+Ui.Money(J.M(o,"amount"),J.S(o,"currency"))+AppLanguage.T(" แล้ว และเปิด VIP ให้ ")+target+"?","PTVgame",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return; await Act(ok,"อนุมัติแล้ว • เปิด VIP ให้ผู้รับแล้ว",async()=>{ await AccountService.AdminApprove(oid); Render(); }); };
     var no=Btn("ปฏิเสธ",ColorBrush("#5A1E28")); no.FontSize=12; act.Children.Add(no);
     no.Click+=async(s,e)=>{ string reason=Ask("เหตุผลที่ปฏิเสธ (ผู้ซื้อจะเห็นข้อความนี้)","สลิปไม่ถูกต้อง หรือยอดไม่ตรง"); if(reason==null) return; await Act(no,"ปฏิเสธแล้ว",async()=>{ await AccountService.AdminReject(oid,reason); Render(); }); };
    }
    body.Children.Add(Box(row));
   }
  }
  async Task ShowSlip(Button b,string path,string caption) {
   b.IsEnabled=false;
   try {
    var bytes=await AccountService.AdminSlip(path); var w=Ui.Frame("สลิป "+caption,520,760);
    var scroll=new ScrollViewer { Margin=new Thickness(16,0,16,16),VerticalScrollBarVisibility=ScrollBarVisibility.Auto }; scroll.Content=new Image { Source=Ui.Bitmap(bytes),Stretch=Stretch.Uniform }; Ui.SetBody(w,scroll); w.ShowDialog();
   } catch(Exception ex) { status.Text=ex.Message; } finally { b.IsEnabled=true; }
  }
  string Ask(string question,string initial) {
   var w=Ui.Frame("แอดมิน",420,250); var p=new StackPanel { Margin=new Thickness(24,0,24,20) }; Ui.SetBody(w,p);
   p.Children.Add(Ui.T(question,14)); var box=Ui.Input(AppLanguage.T(initial)); box.MaxLength=300; p.Children.Add(box); string answer=null;
   var ok=Ui.Btn("บันทึกเหตุผล",Ui.B("#268EFF")); ok.IsDefault=true; ok.HorizontalAlignment=HorizontalAlignment.Left; ok.Click+=(s,e)=>{ answer=box.Text.Trim(); w.Close(); }; p.Children.Add(ok);
   w.Loaded+=(s,e)=>{ box.Focus(); box.SelectAll(); }; w.ShowDialog(); return string.IsNullOrEmpty(answer)?null:answer;
  }

  async void AdminPrices(StackPanel body,int id) {
   List<Plan> plans; Dictionary<string,object> cfg;
   try { plans=await AccountService.AllPlans(); cfg=await AccountService.AdminSettings(); } catch(Exception ex) { if(id==renderId) AdminError(body,ex); return; }
   if(id!=renderId) return; body.Children.Clear();
   var wrap=new WrapPanel(); body.Children.Add(wrap);
   foreach(var plan in plans) {
    var p=plan; var c=new StackPanel { Width=300 }; c.Children.Add(Ui.Bold(Text(Ui.PlanName(p.code)+" ("+p.days+" วัน)",17)));
    Func<string,decimal,TextBox> field=(label,value)=>{ c.Children.Add(Text(label,12,muted)); var t=Ui.Input(value.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)); c.Children.Add(t); return t; };
    var usd=field("ราคาปกติ (USD)",p.priceUsd); var list=field("ราคาก่อนลด / ขีดฆ่า (USD)",p.listPriceUsd); var agentUsd=field("ราคาตัวแทน (USD)",p.agentPriceUsd);
    var thb=field("ราคา QR พร้อมเพย์ (บาท)",p.priceThb); var agentThb=field("ราคาตัวแทน QR (บาท)",p.agentPriceThb);
    var active=new CheckBox { Content=AppLanguage.T("เปิดขาย"),IsChecked=p.active,Foreground=Foreground,Margin=new Thickness(0,0,0,8) }; c.Children.Add(active);
    var save=Btn("บันทึกราคา",accent); save.HorizontalAlignment=HorizontalAlignment.Left; c.Children.Add(save);
    save.Click+=async(s,e)=>await Act(save,"บันทึกราคาแล้ว",async()=>{
     Func<TextBox,decimal> num=t=>{ decimal v; if(!decimal.TryParse(t.Text.Trim(),System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out v)||v<=0) throw new Exception(AppLanguage.T("ราคาไม่ถูกต้อง: ")+t.Text); return Math.Round(v,2); };
     await AccountService.AdminUpdatePlan(new Plan { code=p.code,priceUsd=num(usd),listPriceUsd=num(list),agentPriceUsd=num(agentUsd),priceThb=num(thb),agentPriceThb=num(agentThb),active=active.IsChecked==true });
    });
    wrap.Children.Add(Box(c));
   }
   var pp=new StackPanel { Width=330 }; pp.Children.Add(Ui.Bold(Text("พร้อมเพย์ (QR)",17)));
   pp.Children.Add(Text("เบอร์มือถือ 10 หลัก, เลขบัตรประชาชน/ผู้เสียภาษี 13 หลัก หรือ e-Wallet 15 หลัก",12,muted));
   var ppId=Ui.Input(J.S(cfg,"promptpay_id")); pp.Children.Add(ppId);
   pp.Children.Add(Text("ชื่อบัญชีที่แสดงใต้ QR",12,muted)); var ppName=Ui.Input(J.S(cfg,"promptpay_name")); pp.Children.Add(ppName);
   var savePp=Btn("บันทึกพร้อมเพย์",accent); savePp.HorizontalAlignment=HorizontalAlignment.Left; pp.Children.Add(savePp);
   savePp.Click+=async(s,e)=>await Act(savePp,"บันทึกพร้อมเพย์แล้ว",async()=>{ await AccountService.AdminSetSetting("promptpay_id",ppId.Text.Trim()); await AccountService.AdminSetSetting("promptpay_name",ppName.Text.Trim()); });
   var ag=new StackPanel { Width=330 }; ag.Children.Add(Ui.Bold(Text("ระบบตัวแทน",17)));
   var fields=new List<KeyValuePair<string,TextBox>>();
   foreach(var f in new[]{ new[]{"discount_pct","ส่วนลดครั้งแรกด้วยรหัสตัวแทน (%)"},new[]{"commission_months","ระยะเวลาผูกลูกค้ากับตัวแทน (เดือน)"},new[]{"usd_thb_rate","อัตราแปลง USD → บาท (ค่าคอมและการแสดงผล)"},new[]{"min_withdrawal_thb","ถอนเงินขั้นต่ำ (บาท)"},new[]{"download_url","ลิงก์ดาวน์โหลด Launcher (ปลายทางของลิงก์ตัวแทน)"} }) {
    ag.Children.Add(Text(f[1],12,muted)); var t=Ui.Input(J.S(cfg,f[0])); ag.Children.Add(t); fields.Add(new KeyValuePair<string,TextBox>(f[0],t));
   }
   var saveAg=Btn("บันทึกการตั้งค่าตัวแทน",accent); saveAg.HorizontalAlignment=HorizontalAlignment.Left; ag.Children.Add(saveAg);
   saveAg.Click+=async(s,e)=>await Act(saveAg,"บันทึกการตั้งค่าแล้ว",async()=>{ foreach(var kv in fields) await AccountService.AdminSetSetting(kv.Key,kv.Value.Text.Trim()); });
   wrap.Children.Add(Box(ag));
   var note=Text("PayPal และบัตร: ตั้งค่า PAYPAL_CLIENT_ID / PAYPAL_SECRET / PAYPAL_ENV ใน Supabase Edge Function secrets (ดู server/README.md)",12,muted); note.Margin=new Thickness(0,16,0,0); pp.Children.Add(note);
   wrap.Children.Add(Box(pp));
  }

  string adminWithdrawFilter="requested";
  async void AdminWithdrawals(StackPanel body,int id) {
   List<Dictionary<string,object>> list;
   try { list=await AccountService.AdminWithdrawals(adminWithdrawFilter); } catch(Exception ex) { if(id==renderId) AdminError(body,ex); return; }
   if(id!=renderId) return; body.Children.Clear();
   var filters=new WrapPanel(); body.Children.Add(filters);
   foreach(var f in new[]{ new[]{"requested","รอโอน"},new[]{"paid","โอนแล้ว"},new[]{"rejected","ถูกปฏิเสธ"},new[]{"","ทั้งหมด"} }) {
    var key=f[0]; var b=Btn(f[1],key==adminWithdrawFilter?accent:ColorBrush("#193B58")); b.FontSize=12; b.Click+=(s,e)=>{ adminWithdrawFilter=key; Render(); }; filters.Children.Add(b);
   }
   if(list.Count==0) { var none=Text("ไม่มีรายการ",14,muted); none.Margin=new Thickness(0,14,0,0); body.Children.Add(none); return; }
   foreach(var w in list) {
    string wid=J.S(w,"id"),st=J.S(w,"status"),acct=J.S(w,"account"); decimal amount=J.M(w,"amount_thb");
    var row=new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
    var info=new StackPanel(); row.Children.Add(info);
    var h=new StackPanel { Orientation=Orientation.Horizontal }; h.Children.Add(Ui.Pill(st=="paid"?"โอนแล้ว":st=="rejected"?"ถูกปฏิเสธ":"รอโอน",st=="paid"?Ui.Green:st=="rejected"?Ui.Red:Ui.Gold,Ui.B("#14202B"))); h.Children.Add(Ui.Bold(Line(Ui.Money(amount,"THB")+"  •  "+J.S(w,"agent_name")+"  (ID "+J.L(w,"agent_ptv_id").ToString("00000000")+")",15))); info.Children.Add(h);
    info.Children.Add(Line((J.S(w,"method")=="bank"?AppLanguage.T("บัญชีธนาคาร"):AppLanguage.T("พร้อมเพย์"))+": "+acct,13,Foreground));
    info.Children.Add(Line(AppLanguage.T("ขอเมื่อ ")+Ui.Date(J.T(w,"created_at"))+(J.T(w,"processed_at")!=null?AppLanguage.T(" • ดำเนินการ ")+Ui.Date(J.T(w,"processed_at")):"")+(string.IsNullOrEmpty(J.S(w,"admin_note"))?"":" • "+J.S(w,"admin_note")),12,muted));
    var act=new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center }; Grid.SetColumn(act,1); row.Children.Add(act);
    var copy=Btn("คัดลอกบัญชี",ColorBrush("#193B58")); copy.FontSize=12; copy.Click+=(s,e)=>{ Ui.Copy(acct); status.Text=AppLanguage.T("คัดลอกแล้ว"); }; act.Children.Add(copy);
    if(st=="requested") {
     var paid=Btn("โอนแล้ว",ColorBrush("#1E7A52")); paid.FontSize=12; act.Children.Add(paid);
     paid.Click+=async(s,e)=>{ if(MessageBox.Show(AppLanguage.T("ยืนยันว่าโอน ")+Ui.Money(amount,"THB")+AppLanguage.T(" ไปที่ ")+acct+AppLanguage.T(" แล้ว?"),"PTVgame",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return; await Act(paid,"บันทึกการโอนแล้ว",async()=>{ await AccountService.AdminProcessWithdrawal(wid,true,AppLanguage.T("โอนแล้ว")); Render(); }); };
     var no=Btn("ปฏิเสธ",ColorBrush("#5A1E28")); no.FontSize=12; act.Children.Add(no);
     no.Click+=async(s,e)=>{ string reason=Ask("เหตุผล (เงินจะคืนเข้ากระเป๋าตัวแทน)","ข้อมูลบัญชีไม่ถูกต้อง"); if(reason==null) return; await Act(no,"ปฏิเสธและคืนเงินแล้ว",async()=>{ await AccountService.AdminProcessWithdrawal(wid,false,reason); Render(); }); };
    }
    body.Children.Add(Box(row));
   }
  }

  async void AdminLog(StackPanel body,int id) {
   List<Dictionary<string,object>> log;
   try { log=await AccountService.AdminAudit(); } catch(Exception ex) { if(id==renderId) AdminError(body,ex); return; }
   if(id!=renderId) return; body.Children.Clear();
   if(log.Count==0) { body.Children.Add(Text("ยังไม่มีบันทึก",14,muted)); return; }
   foreach(var e in log) {
    var detail=e.ContainsKey("detail")?Storage.Json.Serialize(e["detail"]):"";
    var row=new StackPanel(); row.Children.Add(Ui.Bold(Line(Ui.Date(J.T(e,"at"))+"  •  "+J.S(e,"action"),13)));
    row.Children.Add(Line((J.S(e,"actor")??AppLanguage.T("ระบบอัตโนมัติ"))+(J.L(e,"target_ptv_id")>0?"  →  ID "+J.L(e,"target_ptv_id").ToString("00000000"):"")+"  "+detail,12,muted));
    var b=Box(row); b.Padding=new Thickness(14,10,14,10); b.Margin=new Thickness(0,0,0,8); body.Children.Add(b);
   }
  }
 }

 public static class AccountGate {
  /// <summary>Signs the player in (when account.json is configured) and then opens the launcher.</summary>
  public static void Run(Application app) {
   app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
   app.Startup+=(s,e)=>{
    if(AccountConfig.Current.Enabled&&!new LoginWindow().Show()) { app.Shutdown(); return; }
    var shell=new LauncherShell(); app.MainWindow=shell; app.ShutdownMode=ShutdownMode.OnMainWindowClose; shell.Show();
   };
   app.Run();
  }
 }
}
