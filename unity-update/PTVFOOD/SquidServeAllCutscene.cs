using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "ทำออเดอร์ทั้งหมด" skill as a cinematic:
///  1. Letterbox + camera dolly onto the vendor          (0.0 - 0.9s)
///  2. Gifter card slams in, sunburst, golden power pillar (0.9 - 2.4s)
///  3. Shockwave + slow-mo, food storm rains onto every waiting customer
///  4. "ออเดอร์หมดเกลี้ยง!" result + fireworks, camera glides back
/// Gameplay (clicks / auto-serve / wheel) waits while it plays. Several gifts queue up.
/// </summary>
public sealed partial class SquidGame
{
    readonly Queue<SquidGiftDirector.StallEvent> serveAllQueue = new Queue<SquidGiftDirector.StallEvent>();
    bool serveAllRunning, cineActive;
    public bool CutscenePlaying => serveAllRunning;

    Canvas cineCanvas;
    RectTransform cineRoot, barTop, barBot, burstRt, speedRt, cardRt, resultRt;
    Image flashImg, vignetteImg, burstImg, speedImg, cardGlow;
    Text cineName, cineTitle, cineResult, cineCount, cineInitial;
    RawImage cinePic;
    Material glowMat;
    static Sprite raysSprite, linesSprite, vignetteSprite;

    void QueueServeAll(SquidGiftDirector.StallEvent e)
    {
        serveAllQueue.Enqueue(e);
        if (!serveAllRunning) StartCoroutine(ServeAllRunner());
    }

    IEnumerator ServeAllRunner()
    {
        serveAllRunning = true;
        while (serveAllQueue.Count > 0)
        {
            while (IsWaiting || PTVStartMenu.UIBlocking) yield return null;
            var e = serveAllQueue.Dequeue();
            yield return ServeAllCutscene(e);
            yield return new WaitForSecondsRealtime(0.25f);
        }
        serveAllRunning = false;
    }

    // ------------------------------------------------------------------ the cutscene
    sealed class FoodShot { public Customer c; public bool squid; }

