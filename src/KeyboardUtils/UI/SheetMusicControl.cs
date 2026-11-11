using System.Drawing.Drawing2D;

namespace KeyboardUtils;

/// <summary>
/// The song as sheet music on a piano grand staff (treble + bass), laid out in lines that fill the width and
/// scroll vertically. Follows playback with a moving marker, colours the notes that are sounding (or, in learning
/// mode, how they were played), and a click on a bar moves the song there. Notes are written the same way the
/// MusicXML export writes them (<see cref="SheetQuantizer"/>): 16th-note grid, one voice per staff.
/// </summary>
sealed class SheetMusicControl : Control, IThemed
{
    /// <summary>Distance between two staff lines at 100 % zoom and 96 DPI - everything else is sized from it.</summary>
    const float BaseSpace = 8.5f;
    const float MinZoom = 0.6f, MaxZoom = 2f;

    static Color PlayheadColor => Color.FromArgb(66, 133, 244);

    /// <summary>Accidental signs, as stored in <see cref="StaffEvent.Accidentals"/>.</summary>
    const int Sharp = MusicGlyphs.Sharp, Natural = MusicGlyphs.Natural, Flat = MusicGlyphs.Flat;

    /// <summary>
    /// Where the key signature's sharps and flats sit on the treble staff (staff steps, 0 = bottom line), in the
    /// order they're added; the bass staff has them two steps lower.
    /// </summary>
    static readonly int[] SharpSteps = [8, 5, 9, 6, 3, 7, 4];
    static readonly int[] FlatSteps = [4, 7, 3, 6, 2, 5, 1];

    /// <summary>The note on the bottom line of each staff, as a diatonic step: E4 (treble) and G2 (bass).</summary>
    static readonly int[] BottomLine = [4 * 7 + 2, 2 * 7 + 4];

    sealed class StaffEvent(int staff, int measure, int offset, NoteValue value, IReadOnlyList<SheetNote> notes)
    {
        public readonly int Staff = staff, Measure = measure, Offset = offset;
        public readonly NoteValue Value = value;
        public readonly IReadOnlyList<SheetNote> Notes = notes;
        public bool TiedFromPrevious, TiedToNext, WholeMeasureRest;

        /// <summary>Per note: 0 = none, or <see cref="Sharp"/>, <see cref="Natural"/>, <see cref="Flat"/>.</summary>
        public readonly int[] Accidentals = new int[notes.Count];

        /// <summary>Notes beamed together share a group; null = on its own (flag or no flag).</summary>
        public List<StaffEvent>? Beam;
        public bool StemUp;

        /// <summary>Left edge of the notehead, set by the layout.</summary>
        public float X;
        public bool IsRest => Notes.Count == 0;
    }

    sealed class MeasureLayout(int index)
    {
        public readonly int Index = index;
        public float X, Width;
        public readonly List<(int Offset, float X)> Columns = [];
    }

    sealed class SystemLine
    {
        public int First, Count;
        public float Top, TrebleTop, BassTop, Bottom, Left, Right;
        public bool ShowTimeSignature;
        public readonly List<MeasureLayout> Measures = [];
    }

    Score? score;
    ScoreTiming timing = ScoreTiming.Constant(100);
    int measureLength = 16, measureCount;

    /// <summary>The key signature (fifths) and how it spells each pitch class.</summary>
    int keyFifths;
    (int Letter, int Alter)[] spelling = KeySignature.Spelling(0);
    readonly List<StaffEvent>[] staffEvents = [[], []];
    List<StaffEvent>[] eventsByMeasure = [];
    List<SystemLine> systems = [];
    float contentHeight;
    float scrollY;
    float zoom = 1;
    double currentSeconds;
    int followedSystem = -1;
    int? thumbGrab;
    readonly Dictionary<(string, float, string), GraphicsPath?> glyphCache = [];

    /// <summary>Learning mode: practiced notes are coloured by how they were played, other parts are greyed.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public LearningSession? Learning { get; set; }

    /// <summary>When true, a click on a bar raises <see cref="Seek"/> with the time there.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Seekable { get; set; }

    public event Action<double>? Seek;

    /// <summary>When true, dragging across bars selects them as a loop (<see cref="LoopSelected"/>).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool LoopSelectable { get; set; }

    /// <summary>Bars were dragged across: the loop's start and end, in seconds (whole bars).</summary>
    public event Action<double, double>? LoopSelected;

    /// <summary>The loop to show, in seconds; null = none.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public (double Start, double End)? Loop
    {
        get => loop;
        set
        {
            loop = value;
            Invalidate();
        }
    }
    (double Start, double End)? loop;

    /// <summary>Hard spots: bar number (1-based) → mistakes (weighted), shown as a small red badge on the bar.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public IReadOnlyDictionary<int, double>? HardBars
    {
        get => hardBars;
        set
        {
            hardBars = value;
            Invalidate();
        }
    }
    IReadOnlyDictionary<int, double>? hardBars;

    // A press on a bar: a click seeks there, a drag selects bars for a loop.
    Point? pressPoint;
    int pressMeasure;
    int? dragMeasure;

    /// <summary>Tempo assumed for songs whose file doesn't say (recordings, converted audio).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int FallbackBpm { get; set; } = 100;

