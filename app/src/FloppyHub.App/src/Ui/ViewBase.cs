using Godot;

namespace FloppyHub.App.Ui;

/// <summary>Basis aller Ansichten im Hauptbereich.</summary>
public partial class ViewBase : VBoxContainer
{
    protected IAppHost Host { get; private set; } = null!;

    public virtual string Key => "";

    public void Init(IAppHost host)
    {
        Host = host;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 8);
        Build();
    }

    protected virtual void Build() { }

    /// <summary>Ansicht wird sichtbar.</summary>
    public virtual void OnShown() { }

    /// <summary>Alle paar Sekunden, solange sichtbar.</summary>
    public virtual void OnTick() { }
}
