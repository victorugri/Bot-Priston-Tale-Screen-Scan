using OpenCvSharp;

namespace BotPriston.Core.Capture;

/// <summary>
/// Replays saved screenshots (in name order, optionally looping). Used for offline development and tests.
/// </summary>
public sealed class FileCaptureSource : ICaptureSource
{
    private static readonly string[] Extensions = [".png", ".bmp", ".jpg", ".jpeg"];

    private readonly IReadOnlyList<string> _files;
    private int _next;

    public FileCaptureSource(string path, bool loop = true)
    {
        if (File.Exists(path))
        {
            _files = [Path.GetFullPath(path)];
        }
        else if (Directory.Exists(path))
        {
            _files = Directory.EnumerateFiles(path)
                .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        else
        {
            throw new FileNotFoundException($"No image file or directory at '{path}'.", path);
        }

        if (_files.Count == 0)
            throw new InvalidOperationException($"No images found in '{path}'.");

        Loop = loop;
    }

    public string Name => "Files";
    public bool Loop { get; }
    public IReadOnlyList<string> Files => _files;

    public Frame? Grab()
    {
        if (_next >= _files.Count)
        {
            if (!Loop) return null;
            _next = 0;
        }

        var file = _files[_next++];
        var image = Cv2.ImRead(file, ImreadModes.Color);
        if (image.Empty())
        {
            image.Dispose();
            throw new InvalidDataException($"Could not decode image '{file}'.");
        }

        return new Frame(image, File.GetLastWriteTimeUtc(file), file);
    }

    public void Dispose() { }
}
