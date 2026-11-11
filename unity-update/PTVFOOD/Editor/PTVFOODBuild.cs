using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class PTVFOODBuild : IPreprocessBuildWithReport
{
    public const string Gate = "Assets/PTVFOOD/Scenes/PTVVersionGate.unity";
    public const string Game = "Assets/PTVFOOD/Scenes/SquidStall.unity";
    public int callbackOrder => -1000;
    public void OnPreprocessBuild(BuildReport report)
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).ToArray();
        if (scenes.Length != 2 || scenes[0].path != Gate || scenes[1].path != Game)
            throw new BuildFailedException("PTVFOOD requires the version gate before SquidStall.");
        if (!report.summary.options.HasFlag(BuildOptions.Development) && PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone).Split(';').Contains("PTV_RELEASE_QA"))
            throw new BuildFailedException("Remove PTV_RELEASE_QA before a retail build.");
        PrepareDependencies();
    }
    public static void Prepare()
    {
        PlayerSettings.productName = "Grilled Squid";
        PlayerSettings.companyName = "PTVgame";
        PlayerSettings.bundleVersion = "1.0.4";
        var appIcon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/PTVFOOD/Branding/AppIcon.png");
        if (!appIcon) throw new BuildFailedException("Missing application icon.");
        var iconSizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Standalone, IconKind.Any);
        PlayerSettings.SetIcons(NamedBuildTarget.Standalone, iconSizes.Select(size => appIcon).ToArray(), IconKind.Any);
        PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { appIcon }, IconKind.Any);
        if (PlayerSettings.GetIcons(NamedBuildTarget.Standalone, IconKind.Any).Any(icon => icon != appIcon))
            throw new BuildFailedException("Application icon must be assigned at every Windows size.");
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone,"com.ptvgame.ptvfood");
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
        PlayerSettings.defaultScreenWidth = 1280; PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = true; PlayerSettings.runInBackground = true;
        if (!File.Exists(Gate)) {
            var active = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,NewSceneMode.Additive);
            EditorSceneManager.SaveScene(scene,Gate);
            EditorSceneManager.CloseScene(scene,true);
            SceneManager.SetActiveScene(active);
        }
        EditorBuildSettings.scenes = new [] {new EditorBuildSettingsScene(Gate,true),new EditorBuildSettingsScene(Game,true)};
        PrepareDependencies(); AssetDatabase.SaveAssets();
    }
    static void PrepareDependencies()
    {
        var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
        var shaders = settings.FindProperty("m_AlwaysIncludedShaders");
        const string folder="Assets/PTVFOOD/Resources/ReleaseShaders";
        if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets/PTVFOOD/Resources","ReleaseShaders");
        foreach (var name in new[]{"Universal Render Pipeline/Lit","Universal Render Pipeline/Particles/Unlit"}) {
            var shader=Shader.Find(name);if(!shader)throw new BuildFailedException("Missing shader: "+name);
            for(int i=shaders.arraySize-1;i>=0;i--)if(shaders.GetArrayElementAtIndex(i).objectReferenceValue==shader){shaders.GetArrayElementAtIndex(i).objectReferenceValue=null;shaders.DeleteArrayElementAtIndex(i);}
        }
        // Material references preserve only the variants the game actually creates.
        foreach(var key in new[]{"Smoke","CoalHot","Cream"}) {
            string target=folder+"/"+key+".mat";
            if(!AssetDatabase.LoadAssetAtPath<Material>(target)) {
                var source=AssetDatabase.LoadAssetAtPath<Material>("Assets/PTVFOOD/Materials/"+key+".mat");
                if(!source)throw new BuildFailedException("Missing retained material: "+key);
                AssetDatabase.CreateAsset(new Material(source),target);
            }
        }
        settings.ApplyModifiedPropertiesWithoutUndo();
        foreach(var guid in AssetDatabase.FindAssets("t:Model",new[]{"Assets/PTVFOOD"})) {
            var importer=AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as ModelImporter;
            if(importer&&!importer.isReadable){importer.isReadable=true;importer.SaveAndReimport();}
        }
    }
}
