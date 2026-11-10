using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Food-stall sound effects, synthesized at start-up (no audio files):
///   sizzle  - "ฉ่า!" when a fresh squid hits the hot grill (hiss + crackles)
///   fry     - soft frying loop while squid are on the grill (louder with more squid)
///   pound   - wooden mortar thump        stir  - wet squish of the paste
///   plop    - ingredient drops in        scoop - spoon into a cup
///   serve   - plate clink                cash  - coins when a customer is served
///   bell    - shop bell when a customer / helper arrives
/// Follows PTVAudio's mute (M) and master volume.
/// </summary>
public sealed class SquidSfx : MonoBehaviour
{
    const int SR = 44100;
    static SquidSfx inst;
    readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    readonly Dictionary<string, float> last = new Dictionary<string, float>();
    AudioSource[] pool; int next; AudioSource fry;
    float fryTarget;
    System.Random rng = new System.Random(11);

    static SquidSfx I
    {
        get
        {
            if (!inst) { var go = new GameObject("PTV_SquidSfx"); DontDestroyOnLoad(go); inst = go.AddComponent<SquidSfx>(); }
            return inst;
        }
    }

    public static void Play(string name, float volume = 1f, float pitch = 1f, float minGap = 0.03f) { if (Application.isPlaying) I.PlayClip(name, volume, pitch, minGap); }
    /// <summary>Frying loop level 0..1 (0 = silent).</summary>
    public static void SetFry(float level) { if (Application.isPlaying) I.fryTarget = Mathf.Clamp01(level); }

    static float Master { get { var a = PTVAudio.I; return a && a.muted ? 0f : a ? a.master : 1f; } }

    void Awake()
    {
        pool = new AudioSource[16];
        for (int i = 0; i < pool.Length; i++) pool[i] = NewSource(false);
        fry = NewSource(true);
        Build();
        fry.clip = clips["fry"]; fry.volume = 0f; fry.Play();
    }

