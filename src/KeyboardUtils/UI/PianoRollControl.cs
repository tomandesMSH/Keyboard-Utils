using System.Drawing.Drawing2D;

namespace KeyboardUtils;

/// <summary>Vertical "falling notes" piano roll (Synthesia-style): notes scroll down toward a keyboard strip at the bottom.</summary>
sealed class PianoRollControl : Control
{
    static readonly Color[] PartColors =
    [
        Color.FromArgb(66, 133, 244), Color.FromArgb(219, 68, 55), Color.FromArgb(244, 180, 0),
        Color.FromArgb(15, 157, 88), Color.FromArgb(171, 71, 188), Color.FromArgb(255, 112, 67),
    ];

    const int KeyboardHeight = 60;
    const double SecondsVisible = 4.0;

    readonly record struct KeyLayout(float X, float Width, bool Black);

    Score? score;
    double currentSeconds;
    int lowestKey = 48; // C3
    int highestKey = 84; // C6
    Dictionary<int, KeyLayout> keyLayout = [];
    readonly HashSet<int> heldKeys = [];

    /// <summary>When true, the roll area is hidden and the keyboard fills the whole control (note-picker use).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool FillKeyboard { get; set; }

    /// <summary>When true, clicking a key raises <see cref="KeySelected"/>.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Clickable { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int? SelectedKey { get; set; }

    public event Action<int>? KeySelected;

    readonly System.Windows.Forms.Timer flashTimer = new() { Interval = 400 };

