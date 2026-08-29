using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zhengyan.DigitalWife.GameProjects;

public sealed class GameProjectPackageBuildOptions
{
    public string OutputPath { get; set; } = string.Empty;

    public string? Password { get; set; }

    public long SplitPartSizeBytes { get; set; }

    public bool IncludeSaves { get; set; }
}

public sealed class GameProjectPackageBuildResult
{
    public string OutputPath { get; init; } = string.Empty;

    public IReadOnlyList<string> PartPaths { get; init; } = [];

    public long TotalBytes { get; init; }

    public bool Encrypted { get; init; }

    public bool Split => PartPaths.Count > 1;
}

public sealed class GameProjectPackageOpenOptions
{
    public string? Password { get; set; }

    public string? SaveDirectory { get; set; }

    public string? TempRootDirectory { get; set; }

    /// <summary>
    /// Optional application-owned root for extracted package cache data.
    /// When omitted, the platform-local default is used.
    /// </summary>
    public string? PersistentCacheDirectory { get; set; }

    public bool UsePersistentCache { get; set; } = true;
}

public sealed class GameProjectPackageSession : IDisposable
{
    private readonly string? _tempRootDirectory;
    private bool _disposed;

    internal GameProjectPackageSession(
        string projectDirectory,
        string saveDirectory,
        string sourcePath,
        bool isPackage,
        string? tempRootDirectory)
    {
        ProjectDirectory = projectDirectory;
        SaveDirectory = saveDirectory;
        SourcePath = sourcePath;
        IsPackage = isPackage;
        _tempRootDirectory = tempRootDirectory;
    }

    public string ProjectDirectory { get; }

    public string SaveDirectory { get; }

    public string SourcePath { get; }

    public bool IsPackage { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (string.IsNullOrWhiteSpace(_tempRootDirectory) || !Directory.Exists(_tempRootDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(_tempRootDirectory, recursive: true);
        }
        catch
        {
            // Best effort cleanup; package temp files are recreated per run.
        }
    }
}

public static class GameProjectPackage
{
    public const string PackageExtension = ".dwgame";
    public const string PasswordEnvironmentVariable = "DW_GAME_PACKAGE_PASSWORD";

    private const string MagicText = "ZDWPKG1";
    private const int MagicLength = 8;
    private const int HeaderLengthBytes = 4;
    private const int CurrentVersion = 1;
    private const int SaltSize = 16;
    private const int BaseNonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int DefaultKdfIterations = 210_000;
    private const int EncryptionChunkSize = 1024 * 1024;
    private const long MinimumSplitPartSizeBytes = 1024 * 1024;

    private static readonly byte[] Magic = Encoding.ASCII.GetBytes(MagicText + "\0");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static bool LooksLikePackagePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string normalized = GameProjectPath.NormalizePathText(path);
        string extension = Path.GetExtension(normalized);
        return string.Equals(extension, PackageExtension, StringComparison.OrdinalIgnoreCase)
            || IsNumericPartExtension(extension)
            || File.Exists(normalized);
    }

    public static GameProjectPackageBuildResult Create(string projectDirectory, GameProjectPackageBuildOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.OutputPath);

        string fullProjectDirectory = Path.GetFullPath(GameProjectPath.NormalizePathText(projectDirectory));
        if (!Directory.Exists(fullProjectDirectory))
        {
            throw new DirectoryNotFoundException($"Project directory not found: {fullProjectDirectory}");
        }

        string projectFile = Path.Combine(fullProjectDirectory, GameProjectStore.ProjectFileName);
        if (!File.Exists(projectFile))
        {
            throw new FileNotFoundException($"Project file not found: {projectFile}", projectFile);
        }

