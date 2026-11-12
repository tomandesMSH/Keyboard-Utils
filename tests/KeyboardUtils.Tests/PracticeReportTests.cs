using System.Text;
using System.Text.RegularExpressions;

namespace KeyboardUtils.Tests;

public class PracticeReportTests
{
    static PracticeEntry Entry(DateTime when, double percent, double minutes, int speed = 100, Dictionary<int, int>? bars = null) =>
        new(when, percent, 10, 10, 0, 0, minutes * 60, true, LearningMode.WaitForNote, speed, ["Pravá ruka"], null, null, bars);

    static readonly DateOnly Today = new(2026, 11, 12); // a Thursday

    static PracticeHistory History()
    {
        var history = new PracticeHistory();
        var day = Today.ToDateTime(new TimeOnly(17, 0));
        string ode = PracticeHistory.KeyFor(@"C:\Songs\Óda na radost.mid", "x");
        history.Add(ode, Entry(day.AddDays(-20), 40, 10));                       // before the period
        history.Add(ode, Entry(day.AddDays(-3), 70, 15, 80, new() { [3] = 4 }));
        history.Add(ode, Entry(day.AddDays(-2), 85, 10, 90, new() { [3] = 2 }));
        history.Add(ode, Entry(day.AddDays(-1), 80, 12, 100));
        history.Add("title:Moje nahrávka", Entry(day, 95, 25));
        return history;
    }

    [Fact]
    public void Only_the_period_counts()
    {
        var report = PracticeReport.Build(History(), Today.AddDays(-6), Today, 20, Today);
        Assert.Equal(7, report.Days);
        Assert.Equal(62, report.Minutes, 3);
        Assert.Equal(4, report.Attempts);
        Assert.Equal(4, report.DaysPractised);
        Assert.Equal(1, report.GoalDaysMet); // only today's 25 min reaches 20
        Assert.Equal(7, report.PerDay.Count);
        Assert.Equal(25, report.PerDay[^1].Minutes, 3);
    }

    [Fact]
    public void Songs_by_minutes_with_best_last_speed_and_hard_spots()
    {
        var report = PracticeReport.Build(History(), Today.AddDays(-6), Today, 20, Today);
        Assert.Equal(["Óda na radost", "Moje nahrávka"], report.Songs.Select(s => s.Title));
        var ode = report.Songs[0];
        Assert.Equal(3, ode.Attempts);
        Assert.Equal(37, ode.Minutes, 3);
        Assert.Equal(85, ode.BestPercent);
        Assert.Equal(80, ode.LastPercent);
        Assert.Equal((80, 100), (ode.SlowestSpeed, ode.FastestSpeed));
        Assert.Equal("3", Assert.Single(ode.HardSpots).Bars);
    }

    [Fact]
    public void Streak_counts_goal_days_up_to_the_end()
    {
        var history = new PracticeHistory();
        for (int d = 0; d < 3; d++)
            history.Add("title:a", Entry(Today.AddDays(-d).ToDateTime(new TimeOnly(10, 0)), 90, 21));
        Assert.Equal(3, PracticeReport.Build(history, Today.AddDays(-6), Today, 20, Today).Streak);
        Assert.Equal(1, PracticeReport.Build(history, Today.AddDays(-6), Today.AddDays(-2), 20, Today).Streak);
    }

    [Fact]
    public void Empty_period_still_makes_a_report()
    {
        var report = PracticeReport.Build(new PracticeHistory(), Today.AddDays(-6), Today, 20, Today);
        Assert.Empty(report.Songs);
        Assert.Equal(0, report.Minutes);
        var pages = PracticeReportRenderer.Render(report, "", Instrument.Piano);
        Assert.Single(pages);
        pages.ForEach(p => p.Dispose());
    }

    [Fact]
    public void Periods()
    {
        Assert.Equal((new DateOnly(2026, 11, 9), Today), PracticeReport.ThisWeek(Today)); // Monday..Thursday
        Assert.Equal((new DateOnly(2026, 11, 6), Today), PracticeReport.LastDays(Today, 7));
        Assert.Equal((new DateOnly(2026, 11, 1), Today), PracticeReport.ThisMonth(Today));
        Assert.Equal("Moje nahrávka", PracticeReport.TitleOf("title:Moje nahrávka"));
        Assert.Equal("Óda", PracticeReport.TitleOf(@"C:\x\Óda.musicxml"));
    }

    [Fact]
    public void Many_songs_continue_on_more_pages()
    {
        var history = new PracticeHistory();
        var day = Today.ToDateTime(new TimeOnly(9, 0));
        for (int i = 0; i < 60; i++)
            history.Add($"title:Skladba {i}", Entry(day, 50 + i % 50, 1 + i % 7));
        var report = PracticeReport.Build(history, Today.AddDays(-6), Today, 20, Today);
        var pages = PracticeReportRenderer.Render(report, "Jana", Instrument.Piano);
        Assert.True(pages.Count >= 3, $"{pages.Count} pages");
        Assert.All(pages, p => Assert.Equal((1240, 1754), (p.Width, p.Height)));
        pages.ForEach(p => p.Dispose());
    }

    [Fact]
    public void Pdf_has_a_page_per_bitmap_and_a_valid_xref()
    {
        var pages = Enumerable.Range(0, 2).Select(_ => new Bitmap(124, 175)).ToList();
        using var stream = new MemoryStream();
        PdfWriter.Write(stream, pages, "Zpráva – Jana Nováková");
        pages.ForEach(p => p.Dispose());
        byte[] bytes = stream.ToArray();
        string text = Encoding.Latin1.GetString(bytes);

        Assert.StartsWith("%PDF-1.4\n", text);
        Assert.EndsWith("%%EOF\n", text);
        Assert.Contains("/Count 2", text);
        Assert.Equal(2, Regex.Matches(text, "/Subtype /Image").Count);

        // startxref points at the table; every entry points at its "n 0 obj".
        int startxref = int.Parse(Regex.Match(text, @"startxref\n(\d+)\n%%EOF").Groups[1].Value);
        Assert.StartsWith("xref\n", text[startxref..]);
        var entries = Regex.Matches(text[startxref..], @"(\d{10}) 00000 n ").Select(m => int.Parse(m.Groups[1].Value)).ToList();
        Assert.Equal(3 + 3 * 2, entries.Count);
        for (int i = 0; i < entries.Count; i++)
            Assert.StartsWith($"{i + 1} 0 obj", text[entries[i]..]);
        // The title in UTF-16 (any letters).
        Assert.Contains("/Title <FEFF", text);
    }
}
