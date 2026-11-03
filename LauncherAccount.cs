// PTVgame accounts: Supabase auth (email, Google, Facebook), profile, VIP, orders, admin.
// C# 5 only (built with the .NET Framework csc.exe): no string interpolation, no ?. operator.
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PTVgame {
 public class AccountConfig {
  public string supabaseUrl="",anonKey="";
  public string defaultAccess="vip";     // "vip": every game needs VIP unless catalog says "access":"free"
  public int callbackPort=53682;         // add http://127.0.0.1:53682/auth/callback to Supabase redirect URLs
  public int offlineHours=72;            // how long a verified VIP can play without internet
  static AccountConfig current;
  public static AccountConfig Current { get { if(current==null) current=Storage.Read(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"account.json"),new AccountConfig()); return current; } set { current=value; } }
  public bool Enabled { get { Uri u; return !string.IsNullOrWhiteSpace(anonKey)&&Uri.TryCreate(supabaseUrl??"",UriKind.Absolute,out u)&&u.Scheme=="https"; } }
  public string Base { get { return (supabaseUrl??"").TrimEnd('/'); } }
  public string CallbackBase { get { return "http://127.0.0.1:"+callbackPort; } }
 }

 public class Session { public string accessToken,refreshToken,userId,email,providerAvatar; public long expiresAt; }

 public class Account {
  public string id,displayName,avatarUrl,provider,role="user",email; public long ptvId; public DateTime? vipUntil; public bool isVip,banned; public DateTime checkedAt;
  public string agentCode,agentName; public long agentPtvId; public decimal walletThb,usdThbRate=34.5m; public bool discountUsed; public int discountPct=10; public DateTime? agentUntil;
  public bool IsAdmin { get { return role=="admin"; } }
  public bool IsAgent { get { return role=="agent"||role=="admin"; } }
  public string PtvIdText { get { return ptvId.ToString("00000000"); } }
 }

 public class Plan { public string code,name; public int days; public decimal priceUsd,listPriceUsd,agentPriceUsd,priceThb,agentPriceThb; public bool active; }

 /// <summary>Reads loosely typed JSON produced by JavaScriptSerializer.</summary>
 public static class J {
  public static Dictionary<string,object> Obj(string json) { return Storage.Json.Deserialize<Dictionary<string,object>>(json); }
  public static List<Dictionary<string,object>> List(string json) {
   var raw=Storage.Json.DeserializeObject(json) as object[]; var list=new List<Dictionary<string,object>>(); if(raw==null) return list;
   foreach(var o in raw) { var d=o as Dictionary<string,object>; if(d!=null) list.Add(d); } return list;
  }
  public static string S(Dictionary<string,object> d,string k) { object v; return d!=null&&d.TryGetValue(k,out v)&&v!=null?Convert.ToString(v,System.Globalization.CultureInfo.InvariantCulture):null; }
  public static decimal M(Dictionary<string,object> d,string k) { var s=S(d,k); decimal v; return decimal.TryParse(s,System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.InvariantCulture,out v)?v:0m; }
  public static long L(Dictionary<string,object> d,string k) { var s=S(d,k); long v; return long.TryParse(s,out v)?v:0; }
  public static bool B(Dictionary<string,object> d,string k) { object v; return d!=null&&d.TryGetValue(k,out v)&&v is bool&&(bool)v; }
  public static DateTime? T(Dictionary<string,object> d,string k) { var s=S(d,k); DateTimeOffset v; return !string.IsNullOrEmpty(s)&&DateTimeOffset.TryParse(s,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AssumeUniversal,out v)?(DateTime?)v.UtcDateTime:null; }
  public static List<Dictionary<string,object>> Arr(Dictionary<string,object> d,string k) {
   var list=new List<Dictionary<string,object>>(); object v; if(d==null||!d.TryGetValue(k,out v)||v==null) return list;
   var items=v as System.Collections.IEnumerable; if(items==null||v is string) return list;
   foreach(var o in items) { var x=o as Dictionary<string,object>; if(x!=null) list.Add(x); }
   return list;
  }
  public static Dictionary<string,object> D(Dictionary<string,object> d,string k) { object v; return d!=null&&d.TryGetValue(k,out v)?v as Dictionary<string,object>:null; }
 }

 /// <summary>DPAPI-protected files: only this Windows user on this PC can read them.</summary>
 public static class SecureFile {
  static readonly byte[] Entropy=Encoding.UTF8.GetBytes("PTVgame.account.v1");
  public static void Save(string path,object value) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path,ProtectedData.Protect(Encoding.UTF8.GetBytes(Storage.Json.Serialize(value)),Entropy,DataProtectionScope.CurrentUser)); }
  public static T Read<T>(string path) where T:class { try { return Storage.Json.Deserialize<T>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path),Entropy,DataProtectionScope.CurrentUser))); } catch { return null; } }
  public static void Delete(string path) { try { if(File.Exists(path)) File.Delete(path); } catch {} }
 }

 public class AccountException : Exception { public int Status; public AccountException(string message,int status) : base(message) { Status=status; } }

 public static class AccountService {
  static readonly HttpClient Http=CreateClient();
  static HttpClient CreateClient() { ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12; var h=new HttpClient(); h.Timeout=TimeSpan.FromSeconds(40); h.DefaultRequestHeaders.UserAgent.ParseAdd("PTVgame/"+LauncherUpdate.Version); return h; }
  static string SessionPath { get { return Path.Combine(Storage.Root,"session.dat"); } }
  static string CachePath { get { return Path.Combine(Storage.Root,"account.dat"); } }
  public static Session Session;
  public static Account Current;
  public static event Action Changed;
  static AccountConfig Cfg { get { return AccountConfig.Current; } }

  // ---------------------------------------------------------------- HTTP plumbing
  static async Task<string> Send(HttpMethod method,string url,string json,bool auth,byte[] bytes=null,string contentType=null,bool upsert=false) {
   if(auth) await EnsureFresh();
   using(var req=new HttpRequestMessage(method,url)) {
    req.Headers.Add("apikey",Cfg.anonKey);
    if(auth&&Session!=null) req.Headers.TryAddWithoutValidation("Authorization","Bearer "+Session.accessToken);
    if(upsert) req.Headers.Add("x-upsert","true");
    if(bytes!=null) { req.Content=new ByteArrayContent(bytes); req.Content.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue(contentType); }
    else if(json!=null) req.Content=new StringContent(json,Encoding.UTF8,"application/json");
    using(var res=await Http.SendAsync(req)) {
     string body=await res.Content.ReadAsStringAsync();
     if(!res.IsSuccessStatusCode) throw new AccountException(Friendly(ErrorText(body,(int)res.StatusCode)),(int)res.StatusCode);
     return body;
    }
   }
  }
  static string ErrorText(string body,int status) {
   try { var d=J.Obj(body); foreach(var k in new[]{"msg","message","error_description","error"}) { var s=J.S(d,k); if(!string.IsNullOrWhiteSpace(s)) return s; } } catch {}
   return "HTTP "+status;
  }
  static readonly Dictionary<string,string> Messages=new Dictionary<string,string> {
   {"Invalid login credentials","อีเมลหรือรหัสผ่านไม่ถูกต้อง"},
   {"Email not confirmed","ยังไม่ได้ยืนยันอีเมล กรุณากดลิงก์ในอีเมลก่อน"},
   {"User already registered","อีเมลนี้สมัครไว้แล้ว"},
   {"only agents can buy VIP for another ID","เฉพาะตัวแทนเท่านั้นที่ซื้อ VIP ให้ไอดีอื่นได้"},
   {"PTV ID not found","ไม่พบไอดีนี้"},
   {"agents only","เฉพาะตัวแทนเท่านั้น"},
   {"admin only","เฉพาะแอดมินเท่านั้น"},
   {"account suspended","บัญชีนี้ถูกระงับ"},
   {"too many unfinished orders","มีคำสั่งซื้อค้างอยู่มากเกินไป กรุณายกเลิกหรือชำระให้เสร็จก่อน"},
   {"PromptPay ID is not configured correctly","ยังไม่ได้ตั้งค่าพร้อมเพย์ หรือรูปแบบไม่ถูกต้อง"},
   {"session expired","เซสชันหมดอายุ กรุณาเข้าสู่ระบบใหม่"},
   {"Password should be at least","รหัสผ่านต้องยาวอย่างน้อย 6 ตัวอักษร"},
   {"discount code not found","ไม่พบโค้ดส่วนลดนี้"},
   {"you cannot use your own code","ใช้โค้ดของตัวเองไม่ได้"},
   {"agents already get agent prices","ตัวแทนได้ราคาตัวแทนอยู่แล้ว ใช้โค้ดส่วนลดไม่ได้"},
   {"discount already used on this ID","ไอดีนี้ใช้ส่วนลดครั้งแรกไปแล้ว"},
   {"already linked to another agent","ไอดีนี้ผูกกับตัวแทนคนอื่นแล้ว"},
   {"finish or cancel your other discounted order first","มีคำสั่งซื้อที่ใช้ส่วนลดค้างอยู่ กรุณาชำระหรือยกเลิกก่อน"},
   {"VIP code not found","ไม่พบโค้ด VIP นี้"},
   {"VIP code already used","โค้ด VIP นี้ถูกใช้ไปแล้ว"},
   {"not enough wallet balance","ยอดในกระเป๋าเงินไม่พอ"},
   {"below the minimum withdrawal","ยอดถอนต่ำกว่าขั้นต่ำ"},
   {"already have a pending withdrawal","มีคำขอถอนเงินรออยู่แล้ว"},
   {"quantity must be 1-100","จำนวนโค้ดต้องอยู่ระหว่าง 1-100"},
  };
  static string Friendly(string message) { foreach(var pair in Messages) if(message.IndexOf(pair.Key,StringComparison.OrdinalIgnoreCase)>=0) return pair.Value; return message; }

  static string Rest(string path) { return Cfg.Base+"/rest/v1/"+path; }
  public static Task<string> Rpc(string fn,object args) { return Send(HttpMethod.Post,Rest("rpc/"+fn),Storage.Json.Serialize(args??new Dictionary<string,object>()),true); }
  public static async Task<Dictionary<string,object>> RpcObj(string fn,object args) { var s=await Rpc(fn,args); return string.IsNullOrWhiteSpace(s)||s.Trim()=="null"?null:J.Obj(s); }
  public static async Task<List<Dictionary<string,object>>> RpcList(string fn,object args) { return J.List(await Rpc(fn,args)); }
  static Dictionary<string,object> A(params object[] kv) { var d=new Dictionary<string,object>(); for(int i=0;i+1<kv.Length;i+=2) d[(string)kv[i]]=kv[i+1]; return d; }

  // ---------------------------------------------------------------- auth
  static void Accept(string json) {
   var d=J.Obj(json); var user=J.D(d,"user"); var meta=J.D(user,"user_metadata");
   var s=new Session { accessToken=J.S(d,"access_token"),refreshToken=J.S(d,"refresh_token"),userId=J.S(user,"id"),email=J.S(user,"email") };
   long expiresIn=J.L(d,"expires_in"); s.expiresAt=J.L(d,"expires_at"); if(s.expiresAt==0) s.expiresAt=Now()+(expiresIn>0?expiresIn:3600);
   s.providerAvatar=J.S(meta,"avatar_url")??J.S(meta,"picture");
   if(Session!=null&&s.providerAvatar==null) s.providerAvatar=Session.providerAvatar;
   if(string.IsNullOrEmpty(s.accessToken)) throw new AccountException("ไม่ได้รับ token จากเซิร์ฟเวอร์",0);
   Session=s; SecureFile.Save(SessionPath,s);
  }
  static long Now() { return (long)(DateTime.UtcNow-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds; }
  static readonly SemaphoreSlim RefreshGate=new SemaphoreSlim(1,1);
  static async Task EnsureFresh() {
   if(Session==null) throw new AccountException("กรุณาเข้าสู่ระบบ",401);
   if(Session.expiresAt-Now()>90) return;
   await RefreshGate.WaitAsync();
   try { if(Session.expiresAt-Now()>90) return; Accept(await Send(HttpMethod.Post,Cfg.Base+"/auth/v1/token?grant_type=refresh_token",Storage.Json.Serialize(A("refresh_token",Session.refreshToken)),false)); }
   finally { RefreshGate.Release(); }
  }
  public static async Task SignIn(string email,string password) {
   Accept(await Send(HttpMethod.Post,Cfg.Base+"/auth/v1/token?grant_type=password",Storage.Json.Serialize(A("email",email.Trim(),"password",password)),false));
   await Load();
  }
  /// <returns>true when signed in immediately, false when the user must confirm by email first.</returns>
  // Email links (confirm sign-up, reset password) come back to this PC's launcher via the loopback
  // callback with a PKCE code, so no web page is needed and the player is signed in automatically.
  static string pendingVerifier;
  static string EmailRedirect { get { return "?redirect_to="+Uri.EscapeDataString(Cfg.CallbackBase+"/auth/callback"); } }
  static string NewChallenge() {
   pendingVerifier=Base64Url(RandomBytes(48));
   using(var sha=SHA256.Create()) return Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(pendingVerifier)));
  }
  public static async Task<bool> SignUp(string email,string password,string name) {
   string challenge=NewChallenge();
   var json=await Send(HttpMethod.Post,Cfg.Base+"/auth/v1/signup"+EmailRedirect,Storage.Json.Serialize(A("email",email.Trim(),"password",password,"data",A("full_name",name.Trim()),"code_challenge",challenge,"code_challenge_method","s256")),false);
   if(string.IsNullOrEmpty(J.S(J.Obj(json),"access_token"))) return false;
   Accept(json); await Load(); return true;
  }
  public static Task ResendSignup(string email) { return Send(HttpMethod.Post,Cfg.Base+"/auth/v1/resend"+EmailRedirect,Storage.Json.Serialize(A("type","signup","email",email.Trim())),false); }
  public static Task ResetPassword(string email) {
   string challenge=NewChallenge();
   return Send(HttpMethod.Post,Cfg.Base+"/auth/v1/recover"+EmailRedirect,Storage.Json.Serialize(A("email",email.Trim(),"code_challenge",challenge,"code_challenge_method","s256")),false);
  }
  /// <summary>Waits for the player to click the email link on this PC.</summary>
  /// <returns>true when signed in; false when the email is confirmed but the player should sign in with the password.</returns>
  public static async Task<bool> WaitForEmailLink(CancellationToken token,string message) {
   using(var listener=new Loopback(Cfg.callbackPort)) {
    var query=await listener.Wait("/auth/callback",TimeSpan.FromMinutes(30),token,message);
    string error; if(query.TryGetValue("error_description",out error)||query.TryGetValue("error",out error)) throw new AccountException(error.IndexOf("expired",StringComparison.OrdinalIgnoreCase)>=0?"ลิงก์หมดอายุแล้ว กรุณาขอใหม่":error,400);
    string code; if(!query.TryGetValue("code",out code)||pendingVerifier==null) return false;
    Accept(await Send(HttpMethod.Post,Cfg.Base+"/auth/v1/token?grant_type=pkce",Storage.Json.Serialize(A("auth_code",code,"code_verifier",pendingVerifier)),false));
    pendingVerifier=null; await Load(); return true;
   }
  }
  public static Task SetPassword(string password) { return Send(HttpMethod.Put,Cfg.Base+"/auth/v1/user",Storage.Json.Serialize(A("password",password)),true); }

  /// <summary>Google / Facebook sign-in in the user's browser (OAuth PKCE + loopback redirect).</summary>
  /// <summary>Which social logins are switched on in Supabase (Authentication > Providers).</summary>
  public static async Task<HashSet<string>> EnabledProviders() {
   var on=new HashSet<string>(); var d=J.D(J.Obj(await Send(HttpMethod.Get,Cfg.Base+"/auth/v1/settings",null,false)),"external");
   if(d!=null) foreach(var pair in d) if(pair.Value is bool&&(bool)pair.Value) on.Add(pair.Key);
   return on;
  }
  public static async Task SignInWith(string provider,CancellationToken token) {
   if(!(await EnabledProviders()).Contains(provider)) throw new AccountException(provider=="google"?"ยังไม่ได้เปิดล็อกอินด้วย Google ในเซิร์ฟเวอร์":"ยังไม่ได้เปิดล็อกอินด้วย Facebook ในเซิร์ฟเวอร์",400);
   string verifier=Base64Url(RandomBytes(48)),challenge;
   using(var sha=SHA256.Create()) challenge=Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
   string redirect=Cfg.CallbackBase+"/auth/callback";
   using(var listener=new Loopback(Cfg.callbackPort)) {
    string url=Cfg.Base+"/auth/v1/authorize?provider="+Uri.EscapeDataString(provider)+"&redirect_to="+Uri.EscapeDataString(redirect)+"&code_challenge="+challenge+"&code_challenge_method=s256";
    if(provider=="facebook") url+="&scopes="+Uri.EscapeDataString("email public_profile");
    OpenBrowser(url);
    var query=await listener.Wait("/auth/callback",TimeSpan.FromMinutes(5),token,"เข้าสู่ระบบ PTVgame สำเร็จ กลับไปที่ Launcher ได้เลย");
    string error; if(query.TryGetValue("error_description",out error)||query.TryGetValue("error",out error)) throw new AccountException(error,400);
    string code; if(!query.TryGetValue("code",out code)) throw new AccountException("ไม่ได้รับรหัสยืนยันจากผู้ให้บริการ",400);
    Accept(await Send(HttpMethod.Post,Cfg.Base+"/auth/v1/token?grant_type=pkce",Storage.Json.Serialize(A("auth_code",code,"code_verifier",verifier)),false));
   }
   await Load();
  }
  public static void OpenBrowser(string url) { Uri u; if(!Uri.TryCreate(url,UriKind.Absolute,out u)||(u.Scheme!="https"&&u.Scheme!="http")) throw new Exception("URL ไม่ถูกต้อง"); Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute=true }); }
  static byte[] RandomBytes(int n) { var b=new byte[n]; using(var r=RandomNumberGenerator.Create()) r.GetBytes(b); return b; }
  static string Base64Url(byte[] b) { return Convert.ToBase64String(b).TrimEnd('=').Replace('+','-').Replace('/','_'); }

  /// <summary>Restores the saved session. Offline, a recently verified account is allowed for offlineHours.</summary>
  public static async Task<bool> Restore() {
   Session=SecureFile.Read<Session>(SessionPath); if(Session==null||string.IsNullOrEmpty(Session.refreshToken)) return false;
   try { await Load(); return true; }
   catch(AccountException ex) { if(ex.Status==400||ex.Status==401||ex.Status==403) { SignOutLocal(); return false; } if(ex.Status!=0&&ex.Status<500) throw; }
   catch(HttpRequestException) {} catch(TaskCanceledException) {}
   var cached=SecureFile.Read<Account>(CachePath);
   if(cached!=null&&cached.id==Session.userId&&(DateTime.UtcNow-cached.checkedAt).TotalHours<Cfg.offlineHours) { Current=cached; Offline=true; Notify(); return true; }
   throw new AccountException("เชื่อมต่อเซิร์ฟเวอร์ไม่ได้ กรุณาตรวจอินเทอร์เน็ต",0);
  }
  public static bool Offline;
  public static async Task<Account> Load() {
   var d=await RpcObj("my_account",null);
   var a=new Account { id=J.S(d,"id"),ptvId=J.L(d,"ptv_id"),displayName=J.S(d,"display_name"),avatarUrl=J.S(d,"avatar_url"),provider=J.S(d,"provider"),role=J.S(d,"role")??"user",
    vipUntil=J.T(d,"vip_until"),isVip=J.B(d,"is_vip"),banned=J.B(d,"banned"),email=J.S(d,"email"),checkedAt=DateTime.UtcNow,
    agentCode=J.S(d,"agent_code"),agentName=J.S(d,"agent_name"),agentPtvId=J.L(d,"agent_ptv_id"),walletThb=J.M(d,"wallet_thb"),discountUsed=J.B(d,"discount_used"),discountPct=(int)J.L(d,"discount_pct"),agentUntil=J.T(d,"agent_until") };
   decimal rate=J.M(d,"usd_thb_rate"); if(rate>0) a.usdThbRate=rate;
   if(a.discountPct<=0) a.discountPct=10;
   if(a.banned) { SignOutLocal(); throw new AccountException("บัญชีนี้ถูกระงับ กรุณาติดต่อแอดมิน",403); }
   Current=a; Offline=false; SecureFile.Save(CachePath,a); Notify(); return a;
  }
  static void Notify() { var h=Changed; if(h!=null) h(); }
  public static async Task SignOut() {
   try { if(Session!=null) await Send(HttpMethod.Post,Cfg.Base+"/auth/v1/logout",null,true); } catch {}
   SignOutLocal();
  }
  static void SignOutLocal() { Session=null; Current=null; SecureFile.Delete(SessionPath); SecureFile.Delete(CachePath); }

  /// <summary>VIP check used before installing or launching a game.</summary>
  public static bool CanPlay(Game g) {
   if(!Cfg.Enabled) return true;
   string access=string.IsNullOrWhiteSpace(g.access)?Cfg.defaultAccess:g.access;
   if(access=="free") return Current!=null;
   if(Current==null) return false;
   if(Current.IsAdmin) return true;
   if(Current.vipUntil==null||Current.vipUntil.Value<=DateTime.UtcNow) return false;
   return !Offline||(DateTime.UtcNow-Current.checkedAt).TotalHours<Cfg.offlineHours;
  }
  public static bool RequiresVip(Game g) { if(!Cfg.Enabled) return false; return (string.IsNullOrWhiteSpace(g.access)?Cfg.defaultAccess:g.access)!="free"; }

  // ---------------------------------------------------------------- profile
  public static Task UpdateProfile(string name,string avatarUrl) { return Rpc("update_my_profile",A("p_display_name",name,"p_avatar_url",avatarUrl)); }
  public static async Task<string> UploadAvatar(byte[] png) {
   string path=Session.userId+"/avatar-"+DateTime.UtcNow.Ticks+".png";
   await Send(HttpMethod.Post,Cfg.Base+"/storage/v1/object/avatars/"+path,null,true,png,"image/png",true);
   return Cfg.Base+"/storage/v1/object/public/avatars/"+path;
  }
  static readonly Dictionary<string,Task<byte[]>> ImageCache=new Dictionary<string,Task<byte[]>>();
  public static Task<byte[]> Image(string url) {
   Uri u; if(!Uri.TryCreate(url??"",UriKind.Absolute,out u)||u.Scheme!="https") return Task.FromResult<byte[]>(null);
   lock(ImageCache) { Task<byte[]> t; if(!ImageCache.TryGetValue(url,out t)) { t=DownloadImage(u); ImageCache[url]=t; } return t; }
  }
  static async Task<byte[]> DownloadImage(Uri u) {
   using(var res=await Http.GetAsync(u,HttpCompletionOption.ResponseHeadersRead)) {
    res.EnsureSuccessStatusCode(); if(res.Content.Headers.ContentLength>3*1024*1024) throw new Exception("image too large");
    return await res.Content.ReadAsByteArrayAsync();
   }
  }

  // ---------------------------------------------------------------- store
  public static async Task<List<Plan>> Plans() {
   var list=J.List(await Send(HttpMethod.Get,Rest("plans?select=*&active=eq.true&order=sort"),null,Session!=null));
   return list.Select(d=>new Plan { code=J.S(d,"code"),name=J.S(d,"name"),days=(int)J.L(d,"days"),priceUsd=J.M(d,"price_usd"),listPriceUsd=J.M(d,"list_price_usd"),
    agentPriceUsd=J.M(d,"agent_price_usd"),priceThb=J.M(d,"price_thb"),agentPriceThb=J.M(d,"agent_price_thb"),active=J.B(d,"active") }).ToList();
  }
  public static async Task<List<Plan>> AllPlans() {
   var list=J.List(await Send(HttpMethod.Get,Rest("plans?select=*&order=sort"),null,true));
   return list.Select(d=>new Plan { code=J.S(d,"code"),name=J.S(d,"name"),days=(int)J.L(d,"days"),priceUsd=J.M(d,"price_usd"),listPriceUsd=J.M(d,"list_price_usd"),
    agentPriceUsd=J.M(d,"agent_price_usd"),priceThb=J.M(d,"price_thb"),agentPriceThb=J.M(d,"agent_price_thb"),active=J.B(d,"active") }).ToList();
  }
  public static Task<Dictionary<string,object>> CreatePromptPay(string plan,long? targetPtvId,string kind="vip",int quantity=1,string discountCode=null) { return RpcObj("create_order",A("p_plan",plan,"p_method","promptpay","p_target_ptv_id",targetPtvId,"p_kind",kind,"p_quantity",quantity,"p_discount_code",discountCode)); }
  public static Task<Dictionary<string,object>> CheckDiscount(string code) { return RpcObj("check_discount_code",A("p_code",code)); }
  public static Task<Dictionary<string,object>> RedeemCode(string code) { return RpcObj("redeem_code",A("p_code",code)); }
  public static Task<Dictionary<string,object>> WalletBuy(string plan,string kind,long? targetPtvId,int quantity) { return RpcObj("wallet_buy",A("p_plan",plan,"p_kind",kind,"p_target_ptv_id",targetPtvId,"p_quantity",quantity)); }
  public static Task<Dictionary<string,object>> MyAgent() { return RpcObj("my_agent",null); }
  public static Task RequestWithdrawal(decimal amount,string method,string account) { return Rpc("request_withdrawal",A("p_amount",amount,"p_method",method,"p_account",account)); }
  public static string AgentLink(string code) { return Cfg.Base+"/functions/v1/dl?ref="+Uri.EscapeDataString(code??""); }
  public static async Task AttachSlip(string orderId,byte[] image,string contentType) {
   string ext=contentType=="image/png"?".png":".jpg"; string path=Session.userId+"/"+orderId+"-"+DateTime.UtcNow.Ticks+ext;
   await Send(HttpMethod.Post,Cfg.Base+"/storage/v1/object/slips/"+path,null,true,image,contentType);
   await Rpc("attach_slip",A("p_order",orderId,"p_path",path));
  }
  public static Task CancelOrder(string orderId) { return Rpc("cancel_order",A("p_order",orderId)); }
  public static Task<List<Dictionary<string,object>>> MyOrders() { return RpcList("my_orders",null); }
  public static Task<Dictionary<string,object>> LookupPlayer(long ptvId) { return RpcObj("lookup_player",A("p_ptv_id",ptvId)); }

  /// <summary>PayPal or card: opens PayPal in the browser and waits for the loopback return.</summary>
  public static async Task<string> PayWithPayPal(string plan,string method,long? targetPtvId,IProgress<string> progress,CancellationToken token,string kind="vip",int quantity=1,string discountCode=null) {
   using(var listener=new Loopback(Cfg.callbackPort)) {
    string ret=Cfg.CallbackBase+"/paypal/return",cancel=Cfg.CallbackBase+"/paypal/cancel";
    progress.Report("กำลังสร้างรายการชำระเงิน…");
    var created=J.Obj(await Send(HttpMethod.Post,Cfg.Base+"/functions/v1/paypal-create",Storage.Json.Serialize(A("plan",plan,"method",method,"target_ptv_id",targetPtvId,"kind",kind,"quantity",quantity,"discount_code",discountCode,"return_url",ret,"cancel_url",cancel)),true));
    string orderId=J.S(created,"order_id"),approve=J.S(created,"approve_url");
    if(string.IsNullOrEmpty(approve)) throw new AccountException("PayPal ไม่ได้ส่งลิงก์ชำระเงินกลับมา",502);
    OpenBrowser(approve);
    progress.Report("ชำระเงินให้เสร็จในเบราว์เซอร์ แล้วระบบจะยืนยันให้อัตโนมัติ…");
    string path=await listener.WaitAny(new[]{"/paypal/return","/paypal/cancel"},TimeSpan.FromMinutes(20),token,"ได้รับข้อมูลแล้ว กลับไปที่ PTVgame Launcher ได้เลย");
    if(path=="/paypal/cancel") { try { await CancelOrder(orderId); } catch {} return "cancelled"; }
    progress.Report("กำลังยืนยันการชำระเงินกับ PayPal…");
    for(int attempt=0;attempt<6;attempt++) {
     var r=J.Obj(await Send(HttpMethod.Post,Cfg.Base+"/functions/v1/paypal-capture",Storage.Json.Serialize(A("order_id",orderId)),true));
     string status=J.S(r,"status"); if(status=="paid") { await Load(); return "paid"; } if(status=="failed") return "failed";
     await Task.Delay(3000,token);
    }
    return "waiting";
   }
  }

  // ---------------------------------------------------------------- admin
  public static Task<Dictionary<string,object>> AdminStats() { return RpcObj("admin_stats",null); }
  public static Task<List<Dictionary<string,object>>> AdminUsers(string search) { return RpcList("admin_list_users",A("p_search",search??"","p_limit",100,"p_offset",0)); }
  public static Task<List<Dictionary<string,object>>> AdminOrders(string status) { return RpcList("admin_list_orders",A("p_status",status??"","p_limit",100,"p_offset",0)); }
  public static Task AdminApprove(string orderId) { return Rpc("admin_approve_order",A("p_order",orderId)); }
  public static Task AdminReject(string orderId,string reason) { return Rpc("admin_reject_order",A("p_order",orderId,"p_reason",reason??"")); }
  public static Task AdminSetRole(string userId,string role) { return Rpc("admin_set_role",A("p_user",userId,"p_role",role)); }
  public static Task AdminGrantVip(string userId,int days) { return Rpc("admin_grant_vip",A("p_user",userId,"p_days",days)); }
  public static Task AdminSetBanned(string userId,bool banned) { return Rpc("admin_set_banned",A("p_user",userId,"p_banned",banned)); }
  public static Task AdminUpdatePlan(Plan p) { return Rpc("admin_update_plan",A("p_code",p.code,"p_price_usd",p.priceUsd,"p_list_price_usd",p.listPriceUsd,"p_agent_price_usd",p.agentPriceUsd,"p_price_thb",p.priceThb,"p_agent_price_thb",p.agentPriceThb,"p_active",p.active)); }
  public static Task<Dictionary<string,object>> AdminSettings() { return RpcObj("admin_get_settings",null); }
  public static Task AdminSetSetting(string key,string value) { return Rpc("admin_set_setting",A("p_key",key,"p_value",value??"")); }
  public static Task<List<Dictionary<string,object>>> AdminAudit() { return RpcList("admin_audit",A("p_limit",150)); }
  public static Task<List<Dictionary<string,object>>> AdminWithdrawals(string status) { return RpcList("admin_list_withdrawals",A("p_status",status??"")); }
  public static Task AdminProcessWithdrawal(string id,bool paid,string note) { return Rpc("admin_process_withdrawal",A("p_id",id,"p_paid",paid,"p_note",note??"")); }
  public static Task AdminAdjustWallet(string userId,decimal amount,string note) { return Rpc("admin_adjust_wallet",A("p_user",userId,"p_amount",amount,"p_note",note??"")); }
  public static Task AdminSetReferrer(string userId,long? agentPtvId) { return Rpc("admin_set_referrer",A("p_user",userId,"p_agent_ptv_id",agentPtvId)); }
  public static async Task<byte[]> AdminSlip(string path) {
   await EnsureFresh();
   using(var req=new HttpRequestMessage(HttpMethod.Get,Cfg.Base+"/storage/v1/object/authenticated/slips/"+path)) {
    req.Headers.Add("apikey",Cfg.anonKey); req.Headers.TryAddWithoutValidation("Authorization","Bearer "+Session.accessToken);
    using(var res=await Http.SendAsync(req)) { if(!res.IsSuccessStatusCode) throw new AccountException("โหลดสลิปไม่สำเร็จ",(int)res.StatusCode); return await res.Content.ReadAsByteArrayAsync(); }
   }
  }
 }

 /// <summary>Minimal HTTP listener on 127.0.0.1 for OAuth/PayPal browser redirects (no admin rights or URL ACL needed).</summary>
 public sealed class Loopback : IDisposable {
  readonly TcpListener listener;
  public Loopback(int port) {
   listener=new TcpListener(IPAddress.Loopback,port);
   try { listener.Start(); } catch(SocketException) { throw new AccountException("พอร์ต "+port+" ถูกใช้งานอยู่ ปิดหน้าต่างชำระเงิน/ล็อกอินอื่นแล้วลองใหม่",0); }
  }
  public async Task<Dictionary<string,string>> Wait(string path,TimeSpan timeout,CancellationToken token,string message) {
   Dictionary<string,string> query=null; await Accept(new[]{path},timeout,token,message,q=>query=q); return query;
  }
  public async Task<string> WaitAny(string[] paths,TimeSpan timeout,CancellationToken token,string message) {
   return await Accept(paths,timeout,token,message,null);
  }
  async Task<string> Accept(string[] paths,TimeSpan timeout,CancellationToken token,string message,Action<Dictionary<string,string>> onQuery) {
   using(var limit=CancellationTokenSource.CreateLinkedTokenSource(token)) {
    limit.CancelAfter(timeout);
    using(limit.Token.Register(()=>{ try { listener.Stop(); } catch {} })) {
     while(true) {
      TcpClient client;
      try { client=await listener.AcceptTcpClientAsync(); }
      catch(Exception) { limit.Token.ThrowIfCancellationRequested(); throw; }
      using(client) {
       var stream=client.GetStream(); stream.ReadTimeout=5000;
       string line=ReadLine(stream); // "GET /auth/callback?code=... HTTP/1.1"
       var parts=(line??"").Split(' '); string target=parts.Length>1?parts[1]:"/";
       int q=target.IndexOf('?'); string p=q<0?target:target.Substring(0,q); string qs=q<0?"":target.Substring(q+1);
       bool match=paths.Contains(p);
       string html="<!doctype html><meta charset='utf-8'><title>PTVgame</title><body style='background:#00101F;color:#fff;font-family:Segoe UI,sans-serif;display:grid;place-items:center;height:90vh'><div style='text-align:center'><h2>"+(match?WebUtility.HtmlEncode(AppLanguage.T(message)):"PTVgame")+"</h2><p style='color:#88AAC9'>"+WebUtility.HtmlEncode(AppLanguage.T("ปิดแท็บนี้ได้"))+"</p></div>";
       byte[] body=Encoding.UTF8.GetBytes(html);
       byte[] head=Encoding.ASCII.GetBytes("HTTP/1.1 "+(match?"200 OK":"404 Not Found")+"\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: "+body.Length+"\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");
       try { stream.Write(head,0,head.Length); stream.Write(body,0,body.Length); } catch {}
       if(!match) continue;
       if(onQuery!=null) onQuery(ParseQuery(qs));
       return p;
      }
     }
    }
   }
  }
  static string ReadLine(Stream s) { var sb=new StringBuilder(); int b; while(sb.Length<8192&&(b=s.ReadByte())>=0) { if(b=='\n') break; if(b!='\r') sb.Append((char)b); } return sb.ToString(); }
  public static Dictionary<string,string> ParseQuery(string qs) {
   var d=new Dictionary<string,string>();
   foreach(var pair in (qs??"").Split(new[]{'&'},StringSplitOptions.RemoveEmptyEntries)) { int i=pair.IndexOf('='); string k=Uri.UnescapeDataString((i<0?pair:pair.Substring(0,i)).Replace('+',' ')); d[k]=i<0?"":Uri.UnescapeDataString(pair.Substring(i+1).Replace('+',' ')); }
   return d;
  }
  public void Dispose() { try { listener.Stop(); } catch {} }
 }

 /// <summary>QR Code encoder (byte mode, error correction M) for PromptPay payloads. Based on the ISO/IEC 18004 algorithm.</summary>
 public static class QrCode {
  static readonly int[] EccPerBlock={-1,10,16,26,18,24,16,18,22,22,26,30,22,22,24,24,28,28,26,26,26,26,28,28,28,28,28,28,28,28,28,28,28,28,28,28,28,28,28,28,28};
  static readonly int[] Blocks={-1,1,1,1,2,2,4,4,4,5,5,5,8,9,9,10,10,11,13,14,16,17,17,18,20,21,23,25,26,28,29,31,33,35,37,38,40,43,45,47,49};
  static int RawModules(int v) { int r=(16*v+128)*v+64; if(v>=2) { int n=v/7+2; r-=(25*n-10)*n-55; if(v>=7) r-=36; } return r; }
  static int DataCodewords(int v) { return RawModules(v)/8-EccPerBlock[v]*Blocks[v]; }

  /// <returns>Square matrix, true = dark module (no quiet zone).</returns>
  public static bool[,] Encode(string text) {
   byte[] data=Encoding.UTF8.GetBytes(text); int version=0;
   for(int v=1;v<=40;v++) { int bits=4+(v<10?8:16)+data.Length*8; if(bits<=DataCodewords(v)*8) { version=v; break; } }
   if(version==0) throw new ArgumentException("QR data too long");
   var bb=new List<bool>(); Append(bb,4,4); Append(bb,data.Length,version<10?8:16); foreach(var b in data) Append(bb,b,8);
   int capacity=DataCodewords(version)*8; Append(bb,0,Math.Min(4,capacity-bb.Count)); Append(bb,0,(8-bb.Count%8)%8);
   for(int pad=0xEC;bb.Count<capacity;pad^=0xEC^0x11) Append(bb,pad,8);
   var codewords=new byte[bb.Count/8]; for(int i=0;i<bb.Count;i++) if(bb[i]) codewords[i>>3]|=(byte)(1<<(7-(i&7)));
   var all=Interleave(codewords,version);
   int size=version*4+17; var m=new bool[size,size]; var fn=new bool[size,size];
   DrawFunctionPatterns(m,fn,version,size);
   DrawCodewords(m,fn,all,size);
   int best=0; long bestPenalty=long.MaxValue;
   for(int mask=0;mask<8;mask++) { ApplyMask(m,fn,mask,size); DrawFormat(m,fn,mask,size); long p=Penalty(m,size); if(p<bestPenalty) { bestPenalty=p; best=mask; } ApplyMask(m,fn,mask,size); }
   ApplyMask(m,fn,best,size); DrawFormat(m,fn,best,size); return m;
  }
  static void Append(List<bool> bb,int value,int len) { for(int i=len-1;i>=0;i--) bb.Add(((value>>i)&1)!=0); }
  static byte[] Interleave(byte[] data,int v) {
   int n=Blocks[v],ecc=EccPerBlock[v],raw=RawModules(v)/8,shortCount=n-raw%n,shortLen=raw/n;
   var divisor=RsDivisor(ecc); var blocks=new List<byte[]>();
   for(int i=0,k=0;i<n;i++) {
    int len=shortLen-ecc+(i<shortCount?0:1); var dat=new byte[len]; Array.Copy(data,k,dat,0,len); k+=len;
    var rem=RsRemainder(dat,divisor); var block=new byte[shortLen+1];
    Array.Copy(dat,block,len); Array.Copy(rem,0,block,shortLen+1-ecc,ecc); blocks.Add(block);
   }
   var result=new List<byte>();
   for(int i=0;i<blocks[0].Length;i++) for(int j=0;j<blocks.Count;j++) if(i!=shortLen-ecc||j>=shortCount) result.Add(blocks[j][i]);
   return result.ToArray();
  }
  static byte[] RsDivisor(int degree) {
   var r=new byte[degree]; r[degree-1]=1; int root=1;
   for(int i=0;i<degree;i++) { for(int j=0;j<degree;j++) { r[j]=Mul(r[j],root); if(j+1<degree) r[j]^=r[j+1]; } root=Mul(root,2); }
   return r;
  }
  static byte[] RsRemainder(byte[] data,byte[] divisor) {
   var r=new byte[divisor.Length];
   foreach(byte b in data) { int f=b^r[0]; Array.Copy(r,1,r,0,r.Length-1); r[r.Length-1]=0; for(int i=0;i<r.Length;i++) r[i]^=Mul(divisor[i],f); }
   return r;
  }
  static byte Mul(int x,int y) { int z=0; for(int i=7;i>=0;i--) { z=(z<<1)^((z>>7)*0x11D); z^=((y>>i)&1)*x; } return (byte)z; }
  static void Set(bool[,] m,bool[,] fn,int x,int y,bool dark) { m[y,x]=dark; fn[y,x]=true; }
  static void DrawFunctionPatterns(bool[,] m,bool[,] fn,int v,int size) {
   for(int i=0;i<size;i++) { Set(m,fn,6,i,i%2==0); Set(m,fn,i,6,i%2==0); }
   foreach(var c in new[]{new[]{3,3},new[]{size-4,3},new[]{3,size-4}})
    for(int dy=-4;dy<=4;dy++) for(int dx=-4;dx<=4;dx++) { int d=Math.Max(Math.Abs(dx),Math.Abs(dy)),x=c[0]+dx,y=c[1]+dy; if(x>=0&&x<size&&y>=0&&y<size) Set(m,fn,x,y,d!=2&&d!=4); }
   if(v>1) {
    int n=v/7+2,step=v==32?26:(v*4+n*2+1)/(n*2-2)*2; var pos=new int[n]; pos[0]=6; for(int i=n-1,p=size-7;i>=1;i--,p-=step) pos[i]=p;
    for(int i=0;i<n;i++) for(int j=0;j<n;j++) {
     if((i==0&&j==0)||(i==0&&j==n-1)||(i==n-1&&j==0)) continue;
     for(int dy=-2;dy<=2;dy++) for(int dx=-2;dx<=2;dx++) Set(m,fn,pos[i]+dx,pos[j]+dy,Math.Max(Math.Abs(dx),Math.Abs(dy))!=1);
    }
   }
   DrawFormat(m,fn,0,size);
   if(v>=7) {
    int rem=v; for(int i=0;i<12;i++) rem=(rem<<1)^((rem>>11)*0x1F25); int bits=v<<12|rem;
    for(int i=0;i<18;i++) { bool bit=((bits>>i)&1)!=0; int a=size-11+i%3,b=i/3; Set(m,fn,a,b,bit); Set(m,fn,b,a,bit); }
   }
  }
  static void DrawFormat(bool[,] m,bool[,] fn,int mask,int size) {
   int data=0<<3|mask; // level M = 0b00
   int rem=data; for(int i=0;i<10;i++) rem=(rem<<1)^((rem>>9)*0x537); int bits=(data<<10|rem)^0x5412;
   Func<int,bool> bit=i=>((bits>>i)&1)!=0;
   for(int i=0;i<=5;i++) Set(m,fn,8,i,bit(i));
   Set(m,fn,8,7,bit(6)); Set(m,fn,8,8,bit(7)); Set(m,fn,7,8,bit(8));
   for(int i=9;i<15;i++) Set(m,fn,14-i,8,bit(i));
   for(int i=0;i<8;i++) Set(m,fn,size-1-i,8,bit(i));
   for(int i=8;i<15;i++) Set(m,fn,8,size-15+i,bit(i));
   Set(m,fn,8,size-8,true);
  }
  static void DrawCodewords(bool[,] m,bool[,] fn,byte[] data,int size) {
   int i=0;
   for(int right=size-1;right>=1;right-=2) {
    if(right==6) right=5;
    for(int vert=0;vert<size;vert++) for(int j=0;j<2;j++) {
     int x=right-j; bool up=((right+1)&2)==0; int y=up?size-1-vert:vert;
     if(!fn[y,x]&&i<data.Length*8) { m[y,x]=((data[i>>3]>>(7-(i&7)))&1)!=0; i++; }
    }
   }
  }
  static void ApplyMask(bool[,] m,bool[,] fn,int mask,int size) {
   for(int y=0;y<size;y++) for(int x=0;x<size;x++) {
    if(fn[y,x]) continue; bool invert;
    switch(mask) {
     case 0: invert=(x+y)%2==0; break; case 1: invert=y%2==0; break; case 2: invert=x%3==0; break; case 3: invert=(x+y)%3==0; break;
     case 4: invert=(x/3+y/2)%2==0; break; case 5: invert=x*y%2+x*y%3==0; break; case 6: invert=(x*y%2+x*y%3)%2==0; break; default: invert=((x+y)%2+x*y%3)%2==0; break;
    }
    if(invert) m[y,x]=!m[y,x];
   }
  }
  static long Penalty(bool[,] m,int size) {
   long p=0; int dark=0;
   for(int a=0;a<size;a++) {
    int runRow=1,runCol=1;
    for(int b=1;b<size;b++) {
     if(m[a,b]==m[a,b-1]) { runRow++; if(runRow==5) p+=3; else if(runRow>5) p++; } else runRow=1;
     if(m[b,a]==m[b-1,a]) { runCol++; if(runCol==5) p+=3; else if(runCol>5) p++; } else runCol=1;
    }
   }
   for(int y=0;y<size;y++) for(int x=0;x<size;x++) {
    if(m[y,x]) dark++;
    if(x<size-1&&y<size-1&&m[y,x]==m[y,x+1]&&m[y,x]==m[y+1,x]&&m[y,x]==m[y+1,x+1]) p+=3;
    if(x+6<size&&m[y,x]&&!m[y,x+1]&&m[y,x+2]&&m[y,x+3]&&m[y,x+4]&&!m[y,x+5]&&m[y,x+6]) p+=40;
    if(y+6<size&&m[y,x]&&!m[y+1,x]&&m[y+2,x]&&m[y+3,x]&&m[y+4,x]&&!m[y+5,x]&&m[y+6,x]) p+=40;
   }
   int total=size*size; p+=(Math.Abs(dark*20-total*10)+total-1)/total*10;
   return p;
  }
 }

 public static class AccountTests {
  static void Assert(bool b,string name) { if(!b) throw new Exception("FAIL: "+name); }
  public static void Run() {
   var saved=AccountConfig.Current; var savedAccount=AccountService.Current;
   try {
    var qr=QrCode.Encode("00020101021229370016A0000006770101110113006681234567853037645406690.005802TH63041064");
    Assert(qr.GetLength(0)==37&&qr[0,0]&&qr[6,6]&&!qr[7,7],"PromptPay QR is version 5 with finder patterns");
    var q=Loopback.ParseQuery("code=a%2Bb&x=1+2&error_description=Bad%20thing");
    Assert(q["code"]=="a+b"&&q["x"]=="1 2"&&q["error_description"]=="Bad thing","OAuth callback query parsing");
    var d=J.Obj("{\"ptv_id\":12345678,\"is_vip\":true,\"amount\":690.5,\"vip_until\":\"2030-01-02T03:04:05+00:00\"}");
    Assert(J.L(d,"ptv_id")==12345678&&J.B(d,"is_vip")&&J.M(d,"amount")==690.5m&&J.T(d,"vip_until").Value==new DateTime(2030,1,2,3,4,5,DateTimeKind.Utc),"JSON helpers");
    var free=new Game { id="a",access="free" }; var vip=new Game { id="b" };
    AccountConfig.Current=new AccountConfig(); Assert(AccountService.CanPlay(vip),"no account.json keeps every game open");
    AccountConfig.Current=new AccountConfig { supabaseUrl="https://example.supabase.co",anonKey="k" };
    AccountService.Current=null; Assert(!AccountService.CanPlay(free)&&!AccountService.CanPlay(vip),"signed out cannot play");
    AccountService.Current=new Account { role="user",checkedAt=DateTime.UtcNow }; Assert(AccountService.CanPlay(free)&&!AccountService.CanPlay(vip),"free game open, VIP game locked");
    AccountService.Current.vipUntil=DateTime.UtcNow.AddDays(3); Assert(AccountService.CanPlay(vip),"active VIP plays");
    AccountService.Current.vipUntil=DateTime.UtcNow.AddMinutes(-1); Assert(!AccountService.CanPlay(vip),"expired VIP locked");
    AccountService.Current.role="admin"; Assert(AccountService.CanPlay(vip),"admin plays everything");
   } finally { AccountConfig.Current=saved; AccountService.Current=savedAccount; }
  }
 }
}
