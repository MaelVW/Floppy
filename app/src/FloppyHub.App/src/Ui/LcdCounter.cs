using FloppyHub.App.Skin;
using Godot;

namespace FloppyHub.App.Ui;

/// <summary>Rote Sieben-Segment-Anzeige wie beim alten Minesweeper.</summary>
public partial class LcdCounter : Control
{
    private static readonly Color On = new("#ff3322");
    private static readonly Color Off = new("#3a0c08");
    private static readonly Color Back = new("#0c0605");

    // Segmente a-g je Ziffer
    private static readonly bool[][] Digits =
    [
        [true, true, true, true, true, true, false],      // 0
        [false, true, true, false, false, false, false],  // 1
        [true, true, false, true, true, false, true],     // 2
        [true, true, true, true, false, false, true],     // 3
        [false, true, true, false, false, true, true],    // 4
        [true, false, true, true, false, true, true],     // 5
        [true, false, true, true, true, true, true],      // 6
        [true, true, true, false, false, false, false],   // 7
        [true, true, true, true, true, true, true],       // 8
        [true, true, true, true, false, true, true],      // 9
    ];

    private int _value;

    public LcdCounter() : this(3) { }

    public LcdCounter(int digits)
    {
        DigitCount = digits;
        CustomMinimumSize = new Vector2(digits * 14 + 6, 28);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public int DigitCount { get; }

    /// <summary>Stoppuhr: Wert = Sekunden, Anzeige MM:SS (braucht 4 Stellen).</summary>
    public bool TimeMode { get; init; }

    public int Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, 0, TimeMode ? 99 * 60 + 59 : (int)Math.Pow(10, DigitCount) - 1);
            if (clamped == _value) return;
            _value = clamped;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var p = Palette.Current;
        var line = UiScale.Line;
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r, Back);
        // eingelassener Rahmen
        DrawRect(new Rect2(0, 0, Size.X, line), p.Shadow);
        DrawRect(new Rect2(0, 0, line, Size.Y), p.Shadow);
        DrawRect(new Rect2(0, Size.Y - line, Size.X, line), p.Highlight);
        DrawRect(new Rect2(Size.X - line, 0, line, Size.Y), p.Highlight);

        var text = TimeMode ? $"{_value / 60:00}{_value % 60:00}".PadLeft(DigitCount, '0') : _value.ToString().PadLeft(DigitCount, '0');
        var cellW = (Size.X - 6) / DigitCount;
        for (var i = 0; i < DigitCount; i++)
            DrawDigit(new Rect2(3 + i * cellW, 3, cellW, Size.Y - 6), text[i] - '0');

        if (TimeMode)
        {
            // Doppelpunkt zwischen Minuten und Sekunden
            var x = 3 + (DigitCount - 2) * cellW;
            var dot = MathF.Max(2f, cellW * 0.16f);
            DrawRect(new Rect2(x - dot / 2, Size.Y * 0.36f - dot / 2, dot, dot), On);
            DrawRect(new Rect2(x - dot / 2, Size.Y * 0.64f - dot / 2, dot, dot), On);
        }
    }

    private void DrawDigit(Rect2 cell, int digit)
    {
        var segs = Digits[digit];
        var t = MathF.Max(2f, cell.Size.X * 0.18f);
        float x0 = cell.Position.X + 1.5f, x1 = cell.End.X - 1.5f;
        float y0 = cell.Position.Y + 0.5f, y2 = cell.End.Y - 0.5f, y1 = (y0 + y2) / 2;
        var w = x1 - x0;
        var hTop = y1 - y0;
        var hBottom = y2 - y1;

        void H(int i, float y) => DrawRect(new Rect2(x0 + t * 0.6f, y - t / 2, w - t * 1.2f, t), segs[i] ? On : Off);
        void V(int i, float x, float top, float height) => DrawRect(new Rect2(x - t / 2, top + t * 0.6f, t, height - t * 1.2f), segs[i] ? On : Off);

        H(0, y0 + t / 2);            // a
        V(1, x1 - t / 2, y0, hTop);  // b
        V(2, x1 - t / 2, y1, hBottom); // c
        H(3, y2 - t / 2);            // d
        V(4, x0 + t / 2, y1, hBottom); // e
        V(5, x0 + t / 2, y0, hTop);  // f
        H(6, y1);                    // g
    }
}
