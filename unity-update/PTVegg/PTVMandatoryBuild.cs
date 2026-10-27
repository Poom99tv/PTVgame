using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;
public sealed class PTVMandatoryBuildValidator : IPreprocessBuildWithReport,IProcessSceneWithReport {
 static bool firstScene;
 public int callbackOrder {get{return -10000;}}
 public void OnPreprocessBuild(BuildReport report){firstScene=true;var scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).ToArray();if(scenes.Length<2||!scenes[0].path.EndsWith("/PTVVersionGate.unity"))throw new BuildFailedException("PTVVersionGate must be the first enabled build scene. Run PTVMandatoryBuild.Run first.");Version v;if(!Version.TryParse(PlayerSettings.bundleVersion,out v))throw new BuildFailedException("A numeric bundle version is required for mandatory updates.");}
 public void OnProcessScene(Scene scene,BuildReport report){if(report==null||!firstScene)return;firstScene=false;EnsureFirstScene(scene.path);}
 public static void EnsureFirstScene(string path){if(!path.EndsWith("/PTVVersionGate.unity",StringComparison.Ordinal))throw new BuildFailedException("The actual build scene list must start with PTVVersionGate, including builds with overridden scene lists.");}
}
public static class PTVMandatoryBuild {
 static readonly string Root="C:/Users/Windows 11 Pro/PTVegg/Builds";
 public const string Version="1.0.0";
 static string Name{get{return "PTVegg";}}
 public static string Run(){Directory.CreateDirectory(Root);File.WriteAllText(Root+"/"+Name+"-build-status.txt","SCHEDULED");EditorApplication.delayCall+=Build;return "Build scheduled: "+Name;}
 static void Build(){try{
  File.WriteAllText(Root+"/"+Name+"-build-status.txt","BUILDING");EditorSceneManager.SaveOpenScenes();
  string path="Assets/Scenes/PTVVersionGate.unity";Directory.CreateDirectory(Path.GetDirectoryName(path));
  if(!File.Exists(path)){var bootstrap=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);EditorSceneManager.SaveScene(bootstrap,path);EditorSceneManager.CloseScene(bootstrap,true);}
  string[] entries=new[]{"Assets/Scenes/PTVegg_Map.unity"};
  var scenes=new[]{path}.Concat(entries).ToArray();EditorBuildSettings.scenes=scenes.Select(s=>new EditorBuildSettingsScene(s,true)).ToArray();
  PlayerSettings.bundleVersion=Version;PlayerSettings.productName="PTVegg";PlayerSettings.runInBackground=true;PlayerSettings.resizableWindow=true;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.defaultScreenWidth=1280;PlayerSettings.defaultScreenHeight=720;
  PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);

  AssetDatabase.SaveAssets();string folder=Root+"/"+Name+"-"+Version+"/"+Name;Directory.CreateDirectory(folder);   // ZIP the version folder -> PTVegg/PTVegg.exe
  var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=scenes,locationPathName=folder+"/"+Name+".exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.CompressWithLz4HC});
  File.WriteAllText(Root+"/"+Name+"-build-status.txt",report.summary.result+"\nErrors="+report.summary.totalErrors+"\nBytes="+report.summary.totalSize+"\nDuration="+report.summary.totalTime);
 }catch(Exception e){File.WriteAllText(Root+"/"+Name+"-build-status.txt","FAILED\n"+e);Debug.LogException(e);}}
 public static string TestPolicy(){if(!PTVVersionGate.SameVersion("1.0.0","1.0.0.0"))throw new Exception("Normalization failed");if(PTVVersionGate.Evaluate("0.9.0","1.0.0",true)!="update_required")throw new Exception("Old version not blocked");if(PTVVersionGate.Evaluate("1.0.0","1.0.0",false)!="connection_failed")throw new Exception("Offline not blocked");if(PTVVersionGate.Evaluate("1.0.0","oops",true)!="invalid_manifest")throw new Exception("Invalid manifest not blocked");if(PTVVersionGate.Evaluate("1.0.0","1.0.0",true)!="allowed")throw new Exception("Latest version not allowed");return "PASS: latest allowed; stale, offline, malformed versions blocked; normalization correct";}
}




public sealed class PTVeggBridgeBuild:UnityEditor.Build.IPostprocessBuildWithReport {
 public int callbackOrder{get{return 50;}}
 public void OnPostprocessBuild(UnityEditor.Build.Reporting.BuildReport report){string source=System.IO.Path.GetFullPath("TikTokBridge");if(!System.IO.Directory.Exists(source))throw new UnityEditor.Build.BuildFailedException("TikTokBridge missing");Copy(source,System.IO.Path.Combine(System.IO.Path.GetDirectoryName(report.summary.outputPath),"TikTokBridge"));}
 static void Copy(string source,string target){System.IO.Directory.CreateDirectory(target);foreach(var f in System.IO.Directory.GetFiles(source))System.IO.File.Copy(f,System.IO.Path.Combine(target,System.IO.Path.GetFileName(f)),true);foreach(var d in System.IO.Directory.GetDirectories(source))Copy(d,System.IO.Path.Combine(target,System.IO.Path.GetFileName(d)));}
}
