using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CodeTranslator.Ollama
{
    // Models
    public record OllamaResponse(
        string Model,
        DateTime CreatedAt,
        string Response,
        bool Done,
        string DoneReason
    );

    public record TranslationOptions(
        string Directory,
        string SourceLang = "Java",
        string TargetLang = "CSharp",
        string Model = "qwen2.5-coder:3b",
        string ApiUrl = "http://localhost:11434/api/generate",
        string? OutputDir = null,
        string? PromptFile = null,
        string? LogPath = null,
        bool Overwrite = false,
        bool DryRun = false,
        bool Verbose = false,
        int Context = 4096,
        TimeSpan Timeout = default
    )
    {
        public string GetOutputDir() => OutputDir ?? Path.Combine(Directory, $"converted_{TargetLang}");
        public string GetLogPath() => LogPath ?? Path.Combine(Directory, "translation_dispatch.log");
        public TimeSpan GetTimeout() => Timeout == default ? TimeSpan.FromMinutes(30) : Timeout;
    }

    // Services
    public interface ILogger
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
        void Log(string message, bool verbose = false);
    }

    public class ConsoleLogger : ILogger
    {
        private readonly string _logPath;
        private readonly bool _verbose;

        public ConsoleLogger(string logPath, bool verbose = false)
        {
            _logPath = logPath;
            _verbose = verbose;
        }

        public void Info(string message)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        public void Warn(string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        public void Error(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        public void Log(string message, bool verbose = false)
        {
            var logEntry = $"{DateTime.UtcNow:o} {message}";
            File.AppendAllText(_logPath, logEntry + Environment.NewLine, Encoding.UTF8);

            if (verbose || _verbose)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"[LOG] {logEntry}");
                Console.ResetColor();
            }
        }
    }

    public interface IOllamaClient
    {
        Task<OllamaResponse> TranslateAsync(string model, string prompt, int context = 4096);
    }

    public class OllamaClient : IOllamaClient, IDisposable
    {
        private readonly HttpClient _client;
        private readonly string _apiUrl;

        public OllamaClient(string apiUrl, TimeSpan timeout)
        {
            _apiUrl = apiUrl;
            _client = new HttpClient { Timeout = timeout };
        }

        public async Task<OllamaResponse> TranslateAsync(string model, string prompt, int context = 4096)
        {
            var payload = new { model, prompt, stream = false, options = new { num_ctx = context } };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _client.PostAsync(_apiUrl, content);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            return JsonSerializer.Deserialize<OllamaResponse>(body, options)
                   ?? throw new InvalidOperationException("Failed to parse API response");
        }

        public void Dispose() => _client?.Dispose();
    }

    public interface IPromptResolver
    {
        Task<string?> ResolvePromptAsync(string directory, string? promptOption, string sourceLang, string targetLang);
    }

    public class PromptResolver : IPromptResolver
    {
        private readonly ILogger _logger;

        public PromptResolver(ILogger logger)
        {
            _logger = logger;
        }

        public async Task<string?> ResolvePromptAsync(string directory, string? promptOption, string sourceLang, string targetLang)
        {
            var promptFile = FindPromptFile(directory, promptOption, sourceLang, targetLang);

            if (promptFile == null)
                return null;

            return await File.ReadAllTextAsync(promptFile, Encoding.UTF8);
        }

        private string? FindPromptFile(string directory, string? promptOption, string sourceLang, string targetLang)
        {
            if (!string.IsNullOrEmpty(promptOption))
            {
                var promptFile = Path.IsPathRooted(promptOption)
                    ? promptOption
                    : Path.Combine(directory, promptOption);

                if (!File.Exists(promptFile))
                    throw new FileNotFoundException($"Specified prompt file '{promptFile}' does not exist.");

                return promptFile;
            }

            var candidates = new[]
            {
                Path.Combine(directory, $"{sourceLang}-to-{targetLang}.prompt"),
                Path.Combine(directory, $"{targetLang}.prompt"),
                Path.Combine(directory, $"{sourceLang}.prompt")
            };

            var existingFile = candidates.FirstOrDefault(File.Exists);
            if (existingFile != null)
                return existingFile;

            return CreateDefaultPromptFile(directory, sourceLang, targetLang);
        }

        private string CreateDefaultPromptFile(string directory, string sourceLang, string targetLang)
        {
            var fileName = $"{sourceLang}-to-{targetLang}.prompt";
            var filePath = Path.Combine(directory, fileName);

            var content = $@"You are a code translation assistant.
Convert exactly this one file from {sourceLang} to {targetLang}, preserving its structure, comments and functionality.
Show me the source code only, full source code, and nothing but the source code.

--- FILE TO TRANSLATE ---
{{code}}
--- END FILE ---";

            File.WriteAllText(filePath, content, Encoding.UTF8);
            _logger.Info($"Default prompt file created: {filePath}");

            return filePath;
        }
    }

    public interface ICodeExtractor
    {
        string ExtractCode(string response);
    }

    public class MarkdownCodeExtractor : ICodeExtractor
    {
        private static readonly Regex CodeBlockRegex = new(@"```[\w]*\n(.*?)\n```", RegexOptions.Singleline);

        public string ExtractCode(string response)
        {
            var match = CodeBlockRegex.Match(response);
            return match.Success ? match.Groups[1].Value : response;
        }
    }

    // Main Application
    public class CodeTranslatorApp
    {
        private readonly ILogger _logger;
        private readonly IOllamaClient _ollamaClient;
        private readonly IPromptResolver _promptResolver;
        private readonly ICodeExtractor _codeExtractor;

        public CodeTranslatorApp(
            ILogger logger,
            IOllamaClient ollamaClient,
            IPromptResolver promptResolver,
            ICodeExtractor codeExtractor)
        {
            _logger = logger;
            _ollamaClient = ollamaClient;
            _promptResolver = promptResolver;
            _codeExtractor = codeExtractor;
        }

        public async Task<int> RunAsync(TranslationOptions options)
        {
            try
            {
                if (!ValidateOptions(options))
                    return 1;

                await LogOptionsAsync(options);

                var promptContent = await _promptResolver.ResolvePromptAsync(
                    options.Directory, options.PromptFile, options.SourceLang, options.TargetLang);

                if (promptContent == null)
                {
                    _logger.Error("No prompt content available.");
                    return 2;
                }

                var files = GetSourceFiles(options);
                if (files.Count == 0)
                {
                    _logger.Info($"No {options.SourceLang} files found in '{options.Directory}'.");
                    return 0;
                }

                var outputDir = options.GetOutputDir();
                Directory.CreateDirectory(outputDir);

                _logger.Info($"Found {files.Count} '{options.SourceLang}' files. Output: '{outputDir}'");

                await ProcessFilesAsync(files, options, promptContent, outputDir);

                _logger.Info("All files processed.");
                return 0;
            }
            catch (Exception ex)
            {
                _logger.Error($"Unexpected error: {ex.Message}");
                return 3;
            }
        }

        private bool ValidateOptions(TranslationOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.Directory))
            {
                ShowUsage();
                return false;
            }

            if (!Directory.Exists(options.Directory))
            {
                _logger.Error($"Directory '{options.Directory}' does not exist.");
                return false;
            }

            return true;
        }

        private async Task LogOptionsAsync(TranslationOptions options)
        {
            var optionsText = $@"Translation Options:
  Directory: {options.Directory}
  Source Language: {options.SourceLang}
  Target Language: {options.TargetLang}
  Model: {options.Model}
  API URL: {options.ApiUrl}
  Output Directory: {options.GetOutputDir()}
  Context: {options.Context}
  Timeout: {options.GetTimeout()}
  Overwrite: {options.Overwrite}
  Dry Run: {options.DryRun}";

            _logger.Log(optionsText);

            if (options.Verbose)
                _logger.Info(optionsText);
        }

        private List<string> GetSourceFiles(TranslationOptions options)
        {
            var sourceExtensions = ExtensionConfig.GetExtensions(options.SourceLang);

            if (options.Verbose)
            {
                var targetExtensions = ExtensionConfig.GetExtensions(options.TargetLang);
                _logger.Info($"Source extensions: {string.Join(", ", sourceExtensions)}");
                _logger.Info($"Target extensions: {string.Join(", ", targetExtensions)}");
            }

            return sourceExtensions
                .SelectMany(ext => Directory.GetFiles(options.Directory, $"*{ext}", SearchOption.AllDirectories))
                .Distinct()
                .ToList();
        }

        private async Task ProcessFilesAsync(List<string> files, TranslationOptions options, string promptContent, string outputDir)
        {
            var targetExtensions = ExtensionConfig.GetExtensions(options.TargetLang);
            var targetExtension = targetExtensions.First();

            foreach (var file in files)
            {
                await ProcessSingleFileAsync(file, options, promptContent, outputDir, targetExtension);
            }
        }

        private async Task ProcessSingleFileAsync(string file, TranslationOptions options, string promptContent, string outputDir, string targetExtension)
        {
            var relativePath = Path.GetRelativePath(options.Directory, file);
            var outputFile = Path.Combine(outputDir, Path.ChangeExtension(relativePath, targetExtension));
            var outputRelativePath = Path.GetRelativePath(options.Directory, outputFile);

            Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);

            if (File.Exists(outputFile) && !options.Overwrite)
            {
                _logger.Log($"SKIP {relativePath} (already exists)", options.Verbose);
                _logger.Info($"[SKIP] {relativePath} (already exists)");
                return;
            }

            _logger.Log($"PROCESSING {relativePath} -> {outputRelativePath}", options.Verbose);
            _logger.Info($"[PROCESSING] {relativePath} → {outputRelativePath}");

            if (options.DryRun)
            {
                _logger.Warn($"[DRY RUN] Would translate {relativePath} to {outputRelativePath}");
                return;
            }

            try
            {
                var code = await File.ReadAllTextAsync(file);
                var prompt = promptContent
                    .Replace("{sourceLang}", options.SourceLang)
                    .Replace("{targetLang}", options.TargetLang)
                    .Replace("{code}", code);

                var response = await _ollamaClient.TranslateAsync(options.Model, prompt, options.Context);

                if (IsIncompleteResponse(response.Response))
                {
                    _logger.Warn($"Translation of '{relativePath}' needs more context: {response.Response.Trim()}");
                    return;
                }

                var extractedCode = _codeExtractor.ExtractCode(response.Response);
                await File.WriteAllTextAsync(outputFile, extractedCode, Encoding.UTF8);

                _logger.Log($"SUCCESS {relativePath} -> {outputRelativePath}", options.Verbose);
                _logger.Info($"[SUCCESS] {relativePath} → {outputRelativePath}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error processing {relativePath}: {ex.Message}");
            }
        }

        private static bool IsIncompleteResponse(string response) =>
            response.Contains("incomplete", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("provide more details", StringComparison.OrdinalIgnoreCase);

        private static void ShowUsage()
        {
            Console.WriteLine("Usage: CodeTranslator --directory <input-dir> [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --directory <dir>    Source directory (required)");
            Console.WriteLine("  --source <lang>      Source language (default: Java)");
            Console.WriteLine("  --target <lang>      Target language (default: CSharp)");
            Console.WriteLine("  --model <name>       Ollama model (default: qwen2.5-coder:3b)");
            Console.WriteLine("  --api-url <url>      API endpoint (default: http://localhost:11434/api/chat)");
            Console.WriteLine("  --output <dir>       Output directory (default: <input-dir>/converted_<target>)");
            Console.WriteLine("  --log <file>         Log file path");
            Console.WriteLine("  --prompt <file>      Custom prompt file");
            Console.WriteLine("  --ctx <number>       Context size (default: 4096)");
            Console.WriteLine("  --timeout <seconds>  Request timeout (default: 1800)");
            Console.WriteLine("  --overwrite          Overwrite existing files");
            Console.WriteLine("  --dry-run           Show what would be done");
            Console.WriteLine("  --verbose           Verbose output");
            Console.WriteLine();
            Console.WriteLine("Language extensions are configured in 'extensions.json'");
        }
    }

    // Configuration
    public static class ExtensionConfig
    {
        private const string ConfigFileName = "extensions.json";
        private static Dictionary<string, List<string>>? _map;

        public static IReadOnlyDictionary<string, List<string>> Map => _map ?? throw new InvalidOperationException("Config not loaded");

        public static void LoadOrCreateDefault()
        {
            if (!File.Exists(ConfigFileName))
            {
                var defaultMap = CreateDefaultExtensionMap();
                var json = JsonSerializer.Serialize(defaultMap, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFileName, json);
            }

            var configJson = File.ReadAllText(ConfigFileName);
            _map = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(configJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }

        public static List<string> GetExtensions(string language)
        {
            var normalizedLang = language?.Trim()?.ToLower() ?? "";
            return _map?.TryGetValue(normalizedLang, out var extensions) == true
                ? extensions
                : new List<string> { ".txt" };
        }

        private static Dictionary<string, List<string>> CreateDefaultExtensionMap() => new()
        {
            ["csharp"] = new() { ".cs" },
            ["c#"] = new() { ".cs" },
            ["vbnet"] = new() { ".vb" },
            ["vb"] = new() { ".vb" },
            ["vb6"] = new() { ".vb", ".frm", ".bas" },
            ["fsharp"] = new() { ".fs" },
            ["fs"] = new() { ".fs" },
            ["f#"] = new() { ".fs" },
            ["java"] = new() { ".java" },
            ["kotlin"] = new() { ".kt" },
            ["scala"] = new() { ".scala" },
            ["groovy"] = new() { ".groovy" },
            ["c"] = new() { ".c" },
            ["cpp"] = new() { ".cpp", ".cc", ".cxx" },
            ["c++"] = new() { ".cpp", ".cc", ".cxx" },
            ["objc"] = new() { ".m", ".mm" },
            ["objective-c"] = new() { ".m", ".mm" },
            ["rust"] = new() { ".rs" },
            ["go"] = new() { ".go" },
            ["javascript"] = new() { ".js", ".mjs", ".cjs" },
            ["js"] = new() { ".js", ".mjs", ".cjs" },
            ["typescript"] = new() { ".ts", ".tsx" },
            ["ts"] = new() { ".ts", ".tsx" },
            ["python"] = new() { ".py", ".pyw" },
            ["py"] = new() { ".py", ".pyw" },
            ["ruby"] = new() { ".rb", ".erb" },
            ["rb"] = new() { ".rb", ".erb" },
            ["php"] = new() { ".php", ".phtml" },
            ["perl"] = new() { ".pl", ".pm" },
            ["pl"] = new() { ".pl", ".pm" },
            ["dart"] = new() { ".dart" },
            ["lua"] = new() { ".lua" },
            ["coffeescript"] = new() { ".coffee" },
            ["coffee"] = new() { ".coffee" },
            ["r"] = new() { ".r", ".R" },
            ["shell"] = new() { ".sh" },
            ["bash"] = new() { ".sh" },
            ["powershell"] = new() { ".ps1", ".psm1" },
            ["haskell"] = new() { ".hs", ".lhs" },
            ["hs"] = new() { ".hs", ".lhs" },
            ["clojure"] = new() { ".clj", ".cljs", ".cljc" },
            ["clj"] = new() { ".clj", ".cljs", ".cljc" },
            ["elixir"] = new() { ".ex", ".exs" },
            ["ex"] = new() { ".ex", ".exs" },
            ["erlang"] = new() { ".erl", ".hrl" },
            ["erl"] = new() { ".erl", ".hrl" },
            ["lisp"] = new() { ".lisp", ".lsp" },
            ["scheme"] = new() { ".scm", ".ss" },
            ["html"] = new() { ".html", ".htm" },
            ["xml"] = new() { ".xml", ".xsl", ".xsd" },
            ["css"] = new() { ".css" },
            ["json"] = new() { ".json" },
            ["yaml"] = new() { ".yaml", ".yml" },
            ["yml"] = new() { ".yaml", ".yml" },
            ["markdown"] = new() { ".md", ".markdown" },
            ["md"] = new() { ".md", ".markdown" },
            ["toml"] = new() { ".toml" },
            ["ini"] = new() { ".ini" },
            ["csv"] = new() { ".csv" },
            ["sql"] = new() { ".sql" },
            ["matlab"] = new() { ".m", ".mlx" },
            ["m"] = new() { ".m", ".mlx" },
            ["racket"] = new() { ".rkt" },
            ["elm"] = new() { ".elm" },
            ["graphql"] = new() { ".graphql", ".gql" },
            ["dockerfile"] = new() { "Dockerfile" },
            ["makefile"] = new() { "Makefile" },
            ["cmake"] = new() { "CMakeLists.txt" },
            ["lingo"] = new() { ".ls" },
            ["shockwave"] = new() { ".ls" }
        };
    }

    // Argument Parser
    public static class ArgumentParser
    {
        public static TranslationOptions Parse(string[] args)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--"))
                    continue;

                var key = args[i][2..];

                if (IsFlagArgument(key))
                {
                    dict[key] = "true";
                }
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                {
                    dict[key] = args[++i];
                }
                else
                {
                    dict[key] = "";
                }
            }

            var timeout = dict.TryGetValue("timeout", out var timeoutStr) &&
                         int.TryParse(timeoutStr, out var timeoutVal) && timeoutVal > 0
                ? TimeSpan.FromSeconds(timeoutVal)
                : TimeSpan.FromMinutes(30);

            var context = dict.TryGetValue("ctx", out var ctxStr) &&
                         int.TryParse(ctxStr, out var ctxVal) && ctxVal > 0
                ? ctxVal
                : 4096;

            return new TranslationOptions(
                Directory: dict.GetValueOrDefault("directory", ""),
                SourceLang: dict.GetValueOrDefault("source", "Java"),
                TargetLang: dict.GetValueOrDefault("target", "CSharp"),
                Model: dict.GetValueOrDefault("model", "qwen2.5-coder:3b"),
                ApiUrl: dict.GetValueOrDefault("api-url", "http://localhost:11434/api/chat"),
                OutputDir: dict.GetValueOrDefault("output"),
                PromptFile: dict.GetValueOrDefault("prompt"),
                LogPath: dict.GetValueOrDefault("log"),
                Overwrite: dict.ContainsKey("overwrite"),
                DryRun: dict.ContainsKey("dry-run"),
                Verbose: dict.ContainsKey("verbose"),
                Context: context,
                Timeout: timeout
            );
        }

        private static bool IsFlagArgument(string key) =>
            key is "overwrite" or "dry-run" or "verbose";
    }

    // Program Entry Point
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            ExtensionConfig.LoadOrCreateDefault();

            var options = ArgumentParser.Parse(args);
            var logger = new ConsoleLogger(options.GetLogPath(), options.Verbose);

            using var ollamaClient = new OllamaClient(options.ApiUrl, options.GetTimeout());
            var promptResolver = new PromptResolver(logger);
            var codeExtractor = new MarkdownCodeExtractor();

            var app = new CodeTranslatorApp(logger, ollamaClient, promptResolver, codeExtractor);

            return await app.RunAsync(options);
        }
    }
}