    IEnumerator ServeAllCutscene(SquidGiftDirector.StallEvent e)
    {
        if (!cam) cam = Camera.main;
        if (!cineCanvas) BuildCine();
        string nm = e.sender != null ? e.sender.DisplayName : "?";
        SetupCard(e.sender, nm);

        Vector3 p0 = cam.transform.position; Quaternion r0 = cam.transform.rotation; float f0 = cam.fieldOfView;
        Quaternion vendorRot0 = vendor.localRotation;
        Vector3 head = vendorHead ? vendorHead.position : vendor.position + Vector3.up * 2.2f;
        Vector3 toCam = p0 - head; toCam.y = 0f; toCam = toCam.sqrMagnitude > 1e-4f ? toCam.normalized : Vector3.back;
        Vector3 side = Vector3.Cross(Vector3.up, toCam);

        cineActive = true;
        cineCanvas.gameObject.SetActive(true);
        cineCanvas.enabled = true;
        float shake = 0f, flash = 0f, bars = 0f, speed = 0f, burst = 0f;
        float t = 0f, slowUntil = -1f;
        bool didTitle = false, didOut = false, didImpact = false, didResult = false;
        ParticleSystem pillar = null, sparkle = null;
        var shots = new List<FoodShot>(); var pending = new Dictionary<Customer, int>();
        int launched = 0, landed = 0, servedPeople = 0; long servedSets = 0;
        float resultAt = 99f, end = 99f, nextFirework = 0f;
        Vector3 wideCenter = stall.position, wideFrom = p0; Quaternion wideRot = r0; float wideFov = f0;

        PTVAudio.Sfx("portal", 0.75f, 1.1f);
        flash = 0.85f;
        try
        {
            while (t < end)
            {
                float dt = Time.unscaledDeltaTime; t += dt;
                Time.timeScale = t < slowUntil ? 0.35f : 1f;

                // ---------------- story beats
                if (!didTitle && t >= 0.9f)
                {
                    didTitle = true; flash = 0.7f; shake = 0.35f; speed = 1f;
                    PTVAudio.Sfx("levelup", 0.8f); PTVAudio.Sfx("boom", 0.35f, 1.4f);
                    pillar = Pillar(vendor.position);
                    sparkle = Orbit(vendor.position + Vector3.up * 1.4f);
                    Burst(vendor.position + Vector3.up * 1.2f, new Color(1f, 0.85f, 0.3f), new Color(1f, 0.5f, 0.2f), 60, 5f, 0.35f, 0.9f, 0.3f);
                }
                if (!didOut && t >= 2.3f) { didOut = true; PTVAudio.Sfx("swoosh", 0.7f, 0.8f); }
                if (!didImpact && t >= 2.45f)
                {
                    didImpact = true; flash = 1f; shake = 0.9f; speed = 1f; slowUntil = t + 0.55f;
                    PTVAudio.Sfx("boom", 0.9f); PTVAudio.Sfx("lightning", 0.45f, 1.2f);
                    if (pillar) pillar.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    Shockwave(vendor.position + Vector3.up * 0.4f);
                    Burst(vendor.position + Vector3.up * 2f, Color.white, new Color(1f, 0.8f, 0.3f), 120, 9f, 0.45f, 0.8f, 0f);
                    // who gets served: everyone waiting right now
                    var targets = new List<Customer>();
                    foreach (var c in customers) if (c.go && !c.leaving && (c.needSquid > 0 || c.needSauce > 0)) targets.Add(c);
                    int per = targets.Count <= 6 ? 3 : targets.Count <= 12 ? 2 : 1;
                    foreach (var c in targets)
                    {
                        servedPeople++; servedSets += Mathf.Max(c.needSquid, c.needSauce);
                        int ns = c.needSquid > 0 ? Mathf.Min(c.needSquid, per) : 0, nc = c.needSauce > 0 ? Mathf.Min(c.needSauce, per) : 0;
                        for (int k = 0; k < Mathf.Max(ns, nc); k++)
                        {
                            if (k < ns) shots.Add(new FoodShot { c = c, squid = true });
                            if (k < nc) shots.Add(new FoodShot { c = c, squid = false });
                        }
                        c.needSquid = 0; c.needSauce = 0;
                        if (ns + nc > 0) { c.deliveries++; pending[c] = ns + nc; }
                        UpdateTag(c);
                    }
                    if (targets.Count > 0)
                    {
                        wideCenter = Vector3.zero; foreach (var c in targets) wideCenter += c.tr.position; wideCenter /= targets.Count;
                        wideCenter = Vector3.Lerp(wideCenter, vendor.position, 0.5f);
                    }
                    else wideCenter = vendor.position + toCam * 2.5f;
                    if (shots.Count == 0) resultAt = t + 0.9f;
                }
                // food storm: launch spread over ~1 second of real time
                if (didImpact && launched < shots.Count)
                {
                    float spacing = Mathf.Min(0.07f, 1.1f / Mathf.Max(1, shots.Count));
                    while (launched < shots.Count && t >= 2.6f + launched * spacing)
                    {
                        var s = shots[launched++];
                        LaunchFood(s, () =>
                        {
                            landed++;
                            if (s.c.go)
                            {
                                Burst(s.c.tr.position + Vector3.up * 1.9f, s.squid ? new Color(1f, 0.65f, 0.3f) : new Color(0.5f, 0.9f, 0.4f), Color.white, 18, 3f, 0.22f, 0.55f, 0.4f);
                                SquidSfx.Play("serve", 0.35f, Random.Range(1.0f, 1.3f), 0.04f);
                            }
                            if (pending.TryGetValue(s.c, out var left))
                            {
                                pending[s.c] = --left;
                                if (left <= 0)
                                {
                                    s.c.deliveries = Mathf.Max(0, s.c.deliveries - 1);
                                    if (s.c.go)
                                    {
                                        Pop(PTVLang.Thai ? "ครบแล้ว!" : "Done!", s.c.tr.position + Vector3.up * 2.8f, new Color(0.4f, 0.82f, 0.4f), 1.2f);
                                        Burst(s.c.tr.position + Vector3.up * 1.2f, new Color(1f, 0.9f, 0.4f), new Color(0.5f, 1f, 0.6f), 30, 4f, 0.25f, 0.7f, 0.2f);
                                        PTVAudio.Sfx("coin", 0.5f, Random.Range(1.1f, 1.4f), 0.05f);
                                    }
                                }
                            }
                        });
                        PTVAudio.Sfx("swoosh", 0.3f, Random.Range(1.2f, 1.6f), 0.06f);
                    }
                    if (launched >= shots.Count) resultAt = Mathf.Max(t + 0.8f, 3.9f);
                }
                if (didImpact && shots.Count > 0 && landed >= shots.Count) resultAt = Mathf.Min(resultAt, Mathf.Max(t + 0.15f, 3.6f));
                if (!didResult && t >= resultAt)
                {
                    didResult = true; flash = 0.6f; shake = 0.3f;
                    end = t + 2.3f; nextFirework = t;
                    bool any = servedPeople > 0;
                    bool th = PTVLang.Thai;
                    cineResult.resizeTextForBestFit = false; cineResult.fontSize = th ? 104 : 80; cineResult.horizontalOverflow = HorizontalWrapMode.Overflow;
                    cineResult.text = any ? (th ? "ออเดอร์หมดเกลี้ยง!" : "ALL ORDERS CLEARED!") : (th ? "ร้านว่างจ้า!" : "Shop is empty!");
                    cineCount.text = any ? (th ? "เสิร์ฟ " + servedPeople + " คน  ·  " + servedSets.ToString("N0") + " ชุด" : "Served " + servedPeople + " customers  ·  " + servedSets.ToString("N0") + " sets") : (th ? "ไม่มีออเดอร์ค้างเลย" : "No pending orders");
                    PTVAudio.Sfx(any ? "win" : "fanfareSmall", 0.75f);
                    wideFrom = cam.transform.position; wideRot = cam.transform.rotation; wideFov = cam.fieldOfView;
                    if (sparkle) sparkle.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
                if (didResult && t < end - 0.6f && t >= nextFirework)
                {
                    nextFirework = t + Random.Range(0.14f, 0.24f);
                    var sky = stall.position + Vector3.up * Random.Range(3.5f, 5.5f) + side * Random.Range(-4f, 4f) + toCam * Random.Range(-0.5f, 1.5f);
                    var hue = Color.HSVToRGB(Random.value, 0.75f, 1f);
                    Burst(sky, hue, Color.white, 70, 4.5f, 0.3f, 1.1f, 0.6f);
                    PTVAudio.Sfx("boom", 0.25f, Random.Range(1.6f, 2.2f), 0.08f);
                }

                // ---------------- camera
                Vector3 pos; Quaternion rot; float fov;
                if (t < 0.9f)
                {   // dolly from the play camera to a low close-up of the vendor
                    float k = Ease(t / 0.9f);
                    Vector3 close = head + toCam * 2.7f + side * 0.6f + Vector3.down * 0.35f;
                    pos = Vector3.Lerp(p0, close, k);
                    rot = Quaternion.Slerp(r0, Quaternion.LookRotation(head + Vector3.up * 0.15f - close, Vector3.up), k);
                    fov = Mathf.Lerp(f0, 38f, k);
                }
                else if (t < 2.45f)
                {   // slow heroic orbit
                    float k = (t - 0.9f) / 1.55f;
                    float ang = Mathf.Lerp(12f, -30f, Ease(k)) * Mathf.Deg2Rad;
                    Vector3 dir = toCam * Mathf.Cos(ang) + side * Mathf.Sin(ang);
                    pos = head + dir * Mathf.Lerp(2.7f, 2.3f, k) + Vector3.down * Mathf.Lerp(0.35f, 0.55f, k);
                    rot = Quaternion.LookRotation(head + Vector3.up * 0.2f - pos, Vector3.up);
                    fov = Mathf.Lerp(38f, 34f, k);
                }
                else if (!didResult)
                {   // whip to a high wide shot over the customers
                    float k = Ease(Mathf.Clamp01((t - 2.45f) / 0.45f));
                    float ang = -30f * Mathf.Deg2Rad;
                    Vector3 orbit = head + (toCam * Mathf.Cos(ang) + side * Mathf.Sin(ang)) * 2.3f + Vector3.down * 0.55f;
                    float drift = (t - 2.45f) * 0.25f;
                    Vector3 wide = wideCenter + toCam * 4.6f + Vector3.up * 8.2f + side * (drift - 0.5f);
                    pos = Vector3.Lerp(orbit, wide, k);
                    Vector3 look = Vector3.Lerp(head + Vector3.up * 0.2f, wideCenter + Vector3.up * 0.8f, k);
                    rot = Quaternion.LookRotation(look - pos, Vector3.up);
                    fov = Mathf.Lerp(34f, Mathf.Max(f0, 52f), k);
                }
                else
                {   // glide back to the play camera
                    float k = Ease(Mathf.Clamp01((t - resultAt - 0.25f) / 1.5f));
                    pos = Vector3.Lerp(wideFrom, p0, k);
                    rot = Quaternion.Slerp(wideRot, r0, k);
                    fov = Mathf.Lerp(wideFov, f0, k);
                }
                shake = Mathf.MoveTowards(shake, 0f, dt * 1.6f);
                float st = Time.unscaledTime * 28f;
                Vector3 jitter = (side * (Mathf.PerlinNoise(st, 1.3f) - 0.5f) + Vector3.up * (Mathf.PerlinNoise(4.1f, st) - 0.5f)) * 0.5f * shake;
                cam.transform.position = pos + jitter;
                cam.transform.rotation = rot * Quaternion.Euler(0, 0, (Mathf.PerlinNoise(st, 9f) - 0.5f) * 6f * shake);
                cam.fieldOfView = fov;

                // ---------------- vendor performance
                if (t >= 0.9f && t < 2.6f)
                {
                    float k = t - 0.9f;
                    float spin = Mathf.Clamp01(k / 0.6f);
                    vendor.localRotation = vendorRot0 * Quaternion.Euler(0, 360f * Ease(spin), 0);
                    var vp = vendor.localPosition; vp.y = vendorHome.y + Mathf.Sin(spin * Mathf.PI) * 0.6f + (spin >= 1f ? Mathf.Abs(Mathf.Sin(k * 9f)) * 0.08f : 0f); vendor.localPosition = vp;
                    armL.localRotation = Quaternion.Euler(0, -160 + 15 * Mathf.Sin(t * 14), 0);
                    armR.localRotation = Quaternion.Euler(0, 160 - 15 * Mathf.Sin(t * 14), 0);
                }
                else if (didImpact && launched < shots.Count)
                {   // throwing food
                    vendor.localRotation = vendorRot0;
                    float w = Mathf.Sin(t * 30f);
                    armL.localRotation = Quaternion.Euler(-60 + 70 * w, -10, 0);
                    armR.localRotation = Quaternion.Euler(-60 - 70 * w, 10, 0);
                }
                else vendor.localRotation = vendorRot0;

                // ---------------- overlay
                bars = Mathf.MoveTowards(bars, t < end - 0.45f ? 1f : 0f, dt * 3.5f);
                float bh = 0.115f * Ease(bars);
                barTop.anchorMin = new Vector2(0, 1f - bh); barBot.anchorMax = new Vector2(1, bh);
                vignetteImg.color = new Color(0.05f, 0.02f, 0f, 0.65f * Ease(bars));
                flash = Mathf.MoveTowards(flash, 0f, dt * 3.2f);
                flashImg.color = new Color(1f, 0.97f, 0.85f, flash * flash);
                speed = Mathf.MoveTowards(speed, 0f, dt * 1.6f);
                speedImg.color = new Color(1f, 1f, 1f, 0.55f * speed);
                speedRt.localRotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
                speedRt.localScale = Vector3.one * (1f + 0.06f * Random.value);

                burst = Mathf.MoveTowards(burst, t >= 0.9f && t < 2.35f ? 1f : didResult && t < end - 0.5f ? 0.8f : 0f, dt * 4f);
                burstImg.color = new Color(1f, 0.82f, 0.35f, 0.75f * burst);
                burstRt.localRotation = Quaternion.Euler(0, 0, -t * 40f);
                burstRt.localScale = Vector3.one * (0.6f + 0.5f * burst);

                // gifter card: slam in with overshoot, hold, shrink out
                if (t >= 0.9f && t < 2.45f)
                {
                    float k = t - 0.9f;
                    float s = k < 0.22f ? Mathf.Lerp(2.6f, 0.92f, Ease(k / 0.22f)) : k < 0.38f ? Mathf.Lerp(0.92f, 1f, (k - 0.22f) / 0.16f) : 1f + 0.03f * Mathf.Sin(k * 9f);
                    if (t > 2.25f) s *= 1f - Ease((t - 2.25f) / 0.2f);
                    cardRt.gameObject.SetActive(true);
                    cardRt.localScale = Vector3.one * Mathf.Max(0f, s);
                    cardRt.localRotation = Quaternion.Euler(0, 0, k < 0.22f ? Mathf.Lerp(-12f, 0f, k / 0.22f) : 0f);
                    cardGlow.color = new Color(1f, 0.85f, 0.35f, 0.55f + 0.25f * Mathf.Sin(t * 10f));
                    cineTitle.color = Color.Lerp(new Color(1f, 0.86f, 0.3f), Color.white, 0.5f + 0.5f * Mathf.Sin(t * 12f));
                }
                else cardRt.gameObject.SetActive(false);

                if (didResult)
                {
                    float k = t - resultAt;
                    float s = k < 0.2f ? Mathf.Lerp(3f, 0.9f, Ease(k / 0.2f)) : k < 0.35f ? Mathf.Lerp(0.9f, 1f, (k - 0.2f) / 0.15f) : 1f + 0.04f * Mathf.Sin(k * 8f);
                    if (t > end - 0.4f) s *= Mathf.Clamp01((end - t) / 0.4f);
                    resultRt.gameObject.SetActive(true);
                    resultRt.localScale = Vector3.one * s;
                }
                else resultRt.gameObject.SetActive(false);

                yield return null;
            }
        }
        finally
        {
            Time.timeScale = 1f;
            if (cam) { cam.transform.rotation = r0; cam.transform.position = p0; cam.fieldOfView = f0; }
            if (vendor) vendor.localRotation = vendorRot0;
            cineActive = false;
            if (pillar) Destroy(pillar.gameObject, 2f);
            if (sparkle) Destroy(sparkle.gameObject, 2f);
            if (cineCanvas) cineCanvas.gameObject.SetActive(false);
            if (cinePic) cinePic.texture = null;
            // anyone still marked as waiting for this cutscene's food gets released
            foreach (var kv in pending) if (kv.Value > 0 && kv.Key != null) kv.Key.deliveries = Mathf.Max(0, kv.Key.deliveries - 1);
        }
    }

    static float Ease(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

    void LaunchFood(FoodShot s, System.Action landed)
    {
        var go = s.squid ? SquidModels.Squid(null, "StormFood") : SquidModels.SauceCup(null, "StormFood");
        if (s.squid) { SquidModels.CookSquid(go, 1f, 1f, 0f, mpb); go.transform.localScale = Vector3.one * 0.85f; }
        else go.transform.localScale = Vector3.one * 1.25f;
        Vector3 from = vendor.position + Vector3.up * 2.3f + Random.insideUnitSphere * 0.25f;
        Vector3 to = s.c.go ? s.c.tr.position + Vector3.up * 1.9f + Random.insideUnitSphere * 0.15f : from;
        go.transform.position = from;
        var trail = go.AddComponent<TrailRenderer>();
        trail.time = 0.25f; trail.startWidth = 0.22f; trail.endWidth = 0f; trail.minVertexDistance = 0.05f;
        trail.sharedMaterial = GlowMat();
        trail.startColor = s.squid ? new Color(1f, 0.7f, 0.25f, 0.9f) : new Color(0.55f, 1f, 0.45f, 0.9f); trail.endColor = new Color(1f, 1f, 1f, 0f);
        Fly(go, from, to, Random.Range(0.55f, 0.75f), Random.rotation, Quaternion.identity, () => { if (go) Destroy(go); landed(); }, false, Random.Range(2.2f, 3.4f));
    }

    // ------------------------------------------------------------------ particles
    Material GlowMat()
    {
        if (glowMat) return glowMat;
        glowMat = new Material(SquidModels.Mat("Smoke", Color.white, 0f, null, true)) { name = "CineGlow" };
        glowMat.SetTexture("_BaseMap", SoftDot());
        glowMat.SetColor("_BaseColor", Color.white);
        glowMat.SetFloat("_Blend", 2f);
        glowMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        glowMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        glowMat.renderQueue = 3100;
        return glowMat;
    }

    ParticleSystem NewPS(string name, Vector3 pos, int max)
    {
        var go = new GameObject(name); go.transform.position = pos;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.maxParticles = max; main.simulationSpace = ParticleSystemSimulationSpace.World; main.useUnscaledTime = true; main.playOnAwake = false;
        var em = ps.emission; em.rateOverTime = 0f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                                          new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 0.5f), new GradientAlphaKey(0, 1) });
        col.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = GlowMat();
        return ps;
    }

    void Burst(Vector3 pos, Color a, Color b, int count, float spd, float size, float life, float gravity)
    {
        var ps = NewPS("CineBurst", pos, count + 10);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
        main.startSpeed = new ParticleSystem.MinMaxCurve(spd * 0.4f, spd);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
        main.startColor = new ParticleSystem.MinMaxGradient(a, b);
        main.gravityModifier = gravity;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.1f;
        var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = spd; lim.dampen = 0.08f;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 1, 1, 0.2f));
        ps.Play(); ps.Emit(count);
        Destroy(ps.gameObject, life + 0.5f);
    }

    void Shockwave(Vector3 pos)
    {
        var ps = NewPS("CineShockwave", pos, 140);
        ps.transform.rotation = Quaternion.Euler(-90, 0, 0);
        var main = ps.main;
        main.startLifetime = 0.7f; main.startSpeed = 11f; main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.5f), Color.white);
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.2f; sh.radiusThickness = 0f;
        ps.Play(); ps.Emit(130);
        Destroy(ps.gameObject, 1.2f);
    }

    ParticleSystem Pillar(Vector3 pos)
    {
        var ps = NewPS("CinePillar", pos, 400);
        ps.transform.rotation = Quaternion.Euler(-90, 0, 0);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f); main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.8f, 0.25f), new Color(1f, 1f, 0.75f));
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.7f; sh.radiusThickness = 0.3f;
        var em = ps.emission; em.rateOverTime = 260f;
        ps.Play();
        return ps;
    }

    ParticleSystem Orbit(Vector3 pos)
    {
        var ps = NewPS("CineSparkle", pos, 200);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f); main.startSpeed = 0.4f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white, new Color(1f, 0.85f, 0.4f));
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 1.3f; sh.radiusThickness = 0.2f;
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.orbitalY = 4f; vel.y = 1.2f;
        var em = ps.emission; em.rateOverTime = 120f;
        ps.Play();
        return ps;
    }

    // ------------------------------------------------------------------ overlay UI
    void BuildCine()
    {
        raysSprite = raysSprite ? raysSprite : MakeRays();
        linesSprite = linesSprite ? linesSprite : MakeSpeedLines();
        vignetteSprite = vignetteSprite ? vignetteSprite : MakeVignette();
        cineCanvas = PTVUI.MakeCanvas("PTV_ServeAllCine", 400, transform);
        cineCanvas.GetComponent<GraphicRaycaster>().enabled = false;
        cineRoot = (RectTransform)cineCanvas.transform;

        vignetteImg = FullImage("Vignette", vignetteSprite, Color.clear);
        speedImg = PTVUI.Img("SpeedLines", cineRoot, null, Vector2.zero, new Vector2(2600, 2600), Color.clear); speedImg.sprite = linesSprite; speedRt = speedImg.rectTransform;
        burstImg = PTVUI.Img("Sunburst", cineRoot, null, new Vector2(0, 60), new Vector2(1700, 1700), Color.clear); burstImg.sprite = raysSprite; burstRt = burstImg.rectTransform;

        // gifter card
        cardRt = PTVUI.Rect("GifterCard", cineRoot, new Vector2(0, 60), new Vector2(1000, 560));
        cardGlow = PTVUI.Img("Glow", cardRt, null, new Vector2(0, 110), new Vector2(330, 330), Color.white); cardGlow.sprite = SoftSprite();
        var ring = PTVUI.Img("Ring", cardRt, null, new Vector2(0, 110), new Vector2(230, 230), Color.white); ring.sprite = circle;
        var ringO = ring.gameObject.AddComponent<Outline>(); ringO.effectColor = new Color(1f, 0.75f, 0.2f); ringO.effectDistance = new Vector2(6, -6);
        var mask = PTVUI.Img("Mask", ring.transform, null, Vector2.zero, new Vector2(206, 206), new Color(1f, 0.72f, 0.5f)); mask.sprite = circle;
        mask.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        cinePic = PTVUI.Rect("Pic", mask.transform, Vector2.zero, new Vector2(206, 206)).gameObject.AddComponent<RawImage>(); cinePic.raycastTarget = false;
        cineInitial = PTVUI.Txt("Initial", mask.transform, "?", Vector2.zero, new Vector2(206, 206), 100, Color.white);
        cineName = PTVUI.Txt("Name", cardRt, "", new Vector2(0, -40), new Vector2(980, 70), 50, Color.white);
        cineName.horizontalOverflow = HorizontalWrapMode.Wrap; cineName.verticalOverflow = VerticalWrapMode.Truncate;
        cineName.resizeTextForBestFit = true; cineName.resizeTextMinSize = 24; cineName.resizeTextMaxSize = 50;
        ((PTVText)cineName).translate = false;
        cineTitle = PTVUI.Txt("Title", cardRt, "ทำออเดอร์ทั้งหมด!", new Vector2(0, -175), new Vector2(1000, 180), 104, new Color(1f, 0.86f, 0.3f));
        cineTitle.resizeTextForBestFit = true; cineTitle.resizeTextMinSize = 40; cineTitle.resizeTextMaxSize = 104;
        cineTitle.horizontalOverflow = HorizontalWrapMode.Wrap; cineTitle.verticalOverflow = VerticalWrapMode.Truncate; cineTitle.lineSpacing = 0.85f;
        Thick(cineName, new Color(0.35f, 0.15f, 0.05f)); Thick(cineTitle, new Color(0.55f, 0.18f, 0.04f));

        // result
        resultRt = PTVUI.Rect("Result", cineRoot, new Vector2(0, 80), new Vector2(1000, 300));
        cineResult = PTVUI.Txt("Big", resultRt, "", new Vector2(0, 50), new Vector2(1000, 160), 110, new Color(1f, 0.9f, 0.35f));
        cineResult.resizeTextForBestFit = true; cineResult.resizeTextMinSize = 40; cineResult.resizeTextMaxSize = 110;
        cineCount = PTVUI.Txt("Count", resultRt, "", new Vector2(0, -80), new Vector2(1000, 80), 52, Color.white);
        cineCount.resizeTextForBestFit = true; cineCount.resizeTextMinSize = 24; cineCount.resizeTextMaxSize = 52;
        Thick(cineResult, new Color(0.15f, 0.42f, 0.12f)); Thick(cineCount, new Color(0.15f, 0.3f, 0.1f));

        // letterbox + flash on top
        barTop = FullImage("BarTop", null, Color.black).rectTransform; barTop.anchorMin = new Vector2(0, 1); barTop.anchorMax = Vector2.one;
        barBot = FullImage("BarBottom", null, Color.black).rectTransform; barBot.anchorMin = Vector2.zero; barBot.anchorMax = new Vector2(1, 0);
        flashImg = FullImage("Flash", null, Color.clear);
        cardRt.gameObject.SetActive(false); resultRt.gameObject.SetActive(false);
        cineCanvas.gameObject.SetActive(false);
    }

    Image FullImage(string name, Sprite sprite, Color color)
    {
        var img = PTVUI.Img(name, cineRoot, null, Vector2.zero, Vector2.zero, color);
        img.sprite = sprite; img.raycastTarget = false; PTVUI.Stretch(img.rectTransform);
        return img;
    }

    static void Thick(Text t, Color edge)
    {
        var o = t.GetComponent<Outline>(); if (!o) o = t.gameObject.AddComponent<Outline>();
        o.effectColor = edge; o.effectDistance = new Vector2(6, -6);
        var s = t.gameObject.AddComponent<Shadow>(); s.effectColor = new Color(0, 0, 0, 0.45f); s.effectDistance = new Vector2(0, -10);
    }

    void SetupCard(PTVTikTokLive.Message who, string nm)
    {
        cineName.text = nm + (PTVLang.Thai ? "  ใช้สกิลสุดยอด!" : "  used the ULTIMATE skill!");
        cinePic.texture = null; cinePic.enabled = false; cineInitial.enabled = true;
        cineInitial.text = string.IsNullOrEmpty(nm) ? "?" : System.Globalization.StringInfo.GetNextTextElement(nm).ToUpperInvariant();
        if (who == null) return;
        var urls = new List<string>();
        if (!string.IsNullOrEmpty(who.profilePictureUrl)) urls.Add(who.profilePictureUrl);
        if (who.profilePictureUrls != null) urls.AddRange(who.profilePictureUrls);
        if (urls.Count > 0) StartCoroutine(PTVProfileCache.Get().Fetch(urls, tx =>
        {
            if (!tx || !cinePic) return;
            cinePic.texture = tx; cinePic.enabled = true; cineInitial.enabled = false;
            float asp = (float)tx.width / tx.height;
            cinePic.uvRect = asp > 1 ? new Rect((1 - 1 / asp) / 2, 0, 1 / asp, 1) : new Rect(0, (1 - asp) / 2, 1, asp);
        }));
    }

    // ------------------------------------------------------------------ procedural textures
    static Sprite ToSprite(Texture2D t) => Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
    static Sprite softSprite;
    static Sprite SoftSprite() => softSprite ? softSprite : (softSprite = ToSprite(SoftDot()));

    static Sprite MakeRays()
    {
        const int S = 512; var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[S * S];
        for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
        {
            float dx = x + 0.5f - S / 2f, dy = y + 0.5f - S / 2f, r = Mathf.Sqrt(dx * dx + dy * dy) / (S / 2f);
            float ang = Mathf.Atan2(dy, dx);
            float ray = Mathf.Clamp01((Mathf.Sin(ang * 14f) - 0.15f) * 3f);
            float a = ray * Mathf.Clamp01(1f - r) * Mathf.Clamp01(r * 4f) + Mathf.Clamp01(1f - r * 2.2f) * 0.6f;
            px[y * S + x] = new Color(1, 1, 1, Mathf.Clamp01(a));
        }
        t.SetPixels(px); t.Apply(); return ToSprite(t);
    }

    static Sprite MakeSpeedLines()
    {
        const int S = 512, N = 140; var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var rng = new System.Random(7); var on = new bool[N]; var inner = new float[N];
        for (int i = 0; i < N; i++) { on[i] = rng.NextDouble() < 0.45; inner[i] = 0.42f + 0.35f * (float)rng.NextDouble(); }
        var px = new Color[S * S];
        for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
        {
            float dx = x + 0.5f - S / 2f, dy = y + 0.5f - S / 2f, r = Mathf.Sqrt(dx * dx + dy * dy) / (S / 2f);
            float u = (Mathf.Atan2(dy, dx) / (2f * Mathf.PI) + 0.5f) * N; int bin = Mathf.Clamp((int)u, 0, N - 1);
            float centre = 1f - Mathf.Abs(u - bin - 0.5f) * 2f;
            float a = on[bin] && r > inner[bin] ? Mathf.Clamp01((r - inner[bin]) * 3f) * Mathf.Clamp01(centre * 2.5f - 0.6f) : 0f;
            px[y * S + x] = new Color(1, 1, 1, a);
        }
        t.SetPixels(px); t.Apply(); return ToSprite(t);
    }

    static Sprite MakeVignette()
    {
        const int S = 128; var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
        {
            float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f, r = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
            float a = Mathf.Clamp01((r - 0.45f) / 0.55f); t.SetPixel(x, y, new Color(1, 1, 1, a * a));
        }
        t.Apply(); return ToSprite(t);
    }
}
