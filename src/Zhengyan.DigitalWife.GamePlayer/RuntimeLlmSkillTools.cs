using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Zhengyan.DigitalWife.GamePlayer;

internal sealed class RuntimeLlmSkillTools
{
    private const int DefaultMaxReadBytes = 256 * 1024;
    private const int MaxReadBytes = 1024 * 1024;
    private const int MaxCommandOutputChars = 40_000;
    private const int MaxSearchFileBytes = 512 * 1024;
    private const int MaxMemoryFileBytes = 256 * 1024;
    private const int MaxMemorySearchFileBytes = 256 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _projectDirectory;
    private readonly string _saveDirectory;
    private readonly string _skillsDirectory;
    private readonly string _memoryDirectory;
    private readonly RuntimeLlmTool[] _tools;

    public RuntimeLlmSkillTools(string projectDirectory, string? saveDirectory = null)
    {
        _projectDirectory = Path.GetFullPath(projectDirectory);
        _saveDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(saveDirectory)
            ? Path.Combine(_projectDirectory, "saves")
            : saveDirectory);
        _skillsDirectory = Path.Combine(_projectDirectory, "skills");
        _memoryDirectory = Path.Combine(_saveDirectory, "memory");
        _tools =
        [
            new RuntimeLlmTool(
                "memory_search",
                "Search long-term memory files stored under the local save memory directory. Use this before answering questions that may depend on remembered user preferences, identity, relationships, tasks, or previous important facts.",
                """
                {
                  "type": "object",
                  "properties": {
                    "query": {
                      "type": "string",
                      "description": "Text to search for in memory files. Use concise keywords from the current user request."
                    },
                    "path": {
                      "type": "string",
                      "description": "Optional memory-relative file or directory path. Defaults to the memory root."
                    },
                    "recursive": {
                      "type": "boolean",
                      "description": "Whether to search subdirectories.",
                      "default": true
                    },
                    "maxResults": {
                      "type": "integer",
                      "description": "Maximum number of matches.",
                      "default": 20
                    }
                  },
                  "required": ["query"],
                  "additionalProperties": false
                }
                """,
                SearchMemoryAsync),
            new RuntimeLlmTool(
                "memory_read",
                "Read a long-term memory markdown/text file from the local save memory directory. Use memory/index.md first when you need to inspect the memory layout.",
                """
                {
                  "type": "object",
                  "properties": {
                    "path": {
                      "type": "string",
                      "description": "Memory-relative file path, for example index.md or user/profile.md."
                    },
                    "maxBytes": {
                      "type": "integer",
                      "description": "Maximum bytes to read.",
                      "default": 262144
                    }
                  },
                  "required": ["path"],
                  "additionalProperties": false
                }
                """,
                ReadMemoryAsync),
            new RuntimeLlmTool(
                "memory_write",
                "Create or append long-term memory content in a markdown/text file under the local save memory directory. Use distilled facts, not raw chat transcripts.",
                """
                {
                  "type": "object",
                  "properties": {
                    "path": {
                      "type": "string",
                      "description": "Memory-relative file path, for example user/preferences.md."
                    },
                    "content": {
                      "type": "string",
                      "description": "Markdown/text content to write."
                    },
                    "append": {
                      "type": "boolean",
                      "description": "Append instead of overwrite.",
                      "default": true
                    },
                    "createDirectories": {
                      "type": "boolean",
                      "description": "Create parent directories if missing.",
                      "default": true
                    }
                  },
                  "required": ["path", "content"],
                  "additionalProperties": false
                }
                """,
                WriteMemoryAsync),
            new RuntimeLlmTool(
                "memory_update",
                "Replace one exact text fragment inside a long-term memory file. Use this to correct, merge, or update existing memory instead of appending conflicting facts.",
                """
                {
                  "type": "object",
                  "properties": {
                    "path": {
                      "type": "string",
                      "description": "Memory-relative file path."
                    },
                    "oldText": {
                      "type": "string",
                      "description": "Exact text fragment to replace."
                    },
                    "newText": {
                      "type": "string",
                      "description": "Replacement text."
                    }
                  },
                  "required": ["path", "oldText", "newText"],
                  "additionalProperties": false
                }
                """,
                UpdateMemoryAsync),
            new RuntimeLlmTool(
                "memory_forget",
                "Forget long-term memory by deleting one memory file or removing an exact text fragment from a memory file. Use only when the user asks to forget/delete/correct memory, or when replacing stale/conflicting memory.",
                """
                {
                  "type": "object",
                  "properties": {
                    "path": {
                      "type": "string",
                      "description": "Memory-relative file path."
                    },
                    "text": {
                      "type": "string",
                      "description": "Optional exact text fragment to remove. When omitted and deleteFile is true, the whole file is deleted."
                    },
                    "deleteFile": {
                      "type": "boolean",
                      "description": "Delete the whole memory file. Defaults to false unless text is empty.",
                      "default": false
                    }
                  },
                  "required": ["path"],
                  "additionalProperties": false
                }
                """,
                ForgetMemoryAsync),
            new RuntimeLlmTool(
                "skill_list",
                "List skills in the project skills/ directory. Use this before reading a skill.",
                """
                {
                  "type": "object",
                  "properties": {
                    "includeContent": {
                      "type": "boolean",
                      "description": "When true, include a short preview from each skill markdown file."
                    },
                    "maxResults": {
                      "type": "integer",
                      "description": "Maximum number of skills to return.",
                      "default": 50
                    }
                  },
                  "additionalProperties": false
                }
                """,
                ListSkillsAsync),
            new RuntimeLlmTool(
                "skill_read",
                "Read a skill markdown file or a file under one skill directory. The skill name must match a direct child directory under skills/. Accepts name, skill, or skillName.",
                """
                {
                  "type": "object",
                  "properties": {
                    "name": {
                      "type": "string",
                      "description": "Skill directory name under skills/."
                    },
                    "skill": {
                      "type": "string",
                      "description": "Alias of name."
                    },
                    "skillName": {
                      "type": "string",
                      "description": "Alias of name."
                    },
                    "path": {
                      "type": "string",
                      "description": "Optional file path inside the skill directory. Defaults to SKILL.md or the first markdown file."
                    },
                    "maxBytes": {
                      "type": "integer",
                      "description": "Maximum bytes to read.",
                      "default": 262144
                    }
                  },
                  "additionalProperties": true
                }
                """,
                ReadSkillAsync),
            new RuntimeLlmTool(
                "skill_list_files",
                "List files or directories inside the project directory or a specific skill directory.",
                """
                {
                  "type": "object",
                  "properties": {
                    "path": {
                      "type": "string",
                      "description": "Project-relative path to list. Defaults to the project root."
                    },
                    "skillName": {
                      "type": "string",
                      "description": "Optional skill directory name. When set, path is resolved inside that skill."
                    },
                    "skill": {
                      "type": "string",
                      "description": "Alias of skillName."
                    },
                    "name": {
                      "type": "string",
                      "description": "Alias of skillName when listing inside one skill."
                    },
                    "recursive": {
                      "type": "boolean",
                      "description": "Whether to list recursively.",
                      "default": false
                    },
                    "maxResults": {
                      "type": "integer",
                      "description": "Maximum number of entries to return.",
                      "default": 100
                    }
                  },
                  "additionalProperties": false
                }
                """,
                ListFilesAsync),
            new RuntimeLlmTool(
                "skill_read_file",
                "Read a UTF-8 text file inside the project directory.",
                """
                {
                  "type": "object",
                  "properties": {
                    "path": {
                      "type": "string",
                      "description": "Project-relative file path."
                    },
                    "maxBytes": {
                      "type": "integer",
                      "description": "Maximum bytes to read.",
                      "default": 262144
                    }
                  },
                  "required": ["path"],
                  "additionalProperties": false
                }
                """,
                ReadFileAsync),
            new RuntimeLlmTool(
                "skill_write_file",
                "Write a UTF-8 text file inside the project directory. Use this for generated scripts, data files, and skill artifacts.",
                """
                {
                  "type": "object",
                  "properties": {
                    "path": {
                      "type": "string",
                      "description": "Project-relative file path."
                    },
                    "content": {
                      "type": "string",
                      "description": "UTF-8 text content to write."
                    },
                    "append": {
                      "type": "boolean",
                      "description": "Append instead of overwrite.",
                      "default": false
                    },
                    "createDirectories": {
                      "type": "boolean",
                      "description": "Create parent directories if missing.",
                      "default": true
                    }
                  },
                  "required": ["path", "content"],
                  "additionalProperties": false
                }
                """,
                WriteFileAsync),
            new RuntimeLlmTool(
                "skill_search_files",
                "Search text in project files or skill files. This is useful for locating scripts, examples, and skill references.",
                """
                {
                  "type": "object",
                  "properties": {
                    "query": {
                      "type": "string",
                      "description": "Text to search for. Case-insensitive."
                    },
                    "path": {
                      "type": "string",
                      "description": "Project-relative directory or file path. Defaults to skills/."
                    },
                    "recursive": {
                      "type": "boolean",
                      "description": "Whether to search recursively.",
                      "default": true
                    },
                    "maxResults": {
                      "type": "integer",
                      "description": "Maximum number of matches.",
                      "default": 50
                    }
                  },
                  "required": ["query"],
                  "additionalProperties": false
                }
                """,
                SearchFilesAsync),
            new RuntimeLlmTool(
                "skill_run_command",
                "Run a shell command with the working directory restricted to the project directory. Use only for trusted local skills.",
                """
                {
                  "type": "object",
                  "properties": {
                    "command": {
                      "type": "string",
                      "description": "Shell command to execute."
                    },
                    "workingDirectory": {
                      "type": "string",
                      "description": "Project-relative working directory. Defaults to the project root."
                    },
                    "timeoutSeconds": {
                      "type": "integer",
                      "description": "Timeout in seconds.",
                      "default": 30
                    }
                  },
                  "required": ["command"],
                  "additionalProperties": false
                }
                """,
                RunCommandAsync)
        ];
    }

    public IReadOnlyList<RuntimeLlmTool> Tools => _tools;

    public string SkillsDirectory => _skillsDirectory;

    public string MemoryDirectory => _memoryDirectory;

    public string GetCharacterMemoryPath(string characterName)
        => $"character/{CreateSafeMemoryFileName(characterName, "character")}.md";

    private Task<string> SearchMemoryAsync(RuntimeLlmToolCall toolCall, CancellationToken cancellationToken)
    {
        return ExecuteToolAsync(toolCall, cancellationToken, args =>
        {
            SearchMemoryArgs parsed = ParseArguments<SearchMemoryArgs>(args);
            if (string.IsNullOrWhiteSpace(parsed.Query))
            {
                throw new InvalidOperationException("Memory search query is required.");
            }

            EnsureMemoryDefaults();
            string startPath = string.IsNullOrWhiteSpace(parsed.Path)
                ? _memoryDirectory
                : ResolveMemoryPath(parsed.Path);
            int maxResults = Math.Clamp(parsed.MaxResults ?? 20, 1, 100);
            SearchOption option = parsed.Recursive != false ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            List<object> matches = [];

            if (!Directory.Exists(_memoryDirectory))
            {
                return new
                {
                    ok = true,
                    query = parsed.Query,
                    memoryDirectory = ToMemoryDisplayPath(_memoryDirectory),
                    matches
                };
            }

            IEnumerable<string> files = File.Exists(startPath)
                ? [startPath]
                : Directory.Exists(startPath)
                    ? Directory.EnumerateFiles(startPath, "*", option)
                    : throw new DirectoryNotFoundException($"Memory search path not found: {ToMemoryDisplayPath(startPath)}");

            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (matches.Count >= maxResults)
                {
                    break;
                }

                if (!IsMemoryTextFile(file) || new FileInfo(file).Length > MaxMemorySearchFileBytes)
                {
                    continue;
                }

                if (Path.GetFileName(file).Contains(parsed.Query, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(new
                    {
                        path = ToMemoryDisplayPath(file),
                        line = 0,
                        preview = Path.GetFileName(file)
                    });
                    if (matches.Count >= maxResults)
                    {
                        break;
                    }
                }

                int lineNumber = 0;
                foreach (string line in File.ReadLines(file, Encoding.UTF8))
                {
                    lineNumber++;
                    if (!line.Contains(parsed.Query, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    matches.Add(new
                    {
                        path = ToMemoryDisplayPath(file),
                        line = lineNumber,
                        preview = TrimPreview(line)
                    });
                    if (matches.Count >= maxResults)
                    {
                        break;
                    }
                }
            }

            return new
            {
                ok = true,
                query = parsed.Query,
                memoryDirectory = ToMemoryDisplayPath(_memoryDirectory),
                path = ToMemoryDisplayPath(startPath),
                matches
            };
        });
    }

    private Task<string> ReadMemoryAsync(RuntimeLlmToolCall toolCall, CancellationToken cancellationToken)
    {
        return ExecuteToolAsync(toolCall, cancellationToken, args =>
        {
            ReadMemoryArgs parsed = ParseArguments<ReadMemoryArgs>(args);
            if (string.IsNullOrWhiteSpace(parsed.Path))
            {
                throw new InvalidOperationException("Memory file path is required.");
            }

            EnsureMemoryDefaults();
            string filePath = ResolveMemoryPath(parsed.Path);
            return ReadMemoryFileResult(filePath, parsed.MaxBytes);
        });
    }

    private Task<string> WriteMemoryAsync(RuntimeLlmToolCall toolCall, CancellationToken cancellationToken)
    {
        return ExecuteToolAsync(toolCall, cancellationToken, args =>
        {
            WriteMemoryArgs parsed = ParseArguments<WriteMemoryArgs>(args);
            if (string.IsNullOrWhiteSpace(parsed.Path))
            {
                throw new InvalidOperationException("Memory file path is required.");
            }

            EnsureMemoryDefaults();
            string filePath = ResolveMemoryPath(parsed.Path);
            string? parent = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(parent) && parsed.CreateDirectories != false)
            {
                Directory.CreateDirectory(parent);
            }

            string content = parsed.Content ?? string.Empty;
            bool append = parsed.Append != false;
            if (append && File.Exists(filePath) && new FileInfo(filePath).Length > 0 && !content.StartsWith('\n'))
            {
                content = Environment.NewLine + content;
            }

            if (append)
            {
                File.AppendAllText(filePath, content, Encoding.UTF8);
            }
            else
            {
                File.WriteAllText(filePath, content, Encoding.UTF8);
            }

            return new
            {
                ok = true,
                path = ToMemoryDisplayPath(filePath),
                bytes = Encoding.UTF8.GetByteCount(content),
                append
            };
        });
    }

    private Task<string> UpdateMemoryAsync(RuntimeLlmToolCall toolCall, CancellationToken cancellationToken)
    {
        return ExecuteToolAsync(toolCall, cancellationToken, args =>
        {
            UpdateMemoryArgs parsed = ParseArguments<UpdateMemoryArgs>(args);
            if (string.IsNullOrWhiteSpace(parsed.Path))
            {
                throw new InvalidOperationException("Memory file path is required.");
            }

            if (string.IsNullOrEmpty(parsed.OldText))
            {
                throw new InvalidOperationException("oldText is required.");
            }

            EnsureMemoryDefaults();
            string filePath = ResolveMemoryPath(parsed.Path);
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Memory file not found: {ToMemoryDisplayPath(filePath)}");
            }

            string current = File.ReadAllText(filePath, Encoding.UTF8);
            int replacements = CountOccurrences(current, parsed.OldText);
            if (replacements == 0)
            {
                return new
                {
                    ok = false,
                    path = ToMemoryDisplayPath(filePath),
                    replacements = 0,
                    error = "oldText was not found in the memory file."
                };
            }

            string updated = current.Replace(parsed.OldText, parsed.NewText ?? string.Empty, StringComparison.Ordinal);
            File.WriteAllText(filePath, updated, Encoding.UTF8);

            return new
            {
                ok = true,
                path = ToMemoryDisplayPath(filePath),
                replacements,
                bytes = Encoding.UTF8.GetByteCount(updated)
            };
        });
    }

    private Task<string> ForgetMemoryAsync(RuntimeLlmToolCall toolCall, CancellationToken cancellationToken)
    {
        return ExecuteToolAsync(toolCall, cancellationToken, args =>
        {
            ForgetMemoryArgs parsed = ParseArguments<ForgetMemoryArgs>(args);
            if (string.IsNullOrWhiteSpace(parsed.Path))
            {
                throw new InvalidOperationException("Memory file path is required.");
            }

            EnsureMemoryDefaults();
            string filePath = ResolveMemoryPath(parsed.Path);
            if (!File.Exists(filePath))
            {
                return new
                {
                    ok = true,
                    path = ToMemoryDisplayPath(filePath),
                    deletedFile = false,
                    removals = 0,
                    alreadyMissing = true
                };
            }

            bool deleteFile = parsed.DeleteFile || string.IsNullOrEmpty(parsed.Text);
            if (deleteFile)
            {
                File.Delete(filePath);
                return new
                {
                    ok = true,
                    path = ToMemoryDisplayPath(filePath),
                    deletedFile = true,
                    removals = 0
                };
            }

            string current = File.ReadAllText(filePath, Encoding.UTF8);
            int removals = CountOccurrences(current, parsed.Text!);
            if (removals == 0)
            {
                return new
                {
                    ok = false,
                    path = ToMemoryDisplayPath(filePath),
                    deletedFile = false,
                    removals = 0,
                    error = "Text was not found in the memory file."
                };
            }

            string updated = current.Replace(parsed.Text!, string.Empty, StringComparison.Ordinal);
            File.WriteAllText(filePath, updated, Encoding.UTF8);
            return new
            {
                ok = true,
                path = ToMemoryDisplayPath(filePath),
                deletedFile = false,
                removals
            };
        });
    }

    private Task<string> ListSkillsAsync(RuntimeLlmToolCall toolCall, CancellationToken cancellationToken)
    {
        return ExecuteToolAsync(toolCall, cancellationToken, args =>
        {
            ListSkillsArgs parsed = ParseArguments<ListSkillsArgs>(args);
            int maxResults = Math.Clamp(parsed.MaxResults ?? 50, 1, 200);
            if (!Directory.Exists(_skillsDirectory))
            {
                return new
                {
                    ok = true,
                    skillsDirectory = ToProjectRelative(_skillsDirectory),
                    skills = Array.Empty<object>()
                };
            }

            var skills = Directory.EnumerateDirectories(_skillsDirectory)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Take(maxResults)
                .Select(directory =>
                {
                    string? markdownPath = FindSkillMarkdownPath(directory);
                    SkillMetadata metadata = markdownPath is null
                        ? new SkillMetadata(Path.GetFileName(directory), string.Empty, string.Empty)
                        : ReadSkillMetadata(directory, markdownPath, parsed.IncludeContent);
                    return new
                    {
                        name = metadata.Name,
                        directory = ToProjectRelative(directory),
                        markdown = markdownPath is null ? string.Empty : ToProjectRelative(markdownPath),
                        description = metadata.Description,
                        preview = metadata.Preview
                    };
                })
                .ToArray();

            return new
            {
                ok = true,
                skillsDirectory = ToProjectRelative(_skillsDirectory),
                skills
            };
        });
    }

    private Task<string> ReadSkillAsync(RuntimeLlmToolCall toolCall, CancellationToken cancellationToken)
    {
        return ExecuteToolAsync(toolCall, cancellationToken, args =>
        {
            ReadSkillArgs parsed = ParseArguments<ReadSkillArgs>(args);
            string skillName = FirstNonWhiteSpace(parsed.Name, parsed.Skill, parsed.SkillName);
            if (string.IsNullOrWhiteSpace(skillName))
            {
                throw new InvalidOperationException("Skill name is required.");
            }

            string skillDirectory = ResolveSkillDirectory(skillName);
            string filePath = string.IsNullOrWhiteSpace(parsed.Path)
                ? FindSkillMarkdownPath(skillDirectory) ?? throw new FileNotFoundException("No SKILL.md or markdown file found for the skill.")
                : ResolveUnderRoot(skillDirectory, parsed.Path);
            return ReadTextFileResult(filePath, parsed.MaxBytes);
        });
    }

    private Task<string> ListFilesAsync(RuntimeLlmToolCall toolCall, CancellationToken cancellationToken)
    {
        return ExecuteToolAsync(toolCall, cancellationToken, args =>
        {
            ListFilesArgs parsed = ParseArguments<ListFilesArgs>(args);
            string skillName = FirstNonWhiteSpace(parsed.SkillName, parsed.Skill, parsed.Name);
            string root = string.IsNullOrWhiteSpace(skillName)
                ? ResolveProjectPath(parsed.Path)
                : ResolveUnderRoot(ResolveSkillDirectory(skillName), parsed.Path);
            if (!Directory.Exists(root))
            {
                if (File.Exists(root))
                {
                    FileInfo file = new(root);
                    return new
                    {
                        ok = true,
                        path = ToProjectRelative(root),
                        entries = new[]
                        {
                            new
                            {
                                path = ToProjectRelative(file.FullName),
                                type = "file",
                                size = file.Length,
                                modifiedUtc = file.LastWriteTimeUtc
                            }
                        }
                    };
                }

                throw new DirectoryNotFoundException($"Directory not found: {ToProjectRelative(root)}");
            }

            int maxResults = Math.Clamp(parsed.MaxResults ?? 100, 1, 500);
            SearchOption option = parsed.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var entries = Directory.EnumerateFileSystemEntries(root, "*", option)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .Take(maxResults)
                .Select(item =>
                {
                    bool isDirectory = Directory.Exists(item);
                    FileInfo? file = isDirectory ? null : new FileInfo(item);
                    DirectoryInfo? directory = isDirectory ? new DirectoryInfo(item) : null;
                    return new
                    {
                        path = ToProjectRelative(item),
                        type = isDirectory ? "directory" : "file",
                        size = file?.Length ?? 0,
                        modifiedUtc = isDirectory ? directory!.LastWriteTimeUtc : file!.LastWriteTimeUtc
                    };
                })
                .ToArray();

            return new
            {
                ok = true,
                path = ToProjectRelative(root),
                entries
            };
        });
    }

    private Task<string> ReadFileAsync(RuntimeLlmToolCall toolCall, CancellationToken cancellationToken)
    {
        return ExecuteToolAsync(toolCall, cancellationToken, args =>
        {
            ReadFileArgs parsed = ParseArguments<ReadFileArgs>(args);
            if (string.IsNullOrWhiteSpace(parsed.Path))
            {
                throw new InvalidOperationException("File path is required.");
            }

            string filePath = ResolveProjectPath(parsed.Path);
            return ReadTextFileResult(filePath, parsed.MaxBytes);
        });
    }

    private Task<string> WriteFileAsync(RuntimeLlmToolCall toolCall, CancellationToken cancellationToken)
    {
        return ExecuteToolAsync(toolCall, cancellationToken, args =>
        {
            WriteFileArgs parsed = ParseArguments<WriteFileArgs>(args);
            if (string.IsNullOrWhiteSpace(parsed.Path))
            {
                throw new InvalidOperationException("File path is required.");
            }

            string filePath = ResolveProjectPath(parsed.Path);
            string? parent = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(parent) && parsed.CreateDirectories != false)
            {
                Directory.CreateDirectory(parent);
            }

            string content = parsed.Content ?? string.Empty;
            if (parsed.Append)
            {
                File.AppendAllText(filePath, content, Encoding.UTF8);
            }
            else
            {
                File.WriteAllText(filePath, content, Encoding.UTF8);
            }

            return new
            {
                ok = true,
                path = ToProjectRelative(filePath),
                bytes = Encoding.UTF8.GetByteCount(content),
                append = parsed.Append
            };
        });
    }

    private Task<string> SearchFilesAsync(RuntimeLlmToolCall toolCall, CancellationToken cancellationToken)
    {
        return ExecuteToolAsync(toolCall, cancellationToken, args =>
        {
            SearchFilesArgs parsed = ParseArguments<SearchFilesArgs>(args);
            if (string.IsNullOrWhiteSpace(parsed.Query))
            {
                throw new InvalidOperationException("Search query is required.");
            }

            string startPath = string.IsNullOrWhiteSpace(parsed.Path)
                ? _skillsDirectory
                : ResolveProjectPath(parsed.Path);
            int maxResults = Math.Clamp(parsed.MaxResults ?? 50, 1, 200);
            SearchOption option = parsed.Recursive != false ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            List<object> matches = [];
            if (string.IsNullOrWhiteSpace(parsed.Path) && !Directory.Exists(startPath))
            {
                return new
                {
                    ok = true,
                    query = parsed.Query,
                    path = ToProjectRelative(startPath),
                    matches
                };
            }

            IEnumerable<string> files = File.Exists(startPath)
                ? [startPath]
                : Directory.Exists(startPath)
                    ? Directory.EnumerateFiles(startPath, "*", option)
                    : throw new DirectoryNotFoundException($"Search path not found: {ToProjectRelative(startPath)}");

            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (matches.Count >= maxResults)
                {
                    break;
                }

                if (new FileInfo(file).Length > MaxSearchFileBytes)
                {
                    continue;
                }

                if (Path.GetFileName(file).Contains(parsed.Query, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(new
                    {
                        path = ToProjectRelative(file),
                        line = 0,
                        preview = Path.GetFileName(file)
                    });
                    if (matches.Count >= maxResults)
                    {
                        break;
                    }
                }

                int lineNumber = 0;
                foreach (string line in File.ReadLines(file, Encoding.UTF8))
                {
                    lineNumber++;
                    if (!line.Contains(parsed.Query, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    matches.Add(new
                    {
                        path = ToProjectRelative(file),
                        line = lineNumber,
                        preview = TrimPreview(line)
                    });
                    if (matches.Count >= maxResults)
                    {
                        break;
                    }
                }
            }

            return new
            {
                ok = true,
                query = parsed.Query,
                path = ToProjectRelative(startPath),
                matches
            };
        });
    }

    private async Task<string> RunCommandAsync(RuntimeLlmToolCall toolCall, CancellationToken cancellationToken)
    {
        try
        {
            RunCommandArgs parsed = ParseArguments<RunCommandArgs>(toolCall.ArgumentsJson);
            if (string.IsNullOrWhiteSpace(parsed.Command))
            {
                throw new InvalidOperationException("Command is required.");
            }

            string workingDirectory = string.IsNullOrWhiteSpace(parsed.WorkingDirectory)
                ? _projectDirectory
                : ResolveProjectPath(parsed.WorkingDirectory);
            if (!Directory.Exists(workingDirectory))
            {
                throw new DirectoryNotFoundException($"Working directory not found: {ToProjectRelative(workingDirectory)}");
            }

            int timeoutSeconds = Math.Clamp(parsed.TimeoutSeconds ?? 30, 1, 300);
            ProcessStartInfo startInfo = new()
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            if (OperatingSystem.IsWindows())
            {
                startInfo.FileName = "cmd.exe";
                startInfo.ArgumentList.Add("/c");
            }
            else
            {
                startInfo.FileName = "/bin/sh";
                startInfo.ArgumentList.Add("-lc");
            }

            startInfo.ArgumentList.Add(parsed.Command);
            using Process process = new() { StartInfo = startInfo };
            DateTimeOffset startedAt = DateTimeOffset.UtcNow;
            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start process.");
            }

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            bool timedOut = false;
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                timedOut = true;
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            string stdout = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);
            return Serialize(new
            {
                ok = !timedOut && process.ExitCode == 0,
                command = parsed.Command,
                workingDirectory = ToProjectRelative(workingDirectory),
                exitCode = timedOut ? (int?)null : process.ExitCode,
                timedOut,
                durationSeconds = Math.Round((DateTimeOffset.UtcNow - startedAt).TotalSeconds, 3),
                stdout = Truncate(stdout, MaxCommandOutputChars),
                stderr = Truncate(stderr, MaxCommandOutputChars)
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Serialize(new
            {
                ok = false,
                error = ex.Message
            });
        }
    }

    private static Task<string> ExecuteToolAsync(
        RuntimeLlmToolCall toolCall,
        CancellationToken cancellationToken,
        Func<string, object> execute)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return Task.FromResult(Serialize(execute(toolCall.ArgumentsJson)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromResult(Serialize(new
            {
                ok = false,
                error = ex.Message
            }));
        }
    }

    private object ReadTextFileResult(string filePath, int? maxBytes)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {ToProjectRelative(filePath)}");
        }

        int byteLimit = Math.Clamp(maxBytes ?? DefaultMaxReadBytes, 1, MaxReadBytes);
        FileInfo fileInfo = new(filePath);
        int count = (int)Math.Min(fileInfo.Length, byteLimit);
        byte[] bytes = new byte[count];
        int offset = 0;
        using (FileStream stream = File.OpenRead(filePath))
        {
            while (offset < count)
            {
                int read = stream.Read(bytes, offset, count - offset);
                if (read <= 0)
                {
                    break;
                }

                offset += read;
            }
        }

        string content = Encoding.UTF8.GetString(bytes, 0, offset);
        return new
        {
            ok = true,
            path = ToProjectRelative(filePath),
            bytes = fileInfo.Length,
            truncated = fileInfo.Length > count,
            content
        };
    }

    private object ReadMemoryFileResult(string filePath, int? maxBytes)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Memory file not found: {ToMemoryDisplayPath(filePath)}");
        }

        int byteLimit = Math.Clamp(maxBytes ?? DefaultMaxReadBytes, 1, MaxMemoryFileBytes);
        FileInfo fileInfo = new(filePath);
        int count = (int)Math.Min(fileInfo.Length, byteLimit);
        byte[] bytes = new byte[count];
        int offset = 0;
        using (FileStream stream = File.OpenRead(filePath))
        {
            while (offset < count)
            {
                int read = stream.Read(bytes, offset, count - offset);
                if (read <= 0)
                {
                    break;
                }

                offset += read;
            }
        }

        string content = Encoding.UTF8.GetString(bytes, 0, offset);
        return new
        {
            ok = true,
            path = ToMemoryDisplayPath(filePath),
            bytes = fileInfo.Length,
            truncated = fileInfo.Length > count,
            content
        };
    }

    private void EnsureMemoryDefaults()
    {
        Directory.CreateDirectory(_memoryDirectory);

        string indexPath = Path.Combine(_memoryDirectory, "index.md");
        if (File.Exists(indexPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.Combine(_memoryDirectory, "user"));
        Directory.CreateDirectory(Path.Combine(_memoryDirectory, "character"));
        Directory.CreateDirectory(Path.Combine(_memoryDirectory, "tasks"));
        Directory.CreateDirectory(Path.Combine(_memoryDirectory, "conversations"));
        File.WriteAllText(
            indexPath,
            string.Join(
                Environment.NewLine,
                "# Memory Index",
                "",
                "Use this local long-term memory only when it is relevant to the current conversation.",
                "Store distilled stable facts, preferences, relationship state, and open tasks. Do not store raw chat transcripts.",
                "",
                "## User",
                "- user/profile.md: User identity, preferred name, stable background.",
                "- user/preferences.md: User preferences, dislikes, habits, communication style.",
                "- user/relationships.md: Important relationship facts between the user and characters.",
                "",
                "## Character",
                "- character/<character-name>.md: Long-term memory for each character. Use Scene.Llm.GetCharacterMemoryPath(Entity) or scene.llm.get_character_memory_path(entity) in scripts to pass the current character path to the model.",
                "",
                "## Tasks",
                "- tasks/open_tasks.md: User requests or commitments that should persist.",
                "",
                "## Conversations",
                "- conversations/: Optional concise summaries of important conversation arcs.",
                ""),
            Encoding.UTF8);
    }

    private string ResolveProjectPath(string? path)
    {
        string raw = (path ?? string.Empty).Trim();
        if (raw.StartsWith("project:", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw["project:".Length..];
        }

        string fullPath = Path.GetFullPath(Path.IsPathRooted(raw)
            ? raw
            : Path.Combine(_projectDirectory, raw));
        EnsureUnderRoot(fullPath, _projectDirectory);
        return fullPath;
    }

    private string ResolveMemoryPath(string? path)
    {
        string raw = (path ?? string.Empty).Trim().Trim('"');
        if (raw.StartsWith("memory:", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw["memory:".Length..];
        }

        if (raw.StartsWith("memory/", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("memory\\", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw["memory/".Length..];
        }

        raw = raw.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException("Memory path cannot be empty.");
        }

        string fullPath = Path.GetFullPath(Path.IsPathRooted(raw)
            ? raw
            : Path.Combine(_memoryDirectory, raw));
        EnsureUnderRoot(fullPath, _memoryDirectory);
        return fullPath;
    }

    private string ResolveSkillDirectory(string skillName)
    {
        string name = skillName.Trim();
        if (name.Contains('/') || name.Contains('\\') || name.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Skill name must be a direct child directory name under skills/.");
        }

        string directory = Path.GetFullPath(Path.Combine(_skillsDirectory, name));
        EnsureUnderRoot(directory, _skillsDirectory);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Skill not found: {name}");
        }

        return directory;
    }

    private string ResolveUnderRoot(string root, string? path)
    {
        string raw = (path ?? string.Empty).Trim();
        string fullPath = Path.GetFullPath(Path.IsPathRooted(raw)
            ? raw
            : Path.Combine(root, raw));
        EnsureUnderRoot(fullPath, root);
        return fullPath;
    }

    private static void EnsureUnderRoot(string fullPath, string root)
    {
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normalizedPath = Path.GetFullPath(fullPath);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(normalizedPath, normalizedRoot, comparison))
        {
            return;
        }

        string rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;
        if (!normalizedPath.StartsWith(rootWithSeparator, comparison))
        {
            throw new InvalidOperationException("Path is outside the allowed project directory.");
        }
    }

    private string ToProjectRelative(string path)
    {
        string relative = Path.GetRelativePath(_projectDirectory, Path.GetFullPath(path));
        return relative == "."
            ? string.Empty
            : relative.Replace('\\', '/');
    }

    private string ToMemoryDisplayPath(string path)
    {
        string relative = Path.GetRelativePath(_memoryDirectory, Path.GetFullPath(path));
        string normalized = relative == "."
            ? string.Empty
            : relative.Replace('\\', '/');
        return string.IsNullOrEmpty(normalized) ? "memory/" : $"memory/{normalized}";
    }

    private static bool IsMemoryTextFile(string filePath)
    {
        string extension = Path.GetExtension(filePath);
        return string.IsNullOrEmpty(extension)
            || extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jsonl", StringComparison.OrdinalIgnoreCase);
    }

    private static int CountOccurrences(string text, string value)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value))
        {
            return 0;
        }

        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string CreateSafeMemoryFileName(string? value, string fallback)
    {
        string raw = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        StringBuilder builder = new(raw.Length);
        bool previousDash = false;
        foreach (char ch in raw)
        {
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-')
            {
                builder.Append(ch);
                previousDash = false;
                continue;
            }

            if (char.IsWhiteSpace(ch) || ch is '.' or '/' or '\\' or ':' or ';')
            {
                if (!previousDash && builder.Length > 0)
                {
                    builder.Append('-');
                    previousDash = true;
                }

                continue;
            }
        }

        string result = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(result) ? fallback : result;
    }

    private string? FindSkillMarkdownPath(string skillDirectory)
    {
        string preferred = Path.Combine(skillDirectory, "SKILL.md");
        if (File.Exists(preferred))
        {
            return preferred;
        }

        preferred = Path.Combine(skillDirectory, "skill.md");
        if (File.Exists(preferred))
        {
            return preferred;
        }

        return Directory.EnumerateFiles(skillDirectory, "*.md", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private SkillMetadata ReadSkillMetadata(string skillDirectory, string markdownPath, bool includeContent)
    {
        object result = ReadTextFileResult(markdownPath, 64 * 1024);
        string text = result.GetType().GetProperty("content")?.GetValue(result) as string ?? string.Empty;
        string directoryName = Path.GetFileName(skillDirectory);
        string name = ReadFrontMatterValue(text, "name") ?? directoryName;
        string description = ReadFrontMatterValue(text, "description") ?? ReadFirstParagraph(text);
        string preview = includeContent ? Truncate(text.Trim(), 2000) : string.Empty;
        return new SkillMetadata(name, description, preview);
    }

    private static string? ReadFrontMatterValue(string text, string key)
    {
        if (!text.StartsWith("---", StringComparison.Ordinal))
        {
            return null;
        }

        int end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0)
        {
            return null;
        }

        foreach (string rawLine in text[3..end].Split('\n'))
        {
            string line = rawLine.Trim();
            int separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            if (!string.Equals(line[..separator].Trim(), key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return line[(separator + 1)..].Trim().Trim('"', '\'');
        }

        return null;
    }

    private static string ReadFirstParagraph(string text)
    {
        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("---", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.Contains(':') && text.StartsWith("---", StringComparison.Ordinal))
            {
                continue;
            }

            return Truncate(line, 240);
        }

        return string.Empty;
    }

    private static T ParseArguments<T>(string argumentsJson)
        where T : new()
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return new T();
        }

        return JsonSerializer.Deserialize<T>(argumentsJson, JsonOptions) ?? new T();
    }

    private static string FirstNonWhiteSpace(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static string Serialize(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static string TrimPreview(string text)
    {
        return Truncate(text.Trim(), 300);
    }

    private static string Truncate(string value, int maxChars)
    {
        if (value.Length <= maxChars)
        {
            return value;
        }

        return value[..maxChars] + $"\n... truncated {value.Length - maxChars} chars";
    }

    private sealed class ListSkillsArgs
    {
        public bool IncludeContent { get; set; }

        public int? MaxResults { get; set; }
    }

    private sealed class SearchMemoryArgs
    {
        public string Query { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;

        public bool? Recursive { get; set; }

        public int? MaxResults { get; set; }
    }

    private sealed class ReadMemoryArgs
    {
        public string Path { get; set; } = string.Empty;

        public int? MaxBytes { get; set; }
    }

    private sealed class WriteMemoryArgs
    {
        public string Path { get; set; } = string.Empty;

        public string? Content { get; set; }

        public bool? Append { get; set; }

        public bool? CreateDirectories { get; set; }
    }

    private sealed class UpdateMemoryArgs
    {
        public string Path { get; set; } = string.Empty;

        public string OldText { get; set; } = string.Empty;

        public string? NewText { get; set; }
    }

    private sealed class ForgetMemoryArgs
    {
        public string Path { get; set; } = string.Empty;

        public string? Text { get; set; }

        public bool DeleteFile { get; set; }
    }

    private sealed class ReadSkillArgs
    {
        public string Name { get; set; } = string.Empty;

        public string Skill { get; set; } = string.Empty;

        public string SkillName { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;

        public int? MaxBytes { get; set; }
    }

    private sealed class ListFilesArgs
    {
        public string Path { get; set; } = string.Empty;

        public string SkillName { get; set; } = string.Empty;

        public string Skill { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public bool Recursive { get; set; }

        public int? MaxResults { get; set; }
    }

    private sealed class ReadFileArgs
    {
        public string Path { get; set; } = string.Empty;

        public int? MaxBytes { get; set; }
    }

    private sealed class WriteFileArgs
    {
        public string Path { get; set; } = string.Empty;

        public string? Content { get; set; }

        public bool Append { get; set; }

        public bool? CreateDirectories { get; set; }
    }

    private sealed class SearchFilesArgs
    {
        public string Query { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;

        public bool? Recursive { get; set; }

        public int? MaxResults { get; set; }
    }

    private sealed class RunCommandArgs
    {
        public string Command { get; set; } = string.Empty;

        public string WorkingDirectory { get; set; } = string.Empty;

        public int? TimeoutSeconds { get; set; }
    }

    private sealed record SkillMetadata(string Name, string Description, string Preview);
}
