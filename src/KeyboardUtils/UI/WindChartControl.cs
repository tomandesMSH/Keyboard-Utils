using System.Drawing.Drawing2D;

namespace KeyboardUtils;

/// <summary>
/// A wind instrument in place of the keyboard, on the same dark stage as the keyboard and the fingerboard, filling the
/// panel: the recorder and the flute lying across (mouthpiece / lip plate on the left), the trumpet's three valves.
/// The fingering shown is the note to play now (in learning: the one expected; in playback: the one sounding;
/// otherwise the next one); its name big on the left, the buttons on the right. The note coming next shows as rings
/// in their own colour round the holes it covers, shrinking until they're the size of the holes - time to play it.
/// <para>
/// It can be played: click the holes / keys to cover them (your fingers show orange, green where they match the
/// note), then hold <b>Fouknout</b> - the fingering's note sounds (<see cref="NoteStarted"/> / <see cref="NoteEnded"/>,
/// so learning mode hears it). <b>Silněji</b>: the flute an octave up, the trumpet's next partial.
/// </para>
/// </summary>
sealed class WindChartControl : Control, IThemed
{
    static readonly Color Stage = Color.FromArgb(24, 24, 28);
    static readonly Color Label = Color.FromArgb(190, 190, 196);
    static readonly Color Dim = Color.FromArgb(120, 122, 132);
    static readonly Color HintColor = Color.FromArgb(96, 156, 255);
    static readonly Color HeldColor = Color.FromArgb(255, 152, 0);
    static Color MatchColor => PianoRollControl.CorrectColor;
    /// <summary>The next note's rings appear this long before it and shrink onto its holes as it comes.</summary>
    const double ApproachSeconds = 1.6;
    static readonly Color ApproachColor = Color.FromArgb(190, 120, 255);

    Instrument instrument = Instrument.Piano;
    Score? score;
    double currentSeconds;
    readonly HashSet<int> held = [];
    readonly System.Windows.Forms.Timer flashTimer = new();
    (int Pitch, Color Color)? flash;

    /// <summary>The player's own fingers (null = not touched: the chart just shows the note).</summary>
    KeyState[]? user;

    /// <summary>Where the keys, the buttons and the note name were drawn - for clicks.</summary>
    readonly List<(RectangleF Bounds, int Key)> keyHits = [];

    /// <summary>Where each key was drawn (its visible shape), for the rings of the note coming next.</summary>
    readonly Dictionary<int, RectangleF> keyShapes = [];
    RectangleF nameHit, releaseHit;
    readonly List<(RectangleF Bounds, int Level)> blowHits = [];
    int? shownPitch;
    int? blowing;
    int pressedBlow = -1;
    string? message;
    readonly System.Windows.Forms.Timer messageTimer = new() { Interval = 1800 };

    public WindChartControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Stage;
        flashTimer.Tick += (_, _) =>
        {
            flashTimer.Stop();
            flash = null;
            Invalidate();
        };
        messageTimer.Tick += (_, _) =>
        {
            messageTimer.Stop();
            message = null;
            Invalidate();
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            flashTimer.Dispose();
            messageTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Instrument Instrument
    {
        get => instrument;
        set
        {
            StopBlowing();
            instrument = value;
            user = null;
            Invalidate();
        }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public RecorderSystem RecorderSystem { get; set { field = value; Invalidate(); } }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public LearningSession? Learning { get; set; }

    /// <summary>The note's name was clicked: that note (to hear it).</summary>
    public event Action<int>? NoteClicked;

    /// <summary>Blowing started: the note (sounding pitch), velocity, sustained (always true: until <see cref="NoteEnded"/>).</summary>
    public event Action<int, int, bool>? NoteStarted;

    public event Action<int>? NoteEnded;

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

    // --- Playing it with the mouse ---------------------------------------------------------------------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || WindFingerings.ChartFor(instrument) is not { } chart)
            return;
        foreach (var (bounds, level) in blowHits)
            if (bounds.Contains(e.Location))
            {
                pressedBlow = level;
                Capture = true;
                Blow(level);
                Invalidate();
                return;
            }
        if (user != null && releaseHit.Contains(e.Location))
        {
            user = null;
            Invalidate();
            return;
        }
        foreach (var (bounds, key) in keyHits)
            if (bounds.Contains(e.Location))
            {
                Toggle(chart, key);
                return;
            }
        if (shownPitch is { } pitch && nameHit.Contains(e.Location))
            NoteClicked?.Invoke(pitch);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (pressedBlow >= 0)
        {
            pressedBlow = -1;
            StopBlowing();
            Capture = false;
            Invalidate();
        }
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture && pressedBlow >= 0)
        {
            pressedBlow = -1;
            StopBlowing();
            Invalidate();
        }
    }

