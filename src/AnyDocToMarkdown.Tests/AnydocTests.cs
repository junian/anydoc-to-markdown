using System.Text;
using AnyDocToMarkdown.Model;

namespace AnyDocToMarkdown.Tests;

/// <summary>Smoke test: the bindings load and every entry point round-trips a
/// fixture, mirroring the Node and Python test suites.</summary>
[TestClass]
public sealed class AnydocTests
{
    private readonly AnyDocToMarkdownConverter converter = new();

    private static string Fixture(params string[] path) =>
        Path.Combine(new[] { AppContext.BaseDirectory, "fixtures" }.Concat(path).ToArray());

    private static readonly string Outline = Fixture("docx", "handmade-outline.docx");
    private static readonly string Rich = Fixture("docx", "handmade-rich.docx");
    private static readonly string Csv = Fixture("csv", "sheet.csv");
    private static readonly string Encrypted = Fixture("malformed", "encrypted--errors.odt");
    private static readonly string Zipbomb = Fixture("abuse", "zipbomb--errors.docx");

    [TestMethod]
    public void ToMarkdown_detects_the_format_from_the_file_content()
    {
        string markdown = converter.ToMarkdown(Outline);
        StringAssert.Matches(markdown, new System.Text.RegularExpressions.Regex("(?m)^# "));
    }

    [TestMethod]
    public void ToMarkdownBytes_converts_in_memory_and_round_trips_unicode()
    {
        string markdown = converter.ToMarkdownBytes(File.ReadAllBytes(Rich), Format.Docx);
        StringAssert.Contains(markdown, "| Quarter | Widgets |");
    }

    [TestMethod]
    public void ToMarkdownBytes_detects_the_format_when_none_is_named()
    {
        string markdown = converter.ToMarkdownBytes(File.ReadAllBytes(Rich));
        StringAssert.Contains(markdown, "| Quarter | Widgets |");
        // CSV carries no signature, so it has to be named.
        var csvBytes = File.ReadAllBytes(Csv);
        var exception = Assert.ThrowsExactly<AnydocException>(() => converter.ToMarkdownBytes(csvBytes));
        Assert.AreEqual(ConvertErrorKind.Unsupported, exception.Kind);
        StringAssert.Contains(exception.Message, "unrecognized file content");
        StringAssert.Contains(converter.ToMarkdownBytes(csvBytes, Format.Csv), "| --- |");
    }

    [TestMethod]
    public void ToDocument_exposes_the_document_model()
    {
        Document document = converter.ToDocument(File.ReadAllBytes(Outline));
        Block heading = document.Blocks.First(b => b.Kind == "heading");
        Assert.IsTrue(heading.Level is >= 1 and <= 6);
        Assert.IsInstanceOfType(heading.Content![0].Text, typeof(string));
        Assert.AreEqual("text", heading.Content[0].Kind);
        Assert.IsNotNull(heading.Content[0].Style);
    }

    [TestMethod]
    public void ToDocument_carries_embedded_assets_as_bytes()
    {
        Document document = converter.ToDocument(File.ReadAllBytes(Rich));
        Asset image = document.Assets.Single(a => a.MediaType == "image/png");
        Assert.IsNotNull(image.Data);
        Assert.IsNotEmpty(image.Data);
        Assert.AreEqual(image.Id, document.Assets.IndexOf(image));
    }

    [TestMethod]
    [DataRow(".pptm", Format.Pptx)]
    [DataRow("xls", Format.Excel)]
    public void DetectFormatByExtension_maps_container_variants(string extension, Format expected)
    {
        Assert.AreEqual(expected, converter.DetectFormatByExtension(extension));
    }

    [TestMethod]
    public void DetectFormat_reads_content_extension_and_path()
    {
        Assert.AreEqual(Format.Docx, converter.DetectFormat(File.ReadAllBytes(Rich)));
        // CSV carries no signature: only the extension names it.
        Assert.IsNull(converter.DetectFormat(File.ReadAllBytes(Csv)));
        Assert.AreEqual(Format.Odt, converter.DetectFormatByPath("report.odt"));
        Assert.IsNull(converter.DetectFormatByPath("report.unknown"));
    }

    [TestMethod]
    public void Conversion_errors_throw_the_kind_that_names_the_failure()
    {
        // Nothing about these bytes is a package part (Malformed).
        var malformed = Assert.ThrowsExactly<AnydocException>(() =>
            converter.ToMarkdownBytes(Encoding.UTF8.GetBytes("not a document"), Format.Docx));
        Assert.AreEqual(ConvertErrorKind.Malformed, malformed.Kind);

        var unsupported = Assert.ThrowsExactly<AnydocException>(() =>
            converter.ToMarkdownBytes(File.ReadAllBytes(Csv)));
        Assert.AreEqual(ConvertErrorKind.Unsupported, unsupported.Kind);

        var encrypted = Assert.ThrowsExactly<AnydocException>(() =>
            converter.ToMarkdownBytes(File.ReadAllBytes(Encrypted), Format.Odt));
        Assert.AreEqual(ConvertErrorKind.Encrypted, encrypted.Kind);

        var limit = Assert.ThrowsExactly<AnydocException>(() =>
            converter.ToMarkdownBytes(File.ReadAllBytes(Zipbomb), Format.Docx));
        Assert.AreEqual(ConvertErrorKind.ResourceLimit, limit.Kind);
        // The message carries the limit name, matching the Rust detail.
        StringAssert.Contains(limit.Message!, "max_entry_bytes");

        // A readable package carrying none of the parts a docx is made of.
        Assert.AreEqual(ConvertErrorKind.MissingPart,
            Assert.ThrowsExactly<AnydocException>(() => ToMarkdownOfEmptyDocx()).Kind);
    }

    [TestMethod]
    public async Task Async_variants_match_the_sync_results()
    {
        byte[] outline = File.ReadAllBytes(Outline);
        byte[] rich = File.ReadAllBytes(Rich);

        Assert.AreEqual(converter.ToMarkdown(Outline), await converter.ToMarkdownAsync(Outline));
        Assert.AreEqual(converter.ToMarkdownBytes(outline), await converter.ToMarkdownBytesAsync(outline));
        Assert.AreEqual(
            converter.ToMarkdownBytes(rich, Format.Docx),
            await converter.ToMarkdownBytesAsync(rich, Format.Docx));

        Document document = await converter.ToDocumentAsync(outline);
        Assert.IsTrue(document.Blocks.Any(b => b.Kind == "heading"));
    }

    [TestMethod]
    public void An_unreadable_file_raises_the_io_kind()
    {
        var exception = Assert.ThrowsExactly<AnydocException>(() => converter.ToMarkdown("no-such-file.docx"));
        Assert.AreEqual(ConvertErrorKind.Io, exception.Kind);
    }

    /// <summary>A ZIP package without the parts a docx needs -> missingPart.</summary>
    private static string ToMarkdownOfEmptyDocx()
    {
        using var package = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(package, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry("[Content_Types].xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("<Types/>");
        }
        return new AnyDocToMarkdownConverter().ToMarkdownBytes(package.ToArray(), Format.Docx);
    }
}