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

    public PianoRollControl()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(24, 24, 28);
        RebuildLayout();
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

        float rollHeight = ClientSize.Height - KeyboardHeight;
        if (rollHeight < 0)
            return;

        DrawNotes(g, rollHeight);
        using var nowPen = new Pen(Color.White, 2);
        g.DrawLine(nowPen, 0, rollHeight, ClientSize.Width, rollHeight);
        DrawKeyboard(g, rollHeight);
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

    void DrawKeyboard(Graphics g, float top)
    {
        using var whiteBrush = new SolidBrush(Color.White);
        using var blackBrush = new SolidBrush(Color.Black);
        using var borderPen = new Pen(Color.Gray);

        foreach (var (_, layout) in keyLayout.Where(k => !k.Value.Black))
        {
            var rect = new RectangleF(layout.X, top, layout.Width, KeyboardHeight);
            g.FillRectangle(whiteBrush, rect);
            g.DrawRectangle(borderPen, rect.X, rect.Y, rect.Width, rect.Height);
        }
        foreach (var (_, layout) in keyLayout.Where(k => k.Value.Black))
            g.FillRectangle(blackBrush, layout.X, top, layout.Width, KeyboardHeight * 0.6f);
    }
}
