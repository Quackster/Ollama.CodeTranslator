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
    public class OllamaResponse
    {
        public string Model { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Response { get; set; }
        public bool Done { get; set; }
        public string DoneReason { get; set; }
    }

    class CodeTranslator
    {
        private static readonly HttpClient client = new HttpClient();

        static async Task<int> Main(string[] args)
        {
            ExtensionConfig.LoadOrCreateDefault();

            var options = ParseArgs(args);

            string logPath = options.ContainsKey("log")
                ? options["log"]
                : Path.Combine(
                    options.ContainsKey("directory") && !string.IsNullOrWhiteSpace(options["directory"])
                        ? options["directory"] : ".",
                    "translation_dispatch.log");

            Log(logPath, "Full command line: " + string.Join(" ", args), verbose: false);

            string optionsText = "Command line options:\n" +
                string.Join("\n", options.Select(kvp => $"  --{kvp.Key}={kvp.Value}"));

            Log(logPath, optionsText, verbose: false);

            bool verbose = options.ContainsKey("verbose");
            if (verbose)
            {
                Info(optionsText);
            }

            if (!options.ContainsKey("directory") || string.IsNullOrWhiteSpace(options["directory"]))
            {
                Console.WriteLine("Usage: CodeTranslator --directory <input-dir> [--source <lang>] [--target <lang>] [--model <name>] [--api-url <url>] [--output <dir>] [--log <file>] [--prompt <file>] [--overwrite] [--dry-run] [--verbose]");
                Console.WriteLine("Language file extensions are now loaded from 'extensions.json' (auto-generated in the working directory if missing). Edit this file to add your own extensions!");
                return 1;
            }

            string directory = options["directory"];
            string sourceLang = options.ContainsKey("source") ? options["source"] : "Java";
            string targetLang = options.ContainsKey("target") ? options["target"] : "CSharp";
            string model = options.ContainsKey("model") ? options["model"] : "qwen2.5-coder:3b";
            string apiUrl = options.ContainsKey("api-url") ? options["api-url"] : "http://localhost:11434/api/generate";
            string outputDir = options.ContainsKey("output") ? options["output"] : Path.Combine(directory, $"converted_{targetLang}");
            string promptOption = options.ContainsKey("prompt") ? options["prompt"] : null;
            bool overwrite = options.ContainsKey("overwrite");
            bool dryRun = options.ContainsKey("dry-run");
            var timeout = TimeSpan.FromMinutes(30);

            if (int.TryParse(options["--timeout"], out var timeoutVal) && timeoutVal > 0)
            {
                timeout = TimeSpan.FromSeconds(timeoutVal);
            }

            client.Timeout = timeout;

            if (!Directory.Exists(directory))
            {
                Error($"Directory '{directory}' does not exist.");
                return 2;
            }

            Directory.CreateDirectory(outputDir);

            // PROMPT FILE PICKUP, CENTRALIZED
            string? promptFile = null;
            string? promptContent = null;
            try
            {
                promptFile = ResolvePromptFile(directory, promptOption, sourceLang, targetLang);
                if (promptFile != null)
                {
                    promptContent = await File.ReadAllTextAsync(promptFile, Encoding.UTF8);
                    if (verbose) Info($"Using prompt file: {promptFile}");
                }
                else
                {
                    if (verbose) Info("No prompt file found or specified.");
                }
            }
            catch (FileNotFoundException ex)
            {
                Error(ex.Message);
                return 2;
            }

            // Load extensions from config
            var sourceFileExtensions = ExtensionConfig.GetExtensions(sourceLang);
            var targetFileExtensions = ExtensionConfig.GetExtensions(targetLang);
            var files = sourceFileExtensions.SelectMany(ext => Directory.GetFiles(directory, $"*{ext}", SearchOption.AllDirectories)).Distinct().ToList();

            string ext = targetFileExtensions.First();

            if (verbose)
            {
                Info("Source extension(s): " + string.Join(", ", sourceFileExtensions));
                Info("Target extension: " + ext);
            }

            Info($"Found {files.Count} '{sourceLang}' files in '{directory}'. Output: '{outputDir}'");

            foreach (var file in files)
            {
                string relativePath = Path.GetRelativePath(directory, file);
                string code = await File.ReadAllTextAsync(file);
                string outFile = Path.Combine(outputDir, Path.ChangeExtension(relativePath, ext));

                Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);

                if (File.Exists(outFile) && !overwrite)
                {
                    Log(logPath, $"{DateTime.UtcNow:o} SKIP {relativePath} (already exists)", verbose);
                    Info($"[SKIP] {relativePath} (already exists).");
                    continue;
                }

                Log(logPath, $"{DateTime.UtcNow:o} SENT {relativePath} -> {Path.GetRelativePath(directory, outFile)}", verbose);
                Info($"[SENT] {relativePath} → {Path.GetRelativePath(directory, outFile)}");

                if (dryRun)
                {
                    Warn($"[DRY RUN] Would translate and write {relativePath} to {Path.GetRelativePath(directory, outFile)}");
                    continue;
                }

                string prompt = promptContent.Replace("{sourceLang}", sourceLang)
                                   .Replace("{targetLang}", targetLang)
                                   .Replace("{code}", code);

                OllamaResponse apiResult = null;
                try
                {
                    apiResult = await TranslateAsync(model, prompt, apiUrl);
                }
                catch (Exception ex)
                {
                    Error($"API ERROR for {relativePath}: {ex.Message}");
                    continue;
                }

                Log(logPath, $"{DateTime.UtcNow:o} TRANSLATED {relativePath} -> {Path.GetRelativePath(directory, outFile)}", verbose);

                if (apiResult.Response.Contains("incomplete", StringComparison.OrdinalIgnoreCase)
                    || apiResult.Response.Contains("provide more details", StringComparison.OrdinalIgnoreCase))
                {
                    Warn($"Translation of '{relativePath}' needs more context:\n   API: {apiResult.Response.Trim()}");
                    continue;
                }

                await File.WriteAllTextAsync(outFile, HandleResponse(apiResult.Response), Encoding.UTF8);
                Info($"[OK] {relativePath} → {Path.GetRelativePath(directory, outFile)}");
            }

            Info("All files processed.");
            return 0;
        }

        private static string? HandleResponse(string response)
        {
            try
            {
                return MarkdownCodeExtractor.ExtractShortCodeSnippet(response);
            }
            catch { return response; }
        }

        static string? ResolvePromptFile(string directory, string? promptOption, string sourceLang, string targetLang)
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

            string[] promptCandidates =
            {
        Path.Combine(directory, $"{sourceLang}-to-{targetLang}.prompt"),
        Path.Combine(directory, $"{targetLang}.prompt"),
        Path.Combine(directory, $"{sourceLang}.prompt")
    };

            foreach (var candidate in promptCandidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            // If no prompt file is found, create a default one with the source-target.prompt convention
            string defaultPromptFileName = $"{sourceLang}-to-{targetLang}.prompt";
            string defaultPromptFilePath = Path.Combine(directory, defaultPromptFileName);
            string defaultPromptContent = @"You are a code translation assistant.
Convert exactly this one file from {sourceLang} to {targetLang}, preserving its structure, comments and functionality.
Show me the source code only, full source code, and nothing but the source code.

--- FILE TO TRANSLATE ---
{{code}}
--- END FILE ---";

            File.WriteAllText(defaultPromptFilePath, defaultPromptContent, Encoding.UTF8);
            Info($"Default prompt file created: {defaultPromptFilePath}");

            return defaultPromptFilePath;
        }

        static async Task<OllamaResponse> TranslateAsync(string model, string prompt, string apiUrl)
        {
            var payload = new { model, prompt, stream = false };
            string json = JsonSerializer.Serialize(payload);
            var resp = await client.PostAsync(apiUrl, new StringContent(json, Encoding.UTF8, "application/json"));
            resp.EnsureSuccessStatusCode();
            string body = await resp.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<OllamaResponse>(body, options)
                   ?? throw new InvalidOperationException("Failed to parse API response");
        }

        static void Log(string logPath, string line, bool verbose)
        {
            File.AppendAllText(logPath, line + Environment.NewLine, Encoding.UTF8);
            if (verbose)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"[LOG] {line}");
                Console.ResetColor();
            }
        }
        static void Info(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(msg);
            Console.ResetColor();
        }
        static void Warn(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(msg);
            Console.ResetColor();
        }
        static void Error(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(msg);
            Console.ResetColor();
        }

        static Dictionary<string, string> ParseArgs(string[] args)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("--"))
                {
                    string key = args[i].Substring(2);
                    if (key == "overwrite" || key == "dry-run" || key == "verbose")
                        dict[key] = "true";
                    else if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                        dict[key] = args[++i];
                    else
                        dict[key] = "";
                }
            }
            return dict;
        }
    }

    // ExtensionConfig: Reads/creates extensions.json for language-extension mapping
    public static class ExtensionConfig
    {
        private const string ConfigFileName = "extensions.json";
        private static Dictionary<string, List<string>> _map = null;

        public static IReadOnlyDictionary<string, List<string>> Map => _map;

        public static void LoadOrCreateDefault()
        {
            if (!File.Exists(ConfigFileName))
            {
                var defaultMap = GetDefaultMap();
                File.WriteAllText(ConfigFileName, JsonSerializer.Serialize(defaultMap, new JsonSerializerOptions { WriteIndented = true }));
            }
            var json = File.ReadAllText(ConfigFileName);
            _map = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new Dictionary<string, List<string>>();
        }

        public static List<string> GetExtensions(string lang)
        {
            lang = lang?.Trim()?.ToLower() ?? "";
            return _map.TryGetValue(lang, out var exts) ? exts : new List<string> { ".txt" };
        }

        // These are your hardcoded defaults
        private static Dictionary<string, List<string>> GetDefaultMap() => new()
        {
            ["csharp"] = new() { ".cs" },
            ["c#"] = new() { ".cs" },
            ["vbnet"] = new() { ".vb" },
            ["vb"] = new() { ".vb" },
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

    public static class MarkdownCodeExtractor
    {
        public static string ExtractShortCodeSnippet(string markdown)
        {
            var regex = new Regex(@"```[\w]*\n(.*?)\n```", RegexOptions.Singleline);
            var match = regex.Match(markdown);

            if (match.Success)
            {
                string code = match.Groups[1].Value;
                return code;
            }
            return markdown;
        }
    }
}