    /// <summary>A click on a hole / key: covered or not; the recorder's thumb and double holes also half (pinched / one of two).</summary>
    void Toggle(WindChart chart, int key)
    {
        user ??= new KeyState[chart.Keys.Count];
        bool canHalf = instrument.Id == "recorder" && chart.Keys[key].Kind is KeyKind.Thumb or KeyKind.DoubleHole;
        user[key] = user[key] switch
        {
            KeyState.Open => KeyState.Closed,
            KeyState.Closed when canHalf => KeyState.Half,
            _ => KeyState.Open,
        };
        // Changing fingers while blowing: the note changes too.
        if (blowing != null && pressedBlow >= 0)
        {
            StopBlowing();
            Blow(pressedBlow);
        }
        Invalidate();
    }

    /// <summary>
    /// Blows the fingering: yours, or the note's when you haven't touched the chart. Several notes share a fingering on
    /// the flute (an octave apart) and the trumpet (the partials): level 0 the lowest / nearest, level 1 harder.
    /// </summary>
    void Blow(int level)
    {
        var keys = user ?? (shownPitch is { } p ? WindFingerings.For(instrument, p, RecorderSystem)?.Keys.ToArray() : null);
        if (keys == null)
            return;
        var notes = WindFingerings.NotesFor(instrument, keys, RecorderSystem);
        if (notes.Count == 0)
        {
            Say(T("Tenhle hmat v tabulce není.", "That's not a fingering in the chart."));
            return;
        }
        int pick;
        if (instrument.Id == "trumpet" && shownPitch is { } target)
        {
            // The lip finds the partial nearest the note to play; harder = the next one up.
            int nearest = notes.Select((n, i) => (Distance: Math.Abs(n - target), i)).Min().i;
            pick = Math.Min(nearest + level, notes.Count - 1);
        }
        else
            pick = Math.Min(level, notes.Count - 1);
        blowing = notes[pick];
        NoteStarted?.Invoke(blowing.Value, 90, true);
    }

    void StopBlowing()
    {
        if (blowing is { } pitch)
            NoteEnded?.Invoke(pitch);
        blowing = null;
    }

    void Say(string text)
    {
        message = text;
        messageTimer.Stop();
        messageTimer.Start();
        Invalidate();
    }

    // --- Which notes ----------------------------------------------------------------------------------

