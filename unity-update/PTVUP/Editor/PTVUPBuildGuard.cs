using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Keep runtime-only shader and collision mesh dependencies in every player build.</summary>
public sealed class PTVUPBuildGuard : IPreprocessBuildWithReport
{
    public int callbackOrder { get { return -1000; } }
    public void OnPreprocessBuild(BuildReport report) { Prepare(); }

    [MenuItem("ClimbGame/Validate Player Dependencies")]
    public static void Prepare()
    {
        // Other runtime shaders already have scene/Resources references or are always included.
        // Bonk Choy and chili create Unlit materials only from code; player stripping otherwise removes it.
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (!shader) throw new BuildFailedException("Required URP Unlit shader is missing");
        var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
        var included = settings.FindProperty("m_AlwaysIncludedShaders");
        bool found = false;
        for (int i = 0; i < included.arraySize; i++) if (included.GetArrayElementAtIndex(i).objectReferenceValue == shader) found = true;
        if (!found) { included.InsertArrayElementAtIndex(included.arraySize); included.GetArrayElementAtIndex(included.arraySize - 1).objectReferenceValue = shader; settings.ApplyModifiedPropertiesWithoutUndo(); }

        var paths = new HashSet<string>();
        var game = UnityEngine.Object.FindFirstObjectByType<ClimbGame.ClimbGame>();
        if (!game) throw new BuildFailedException("Open the current BeanstalkClimb scene before building PTVUP");
        var models = new List<GameObject>(game.coreModules); models.Add(game.rootBase);
        var balloon = Resources.Load<GameObject>("BalloonLift/BalloonOnly"); if (balloon) models.Add(balloon);
        foreach (var model in models)
            if (model) foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh) paths.Add(AssetDatabase.GetAssetPath(filter.sharedMesh));
        foreach (var path in paths)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer && !importer.isReadable) { importer.isReadable = true; importer.SaveAndReimport(); }
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[PTVUP Build] runtime Unlit shader retained; runtime collision/balloon meshes readable: " + paths.Count);
    }
}