    AudioSource NewSource(bool loop)
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false; s.loop = loop; s.spatialBlend = 0f; s.ignoreListenerPause = true;
        return s;
    }

    void Update()
    {
        fry.volume = Mathf.MoveTowards(fry.volume, fryTarget * 0.45f * Master, Time.unscaledDeltaTime * 0.8f);
        fry.pitch = 0.95f + 0.1f * fryTarget;
    }

    void PlayClip(string name, float volume, float pitch, float minGap)
    {
        if (!clips.TryGetValue(name, out var c)) return;
        if (last.TryGetValue(name, out var t) && Time.unscaledTime - t < minGap) return;
        last[name] = Time.unscaledTime;
        var s = pool[next]; next = (next + 1) % pool.Length;
        s.pitch = pitch; s.PlayOneShot(c, volume * Master);
    }

    // ------------------------------------------------------------------ synthesis
    float Rnd() => (float)(rng.NextDouble() * 2 - 1);

    AudioClip Make(string name, float[] data, bool loop = false)
    {
        float peak = 0f; foreach (var v in data) peak = Mathf.Max(peak, Mathf.Abs(v));
        float g = peak > 0.95f ? 0.95f / peak : 1f;
        int fade = loop ? 0 : Mathf.Min(data.Length / 4, 96);
        for (int i = 0; i < data.Length; i++)
        {
            float k = 1f;
            if (fade > 0) { if (i < fade) k = (float)i / fade; else if (i > data.Length - fade) k = (float)(data.Length - i) / fade; }
            data[i] *= g * k;
        }
        var clip = AudioClip.Create(name, data.Length, 1, SR, false);
        clip.SetData(data, 0);
        clips[name] = clip;
        return clip;
    }

    /// <summary>Hissy sizzle with random fat crackles. density = crackles per second at full level.</summary>
    float[] Sizzle(float seconds, Func<float, float> env, float density, float hiss, int seed)
    {
        rng = new System.Random(seed);
        int len = (int)(seconds * SR); var d = new float[len];
        float lp = 0f, prev = 0f, lp2 = 0f;
        float crackT = 0f, crackF = 3000f, crackA = 0f;
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / SR, e = env(t);
            float n = Rnd();
            float hp = n - prev; prev = n;                    // bright noise
            lp += (hp - lp) * 0.55f;                          // tame the very top
            lp2 += (n - lp2) * 0.08f;                         // a little body
            float s = (lp * 0.8f + lp2 * 0.5f) * hiss;
            // crackles: short decaying high pings, more of them while it is loud
            if (rng.NextDouble() < density * e / SR) { crackT = 0f; crackF = 1800f + (float)rng.NextDouble() * 3500f; crackA = 0.4f + (float)rng.NextDouble() * 0.6f; }
            if (crackA > 0f)
            {
                crackT += 1f / SR;
                s += crackA * Mathf.Exp(-crackT * 900f) * (Mathf.Sin(2 * Mathf.PI * crackF * crackT) * 0.6f + Rnd() * 0.4f);
                if (crackT > 0.01f) crackA = 0f;
            }
            d[i] = s * e;
        }
        return d;
    }

    static float Bell(float f, float t, float decay)
        => Mathf.Exp(-t / decay) * (Mathf.Sin(2 * Mathf.PI * f * t) + 0.4f * Mathf.Sin(2 * Mathf.PI * f * 2.76f * t) * Mathf.Exp(-t * 8f) + 0.2f * Mathf.Sin(2 * Mathf.PI * f * 5.4f * t) * Mathf.Exp(-t * 14f));

    float[] Gen(float seconds, Func<float, float> f) { int len = (int)(seconds * SR); var d = new float[len]; for (int i = 0; i < len; i++) d[i] = f((float)i / SR); return d; }

    void Build()
    {
        // ฉ่า! - big burst that settles down
        Make("sizzle", Sizzle(1.7f, t => (t < 0.012f ? t / 0.012f : 1f) * (0.25f + 0.75f * Mathf.Exp(-t * 3.2f)) * Mathf.Clamp01((1.7f - t) / 0.4f), 140f, 0.9f, 3));
        // frying loop (3 s, crossfaded so it loops smoothly)
        var fryData = Sizzle(3.2f, t => 0.55f + 0.15f * Mathf.Sin(t * 2.1f) + 0.1f * Mathf.Sin(t * 5.3f + 1f), 26f, 0.5f, 5);
        int loopLen = 3 * SR, xf = (int)(0.2f * SR); var loop = new float[loopLen];
        for (int i = 0; i < loopLen; i++) loop[i] = fryData[i];
        for (int i = 0; i < xf; i++) { float k = (float)i / xf; loop[i] = fryData[i] * k + fryData[loopLen + i] * (1f - k); }
        Make("fry", loop, true);

        // wooden mortar thump + wet squish
        rng = new System.Random(7);
        Make("pound", Gen(0.28f, t =>
        {
            float f = 150f - 70f * Mathf.Clamp01(t / 0.08f);
            float body = Mathf.Sin(2 * Mathf.PI * f * t) * Mathf.Exp(-t * 28f);
            float knock = Rnd() * Mathf.Exp(-t * 160f) * 0.6f;
            float wet = Rnd() * Mathf.Exp(-Mathf.Pow((t - 0.05f) / 0.035f, 2f)) * 0.25f;
            return body + knock + wet;
        }));
        float lpS = 0f;
        Make("stir", Gen(0.3f, t =>
        {
            float n = Rnd(); lpS += (n - lpS) * (0.05f + 0.15f * Mathf.Sin(t * 30f) * Mathf.Sin(t * 30f));
            float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.3f));
            return lpS * 2.2f * env + 0.15f * Mathf.Sin(2 * Mathf.PI * (300f + 400f * t) * t) * env;
        }));
        // ingredient drops into the paste
        Make("plop", Gen(0.18f, t =>
        {
            float f = 700f * Mathf.Exp(-t * 18f) + 160f;
            return Mathf.Sin(2 * Mathf.PI * f * t) * Mathf.Exp(-t * 22f) * 0.9f;
        }));
        // spoon scoop into a cup: wet slurp + light tap
        float lpC = 0f;
        Make("scoop", Gen(0.32f, t =>
        {
            float n = Rnd(); lpC += (n - lpC) * 0.12f;
            float slurp = lpC * 1.8f * Mathf.Exp(-Mathf.Pow((t - 0.07f) / 0.05f, 2f));
            float tap = Bell(1500f, Mathf.Max(0f, t - 0.16f), 0.05f) * (t > 0.16f ? 0.35f : 0f);
            float bloop = Mathf.Sin(2 * Mathf.PI * (380f + 500f * t) * t) * Mathf.Exp(-t * 14f) * 0.5f;
            return slurp + tap + bloop;
        }));
        // plate clink
        Make("serve", Gen(0.45f, t => Bell(2100f, t, 0.09f) * 0.6f + Bell(3150f, Mathf.Max(0f, t - 0.06f), 0.07f) * (t > 0.06f ? 0.4f : 0f)));
        // coins: a few bright clinks
        Make("cash", Gen(0.55f, t =>
        {
            float v = 0f; float[] at = { 0f, 0.07f, 0.12f, 0.2f }; float[] fr = { 2637f, 3136f, 2794f, 3520f };
            for (int k = 0; k < at.Length; k++) if (t >= at[k]) v += Bell(fr[k], t - at[k], 0.08f) * 0.35f;
            return v;
        }));
        // shop door bell: ding-dong
        Make("bell", Gen(0.9f, t => Bell(1318.5f, t, 0.25f) * 0.5f + (t > 0.18f ? Bell(1046.5f, t - 0.18f, 0.35f) * 0.5f : 0f)));
    }
}
