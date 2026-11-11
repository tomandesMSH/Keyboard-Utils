namespace KeyboardUtils.Tests;

public class ReleaseNotesTests
{
    [Fact]
    public void Headings_bullets_and_paragraphs()
    {
        var blocks = ReleaseNotes.ToBlocks("""
            ## Novinky

            ### Úpravy nahrávky
            Špatnou notu už nemusíš
            nahrávat znovu:
            - **klik** vybere notu, *Shift* přidá další;
            * `Ctrl + Z` vrátí zpět
            1. první

            ## Stažení
            """);
        Assert.Equal(
            [
                (NoteBlockKind.Heading, "Novinky"),
                (NoteBlockKind.Heading, "Úpravy nahrávky"),
                (NoteBlockKind.Paragraph, "Špatnou notu už nemusíš nahrávat znovu:"),
                (NoteBlockKind.Bullet, "klik vybere notu, Shift přidá další;"),
                (NoteBlockKind.Bullet, "Ctrl + Z vrátí zpět"),
                (NoteBlockKind.Bullet, "první"),
                (NoteBlockKind.Heading, "Stažení"),
            ],
            blocks.Select(b => (b.Kind, b.Text)));
    }

    [Fact]
    public void Bold_runs_stay_bold()
    {
        var bullet = Assert.Single(ReleaseNotes.ToBlocks("- vyber **Soubor > Knihovna** a pak **Štítky**."));
        Assert.Equal(
            [("vyber ", false), ("Soubor > Knihovna", true), (" a pak ", false), ("Štítky", true), (".", false)],
            bullet.Spans.Select(s => (s.Text, s.Bold)));
    }

    [Fact]
    public void Links_keep_only_their_text()
    {
        var block = Assert.Single(ReleaseNotes.ToBlocks("Viz [dokumentace](https://example.com/x) a ![obrázek](a.png)."));
        Assert.Equal("Viz dokumentace a obrázek.", block.Text);
    }

    [Fact]
    public void An_unclosed_bold_mark_is_just_text()
    {
        var block = Assert.Single(ReleaseNotes.ToBlocks("5 ** 2 a snake_case_name"));
        Assert.All(block.Spans, s => Assert.False(s.Bold));
        Assert.Contains("snake_case_name", block.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n\n ")]
    public void Nothing_to_show(string? markdown)
    {
        Assert.Empty(ReleaseNotes.ToBlocks(markdown));
    }

    [Fact]
    public void Notes_come_from_the_release_body()
    {
        Assert.Equal("## Novinky", UpdateChecker.NotesOf("""{ "tag_name": "v1.3.0", "body": "## Novinky" }"""));
        Assert.Null(UpdateChecker.NotesOf("""{ "tag_name": "v1.3.0", "body": "" }"""));
        Assert.Null(UpdateChecker.NotesOf("""{ "tag_name": "v1.3.0", "body": null }"""));
        Assert.Null(UpdateChecker.NotesOf("""{ "tag_name": "v1.3.0" }"""));
    }
}
