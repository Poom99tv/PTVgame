using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// All game sound effects, synthesized at start-up (no audio files needed).
///   Pickup   : bell notes running up and down a pentatonic scale while the "+N" combo keeps going, back to the
///              first note when the combo ends.
///   Others   : footsteps, jump / land, attack swoosh, countdown, win fanfare, level up, skill unlock,
///              UI clicks, black hole, lightning, golden walnut, TikTok gifts, sky-rain cutscene, ambience.
/// M = mute / unmute (saved).
/// </summary>
public sealed class PTVAudio : MonoBehaviour
{
    const int SR = 44100;
    static PTVAudio inst;
    public static PTVAudio I { get { if (!inst) Create(); return inst; } }

    [Range(0, 1)] public float master = 0.8f;
    public enum PickupStyle { Collect, Bell }
    [Tooltip("Collect = game item-get blip, Bell = soft bell notes")]
    public PickupStyle pickupStyle = PickupStyle.Collect;
    public bool muted;

    AudioSource[] pool; int next;
    AudioSource windSrc, rainSrc;
    readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    AudioClip[] notes;
    readonly Dictionary<string, float> lastPlay = new Dictionary<string, float>();
    float lastNote = -1f, nextBird, rainTarget;
    /// <summary>Wind + birds background. Off in PTVFOOD.</summary>
    public bool ambience = false;
    int noteStep = -1;
    System.Random rng = new System.Random(7);

    static void Create()
    {
        var go = new GameObject("PTV_Audio");
        DontDestroyOnLoad(go);
        inst = go.AddComponent<PTVAudio>();
    }

    void Awake()
    {
        if (inst && inst != this) { Destroy(gameObject); return; }
        inst = this;
        muted = PlayerPrefs.GetInt("PTVFOOD.Mute", 0) == 1;
        pool = new AudioSource[20];
        for (int i = 0; i < pool.Length; i++) pool[i] = NewSource(false);
        windSrc = NewSource(true); rainSrc = NewSource(true);
        Build();
        windSrc.clip = clips["wind"]; windSrc.volume = 0f; windSrc.Play();
        rainSrc.clip = clips["rain"]; rainSrc.volume = 0f; rainSrc.Play();
        nextBird = Time.unscaledTime + 3f;
        if (!FindAnyObjectByType<AudioListener>() && Camera.main) Camera.main.gameObject.AddComponent<AudioListener>();
    }