        string outputPath = NormalizeOutputPackagePath(options.OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        string tempDirectory = CreateTempDirectory("pack");
        string zipPath = Path.Combine(tempDirectory, "payload.zip");
        string payloadPath = Path.Combine(tempDirectory, "payload.bin");
        string containerPath = Path.Combine(tempDirectory, "package.dwgame");

        try
        {
            CreateZipPayload(fullProjectDirectory, zipPath, outputPath, options.IncludeSaves);
            string plainSha256 = ComputeSha256Hex(zipPath);
            string? password = NormalizePassword(options.Password);
            PackageCryptoHeader? crypto = null;
            string finalPayloadPath = zipPath;
            long payloadLength = new FileInfo(zipPath).Length;

            if (!string.IsNullOrEmpty(password))
            {
                crypto = EncryptPayload(zipPath, payloadPath, password);
                finalPayloadPath = payloadPath;
                payloadLength = new FileInfo(payloadPath).Length;
            }

            PackageHeader header = new()
            {
                Version = CurrentVersion,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                ProjectFile = GameProjectStore.ProjectFileName,
                PayloadFormat = "zip",
                PayloadLength = payloadLength,
                PlainSha256 = plainSha256,
                Crypto = crypto
            };

            WriteContainer(containerPath, header, finalPayloadPath);
            IReadOnlyList<string> parts = WriteOutputFiles(containerPath, outputPath, options.SplitPartSizeBytes);
            return new GameProjectPackageBuildResult
            {
                OutputPath = outputPath,
                PartPaths = parts,
                TotalBytes = parts.Sum(path => new FileInfo(path).Length),
                Encrypted = crypto is not null
            };
        }
        finally
        {
            TryDeleteDirectory(tempDirectory);
        }
    }

    public static GameProjectPackageSession OpenOrExtract(string inputPath, GameProjectPackageOpenOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        options ??= new GameProjectPackageOpenOptions();

        string normalizedInputPath = Path.GetFullPath(GameProjectPath.NormalizePathText(inputPath));
        if (Directory.Exists(normalizedInputPath))
        {
            string saveDirectory = string.IsNullOrWhiteSpace(options.SaveDirectory)
                ? Path.Combine(normalizedInputPath, "saves")
                : Path.GetFullPath(GameProjectPath.NormalizePathText(options.SaveDirectory));

            Directory.CreateDirectory(saveDirectory);
            return new GameProjectPackageSession(
                normalizedInputPath,
                saveDirectory,
                normalizedInputPath,
                isPackage: false,
                tempRootDirectory: null);
        }

        return ExtractPackage(normalizedInputPath, options);
    }

    public static string GetFirstPartPath(string packagePath)
    {
        string normalized = NormalizeOutputPackagePath(packagePath);
        return normalized + ".001";
    }

