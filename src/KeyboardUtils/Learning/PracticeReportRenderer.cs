using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace KeyboardUtils;

/// <summary>
/// Draws the teacher's report as A4 pages (150 dpi bitmaps, always on white paper): the heading, the totals, minutes
/// per day against the goal, a table of the songs, and room for the teacher's note and signature. A long table
/// continues on the next page.
/// </summary>
static class PracticeReportRenderer
{
    const int Width = 1240, Height = 1754; // A4 at 150 dpi
    const int Margin = 90;
    static readonly Color Ink = Color.FromArgb(28, 28, 32);
    static readonly Color Subtle = Color.FromArgb(110, 112, 120);
    static readonly Color Line = Color.FromArgb(214, 216, 222);
    static readonly Color Accent = Color.FromArgb(52, 120, 246);
    static readonly Color Band = Color.FromArgb(243, 245, 249);

    const int RowHeight = 54;

    /// <param name="student">The pupil's name (may be empty).</param>
    /// <param name="instrument">Shown in the heading.</param>
    public static List<Bitmap> Render(PracticeReport report, string student, Instrument instrument)
    {
        var pages = new List<Bitmap>();
        int songIndex = 0;
        bool noteDrawn = false;
        do
        {
            var page = new Bitmap(Width, Height);
            page.SetResolution(150, 150);
            using var g = Graphics.FromImage(page);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.White);
            float y = Margin;
            if (pages.Count == 0)
            {
                y = DrawHeading(g, report, student, instrument, y);
                y = DrawTotals(g, report, y + 30);
                y = DrawDays(g, report, y + 40);
            }
            else
                y = DrawContinued(g, report, student, y);
            y = DrawSongs(g, report, ref songIndex, y + 40, footerRoom: songIndex == 0 ? 330 : 0);
            if (songIndex >= report.Songs.Count && Height - y > 330)
            {
                DrawTeacherNote(g, Height - Margin - 260);
                noteDrawn = true;
            }
            DrawFooter(g, pages.Count + 1);
            pages.Add(page);
        }
        while (songIndex < report.Songs.Count || pages.Count == 0);

