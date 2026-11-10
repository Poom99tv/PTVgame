using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Legacy uGUI Text has no Thai shaping: a tone mark (่ ้ ๊ ๋ ์) written after an upper vowel
/// (ิ ี ึ ื ั ็ ํ ำ) is drawn on top of the vowel and disappears. This effect lifts those tone-mark
/// quads above the vowel. Add it right after the Text, before Outline / Shadow.
/// </summary>
[RequireComponent(typeof(Text))]
public sealed class PTVThaiShift : BaseMeshEffect
{
    public float lift = 0.26f;   // x fontSize
    static readonly List<UIVertex> verts = new List<UIVertex>();

    static bool Upper(char c) => c == 'ั' || (c >= 'ิ' && c <= 'ื') || c == '็' || c == 'ํ';
    static bool Tone(char c) => c >= '่' && c <= '์';

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive()) return;
        var t = graphic as Text;
        if (!t || string.IsNullOrEmpty(t.text)) return;
        string s = t.text;
        bool any = false;
        for (int i = 1; i < s.Length; i++) if (Tone(s[i]) && (Upper(s[i - 1]) || (i + 1 < s.Length && s[i + 1] == 'ำ'))) { any = true; break; }
        if (!any) return;

        // map characters -> quads (whitespace and rich-text tags produce no quad)
        var quadOf = new List<int>(s.Length);
        int q = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (t.supportRichText && c == '<')
            {
                int end = s.IndexOf('>', i);
                if (end > i && IsTag(s.Substring(i + 1, end - i - 1)))
                { for (int k = i; k <= end; k++) quadOf.Add(-1); i = end; continue; }
            }
            if (char.IsWhiteSpace(c) || char.IsControl(c)) { quadOf.Add(-1); continue; }
            quadOf.Add(q++);
        }
        vh.GetUIVertexStream(verts);
        if (verts.Count != q * 6) { verts.Clear(); return; }   // layout we don't understand (e.g. truncated) -> leave as is

        float dy = t.fontSize * lift;
        bool changed = false;
        for (int i = 1; i < s.Length; i++)
        {
            if (!Tone(s[i])) continue;
            bool high = Upper(s[i - 1]) || (i + 1 < s.Length && s[i + 1] == 'ำ');
            if (!high || quadOf[i] < 0) continue;
            int b = quadOf[i] * 6;
            for (int k = 0; k < 6; k++) { var v = verts[b + k]; v.position.y += dy; verts[b + k] = v; }
            changed = true;
        }
        if (changed) { vh.Clear(); vh.AddUIVertexTriangleStream(verts); }
        verts.Clear();
    }

    static bool IsTag(string inner)
    {
        string x = inner.TrimStart('/').ToLowerInvariant();
        return x == "b" || x == "i" || x.StartsWith("color") || x.StartsWith("size") || x.StartsWith("material") || x.StartsWith("quad") || x == "";
    }
}
