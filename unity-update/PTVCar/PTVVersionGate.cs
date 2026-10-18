using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using TMPro;

// Installed in the bootstrap scene before any gameplay scene is loaded.
// This is a client-side update gate, not anti-tamper or server authentication.
[DefaultExecutionOrder(-32000)]
public sealed class PTVVersionGate : MonoBehaviour {
 public const string GameName="PTVCar";
 public const string ManifestUrl="https://raw.githubusercontent.com/Poom99tv/PTVgame/main/release-manifests/ptvcar.json";
 public const string EntryScene="PTVCarRace";
 public const string BootstrapScene="PTVVersionGate";
 public const string LauncherPage="https://github.com/Poom99tv/PTVgame/releases/latest";
 static PTVVersionGate instance;
 public static bool Authorized {get;private set;}
 public string State {get;private set;}
 public string Detail {get;private set;}
 public string RemoteVersion {get;private set;}
 GameObject overlay,eventSystem; TextMeshProUGUI title,body; bool checking,gameplayLoaded,unloading;float previousScale=1;
 [Serializable] public class RemoteManifest {public string version;}
 [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics(){instance=null;Authorized=false;}
 [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)] static void Bootstrap(){if(!instance)new GameObject("PTV Mandatory Version Gate").AddComponent<PTVVersionGate>();}
 void Awake(){if(instance&&instance!=this){Destroy(gameObject);return;}instance=this;DontDestroyOnLoad(gameObject);Authorized=false;State="checking";Time.timeScale=0;AudioListener.pause=true;}
 IEnumerator Start(){yield return Check();while(true){yield return new WaitForSecondsRealtime(60);if(!checking)yield return Check();}}
 void OnApplicationFocus(bool focused){if(focused&&gameplayLoaded&&!checking)StartCoroutine(Check());}
 void LateUpdate(){if(!Authorized){Time.timeScale=0;AudioListener.pause=true;}}
 void OnDestroy(){if(instance==this){instance=null;Authorized=false;}}
 public static bool SameVersion(string local,string remote){Version a,b;if(!Version.TryParse(local,out a)||!Version.TryParse(remote,out b))return false;return a.Major==b.Major&&a.Minor==b.Minor&&Math.Max(a.Build,0)==Math.Max(b.Build,0)&&Math.Max(a.Revision,0)==Math.Max(b.Revision,0);}
 public static string Evaluate(string local,string remote,bool requestSucceeded){if(!requestSucceeded)return "connection_failed";Version parsed;if(!Version.TryParse(local,out parsed)||!Version.TryParse(remote,out parsed))return "invalid_manifest";return SameVersion(local,remote)?"allowed":"update_required";}
 public IEnumerator Check(){
  if(checking)yield break;checking=true;bool wasAuthorized=Authorized;
  if(!Authorized)State="checking";
  string url=ManifestUrl+"?check="+Guid.NewGuid().ToString("N");
  using(var request=UnityWebRequest.Get(url)){
   request.timeout=15;request.SetRequestHeader("Cache-Control","no-cache");
   yield return request.SendWebRequest();
   bool ok=request.result==UnityWebRequest.Result.Success&&request.responseCode==200;
   string remote=null;
   if(ok){try{if(request.downloadHandler.data.Length>65536)ok=false;else remote=JsonUtility.FromJson<RemoteManifest>(request.downloadHandler.text.TrimStart('\uFEFF'))?.version;}catch{remote=null;}}
   RemoteVersion=remote;State=Evaluate(Application.version,remote,ok);
   Debug.Log("PTV_GATE "+State+" game="+GameName+" local="+Application.version+" remote="+(remote??"unavailable"));
   if(State=="allowed"){
    Authorized=true;Detail="Version verified";if(overlay)overlay.SetActive(false);if(eventSystem)eventSystem.SetActive(false);ApplyVerifiedTiming(wasAuthorized,previousScale);
    if(!gameplayLoaded){while(unloading)yield return null;gameplayLoaded=true;var load=SceneManager.LoadSceneAsync(EntryScene,LoadSceneMode.Single);if(load!=null)yield return load;}
   }else{
    string detail=State=="update_required"?"Installed: "+Application.version+"    Required: "+remote+"\nUpdate this game in PTVgame Launcher before playing.":State=="invalid_manifest"?"The update server returned an invalid version.\nPlaying is blocked until verification succeeds.":"Cannot verify the game version.\nCheck your internet connection, then retry.";
    Block(State=="update_required"?"UPDATE REQUIRED":"VERIFICATION REQUIRED",detail);
    if(gameplayLoaded&&!unloading){unloading=true;gameplayLoaded=false;var load=SceneManager.LoadSceneAsync(BootstrapScene,LoadSceneMode.Single);if(load!=null)yield return load;unloading=false;}
   }
  }
  checking=false;
 }
 void Block(string heading,string detail){if(!overlay)BuildUI();if(Authorized)previousScale=Time.timeScale>0?Time.timeScale:1;Authorized=false;Detail=detail;Time.timeScale=0;AudioListener.pause=true;if(overlay){overlay.SetActive(true);title.text=heading;body.text=GameName+"  •  v"+Application.version+"\n\n"+detail;}if(eventSystem)eventSystem.SetActive(true);}
 void BuildUI(){
  overlay=new GameObject("Version Gate Screen",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler),typeof(UnityEngine.UI.GraphicRaycaster));overlay.transform.SetParent(transform,false);
  var canvas=overlay.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=32767;
  var scaler=overlay.GetComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);scaler.matchWidthOrHeight=.5f;
  var background=new GameObject("Input Blocker",typeof(RectTransform),typeof(UnityEngine.UI.Image));background.transform.SetParent(overlay.transform,false);var bg=background.GetComponent<RectTransform>();bg.anchorMin=Vector2.zero;bg.anchorMax=Vector2.one;bg.offsetMin=bg.offsetMax=Vector2.zero;background.GetComponent<UnityEngine.UI.Image>().color=new Color(.015f,.045f,.085f,1);
  Label("PTVgame",new Vector2(0,225),new Vector2(950,64),42,new Color(.2f,.65f,1));
  title=Label("UPDATE REQUIRED",new Vector2(0,125),new Vector2(1000,72),42,Color.white);
  body=Label("",new Vector2(0,5),new Vector2(1000,180),24,new Color(.7f,.82f,.94f));
  Button("RETRY",new Vector2(-290,-165),()=>{if(!checking)StartCoroutine(Check());});
  Button("GET LAUNCHER / UPDATE",new Vector2(0,-165),()=>Application.OpenURL(LauncherPage));
  Button("QUIT",new Vector2(290,-165),()=>Application.Quit());
  Label("The latest version must be verified before gameplay can start.",new Vector2(0,-260),new Vector2(1100,50),19,new Color(.45f,.6f,.75f));
  eventSystem=new GameObject("Version Gate EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));eventSystem.transform.SetParent(transform,false);
 }
 public static void ApplyVerifiedTiming(bool alreadyAuthorized,float previous){if(alreadyAuthorized)return;Time.timeScale=previous>0?previous:1;AudioListener.pause=false;}
 TextMeshProUGUI Label(string text,Vector2 position,Vector2 size,int fontSize,Color color){var go=new GameObject("Gate Label",typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(overlay.transform,false);var rect=go.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.anchoredPosition=position;rect.sizeDelta=size;var label=go.GetComponent<TextMeshProUGUI>();label.text=text;label.fontSize=fontSize;label.alignment=TextAlignmentOptions.Center;label.color=color;label.raycastTarget=false;label.enableAutoSizing=true;label.fontSizeMin=16;label.fontSizeMax=fontSize;return label;}
 void Button(string label,Vector2 position,Action action){var go=new GameObject(label,typeof(RectTransform),typeof(UnityEngine.UI.Image),typeof(UnityEngine.UI.Button));go.transform.SetParent(overlay.transform,false);var rect=go.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.anchoredPosition=position;rect.sizeDelta=new Vector2(265,62);go.GetComponent<UnityEngine.UI.Image>().color=new Color(.06f,.28f,.52f);go.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(()=>action());var text=Label(label,Vector2.zero,new Vector2(250,54),18,Color.white);text.transform.SetParent(go.transform,false);text.rectTransform.anchoredPosition=Vector2.zero;}
}


