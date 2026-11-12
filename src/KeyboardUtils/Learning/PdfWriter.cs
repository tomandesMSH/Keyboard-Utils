using System.Drawing.Imaging;
using System.Text;

namespace KeyboardUtils;

/// <summary>
/// A minimal PDF 1.4 writer: each page is a picture (JPEG) filling an A4 page. No dependency, and whatever the page
/// shows - Czech letters included - comes out exactly as drawn; the text just isn't selectable.
/// </summary>
static class PdfWriter
{
    const string A4 = "595.28 841.89"; // points

    public static void Write(string path, IReadOnlyList<Bitmap> pages, string title)
    {
        using var file = File.Create(path);
        Write(file, pages, title);
    }

    public static void Write(Stream output, IReadOnlyList<Bitmap> pages, string title)
    {
        var offsets = new List<long>();
        long position = 0;
        void Raw(byte[] bytes)
        {
            output.Write(bytes);
            position += bytes.Length;
        }
        void Text(string text) => Raw(Encoding.ASCII.GetBytes(text));
        void BeginObject(int number)
        {
            while (offsets.Count < number)
                offsets.Add(0);
            offsets[number - 1] = position;
            Text($"{number} 0 obj\n");
        }

        // Objects: 1 catalog, 2 page tree, 3 info, then three per page (page, its contents, its image).
        int PageObject(int i) => 4 + 3 * i;
        Text("%PDF-1.4\n");
        Raw([0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A]); // a comment of high bytes: tells tools the file is binary

        BeginObject(1);
        Text("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        BeginObject(2);
        Text($"<< /Type /Pages /Count {pages.Count} /Kids [{string.Join(" ", pages.Select((_, i) => $"{PageObject(i)} 0 R"))}] >>\nendobj\n");
        BeginObject(3);
        Text($"<< /Title {PdfString(title)} /Producer (Keyboard Utils) /CreationDate (D:{DateTime.Now:yyyyMMddHHmmss}) >>\nendobj\n");

        var jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var quality = new EncoderParameters(1);
        quality.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 88L);
        for (int i = 0; i < pages.Count; i++)
        {
            int page = PageObject(i), contents = page + 1, image = page + 2;
            BeginObject(page);
            Text($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {A4}] /Resources << /XObject << /Im0 {image} 0 R >> >> /Contents {contents} 0 R >>\nendobj\n");

            byte[] draw = Encoding.ASCII.GetBytes($"q {A4.Split(' ')[0]} 0 0 {A4.Split(' ')[1]} 0 0 cm /Im0 Do Q\n");
            BeginObject(contents);
            Text($"<< /Length {draw.Length} >>\nstream\n");
            Raw(draw);
            Text("endstream\nendobj\n");

            using var buffer = new MemoryStream();
            pages[i].Save(buffer, jpeg, quality);
            byte[] bytes = buffer.ToArray();
            BeginObject(image);
            Text($"<< /Type /XObject /Subtype /Image /Width {pages[i].Width} /Height {pages[i].Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {bytes.Length} >>\nstream\n");
            Raw(bytes);
            Text("\nendstream\nendobj\n");
        }

        long xref = position;
        Text($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (long offset in offsets)
            Text($"{offset:D10} 00000 n \n");
        Text($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R /Info 3 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }

    /// <summary>A PDF text string: UTF-16 with a byte order mark, as hex (any language).</summary>
    static string PdfString(string text) =>
        "<FEFF" + string.Concat(Encoding.BigEndianUnicode.GetBytes(text).Select(b => b.ToString("X2"))) + ">";
}