    public SheetMusicControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AppTheme.Changed += OnThemeChanged;
        Rebuild();
    }

    void OnThemeChanged() => Invalidate();

    float S => BaseSpace * zoom * DeviceDpi / 96f;

    /// <summary>Width of a notehead; stems sit on its edges.</summary>
    float HeadWidth => MusicGlyphs.HeadWidth * S;

    float ScrollbarWidth => 6 * DeviceDpi / 96f;

    public void SetScore(Score? newScore)
    {
        score = newScore;
        currentSeconds = 0;
        scrollY = 0;
        followedSystem = -1;
        Rebuild();
    }

    /// <summary>A score still being written (create mode): re-laid out, keeping the scroll position.</summary>
    public void UpdateLiveScore(Score liveScore)
    {
        score = liveScore;
        Rebuild();
    }

    public void SetCurrentTime(double seconds)
    {
        if (seconds == currentSeconds)
            return;
        currentSeconds = seconds;
        // Keep the line being played in view - but only when playback reaches a new line, so scrolling around
        // while paused isn't undone.
        int system = SystemAt(CurrentMeasure);
        if (system >= 0 && system != followedSystem)
        {
            followedSystem = system;
            var line = systems[system];
            if (line.Top < scrollY || line.Bottom > scrollY + ClientSize.Height)
                ScrollTo(line.Top - S);
        }
        Invalidate();
    }

    int CurrentMeasure => score == null ? -1 : (int)Math.Floor(CurrentUnits / measureLength);

    /// <summary>The current time in 16th notes since the start.</summary>
    double CurrentUnits => timing.ToQuarters(currentSeconds) * SheetQuantizer.Divisions;

    int SystemAt(int measure) => systems.FindIndex(s => measure >= s.First && measure < s.First + s.Count);

    void ScrollTo(float y)
    {
        scrollY = Math.Clamp(y, 0, Math.Max(0, contentHeight - ClientSize.Height));
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutSystems();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ClearGlyphs();
        LayoutSystems();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            AppTheme.Changed -= OnThemeChanged;
            ClearGlyphs();
        }
        base.Dispose(disposing);
    }

    // --- Turning the score into written events ---------------------------------------------------

    void Rebuild()
    {
        staffEvents[0].Clear();
        staffEvents[1].Clear();
        SetKey(score == null ? 0 : KeySignature.Of(score), score != null && KeySignature.IsMinor(score));
        if (score == null)
        {
            measureCount = 0;
            eventsByMeasure = [];
            LayoutSystems();
            return;
        }

        timing = score.Timing ?? ScoreTiming.Constant(FallbackBpm);
        measureLength = timing.MeasureSixteenths;
        var notes = score.Parts.SelectMany((part, index) => part.Muted && !(Learning?.PracticedParts.Contains(index) ?? false)
            ? []
            : part.Notes.Select(n => (n, index)));
        var grandStaff = SheetQuantizer.Quantize(notes, s => timing.ToQuarters(s) * SheetQuantizer.Divisions, measureLength,
            i => score.Parts[i].Staff);
        measureCount = grandStaff.Measures;

        BuildEvents(0, grandStaff.Treble);
        BuildEvents(1, grandStaff.Bass);
        eventsByMeasure = Enumerable.Range(0, measureCount).Select(_ => new List<StaffEvent>()).ToArray();
        foreach (var e in staffEvents.SelectMany(list => list))
            eventsByMeasure[e.Measure].Add(e);
        LayoutSystems();
    }

    /// <summary>Muting a part or starting learning changes which notes are written.</summary>
    public void NotesChanged() => Rebuild();

    void SetKey(int fifths, bool minor)
    {
        keyFifths = Math.Clamp(fifths, -7, 7);
        keyMinor = minor;
        spelling = KeySignature.Spelling(keyFifths, minor);
    }

    bool keyMinor;

    int Diatonic(int pitch) => KeySignature.Diatonic(pitch, spelling[pitch % 12]);

    /// <summary>Staff position: 0 = bottom line, 1 = the space above it, ..., 8 = top line.</summary>
    int StepOf(int pitch, int staff) => Diatonic(pitch) - BottomLine[staff];

    void BuildEvents(int staff, List<Segment> segments)
    {
        var events = staffEvents[staff];
        for (int m = 0; m < measureCount; m++)
        {
            var inMeasure = SheetQuantizer.InMeasure(segments, m, measureLength).ToList();
            if (inMeasure.All(s => s.Segment.IsRest))
            {
                events.Add(new StaffEvent(staff, m, 0, new NoteValue(measureLength, "whole", false), []) { WholeMeasureRest = true });
                continue;
            }

            var accidentals = new AccidentalState(keyFifths, keyMinor);
            foreach (var (segment, index) in inMeasure)
            {
                var pieces = SheetQuantizer.Decompose(segment.Length);
                int offset = segment.Start - m * measureLength;
                bool continuesAfter = index + 1 < segments.Count && segments[index + 1].TiedFromPrevious;
                for (int p = 0; p < pieces.Count; p++)
                {
                    var e = new StaffEvent(staff, m, offset, pieces[p], segment.Notes)
                    {
                        TiedFromPrevious = !segment.IsRest && (p > 0 || segment.TiedFromPrevious),
                        TiedToNext = !segment.IsRest && (p < pieces.Count - 1 || continuesAfter),
                    };
                    for (int k = 0; k < e.Notes.Count; k++)
                    {
                        int pitch = e.Notes[k].Pitch;
                        if (accidentals.Next(pitch, e.TiedFromPrevious) is { } sign)
                            e.Accidentals[k] = sign > 0 ? Sharp : sign < 0 ? Flat : Natural;
                    }
                    events.Add(e);
                    offset += pieces[p].Length;
                }
            }
        }
        BuildBeams(events);
    }

    /// <summary>Eighths and 16ths within one beat are beamed together; everything else stands alone.</summary>
    void BuildBeams(List<StaffEvent> events)
    {
        // Compound time (6/8, 9/8, 12/8) groups in dotted quarters, everything else in quarters.
        int beatLength = timing.BeatType == 8 && timing.Beats % 3 == 0 ? 6 : 4;
        var run = new List<StaffEvent>();
        int runMeasure = -1, runBeat = -1;
        void Flush()
        {
            if (run.Count >= 2)
            {
                var group = run.ToList();
                foreach (var e in group)
                    e.Beam = group;
            }
            run.Clear();
        }

        foreach (var e in events)
        {
            int beat = e.Offset / beatLength;
            bool beamable = !e.IsRest && e.Value.Length <= 3 && e.Offset + e.Value.Length <= (beat + 1) * beatLength;
            if (!beamable || e.Measure != runMeasure || beat != runBeat)
                Flush();
            if (beamable)
            {
                run.Add(e);
                runMeasure = e.Measure;
                runBeat = beat;
            }
        }
        Flush();

        // Stems point away from the note furthest from the middle line - for a beamed group, of the whole group.
        foreach (var e in events.Where(e => !e.IsRest))
        {
            var notes = (e.Beam ?? [e]).SelectMany(x => x.Notes).Select(n => StepOf(n.Pitch, e.Staff)).ToList();
            e.StemUp = 4 - notes.Min() > notes.Max() - 4;
        }
    }

    // --- Layout: bars into lines that fill the width ----------------------------------------------

    void LayoutSystems()
    {
        systems = [];
        float s = S;
        float margin = 2.5f * s;
        float available = Math.Max(10 * s, ClientSize.Width - 2 * margin - ScrollbarWidth);
        float y = score != null ? 5.5f * s : 1.5f * s;

        // No song: a few empty lines of music paper.
        int measures = score != null ? measureCount : 16;
        var natural = new float[measures];
        var columns = new List<(int Offset, float Lead, float Width)>[measures];
        for (int m = 0; m < measures; m++)
        {
            columns[m] = score != null ? MeasureColumns(m) : [];
            natural[m] = score != null ? 1.4f * s + columns[m].Sum(c => c.Lead + c.Width) + 0.6f * s : (available - Header(false)) / 4;
        }

        int first = 0;
        while (first < measures)
        {
            bool showTime = systems.Count == 0 && score != null;
            float header = Header(showTime);
            float used = 0;
            int count = 0;
            while (first + count < measures && (count == 0 || header + used + natural[first + count] <= available))
                used += natural[first + count++];

            // Stretch the line to the full width - except a short last line, which would look pulled apart.
            float target = available - header;
            bool last = first + count >= measures;
            float factor = last && used < target * 0.6f ? 1 : target / used;

            var line = new SystemLine { First = first, Count = count, Left = margin, ShowTimeSignature = showTime };
            float x = margin + header;
            for (int m = first; m < first + count; m++)
            {
                var measure = new MeasureLayout(m) { X = x, Width = natural[m] * factor };
                float columnX = x + 1.4f * s * factor;
                foreach (var (offset, lead, width) in columns[m])
                {
                    measure.Columns.Add((offset, columnX + lead));
                    columnX += (lead + width) * factor;
                }
                line.Measures.Add(measure);
                x += measure.Width;
            }
            line.Right = x;
            foreach (var e in line.Measures.SelectMany(m => score != null ? eventsByMeasure[m.Index] : []))
                e.X = line.Measures[e.Measure - first].Columns.First(c => c.Offset == e.Offset).X;

            PlaceVertically(line, ref y);
            systems.Add(line);
            first += count;
        }

        contentHeight = y + 2 * s;
        scrollY = Math.Clamp(scrollY, 0, Math.Max(0, contentHeight - ClientSize.Height));
        followedSystem = -1;
        Invalidate();
    }

    /// <summary>Clefs, key signature (on every line) and time signature (first line only).</summary>
    float Header(bool timeSignature) => S * (0.8f + 3.4f + KeyWidth + (timeSignature ? 2.6f : 0) + 0.6f);

    /// <summary>The key signature's width in staff spaces.</summary>
    float KeyWidth => keyFifths == 0 ? 0 : Math.Abs(keyFifths) * 1.0f + 0.4f;

    /// <summary>
    /// The moments in a bar where something starts on either staff, each with room before it (accidentals,
    /// a chord's second pushed left) and its width - longer notes get more room, like engraved music.
    /// </summary>
    List<(int Offset, float Lead, float Width)> MeasureColumns(int measure)
    {
        float s = S;
        var events = eventsByMeasure[measure];
        var offsets = events.Select(e => e.Offset).Distinct().Order().ToList();
        var result = new List<(int, float, float)>();
        for (int i = 0; i < offsets.Count; i++)
        {
            int offset = offsets[i];
            int next = i + 1 < offsets.Count ? offsets[i + 1] : measureLength;
            var here = events.Where(e => e.Offset == offset).ToList();
            float lead = here.Any(e => e.Accidentals.Any(a => a != 0)) ? 1.2f * s : 0;
            if (here.Any(e => !e.StemUp && HasSecond(e)))
                lead += HeadWidth;
            float width = s * (1.9f + MathF.Log2(Math.Max(1, next - offset)));
            if (here.Any(e => e.Value.Dotted))
                width += 0.6f * s;
            // A flag sticks out to the right of an up-stem.
            if (here.Any(e => e.Beam == null && !e.IsRest && e.Value.Length <= 3 && e.StemUp))
                width += 0.9f * s;
            if (here.Any(e => e.StemUp && HasSecond(e)))
                width += HeadWidth;
            if (here.Any(e => e.WholeMeasureRest))
                width = Math.Max(width, 6 * s);
            result.Add((offset, lead, width));
        }
        return result;
    }

    bool HasSecond(StaffEvent e)
    {
        var steps = e.Notes.Select(n => Diatonic(n.Pitch)).Order().ToList();
        return steps.Zip(steps.Skip(1)).Any(p => p.Second - p.First == 1);
    }

    /// <summary>Spaces the line's two staves so notes far above or below (ledger lines, stems) don't collide.</summary>
    void PlaceVertically(SystemLine line, ref float y)
    {
        float s = S;
        var events = line.Measures.SelectMany(m => score != null ? eventsByMeasure[m.Index] : []).Where(e => !e.IsRest).ToList();
        int Extreme(int staff, bool highest)
        {
            var steps = events.Where(e => e.Staff == staff).SelectMany(e => e.Notes.Select(n => StepOf(n.Pitch, staff))).ToList();
            return steps.Count == 0 ? 4 : highest ? steps.Max() : steps.Min();
        }

        float above = Math.Max(4 * s, (Extreme(0, true) - 8) * s / 2 + 4 * s);
        float trebleBelow = Math.Max(0, -Extreme(0, false)) * s / 2;
        float bassAbove = Math.Max(0, Extreme(1, true) - 8) * s / 2;
        float gap = Math.Max(7 * s, trebleBelow + bassAbove + 5 * s);
        float below = Math.Max(4 * s, Math.Max(0, -Extreme(1, false)) * s / 2 + 4 * s);

        line.Top = y;
        line.TrebleTop = y + above;
        line.BassTop = line.TrebleTop + 4 * s + gap;
        line.Bottom = line.BassTop + 4 * s + below;
        y = line.Bottom;
    }

    // --- Mouse: scroll, zoom, click to seek -----------------------------------------------------------

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if ((ModifierKeys & Keys.Control) != 0)
        {
            // Ctrl + wheel zooms, keeping the line under the mouse in place.
            ZoomBy(e.Delta > 0 ? 1 : -1, e.Y);
            return;
        }
        ScrollTo(scrollY - e.Delta / 120f * 6 * S);
    }

    /// <summary>Zooms by <paramref name="steps"/> × 10 % (negative = smaller), keeping the line at <paramref name="anchorY"/> in place.</summary>
    public void ZoomBy(int steps, float? anchorY = null)
    {
        float y = anchorY ?? ClientSize.Height / 2f;
        float before = (scrollY + y) / Math.Max(1, contentHeight);
        zoom = Math.Clamp(zoom * MathF.Pow(1.1f, steps), MinZoom, MaxZoom);
        ClearGlyphs();
        LayoutSystems();
        ScrollTo(before * contentHeight - y);
    }

    /// <summary>1 = 100 %.</summary>
    public float Zoom => zoom;

    RectangleF Thumb
    {
        get
        {
            if (contentHeight <= ClientSize.Height)
                return RectangleF.Empty;
            float height = Math.Max(30, ClientSize.Height * ClientSize.Height / contentHeight);
            float top = (ClientSize.Height - height) * scrollY / (contentHeight - ClientSize.Height);
            return new RectangleF(ClientSize.Width - ScrollbarWidth - 2, top, ScrollbarWidth, height);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;
        var thumb = Thumb;
        if (!thumb.IsEmpty && e.X >= thumb.X - 4)
        {
            // Grab the scrollbar (a click beside the thumb jumps there first).
            if (e.Y < thumb.Top || e.Y > thumb.Bottom)
                ScrollTo((e.Y - thumb.Height / 2) / (ClientSize.Height - thumb.Height) * (contentHeight - ClientSize.Height));
            thumbGrab = e.Y - (int)Thumb.Top;
            Capture = true;
            return;
        }
        if (MeasureAt(e.Location) is { } measure)
        {
            pressPoint = e.Location;
            pressMeasure = measure;
            dragMeasure = null;
            Capture = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (thumbGrab is { } grab)
        {
            var thumb = Thumb;
            ScrollTo((e.Y - grab) / Math.Max(1, ClientSize.Height - thumb.Height) * (contentHeight - ClientSize.Height));
            return;
        }
        if (pressPoint is { } press && LoopSelectable)
        {
            // A few pixels of wobble is still a click.
            if (dragMeasure == null && Math.Abs(e.X - press.X) + Math.Abs(e.Y - press.Y) < 8)
                return;
            var to = MeasureAt(e.Location) ?? dragMeasure ?? pressMeasure;
            if (to != dragMeasure)
            {
                dragMeasure = to;
                Invalidate();
            }
            return;
        }
        Cursor = MeasureAt(e.Location) != null ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (pressPoint is { } press && thumbGrab == null)
        {
            if (dragMeasure is { } to)
            {
                int first = Math.Min(pressMeasure, to), last = Math.Max(pressMeasure, to);
                LoopSelected?.Invoke(BarSeconds(first), BarSeconds(last + 1));
            }
            else if (SecondsAt(press) is { } seconds)
                Seek?.Invoke(seconds);
        }
        pressPoint = null;
        dragMeasure = null;
        thumbGrab = null;
        Capture = false;
        Invalidate();
    }

    double BarSeconds(int measure) => timing.ToSeconds(measure * measureLength / (double)SheetQuantizer.Divisions);

    /// <summary>The bar under <paramref name="p"/>, or null when it's not on one (or nothing can be done with it).</summary>
    int? MeasureAt(Point p)
    {
        if ((!Seekable && !LoopSelectable) || score == null || (!Thumb.IsEmpty && p.X >= Thumb.X - 4))
            return null;
        float y = p.Y + scrollY;
        var line = systems.FirstOrDefault(l => y >= l.TrebleTop - 2 * S && y <= l.BassTop + 6 * S);
        return line?.Measures.FirstOrDefault(m => p.X >= m.X && p.X < m.X + m.Width)?.Index;
    }

    /// <summary>The time of the note column nearest to <paramref name="p"/>, or null when it's not on a bar.</summary>
    double? SecondsAt(Point p)
    {
        if (!Seekable || score == null || (!Thumb.IsEmpty && p.X >= Thumb.X - 4))
            return null;
        float y = p.Y + scrollY;
        var line = systems.FirstOrDefault(l => y >= l.TrebleTop - 2 * S && y <= l.BassTop + 6 * S);
        var measure = line?.Measures.FirstOrDefault(m => p.X >= m.X && p.X < m.X + m.Width);
        if (measure == null)
            return null;
        int offset = measure.Columns.Count == 0 ? 0
            : measure.Columns.MinBy(c => Math.Abs(c.X + HeadWidth / 2 - p.X)).Offset;
        return timing.ToSeconds((measure.Index * measureLength + offset) / (double)SheetQuantizer.Divisions);
    }

    // --- Drawing ------------------------------------------------------------------------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(AppTheme.Paper);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        var state = g.Save();
        g.TranslateTransform(0, -scrollY);
        if (score != null)
            DrawTitle(g);
        foreach (var line in systems)
            if (line.Bottom >= scrollY && line.Top <= scrollY + ClientSize.Height)
                DrawSystem(g, line);
        g.Restore(state);

        var thumb = Thumb;
        if (!thumb.IsEmpty)
        {
            using var thumbBrush = new SolidBrush(Color.FromArgb(AppTheme.Dark ? 90 : 70, AppTheme.Ink));
            using var path = Rounded(thumb, ScrollbarWidth / 2);
            g.FillPath(thumbBrush, path);
        }
    }

    void DrawTitle(Graphics g)
    {
        using var font = new Font("Segoe UI Semibold", 1.9f * S, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(AppTheme.Ink);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        g.DrawString(score!.Title, font, brush, new RectangleF(2 * S, 0.8f * S, ClientSize.Width - 4 * S - ScrollbarWidth, 3.6f * S), format);
    }

    /// <summary>A red badge with the mistake count above the top right of each hard bar.</summary>
    void DrawHardBars(Graphics g, SystemLine line)
    {
        if (hardBars is not { Count: > 0 })
            return;
        float s = S;
        using var font = new Font("Segoe UI", 1.35f * s, FontStyle.Bold, GraphicsUnit.Pixel);
        using var fill = new SolidBrush(PianoRollControl.WrongColor);
        using var text = new SolidBrush(Color.White);
        var oldMode = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        foreach (var measure in line.Measures)
        {
            if (!hardBars.TryGetValue(measure.Index + 1, out var mistakes) || mistakes <= 0)
                continue;
            string label = Math.Max(1, (int)Math.Round(mistakes)).ToString();
            var size = g.MeasureString(label, font);
            float h = 2.1f * s, w = Math.Max(h, size.Width + 0.6f * s);
            float x = measure.X + measure.Width - w - 0.3f * s, y = line.TrebleTop - 3.0f * s;
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(x, y, h, h, 90, 180);
            path.AddArc(x + w - h, y, h, h, 270, 180);
            path.CloseFigure();
            g.FillPath(fill, path);
            g.DrawString(label, font, text, x + (w - size.Width) / 2, y + (h - size.Height) / 2);
        }
        g.SmoothingMode = oldMode;
    }

    void DrawSystem(Graphics g, SystemLine line)
    {
        float s = S;
        float[] staffTop = [line.TrebleTop, line.BassTop];
        using var ink = new SolidBrush(AppTheme.Ink);
        using var staffPen = new Pen(AppTheme.StaffLine, Math.Max(1, 0.1f * s));
        using var barPen = new Pen(AppTheme.Ink, Math.Max(1, 0.14f * s));

        DrawLoop(g, line);
        DrawPlayhead(g, line);

        // Staff lines, the bracket line joining the staves, bar lines.
        foreach (float top in staffTop)
            for (int i = 0; i < 5; i++)
                g.DrawLine(staffPen, line.Left, top + i * s, line.Right, top + i * s);
        float systemBottom = line.BassTop + 4 * s;
        g.DrawLine(barPen, line.Left, line.TrebleTop, line.Left, systemBottom);
        foreach (var measure in line.Measures)
        {
            float x = measure.X + measure.Width;
            bool final = score != null && measure.Index == measureCount - 1;
            if (final)
            {
                using var thick = new Pen(AppTheme.Ink, 0.5f * s);
                g.DrawLine(barPen, x - 0.9f * s, line.TrebleTop, x - 0.9f * s, systemBottom);
                g.DrawLine(thick, x - 0.25f * s, line.TrebleTop, x - 0.25f * s, systemBottom);
            }
            else
                g.DrawLine(barPen, x, line.TrebleTop, x, systemBottom);
        }

        // Clefs, the time signature on the first line, the bar number at the start of the others.
        float clefX = line.Left + 0.8f * s;
        DrawGlyph(g, ink, "\U0001D11E", 7.2f * s, clefX, line.TrebleTop - 1.55f * s, fallback: "G");
        DrawGlyph(g, ink, "\U0001D122", 3.3f * s, clefX + 0.2f * s, line.BassTop - 0.05f * s, fallback: "F");
        float keyX = clefX + 3.6f * s;
        for (int i = 0; i < Math.Abs(keyFifths); i++)
        {
            int step = keyFifths > 0 ? SharpSteps[i] : FlatSteps[i];
            for (int staff = 0; staff < 2; staff++)
            {
                float y = staffTop[staff] + 4 * s - (step - 2 * staff) * s / 2;
                DrawAccidental(g, ink, keyFifths > 0 ? Sharp : Flat, keyX + (i + 1) * 1.0f * s, y);
            }
        }
        if (line.ShowTimeSignature)
        {
            float timeX = keyX + KeyWidth * s;
            foreach (float top in staffTop)
            {
                DrawNumber(g, ink, timing.Beats, timeX, top, 2 * s);
                DrawNumber(g, ink, timing.BeatType, timeX, top + 2 * s, 2 * s);
            }
        }
        if (score != null && line.First > 0)
        {
            using var font = new Font("Segoe UI", 1.15f * s, GraphicsUnit.Pixel);
            using var subtle = new SolidBrush(AppTheme.SubtleText);
            g.DrawString((line.First + 1).ToString(), font, subtle, line.Left, line.TrebleTop - 2.6f * s);
        }

        if (score == null)
            return;
        DrawHardBars(g, line);
        foreach (var measure in line.Measures)
            foreach (var e in eventsByMeasure[measure.Index])
                if (e.WholeMeasureRest)
                    DrawWholeMeasureRest(g, ink, measure, staffTop[e.Staff]);
                else if (e.IsRest)
                    DrawRest(g, ink, e, staffTop[e.Staff]);
                else if (e.Beam == null || e.Beam[0] == e)
                    DrawChordOrBeam(g, e.Beam ?? [e], staffTop[e.Staff]);
        DrawTies(g, line, staffTop);
    }

    /// <summary>
    /// Where a moment (in 16ths) is on a line: between the column that started last and the next one (or the bar
    /// line). <paramref name="barStart"/>: a moment right on a bar line is the bar line itself, not its first note.
    /// </summary>
    float XAt(SystemLine line, double units, bool barStart = false)
    {
        int measureIndex = Math.Clamp((int)Math.Floor(units / measureLength), line.First, line.First + line.Count - 1);
        var measure = line.Measures[measureIndex - line.First];
        double inMeasure = units - measureIndex * measureLength;
        if (inMeasure >= measureLength)
            return measure.X + measure.Width;
        if (barStart && inMeasure <= 0.01)
            return measure.X;

        float x = measure.X + 1.4f * S;
        var columns = measure.Columns;
        for (int i = 0; i < columns.Count; i++)
        {
            if (columns[i].Offset > inMeasure)
                break;
            int nextOffset = i + 1 < columns.Count ? columns[i + 1].Offset : measureLength;
            float nextX = i + 1 < columns.Count ? columns[i + 1].X : measure.X + measure.Width - 0.6f * S;
            float t = (float)((inMeasure - columns[i].Offset) / Math.Max(1, nextOffset - columns[i].Offset));
            x = columns[i].X + HeadWidth / 2 + t * (nextX - columns[i].X);
        }
        return x;
    }

    /// <summary>The loop (or the bars being dragged across) as a band behind the notes.</summary>
    void DrawLoop(Graphics g, SystemLine line)
    {
        if (score == null)
            return;
        double lineStart = line.First * measureLength, lineEnd = (line.First + line.Count) * measureLength;
        float top = line.TrebleTop - 1.5f * S, bottom = line.BassTop + 5.5f * S;
        void Band(double from, double to, int alpha, bool edges)
        {
            if (to <= lineStart || from >= lineEnd)
                return;
            float x1 = from <= lineStart ? line.Measures[0].X : XAt(line, from, barStart: true);
            float x2 = to >= lineEnd ? line.Right : XAt(line, to, barStart: true);
            using var brush = new SolidBrush(Color.FromArgb(alpha, PianoRollControl.LoopColor));
            g.FillRectangle(brush, x1, top, x2 - x1, bottom - top);
            if (!edges)
                return;
            using var pen = new Pen(PianoRollControl.LoopColor, Math.Max(2, 0.22f * S));
            if (from >= lineStart)
                g.DrawLine(pen, x1, top, x1, bottom);
            if (to <= lineEnd)
                g.DrawLine(pen, x2, top, x2, bottom);
        }

        if (loop is { } range)
            Band(timing.ToQuarters(range.Start) * SheetQuantizer.Divisions, timing.ToQuarters(range.End) * SheetQuantizer.Divisions,
                AppTheme.Dark ? 34 : 40, edges: true);
        if (dragMeasure is { } dragged)
        {
            int first = Math.Min(pressMeasure, dragged), last = Math.Max(pressMeasure, dragged);
            Band(first * measureLength, (last + 1) * measureLength, AppTheme.Dark ? 60 : 70, edges: false);
        }
    }

    void DrawPlayhead(Graphics g, SystemLine line)
    {
        if (score == null || currentSeconds <= 0)
            return;
        double units = CurrentUnits;
        int measureIndex = (int)Math.Floor(units / measureLength);
        if (measureIndex < line.First || measureIndex >= line.First + line.Count)
            return;
        var measure = line.Measures[measureIndex - line.First];
        float x = XAt(line, units);

        float top = line.TrebleTop - 1.5f * S, bottom = line.BassTop + 5.5f * S;
        using var band = new SolidBrush(Color.FromArgb(AppTheme.Dark ? 40 : 28, PlayheadColor));
        g.FillRectangle(band, measure.X, top, measure.Width, bottom - top);
        using var pen = new Pen(Color.FromArgb(200, PlayheadColor), Math.Max(2, 0.22f * S));
        g.DrawLine(pen, x, top, x, bottom);
    }

    /// <summary>What colour a note is drawn in: the part's while it sounds, green/red once judged in learning mode.</summary>
    Color NoteColor(SheetNote note, StaffEvent e)
    {
        if (Learning is { } learning)
        {
            if (!learning.PracticedParts.Contains(note.PartIndex))
                return Color.FromArgb(AppTheme.Dark ? 90 : 80, AppTheme.Ink);
            return learning.MarkOf(note.Source) switch
            {
                NoteMark.Correct => PianoRollControl.CorrectColor,
                NoteMark.Wrong => PianoRollControl.WrongColor,
                _ => Sounding(e) ? PianoRollControl.PartColor(note.PartIndex) : AppTheme.Ink,
            };
        }
        return Sounding(e) ? PianoRollControl.PartColor(note.PartIndex) : AppTheme.Ink;
    }

    bool Sounding(StaffEvent e)
    {
        if (currentSeconds <= 0)
            return false;
        double units = CurrentUnits;
        int start = e.Measure * measureLength + e.Offset;
        return units >= start && units < start + e.Value.Length;
    }

    float NoteY(int pitch, int staff, float staffTop) => staffTop + 4 * S - StepOf(pitch, staff) * S / 2;

    /// <summary>A single chord (with a flag if it's short) or a beamed group of them.</summary>
    void DrawChordOrBeam(Graphics g, List<StaffEvent> group, float staffTop)
    {
        float s = S;
        int staff = group[0].Staff;
        bool up = group[0].StemUp;
        float middle = staffTop + 2 * s;

        // Where each stem would end on its own: 3.5 spaces from the outermost note, at least to the middle line.
        var stemX = new float[group.Count];
        var ends = new float[group.Count];
        var near = new float[group.Count]; // the note the stem ends nearest to (the outermost)
        for (int i = 0; i < group.Count; i++)
        {
            var e = group[i];
            var ys = e.Notes.Select(n => NoteY(n.Pitch, staff, staffTop)).ToList();
            stemX[i] = up ? e.X + HeadWidth - 0.06f * s : e.X + 0.06f * s;
            near[i] = up ? ys.Min() : ys.Max();
            float length = 3.5f * s + (group.Count == 1 && e.Value.Length == 1 ? 0.6f * s : 0);
            ends[i] = up ? Math.Min(near[i] - length, middle) : Math.Max(near[i] + length, middle);
        }

        if (group.Count > 1)
        {
            // A straight beam from the first stem to the last, tilted at most a little, moved out until every
            // stem is long enough (more for 16ths, which carry a second beam).
            float dx = Math.Max(1, stemX[^1] - stemX[0]);
            float slope = Math.Clamp((ends[^1] - ends[0]) / dx, -0.12f, 0.12f);
            float minStem = 2.8f * s + (group.Any(e => e.Value.Length == 1) ? 0.8f * s : 0);
            float shift = 0;
            for (int i = 0; i < group.Count; i++)
            {
                float beamY = ends[0] + slope * (stemX[i] - stemX[0]);
                float need = up ? beamY - (near[i] - minStem) : (near[i] + minStem) - beamY;
                shift = Math.Max(shift, need);
            }
            for (int i = 0; i < group.Count; i++)
                ends[i] = ends[0] + slope * (stemX[i] - stemX[0]) + (up ? -shift : shift);
        }

        for (int i = 0; i < group.Count; i++)
        {
            var e = group[i];
            Color stemColor = e.Notes.Select(n => NoteColor(n, e)).Distinct().Count() == 1 ? NoteColor(e.Notes[0], e) : AppTheme.Ink;
            DrawNoteheads(g, e, staffTop, up);
            if (e.Value.Length >= 16)
                continue; // a whole note has no stem
            float far = up ? e.Notes.Select(n => NoteY(n.Pitch, staff, staffTop)).Max() : e.Notes.Select(n => NoteY(n.Pitch, staff, staffTop)).Min();
            using var stemPen = new Pen(stemColor, Math.Max(1, 0.12f * s));
            g.DrawLine(stemPen, stemX[i], far, stemX[i], ends[i]);
            if (group.Count == 1 && e.Value.Length <= 3)
            {
                using var flagBrush = new SolidBrush(stemColor);
                DrawFlag(g, flagBrush, stemX[i], ends[i], up);
                if (e.Value.Length == 1)
                    DrawFlag(g, flagBrush, stemX[i], ends[i] + (up ? 0.85f : -0.85f) * s, up);
            }
        }

        if (group.Count > 1)
            DrawBeams(g, group, stemX, ends, up);
    }

    void DrawBeams(Graphics g, List<StaffEvent> group, float[] stemX, float[] ends, bool up)
    {
        float s = S;
        float thickness = 0.5f * s;
        float inward = up ? 1 : -1; // towards the noteheads
        float half = Math.Max(1, 0.12f * s) / 2;
        using var inkBrush = new SolidBrush(AppTheme.Ink);

        void Beam(Brush b, float x1, float y1, float x2, float y2, float level)
        {
            float o = level * 0.8f * s * inward;
            float t = thickness * inward;
            g.FillPolygon(b, [new PointF(x1, y1 + o), new PointF(x2, y2 + o), new PointF(x2, y2 + o + t), new PointF(x1, y1 + o + t)]);
        }

        Beam(inkBrush, stemX[0] - half, ends[0], stemX[^1] + half, ends[^1], 0);
        // Second beam: between neighbouring 16ths, or a stub towards the neighbour for a lone one.
        float slope = (ends[^1] - ends[0]) / Math.Max(1, stemX[^1] - stemX[0]);
        for (int i = 0; i < group.Count; i++)
        {
            if (group[i].Value.Length != 1)
                continue;
            bool nextToo = i + 1 < group.Count && group[i + 1].Value.Length == 1;
            bool prevToo = i > 0 && group[i - 1].Value.Length == 1;
            if (nextToo)
                Beam(inkBrush, stemX[i] - half, ends[i], stemX[i + 1] + half, ends[i + 1], 1);
            else if (!prevToo)
            {
                float stub = 1.1f * s * (i + 1 < group.Count ? 1 : -1);
                Beam(inkBrush, stemX[i], ends[i], stemX[i] + stub, ends[i] + slope * stub, 1);
            }
        }
    }

    void DrawFlag(Graphics g, Brush brush, float x, float y, bool up)
    {
        float s = S;
        float d = up ? 1 : -1;
        using var path = new GraphicsPath();
        path.AddBezier(x, y, x + 0.1f * s, y + d * 1.1f * s, x + 1.25f * s, y + d * 1.3f * s, x + 0.85f * s, y + d * 2.9f * s);
        path.AddBezier(x + 0.85f * s, y + d * 2.9f * s, x + 0.95f * s, y + d * 1.8f * s, x + 0.2f * s, y + d * 1.35f * s, x, y + d * 0.75f * s);
        path.CloseFigure();
        g.FillPath(brush, path);
    }

    void DrawNoteheads(Graphics g, StaffEvent e, float staffTop, bool up)
    {
        float s = S;
        int staff = e.Staff;
        // Notes a step apart can't sit side by side on one stem: every second one moves to the other side.
        var order = Enumerable.Range(0, e.Notes.Count).OrderBy(k => up ? e.Notes[k].Pitch : -e.Notes[k].Pitch).ToList();
        var shifted = new bool[e.Notes.Count];
        for (int i = 1; i < order.Count; i++)
        {
            int previous = order[i - 1], current = order[i];
            shifted[current] = !shifted[previous] && Math.Abs(Diatonic(e.Notes[current].Pitch) - Diatonic(e.Notes[previous].Pitch)) == 1;
        }

        using var ledgerPen = new Pen(AppTheme.Ink, Math.Max(1, 0.16f * s));
        for (int k = 0; k < e.Notes.Count; k++)
        {
            var note = e.Notes[k];
            int step = StepOf(note.Pitch, staff);
            float y = NoteY(note.Pitch, staff, staffTop);
            float x = e.X + (shifted[k] ? (up ? HeadWidth - 0.12f * s : -HeadWidth + 0.12f * s) : 0);
            using var brush = new SolidBrush(NoteColor(note, e));

            // Ledger lines from the staff out to the note.
            for (int l = -2; l >= step; l -= 2)
                LedgerLine(l);
            for (int l = 10; l <= step; l += 2)
                LedgerLine(l);
            void LedgerLine(int lineStep)
            {
                float ly = staffTop + 4 * s - lineStep * s / 2;
                g.DrawLine(ledgerPen, x - 0.4f * s, ly, x + HeadWidth + 0.4f * s, ly);
            }

            DrawNotehead(g, brush, x, y, e.Value.Length);
            if (e.Value.Dotted)
            {
                float dotY = step % 2 == 0 ? y - s / 2 : y;
                float dotX = e.X + HeadWidth + (up && shifted.Any(b => b) ? HeadWidth : 0) + 0.45f * s;
                float r = 0.2f * s;
                g.FillEllipse(brush, dotX - r, dotY - r, 2 * r, 2 * r);
            }
            if (e.Accidentals[k] != 0)
                DrawAccidental(g, brush, e.Accidentals[k], e.X - (!up && shifted.Any(b => b) ? HeadWidth : 0) - 0.3f * s, y);
        }
    }

    /// <summary>An oval tilted up to the right: filled for quarters and shorter, open for halves and wholes.</summary>
    void DrawNotehead(Graphics g, Brush brush, float x, float y, int length) => MusicGlyphs.Notehead(g, brush, x, y, S, length);

    void DrawAccidental(Graphics g, Brush brush, int kind, float right, float y) => MusicGlyphs.Accidental(g, brush, kind, right, y, S);

    void DrawWholeMeasureRest(Graphics g, Brush ink, MeasureLayout measure, float staffTop)
    {
        float s = S;
        float cx = measure.X + measure.Width / 2;
        g.FillRectangle(ink, cx - 0.65f * s, staffTop + s, 1.3f * s, 0.55f * s);
    }

    void DrawRest(Graphics g, Brush ink, StaffEvent e, float staffTop)
    {
        float s = S;
        float cx = e.X + HeadWidth / 2;
        float middle = staffTop + 2 * s;
        switch (e.Value.Length)
        {
            case >= 16:
                g.FillRectangle(ink, cx - 0.65f * s, staffTop + s, 1.3f * s, 0.55f * s);
                break;
            case >= 8:
                g.FillRectangle(ink, cx - 0.65f * s, middle - 0.55f * s, 1.3f * s, 0.55f * s);
                break;
            case >= 4:
                if (!DrawGlyph(g, ink, "\U0001D13D", 3 * s, cx - 0.55f * s, middle - 1.5f * s, centerX: cx))
                    DrawQuarterRest(g, ink, cx, middle);
                break;
            case >= 2:
                if (!DrawGlyph(g, ink, "\U0001D13E", 2 * s, cx - 0.5f * s, middle - 1.0f * s, centerX: cx))
                    DrawSmallRest(g, ink, cx, middle, 1);
                break;
            default:
                if (!DrawGlyph(g, ink, "\U0001D13F", 3 * s, cx - 0.6f * s, middle - 1.0f * s, centerX: cx))
                    DrawSmallRest(g, ink, cx, middle, 2);
                break;
        }
        if (e.Value.Dotted)
        {
            float r = 0.2f * s;
            g.FillEllipse(ink, cx + 0.9f * s - r, middle - s / 2 - r, 2 * r, 2 * r);
        }
    }

    void DrawQuarterRest(Graphics g, Brush ink, float cx, float middle)
    {
        float s = S;
        using var pen = new Pen(ink, 0.3f * s) { LineJoin = LineJoin.Round };
        g.DrawLines(pen, [
            new PointF(cx - 0.3f * s, middle - 1.5f * s), new PointF(cx + 0.35f * s, middle - 0.7f * s),
            new PointF(cx - 0.25f * s, middle), new PointF(cx + 0.35f * s, middle + 0.7f * s),
        ]);
        g.DrawBezier(pen, cx + 0.35f * s, middle + 0.7f * s, cx - 0.4f * s, middle + 0.4f * s, cx - 0.5f * s, middle + 1.1f * s, cx - 0.05f * s, middle + 1.5f * s);
    }

    void DrawSmallRest(Graphics g, Brush ink, float cx, float middle, int hooks)
    {
        float s = S;
        using var pen = new Pen(ink, Math.Max(1, 0.14f * s));
        g.DrawLine(pen, cx + 0.5f * s, middle - 1 * s, cx - 0.1f * s, middle + (hooks == 1 ? 1 : 2) * s);
        for (int i = 0; i < hooks; i++)
        {
            float y = middle - 0.9f * s + i * s;
            g.FillEllipse(ink, cx - 0.5f * s, y - 0.25f * s, 0.5f * s, 0.5f * s);
            g.DrawBezier(pen, cx - 0.4f * s, y + 0.15f * s, cx - 0.1f * s, y + 0.4f * s, cx + 0.3f * s, y + 0.2f * s, cx + 0.5f * s - i * 0.2f * s, y - 0.1f * s);
        }
    }

    /// <summary>Ties between a note and its continuation; one crossing to the next line is drawn in two halves.</summary>
    void DrawTies(Graphics g, SystemLine line, float[] staffTop)
    {
        float s = S;
        for (int staff = 0; staff < 2; staff++)
        {
            var events = staffEvents[staff];
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                bool inLine = e.Measure >= line.First && e.Measure < line.First + line.Count;
                if (!e.TiedToNext || i + 1 >= events.Count)
                    continue;
                var next = events[i + 1];
                bool nextInLine = next.Measure >= line.First && next.Measure < line.First + line.Count;
                if (!inLine && !nextInLine)
                    continue;
                foreach (var note in e.Notes)
                {
                    if (!next.Notes.Any(n => n.Pitch == note.Pitch))
                        continue;
                    float y = NoteY(note.Pitch, staff, staffTop[staff]);
                    float x1 = inLine ? e.X + HeadWidth + 0.15f * s : line.Left + Header(line.ShowTimeSignature) - 0.5f * s;
                    float x2 = nextInLine ? next.X - 0.15f * s : line.Right - 0.3f * s;
                    using var brush = new SolidBrush(NoteColor(note, e));
                    DrawTie(g, brush, x1, x2, y + (e.StemUp ? 0.55f : -0.55f) * s, e.StemUp);
                }
            }
        }
    }

    void DrawTie(Graphics g, Brush brush, float x1, float x2, float y, bool below)
    {
        if (x2 - x1 < 2)
            return;
        float s = S;
        float d = below ? 1 : -1;
        float rise = Math.Min(1.0f * s, 0.12f * (x2 - x1) + 0.4f * s);
        using var path = new GraphicsPath();
        path.AddBezier(x1, y, x1 + (x2 - x1) * 0.25f, y + d * rise, x1 + (x2 - x1) * 0.75f, y + d * rise, x2, y);
        path.AddBezier(x2, y, x1 + (x2 - x1) * 0.75f, y + d * (rise - 0.22f * s), x1 + (x2 - x1) * 0.25f, y + d * (rise - 0.22f * s), x1, y);
        path.CloseFigure();
        g.FillPath(brush, path);
    }

    void DrawNumber(Graphics g, Brush ink, int value, float x, float top, float height) =>
        DrawGlyph(g, ink, value.ToString(), height * 0.98f, x, top + height * 0.01f, family: "Times New Roman", style: FontStyle.Bold);

    /// <summary>
    /// Draws a font character scaled so its ink is exactly <paramref name="height"/> tall with its top at
    /// <paramref name="y"/> - glyph metrics differ between fonts, ink bounds don't. False if the font lacks it.
    /// </summary>
    bool DrawGlyph(Graphics g, Brush brush, string text, float height, float x, float y, string family = "Segoe UI Symbol",
        FontStyle style = FontStyle.Regular, string? fallback = null, float? centerX = null)
    {
        var key = (text, height, family + (int)style);
        if (!glyphCache.TryGetValue(key, out var path))
        {
            path = new GraphicsPath();
            try
            {
                using var fontFamily = new FontFamily(family);
                path.AddString(text, fontFamily, (int)style, 100, PointF.Empty, StringFormat.GenericTypographic);
            }
            catch (ArgumentException)
            {
            }
            var bounds = path.GetBounds();
            if (bounds.Height <= 0)
            {
                path.Dispose();
                path = null;
            }
            else
            {
                using var matrix = new Matrix();
                matrix.Scale(height / bounds.Height, height / bounds.Height);
                matrix.Translate(-bounds.X, -bounds.Y);
                path.Transform(matrix);
            }
            glyphCache[key] = path;
        }

        if (path == null)
        {
            if (fallback != null)
            {
                using var font = new Font("Times New Roman", height * 0.6f, FontStyle.Bold, GraphicsUnit.Pixel);
                g.DrawString(fallback, font, brush, x, y + height * 0.15f);
            }
            return false;
        }
        if (centerX is { } center)
            x = center - path.GetBounds().Width / 2;
        var state = g.Save();
        g.TranslateTransform(x, y);
        g.FillPath(brush, path);
        g.Restore(state);
        return true;
    }

    void ClearGlyphs()
    {
        foreach (var path in glyphCache.Values)
            path?.Dispose();
        glyphCache.Clear();
    }

    static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
