using UnityEngine.UI;

/// <summary>uGUI Text that follows the game language (PTVLang). Assign Thai source text as usual.</summary>
public class PTVText : Text
{
    string source;
    public bool translate = true;
    public override string text
    {
        get => base.text;
        set { source = value; base.text = translate ? PTVLang.T(value) : value; }
    }
    protected override void OnEnable()
    {
        base.OnEnable();
        PTVLang.Changed += Reapply;
        if (source != null) Reapply();
    }
    protected override void OnDisable() { PTVLang.Changed -= Reapply; base.OnDisable(); }
    void Reapply() { if (source != null) base.text = translate ? PTVLang.T(source) : source; }
}
