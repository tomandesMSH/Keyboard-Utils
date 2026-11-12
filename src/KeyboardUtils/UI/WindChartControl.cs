using System.Drawing.Drawing2D;

namespace KeyboardUtils;

/// <summary>
/// A wind instrument's fingering chart in place of the keyboard: big, the note to play now (in learning: the one
/// expected; in playback: the one sounding; otherwise what's played, or the next note), and smaller, the next notes.
/// Click the big chart to hear its note. Same surface as <see cref="FretboardControl"/>, so MainForm treats them alike.
/// </summary>
sealed class WindChartControl : Control, IThemed
{
    static readonly Color HintColor = Color.FromArgb(70, 130, 230);
    static readonly Color HeldColor = Color.FromArgb(255, 152, 0);
    const double LookAheadSeconds = 4;
    const int NextCount = 3;

    Instrument instrument = Instrument.Piano;
    Score? score;
    double currentSeconds;
    readonly HashSet<int> held = [];
    readonly System.Windows.Forms.Timer flashTimer = new();
    (int Pitch, Color Color)? flash;
    RectangleF bigBox;
    int? bigPitch;

    public WindChartControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        flashTimer.Tick += (_, _) =>
        {
            flashTimer.Stop();
            flash = null;
            Invalidate();
        };
        AppTheme.Changed += Invalidate;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            flashTimer.Dispose();
            AppTheme.Changed -= Invalidate;
        }
        base.Dispose(disposing);
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Instrument Instrument
    {
        get => instrument;
        set
        {
            instrument = value;
            Invalidate();
        }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public RecorderSystem RecorderSystem { get; set { field = value; Invalidate(); } }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public LearningSession? Learning { get; set; }

    /// <summary>The big chart was clicked: its note (sounding pitch).</summary>
    public event Action<int>? NoteClicked;

    /// <summary>Whether the instrument has a chart (otherwise the keyboard stays).</summary>
    public static bool Covers(Instrument instrument) => WindFingerings.ChartFor(instrument) != null;

    public void SetScore(Score? newScore)
    {
        score = newScore;
        currentSeconds = 0;
        Invalidate();
    }

    public void SetCurrentTime(double seconds)
    {
        if (seconds == currentSeconds)
            return;
        currentSeconds = seconds;
        Invalidate();
    }

    public void SetKeyHeld(int pitch, bool down)
    {
        if (down ? held.Add(pitch) : held.Remove(pitch))
            Invalidate();
    }

    public void ClearHeldKeys()
    {
        held.Clear();
        Invalidate();
    }

    public void FlashKey(int pitch, int durationMs = 400, Color? color = null)
    {
        flash = (pitch, color ?? HintColor);
        flashTimer.Stop();
        flashTimer.Interval = Math.Max(1, durationMs);
        flashTimer.Start();
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && bigPitch is { } pitch && bigBox.Contains(e.Location))
            NoteClicked?.Invoke(pitch);
    }

    // --- Which notes ----------------------------------------------------------------------------------

    /// <summary>The note for the big chart, and its colour.</summary>
    (int Pitch, Color Color)? NowNote()
    {
        if (flash is { } f)
            return f;
        if (Learning is { } learning && learning.ExpectedKeys().FirstOrDefault() is var expected && expected > 0)
            return (expected, HintColor);
        if (score != null && Learning == null)
        {
            for (int part = 0; part < score.Parts.Count; part++)
            {
                if (score.Parts[part].Muted)
                    continue;
                var sounding = score.Parts[part].Notes.FirstOrDefault(n => n.StartSeconds <= currentSeconds && n.StartSeconds + n.DurationSeconds > currentSeconds);
                if (sounding != null)
                    return (sounding.Pitch, PianoRollControl.PartColor(part));
            }
        }
        if (held.Count > 0)
            return (held.Max(), HeldColor);
        return null;
    }

    /// <summary>The next few different notes coming (after the one shown big).</summary>
    List<int> NextNotes(int? now)
    {
        var next = new List<int>();
        if (score == null)
            return next;
        bool practiced(int part) => Learning?.PracticedParts.Contains(part) ?? !score.Parts[part].Muted;
        var coming = score.Parts
            .SelectMany((p, i) => practiced(i) ? p.Notes : [])
            .Where(n => n.StartSeconds > currentSeconds + 0.02 && n.StartSeconds <= currentSeconds + LookAheadSeconds)
            .OrderBy(n => n.StartSeconds);
        int? last = now;
        foreach (var note in coming)
        {
            if (note.Pitch == last)
                continue;
            next.Add(note.Pitch);
            last = note.Pitch;
            if (next.Count == NextCount)
                break;
        }
        return next;
    }

    // --- Drawing -------------------------------------------------------------------------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(AppTheme.Paper);
        if (WindFingerings.ChartFor(instrument) is not { } chart || Width < 120 || Height < 80)
            return;

        var now = NowNote();
        var next = NextNotes(now?.Pitch);
        if (now == null && next.Count > 0)
        {
            // Nothing sounding yet: the first note to come, big.
            now = (next[0], HintColor);
            next.RemoveAt(0);
        }

        float pad = 12;
        float bigWidth = Math.Min(Width * 0.42f, (Height - 2 * pad) * (chart.InstrumentId == "recorder" ? 0.62f : 1.1f));
        bigBox = new RectangleF(pad, pad, bigWidth, Height - 2 * pad);
        bigPitch = now?.Pitch;
        using var labelFont = new Font("Segoe UI", 9f);
        using var subtle = new SolidBrush(AppTheme.SubtleText);
        g.DrawString(T("Teď", "Now"), labelFont, subtle, bigBox.Left, bigBox.Top - 2);
        DrawNote(g, chart, now?.Pitch, now?.Color ?? AppTheme.SubtleText, Inset(bigBox, 0, 18), big: true);

        if (next.Count > 0)
        {
            float left = bigBox.Right + 3 * pad;
            float each = Math.Min((Width - left - pad) / NextCount, bigWidth * 0.7f);
            g.DrawString(T("Pak", "Next"), labelFont, subtle, left, bigBox.Top - 2);
            for (int i = 0; i < next.Count; i++)
            {
                var box = new RectangleF(left + i * each, bigBox.Top + 18 + bigBox.Height * 0.15f, each - pad, (bigBox.Height - 18) * 0.85f);
                DrawNote(g, chart, next[i], AppTheme.SubtleText, box, big: false);
            }
        }
    }

    static RectangleF Inset(RectangleF r, float x, float top) => new(r.Left + x, r.Top + top, r.Width - 2 * x, r.Height - top);

    /// <summary>The note's written name on top, its fingering under it.</summary>
    void DrawNote(Graphics g, WindChart chart, int? pitch, Color accent, RectangleF box, bool big)
    {
        using var nameFont = new Font("Segoe UI Semibold", big ? 18f : 12f);
        using var hintFont = new Font("Segoe UI", big ? 9f : 8f);
        using var accentBrush = new SolidBrush(accent);
        using var subtle = new SolidBrush(AppTheme.SubtleText);
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };
        float nameHeight = big ? 34 : 24;
        string name = pitch is { } p ? Tuning.Name(instrument.Written(p)) : "–";
        g.DrawString(name, nameFont, accentBrush, new RectangleF(box.Left, box.Top, box.Width, nameHeight), center);

        var fingering = pitch is { } p2 ? WindFingerings.For(instrument, p2, RecorderSystem) : null;
        var area = new RectangleF(box.Left, box.Top + nameHeight, box.Width, box.Height - nameHeight - (big ? 22 : 16));
        // Valves: a row of buttons, just under the name (the hint right below them).
        if (chart.InstrumentId != "recorder")
            area.Height = Math.Min(area.Height, area.Width * 0.5f);
        if (pitch != null && fingering == null)
        {
            g.DrawString(T("mimo rozsah", "out of range"), hintFont, subtle, new RectangleF(area.Left, area.Top + area.Height / 2 - 8, area.Width, 20), center);
            return;
        }
        if (chart.InstrumentId == "recorder")
            DrawRecorder(g, chart, fingering, area, accent);
        else
            DrawValves(g, chart, fingering, area, accent);
        if (fingering?.Hint is { } hint)
            g.DrawString(hint, hintFont, subtle, new RectangleF(box.Left - 20, area.Bottom + 2, box.Width + 40, 20), center);
    }

    void DrawRecorder(Graphics g, WindChart chart, WindFingering? fingering, RectangleF area, Color accent)
    {
        // The body: a tall rounded tube, the mouthpiece narrowing at the top.
        float w = Math.Min(area.Width * 0.34f, area.Height * 0.16f);
        float cx = area.Left + area.Width / 2;
        var body = new RectangleF(cx - w / 2, area.Top, w, area.Height);
        using (var path = Rounded(body, w / 2))
        using (var fill = new SolidBrush(AppTheme.Dark ? Color.FromArgb(70, 62, 52) : Color.FromArgb(232, 214, 180)))
        using (var edge = new Pen(AppTheme.Dark ? Color.FromArgb(120, 110, 96) : Color.FromArgb(170, 140, 100), 1.2f))
        {
            g.FillPath(fill, path);
            g.DrawPath(edge, path);
        }
        float r = Math.Clamp(w * 0.28f, 4, 14);
        for (int i = 0; i < chart.Keys.Count; i++)
        {
            var key = chart.Keys[i];
            var state = fingering?.Keys[i] ?? KeyState.Open;
            float x = key.Kind == KeyKind.Thumb ? body.Left - r * 2.4f : cx;
            float y = area.Top + key.Y * area.Height;
            if (key.Kind == KeyKind.DoubleHole)
            {
                // Two little holes side by side: half = the bigger one covered, the small one open.
                DrawHole(g, x - r * 0.45f, y, r * 0.62f, state is KeyState.Closed or KeyState.Half, accent);
                DrawHole(g, x + r * 0.55f, y, r * 0.42f, state == KeyState.Closed, accent);
            }
            else
                DrawHole(g, x, y, r, state == KeyState.Closed, accent, half: state == KeyState.Half);
            if (key.Kind == KeyKind.Thumb)
            {
                using var font = new Font("Segoe UI", 7.5f);
                using var subtle = new SolidBrush(AppTheme.SubtleText);
                using var right = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
                g.DrawString(T("palec", "thumb"), font, subtle, new RectangleF(x - r - 60, y - 8, 58, 16), right);
            }
        }
    }

    void DrawValves(Graphics g, WindChart chart, WindFingering? fingering, RectangleF area, Color accent)
    {
        float r = Math.Clamp(Math.Min(area.Width * 0.09f, area.Height / 4), 4, 30);
        using var font = new Font("Segoe UI Semibold", Math.Max(7f, r * 0.7f), GraphicsUnit.Pixel);
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        for (int i = 0; i < chart.Keys.Count; i++)
        {
            var key = chart.Keys[i];
            bool down = fingering?.Keys[i] == KeyState.Closed;
            float x = area.Left + key.X * area.Width, y = area.Top + key.Y * area.Height;
            // The valve's stem, the button on top - pressed down when used.
            float drop = down ? r * 0.5f : 0;
            using (var stem = new Pen(AppTheme.StaffLine, Math.Max(2, r * 0.35f)))
                g.DrawLine(stem, x, y - r + drop, x, y + r * 1.8f);
            var button = new RectangleF(x - r, y - r * 1.6f + drop, 2 * r, r * 1.2f);
            using (var fill = new SolidBrush(down ? accent : AppTheme.Paper))
                g.FillEllipse(fill, button);
            using (var edge = new Pen(down ? accent : AppTheme.SubtleText, 1.6f))
                g.DrawEllipse(edge, button);
            using var ink = new SolidBrush(down ? Color.White : AppTheme.SubtleText);
            g.DrawString(key.Label, font, ink, button, center);
        }
    }

    static void DrawHole(Graphics g, float x, float y, float r, bool closed, Color accent, bool half = false)
    {
        var rect = new RectangleF(x - r, y - r, 2 * r, 2 * r);
        using (var open = new SolidBrush(AppTheme.Paper))
            g.FillEllipse(open, rect);
        if (closed)
        {
            using var fill = new SolidBrush(accent);
            g.FillEllipse(fill, rect);
        }
        else if (half)
        {
            using var fill = new SolidBrush(accent);
            g.FillPie(fill, rect.X, rect.Y, rect.Width, rect.Height, 90, 180);
        }
        using var edge = new Pen(AppTheme.Ink, 1.3f);
        g.DrawEllipse(edge, rect);
    }

    static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(2 * radius, Math.Min(r.Width, r.Height));
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
