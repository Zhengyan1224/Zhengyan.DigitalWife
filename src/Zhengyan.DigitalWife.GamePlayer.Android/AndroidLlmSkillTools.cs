using System.Text;
using System.Text.Json;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal static class AndroidSkillToolCatalog
{
    private const int MaxReadBytes = 1024 * 1024;

    public static IReadOnlyList<RuntimeLlmTool> Create(string projectDirectory, GameProjectLlmSettings settings)
    {
        List<RuntimeLlmTool> tools = [];
        if (settings.EnableMemory)
        {
            tools.Add(Tool("memory_search", "Search saved memory text files.", "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"},\"maxResults\":{\"type\":\"integer\"}}}"));
            tools.Add(Tool("memory_read", "Read a saved memory file.", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"maxBytes\":{\"type\":\"integer\"}},\"required\":[\"path\"]}"));
            tools.Add(Tool("memory_write", "Write a saved memory file.", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"content\":{\"type\":\"string\"},\"append\":{\"type\":\"boolean\"}},\"required\":[\"path\",\"content\"]}"));
        }
        if (settings.EnableSkills)
        {
            tools.Add(Tool("skill_list", "List project skills.", "{\"type\":\"object\",\"properties\":{\"includeContent\":{\"type\":\"boolean\"}}}"));
            tools.Add(Tool("skill_read", "Read a skill markdown file.", "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"},\"path\":{\"type\":\"string\"}},\"required\":[\"name\"]}"));
            tools.Add(Tool("skill_list_files", "List files in a skill or project directory.", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"skillName\":{\"type\":\"string\"},\"recursive\":{\"type\":\"boolean\"}}}"));
            tools.Add(Tool("skill_read_file", "Read a project-relative skill file.", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"maxBytes\":{\"type\":\"integer\"}},\"required\":[\"path\"]}"));
            tools.Add(Tool("skill_write_file", "Write a project-relative skill file.", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"content\":{\"type\":\"string\"},\"append\":{\"type\":\"boolean\"}},\"required\":[\"path\",\"content\"]}"));
            tools.Add(Tool("skill_search_files", "Search text in project skill files.", "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"},\"path\":{\"type\":\"string\"},\"maxResults\":{\"type\":\"integer\"}},\"required\":[\"query\"]}"));
        }
        return tools;
    }

    public static Task<string> InvokeAsync(string projectDirectory, string name, string argumentsJson, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using JsonDocument document = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            JsonElement args = document.RootElement;
            string result = name switch
            {
                "memory_search" => SearchMemory(projectDirectory, args),
                "memory_read" => ReadMemory(projectDirectory, args),
                "memory_write" => WriteMemory(projectDirectory, args),
                "skill_list" => ListSkills(projectDirectory, args),
                "skill_read" => ReadSkill(projectDirectory, args),
                "skill_list_files" => ListFiles(projectDirectory, args),
                "skill_read_file" => ReadProjectFile(projectDirectory, args),
                "skill_write_file" => WriteProjectFile(projectDirectory, args),
                "skill_search_files" => SearchFiles(projectDirectory, args),
                _ => JsonSerializer.Serialize(new { ok = false, error = $"Tool '{name}' is not registered." })
            };
            return Task.FromResult(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromResult(JsonSerializer.Serialize(new { ok = false, error = ex.Message }));
        }
    }

    private static RuntimeLlmTool Tool(string name, string description, string schema) => new(name, description, schema);

    private static string SearchMemory(string projectDirectory, JsonElement args)
    {
        string query = GetString(args, "query");
        string root = MemoryRoot(projectDirectory);
        List<object> matches = [];
        if (Directory.Exists(root))
        {
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (matches.Count >= GetInt(args, "maxResults", 20) || new FileInfo(file).Length > MaxReadBytes) break;
                string content = File.ReadAllText(file);
                if (content.Contains(query, StringComparison.OrdinalIgnoreCase))
                    matches.Add(new { path = Path.GetRelativePath(root, file).Replace('\\', '/'), preview = content[..Math.Min(content.Length, 300)] });
            }
        }
        return JsonSerializer.Serialize(new { ok = true, query, matches });
    }

    private static string ReadMemory(string projectDirectory, JsonElement args) => ReadText(SafePath(MemoryRoot(projectDirectory), GetRequiredString(args, "path")), args);

    private static string WriteMemory(string projectDirectory, JsonElement args)
    {
        string path = SafePath(MemoryRoot(projectDirectory), GetRequiredString(args, "path"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string content = GetString(args, "content");
        if (GetBool(args, "append")) File.AppendAllText(path, content, Encoding.UTF8); else File.WriteAllText(path, content, Encoding.UTF8);
        return JsonSerializer.Serialize(new { ok = true, path });
    }

    private static string ListSkills(string projectDirectory, JsonElement args)
    {
        string root = Path.Combine(projectDirectory, "skills");
        object[] skills = Directory.Exists(root)
            ? Directory.EnumerateDirectories(root).Select(path => new { name = Path.GetFileName(path), path = Path.GetRelativePath(projectDirectory, path).Replace('\\', '/') }).Cast<object>().ToArray()
            : [];
        return JsonSerializer.Serialize(new { ok = true, skills });
    }

    private static string ReadSkill(string projectDirectory, JsonElement args)
    {
        string name = GetRequiredString(args, "name");
        string relative = args.TryGetProperty("path", out JsonElement path) && path.ValueKind == JsonValueKind.String ? path.GetString() ?? "SKILL.md" : "SKILL.md";
        return ReadText(SafePath(Path.Combine(projectDirectory, "skills", name), relative), args);
    }

    private static string ListFiles(string projectDirectory, JsonElement args)
    {
        string root = projectDirectory;
        if (args.TryGetProperty("skillName", out JsonElement skill) && skill.ValueKind == JsonValueKind.String) root = Path.Combine(root, "skills", skill.GetString() ?? string.Empty);
        if (args.TryGetProperty("path", out JsonElement path) && path.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(path.GetString())) root = SafePath(root, path.GetString()!);
        SearchOption option = GetBool(args, "recursive") ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        string[] files = Directory.Exists(root) ? Directory.EnumerateFiles(root, "*", option).Select(file => Path.GetRelativePath(projectDirectory, file).Replace('\\', '/')).Take(200).ToArray() : [];
        return JsonSerializer.Serialize(new { ok = true, files });
    }

    private static string ReadProjectFile(string projectDirectory, JsonElement args) => ReadText(SafePath(projectDirectory, GetRequiredString(args, "path")), args);

    private static string WriteProjectFile(string projectDirectory, JsonElement args)
    {
        string path = SafePath(projectDirectory, GetRequiredString(args, "path"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (GetBool(args, "append")) File.AppendAllText(path, GetString(args, "content"), Encoding.UTF8); else File.WriteAllText(path, GetString(args, "content"), Encoding.UTF8);
        return JsonSerializer.Serialize(new { ok = true, path = Path.GetRelativePath(projectDirectory, path).Replace('\\', '/') });
    }

    private static string SearchFiles(string projectDirectory, JsonElement args)
    {
        string query = GetRequiredString(args, "query");
        string root = projectDirectory;
        if (args.TryGetProperty("path", out JsonElement path) && path.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(path.GetString())) root = SafePath(root, path.GetString()!);
        List<object> matches = [];
        if (Directory.Exists(root)) foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (matches.Count >= GetInt(args, "maxResults", 50) || new FileInfo(file).Length > MaxReadBytes) break;
            string content = File.ReadAllText(file);
            if (content.Contains(query, StringComparison.OrdinalIgnoreCase)) matches.Add(new { path = Path.GetRelativePath(projectDirectory, file).Replace('\\', '/') });
        }
        return JsonSerializer.Serialize(new { ok = true, matches });
    }

    private static string ReadText(string path, JsonElement args)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(path);
        int maxBytes = Math.Clamp(GetInt(args, "maxBytes", 256 * 1024), 1, MaxReadBytes);
        byte[] bytes = File.ReadAllBytes(path);
        string content = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, maxBytes));
        return JsonSerializer.Serialize(new { ok = true, path, content, truncated = bytes.Length > maxBytes });
    }

    private static string MemoryRoot(string projectDirectory) => Path.Combine(global::Android.App.Application.Context.FilesDir?.AbsolutePath ?? projectDirectory, "saves", "memory");
    private static string SafePath(string root, string relative)
    {
        string fullRoot = Path.GetFullPath(root);
        string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(fullRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Path is outside the allowed directory.");
        return fullPath;
    }
    private static string GetString(JsonElement args, string name) => args.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    private static string GetRequiredString(JsonElement args, string name) => string.IsNullOrWhiteSpace(GetString(args, name)) ? throw new InvalidOperationException($"Argument '{name}' is required.") : GetString(args, name);
    private static int GetInt(JsonElement args, string name, int fallback) => args.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int result) ? result : fallback;
    private static bool GetBool(JsonElement args, string name) => args.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
}
