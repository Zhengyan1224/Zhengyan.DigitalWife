using SharpCompress.Common;
using SharpCompress.Compressors.BZip2;
using SharpCompress.Readers;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: <archivePath>");
    return 1;
}

var archivePath = args[0];
await using var stream = File.OpenRead(archivePath);
using var bz2 = new BZip2Stream(stream, SharpCompress.Compressors.CompressionMode.Decompress, false);
using var reader = ReaderFactory.Open(bz2);
var index = 0;
while (reader.MoveToNextEntry() && index < 40)
{
    Console.WriteLine($"{reader.Entry.Key} | dir={reader.Entry.IsDirectory}");
    index++;
}

return 0;