    private static GameProjectPackageSession ExtractPackage(string inputPath, GameProjectPackageOpenOptions options)
    {
        PackageInput packageInput = ResolvePackageInput(inputPath);
        PackageHeader packageHeader = ReadPackageInputHeader(packageInput);
        bool usePersistentCache = options.UsePersistentCache
            && packageHeader.Crypto is null
            && (string.IsNullOrWhiteSpace(options.TempRootDirectory)
                || !string.IsNullOrWhiteSpace(options.PersistentCacheDirectory));
        string? cacheKey = usePersistentCache
            ? NormalizeCacheKey(packageHeader.PlainSha256)
            : null;

        string? inputFingerprint = usePersistentCache
            ? ComputePackageInputFingerprint(packageInput)
            : null;

        if (usePersistentCache
            && cacheKey is not null
            && inputFingerprint is not null
            && TryOpenCachedPackage(inputPath, cacheKey, inputFingerprint, options, out GameProjectPackageSession? cachedSession)
            && cachedSession is not null)
        {
            return cachedSession;
        }

        string tempRoot = string.IsNullOrWhiteSpace(options.TempRootDirectory)
            ? CreateTempDirectory("run")
            : Path.GetFullPath(GameProjectPath.NormalizePathText(options.TempRootDirectory));
        Directory.CreateDirectory(tempRoot);

        string? cacheWorkRoot = null;
        string extractDirectory;
        if (usePersistentCache && cacheKey is not null)
        {
            cacheWorkRoot = Path.Combine(CreatePackageCacheRootDirectory(options.PersistentCacheDirectory), cacheKey + ".tmp." + Guid.NewGuid().ToString("N"));
            extractDirectory = Path.Combine(cacheWorkRoot, "project");
        }
        else
        {
            extractDirectory = Path.Combine(tempRoot, "project");
        }

        string zipPath = Path.Combine(tempRoot, "payload.zip");
        string packagePath = packageInput.IsSplit
            ? Path.Combine(tempRoot, "combined.dwgame")
            : packageInput.BasePath;

        try
        {
            if (packageInput.IsSplit)
            {
                CombineSplitParts(packageInput.BasePath, packagePath);
            }

            if (!File.Exists(packagePath))
            {
                throw new FileNotFoundException($"Game package not found: {inputPath}", inputPath);
            }

            ReadPayloadToZip(packagePath, zipPath, options.Password);
            SafeExtractZip(zipPath, extractDirectory);

            string projectPath = Path.Combine(extractDirectory, GameProjectStore.ProjectFileName);
            if (!File.Exists(projectPath))
            {
                throw new InvalidDataException($"Package does not contain {GameProjectStore.ProjectFileName}.");
            }

            string saveDirectory = string.IsNullOrWhiteSpace(options.SaveDirectory)
                ? CreateDefaultPackageSaveDirectory(inputPath, extractDirectory)
                : Path.GetFullPath(GameProjectPath.NormalizePathText(options.SaveDirectory));
            Directory.CreateDirectory(saveDirectory);

            if (usePersistentCache && cacheKey is not null && inputFingerprint is not null && cacheWorkRoot is not null)
            {
                string cacheRoot = GetPackageCacheDirectory(cacheKey, options.PersistentCacheDirectory);
                WriteCacheMarker(cacheWorkRoot, inputPath, cacheKey, inputFingerprint);
                ReplaceCacheDirectory(cacheWorkRoot, cacheRoot);
                cacheWorkRoot = null;
                TryDeleteDirectory(tempRoot);

                string cachedProjectDirectory = Path.Combine(cacheRoot, "project");
                string cachedSaveDirectory = string.IsNullOrWhiteSpace(options.SaveDirectory)
                    ? CreateDefaultPackageSaveDirectory(inputPath, cachedProjectDirectory)
                    : Path.GetFullPath(GameProjectPath.NormalizePathText(options.SaveDirectory));
                Directory.CreateDirectory(cachedSaveDirectory);

                return new GameProjectPackageSession(
                    cachedProjectDirectory,
                    cachedSaveDirectory,
                    inputPath,
                    isPackage: true,
                    tempRootDirectory: null);
            }

            return new GameProjectPackageSession(
                extractDirectory,
                saveDirectory,
                inputPath,
                isPackage: true,
                tempRootDirectory: tempRoot);
        }
        catch
        {
            if (cacheWorkRoot is not null)
            {
                TryDeleteDirectory(cacheWorkRoot);
            }

            TryDeleteDirectory(tempRoot);
            throw;
        }
    }

    private static void CreateZipPayload(string projectDirectory, string zipPath, string outputPath, bool includeSaves)
    {
        string fullProjectDirectory = Path.GetFullPath(projectDirectory);
        string fullOutputPath = Path.GetFullPath(outputPath);
        string outputBasePath = StripNumericPartExtension(fullOutputPath);

        using FileStream zipStream = File.Create(zipPath);
        using ZipArchive archive = new(zipStream, ZipArchiveMode.Create);

        foreach (string filePath in Directory.EnumerateFiles(fullProjectDirectory, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string fullFilePath = Path.GetFullPath(filePath);
            string relativePath = Path.GetRelativePath(fullProjectDirectory, fullFilePath).Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(relativePath) || relativePath.StartsWith("..", StringComparison.Ordinal))
            {
                continue;
            }

            if (!includeSaves && IsUnderTopLevelDirectory(relativePath, "saves"))
            {
                continue;
            }

            if (IsPackageOutputFile(fullFilePath, fullOutputPath, outputBasePath))
            {
                continue;
            }

            ZipArchiveEntry entry = archive.CreateEntry(relativePath, CompressionLevel.Optimal);
            using Stream entryStream = entry.Open();
            using FileStream sourceStream = File.OpenRead(fullFilePath);
            sourceStream.CopyTo(entryStream);
        }
    }

