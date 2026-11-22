using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
public sealed class PTVProfileCache : MonoBehaviour
{
    static PTVProfileCache instance;
    public static PTVProfileCache Existing => instance;
    readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
    readonly Dictionary<Texture, int> uses = new Dictionary<Texture, int>();
    readonly HashSet<string> downloading = new HashSet<string>();
    public static PTVProfileCache Get()
    {
        if (!instance) { var go = new GameObject("PTV_ProfileCache"); DontDestroyOnLoad(go); instance = go.AddComponent<PTVProfileCache>(); }
        return instance;
    }
    public void Retain(Texture t) { uses.TryGetValue(t, out int c); uses[t] = c + 1; }
    public void Release(Texture t) { if (uses.TryGetValue(t, out int c)) { if (c <= 1) uses.Remove(t); else uses[t] = c - 1; } Trim(); }
    void Trim()
    {
        if (cache.Count <= 128) return;
        string victim = null;
        foreach (var p in cache) if (!uses.ContainsKey(p.Value)) { victim = p.Key; break; }
        if (victim != null) { var t = cache[victim]; cache.Remove(victim); Destroy(t); }
    }
    public IEnumerator Fetch(List<string> urls, System.Action<Texture> ready)
    {
        foreach (string url in urls)
        {
            if (!System.Uri.TryCreate(url, System.UriKind.Absolute, out var uri) || !(uri.Scheme == "https" || uri.IsLoopback)) continue;
            while (downloading.Contains(url)) yield return null;
            if (cache.TryGetValue(url, out var saved) && saved) { ready(saved); yield break; }
            while (downloading.Count >= 4 || downloading.Contains(url)) yield return null;
            if (cache.TryGetValue(url, out saved) && saved) { ready(saved); yield break; }
            downloading.Add(url);
            try
            {
                using (var request = UnityWebRequestTexture.GetTexture(url, true))
                {
                    request.timeout = 5; request.SetRequestHeader("User-Agent", "Mozilla/5.0");
                    var op = request.SendWebRequest();
                    while (!op.isDone) { if (request.downloadedBytes > 4194304) { request.Abort(); break; } yield return null; }
                    if (request.isNetworkError || request.isHttpError || request.downloadedBytes > 4194304) continue;
                    var texture = DownloadHandlerTexture.GetContent(request); if (!texture) continue;
                    if (texture.width > 2048 || texture.height > 2048) { Destroy(texture); continue; }
                    if (texture.width > 256 || texture.height > 256)
                    {
                        float f = 256f / Mathf.Max(texture.width, texture.height);
                        int w = Mathf.Max(1, Mathf.RoundToInt(texture.width * f)), h = Mathf.Max(1, Mathf.RoundToInt(texture.height * f));
                        var rt = RenderTexture.GetTemporary(w, h, 0); var prev = RenderTexture.active; Graphics.Blit(texture, rt); RenderTexture.active = rt;
                        var small = new Texture2D(w, h, TextureFormat.RGB24, false); small.ReadPixels(new Rect(0, 0, w, h), 0, 0); small.Apply(false, true);
                        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); Destroy(texture); texture = small;
                    }
                    texture.wrapMode = TextureWrapMode.Clamp;
                    cache[url] = texture; ready(texture); Trim(); yield break;
                }
            }
            finally { downloading.Remove(url); }
        }
        ready(null);
    }
    void OnDestroy() { foreach (var t in cache.Values) if (t) Destroy(t); if (instance == this) instance = null; }
}

