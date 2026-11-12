using System.Drawing.Drawing2D;

namespace KeyboardUtils;

/// <summary>
/// The fingerboard of a string instrument, in place of the piano keyboard: strings across (the lowest at the bottom),
/// the guitar's frets or the violin family's finger places, and dots where to play - what sounds in playback, what to
/// play next in learning mode (blue), what the instrument is heard playing (orange), a wrong note (red). Each dot
/// carries the fret or the finger. Dark in both themes, like the falling notes.
/// </summary>
sealed class FretboardControl : Control, IThemed
{
    static readonly Color Board = Color.FromArgb(38, 30, 26);
    static readonly Color Ebony = Color.FromArgb(26, 24, 24);
    static readonly Color FretColor = Color.FromArgb(170, 170, 176);
    static readonly Color StringColor = Color.FromArgb(214, 200, 168);
    static readonly Color LabelColor = Color.FromArgb(190, 190, 196);
    static readonly Color HintColor = Color.FromArgb(150, 190, 255);
    static readonly Color HeldColor = Color.FromArgb(255, 152, 0);

    /// <summary>How far ahead the next notes are shown (faintly), in seconds.</summary>
    const double LookAheadSeconds = 1.2;

    Instrument instrument = Instrument.ById("guitar");
    Score? score;
    double currentSeconds;
    readonly HashSet<int> held = [];
    readonly System.Windows.Forms.Timer flashTimer = new();
    (int Pitch, Color Color)? flash;

