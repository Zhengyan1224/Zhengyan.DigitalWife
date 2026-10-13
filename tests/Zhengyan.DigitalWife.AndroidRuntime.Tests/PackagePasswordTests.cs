using System.Security.Cryptography;
using Zhengyan.DigitalWife.GameProjects;

internal static class PackagePasswordTests
{
    public static void TestOpening()
    {
        string root = Path.Combine(Path.GetTempPath(), "dw-password-test-" + Guid.NewGuid().ToString("N"));
        string? previousPassword = Environment.GetEnvironmentVariable(GameProjectPackage.PasswordEnvironmentVariable);
        Environment.SetEnvironmentVariable(GameProjectPackage.PasswordEnvironmentVariable, null);
        try
        {
            string projectRoot = Path.Combine(root, "project");
            Directory.CreateDirectory(projectRoot);
            GameProjectStore.Save(projectRoot, new GameProject { Name = "Password test" });
            // Incompressible data exercises multiple encryption chunks and split packages.
            byte[] payload = RandomNumberGenerator.GetBytes(1024 * 1024 + 4096);
            File.WriteAllBytes(Path.Combine(projectRoot, "payload.bin"), payload);
            const string password = "  测试 password  ";
            foreach (bool split in new[] { false, true })
            {
                string attemptRoot = Path.Combine(root, split ? "split" : "single");
                GameProjectPackageBuildResult package = GameProjectPackage.Create(projectRoot, new()
                {
                    OutputPath = Path.Combine(attemptRoot, "game.dwgame"),
                    Password = password,
                    SplitPartSizeBytes = split ? 1024 * 1024 : 0
                });
                Check(package.Encrypted && package.Split == split, "Incorrect test package format.");
                string path = package.PartPaths[0];
                ExpectPasswordError(null, GameProjectPackagePasswordError.Required, "missing");
                ExpectPasswordError("wrong-password", GameProjectPackagePasswordError.Invalid, "wrong");
                ExpectPasswordError("another-wrong-password", GameProjectPackagePasswordError.Invalid, "retry");
                using GameProjectPackageSession opened = GameProjectPackage.OpenOrExtract(path, Options(password, "correct"));
                Check(File.ReadAllBytes(Path.Combine(opened.ProjectDirectory, "payload.bin")).SequenceEqual(payload),
                    "Correct password after retries did not restore the original data.");
                Check(GameProjectStore.Load(opened.ProjectDirectory).Name == "Password test", "Decrypted project is not loadable.");
                ExpectPasswordError(null, GameProjectPackagePasswordError.Required, "reopen");
                Check(File.Exists(Path.Combine(opened.ProjectDirectory, "payload.bin")), "Another attempt removed the active project.");
                Check(!Directory.Exists(Path.Combine(attemptRoot, "cache")), "Encrypted package was placed in the persistent cache.");

                // Preserve the existing desktop environment-variable input as well.
                Environment.SetEnvironmentVariable(GameProjectPackage.PasswordEnvironmentVariable, password);
                try
                {
                    using GameProjectPackageSession fromEnvironment = GameProjectPackage.OpenOrExtract(path, Options(null, "environment"));
                    Check(File.Exists(Path.Combine(fromEnvironment.ProjectDirectory, "payload.bin")), "Environment password stopped working.");
                }
                finally { Environment.SetEnvironmentVariable(GameProjectPackage.PasswordEnvironmentVariable, null); }

                GameProjectPackageOpenOptions Options(string? value, string attempt) => new()
                {
                    Password = value,
                    TempRootDirectory = Path.Combine(attemptRoot, attempt),
                    SaveDirectory = Path.Combine(attemptRoot, "saves"),
                    PersistentCacheDirectory = Path.Combine(attemptRoot, "cache"),
                    UsePersistentCache = true
                };

                void ExpectPasswordError(string? value, GameProjectPackagePasswordError reason, string attempt)
                {
                    try
                    {
                        using var unexpected = GameProjectPackage.OpenOrExtract(path, Options(value, attempt));
                        throw new InvalidOperationException("Package opened without the correct password.");
                    }
                    catch (GameProjectPackagePasswordException ex)
                    {
                        Check(ex.Reason == reason, $"Expected {reason}, got {ex.Reason}.");
                        if (reason == GameProjectPackagePasswordError.Invalid)
                            Check(ex.InnerException is AuthenticationTagMismatchException, "Invalid password did not exercise authenticated decryption.");
                    }
                    Check(!Directory.Exists(Path.Combine(attemptRoot, attempt)), "Password failure left extraction files behind.");
                }
            }

            GameProjectPackageBuildResult plain = GameProjectPackage.Create(projectRoot,
                new() { OutputPath = Path.Combine(root, "plain.dwgame") });
            using (GameProjectPackageSession opened = GameProjectPackage.OpenOrExtract(plain.OutputPath,
                new() { UsePersistentCache = false, TempRootDirectory = Path.Combine(root, "plain-open"), SaveDirectory = Path.Combine(root, "saves") }))
                Check(File.Exists(Path.Combine(opened.ProjectDirectory, "payload.bin")), "Unencrypted package incorrectly requires a password.");

            byte[] damaged = File.ReadAllBytes(plain.OutputPath);
            damaged[^1] ^= 0x80;
            File.WriteAllBytes(plain.OutputPath, damaged);
            try
            {
                using var unexpected = GameProjectPackage.OpenOrExtract(plain.OutputPath,
                    new() { UsePersistentCache = false, TempRootDirectory = Path.Combine(root, "damaged"), SaveDirectory = Path.Combine(root, "saves") });
                throw new InvalidOperationException("Damaged package opened successfully.");
            }
            catch (CryptographicException ex)
            {
                Check(ex is not GameProjectPackagePasswordException, "Unencrypted integrity failure requested a password.");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(GameProjectPackage.PasswordEnvironmentVariable, previousPassword);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