        // No room for the teacher's note under the table: a last page for it.
        if (!noteDrawn)
        {
            var page = new Bitmap(Width, Height);
            page.SetResolution(150, 150);
            using var g = Graphics.FromImage(page);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.White);
            DrawContinued(g, report, student, Margin);
            DrawTeacherNote(g, Margin + 160);
            DrawFooter(g, pages.Count + 1);
            pages.Add(page);
        }
        return pages;
    }

    static string Date(DateOnly day) => day.ToString("d. M. yyyy");

    static float DrawHeading(Graphics g, PracticeReport report, string student, Instrument instrument, float y)
    {
        using var title = new Font("Segoe UI Semibold", 30, GraphicsUnit.Pixel);
        using var big = new Font("Segoe UI Semibold", 46, GraphicsUnit.Pixel);
        using var normal = new Font("Segoe UI", 26, GraphicsUnit.Pixel);
        using var ink = new SolidBrush(Ink);
        using var subtle = new SolidBrush(Subtle);
        using var accent = new SolidBrush(Accent);
        g.DrawString(T("Zpráva o cvičení", "Practice report"), title, accent, Margin, y);
        y += 46;
        string who = string.IsNullOrWhiteSpace(student) ? T("Žák / žákyně", "Student") : student.Trim();
        g.DrawString(who, big, ink, Margin - 3, y);
        y += 66;
        g.DrawString($"{instrument.Name}   ·   {Date(report.From)} – {Date(report.To)}", normal, subtle, Margin, y);
        return y + 40;
    }

    static float DrawContinued(Graphics g, PracticeReport report, string student, float y)
    {
        using var font = new Font("Segoe UI", 24, GraphicsUnit.Pixel);
        using var subtle = new SolidBrush(Subtle);
        string who = string.IsNullOrWhiteSpace(student) ? "" : student.Trim() + "   ·   ";
        g.DrawString($"{T("Zpráva o cvičení", "Practice report")}   ·   {who}{Date(report.From)} – {Date(report.To)}", font, subtle, Margin, y);
        return y + 50;
    }

    /// <summary>Four boxes: minutes, days, goal days, streak.</summary>
    static float DrawTotals(Graphics g, PracticeReport report, float y)
    {
        (string Value, string Label)[] boxes =
        [
            (Math.Round(report.Minutes).ToString("0"), T("minut cvičení", "minutes practised")),
            ($"{report.DaysPractised} / {report.Days}", T("dní, kdy cvičil(a)", "days practised")),
            (report.GoalMinutes > 0 ? $"{report.GoalDaysMet}" : "–", report.GoalMinutes > 0 ? T($"dní splněný cíl {report.GoalMinutes} min", $"days with the {report.GoalMinutes} min goal") : T("bez denního cíle", "no daily goal")),
            ($"{report.Attempts}", T("pokusů v režimu učení", "learning-mode attempts")),
        ];
        float gap = 24, w = (Width - 2 * Margin - 3 * gap) / 4, h = 150;
        using var value = new Font("Segoe UI Semibold", 52, GraphicsUnit.Pixel);
        using var label = new Font("Segoe UI", 22, GraphicsUnit.Pixel);
        using var band = new SolidBrush(Band);
        using var ink = new SolidBrush(Ink);
        using var subtle = new SolidBrush(Subtle);
        for (int i = 0; i < boxes.Length; i++)
        {
            float x = Margin + i * (w + gap);
            using var path = Rounded(new RectangleF(x, y, w, h), 18);
            g.FillPath(band, path);
            g.DrawString(boxes[i].Value, value, ink, x + 22, y + 18);
            g.DrawString(boxes[i].Label, label, subtle, new RectangleF(x + 24, y + 92, w - 40, 56));
        }
        return y + h;
    }

    /// <summary>Minutes per day as bars, the goal as a dashed line.</summary>
    static float DrawDays(Graphics g, PracticeReport report, float y)
    {
        using var headingFont = new Font("Segoe UI Semibold", 28, GraphicsUnit.Pixel);
        using var small = new Font("Segoe UI", 18, GraphicsUnit.Pixel);
        using var ink = new SolidBrush(Ink);
        using var subtle = new SolidBrush(Subtle);
        g.DrawString(T("Minuty po dnech", "Minutes per day"), headingFont, ink, Margin, y);
        y += 50;
        float chartHeight = 230, left = Margin + 50, right = Width - Margin;
        var days = report.PerDay;
        double max = Math.Max(Math.Max(report.GoalMinutes, 10), days.Count == 0 ? 0 : days.Max(d => d.Minutes)) * 1.1;
        using var axis = new Pen(Line, 2);
        g.DrawLine(axis, left, y + chartHeight, right, y + chartHeight);
        float slot = (right - left) / Math.Max(1, days.Count), bar = Math.Min(44, slot * 0.7f);
        using var center = new StringFormat { Alignment = StringAlignment.Center };
        for (int i = 0; i < days.Count; i++)
        {
            float h = (float)(days[i].Minutes / max * chartHeight);
            float x = left + i * slot + (slot - bar) / 2;
            bool met = report.GoalMinutes > 0 && days[i].Minutes >= report.GoalMinutes;
            using var fill = new SolidBrush(days[i].Minutes <= 0 ? Line : met ? PianoRollControl.CorrectColor : Accent);
            if (h > 0)
                g.FillRectangle(fill, x, y + chartHeight - h, bar, h);
            // The day of the month under every bar (every few when there are many).
            if (days.Count <= 16 || i % 3 == 0 || i == days.Count - 1)
                g.DrawString(days[i].Day.Day + ".", small, subtle, new RectangleF(left + i * slot - 10, y + chartHeight + 6, slot + 20, 24), center);
        }
        if (report.GoalMinutes > 0)
        {
            float gy = y + chartHeight - (float)(report.GoalMinutes / max * chartHeight);
            using var goal = new Pen(PianoRollControl.CorrectColor, 2) { DashStyle = DashStyle.Dash };
            g.DrawLine(goal, left, gy, right, gy);
            string goalText = T($"cíl {report.GoalMinutes} min", $"goal {report.GoalMinutes} min");
            var size = g.MeasureString(goalText, small);
            g.DrawString(goalText, small, subtle, right - size.Width, gy - size.Height - 2);
        }
        return y + chartHeight + 40;
    }

    /// <summary>The song table, from <paramref name="index"/> on, as many rows as fit; leaves <paramref name="footerRoom"/> free.</summary>
    static float DrawSongs(Graphics g, PracticeReport report, ref int index, float y, float footerRoom)
    {
        using var headingFont = new Font("Segoe UI Semibold", 28, GraphicsUnit.Pixel);
        using var header = new Font("Segoe UI Semibold", 19, GraphicsUnit.Pixel);
        using var cell = new Font("Segoe UI", 21, GraphicsUnit.Pixel);
        using var ink = new SolidBrush(Ink);
        using var subtle = new SolidBrush(Subtle);
        using var line = new Pen(Line, 1.5f);
        if (index == 0)
        {
            g.DrawString(T("Skladby", "Songs"), headingFont, ink, Margin, y);
            y += 50;
        }
        if (report.Songs.Count == 0)
        {
            g.DrawString(T("V tomhle období se v režimu učení necvičilo.", "No learning mode in this period."), cell, subtle, Margin, y);
            return y + 40;
        }

        // Columns: song | attempts | minutes | best | last | speed | hard spots.
        float[] x = [Margin, Margin + 390, Margin + 485, Margin + 575, Margin + 695, Margin + 815, Margin + 945];
        string[] titles =
        [
            T("Skladba", "Song"), T("Pokusů", "Attempts"), T("Minut", "Minutes"), T("Nejlepší", "Best"),
            T("Poslední", "Last"), T("Tempo", "Speed"), T("Těžké takty", "Hard bars"),
        ];
        for (int c = 0; c < titles.Length; c++)
            g.DrawString(titles[c], header, subtle, x[c], y);
        y += 34;
        g.DrawLine(line, Margin, y, Width - Margin, y);

        using var trim = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center };
        float bottom = Height - Margin - 80 - footerRoom;
        while (index < report.Songs.Count && y + RowHeight <= bottom)
        {
            var song = report.Songs[index++];
            var row = new RectangleF(0, y, 0, RowHeight);
            void Cell(int column, string text, Brush brush) =>
                g.DrawString(text, cell, brush, new RectangleF(x[column], row.Top, (column + 1 < x.Length ? x[column + 1] : Width - Margin) - x[column] - 10, RowHeight), trim);
            Cell(0, song.Title, ink);
            Cell(1, song.Attempts.ToString(), ink);
            Cell(2, Math.Round(song.Minutes).ToString("0"), ink);
            Percent(g, song.BestPercent, x[3], row.Top, cell, trim);
            Percent(g, song.LastPercent, x[4], row.Top, cell, trim);
            Cell(5, song.SlowestSpeed == song.FastestSpeed ? $"{song.SlowestSpeed} %" : $"{song.SlowestSpeed}–{song.FastestSpeed} %", ink);
            Cell(6, song.HardSpots.Count == 0 ? "–" : string.Join(", ", song.HardSpots.Select(h => h.Bars)), song.HardSpots.Count == 0 ? subtle : ink);
            y += RowHeight;
            g.DrawLine(line, Margin, y, Width - Margin, y);
        }
        return y;
    }

    /// <summary>A result: a small coloured bar (green / amber / red, as in the app) and the number.</summary>
    static void Percent(Graphics g, double percent, float x, float top, Font font, StringFormat format)
    {
        using var fill = new SolidBrush(PracticeChart.ColorFor(percent));
        float h = 12, w = (float)(36 * Math.Clamp(percent, 0, 100) / 100);
        using var track = new SolidBrush(Line);
        g.FillRectangle(track, x, top + RowHeight / 2f - h / 2, 36, h);
        g.FillRectangle(fill, x, top + RowHeight / 2f - h / 2, w, h);
        using var ink = new SolidBrush(Ink);
        g.DrawString($"{percent:0} %", font, ink, new RectangleF(x + 44, top, 80, RowHeight), format);
    }

    static void DrawTeacherNote(Graphics g, float y)
    {
        using var heading = new Font("Segoe UI Semibold", 24, GraphicsUnit.Pixel);
        using var subtle = new SolidBrush(Subtle);
        using var line = new Pen(Line, 1.5f);
        g.DrawString(T("Poznámka učitele", "Teacher's note"), heading, subtle, Margin, y);
        for (int i = 1; i <= 3; i++)
            g.DrawLine(line, Margin, y + 30 + i * 52, Width - Margin, y + 30 + i * 52);
        g.DrawString(T("Podpis", "Signature"), heading, subtle, Width - Margin - 360, y + 220);
        g.DrawLine(line, Width - Margin - 260, y + 248, Width - Margin, y + 248);
    }

    static void DrawFooter(Graphics g, int page)
    {
        using var font = new Font("Segoe UI", 17, GraphicsUnit.Pixel);
        using var subtle = new SolidBrush(Subtle);
        g.DrawString($"Keyboard Utils · {DateTime.Now:d. M. yyyy H:mm} · {T("strana", "page")} {page}", font, subtle, Margin, Height - Margin + 20);
    }

    static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = 2 * radius;
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
