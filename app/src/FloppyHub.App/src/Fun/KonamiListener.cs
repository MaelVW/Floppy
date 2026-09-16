using Godot;

namespace FloppyHub.App.Fun;

/// <summary>Lauscht auf hoch, hoch, runter, runter, links, rechts, links, rechts, B, A.</summary>
public partial class KonamiListener : Node
{
    private static readonly Key[] Code =
        [Key.Up, Key.Up, Key.Down, Key.Down, Key.Left, Key.Right, Key.Left, Key.Right, Key.B, Key.A];

    private int _position;

    public event Action? Activated;

    public override void _Input(InputEvent e)
    {
        if (!EasterEggs.Enabled || e is not InputEventKey { Pressed: true, Echo: false } key) return;

        if (key.Keycode == Code[_position])
        {
            _position++;
            if (_position < Code.Length) return;
            _position = 0;
            Activated?.Invoke();
        }
        else
        {
            _position = key.Keycode == Code[0] ? 1 : 0;
        }
    }
}