    private static bool IsPackageOutputFile(string fullFilePath, string fullOutputPath, string outputBasePath)
    {
        if (string.Equals(fullFilePath, fullOutputPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (fullFilePath.StartsWith(outputBasePath + ".", StringComparison.OrdinalIgnoreCase))
        {
            string extension = Path.GetExtension(fullFilePath);
            return IsNumericPartExtension(extension);
        }

        return false;
    }

    private static PackageCryptoHeader EncryptPayload(string inputPath, string outputPath, string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] baseNonce = RandomNumberGenerator.GetBytes(BaseNonceSize);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            DefaultKdfIterations,
            HashAlgorithmName.SHA256,
            KeySize);

        using AesGcm aes = new(key, TagSize);
        using FileStream input = File.OpenRead(inputPath);
        using FileStream output = File.Create(outputPath);

        byte[] plainBuffer = new byte[EncryptionChunkSize];
        byte[] lengthBuffer = new byte[4];
        ulong chunkIndex = 0;
        while (true)
        {
            int read = input.Read(plainBuffer, 0, plainBuffer.Length);
            if (read <= 0)
            {
                break;
            }

            byte[] nonce = CreateChunkNonce(baseNonce, chunkIndex);
            byte[] cipherText = new byte[read];
            byte[] tag = new byte[TagSize];
            aes.Encrypt(nonce, plainBuffer.AsSpan(0, read), cipherText, tag);

            BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, read);
            output.Write(lengthBuffer);
            output.Write(cipherText);
            output.Write(tag);
            chunkIndex++;
        }

