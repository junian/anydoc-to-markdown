using System;
using System.IO;
using System.Threading.Tasks;
using AnyDocToMarkdown;
using AnyDocToMarkdown.Model;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AvaloniaUIDemo.ViewModels;

/// <summary>Picks a document, converts it to Markdown with the anydoc engine
/// (native binding), and offers copy/save for the result.</summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly AnyDocToMarkdownConverter _converter = new();
    private TopLevel? _topLevel;

    /// <summary>State of the last conversion.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = "No document selected";

    /// <summary>The converted Markdown output.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCopyableMarkdown))]
    public partial string Markdown { get; set; } = string.Empty;

    /// <summary>True once there is Markdown to copy/save.</summary>
    public bool HasCopyableMarkdown => Markdown.Length > 0;

    /// <summary>True while a pick-and-convert operation is running.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>The name the Markdown would be saved as.</summary>
    public string SuggestedFileName { get; set; } = "document.md";

    /// <summary>Attach the window that hosts this view model; needed for the
    /// platform StorageProvider/Clipboard the commands use.</summary>
    public void Attach(TopLevel window) => _topLevel = window;

    [RelayCommand]
    private async Task PickDocumentAsync()
    {
        if (_topLevel is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var files = await _topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select a document to convert",
                AllowMultiple = false,
            });

            if (files.Count == 0)
            {
                return;
            }

            IStorageFile file = files[0];
            Status = $"Reading {file.Name}…";

            byte[] data;
            await using (Stream stream = await file.OpenReadAsync())
            using (var memory = new MemoryStream())
            {
                await stream.CopyToAsync(memory);
                data = memory.ToArray();
            }

            Format? format = _converter.DetectFormat(data) ?? _converter.DetectFormatByPath(file.Name);

            SuggestedFileName = Path.GetFileNameWithoutExtension(file.Name) + ".md";
            Status = $"Converting {file.Name} ({format?.ToString() ?? "unknown"})…";

            Markdown = format is null
                ? await Task.Run(() => _converter.ToMarkdownBytes(data))
                : await Task.Run(() => _converter.ToMarkdownBytes(data, format.Value));

            Status = $"{file.Name} → Markdown ({Markdown.Length} characters)";
        }
        catch (AnydocException ex)
        {
            Status = $"Conversion failed: {ex.Message}";
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CopyMarkdownAsync()
    {
        if (_topLevel is null || string.IsNullOrEmpty(Markdown))
        {
            return;
        }

        IClipboard? clipboard = _topLevel.Clipboard;
        if (clipboard is null)
        {
            Status = "Clipboard is not available on this platform.";
            return;
        }

        await clipboard.SetTextAsync(Markdown);
        Status = "Markdown copied to the clipboard.";
    }

    [RelayCommand]
    private async Task SaveMarkdownAsync()
    {
        if (_topLevel is null || string.IsNullOrEmpty(Markdown))
        {
            return;
        }

        var file = await _topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Markdown",
            SuggestedFileName = SuggestedFileName,
            DefaultExtension = "md",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Markdown") { Patterns = new[] { "*.md" } },
                new FilePickerFileType("Text") { Patterns = new[] { "*.txt" } },
                FilePickerFileTypes.All,
            },
        });

        if (file is null)
        {
            Status = "Save cancelled.";
            return;
        }

        await using (Stream stream = await file.OpenWriteAsync())
        using (var writer = new StreamWriter(stream))
        {
            await writer.WriteAsync(Markdown);
        }

        Status = $"Saved {file.Name}.";
    }
}