    public FretboardControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(24, 24, 28);
        flashTimer.Tick += (_, _) =>
        {
            flashTimer.Stop();
            flash = null;
            Invalidate();
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            flashTimer.Dispose();
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

    /// <summary>Learning mode: the notes to play now are lit blue, the other parts aren't shown sounding.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public LearningSession? Learning { get; set; }

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

    /// <summary>Lights a note for a moment (a wrong one: red).</summary>
    public void FlashKey(int pitch, int durationMs = 400, Color? color = null)
    {
        flash = (pitch, color ?? HintColor);
        flashTimer.Stop();
        flashTimer.Interval = Math.Max(1, durationMs);
        flashTimer.Start();
        Invalidate();
    }

    // --- Playing it with the mouse ------------------------------------------------------------------------

    /// <summary>A place on the fingerboard was pressed: the note it sounds (like a key of the on-screen keyboard).</summary>
    public event Action<int>? KeySelected;

    /// <summary>The mouse button went up after <see cref="KeySelected"/>.</summary>
    public event Action<int>? KeyReleased;

    int? pressed;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || PitchAt(e.Location) is not { } pitch)
            return;
        pressed = pitch;
        Capture = true;
        KeySelected?.Invoke(pitch);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (pressed is { } pitch)
            KeyReleased?.Invoke(pitch);
        pressed = null;
        Capture = false;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = PitchAt(e.Location) != null ? Cursors.Hand : Cursors.Default;
    }

    /// <summary>The note at a point: the nearest string, the fret (or place) under the pointer, open left of the nut.</summary>
    int? PitchAt(Point p)
    {
        var strings = instrument.Strings;
        if (strings.Length == 0)
            return null;
        var board = BoardRect;
        if (p.Y < board.Top - 6 || p.Y > board.Bottom + 6 || p.X > board.Right || p.X < board.Left - 30)
            return null;
        int s = Enumerable.Range(0, strings.Length).MinBy(i => Math.Abs(StringY(i) - p.Y));
        int place = 0;
        for (int n = 1; n <= Places; n++)
            if (p.X >= WireX(n - 1) && p.X < WireX(n))
                place = n;
        return strings[s] + place;
    }

    // --- Geometry ------------------------------------------------------------------------------------

    /// <summary>Places shown along the board: the guitar's 12 frets, an octave on the violin family.</summary>
    int Places => Fingering.GuitarFrets;

    bool IsGuitar => instrument.Id == "guitar";

    RectangleF BoardRect
    {
        get
        {
            float labels = 34, openRoom = 30;
            return new RectangleF(labels + openRoom, 14, Math.Max(40, Width - labels - openRoom - 14), Math.Max(30, Height - 40));
        }
    }

    /// <summary>The x of fret (or semitone place) <paramref name="n"/>: real spacing, each a twelfth root of two closer.</summary>
    float WireX(int n)
    {
        var board = BoardRect;
        float full = 1 - MathF.Pow(2, -Places / 12f);
        return board.Left + board.Width * (1 - MathF.Pow(2, -n / 12f)) / full;
    }

    /// <summary>Where a note's dot sits: an open string left of the nut, otherwise between the wires.</summary>
    float SpotX(int semitones)
    {
        if (semitones == 0)
            return BoardRect.Left - 16;
        int n = Math.Min(semitones, Places);
        return (WireX(n - 1) + WireX(n)) / 2;
    }

    float StringY(int s)
    {
        var board = BoardRect;
        int count = instrument.Strings.Length;
        float gap = board.Height / count;
        // The lowest string at the bottom, as the player looks down at it.
        return board.Bottom - gap * (s + 0.5f);
    }

    // --- Drawing -------------------------------------------------------------------------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.Clear(BackColor);
        var strings = instrument.Strings;
        if (strings.Length == 0 || Width < 120 || Height < 60)
            return;
        var board = BoardRect;

        using (var wood = new SolidBrush(IsGuitar ? Board : Ebony))
            g.FillRectangle(wood, board);
        using var labelFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var smallFont = new Font("Segoe UI", 8f);
        using var label = new SolidBrush(LabelColor);
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        // The nut, then the frets (guitar) or the finger places (violin family: faint lines, tapes for the fingers).
        using (var nut = new Pen(Color.FromArgb(230, 222, 200), 5))
            g.DrawLine(nut, board.Left, board.Top, board.Left, board.Bottom);
        if (IsGuitar)
        {
            using var fret = new Pen(FretColor, 2);
            using var inlay = new SolidBrush(Color.FromArgb(120, 230, 222, 200));
            for (int n = 1; n <= Places; n++)
            {
                g.DrawLine(fret, WireX(n), board.Top, WireX(n), board.Bottom);
                g.DrawString(n.ToString(), smallFont, label, new RectangleF(WireX(n - 1), board.Bottom + 2, WireX(n) - WireX(n - 1), 16), center);
            }
            foreach (int n in new[] { 3, 5, 7, 9 })
                g.FillEllipse(inlay, SpotX(n) - 5, board.Top + board.Height / 2 - 5, 10, 10);
            g.FillEllipse(inlay, SpotX(12) - 5, board.Top + board.Height / 3 - 5, 10, 10);
            g.FillEllipse(inlay, SpotX(12) - 5, board.Top + board.Height * 2 / 3 - 5, 10, 10);
        }
        else
        {
            using var place = new Pen(Color.FromArgb(40, 255, 255, 255), 1);
            for (int n = 1; n <= Places; n++)
                g.DrawLine(place, WireX(n), board.Top, WireX(n), board.Bottom);
            // Tapes where a beginner's fingers go (first position): 1, 2 (high), 3, 4.
            int[] tapes = instrument.Id == "cello" ? [2, 3, 4, 5] : [2, 4, 5, 7];
            using var tape = new Pen(Color.FromArgb(150, 245, 240, 225), 3);
            for (int i = 0; i < tapes.Length; i++)
            {
                float x = SpotX(tapes[i]);
                g.DrawLine(tape, x, board.Top, x, board.Bottom);
                g.DrawString((i + 1).ToString(), smallFont, label, new RectangleF(x - 10, board.Bottom + 2, 20, 16), center);
            }
        }

        // The strings, thicker the lower they sound, with their names on the left.
        for (int s = 0; s < strings.Length; s++)
        {
            float y = StringY(s);
            using var pen = new Pen(StringColor, Math.Max(1f, 3.2f - s * 0.45f));
            g.DrawLine(pen, board.Left - 30, y, board.Right, y);
            g.DrawString(LetterOf(strings[s]), labelFont, label, new RectangleF(0, y - 10, 30, 20), center);
        }

        DrawDots(g, strings);
    }

    /// <summary>A string's name without the octave: E A D G H E (Czech) / B (English).</summary>
    static string LetterOf(int pitch) => new string(Tuning.Name(pitch).TakeWhile(c => !char.IsDigit(c)).ToArray());

    void DrawDots(Graphics g, int[] strings)
    {
        // What to show, by pitch, strongest first: a flash, a heard note, a note to play, a sounding note, a next note.
        var dots = new Dictionary<int, (Color Color, bool Faint)>();
        void Add(int pitch, Color color, bool faint = false) => dots.TryAdd(pitch, (color, faint));

        if (flash is { } f)
            Add(f.Pitch, f.Color);
        foreach (int pitch in held)
            Add(pitch, HeldColor);
        if (Learning is { } learning)
            foreach (int pitch in learning.ExpectedKeys())
                Add(pitch, HintColor);
        if (score != null)
        {
            for (int part = 0; part < score.Parts.Count; part++)
            {
                var p = score.Parts[part];
                bool practiced = Learning?.PracticedParts.Contains(part) ?? true;
                if (p.Muted && !practiced)
                    continue;
                var color = PianoRollControl.PartColor(part);
                foreach (var note in p.Notes)
                {
                    double end = note.StartSeconds + note.DurationSeconds;
                    // In learning mode the player's own notes show as hints, not as "sounding".
                    if (Learning == null && note.StartSeconds <= currentSeconds && end > currentSeconds)
                        Add(note.Pitch, color);
                    else if (practiced && note.StartSeconds > currentSeconds && note.StartSeconds <= currentSeconds + LookAheadSeconds)
                        Add(note.Pitch, color, faint: true);
                }
            }
        }

        float radius = Math.Clamp(BoardRect.Height / strings.Length * 0.38f, 7, 14);
        using var font = new Font("Segoe UI", Math.Max(7f, radius * 0.75f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        // Faint ones first, so the strong ones are drawn over them.
        foreach (var (pitch, (color, faint)) in dots.OrderByDescending(d => d.Value.Faint))
        {
            if (Fingering.For(instrument, pitch) is not { } spot)
                continue;
            float x = SpotX(spot.Semitones), y = StringY(spot.String);
            var rect = new RectangleF(x - radius, y - radius, 2 * radius, 2 * radius);
            if (faint)
            {
                using var ring = new Pen(Color.FromArgb(150, color), 2);
                g.DrawEllipse(ring, rect);
                continue;
            }
            using (var fill = new SolidBrush(color))
                g.FillEllipse(fill, rect);
            using var outline = new Pen(Color.FromArgb(200, 255, 255, 255), 1.5f);
            g.DrawEllipse(outline, rect);
            // The guitar shows the fret; the violin family the finger (0 = open string).
            string text = IsGuitar ? spot.Semitones.ToString() : spot.Label;
            using var ink = new SolidBrush(color.GetBrightness() > 0.6f ? Color.FromArgb(30, 30, 30) : Color.White);
            g.DrawString(text, font, ink, rect, center);
        }
    }
}