    /// <summary>The note to show, and its colour.</summary>
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
        if (blowing is { } blown)
            return (blown, HeldColor);
        if (held.Count > 0)
            return (held.Max(), HeldColor);
        return null;
    }

    /// <summary>The next note to play (of the parts practised / heard) and when it starts.</summary>
    (int Pitch, double Start)? NextNote()
    {
        if (score == null)
            return null;
        bool practiced(int part) => Learning?.PracticedParts.Contains(part) ?? !score.Parts[part].Muted;
        var next = score.Parts
            .SelectMany((p, i) => practiced(i) ? p.Notes : [])
            .Where(n => n.StartSeconds > currentSeconds + 0.02)
            .MinBy(n => n.StartSeconds);
        return next == null ? null : (next.Pitch, next.StartSeconds);
    }

    // --- Drawing ---------------------------------------------------------------------------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.Clear(Stage);
        keyHits.Clear();
        blowHits.Clear();
        nameHit = releaseHit = RectangleF.Empty;
        if (WindFingerings.ChartFor(instrument) is not { } chart || Width < 260 || Height < 70)
            return;

        keyShapes.Clear();
        var now = NowNote();
        var next = NextNote();
        // Nothing sounding yet: the note to come is the one shown (and its rings close in on it).
        if (now == null && next is { } first)
            now = (first.Pitch, HintColor);
        shownPitch = now?.Pitch;
        var fingering = now is { } n ? WindFingerings.For(instrument, n.Pitch, RecorderSystem) : null;

        // Three columns: the note | the instrument | the buttons.
        // Text and buttons grow with the panel - by the smaller of its height and width, so a narrow panel still
        // leaves the instrument most of the room.
        float scale = Math.Clamp(Math.Min(Height / 200f, Width / 760f), 0.7f, 1.6f);
        float leftWidth = 96 * scale, rightWidth = 140 * scale;
        var noteArea = new RectangleF(0, 0, leftWidth, Height);
        var stage = new RectangleF(leftWidth, 0, Width - leftWidth - rightWidth, Height);
        var side = new RectangleF(Width - rightWidth, 0, rightWidth, Height);

        DrawNoteName(g, now, fingering, noteArea, scale);
        var looks = Enumerable.Range(0, chart.Keys.Count)
            .Select(i => Look(fingering?.Keys[i] ?? KeyState.Open, user?[i], now?.Color ?? HintColor))
            .ToArray();
        bool outOfRange = now != null && fingering == null && user == null;
        switch (instrument.Id)
        {
            case "recorder": DrawRecorder(g, chart, looks, stage, scale, outOfRange); break;
            case "flute": DrawFlute(g, chart, looks, stage, scale, outOfRange); break;
            default: DrawTrumpet(g, chart, looks, stage, scale, outOfRange); break;
        }
        DrawSide(g, side, scale);
        if (next is { } coming)
            DrawApproach(g, coming.Pitch, coming.Start);
    }

    /// <summary>
    /// The note coming next: a ring round each hole / key it covers, in its own colour, shrinking as the note comes
    /// until it's the size of the hole - that's when to play it (like the falling notes reaching the keyboard).
    /// </summary>
    void DrawApproach(Graphics g, int pitch, double start)
    {
        double left = start - currentSeconds;
        if (left > ApproachSeconds || WindFingerings.For(instrument, pitch, RecorderSystem) is not { } fingering)
            return;
        float progress = (float)Math.Clamp(left / ApproachSeconds, 0, 1); // 1 = just appeared, 0 = play now
        using var pen = new Pen(Color.FromArgb((int)(255 * (1 - 0.65f * progress)), ApproachColor), 2.6f);
        for (int i = 0; i < fingering.Keys.Count; i++)
        {
            if (fingering.Keys[i] == KeyState.Open || !keyShapes.TryGetValue(i, out var shape))
                continue;
            float grow = 1 + 1.7f * progress;
            float w = shape.Width * grow + 6, h = shape.Height * grow + 6;
            var ring = new RectangleF(shape.Left + shape.Width / 2 - w / 2, shape.Top + shape.Height / 2 - h / 2, w, h);
            if (fingering.Keys[i] == KeyState.Half)
                g.DrawArc(pen, ring, 90, 180); // a half ring: half cover it
            else
                g.DrawEllipse(pen, ring);
        }
    }

    void DrawNoteName(Graphics g, (int Pitch, Color Color)? now, WindFingering? fingering, RectangleF area, float scale)
    {
        using var small = new Font("Segoe UI", 8.5f * scale);
        using var big = new Font("Segoe UI Semibold", 26f * scale);
        using var label = new SolidBrush(Dim);
        g.DrawString(T("Teď", "Now"), small, label, area.Left + 14, area.Top + 10 * scale);
        string name = now is { } n ? Tuning.Name(instrument.Written(n.Pitch)) : "–";
        using var brush = new SolidBrush(now?.Color ?? Dim);
        var size = g.MeasureString(name, big);
        var at = new PointF(area.Left + 10, area.Top + area.Height / 2 - size.Height / 2 - 6 * scale);
        g.DrawString(name, big, brush, at);
        nameHit = new RectangleF(at, size);
        string? hint = message ?? fingering?.Hint;
        if (hint != null)
        {
            using var warn = new SolidBrush(HeldColor);
            g.DrawString(hint, small, message != null ? warn : label,
                new RectangleF(area.Left + 12, at.Y + size.Height, area.Width - 14, area.Height - (at.Y + size.Height) - 4));
        }
    }

    /// <summary>How a key looks: shown covered or not, its colour, whether to point at it (still to cover).</summary>
    readonly record struct KeyLook(KeyState State, Color Fill, bool Point);

    static KeyLook Look(KeyState target, KeyState? mine, Color accent)
    {
        if (mine is not { } m)
            return new KeyLook(target, accent, false);
        // Your fingers: green where they're right, orange where they aren't; a ring where one is missing.
        return m == target
            ? new KeyLook(m, MatchColor, false)
            : new KeyLook(m, HeldColor, target != KeyState.Open && m == KeyState.Open);
    }

    // --- The recorder (lying: mouthpiece left, bell right) ----------------------------------------------

    static readonly Color Wood = Color.FromArgb(214, 178, 128);
    static readonly Color WoodDark = Color.FromArgb(140, 100, 62);
    static readonly Color WoodLight = Color.FromArgb(240, 214, 170);
    static readonly Color HoleDark = Color.FromArgb(46, 32, 22);

    void DrawRecorder(Graphics g, WindChart chart, KeyLook[] looks, RectangleF stage, float scale, bool outOfRange)
    {
        float left = stage.Left + 8, right = stage.Right - 12;
        float length = right - left;
        float d = Math.Min(stage.Height * 0.32f, length * 0.088f); // the tube's thickness
        float cy = stage.Top + stage.Height * 0.47f;
        float X(float along) => left + along * length;

        // Beak, head (a bit thicker), body, foot, the bell flaring.
        using var body = new GraphicsPath();
        body.AddLine(X(0), cy - d * 0.3f, X(0.07f), cy - d * 0.5f);
        body.AddLine(X(0.07f), cy - d * 0.5f, X(0.25f), cy - d * 0.56f);
        body.AddLine(X(0.26f), cy - d * 0.5f, X(0.77f), cy - d * 0.46f);
        body.AddLine(X(0.78f), cy - d * 0.5f, X(0.93f), cy - d * 0.44f);
        body.AddLine(X(1), cy - d * 0.56f, X(1), cy + d * 0.56f);
        body.AddLine(X(0.93f), cy + d * 0.44f, X(0.78f), cy + d * 0.5f);
        body.AddLine(X(0.77f), cy + d * 0.46f, X(0.26f), cy + d * 0.5f);
        body.AddLine(X(0.25f), cy + d * 0.56f, X(0.07f), cy + d * 0.5f);
        body.AddLine(X(0.07f), cy + d * 0.5f, X(0), cy + d * 0.1f);
        body.CloseFigure();
        using (var wood = new LinearGradientBrush(new RectangleF(left, cy - d * 0.6f, length, d * 1.2f), WoodLight, WoodDark, LinearGradientMode.Vertical))
        {
            wood.SetBlendTriangularShape(0.35f, 0.9f);
            g.FillPath(wood, body);
        }
        using (var edge = new Pen(WoodDark, 1.2f))
        {
            g.DrawPath(edge, body);
            foreach (float joint in new[] { 0.25f, 0.26f, 0.775f })
                g.DrawLine(edge, X(joint), cy - d * 0.52f, X(joint), cy + d * 0.52f);
        }
        // The window (labium) on top of the head, the windway at the beak's end.
        using (var dark = new SolidBrush(HoleDark))
        {
            g.FillRectangle(dark, X(0.105f), cy - d * 0.5f, length * 0.035f, d * 0.32f);
            g.FillRectangle(dark, X(0), cy - d * 0.22f, 3, d * 0.16f);
        }

        float r = Math.Clamp(d * 0.26f, 4, 18);
        using var fingerFont = new Font("Segoe UI", 8.5f * scale);
        using var handFont = new Font("Segoe UI Semibold", 9f * scale);
        using var label = new SolidBrush(Label);
        using var dim = new SolidBrush(Dim);
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        for (int i = 0; i < chart.Keys.Count; i++)
        {
            var key = chart.Keys[i];
            float x = X(key.Y);
            var look = outOfRange ? new KeyLook(KeyState.Open, Dim, false) : looks[i];
            RectangleF hit;
            if (key.Kind == KeyKind.Thumb)
            {
                // At the back: drawn under the recorder, dashed, with a line to where it is.
                float ty = cy + d * 0.5f + r * 2.2f;
                using (var joint = new Pen(Dim, 1) { DashStyle = DashStyle.Dot })
                    g.DrawLine(joint, x, cy + d * 0.5f, x, ty - r);
                DrawHole(g, x, ty, r, look, dashed: true);
                keyShapes[i] = new RectangleF(x - r, ty - r, 2 * r, 2 * r);
                g.DrawString(T("palec (zezadu)", "thumb (back)"), fingerFont, dim, x + r + 6, ty - fingerFont.Height / 2f);
                hit = new RectangleF(x - r * 1.6f, ty - r * 1.6f, r * 3.2f, r * 3.2f);
            }
            else if (key.Kind == KeyKind.DoubleHole)
            {
                // Two little holes side by side (one bigger): half = the bigger one covered.
                var bigger = look with { State = look.State is KeyState.Closed or KeyState.Half ? KeyState.Closed : KeyState.Open };
                var smaller = look with { State = look.State == KeyState.Closed ? KeyState.Closed : KeyState.Open };
                DrawHole(g, x - r * 0.45f, cy, r * 0.66f, bigger);
                DrawHole(g, x + r * 0.62f, cy - r * 0.05f, r * 0.42f, smaller);
                keyShapes[i] = new RectangleF(x - r * 1.11f, cy - r * 0.7f, r * 2.15f, r * 1.4f);
                hit = new RectangleF(x - r * 1.6f, cy - r * 1.6f, r * 3.2f, r * 3.2f);
            }
            else
            {
                DrawHole(g, x, cy, r, look);
                keyShapes[i] = new RectangleF(x - r, cy - r, 2 * r, 2 * r);
                hit = new RectangleF(x - r * 1.5f, cy - r * 1.5f, r * 3f, r * 3f);
            }
            keyHits.Add((hit, i));
            if (key.Kind != KeyKind.Thumb)
                g.DrawString(key.Finger[1..], fingerFont, label, new RectangleF(x - 20, cy - d * 0.5f - 22 * scale, 40, 18 * scale), center);
        }

        // The hands, over their holes.
        void Hand(int first, int last, string text)
        {
            float x1 = X(chart.Keys[first].Y) - r, x2 = X(chart.Keys[last].Y) + r, y = cy - d * 0.5f - 30 * scale;
            using var pen = new Pen(Dim, 1);
            g.DrawLine(pen, x1, y, x2, y);
            g.DrawLine(pen, x1, y, x1, y + 4);
            g.DrawLine(pen, x2, y, x2, y + 4);
            var size = g.MeasureString(text, handFont);
            g.DrawString(text, handFont, label, (x1 + x2) / 2 - size.Width / 2, y - size.Height - 1);
        }
        Hand(1, 3, T("levá ruka", "left hand"));
        Hand(4, 7, T("pravá ruka", "right hand"));
        if (outOfRange)
            g.DrawString(T("Tahle nota na zobcové flétně není.", "Not a note of the recorder."), fingerFont, dim, X(0.3f), cy + d * 0.5f + r * 4.5f);
    }

    // --- The flute (lying: crown and lip plate left, foot right) ----------------------------------------

    static readonly Color SilverLight = Color.FromArgb(236, 238, 242);
    static readonly Color Silver = Color.FromArgb(188, 192, 200);
    static readonly Color SilverDark = Color.FromArgb(110, 114, 124);

    void DrawFlute(Graphics g, WindChart chart, KeyLook[] looks, RectangleF stage, float scale, bool outOfRange)
    {
        float left = stage.Left + 6, right = stage.Right - 10;
        float length = right - left;
        float d = Math.Min(stage.Height * 0.15f, length * 0.04f); // the tube
        float cy = stage.Top + stage.Height * 0.46f;
        float X(float along) => left + along * length;

        // The tube, shaded round; head joint a touch thinner; crown; joint rings.
        void Tube(float from, float to, float thickness)
        {
            var r = new RectangleF(X(from), cy - thickness / 2, X(to) - X(from), thickness);
            using var metal = new LinearGradientBrush(new RectangleF(r.Left, r.Top - 1, r.Width, r.Height + 2), SilverLight, SilverDark, LinearGradientMode.Vertical);
            metal.SetBlendTriangularShape(0.3f, 0.95f);
            g.FillRectangle(metal, r);
        }
        Tube(0.02f, 0.28f, d * 0.92f);
        Tube(0.28f, 1f, d);
        using (var crown = new SolidBrush(SilverDark))
        using (var path = Rounded(new RectangleF(X(0), cy - d * 0.6f, X(0.022f) - X(0), d * 1.2f), 3))
            g.FillPath(crown, path);
        using var ring = new Pen(SilverDark, Math.Max(1.5f, d * 0.08f));
        foreach (float joint in new[] { 0.27f, 0.285f, 0.855f, 0.865f })
            g.DrawLine(ring, X(joint), cy - d * 0.55f, X(joint), cy + d * 0.55f);

        // The lip plate with its embouchure hole.
        var plate = new RectangleF(X(0.075f) - d * 1.1f, cy - d * 0.78f, d * 2.2f, d * 1.25f);
        using (var plateFill = new LinearGradientBrush(plate, SilverLight, Silver, LinearGradientMode.Vertical))
            g.FillEllipse(plateFill, plate);
        using (var edge = new Pen(SilverDark, 1.2f))
            g.DrawEllipse(edge, plate);
        using (var hole = new SolidBrush(Color.FromArgb(30, 30, 34)))
            g.FillEllipse(hole, X(0.075f) - d * 0.38f, cy - d * 0.47f, d * 0.76f, d * 0.46f);

        // The rod the keys turn on, with its posts.
        using (var rod = new Pen(Silver, Math.Max(1.5f, d * 0.12f)))
            g.DrawLine(rod, X(0.33f), cy - d * 0.62f, X(0.84f), cy - d * 0.62f);
        using (var post = new SolidBrush(SilverDark))
            foreach (float at in new[] { 0.33f, 0.585f, 0.84f })
                g.FillRectangle(post, X(at) - 2, cy - d * 0.78f, 4, d * 0.3f);

        float cup = Math.Clamp(d * 0.62f, 6, 24);
        using var fingerFont = new Font("Segoe UI", 8.5f * scale);
        using var handFont = new Font("Segoe UI Semibold", 9f * scale);
        using var label = new SolidBrush(Label);
        using var dim = new SolidBrush(Dim);
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        for (int i = 0; i < chart.Keys.Count; i++)
        {
            var key = chart.Keys[i];
            var look = outOfRange ? new KeyLook(KeyState.Open, Dim, false) : looks[i];
            float x = X(key.X);
            RectangleF hit;
            if (key.Kind == KeyKind.Key)
            {
                // A key cup on the tube: rim, shine; covered = coloured.
                var rect = new RectangleF(x - cup, cy - cup * 1.05f, 2 * cup, 2 * cup);
                using (var shade = new LinearGradientBrush(rect, SilverLight, SilverDark, LinearGradientMode.ForwardDiagonal))
                    g.FillEllipse(shade, rect);
                var inner = RectangleF.Inflate(rect, -cup * 0.18f, -cup * 0.18f);
                if (look.State != KeyState.Open)
                    using (var fill = new SolidBrush(look.Fill))
                        g.FillEllipse(fill, inner);
                using (var rim = new Pen(look.Point ? HintColor : SilverDark, look.Point ? 3f : 1.4f))
                    g.DrawEllipse(rim, rect);
                keyShapes[i] = rect;
                hit = RectangleF.Inflate(rect, 4, 4);
                g.DrawString(key.Finger.Replace("P", T("P", "R")), fingerFont, label, new RectangleF(x - 24, rect.Top - 20 * scale, 48, 18 * scale), center);
            }
            else
            {
                // Touch pieces under the tube: the thumb's key, the pinkies' levers and rollers.
                float ly = cy + d * 0.5f + cup * 1.2f;
                bool roller = key.Label is "C#" or "C";
                var touch = roller
                    ? new RectangleF(x - cup * 0.55f, ly - cup * 0.45f, cup * 1.1f, cup * 0.9f)
                    : new RectangleF(x - cup * 0.95f, ly - cup * 0.5f, cup * 1.9f, cup);
                using (var arm = new Pen(SilverDark, Math.Max(1.5f, d * 0.1f)))
                    g.DrawLine(arm, x, cy + d * 0.45f, x, touch.Top);
                using (var path = Rounded(touch, touch.Height / 2))
                {
                    using var fill = new SolidBrush(look.State != KeyState.Open ? look.Fill : Silver);
                    g.FillPath(fill, path);
                    using var outline = new Pen(look.Point ? HintColor : SilverDark, look.Point ? 3f : 1.2f);
                    g.DrawPath(outline, path);
                }
                keyShapes[i] = touch;
                hit = RectangleF.Inflate(touch, 5, 6);
                string text = key.Kind == KeyKind.Thumb ? T("palec", "thumb") : key.Label.Replace("#", "♯").Replace("b", "♭");
                g.DrawString(text, fingerFont, dim, new RectangleF(x - 30, touch.Bottom + 1, 60, 18 * scale), center);
            }
            keyHits.Add((hit, i));
        }

        void Hand(float from, float to, string text)
        {
            float x1 = X(from) - cup, x2 = X(to) + cup, y = cy - cup * 1.05f - 24 * scale;
            using var pen = new Pen(Dim, 1);
            g.DrawLine(pen, x1, y, x2, y);
            g.DrawLine(pen, x1, y, x1, y + 4);
            g.DrawLine(pen, x2, y, x2, y + 4);
            var size = g.MeasureString(text, handFont);
            g.DrawString(text, handFont, label, (x1 + x2) / 2 - size.Width / 2, y - size.Height - 1);
        }
        Hand(0.37f, 0.565f, T("levá ruka", "left hand"));
        Hand(0.63f, 0.95f, T("pravá ruka", "right hand"));
        if (outOfRange)
            g.DrawString(T("Tahle nota na flétně není (v tabulce je c1 – a3).", "Not in the flute's chart (C4 – A6)."), fingerFont, dim, X(0.3f), stage.Bottom - 22 * scale);
    }

    // --- The trumpet ---------------------------------------------------------------------------------------

    void DrawTrumpet(Graphics g, WindChart chart, KeyLook[] looks, RectangleF stage, float scale, bool outOfRange)
    {
        float r = Math.Clamp(Math.Min(stage.Width / 12, stage.Height / 4.2f), 8, 38);
        float spacing = r * 3.2f, cx = stage.Left + stage.Width / 2, cy = stage.Top + stage.Height * 0.52f;
        using var font = new Font("Segoe UI Semibold", Math.Max(8f, r * 0.75f), GraphicsUnit.Pixel);
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        // The valve casings joined by the lead pipe.
        using (var pipe = new Pen(Color.FromArgb(150, 130, 70), r * 0.45f))
            g.DrawLine(pipe, cx - spacing * 1.7f, cy + r * 1.6f, cx + spacing * 1.7f, cy + r * 1.6f);
        for (int i = 0; i < chart.Keys.Count; i++)
        {
            var look = outOfRange ? new KeyLook(KeyState.Open, Dim, false) : looks[i];
            bool down = look.State == KeyState.Closed;
            float x = cx + (i - 1) * spacing;
            var casing = new RectangleF(x - r * 0.6f, cy - r * 0.6f, r * 1.2f, r * 2.8f);
            using (var brass = new LinearGradientBrush(casing, Color.FromArgb(240, 214, 130), Color.FromArgb(160, 120, 40), LinearGradientMode.Horizontal))
                g.FillRectangle(brass, casing);
            float drop = down ? r * 0.55f : 0;
            using (var stem = new Pen(Color.FromArgb(200, 200, 206), r * 0.25f))
                g.DrawLine(stem, x, cy - r * 1.2f + drop, x, cy - r * 0.6f);
            var button = new RectangleF(x - r, cy - r * 2.2f + drop, 2 * r, r * 1.1f);
            using (var fill = new SolidBrush(down ? look.Fill : Color.FromArgb(232, 232, 238)))
                g.FillEllipse(fill, button);
            using (var outline = new Pen(look.Point ? HintColor : Color.FromArgb(120, 120, 128), look.Point ? 3f : 1.4f))
                g.DrawEllipse(outline, button);
            using var ink = new SolidBrush(down ? Color.White : Color.FromArgb(60, 60, 66));
            g.DrawString((i + 1).ToString(), font, ink, button, center);
            keyShapes[i] = button;
            keyHits.Add((RectangleF.Inflate(button, r * 0.3f, r * 0.8f), i));
        }
    }

    // --- The side: the buttons ------------------------------------------------------------------------------

    void DrawSide(Graphics g, RectangleF side, float scale)
    {
        bool harder = instrument.Id is "flute" or "trumpet";
        string[] labels = harder ? [T("Fouknout", "Blow"), T("Silněji", "Harder")] : [T("Fouknout", "Blow")];
        using var buttonFont = new Font("Segoe UI Semibold", 9.5f * scale);
        using var small = new Font("Segoe UI", 8.5f * scale);
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        float x = side.Left + 6, w = side.Width - 18, h = 30 * scale;
        // Stacked in the middle of the height: Fouknout, and under it (smaller) Silněji.
        float total = h + (harder ? h * 0.8f + 4 * scale : 0) + 26 * scale;
        float y = side.Top + Math.Max(10 * scale, (side.Height - total) / 2);
        for (int i = 0; i < labels.Length; i++)
        {
            var r = new RectangleF(x, y, w, i == 0 ? h : h * 0.8f);
            y += r.Height + 4 * scale;
            bool down = pressedBlow == i;
            using (var path = Rounded(r, 8))
            using (var fill = new SolidBrush(down ? HeldColor : i == 0 ? Color.FromArgb(52, 120, 246) : Color.FromArgb(58, 58, 66)))
                g.FillPath(fill, path);
            using var text = new SolidBrush(Color.White);
            g.DrawString(labels[i], buttonFont, text, r, center);
            blowHits.Add((r, i));
        }
        using var dim = new SolidBrush(Dim);
        using var label = new SolidBrush(Label);
        if (user != null)
        {
            releaseHit = new RectangleF(x, y, w, 18 * scale);
            g.DrawString(T("× pustit prsty", "× release fingers"), small, label, releaseHit.Location);
        }
        else
            g.DrawString(T("zakryj dírky klikem", "click to cover holes"), small, dim, new RectangleF(x, y, w + 10, 18 * scale));
    }

    // --- Pieces ------------------------------------------------------------------------------------------

    /// <summary>A hole in the wood: dark when open, coloured when covered, half when half; a ring to point at it.</summary>
    static void DrawHole(Graphics g, float x, float y, float r, KeyLook look, bool dashed = false)
    {
        var rect = new RectangleF(x - r, y - r, 2 * r, 2 * r);
        using (var open = new SolidBrush(HoleDark))
            g.FillEllipse(open, rect);
        if (look.State == KeyState.Closed)
        {
            using var fill = new SolidBrush(look.Fill);
            g.FillEllipse(fill, rect);
        }
        else if (look.State == KeyState.Half)
        {
            using var fill = new SolidBrush(look.Fill);
            g.FillPie(fill, rect.X, rect.Y, rect.Width, rect.Height, 90, 180);
        }
        using var edge = new Pen(look.Point ? HintColor : WoodDark, look.Point ? 3f : 1.2f) { DashStyle = dashed && !look.Point ? DashStyle.Dash : DashStyle.Solid };
        g.DrawEllipse(edge, rect);
    }

    static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Max(1, Math.Min(2 * radius, Math.Min(r.Width, r.Height)));
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
