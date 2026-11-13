using System.Drawing.Drawing2D;

namespace KeyboardUtils;

/// <summary>
/// A wind instrument in place of the keyboard, on the same dark stage as the keyboard and the fingerboard, filling the
/// panel: the recorder and the flute lying across (mouthpiece / lip plate on the left), the trumpet's three valves.
/// The fingering shown is the note to play now (in learning: the one expected; in playback: the one sounding;
/// otherwise the next one); its name big on the left, the buttons on the right. Above each hole / key is its lane,
/// and the song falls down the lanes like the falling notes onto the keyboard: a bar reaching the hole = cover it,
/// its end passing = lift the finger, its length = how long to hold.
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
    /// <summary>How far ahead the lanes show (their height).</summary>
    const double LookAheadSeconds = 3;
    static readonly Color LaneColor = Color.FromArgb(34, 34, 41);

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

    /// <summary>The x of each key's lane (where the instrument was drawn).</summary>
    readonly Dictionary<int, float> laneX = [];
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
        laneX.Clear();
        var now = NowNote();
        var next = NextNote();
        // Nothing sounding yet: the note to come is the one shown.
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
        float hitY = instrument.Id switch
        {
            "recorder" => DrawRecorder(g, chart, looks, stage, scale, outOfRange),
            "flute" => DrawFlute(g, chart, looks, stage, scale, outOfRange),
            "clarinet" => DrawClarinet(g, chart, looks, stage, scale, outOfRange),
            "altosax" => DrawSaxophone(g, chart, looks, stage, scale, outOfRange),
            _ => DrawTrumpet(g, chart, looks, stage, scale, outOfRange),
        };
        DrawLanes(g, chart, stage, hitY, scale, outOfRange);
        DrawSide(g, side, scale);
    }

    /// <summary>
    /// The lanes over the keys and the song falling down them: per key, a bar for each note that covers it (half wide
    /// for a half-covered hole), from the note's start (bottom) to its end (top); notes in a row that keep a finger
    /// down join up. The names of the notes coming are written at their starts, left of the lanes.
    /// </summary>
    void DrawLanes(Graphics g, WindChart chart, RectangleF stage, float hitY, float scale, bool outOfRange)
    {
        if (laneX.Count == 0)
            return;
        float header = 30 * scale;
        float top = Math.Min(stage.Top + 6 + header, hitY - 30);
        var lanes = laneX.OrderBy(l => l.Value).ToList();
        float gap = lanes.Zip(lanes.Skip(1), (a, b) => b.Value - a.Value).DefaultIfEmpty(40).Min();
        float width = Math.Clamp(gap * 0.72f, 6, 22 * scale);

        using (var stripe = new SolidBrush(LaneColor))
            foreach (var (_, x) in lanes)
                g.FillRectangle(stripe, x - width / 2, top, width, hitY - top);
        using (var line = new Pen(Color.FromArgb(96, 98, 110), 1.5f))
            g.DrawLine(line, lanes[0].Value - width, hitY, lanes[^1].Value + width, hitY);

        // Headers: the hand over its lanes, each lane's finger (the thumb's, the levers' names).
        using var small = new Font("Segoe UI", 7.5f * scale);
        using var handFont = new Font("Segoe UI Semibold", 8.5f * scale);
        using var label = new SolidBrush(Label);
        using var dim = new SolidBrush(Dim);
        using var center = new StringFormat { Alignment = StringAlignment.Center };
        float labelY = top - small.Height - 1;
        foreach (var (i, x) in lanes)
        {
            var key = chart.Keys[i];
            // Levers close together are named under the instrument instead (no room up here).
            if (key.Kind == KeyKind.Lever && gap < 30 * scale)
                continue;
            string text = key.Kind == KeyKind.Thumb ? KeyName(key)
                : key.Kind == KeyKind.Lever ? KeyName(key)
                : key.Kind == KeyKind.Valve ? key.Label
                : instrument.Id == "recorder" ? key.Finger[1..] : key.Finger.Replace("P", T("P", "R"));
            g.DrawString(text, small, key.Kind is KeyKind.Thumb or KeyKind.Lever ? dim : label, new RectangleF(x - 30, labelY, 60, small.Height), center);
        }
        foreach (var (hand, name) in new[] { ('L', T("levá ruka", "left hand")), ('P', T("pravá ruka", "right hand")) })
        {
            var group = lanes.Where(l => chart.Keys[l.Key].Finger.StartsWith(hand) && chart.Keys[l.Key].Kind != KeyKind.Thumb).ToList();
            if (group.Count == 0)
                continue;
            float x1 = group[0].Value - width / 2, x2 = group[^1].Value + width / 2;
            g.DrawString(name, handFont, dim, new RectangleF(x1 - 20, labelY - handFont.Height, x2 - x1 + 40, handFont.Height), center);
        }

        if (outOfRange)
        {
            g.DrawString(T("Tahle nota v hmatové tabulce není.", "This note isn't in the fingering chart."), handFont, dim,
                new RectangleF(stage.Left, top + (hitY - top) / 2 - 10, stage.Width, 20), center);
        }
        if (score == null)
            return;

        // The notes from now to the top of the lanes, as bars per key.
        double now = currentSeconds;
        float span = hitY - top;
        float Y(double seconds) => hitY - (float)((seconds - now) / LookAheadSeconds) * span;
        bool practiced(int part) => Learning?.PracticedParts.Contains(part) ?? !score.Parts[part].Muted;
        var notes = score.Parts
            .SelectMany((part, index) => practiced(index) ? part.Notes.Select(n => (Note: n, Part: index)) : [])
            .Where(n => n.Note.StartSeconds < now + LookAheadSeconds && n.Note.StartSeconds + n.Note.DurationSeconds > now)
            .OrderBy(n => n.Note.StartSeconds)
            .ToList();
        var bars = new Dictionary<int, List<(double Start, double End, bool Half, int Pitch, int Part)>>();
        foreach (var (note, part) in notes)
        {
            if (WindFingerings.For(instrument, note.Pitch, RecorderSystem) is not { } fingering)
                continue;
            for (int k = 0; k < fingering.Keys.Count; k++)
            {
                if (fingering.Keys[k] == KeyState.Open || !laneX.ContainsKey(k))
                    continue;
                if (!bars.TryGetValue(k, out var list))
                    bars[k] = list = [];
                var bar = (note.StartSeconds, note.StartSeconds + note.DurationSeconds, fingering.Keys[k] == KeyState.Half, note.Pitch, part);
                // The finger stays down from one note into the next: one bar (a repeated note is struck again).
                if (list.Count > 0 && list[^1] is var last && bar.Item1 - last.End < 0.04 && last.Pitch != bar.Pitch
                    && last.Half == bar.Item3 && last.Part == part)
                    list[^1] = last with { End = Math.Max(last.End, bar.Item2), Pitch = bar.Pitch };
                else
                    list.Add(bar);
            }
        }
        foreach (var (k, list) in bars)
        {
            float x = laneX[k];
            foreach (var bar in list)
            {
                float bottom = Math.Min(Y(bar.Start), hitY), upper = Math.Max(Y(bar.End), top);
                if (bottom - upper < 2)
                    continue;
                bool sounding = bar.Start <= now && bar.End > now;
                float w = bar.Half ? width / 2 : width;
                var rect = new RectangleF(x - width / 2, upper, w, bottom - upper - (bottom < hitY ? 2 : 0));
                using var path = Rounded(rect, Math.Min(4, w / 2));
                using var fill = new SolidBrush(Color.FromArgb(sounding ? 255 : 200, PianoRollControl.PartColor(bar.Part)));
                g.FillPath(fill, path);
            }
        }

        // The names of the notes to come, at their starts, left of the lanes.
        using var nameFont = new Font("Segoe UI Semibold", 8.5f * scale);
        using var right = new StringFormat { Alignment = StringAlignment.Far };
        float nameRight = lanes[0].Value - width;
        int? lastPitch = null;
        double lastStart = double.MinValue;
        foreach (var (note, _) in notes)
        {
            if (note.StartSeconds < now - 0.01 || note.StartSeconds - lastStart < 0.03 && note.Pitch == lastPitch)
                continue;
            float y = Y(note.StartSeconds);
            if (y - nameFont.Height < top)
                break;
            g.DrawString(Tuning.Name(instrument.Written(note.Pitch)), nameFont, label,
                new RectangleF(nameRight - 70, y - nameFont.Height, 70, nameFont.Height), right);
            lastPitch = note.Pitch;
            lastStart = note.StartSeconds;
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

    /// <returns>Where the lanes end (just above the recorder).</returns>
    float DrawRecorder(Graphics g, WindChart chart, KeyLook[] looks, RectangleF stage, float scale, bool outOfRange)
    {
        float left = stage.Left + 8, right = stage.Right - 12;
        float length = right - left;
        float d = Math.Min(stage.Height * 0.2f, length * 0.075f); // the tube's thickness
        float r = Math.Clamp(d * 0.26f, 4, 18);
        // Low in the panel: under it only the thumb hole; above it the lanes.
        float cy = stage.Bottom - (d * 0.5f + r * 3.4f + 6);
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

        using var fingerFont = new Font("Segoe UI", 8.5f * scale);
        using var dim = new SolidBrush(Dim);
        for (int i = 0; i < chart.Keys.Count; i++)
        {
            var key = chart.Keys[i];
            float x = X(key.Y);
            // The thumb is behind hole 1: its lane (and its hole, under the recorder) just left of it.
            if (key.Kind == KeyKind.Thumb)
                x -= r * 3f;
            laneX[i] = x;
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
                g.DrawString(T("palec (zezadu)", "thumb (back)"), fingerFont, dim, x - r - 4 - g.MeasureString(T("palec (zezadu)", "thumb (back)"), fingerFont).Width, ty - fingerFont.Height / 2f);
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
        }
        return cy - d * 0.56f - 2;
    }

    // --- The flute (lying: crown and lip plate left, foot right) ----------------------------------------

    static readonly Color SilverLight = Color.FromArgb(236, 238, 242);
    static readonly Color Silver = Color.FromArgb(188, 192, 200);
    static readonly Color SilverDark = Color.FromArgb(110, 114, 124);

    /// <returns>Where the lanes end (just above the key cups).</returns>
    float DrawFlute(Graphics g, WindChart chart, KeyLook[] looks, RectangleF stage, float scale, bool outOfRange)
    {
        float left = stage.Left + 6, right = stage.Right - 10;
        float length = right - left;
        float d = Math.Min(stage.Height * 0.11f, length * 0.04f); // the tube
        float cup = Math.Clamp(d * 0.62f, 6, 24);
        // Low in the panel: under it the levers and their names; above it the lanes.
        float cy = stage.Bottom - (d * 0.5f + cup * 2.4f + 16 * scale);
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

        using var fingerFont = new Font("Segoe UI", 8.5f * scale);
        using var dim = new SolidBrush(Dim);
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        for (int i = 0; i < chart.Keys.Count; i++)
        {
            var key = chart.Keys[i];
            var look = outOfRange ? new KeyLook(KeyState.Open, Dim, false) : looks[i];
            float x = X(key.X);
            // The thumb's key is under L1: its lane (and its touch) just left of it.
            if (key.Kind == KeyKind.Thumb)
                x -= cup * 2.4f;
            laneX[i] = x;
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
        return cy - cup * 1.05f - 3;
    }

    /// <summary>A key's short name for its label: the note it's for (Czech Gis, Es, H...), or what it is.</summary>
    static string KeyName(WindKey key) => key.Label switch
    {
        "T" => T("palec", "thumb"),
        "O" => T("okt.", "oct."),
        "R" => T("reg.", "reg."),
        "S4" => "4",
        "G#" or "LG#" or "RG#" => T("Gis", "G♯"),
        "LF#" => T("Fis", "F♯"),
        "LC#" or "C#" => T("Cis", "C♯"),
        "PEb" or "REb" or "Eb" => T("Es", "E♭"),
        "LB" => T("H", "B"),
        "LBb" or "SBb" => T("B", "B♭"),
        "PD" => "D",
        "PF" or "RF" => "F",
        "LE" or "SE" => "E",
        "RC" => "C",
        _ => key.Label.Replace("#", "♯").Replace("b", "♭"),
    };

    /// <summary>
    /// A key under the instrument (thumb / register / octave key, side and little-finger keys): a touch piece on an
    /// arm, its name under it - every second one lower, so neighbours' names don't overlap.
    /// </summary>
    RectangleF DrawTouch(Graphics g, WindKey key, int order, KeyLook look, float x, float bottomOfBody, float size,
        Color metal, Color metalDark, Font font, Brush text, float room)
    {
        // As wide as there's room between neighbours (keys close together get narrow touches, not overlapping ones).
        float ly = bottomOfBody + size * 1.15f, w = Math.Min(size * 1.24f, room * 0.86f);
        var touch = new RectangleF(x - w / 2, ly - size * 0.42f, w, size * 0.84f);
        using (var arm = new Pen(metalDark, Math.Max(1.4f, size * 0.12f)))
            g.DrawLine(arm, x, bottomOfBody - 1, x, touch.Top);
        using (var path = Rounded(touch, touch.Height / 2))
        {
            using var fill = new SolidBrush(look.State != KeyState.Open ? look.Fill : metal);
            g.FillPath(fill, path);
            using var outline = new Pen(look.Point ? HintColor : metalDark, look.Point ? 3f : 1.1f);
            g.DrawPath(outline, path);
        }
        string name = KeyName(key);
        var size2 = g.MeasureString(name, font);
        float ny = touch.Bottom + 1 + (order % 2 == 1 ? font.Height * 0.85f : 0);
        g.DrawString(name, font, text, x - size2.Width / 2, ny);
        return touch;
    }

    // --- The clarinet (lying: mouthpiece left, bell right) ------------------------------------------------------

    /// <returns>Where the lanes end (just above the clarinet).</returns>
    float DrawClarinet(Graphics g, WindChart chart, KeyLook[] looks, RectangleF stage, float scale, bool outOfRange)
    {
        float left = stage.Left + 6, right = stage.Right - 10;
        float length = right - left;
        float d = Math.Min(stage.Height * 0.12f, length * 0.042f); // the tube
        float touchSize = Math.Clamp(d * 0.7f, 7, 18);
        float cy = stage.Bottom - (d * 0.5f + touchSize * 1.6f + 2 * 10 * scale + 6);
        float X(float along) => left + along * length;
        var ebony = Color.FromArgb(34, 32, 34);
        var ebonyLight = Color.FromArgb(78, 74, 76);

        // Mouthpiece (tapering to its tip), barrel, the two joints, the bell flaring out.
        using var body = new GraphicsPath();
        body.AddLine(X(0), cy - d * 0.2f, X(0.07f), cy - d * 0.42f);
        body.AddLine(X(0.07f), cy - d * 0.5f, X(0.86f), cy - d * 0.5f);
        body.AddBezier(X(0.86f), cy - d * 0.5f, X(0.94f), cy - d * 0.55f, X(0.98f), cy - d * 0.9f, X(1), cy - d * 1.05f);
        body.AddLine(X(1), cy - d * 1.05f, X(1), cy + d * 1.05f);
        body.AddBezier(X(1), cy + d * 1.05f, X(0.98f), cy + d * 0.9f, X(0.94f), cy + d * 0.55f, X(0.86f), cy + d * 0.5f);
        body.AddLine(X(0.86f), cy + d * 0.5f, X(0.07f), cy + d * 0.5f);
        body.AddLine(X(0.07f), cy + d * 0.42f, X(0), cy + d * 0.05f);
        body.CloseFigure();
        using (var wood = new LinearGradientBrush(new RectangleF(left, cy - d * 1.1f, length, d * 2.2f), ebonyLight, ebony, LinearGradientMode.Vertical))
        {
            wood.SetBlendTriangularShape(0.42f, 0.85f);
            g.FillPath(wood, body);
        }
        // Silver: the ligature on the mouthpiece and the rings where the parts join.
        using (var ring = new SolidBrush(Silver))
        {
            g.FillRectangle(ring, X(0.045f), cy - d * 0.47f, length * 0.018f, d * 0.94f);
            foreach (float joint in new[] { 0.09f, 0.165f, 0.565f, 0.86f })
                g.FillRectangle(ring, X(joint) - 2, cy - d * 0.53f, 4, d * 1.06f);
        }

        float r = Math.Clamp(d * 0.3f, 4, 14);
        using var font = new Font("Segoe UI", 7.5f * scale);
        using var dim = new SolidBrush(Dim);
        int order = 0;
        for (int i = 0; i < chart.Keys.Count; i++)
        {
            var key = chart.Keys[i];
            var look = outOfRange ? new KeyLook(KeyState.Open, Dim, false) : looks[i];
            float x = X(key.X);
            laneX[i] = x;
            RectangleF hit;
            if (key.Kind == KeyKind.Hole)
            {
                // A tone hole with its silver ring.
                using (var rim = new Pen(Silver, Math.Max(1.5f, r * 0.28f)))
                    g.DrawEllipse(rim, x - r * 1.3f, cy - r * 1.3f, r * 2.6f, r * 2.6f);
                DrawHole(g, x, cy, r, look);
                keyShapes[i] = new RectangleF(x - r, cy - r, 2 * r, 2 * r);
                hit = RectangleF.Inflate(keyShapes[i], r * 0.6f, r * 0.6f);
            }
            else if (key.Kind == KeyKind.Thumb)
            {
                // The thumb hole is at the back: under the clarinet, dashed.
                float ty = cy + d * 0.5f + touchSize * 1.15f;
                using (var joint = new Pen(Dim, 1) { DashStyle = DashStyle.Dot })
                    g.DrawLine(joint, x, cy + d * 0.5f, x, ty - r);
                float tr = Math.Min(r, length * 0.025f * 0.43f);
                DrawHole(g, x, ty, tr, look, dashed: true);
                keyShapes[i] = new RectangleF(x - tr, ty - tr, 2 * tr, 2 * tr);
                hit = RectangleF.Inflate(keyShapes[i], tr * 0.4f, tr * 0.6f);
                var name = KeyName(key);
                g.DrawString(name, font, dim, x - g.MeasureString(name, font).Width / 2, ty + r + 1 + (order++ % 2 == 1 ? font.Height * 0.85f : 0));
            }
            else
            {
                var touch = DrawTouch(g, key, order++, look, x, cy + d * 0.5f, touchSize, Silver, SilverDark, font, dim, length * 0.025f);
                keyShapes[i] = touch;
                hit = RectangleF.Inflate(touch, 4, 6);
            }
            keyHits.Add((hit, i));
        }
        return cy - Math.Max(d * 0.5f, r * 1.3f) - 3;
    }

    // --- The saxophone (lying: neck and mouthpiece left, bell right) --------------------------------------------

    static readonly Color Brass = Color.FromArgb(222, 180, 92);
    static readonly Color BrassLight = Color.FromArgb(250, 226, 150);
    static readonly Color BrassDark = Color.FromArgb(150, 108, 40);

    /// <returns>Where the lanes end (just above the key cups).</returns>
    float DrawSaxophone(Graphics g, WindChart chart, KeyLook[] looks, RectangleF stage, float scale, bool outOfRange)
    {
        float left = stage.Left + 6, right = stage.Right - 10;
        float length = right - left;
        float d = Math.Min(stage.Height * 0.13f, length * 0.045f); // the body where it's widest
        float touchSize = Math.Clamp(d * 0.62f, 7, 18);
        float cy = stage.Bottom - (d * 0.6f + touchSize * 1.6f + 2 * 10 * scale + 6);
        float X(float along) => left + along * length;

        // Black mouthpiece, the thin neck, the body widening towards the bell, the bell flaring.
        using (var mouthpiece = new SolidBrush(Color.FromArgb(30, 30, 32)))
        using (var tip = new GraphicsPath())
        {
            tip.AddPolygon(new[] { new PointF(X(0), cy - d * 0.1f), new PointF(X(0.06f), cy - d * 0.25f), new PointF(X(0.06f), cy + d * 0.25f), new PointF(X(0), cy + d * 0.12f) });
            g.FillPath(mouthpiece, tip);
        }
        using var body = new GraphicsPath();
        body.AddLine(X(0.06f), cy - d * 0.18f, X(0.2f), cy - d * 0.24f);
        body.AddLine(X(0.2f), cy - d * 0.3f, X(0.86f), cy - d * 0.6f);
        body.AddBezier(X(0.86f), cy - d * 0.6f, X(0.93f), cy - d * 0.66f, X(0.97f), cy - d * 1.1f, X(1), cy - d * 1.4f);
        body.AddLine(X(1), cy - d * 1.4f, X(1), cy + d * 1.1f);
        body.AddBezier(X(1), cy + d * 1.1f, X(0.97f), cy + d * 0.9f, X(0.93f), cy + d * 0.62f, X(0.86f), cy + d * 0.58f);
        body.AddLine(X(0.86f), cy + d * 0.58f, X(0.2f), cy + d * 0.3f);
        body.AddLine(X(0.2f), cy + d * 0.24f, X(0.06f), cy + d * 0.18f);
        body.CloseFigure();
        using (var brass = new LinearGradientBrush(new RectangleF(left, cy - d * 1.5f, length, d * 3f), BrassLight, BrassDark, LinearGradientMode.Vertical))
        {
            brass.SetBlendTriangularShape(0.38f, 0.9f);
            g.FillPath(brass, body);
        }
        using (var edge = new Pen(BrassDark, 1.2f))
        {
            g.DrawPath(edge, body);
            g.DrawLine(edge, X(0.2f), cy - d * 0.3f, X(0.2f), cy + d * 0.3f); // where the neck meets the body
        }

        float r = Math.Clamp(d * 0.36f, 5, 16);
        using var font = new Font("Segoe UI", 7.5f * scale);
        using var dim = new SolidBrush(Dim);
        int order = 0;
        for (int i = 0; i < chart.Keys.Count; i++)
        {
            var key = chart.Keys[i];
            var look = outOfRange ? new KeyLook(KeyState.Open, Dim, false) : looks[i];
            float x = X(key.X);
            laneX[i] = x;
            RectangleF hit;
            if (key.Kind == KeyKind.Key)
            {
                // A key cup with a pearl touch; covered = the pearl coloured.
                var cup = new RectangleF(x - r * 1.25f, cy - r * 1.25f, r * 2.5f, r * 2.5f);
                using (var shade = new LinearGradientBrush(cup, BrassLight, BrassDark, LinearGradientMode.ForwardDiagonal))
                    g.FillEllipse(shade, cup);
                var pearl = new RectangleF(x - r * 0.8f, cy - r * 0.8f, r * 1.6f, r * 1.6f);
                using (var fill = new SolidBrush(look.State != KeyState.Open ? look.Fill : Color.FromArgb(240, 236, 226)))
                    g.FillEllipse(fill, pearl);
                using (var rim = new Pen(look.Point ? HintColor : BrassDark, look.Point ? 3f : 1.2f))
                    g.DrawEllipse(rim, cup);
                keyShapes[i] = cup;
                hit = RectangleF.Inflate(cup, 3, 3);
            }
            else
            {
                var touch = DrawTouch(g, key, order++, look, x, cy + d * 0.5f, touchSize, Brass, BrassDark, font, dim, length * 0.025f);
                keyShapes[i] = touch;
                hit = RectangleF.Inflate(touch, 4, 6);
            }
            keyHits.Add((hit, i));
        }
        return cy - r * 1.25f - 3;
    }

    // --- The trumpet ---------------------------------------------------------------------------------------

    /// <returns>Where the lanes end (just above the valve buttons).</returns>
    float DrawTrumpet(Graphics g, WindChart chart, KeyLook[] looks, RectangleF stage, float scale, bool outOfRange)
    {
        float r = Math.Clamp(Math.Min(stage.Width / 12, stage.Height / 6f), 8, 30);
        float spacing = r * 3.2f, cx = stage.Left + stage.Width / 2, cy = stage.Bottom - r * 2.4f;
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
            laneX[i] = x;
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
        return cy - r * 2.35f;
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