        CryptographicOperations.ZeroMemory(key);
        return new PackageCryptoHeader
        {
            Algorithm = "AES-256-GCM",
            Kdf = "PBKDF2-SHA256",
            Iterations = DefaultKdfIterations,
            Salt = Convert.ToBase64String(salt),
            BaseNonce = Convert.ToBase64String(baseNonce),
            ChunkSize = EncryptionChunkSize,
            TagSize = TagSize
        };
    }

    private static void DecryptPayload(Stream input, long payloadLength, string outputPath, string password, PackageCryptoHeader crypto)
    {
        byte[] salt = Convert.FromBase64String(crypto.Salt);
        byte[] baseNonce = Convert.FromBase64String(crypto.BaseNonce);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            crypto.Iterations,
            HashAlgorithmName.SHA256,
            KeySize);

        using AesGcm aes = new(key, crypto.TagSize);
        using FileStream output = File.Create(outputPath);

        long remaining = payloadLength;
        ulong chunkIndex = 0;
        byte[] lengthBuffer = new byte[4];
        while (remaining > 0)
        {
            ReadExactly(input, lengthBuffer);
            remaining -= lengthBuffer.Length;
            int plainLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
            if (plainLength <= 0 || plainLength > crypto.ChunkSize || remaining < plainLength + crypto.TagSize)
            {
                throw new InvalidDataException("Invalid encrypted package payload.");
            }

            byte[] cipherText = new byte[plainLength];
            byte[] tag = new byte[crypto.TagSize];
            ReadExactly(input, cipherText);
            ReadExactly(input, tag);
            remaining -= plainLength + crypto.TagSize;

            byte[] plainText = new byte[plainLength];
            byte[] nonce = CreateChunkNonce(baseNonce, chunkIndex);
            aes.Decrypt(nonce, cipherText, tag, plainText);
            output.Write(plainText);
            chunkIndex++;
        }

        CryptographicOperations.ZeroMemory(key);
    }

    private static void WriteContainer(string containerPath, PackageHeader header, string payloadPath)
    {
        byte[] headerBytes = JsonSerializer.SerializeToUtf8Bytes(header, JsonOptions);
        if (headerBytes.Length > int.MaxValue)
        {
            throw new InvalidDataException("Package header is too large.");
        }

        using FileStream output = File.Create(containerPath);
        output.Write(Magic);
        Span<byte> lengthBytes = stackalloc byte[HeaderLengthBytes];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, headerBytes.Length);
        output.Write(lengthBytes);
        output.Write(headerBytes);

        using FileStream payload = File.OpenRead(payloadPath);
        payload.CopyTo(output);
    }

    private static IReadOnlyList<string> WriteOutputFiles(string containerPath, string outputPath, long splitPartSizeBytes)
    {
        long containerLength = new FileInfo(containerPath).Length;
        if (splitPartSizeBytes < MinimumSplitPartSizeBytes || containerLength <= splitPartSizeBytes)
        {
            DeleteOutputFiles(outputPath);
            File.Copy(containerPath, outputPath, overwrite: true);
            return [outputPath];
        }

        string basePath = StripNumericPartExtension(outputPath);
        DeleteOutputFiles(basePath);
        List<string> partPaths = [];
        byte[] buffer = new byte[1024 * 1024];

        using FileStream input = File.OpenRead(containerPath);
        for (int partIndex = 1; input.Position < input.Length; partIndex++)
        {
            string partPath = $"{basePath}.{partIndex:000}";
            long remainingInPart = splitPartSizeBytes;
            using FileStream output = File.Create(partPath);
            while (remainingInPart > 0 && input.Position < input.Length)
            {
                int toRead = (int)Math.Min(buffer.Length, remainingInPart);
                int read = input.Read(buffer, 0, toRead);
                if (read <= 0)
                {
                    break;
                }

                output.Write(buffer, 0, read);
                remainingInPart -= read;
            }

            partPaths.Add(partPath);
        }

        return partPaths;
    }

    private static void ReadPayloadToZip(string packagePath, string zipPath, string? password)
    {
        using FileStream input = File.OpenRead(packagePath);
        PackageHeader header = ReadHeader(input);
        if (header.Version != CurrentVersion || !string.Equals(header.PayloadFormat, "zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Unsupported game package version or payload format: {header.Version}/{header.PayloadFormat}");
        }

        long payloadStart = input.Position;
        long availablePayloadLength = input.Length - payloadStart;
        if (header.PayloadLength < 0 || header.PayloadLength > availablePayloadLength)
        {
            throw new InvalidDataException("Package payload length is invalid.");
        }

        if (header.Crypto is not null)
        {
            string normalizedPassword = NormalizePassword(password)
                ?? Environment.GetEnvironmentVariable(PasswordEnvironmentVariable)
                ?? string.Empty;
            if (string.IsNullOrEmpty(normalizedPassword))
            {
                throw new InvalidOperationException(
                    $"Encrypted package requires a password. Pass --package-password or set {PasswordEnvironmentVariable}.");
            }

            DecryptPayload(input, header.PayloadLength, zipPath, normalizedPassword, header.Crypto);
        }
        else
        {
            CopyExact(input, zipPath, header.PayloadLength);
        }

        string actualHash = ComputeSha256Hex(zipPath);
        if (!string.Equals(actualHash, header.PlainSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new CryptographicException("Package integrity check failed.");
        }
    }

    private static PackageHeader ReadHeader(Stream input)
    {
        byte[] magic = new byte[MagicLength];
        ReadExactly(input, magic);
        if (!magic.AsSpan().SequenceEqual(Magic))
        {
            throw new InvalidDataException("Invalid game package magic header.");
        }

        byte[] lengthBytes = new byte[HeaderLengthBytes];
        ReadExactly(input, lengthBytes);
        int headerLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (headerLength <= 0 || headerLength > 1024 * 1024)
        {
            throw new InvalidDataException("Invalid game package header length.");
        }

        byte[] headerBytes = new byte[headerLength];
        ReadExactly(input, headerBytes);
        return JsonSerializer.Deserialize<PackageHeader>(headerBytes, JsonOptions)
            ?? throw new InvalidDataException("Invalid game package header.");
    }

    private static void SafeExtractZip(string zipPath, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        string fullTargetDirectory = Path.GetFullPath(targetDirectory);
        string fullTargetRoot = fullTargetDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? fullTargetDirectory
            : fullTargetDirectory + Path.DirectorySeparatorChar;

        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string destinationPath = Path.GetFullPath(Path.Combine(targetDirectory, entry.FullName));
            if (!destinationPath.StartsWith(fullTargetRoot, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(destinationPath, fullTargetDirectory, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Package entry escapes target directory: {entry.FullName}");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static void CombineSplitParts(string splitBasePath, string outputPath)
    {
        string basePath = StripNumericPartExtension(splitBasePath);
        string firstPartPath = $"{basePath}.001";
        if (!File.Exists(firstPartPath))
        {
            throw new FileNotFoundException($"First package part not found: {firstPartPath}", firstPartPath);
        }

        using FileStream output = File.Create(outputPath);
        byte[] buffer = new byte[1024 * 1024];
        for (int partIndex = 1; ; partIndex++)
        {
            string partPath = $"{basePath}.{partIndex:000}";
            if (!File.Exists(partPath))
            {
                if (partIndex == 1)
                {
                    throw new FileNotFoundException($"Package part not found: {partPath}", partPath);
                }

                break;
            }

            using FileStream input = File.OpenRead(partPath);
            input.CopyTo(output, buffer.Length);
        }
    }

    private static PackageInput ResolvePackageInput(string inputPath)
    {
        if (IsSplitPackageInput(inputPath, out string splitBasePath))
        {
            string firstPartPath = $"{splitBasePath}.001";
            if (!File.Exists(firstPartPath))
            {
                throw new FileNotFoundException($"First package part not found: {firstPartPath}", firstPartPath);
            }

            return new PackageInput(splitBasePath, IsSplit: true);
        }

        if (File.Exists(inputPath))
        {
            return new PackageInput(inputPath, IsSplit: false);
        }

        string splitFirstPart = GetFirstPartPath(inputPath);
        if (File.Exists(splitFirstPart))
        {
            return new PackageInput(inputPath, IsSplit: true);
        }

        throw new FileNotFoundException($"Game package not found: {inputPath}", inputPath);
    }

    private static PackageHeader ReadPackageInputHeader(PackageInput packageInput)
    {
        string headerPath = packageInput.IsSplit
            ? $"{packageInput.BasePath}.001"
            : packageInput.BasePath;

        using FileStream input = File.OpenRead(headerPath);
        return ReadHeader(input);
    }

    private static bool TryOpenCachedPackage(
        string inputPath,
        string cacheKey,
        string inputFingerprint,
        GameProjectPackageOpenOptions options,
        out GameProjectPackageSession? session)
    {
        session = null;
        string cacheRoot = GetPackageCacheDirectory(cacheKey, options.PersistentCacheDirectory);
        string markerPath = Path.Combine(cacheRoot, ".dwgame-cache.json");
        string projectDirectory = Path.Combine(cacheRoot, "project");
        string projectFile = Path.Combine(projectDirectory, GameProjectStore.ProjectFileName);
        if (!File.Exists(markerPath) || !File.Exists(projectFile))
        {
            return false;
        }

        try
        {
            PackageCacheMarker? marker = JsonSerializer.Deserialize<PackageCacheMarker>(File.ReadAllBytes(markerPath), JsonOptions);
            if (marker is null
                || marker.Version != CurrentVersion
                || !string.Equals(marker.CacheKey, cacheKey, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(marker.InputFingerprint, inputFingerprint, StringComparison.Ordinal))
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        string saveDirectory = string.IsNullOrWhiteSpace(options.SaveDirectory)
            ? CreateDefaultPackageSaveDirectory(inputPath, projectDirectory)
            : Path.GetFullPath(GameProjectPath.NormalizePathText(options.SaveDirectory));
        Directory.CreateDirectory(saveDirectory);

        session = new GameProjectPackageSession(
            projectDirectory,
            saveDirectory,
            inputPath,
            isPackage: true,
            tempRootDirectory: null);
        return true;
    }

    private static void WriteCacheMarker(string cacheRoot, string sourcePath, string cacheKey, string inputFingerprint)
    {
        PackageCacheMarker marker = new()
        {
            Version = CurrentVersion,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            SourcePath = sourcePath,
            CacheKey = cacheKey,
            InputFingerprint = inputFingerprint
        };

        string markerPath = Path.Combine(cacheRoot, ".dwgame-cache.json");
        File.WriteAllBytes(markerPath, JsonSerializer.SerializeToUtf8Bytes(marker, JsonOptions));
    }

    private static void ReplaceCacheDirectory(string sourceRoot, string targetRoot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetRoot)!);
        if (Directory.Exists(targetRoot))
        {
            TryDeleteDirectory(targetRoot);
        }

        if (Directory.Exists(targetRoot))
        {
            throw new IOException($"Package cache directory is currently in use: {targetRoot}");
        }

        Directory.Move(sourceRoot, targetRoot);
    }

    private static string CreatePackageCacheRootDirectory(string? configuredRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            string normalizedRoot = Path.GetFullPath(GameProjectPath.NormalizePathText(configuredRoot));
            Directory.CreateDirectory(normalizedRoot);
            return normalizedRoot;
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "share");
        }

        string cacheRoot = Path.Combine(localAppData, "Zhengyan.DigitalWife", "GamePlayer", "PackageCache");
        Directory.CreateDirectory(cacheRoot);
        return cacheRoot;
    }

    private static string GetPackageCacheDirectory(string cacheKey, string? configuredRoot = null)
    {
        return Path.Combine(CreatePackageCacheRootDirectory(configuredRoot), cacheKey);
    }

    private static string ComputePackageInputFingerprint(PackageInput packageInput)
    {
        StringBuilder builder = new();
        if (!packageInput.IsSplit)
        {
            AppendFileFingerprint(builder, packageInput.BasePath);
            return builder.ToString();
        }

        string basePath = StripNumericPartExtension(packageInput.BasePath);
        for (int partIndex = 1; ; partIndex++)
        {
            string partPath = $"{basePath}.{partIndex:000}";
            if (!File.Exists(partPath))
            {
                if (partIndex == 1)
                {
                    throw new FileNotFoundException($"Package part not found: {partPath}", partPath);
                }

                break;
            }

            if (builder.Length > 0)
            {
                builder.Append('|');
            }

            builder.Append(partIndex.ToString("000", System.Globalization.CultureInfo.InvariantCulture));
            builder.Append(':');
            AppendFileFingerprint(builder, partPath);
        }

        return builder.ToString();
    }

    private static void AppendFileFingerprint(StringBuilder builder, string path)
    {
        FileInfo info = new(path);
        builder.Append(info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(info.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string CreateDefaultPackageSaveDirectory(string packagePath, string extractedProjectDirectory)
    {
        string name = Path.GetFileNameWithoutExtension(StripNumericPartExtension(packagePath));
        try
        {
            GameProject project = GameProjectStore.Load(extractedProjectDirectory);
            if (!string.IsNullOrWhiteSpace(project.Name))
            {
                name = project.Name;
            }
        }
        catch
        {
        }

        string safeName = ToSafePathSegment(name);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "share");
        }

        return Path.Combine(localAppData, "Zhengyan.DigitalWife", "GamePlayer", "Saves", safeName);
    }

    private static string NormalizeOutputPackagePath(string outputPath)
    {
        string normalized = Path.GetFullPath(GameProjectPath.NormalizePathText(outputPath));
        if (IsNumericPartExtension(Path.GetExtension(normalized)))
        {
            normalized = StripNumericPartExtension(normalized);
        }

        if (!string.Equals(Path.GetExtension(normalized), PackageExtension, StringComparison.OrdinalIgnoreCase))
        {
            normalized += PackageExtension;
        }

        return normalized;
    }

    private static void DeleteOutputFiles(string outputPath)
    {
        string basePath = StripNumericPartExtension(outputPath);
        if (File.Exists(basePath))
        {
            File.Delete(basePath);
        }

        for (int partIndex = 1; ; partIndex++)
        {
            string partPath = $"{basePath}.{partIndex:000}";
            if (!File.Exists(partPath))
            {
                break;
            }

            File.Delete(partPath);
        }
    }

    private static string StripNumericPartExtension(string path)
    {
        string extension = Path.GetExtension(path);
        return IsNumericPartExtension(extension)
            ? path[..^extension.Length]
            : path;
    }

    private static bool IsSplitPackageInput(string path, out string splitBasePath)
    {
        splitBasePath = StripNumericPartExtension(path);
        return IsNumericPartExtension(Path.GetExtension(path));
    }

    private static bool IsNumericPartExtension(string extension)
    {
        return extension.Length == 4
            && extension[0] == '.'
            && char.IsDigit(extension[1])
            && char.IsDigit(extension[2])
            && char.IsDigit(extension[3]);
    }

    private static bool IsUnderTopLevelDirectory(string relativePath, string topLevelDirectory)
    {
        return relativePath.Equals(topLevelDirectory, StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith(topLevelDirectory + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] CreateChunkNonce(byte[] baseNonce, ulong chunkIndex)
    {
        byte[] nonce = (byte[])baseNonce.Clone();
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(BaseNonceSize - sizeof(ulong)), chunkIndex);
        return nonce;
    }

    private static void CopyExact(Stream input, string outputPath, long bytesToCopy)
    {
        using FileStream output = File.Create(outputPath);
        byte[] buffer = new byte[1024 * 1024];
        long remaining = bytesToCopy;
        while (remaining > 0)
        {
            int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read <= 0)
            {
                throw new EndOfStreamException();
            }

            output.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static void ReadExactly(Stream stream, byte[] buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read <= 0)
            {
                throw new EndOfStreamException();
            }

            offset += read;
        }
    }

    private static string ComputeSha256Hex(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }

    private static string? NormalizePassword(string? password)
    {
        return string.IsNullOrEmpty(password) ? null : password;
    }

    private static string NormalizeCacheKey(string plainSha256)
    {
        string key = string.IsNullOrWhiteSpace(plainSha256)
            ? "unknown"
            : plainSha256.Trim().ToUpperInvariant();

        StringBuilder builder = new(key.Length);
        foreach (char ch in key)
        {
            builder.Append(char.IsAsciiHexDigit(ch) ? ch : '_');
        }

        return builder.Length == 0 ? "unknown" : builder.ToString();
    }

    private static string CreateTempDirectory(string purpose)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "Zhengyan.DigitalWife",
            "GamePackages",
            purpose,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static string ToSafePathSegment(string value)
    {
        string safe = string.IsNullOrWhiteSpace(value) ? "Game" : value.Trim();
        foreach (char ch in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(ch, '_');
        }

        return string.IsNullOrWhiteSpace(safe) ? "Game" : safe;
    }

    private readonly record struct PackageInput(string BasePath, bool IsSplit);

    private sealed class PackageCacheMarker
    {
        public int Version { get; set; }

        public DateTimeOffset CreatedAtUtc { get; set; }

        public string SourcePath { get; set; } = string.Empty;

        public string CacheKey { get; set; } = string.Empty;

        public string InputFingerprint { get; set; } = string.Empty;
    }

    private sealed class PackageHeader
    {
        public int Version { get; set; }

        public DateTimeOffset CreatedAtUtc { get; set; }

        public string ProjectFile { get; set; } = GameProjectStore.ProjectFileName;

        public string PayloadFormat { get; set; } = "zip";

        public long PayloadLength { get; set; }

        public string PlainSha256 { get; set; } = string.Empty;

        public PackageCryptoHeader? Crypto { get; set; }
    }

    private sealed class PackageCryptoHeader
    {
        public string Algorithm { get; set; } = string.Empty;

        public string Kdf { get; set; } = string.Empty;

        public int Iterations { get; set; }

        public string Salt { get; set; } = string.Empty;

        public string BaseNonce { get; set; } = string.Empty;

        public int ChunkSize { get; set; }

        public int TagSize { get; set; }
    }
}
