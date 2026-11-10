using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Places the clay-style models made in Blender (Assets/PTVFOOD/Resources/Models/*.fbx, sources in PTVFOOD/BlenderSource)
/// and builds the stall layout. Logical points the game needs (grill slots, flames, tray item anchors,
/// customer spots, mortar bits) are plain empties created here.
/// </summary>
public static class SquidModels
{
    public static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
    public static readonly Dictionary<string, Mesh> MeshCache = new Dictionary<string, Mesh>();
    /// <summary>Editor hooks used by the scene builder (materials / meshes saved as assets).</summary>
    public static System.Func<string, Material> LoadSaved;
    public static System.Func<string, Mesh> LoadMesh;

    public const float TopY = 1.16f;   // counter top surface

    public static readonly Color SquidRaw = new Color(0.97f, 0.73f, 0.66f);
    public static readonly Color SquidCooked = new Color(0.96f, 0.6f, 0.3f);
    public static readonly Color SquidBurnt = new Color(0.3f, 0.17f, 0.1f);

    // ------------------------------------------------------------------ materials
    static readonly Dictionary<string, Color> Glow = new Dictionary<string, Color>
    {
        { "CoalHot", new Color(1.3f, 0.45f, 0.1f) }, { "CoalRed", new Color(0.8f, 0.18f, 0.05f) },
        { "FlameOrange", new Color(1.8f, 0.7f, 0.1f) }, { "FlameYellow", new Color(2.0f, 1.6f, 0.4f) },
        { "Bulb", new Color(2.2f, 1.8f, 0.7f) },
    };

    public static Material Mat(string key, Color c, float smooth = 0.25f, Color? emission = null, bool particles = false)
    {
        if (Cache.TryGetValue(key, out var m) && m) return m;
        if (LoadSaved != null) { m = LoadSaved(key); if (m) { Cache[key] = m; return m; } }
        var sh = Shader.Find(particles ? "Universal Render Pipeline/Particles/Unlit" : "Universal Render Pipeline/Lit");
        if (!sh) sh = Shader.Find("Standard");
        m = new Material(sh) { name = key };
        m.SetColor("_BaseColor", c); m.color = c;
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (!emission.HasValue && Glow.TryGetValue(key, out var g)) emission = g;
        if (emission.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; }
        if (particles)
        {
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 3000;
        }
        Cache[key] = m;
        return m;
    }

    /// <summary>Swap the FBX-imported materials for shared URP/Lit ones (same colour, soft clay smoothness, glow where needed).</summary>
    static void FixMaterials(GameObject go)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (!mats[i]) continue;
                string key = mats[i].name.Replace(" (Instance)", "");
                float smooth = key == "GrillMetal" || key == "IceCube" || key == "Eye" ? 0.75f : 0.22f;
                mats[i] = Mat(key, mats[i].HasProperty("_BaseColor") ? mats[i].GetColor("_BaseColor") : mats[i].color, smooth);
            }
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
    }

    /// <summary>Instantiate a Blender model from Resources/Models.</summary>
    public static GameObject Model(string name, Transform parent, Vector3 pos, float scale = 1f, float yaw = 0f)
    {
        var prefab = Resources.Load<GameObject>("Models/" + name);
        GameObject go;
        if (prefab)
        {
            go = Object.Instantiate(prefab);
            go.name = name;
            FixMaterials(go);
        }
        else { go = new GameObject(name + " (missing model)"); Debug.LogWarning("[PTVFOOD] model not found: Models/" + name); }
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(0, yaw, 0) * go.transform.localRotation;
        go.transform.localScale = go.transform.localScale * scale;
        return go;
    }

    public static Transform Deep(Transform t, string name)
    {
        if (!t) return null;
        if (t.name == name) return t;
        foreach (Transform c in t) { var r = Deep(c, name); if (r) return r; }
        return null;
    }

    public static GameObject Empty(string name, Transform parent, Vector3 pos, Vector3? euler = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos; go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
        return go;
    }

    /// <summary>Wraps a model in an empty so game code can move / rotate / scale the root freely.</summary>
    static GameObject Wrapped(string model, Transform parent, string name)
    {
        var root = Empty(name, parent, Vector3.zero);
        Model(model, root.transform, Vector3.zero);
        return root;
    }

    // ------------------------------------------------------------------ props
    /// <summary>Squid on a skewer: length along +Z (fins at +Z), grilled side "StripesB" on top.</summary>
    public static GameObject Squid(Transform parent, string name = "Squid")
    {
        var root = Wrapped("Squid", parent, name);
        CookSquid(root, 0f, 0f, 0f, rawBlock ?? (rawBlock = new MaterialPropertyBlock()));   // starts raw: pink, no grill marks
        return root;
    }

    /// <summary>Tint a squid: a / b = how cooked each side is (0..1), burnt 0..1.</summary>
    public static void CookSquid(GameObject squid, float a, float b, float burnt, MaterialPropertyBlock mpb, float top = -1f)
    {
        if (!squid) return;
        // colour of the side you can see: top >= 0 = how cooked the upward side is (the grill passes it in)
        float k = top >= 0f ? Mathf.Clamp01(top) : Mathf.Clamp01((a + b) * 0.5f + 0.15f * Mathf.Max(a, b));
        var c = Color.Lerp(SquidRaw, SquidCooked, k);
        c = Color.Lerp(c, SquidBurnt, Mathf.Clamp01(burnt));
        mpb.SetColor("_BaseColor", c);
        foreach (var r in squid.GetComponentsInChildren<Renderer>()) if (r.name.StartsWith("S_")) r.SetPropertyBlock(mpb);
        var sa = Deep(squid.transform, "StripesA"); var sb = Deep(squid.transform, "StripesB"); var eyes = Deep(squid.transform, "Eyes");
        if (sa) sa.localScale = Vector3.one * Mathf.Max(0.001f, Mathf.Clamp01(a));
        if (sb) sb.localScale = Vector3.one * Mathf.Max(0.001f, Mathf.Clamp01(b));
        if (eyes) eyes.gameObject.SetActive(a + b < 0.3f);
    }

    static MaterialPropertyBlock rawBlock;

    public static void HideStick(GameObject squid) { var s = Deep(squid.transform, "Stick"); if (s) s.gameObject.SetActive(false); }

    public static GameObject SauceCup(Transform parent, string name = "Sauce") => Wrapped("SauceCup", parent, name);
    public static GameObject Chili(Transform parent, string name = "Chili") => Wrapped("Chili", parent, name);
    public static GameObject Lime(Transform parent, string name = "Lime") => Wrapped("Lime", parent, name);
    public static GameObject Garlic(Transform parent, string name = "Garlic") => Wrapped("Garlic", parent, name);

    // ------------------------------------------------------------------ people (face -Z; customers are turned around by the game)
    public static GameObject Person(Transform parent, string name, Color shirt, Color hair, Color skin, bool apron, float scale = 1f)
    {
        var root = Empty(name, parent, Vector3.zero);
        var m = Model(apron ? "Vendor" : "Customer", root.transform, Vector3.zero, scale);
        if (!apron)
        {
            var mpb = new MaterialPropertyBlock();
            foreach (var r in m.GetComponentsInChildren<Renderer>())
            {
                string mn = r.sharedMaterial ? r.sharedMaterial.name : "";
                if (mn.StartsWith("Shirt")) { mpb.SetColor("_BaseColor", shirt); r.SetPropertyBlock(mpb); }
                else if (mn.StartsWith("Hair")) { mpb.SetColor("_BaseColor", hair); r.SetPropertyBlock(mpb); }
                else if (mn.StartsWith("Skin")) { mpb.SetColor("_BaseColor", skin); r.SetPropertyBlock(mpb); }
            }
        }
        return root;
    }

    // ------------------------------------------------------------------ the stall
    public static Transform BuildStall(Transform parent)
    {
        var root = Empty("Stall", parent, Vector3.zero).transform;
        float Y = TopY;
        Model("Stall", root, Vector3.zero).name = "Room";   // table, wooden wall with the sign, tiled floor

        // ---- grill + logical points
        var grill = Empty("Grill", root, new Vector3(-0.35f, Y, 1.25f)).transform;
        Model("Grill", grill, Vector3.zero);
        Empty("Flames", grill, new Vector3(0, 0.42f, 0));   // no flame models (just glowing coals + smoke)
        var slots = Empty("GrillSlots", grill, new Vector3(0, 0.66f, 0)).transform;
        for (int i = 0; i < 4; i++) Empty("Slot" + i, slots, new Vector3(-0.9f + i * 0.6f, 0, 0));

        // ---- ice box (portrait, raw squid lying on the ice)
        var ice = Empty("IceBox", root, new Vector3(-2.4f, Y, 1.2f)).transform;
        Model("IceBox", ice, Vector3.zero, 1f, 90f);
        var raw = Squid(ice, "RawSquid"); raw.transform.localPosition = new Vector3(0.0f, 0.52f, 0.05f);
        raw.transform.localRotation = Quaternion.Euler(-12, 12, 0); raw.transform.localScale = Vector3.one * 0.9f;
        HideStick(raw);

        // ---- mortar (pestle / paste are inside the model)
        var mortar = Empty("Mortar", root, new Vector3(2.3f, Y, 1.6f)).transform;
        Model("Mortar", mortar, Vector3.zero);
        Empty("Bits", mortar, new Vector3(0, 0.45f, 0));

        // ---- ingredient tray: 2 chili, 2 lime halves, 3 garlic
        var tray = Empty("Ingredients", root, new Vector3(2.3f, Y, 0.3f)).transform;
        Model("IngredientTray", tray, Vector3.zero);
        for (int i = 0; i < 2; i++) { var c = Chili(tray); c.transform.localPosition = new Vector3(-0.3f + i * 0.16f, 0.07f, 0.12f); c.transform.localRotation = Quaternion.Euler(0, 180 + 10 - i * 8, 0); }
        for (int i = 0; i < 2; i++) { var l = Lime(tray); l.transform.localPosition = new Vector3(0.1f + i * 0.24f, 0.08f, 0.16f); l.transform.localRotation = Quaternion.Euler(-30, 0, 0); }
        for (int i = 0; i < 3; i++) { var g = Garlic(tray); g.transform.localPosition = new Vector3(-0.24f + i * 0.22f, 0.05f, -0.2f); g.transform.localRotation = Quaternion.Euler(0, 160 + i * 25, 0); }

        // ---- stack of plates

        // ---- boards for finished food
        var sTray = Empty("SquidTray", root, new Vector3(-1.45f, Y, -0.8f)).transform;
        Model("BoardWood", sTray, Vector3.zero);
        Empty("Items", sTray, new Vector3(0, 0.1f, 0));
        var cTray = Empty("SauceTray", root, new Vector3(1.45f, Y, -0.8f)).transform;
        Model("BoardTeal", cTray, Vector3.zero);
        Empty("Items", cTray, new Vector3(0, 0.09f, 0));

        // ---- vendor behind the table
        var vendor = Person(root, "Vendor", Color.white, Color.white, Color.white, true, 1.0f);
        vendor.transform.localPosition = new Vector3(0f, 0, 2.95f);

        // ---- customers stand right at the table front (only their heads show at the bottom)
        var spots = Empty("CustomerSpots", root, Vector3.zero).transform;
        float[] xs = { -2.1f, -0.7f, 0.7f, 2.1f };
        for (int i = 0; i < 4; i++) Empty("Spot" + i, spots, new Vector3(xs[i], 0, -2.7f));
        for (int i = 0; i < 4; i++) Empty("Spot" + (i + 4), spots, new Vector3(xs[i] + 0.7f, 0, -3.5f));
        return root;
    }
}
