using System.Net.Http;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;
using SharpCompress.Readers;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  download-file <url> <destination>");
    Console.Error.WriteLine("  download-and-extract-tarbz2 <url> <targetDirectory> [downloadsRoot]");
    return 1;
}

var command = args[0].ToLowerInvariant();
var url = args[1];

return command switch
{
    "download-file" when args.Length >= 3 => await DownloadFileAsync(url, args[2]),
    "download-and-extract-tarbz2" when args.Length >= 3 => await DownloadAndExtractAsync(url, args[2], args.Length >= 4 ? args[3] : null),
    _ => 1
};

static async Task<int> DownloadFileAsync(string url, string destination)
{
    Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? ".");

    if (File.Exists(destination))
    {
        Console.WriteLine($"Skip existing file: {destination}");
        return 0;
    }

    Console.WriteLine($"Downloading {url}");
    using var http = new HttpClient { Timeout = TimeSpan.FromHours(1) };
    await using var responseStream = await http.GetStreamAsync(url);
    await using var fileStream = File.Create(destination);
    await responseStream.CopyToAsync(fileStream);
    return 0;
}

static async Task<int> DownloadAndExtractAsync(string url, string targetDirectory, string? downloadsRoot)
{
    Directory.CreateDirectory(targetDirectory);
    downloadsRoot ??= Path.Combine(targetDirectory, "..", "..", "..", "artifacts", "downloads");
    downloadsRoot = Path.GetFullPath(downloadsRoot);
    Directory.CreateDirectory(downloadsRoot);

    var archiveName = Path.GetFileName(new Uri(url).AbsolutePath);
    var archivePath = Path.Combine(downloadsRoot, archiveName);

    await DownloadFileAsync(url, archivePath);

    Console.WriteLine($"Extracting {archiveName} to {targetDirectory}");
    await using var fileStream = File.OpenRead(archivePath);
    using var bzip2Stream = new BZip2Stream(fileStream, SharpCompress.Compressors.CompressionMode.Decompress, false);
    using var reader = ReaderFactory.Open(bzip2Stream);
    while (reader.MoveToNextEntry())
    {
        if (reader.Entry.IsDirectory)
        {
            continue;
        }

        reader.WriteEntryToDirectory(targetDirectory, new ExtractionOptions
        {
            ExtractFullPath = true,
            Overwrite = true
        });
    }

    return 0;
}
