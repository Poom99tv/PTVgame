#if DEVELOPMENT_BUILD && PTV_RELEASE_QA
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
public sealed class PTVReleaseQA:MonoBehaviour {
 [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] static void Boot(){if(SceneManager.GetActiveScene().name!="PTVVersionGate")return;foreach(var device in UnityEngine.InputSystem.InputSystem.devices.ToArray())UnityEngine.InputSystem.InputSystem.DisableDevice(device);var o=new GameObject("Release QA");DontDestroyOnLoad(o);o.AddComponent<PTVReleaseQA>();}
 IEnumerator Start(){
  Application.runInBackground=true;
  yield return SceneManager.LoadSceneAsync("SquidStall");yield return new WaitForSeconds(2);
  var g=UnityEngine.Object.FindFirstObjectByType<SquidGame>();
  typeof(PTVStartMenu).GetMethod("BeginTrial",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(PTVLive.I.menu,null);
  Call(g,"ClickIceBox");Call(g,"ClickIngredients");yield return new WaitForSeconds(1.1f);
  for(int i=0;i<6;i++)Call(g,"ClickMortar");for(int i=0;i<3;i++)Call(g,"ClickMortar");
  yield return new WaitForSeconds(g.sideTime+.1f);Call(g,"ClickSquid",0);
  yield return new WaitForSeconds(g.sideTime+1f);Call(g,"ClickSquid",0);yield return new WaitForSeconds(1f);
  bool gameplay=g.Squid==1&&g.Sauce==3;
  var hud=SquidSkillHud.I;
  bool tiles=hud&&hud.GetComponentsInChildren<UnityEngine.UI.Image>(true).Count(i=>i.name.StartsWith("Skill"))==13;
  bool english=PTVLang.Current=="en"&&!UnityEngine.Object.FindObjectsByType<PTVText>(FindObjectsInactive.Exclude,FindObjectsSortMode.None).Any(t=>t.translate&&System.Text.RegularExpressions.Regex.IsMatch(t.text,"[ก-๿]"));
  var args=Environment.GetCommandLineArgs();string capture="C:/Users/Windows 11 Pro/Documents/Codex/2026-11-10/new-chat/outputs/skills-13/player-qa.png";for(int i=0;i+1<args.Length;i++)if(args[i]=="--qa-capture")capture=args[i+1];ScreenCapture.CaptureScreenshot(capture);
  var menu=PTVLive.I.menu; var director=PTVLive.I.director;
  menu.OpenSettings(); menu.OpenGiftPicker(0); menu.HandleEscape();
  bool escapePicker=menu.SettingsOpen&&!((GameObject)typeof(PTVStartMenu).GetField("picker",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(menu)).activeSelf;
  hud.OpenSettings();menu.HandleEscape();bool escapeHud=menu.SettingsOpen&&!hud.SettingsOpen;
  menu.HandleEscape();bool escapeSettings=!menu.SettingsOpen&&!menu.StartScreenOpen&&Time.timeScale==1;
  int squidBefore=g.Squid,sauceBefore=g.Sauce;
  menu.HandleEscape();yield return new WaitForSecondsRealtime(.25f);
  bool escapeHome=menu.StartScreenOpen&&g.IsWaiting&&!PTVStartMenu.SessionStarted&&PTVStartMenu.UIBlocking&&Time.timeScale==0&&g.Squid==squidBefore&&g.Sauce==sauceBefore;
  menu.HandleEscape();bool escapeHomeAgain=menu.StartScreenOpen&&Time.timeScale==0;
  director.Enqueue(new PTVTikTokLive.Message{@event="gift",giftName=director.settings.slots[0].gift,repeatCount=1,userId="qa",uniqueId="qa",nickname="QA"});
  int queued=director.events.Count;yield return new WaitForSecondsRealtime(.25f);
  bool queuedWhileHome=queued>0&&director.events.Count==queued;
  menu.BeginPlay();yield return null;yield return null;
  bool escapeResume=!menu.StartScreenOpen&&!g.IsWaiting&&PTVStartMenu.SessionStarted&&!PTVStartMenu.UIBlocking&&Time.timeScale==1&&director.events.Count==0;
  bool escape=escapePicker&&escapeHud&&escapeSettings&&escapeHome&&escapeHomeAgain&&queuedWhileHome&&escapeResume;
  Debug.Log("PTV_ESCAPE_QA "+(escape?"PASS":"FAIL")+" picker="+escapePicker+" hud="+escapeHud+" settings="+escapeSettings+" home="+escapeHome+" repeated="+escapeHomeAgain+" queued="+queuedWhileHome+" resume="+escapeResume);
  // A fresh stall isolates customer tests from helpers produced by the earlier gift test.
  yield return SceneManager.LoadSceneAsync("SquidStall"); yield return new WaitForSeconds(.5f);
  g=UnityEngine.Object.FindFirstObjectByType<SquidGame>();
  var a=new PTVTikTokLive.Message{userId="order-a",uniqueId="viewer_a",nickname="Viewer A"};
  var b=new PTVTikTokLive.Message{userId="order-b",uniqueId="viewer_b",nickname="Viewer B"};
  Call(g,"AddCustomer",a,2);Call(g,"AddCustomer",b,2);Call(g,"AddCustomer",new PTVTikTokLive.Message{userId="order-a",uniqueId="viewer_a",nickname="Viewer A"},2);
  Call(g,"AddCustomer",new PTVTikTokLive.Message{uniqueId="VIEWER_A",nickname="Viewer A"},1);
  var people=(System.Collections.IList)typeof(SquidGame).GetField("customers",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(g);
  object first=people[0],second=people[1];
  bool merged=people.Count==2&&Number(first,"needSquid")==5&&Number(first,"needSauce")==5&&Number(second,"needSquid")==2;
  bool totals=g.PendingOrderSets==7&&g.PendingSquid==7&&g.PendingSauce==7&&g.WaitingCustomerCount==2;
  menu.HandleEscape();yield return new WaitForSecondsRealtime(.2f);bool orderPause=g.IsWaiting&&g.PendingOrderSets==7&&g.WaitingCustomerCount==2;menu.BeginPlay();
  yield return new WaitForSeconds(2.5f);
  var totalText=g.GetComponentsInChildren<UnityEngine.UI.Text>(true).Single(t=>t.name=="Owed");
  var itemsText=g.GetComponentsInChildren<UnityEngine.UI.Text>(true).Single(t=>t.name=="OrderItems");
  bool totalsUI=totalText.text.Contains("7")&&totalText.text.Contains("2")&&itemsText.text.Contains("7")&&!System.Text.RegularExpressions.Regex.IsMatch(totalText.text+itemsText.text,"[ก-๿]");
  ScreenCapture.CaptureScreenshot(capture.Replace(".png","-orders.png")); yield return null;
  PreviewLanguage(g,"th");yield return new WaitForSeconds(.25f);
  bool thaiUI=totalText.text.Contains("ออร์เดอร์ทั้งหมด")&&itemsText.text.Contains("ปลาหมึก")&&itemsText.text.Contains("น้ำจิ้ม");
  ScreenCapture.CaptureScreenshot(capture.Replace(".png","-orders-th.png"));yield return null;
  PreviewLanguage(g,"en");yield return null;
  Call(g,"Produce",true,7,Vector3.zero,false);yield return new WaitForSeconds(4);
  bool fifoShortage=Number(first,"needSquid")==0&&Number(first,"needSauce")==5&&Number(second,"needSquid")==2&&Number(second,"needSauce")==2&&g.Squid==2;
  Call(g,"Produce",false,7,Vector3.zero,false);
  float orderDeadline=Time.realtimeSinceStartup+8;
  while(!(bool)Field(first,"leaving")&&Time.realtimeSinceStartup<orderDeadline)yield return null;
  bool waitsForDelivery=Number(first,"deliveries")==0;
  Call(g,"AddCustomer",a,1);
  bool reused=people.Count==2&&object.ReferenceEquals(people[people.Count-1],first)&&!(bool)Field(first,"leaving");
  yield return new WaitForSeconds(4);
  bool rejoinFIFO=Number(second,"needSquid")==0&&Number(second,"needSauce")==0&&Number(first,"needSquid")==1&&Number(first,"needSauce")==1&&g.Squid==0&&g.Sauce==0;
  Call(g,"Produce",true,1,Vector3.zero,false);Call(g,"Produce",false,1,Vector3.zero,false);yield return new WaitForSeconds(4);
  bool served=g.PendingOrderSets==0&&g.PendingSquid==0&&g.PendingSauce==0&&g.WaitingCustomerCount==0&&g.Squid==0&&g.Sauce==0;
  bool noStuckFood=UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).All(t=>t.name!="Flyer");
  for(int i=0;i<12;i++)Call(g,"AddCustomer",new PTVTikTokLive.Message{userId="overflow-"+i,uniqueId="queue_"+i,nickname="Queue "+i},1);
  yield return null;
  bool overflow=g.WaitingCustomerCount==12&&g.PendingOrderSets==12;
  int index=0;foreach(object person in people)if(!(bool)Field(person,"leaving")){overflow&=Number(person,"spot")== (index<8?index:-1);index++;}
  Call(g,"Apply",new SquidGiftDirector.StallEvent{action=SquidAction.ServeAll,sender=a});yield return null;
  bool allOrders=g.PendingOrderSets==0&&g.WaitingCustomerCount==0;
  bool orders=merged&&totals&&orderPause&&totalsUI&&thaiUI&&fifoShortage&&waitsForDelivery&&reused&&rejoinFIFO&&served&&noStuckFood&&overflow&&allOrders;
  Debug.Log("PTV_ORDERS_QA "+(orders?"PASS":"FAIL")+" merged="+merged+" totals="+totals+" totalsUI="+totalsUI+" thaiUI="+thaiUI+" pause="+orderPause+" fifoShortage="+fifoShortage+" waitsForDelivery="+waitsForDelivery+" reuse="+reused+" rejoinFIFO="+rejoinFIFO+" served="+served+" noStuckFood="+noStuckFood+" overflow="+overflow+" serveAll="+allOrders);
  var originalSlots=director.settings.slots;director.settings.slots=SquidGiftDirector.Settings.Defaults();
  foreach(var slot in director.settings.slots){slot.rule.gift=false;slot.rule.follow=false;slot.rule.likes=false;slot.rule.orangeHeart=false;}
  director.settings.slots[0].gift="Baby Hippo";director.settings.slots[0].rule.gift=true;
  director.settings.slots[6].gift="Friendship Necklace";director.settings.slots[6].rule.gift=true;
  bool hippoAlias=GiftCheck(director,"little hippo",null,0,1,"alias-hippo");
  bool necklaceAlias=GiftCheck(director,"BFF Necklace",null,6,3,"alias-necklace");
  bool hippoImage=GiftCheck(director,"unrelated localized name","Baby Hippo",0,1,"image-hippo");
  bool necklaceImage=GiftCheck(director,"Rose","Friendship Necklace",6,3,"image-necklace");
  var unmatched=new PTVTikTokLive.Message{@event="gift",eventId="unknown-gift",userId="gift-qa",uniqueId="gift_qa",giftName="unknown picture",giftId="989999",repeatCount=1,repeatEnd=true};
  bool unknownRejected=!PTVLive.I.live.AcceptJson(JsonUtility.ToJson(unmatched))&&director.events.Count==0;
  director.settings.slots=originalSlots;
  bool identity=hippoAlias&&necklaceAlias&&hippoImage&&necklaceImage&&unknownRejected;
  Debug.Log("PTV_GIFT_IDENTITY_QA "+(identity?"PASS":"FAIL")+" littleHippoAlias="+hippoAlias+" bffNecklaceAlias="+necklaceAlias+" hippoPictureCanonical="+hippoImage+" necklacePictureOverridesWrongName="+necklaceImage+" unknownRejected="+unknownRejected);
  int completed=0;bool ranges=true;
  g.WheelResolved+=(a,n,s)=>{completed++;var config=director.settings.slots[s];ranges&=n==config.min&&n==config.max&&(a==SquidAction.HelpSet||a==SquidAction.Customer);};
  director.Enqueue(new PTVTikTokLive.Message{@event="gift",giftName=director.settings.slots[10].gift,repeatCount=7,userId="qa",uniqueId="qa",nickname="QA"});
  Time.timeScale=20;float deadline=Time.realtimeSinceStartup+20;
  while(completed<7&&Time.realtimeSinceStartup<deadline)yield return null;
  Time.timeScale=1;
  bool ok=gameplay&&tiles&&english&&escape&&orders&&identity&&completed==7&&ranges&&PTVLicense.Allowed;
  Debug.Log("PTV_RELEASE_QA "+(ok?"PASS":"FAIL")+" gameplay="+gameplay+" skillTiles="+tiles+" english="+english+" wheelSpins="+completed+" wheelRanges="+ranges+" screen="+Screen.width+"x"+Screen.height);
  yield return new WaitForSeconds(1);Application.Quit(ok?0:2);
 }
 void Call(SquidGame g,string name,params object[] args){typeof(SquidGame).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(g,args);}
 bool GiftCheck(SquidGiftDirector director,string raw,string canonical,int slot,int amount,string id){
  director.events.Clear();
  var gift=new PTVTikTokLive.Message{@event="gift",eventId=id,userId="gift-qa",uniqueId="gift_qa",nickname="Gift QA",giftName=raw,giftCanonicalName=canonical,giftMatchMethod=canonical==null?"name":"image-exact",giftImageUrl="https://p19.tiktokcdn.com/test.png",giftId=id.Contains("hippo")?"123456":"123457",repeatCount=1,repeatEnd=true};
  bool accepted=PTVLive.I.live.AcceptJson(JsonUtility.ToJson(gift));
  if(!accepted||director.events.Count!=1){director.events.Clear();return false;}
  var action=director.events.Dequeue();return action.slot==slot&&action.amount==amount;
 }
 object Field(object target,string name){return target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public).GetValue(target);}
 int Number(object target,string name){return (int)Field(target,name);}
 void PreviewLanguage(SquidGame g,string language){
  // QA does not modify the user's saved settings; CI may have read-only preferences.
  typeof(PTVLang).GetField("lang",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,language);
  var reapply=typeof(PTVText).GetMethod("Reapply",BindingFlags.Instance|BindingFlags.NonPublic);
  foreach(var text in UnityEngine.Object.FindObjectsByType<PTVText>(FindObjectsInactive.Include,FindObjectsSortMode.None))reapply.Invoke(text,null);
  Call(g,"RefreshSignText");
 }
}
#endif