    AudioSource NewSource(bool loop)
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false; s.loop = loop; s.spatialBlend = 0f; s.ignoreListenerPause = true;
        return s;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.mKey.wasPressedThisFrame && !TypingInUI())
        {
            muted = !muted; PlayerPrefs.SetInt("PTVFOOD.Mute", muted ? 1 : 0); PlayerPrefs.Save();
            AudioListener.volume = 1f;
        }
        float m = muted ? 0f : master;
        // ambience: soft wind + birds
        windSrc.volume = 0f;   // PTVFOOD: no background ambience, game sounds only
        rainSrc.volume = Mathf.MoveTowards(rainSrc.volume, rainTarget * m, Time.unscaledDeltaTime * 1.2f);
        if (ambience && Time.unscaledTime > nextBird)
        {
            nextBird = Time.unscaledTime + 2.5f + (float)rng.NextDouble() * 6f;
            Play("bird" + rng.Next(3), 0.10f + 0.08f * (float)rng.NextDouble(), 0.9f + 0.25f * (float)rng.NextDouble());
        }
    }

    static bool TypingInUI()
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        var sel = es ? es.currentSelectedGameObject : null;
        var f = sel ? sel.GetComponent<UnityEngine.UI.InputField>() : null;
        return f && f.isFocused;
    }

    // ------------------------------------------------------------------ public API
    public void Play(string name, float volume = 1f, float pitch = 1f, float minGap = 0f)
    {
        if (muted || !clips.TryGetValue(name, out var clip)) return;
        if (minGap > 0f)
        {
            if (lastPlay.TryGetValue(name, out float t) && Time.unscaledTime - t < minGap) return;
            lastPlay[name] = Time.unscaledTime;
        }
        PlayClip(clip, volume, pitch);
    }

    void PlayClip(AudioClip clip, float volume, float pitch)
    {
        var s = pool[next]; next = (next + 1) % pool.Length;
        s.Stop(); s.clip = clip; s.volume = volume * master; s.pitch = pitch; s.Play();
    }

    /// <summary>One note of the scale: step 0 = first pickup of a combo; the notes go up and down continuously.</summary>
    public void Pickup(int step)
    {
        if (muted) return;
        if (step == 0) noteStep = -1;                         // new combo -> back to the first note
        if (Time.unscaledTime - lastNote < 0.06f && step != 0) return;   // don't machine-gun when a skill grabs 100s at once
        lastNote = Time.unscaledTime;
        noteStep++;
        int n = notes.Length;
        int period = 2 * (n - 1);                             // up the scale, back down, up again ... (ping-pong)
        int ph = noteStep % period;
        int idx = ph < n ? ph : period - ph;
        float vol = 0.30f + 0.06f * Mathf.Min(1f, noteStep / 20f);
        PlayClip(notes[idx], vol, 1f);
        if (idx == n - 1) Play("sparkle", 0.25f);             // top of the run
    }

    public void Rain(float level) { rainTarget = Mathf.Clamp01(level) * 0.5f; }

    public static void Sfx(string name, float volume = 1f, float pitch = 1f, float minGap = 0f) { if (inst || Application.isPlaying) I.Play(name, volume, pitch, minGap); }

    // ------------------------------------------------------------------ synthesis
    static float Sin(double ph) => (float)Math.Sin(ph);
    const double TAU = Math.PI * 2.0;

    AudioClip Make(string name, float seconds, Func<int, float, float> f)
    {
        int len = Mathf.Max(1, (int)(seconds * SR));
        var data = new float[len];
        for (int i = 0; i < len; i++) data[i] = f(i, (float)i / SR);
        // tiny fade in/out to avoid clicks + normalize peaks above 0.95
        float peak = 0f; foreach (var v in data) peak = Mathf.Max(peak, Mathf.Abs(v));
        float g = peak > 0.95f ? 0.95f / peak : 1f;
        int fade = Mathf.Min(len / 4, 64);
        for (int i = 0; i < len; i++)
        {
            float k = 1f;
            if (i < fade) k = (float)i / fade; else if (i > len - fade) k = (float)(len - i) / fade;
            data[i] *= g * k;
        }
        var clip = AudioClip.Create(name, len, 1, SR, false);
        clip.SetData(data, 0);
        clips[name] = clip;
        return clip;
    }

    float[] Noise(int len, int seed) { var r = new System.Random(seed); var n = new float[len]; for (int i = 0; i < len; i++) n[i] = (float)(r.NextDouble() * 2 - 1); return n; }

    static float Env(float t, float a, float d) => t < a ? t / a : Mathf.Exp(-(t - a) / d);

    static float Bell(float f, float t, float decay)
    {
        float e = Env(t, 0.003f, decay);
        double w = TAU * f * t;
        return e * (Sin(w) + 0.38f * Sin(2 * w) * Mathf.Exp(-t * 6f) + 0.16f * Sin(3.01 * w) * Mathf.Exp(-t * 10f) + 0.06f * Sin(4.2 * w) * Mathf.Exp(-t * 16f));
    }

    /// <summary>Game "item get" blip: tiny pop, quick upward pitch flick, bright tone, then a softer echo blip a fifth higher.</summary>
    static float Collect(float f, float t)
    {
        float v = 0f;
        // main blip with a fast pitch flick up into the note
        float glide = t < 0.025f ? Mathf.Lerp(0.7f, 1f, t / 0.025f) : 1f;
        double ph = TAU * f * glide * t;
        float tone = Sin(ph);
        float bright = (float)Math.Tanh(3.0 * Math.Sin(ph)) * 0.35f;      // a little square-ish shine
        v += 0.5f * (tone + bright) * Env(t, 0.001f, 0.055f);
        // second blip (fifth above)
        float t2 = t - 0.045f;
        if (t2 > 0f) { double p2 = TAU * f * 1.4983f * t2; v += 0.3f * (Sin(p2) + 0.25f * (float)Math.Tanh(3.0 * Math.Sin(p2))) * Env(t2, 0.001f, 0.06f); }
        // pop at the very start
        v += 0.25f * Sin(TAU * f * 3f * t) * Mathf.Exp(-t / 0.004f);
        return v;
    }

    static float Midi(float m) => 440f * Mathf.Pow(2f, (m - 69f) / 12f);

    void Build()
    {
        // rising pickup scale: C major pentatonic, C5 -> 3 octaves
        int[] penta = { 0, 2, 4, 7, 9 };
        notes = new AudioClip[15];
        for (int i = 0; i < notes.Length; i++)
        {
            float f = Midi((pickupStyle == PickupStyle.Bell ? 72 : 67) + 12 * (i / 5) + penta[i % 5]);   // blips start a bit lower so the top isn't shrill
            if (pickupStyle == PickupStyle.Bell)
                notes[i] = Make("note" + i, 0.5f, (s, t) => 0.55f * Bell(f, t, 0.18f) + 0.12f * Sin(TAU * f * 0.5 * t) * Env(t, 0.002f, 0.05f));
            else
                notes[i] = Make("note" + i, 0.22f, (s, t) => Collect(f, t));
        }

        // UI
        Make("click", 0.09f, (s, t) => { float f = 900f * Mathf.Exp(-t * 18f) + 300f; return 0.7f * Sin(TAU * f * t) * Env(t, 0.001f, 0.025f); });
        Make("hover", 0.04f, (s, t) => 0.25f * Sin(TAU * 2400 * t) * Env(t, 0.001f, 0.01f));
        var nz = Noise(SR * 3, 1);
        Make("open", 0.45f, (s, t) => { float k = t / 0.45f; return 0.35f * nz[s] * Mathf.Sin(k * Mathf.PI) * (0.4f + k) + 0.35f * Bell(Midi(84 + 7 * (k > 0.5f ? 1 : 0)), t % 0.22f, 0.15f); });
        Make("close", 0.3f, (s, t) => { float k = t / 0.3f; return 0.3f * nz[s] * Mathf.Sin(k * Mathf.PI) * (1.2f - k) + 0.3f * Bell(Midi(79), t, 0.1f); });
        Make("toggle", 0.12f, (s, t) => 0.5f * Bell(Midi(88), t, 0.05f));
        Make("deny", 0.32f, (s, t) => { float f = t < 0.14f ? 220f : 165f; float tt = t < 0.14f ? t : t - 0.16f; return tt < 0 ? 0 : 0.35f * Mathf.Sign(Sin(TAU * f * t)) * Env(tt, 0.004f, 0.06f) * 0.6f; });
        Make("unlock", 1.1f, (s, t) =>
        {
            float v = 0f; int[] arp = { 72, 76, 79, 84, 88 };
            for (int i = 0; i < arp.Length; i++) { float tt = t - i * 0.07f; if (tt > 0) v += 0.32f * Bell(Midi(arp[i]), tt, 0.35f); }
            return v + 0.05f * nz[s] * Env(t, 0.01f, 0.3f) * Sin(TAU * 6000 * t);
        });
        Make("sparkle", 0.6f, (s, t) =>
        {
            float v = 0f;
            for (int i = 0; i < 6; i++) { float tt = t - i * 0.045f; if (tt > 0) v += 0.18f * Bell(Midi(96 + (i * 5) % 12), tt, 0.12f); }
            return v;
        });

        // level up fanfare
        Make("levelup", 1.6f, (s, t) =>
        {
            float v = 0f; int[] m = { 67, 72, 76, 79 }; float[] at = { 0f, 0.1f, 0.2f, 0.32f };
            for (int i = 0; i < m.Length; i++)
            {
                float tt = t - at[i]; if (tt < 0) continue; float f = Midi(m[i]);
                float d = i == 3 ? 0.6f : 0.12f;
                double w = TAU * f * tt;
                v += 0.22f * Env(tt, 0.01f, d) * (Sin(w) + 0.5f * Sin(2 * w) + 0.3f * Sin(3 * w) + 0.15f * Sin(4 * w));
            }
            float c = t - 0.32f; if (c > 0) v += 0.18f * (Bell(Midi(84), c, 0.5f) + Bell(Midi(88), c, 0.5f) + Bell(Midi(91), c, 0.5f));
            return v;
        });

        // countdown + win
        Make("tick", 0.14f, (s, t) => 0.45f * Sin(TAU * 880 * t) * Env(t, 0.002f, 0.05f));
        Make("tickLast", 0.35f, (s, t) => 0.5f * (Sin(TAU * 1320 * t) + 0.3f * Sin(TAU * 2640 * t)) * Env(t, 0.002f, 0.12f));
        Make("win", 3.0f, (s, t) =>
        {
            float v = 0f;
            int[][] chords = { new[] { 60, 64, 67 }, new[] { 65, 69, 72 }, new[] { 67, 71, 74 }, new[] { 72, 76, 79, 84 } };
            float[] at = { 0f, 0.35f, 0.7f, 1.05f };
            for (int c = 0; c < chords.Length; c++)
            {
                float tt = t - at[c]; if (tt < 0) continue;
                float d = c == 3 ? 1.2f : 0.25f;
                foreach (var m in chords[c])
                {
                    double w = TAU * Midi(m) * tt;
                    v += 0.12f * Env(tt, 0.015f, d) * (Sin(w) + 0.45f * Sin(2 * w) + 0.25f * Sin(3 * w));
                }
                for (int a = 0; a < 3 && c < 3; a++) { float ta = tt - a * 0.09f; if (ta > 0) v += 0.1f * Bell(Midi(chords[c][a] + 24), ta, 0.15f); }
            }
            float sp = t - 1.05f; if (sp > 0) v += 0.08f * Bell(Midi(96 + (int)(sp * 20) % 12), sp % 0.05f, 0.04f) * Mathf.Exp(-sp);
            return v;
        });

        // player
        Make("step", 0.05f, (s, t) => 0.5f * nz[s + 9000] * Env(t, 0.001f, 0.012f) + 0.3f * Sin(TAU * 140 * t) * Env(t, 0.001f, 0.015f));
        Make("jump", 0.2f, (s, t) => { float f = 320f + 900f * t; return 0.35f * Sin(TAU * f * t) * Env(t, 0.004f, 0.06f); });
        Make("land", 0.16f, (s, t) => 0.5f * Sin(TAU * (110 - 200 * t) * t) * Env(t, 0.002f, 0.04f) + 0.3f * nz[s + 20000] * Env(t, 0.001f, 0.02f));
        float lp = 0f;
        Make("swoosh", 0.25f, (s, t) =>
        {
            float k = t / 0.25f; float a = 0.05f + 0.5f * Mathf.Sin(k * Mathf.PI);   // cutoff sweeps up then down
            lp += a * (nz[s + 30000] - lp);
            return 0.6f * (nz[s + 30000] - lp) * Mathf.Sin(k * Mathf.PI);
        });

        // skills
        lp = 0f;
        Make("blackhole", 1.6f, (s, t) =>
        {
            float k = t / 1.6f;
            lp += 0.02f * (nz[s] - lp);
            float rumble = Sin(TAU * (45 + 15 * Mathf.Sin(t * 9f)) * t);
            float suck = Sin(TAU * (900 - 700 * k) * t) * 0.15f;     // downward whistle: things being sucked in
            return (0.55f * rumble + 2.2f * lp + suck) * Mathf.Sin(Mathf.Min(1f, k * 3f) * Mathf.PI * 0.5f) * (1f - k * k);
        });
        Make("lightning", 1.0f, (s, t) =>
        {
            float crack = nz[s + 40000] * Env(t, 0.0005f, 0.08f) * (0.6f + 0.4f * Mathf.Sign(Sin(TAU * 60 * t)));
            float boom = Sin(TAU * (70 - 30 * t) * t) * Env(t, 0.01f, 0.35f);
            return 0.6f * crack + 0.5f * boom;
        });
        Make("walnutSpawn", 1.0f, (s, t) =>
        {
            float v = 0f; for (int i = 0; i < 8; i++) { float tt = t - i * 0.06f; if (tt > 0) v += 0.14f * Bell(Midi(84 + (i * 7) % 24), tt, 0.25f); }
            return v;
        });
        Make("walnutGet", 1.4f, (s, t) => 0.25f * (Bell(Midi(72), t, 0.6f) + Bell(Midi(76), t - 0.05f > 0 ? t - 0.05f : 0, 0.6f) + Bell(Midi(79), t - 0.1f > 0 ? t - 0.1f : 0, 0.6f) + Bell(Midi(84), t - 0.15f > 0 ? t - 0.15f : 0, 0.8f)));

        // TikTok
        Make("coin", 0.5f, (s, t) =>
        {   // classic two-note coin
            float f = t < 0.08f ? Midi(83) : Midi(88); float tt = t < 0.08f ? t : t - 0.08f;
            float sq = Mathf.Sign(Sin(TAU * f * t)) * 0.5f + Sin(TAU * f * t) * 0.5f;
            return 0.3f * sq * Env(tt, 0.002f, t < 0.08f ? 0.2f : 0.15f);
        });
        Make("connect", 0.7f, (s, t) => 0.3f * Bell(Midi(76), t, 0.3f) + (t > 0.12f ? 0.3f * Bell(Midi(83), t - 0.12f, 0.4f) : 0f));
        Make("error", 0.5f, (s, t) => 0.3f * (t < 0.15f ? Bell(Midi(70), t, 0.15f) : Bell(Midi(63), t - 0.15f, 0.25f)));
        Make("start", 1.2f, (s, t) =>
        {
            float k = Mathf.Clamp01(t / 0.5f);
            float whoosh = 0.25f * nz[s] * Mathf.Sin(k * Mathf.PI);
            float c = t - 0.4f; float chord = c > 0 ? 0.18f * (Bell(Midi(72), c, 0.5f) + Bell(Midi(76), c, 0.5f) + Bell(Midi(79), c, 0.5f) + Bell(Midi(84), c, 0.6f)) : 0f;
            return whoosh + chord;
        });
        Make("portal", 1.4f, (s, t) =>
        {   // magic rising sweep for the cutscene
            float k = t / 1.4f; float f = 200f * Mathf.Pow(8f, k);
            float sweep = Sin(TAU * f * t * 0.5f) * 0.25f;
            float shimmer = 0f; for (int i = 0; i < 4; i++) shimmer += Sin(TAU * (f * (2 + i * 1.5f)) * t) * 0.04f;
            return (sweep + shimmer) * Mathf.Sin(Mathf.Min(1f, k * 1.2f) * Mathf.PI * 0.5f) + 0.08f * nz[s + 50000] * k;
        });
        Make("boom", 1.6f, (s, t) => 0.7f * Sin(TAU * (60 - 25 * t) * t) * Env(t, 0.005f, 0.5f) + 0.35f * nz[s + 60000] * Env(t, 0.001f, 0.12f) + 0.15f * Bell(Midi(96), t, 0.6f));
        Make("fanfareSmall", 1.0f, (s, t) =>
        {
            float v = 0f; int[] m = { 79, 84, 88, 91 };
            for (int i = 0; i < m.Length; i++) { float tt = t - i * 0.06f; if (tt > 0) v += 0.2f * Bell(Midi(m[i]), tt, 0.4f); }
            return v;
        });

        // acorns hitting the ground: 2 s seamless loop of random woody taps
        var rain = new float[SR * 2]; var rr = new System.Random(11);
        for (int n = 0; n < 220; n++)
        {
            int start = rr.Next(0, rain.Length - 3000); float f = 1400f + (float)rr.NextDouble() * 2200f; float a = 0.15f + 0.25f * (float)rr.NextDouble();
            for (int i = 0; i < 2600; i++) { float t = (float)i / SR; rain[start + i] += a * Sin(TAU * f * t) * Mathf.Exp(-t / 0.008f) + a * 0.3f * (float)(rr.NextDouble() * 2 - 1) * Mathf.Exp(-t / 0.002f); }
        }
        Make("rain", 2f, (s, t) => rain[s]);
        Make("patter", 1.2f, (s, t) => rain[s] * (1f - t / 1.2f));

        // ambience: soft wind (seamless 6 s) + three bird chirps
        int wl = SR * 6; var wind = new float[wl]; float w1 = 0f, w2 = 0f;
        var wn = Noise(wl, 5);
        for (int i = 0; i < wl; i++)
        {
            float t = (float)i / SR;
            float cut = 0.004f + 0.006f * (0.5f + 0.5f * Mathf.Sin(t * TAU_F / 6f * 2f));
            w1 += cut * (wn[i] - w1); w2 += cut * (w1 - w2);
            wind[i] = w2 * 6f;
        }
        int xf = SR / 2;    // crossfade the end into the start
        for (int i = 0; i < xf; i++) { float k = (float)i / xf; wind[i] = wind[i] * k + wind[wl - xf + i] * (1f - k); }
        Make("wind", 6f - 0.5f, (s, t) => wind[s]);
        for (int b = 0; b < 3; b++)
        {
            int bb = b;
            Make("bird" + b, 0.5f, (s, t) =>
            {
                float v = 0f; int chirps = 2 + bb;
                for (int c = 0; c < chirps; c++)
                {
                    float tt = t - c * 0.11f; if (tt < 0 || tt > 0.09f) continue;
                    float f = 2600f + 1400f * Mathf.Sin(tt / 0.09f * Mathf.PI) + bb * 400f;
                    v += 0.4f * Sin(TAU * f * tt) * Mathf.Sin(tt / 0.09f * Mathf.PI);
                }
                return v;
            });
        }
    }
    const float TAU_F = Mathf.PI * 2f;
}
