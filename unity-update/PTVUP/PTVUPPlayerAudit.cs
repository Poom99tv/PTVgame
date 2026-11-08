using System.Collections;
using UnityEngine;

/// <summary>Report real player startup completion, including the HUD built after all eight skills.</summary>
public sealed class PTVUPPlayerAudit : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { new GameObject("PTVUPPlayerAudit").AddComponent<PTVUPPlayerAudit>(); }
    IEnumerator Start()
    {
        yield return null;
        yield return new WaitForSecondsRealtime(.6f);
        var game = FindFirstObjectByType<ClimbGame.ClimbGame>();
        var hud = FindFirstObjectByType<ZHud>();
        int skills = 0;
        if (game) foreach (string name in new[]{"DuckStarSkill","BonkChoySkill","BalloonLiftSkill","FanGustSkill","CoconutCannonSkill","BrainBoostSkill","ChiliBombSkill","JetpackSkill"})
            if (game.GetComponent("ClimbGame."+name)) skills++;
        var canvas = GameObject.Find("PVZUP_SkillHUD");
        int columns = canvas ? canvas.transform.childCount : 0;
        int tiles = 0;
        if (canvas) foreach (var rt in canvas.GetComponentsInChildren<RectTransform>(true)) if (rt.name.StartsWith("Tile")) tiles++;
        var legacy = GameObject.Find("Skill1_DuckStar");
        bool ready = game && hud && skills == 8 && columns == 2 && tiles == 8 && !legacy;
        string message = "[PTVUP Player] version="+Application.version+" ready="+ready+" skills="+skills+" columns="+columns+" tiles="+tiles+" legacyActive="+(legacy!=null)+" shaderUnlit="+(Shader.Find("Universal Render Pipeline/Unlit")!=null);
        if (ready) Debug.Log(message); else Debug.LogError(message);
#if DEVELOPMENT_BUILD && PTV_RELEASE_QA
        // Local QA build only: exercise the same public skill paths without requiring live gifts or keyboard timing.
        yield return new WaitForSecondsRealtime(3);
        PTVLive.I.menu.BeginPlay();
        string folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../verification"));
        System.IO.Directory.CreateDirectory(folder);
        yield return new WaitForSecondsRealtime(1);
        ScreenCapture.CaptureScreenshot(folder+"/landscape.png");
        game.PrepareForSkill(); game.Height=500;
        yield return new WaitForSecondsRealtime(1);
        foreach(var effect in new[]{ZEffect.DuckStar,ZEffect.BonkChoy,ZEffect.Balloon,ZEffect.Fan,ZEffect.Coconut,ZEffect.Brain,ZEffect.Chili,ZEffect.Jetpack})
        {
            Debug.Log("[PTVUP QA] trigger="+effect+" height="+game.Height+" allowed="+game.CanUseSkill);
            game.TriggerSkill(effect,null);
            yield return new WaitForSecondsRealtime(.3f);
            float deadline=Time.realtimeSinceStartup+35;
            while(game.SkillBusy && Time.realtimeSinceStartup<deadline) yield return null;
            if(game.SkillBusy) { Debug.LogError("[PTVUP QA] TIMEOUT "+effect); yield break; }
            Debug.Log("[PTVUP QA] completed="+effect+" height="+game.Height);
            yield return new WaitForSecondsRealtime(.4f);
        }
        PTVScreenLayout.SetPortrait(true);
        yield return new WaitForSecondsRealtime(2);
        ScreenCapture.CaptureScreenshot(folder+"/portrait.png");
        Debug.Log("[PTVUP QA] ALL_EIGHT_SKILLS_COMPLETED");
#endif
        Destroy(gameObject);
    }
}
