using AnyDocToMarkdown;
using AnyDocToMarkdown.Model;
using CommunityToolkit.Maui.Storage;

namespace MauiDemo;

public partial class MainPage : ContentPage
{
    private readonly AnyDocToMarkdownConverter _converter = new();

    private string _sourceName = string.Empty;
    private string _markdown = string.Empty;

    public MainPage()
    {
        InitializeComponent();
    }

    private async void OnPickDocumentClicked(object? sender, EventArgs e)
    {
        try
        {
            FileResult? result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select a document to convert",
                FileTypes = new FilePickerFileType(
                    new Dictionary<DevicePlatform, IEnumerable<string>>
                    {
                        { DevicePlatform.iOS, new[] { "public.data" } },
                        { DevicePlatform.MacCatalyst, new[] { "public.data" } },
                        { DevicePlatform.Android, new[] { "*/*" } },
                        { DevicePlatform.WinUI, new[] { "*.doc", "*.docx", "*.odt", "*.pdf", "*.ppt", "*.pptx", "*.rtf", "*.epub", "*.xls", "*.xlsx", "*.ods", "*.odp", "*.csv" } },
                        { DevicePlatform.Tizen, new[] { "*/*" } },
                    }),
            });

            if (result is null)
            {
                return;
            }

            SetBusy(true, $"Reading {result.FileName}…");

            byte[] data;
            using (Stream stream = await result.OpenReadAsync())
            using (var memory = new MemoryStream())
            {
                await stream.CopyToAsync(memory);
                data = memory.ToArray();
            }

            Format? format = _converter.DetectFormat(data) ?? _converter.DetectFormatByPath(result.FileName);

            _sourceName = Path.GetFileNameWithoutExtension(result.FileName);
            SetBusy(true, $"Converting {result.FileName} ({format?.ToString() ?? "unknown"})…");

            _markdown = format is null
                ? await _converter.ToMarkdownBytesAsync(data)
                : await _converter.ToMarkdownBytesAsync(data, format.Value);

            MarkdownEditor.Text = _markdown;
            StatusLabel.Text = $"{result.FileName} → Markdown ({_markdown.Length} characters)";
            CopyButton.IsEnabled = _markdown.Length > 0;
            SaveButton.IsEnabled = _markdown.Length > 0;
        }
        catch (AnydocException ex)
        {
            await DisplayAlertAsync("Conversion failed", ex.Message, "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnCopyClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_markdown))
        {
            return;
        }

        await Clipboard.Default.SetTextAsync(_markdown);
        await DisplayAlertAsync("Copied", "The Markdown output was copied to the clipboard.", "OK");
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_markdown))
        {
            return;
        }

        string fileName = string.IsNullOrEmpty(_sourceName) ? "document" : _sourceName;
        fileName += ".md";

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(_markdown));
        FileSaverResult result = await FileSaver.Default.SaveAsync(fileName, stream);

        if (result.IsSuccessful)
        {
            await DisplayAlertAsync("Saved", $"Markdown saved to {result.FilePath}", "OK");
        }
        else
        {
            await DisplayAlertAsync("Save cancelled", result.Exception?.Message ?? "The file was not saved.", "OK");
        }
    }

    private void SetBusy(bool busy, string? message = null)
    {
        PickButton.IsEnabled = !busy;
        if (message is not null)
        {
            StatusLabel.Text = message;
        }
    }
}
