using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;

namespace Zhengyan.DigitalWife.Assistant.Text;

public sealed class SentenceChunker
{
    private static readonly char[] SentenceTerminators = ['.', '!', '?', '。', '！', '？', '\n'];

    private static readonly char[] ClauseTerminators = [',', ';', ':', '，', '；', '：'];

    private readonly SentenceChunkerOptions _options;

    public SentenceChunker()
        : this(new SentenceChunkerOptions())
    {
    }

    public SentenceChunker(SentenceChunkerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async IAsyncEnumerable<string> ChunkAsync(
        IAsyncEnumerable<string> tokens,
        int maxBufferedCharacters = -1,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        int effectiveMaxBufferedCharacters = maxBufferedCharacters > 0
            ? maxBufferedCharacters
            : _options.MaxBufferedCharacters;

        int effectiveMinClauseCharacters = Math.Max(1, _options.MinClauseCharacters);
        var buffer = new StringBuilder();

        await foreach (var token in tokens.WithCancellation(cancellationToken))
        {
            if (string.IsNullOrEmpty(token))
            {
                continue;
            }

            buffer.Append(token);

            while (TryExtractChunk(buffer, SentenceTerminators, out var sentence))
            {
                yield return sentence;
            }

            if (_options.EnableClauseBoundaries)
            {
                while (TryExtractChunk(buffer, ClauseTerminators, effectiveMinClauseCharacters, out var clause))
                {
                    yield return clause;
                }
            }

            if (buffer.Length >= effectiveMaxBufferedCharacters)
            {
                while (buffer.Length >= effectiveMaxBufferedCharacters)
                {
                    int splitIndex = buffer.ToString().LastIndexOf(' ');
                    if (splitIndex <= 0)
                    {
                        splitIndex = Math.Min(buffer.Length - 1, effectiveMaxBufferedCharacters - 1);
                    }

                    var chunk = buffer.ToString(0, splitIndex + 1).Trim();
                    buffer.Remove(0, splitIndex + 1);
                    if (!string.IsNullOrWhiteSpace(chunk))
                    {
                        yield return chunk;
                    }
                }
            }
        }

        var tail = buffer.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(tail))
        {
            yield return tail;
        }
    }

    private static bool TryExtractChunk(StringBuilder buffer, char[] terminators, out string chunk)
    {
        return TryExtractChunk(buffer, terminators, 1, out chunk);
    }

    private static bool TryExtractChunk(StringBuilder buffer, char[] terminators, int minChunkCharacters, out string chunk)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            if (!terminators.Contains(buffer[i]))
            {
                continue;
            }

            if (i + 1 < minChunkCharacters)
            {
                continue;
            }

            chunk = buffer.ToString(0, i + 1).Trim();
            buffer.Remove(0, i + 1);
            return !string.IsNullOrWhiteSpace(chunk);
        }

        chunk = string.Empty;
        return false;
    }
}