    public PianoRollControl()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(24, 24, 28);
        flashTimer.Tick += (_, _) =>
        {
            flashTimer.Stop();
            SelectedKey = null;
            Invalidate();
        };
        RebuildLayout();
    }

    /// <summary>Briefly highlights a key, e.g. when a macro key plays it. Clears itself after <paramref name="durationMs"/>.</summary>
    public void FlashKey(int key, int durationMs = 400)
    {
        SelectedKey = key;
        Invalidate();
        flashTimer.Interval = durationMs;
        flashTimer.Stop();
        flashTimer.Start();
    }

    /// <summary>Lights a key for as long as it's held (a note heard from the microphone/MIDI input), unlike <see cref="FlashKey"/>.</summary>
    public void SetKeyHeld(int key, bool held)
    {
        if (held ? !heldKeys.Add(key) : !heldKeys.Remove(key))
            return;
        // A heard note outside the visible range would otherwise light up nothing.
        if (held && (key < lowestKey || key > highestKey))
        {
            lowestKey = Math.Max(0, Math.Min(lowestKey, key - 2));
            highestKey = Math.Min(127, Math.Max(highestKey, key + 2));
            RebuildLayout();
        }
        Invalidate();
    }

    public void ClearHeldKeys()
    {
        heldKeys.Clear();
        Invalidate();
    }

    public void SetScore(Score? newScore)
    {
        score = newScore;
        currentSeconds = 0;
        var pitches = score?.Parts.SelectMany(p => p.Notes).Select(n => n.Pitch).ToList() ?? [];
        if (pitches.Count > 0)
        {
            lowestKey = Math.Max(0, pitches.Min() - 2);
            highestKey = Math.Min(127, pitches.Max() + 2);
        }
        RebuildLayout();
        Invalidate();
    }

    public void SetCurrentTime(double seconds)
    {
        currentSeconds = seconds;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RebuildLayout();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            flashTimer.Dispose();
        base.Dispose(disposing);
    }

    static bool IsBlackKey(int note) => (note % 12) is 1 or 3 or 6 or 8 or 10;

    void RebuildLayout()
    {
        var layout = new Dictionary<int, KeyLayout>();
        int whiteCount = Enumerable.Range(lowestKey, highestKey - lowestKey + 1).Count(n => !IsBlackKey(n));
        float whiteWidth = whiteCount > 0 ? (float)Math.Max(1, ClientSize.Width) / whiteCount : 20f;
        float blackWidth = whiteWidth * 0.6f;

        var whiteX = new Dictionary<int, float>();
        float x = 0;
        for (int n = lowestKey; n <= highestKey; n++)
        {
            if (IsBlackKey(n))
                continue;
            whiteX[n] = x;
            layout[n] = new KeyLayout(x, whiteWidth, false);
            x += whiteWidth;
        }
        for (int n = lowestKey; n <= highestKey; n++)
        {
            if (!IsBlackKey(n))
                continue;
            int prevWhite = n - 1;
            while (prevWhite >= lowestKey && IsBlackKey(prevWhite))
                prevWhite--;
            float baseX = whiteX.TryGetValue(prevWhite, out var wx) ? wx : 0;
            layout[n] = new KeyLayout(baseX + whiteWidth - blackWidth / 2, blackWidth, true);
        }

        keyLayout = layout;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        float keyboardHeight = FillKeyboard ? ClientSize.Height : KeyboardHeight;
        float rollHeight = ClientSize.Height - keyboardHeight;
        if (rollHeight < 0)
            return;

        if (!FillKeyboard)
        {
            DrawNotes(g, rollHeight);
            using var nowPen = new Pen(Color.White, 2);
            g.DrawLine(nowPen, 0, rollHeight, ClientSize.Width, rollHeight);
        }
        DrawKeyboard(g, rollHeight, keyboardHeight);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Clickable || e.Button != MouseButtons.Left)
            return;
        if (HitTestKey(e.Location) is not { } key)
            return;
        KeySelected?.Invoke(key);
    }

    int? HitTestKey(Point p)
    {
        // Black keys are drawn on top, so test them first.
        foreach (var (note, layout) in keyLayout.Where(k => k.Value.Black))
            if (p.X >= layout.X && p.X < layout.X + layout.Width)
                return note;
        foreach (var (note, layout) in keyLayout.Where(k => !k.Value.Black))
            if (p.X >= layout.X && p.X < layout.X + layout.Width)
                return note;
        return null;
    }

    void DrawNotes(Graphics g, float rollHeight)
    {
        if (score == null)
            return;

        float pixelsPerSecond = (float)(rollHeight / SecondsVisible);
        for (int partIndex = 0; partIndex < score.Parts.Count; partIndex++)
        {
            var part = score.Parts[partIndex];
            if (part.Muted)
                continue;

            using var brush = new SolidBrush(PartColors[partIndex % PartColors.Length]);
            foreach (var note in part.Notes)
            {
                double noteEnd = note.StartSeconds + note.DurationSeconds;
                if (noteEnd < currentSeconds || note.StartSeconds > currentSeconds + SecondsVisible)
                    continue;
                if (!keyLayout.TryGetValue(note.Pitch, out var layout))
                    continue;

                float yBottom = rollHeight - (float)((note.StartSeconds - currentSeconds) * pixelsPerSecond);
                float yTop = rollHeight - (float)((noteEnd - currentSeconds) * pixelsPerSecond);
                float height = Math.Max(3, yBottom - yTop);
                g.FillRectangle(brush, layout.X + 1, yTop, Math.Max(2, layout.Width - 2), height);
            }
        }
    }

    void DrawKeyboard(Graphics g, float top, float height)
    {
        using var whiteBrush = new SolidBrush(Color.White);
        using var blackBrush = new SolidBrush(Color.Black);
        using var selectedBrush = new SolidBrush(Color.FromArgb(66, 133, 244));
        using var heldBrush = new SolidBrush(Color.FromArgb(255, 152, 0));
        using var borderPen = new Pen(Color.Gray);

        Brush BrushFor(int note, Brush rest) => heldKeys.Contains(note) ? heldBrush : note == SelectedKey ? selectedBrush : rest;

        foreach (var (note, layout) in keyLayout.Where(k => !k.Value.Black))
        {
            var rect = new RectangleF(layout.X, top, layout.Width, height);
            g.FillRectangle(BrushFor(note, whiteBrush), rect);
            g.DrawRectangle(borderPen, rect.X, rect.Y, rect.Width, rect.Height);
        }
        foreach (var (note, layout) in keyLayout.Where(k => k.Value.Black))
            g.FillRectangle(BrushFor(note, blackBrush), layout.X, top, layout.Width, height * 0.6f);
    }
}
