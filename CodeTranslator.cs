using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;

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
            var options = ParseArgs(args);

            // Determine logPath before first log
            string logPath = options.ContainsKey("log")
                ? options["log"]
                : Path.Combine(
                    options.ContainsKey("directory") && !string.IsNullOrWhiteSpace(options["directory"])
                        ? options["directory"] : ".",
                    "translation_dispatch.log");

            // Log full raw command line (optional)
            Log(logPath, "Full command line: " + string.Join(" ", args), verbose: false);

            // Build pretty options text for log/console
            string optionsText = "Command line options:\n" +
                string.Join("\n", options.Select(kvp => $"  --{kvp.Key}={kvp.Value}"));

            // Always log options to file
            Log(logPath, optionsText, verbose: false);

            // Print to console if verbose
            bool verbose = options.ContainsKey("verbose");
            if (verbose)
            {
                Info(optionsText);
            }

            if (!options.ContainsKey("directory") || string.IsNullOrWhiteSpace(options["directory"]))
            {
                Console.WriteLine("Usage: CodeTranslator --directory <input-dir> [--source <lang>] [--target <lang>] [--model <name>] [--api-url <url>] [--output <dir>] [--log <file>] [--prompt <file>] [--overwrite] [--dry-run] [--verbose]");
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
            // `verbose` already set above

            if (!Directory.Exists(directory))
            {
                Error($"Directory '{directory}' does not exist.");
                return 2;
            }

            Directory.CreateDirectory(outputDir);

            // --- PROMPT FILE PICKUP, CENTRALIZED ---
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

            // Collect files
            var extensions = GetSourceExtensions(sourceLang).ToList();
            var files = extensions.SelectMany(ext => Directory.GetFiles(directory, $"*{ext}", SearchOption.AllDirectories)).Distinct().ToList();

            Info($"Found {files.Count} '{sourceLang}' files in '{directory}'. Output: '{outputDir}'");

            client.Timeout = TimeSpan.FromMinutes(30);

            foreach (var file in files)
            {
                string relativePath = Path.GetRelativePath(directory, file);
                string code = await File.ReadAllTextAsync(file);
                string outFile = Path.Combine(outputDir, Path.ChangeExtension(relativePath, GetExtensionFor(targetLang)));

                Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);

                if (File.Exists(outFile) && !overwrite)
                {
                    Log(logPath, $"{DateTime.UtcNow:o} SKIP {relativePath} (already exists)", verbose);
                    Info($"[SKIP] {relativePath} (already exists).");
                    continue;
                }

                // LOG SENT
                Log(logPath, $"{DateTime.UtcNow:o} SENT {relativePath} -> {Path.GetRelativePath(directory, outFile)}", verbose);
                Info($"[SENT] {relativePath} → {Path.GetRelativePath(directory, outFile)}");

                if (dryRun)
                {
                    Warn($"[DRY RUN] Would translate and write {relativePath} to {Path.GetRelativePath(directory, outFile)}");
                    continue;
                }

                string prompt = promptContent != null
                    ? promptContent.Replace("{sourceLang}", sourceLang)
                                   .Replace("{targetLang}", targetLang)
                                   .Replace("{code}", code)
                    : BuildPrompt(code, sourceLang, targetLang);

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

                // LOG TRANSLATE
                Log(logPath, $"{DateTime.UtcNow:o} TRANSLATED {relativePath} -> {Path.GetRelativePath(directory, outFile)}", verbose);

                if (apiResult.Response.Contains("incomplete", StringComparison.OrdinalIgnoreCase)
                    || apiResult.Response.Contains("provide more details", StringComparison.OrdinalIgnoreCase))
                {
                    Warn($"Translation of '{relativePath}' needs more context:\n   API: {apiResult.Response.Trim()}");
                    continue;
                }

                await File.WriteAllTextAsync(outFile, apiResult.Response, Encoding.UTF8);
                Info($"[OK] {relativePath} → {Path.GetRelativePath(directory, outFile)}");
            }

            Info("All files processed.");
            return 0;
        }

        /// <summary>
        /// Resolve the prompt file, handling user input and fallback logic.
        /// Returns the path to use, or null if not found and not specified.
        /// Throws FileNotFoundException if user specified file is missing.
        /// </summary>
        static string? ResolvePromptFile(string directory, string? promptOption, string sourceLang, string targetLang)
        {
            if (!string.IsNullOrEmpty(promptOption))
            {
                // Use user-specified prompt file, make relative to directory if not rooted
                var promptFile = Path.IsPathRooted(promptOption)
                    ? promptOption
                    : Path.Combine(directory, promptOption);

                if (!File.Exists(promptFile))
                    throw new FileNotFoundException($"Specified prompt file '{promptFile}' does not exist.");
                return promptFile;
            }

            // Auto-pick from candidates in directory
            string[] promptCandidates =
            {
                Path.Combine(directory, $"{sourceLang}-to-{targetLang}.prompt"),
                Path.Combine(directory, $"{targetLang}.prompt"),
                Path.Combine(directory, $"{sourceLang}.prompt")
            };
            return promptCandidates.FirstOrDefault(File.Exists);
        }

        static string BuildPrompt(string code, string src, string tgt) =>
$@"You are a code translation assistant.
Convert exactly this one file from {src} to {tgt}, preserving its structure, comments and functionality.
Show me the source code only, full source code, and nothing but the source code.

--- FILE TO TRANSLATE ---
{code}
--- END FILE ---";

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
                    // boolean switch args
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

        static string GetExtensionFor(string lang) => lang.ToLower() switch
        {
            "csharp" or "c#" => ".cs",
            "vbnet" or "vb" => ".vb",
            "fsharp" or "fs" or "f#" => ".fs",
            "java" => ".java",
            "kotlin" => ".kt",
            "scala" => ".scala",
            "groovy" => ".groovy",
            "c" => ".c",
            "cpp" or "c++" => ".cpp",
            "objc" or "objective-c" => ".m",
            "rust" => ".rs",
            "go" => ".go",
            "javascript" or "js" => ".js",
            "typescript" or "ts" => ".ts",
            "python" or "py" => ".py",
            "ruby" or "rb" => ".rb",
            "php" => ".php",
            "perl" or "pl" => ".pl",
            "dart" => ".dart",
            "lua" => ".lua",
            "coffeescript" or "coffee" => ".coffee",
            "r" => ".r",
            "shell" or "bash" => ".sh",
            "powershell" => ".ps1",
            "haskell" or "hs" => ".hs",
            "clojure" or "clj" => ".clj",
            "elixir" or "ex" => ".ex",
            "erlang" or "erl" => ".erl",
            "lisp" => ".lisp",
            "scheme" => ".scm",
            "html" => ".html",
            "xml" => ".xml",
            "css" => ".css",
            "json" => ".json",
            "yaml" or "yml" => ".yaml",
            "markdown" or "md" => ".md",
            "toml" => ".toml",
            "ini" => ".ini",
            "csv" => ".csv",
            "sql" => ".sql",
            "matlab" or "m" => ".m",
            "racket" => ".rkt",
            "elm" => ".elm",
            "graphql" => ".graphql",
            "dockerfile" => "Dockerfile",
            "makefile" => "Makefile",
            "cmake" => "CMakeLists.txt",
            _ => ".txt"
        };

        static IEnumerable<string> GetSourceExtensions(string lang) => lang.ToLower() switch
        {
            "csharp" or "c#" => new[] { ".cs" },
            "vbnet" or "vb" => new[] { ".vb" },
            "fsharp" or "fs" or "f#" => new[] { ".fs" },
            "java" => new[] { ".java" },
            "kotlin" => new[] { ".kt" },
            "scala" => new[] { ".scala" },
            "groovy" => new[] { ".groovy" },
            "c" => new[] { ".c" },
            "cpp" or "c++" => new[] { ".cpp", ".cc", ".cxx" },
            "objc" or "objective-c" => new[] { ".m", ".mm" },
            "rust" => new[] { ".rs" },
            "go" => new[] { ".go" },
            "javascript" or "js" => new[] { ".js", ".mjs", ".cjs" },
            "typescript" or "ts" => new[] { ".ts", ".tsx" },
            "python" or "py" => new[] { ".py", ".pyw" },
            "ruby" or "rb" => new[] { ".rb", ".erb" },
            "php" => new[] { ".php", ".phtml" },
            "perl" or "pl" => new[] { ".pl", ".pm" },
            "dart" => new[] { ".dart" },
            "lua" => new[] { ".lua" },
            "coffeescript" or "coffee" => new[] { ".coffee" },
            "r" => new[] { ".r", ".R" },
            "shell" or "bash" => new[] { ".sh" },
            "powershell" => new[] { ".ps1", ".psm1" },
            "haskell" or "hs" => new[] { ".hs", ".lhs" },
            "clojure" or "clj" => new[] { ".clj", ".cljs", ".cljc" },
            "elixir" or "ex" => new[] { ".ex", ".exs" },
            "erlang" or "erl" => new[] { ".erl", ".hrl" },
            "lisp" => new[] { ".lisp", ".lsp" },
            "scheme" => new[] { ".scm", ".ss" },
            "html" => new[] { ".html", ".htm" },
            "xml" => new[] { ".xml", ".xsl", ".xsd" },
            "css" => new[] { ".css" },
            "json" => new[] { ".json" },
            "yaml" or "yml" => new[] { ".yaml", ".yml" },
            "markdown" or "md" => new[] { ".md", ".markdown" },
            "toml" => new[] { ".toml" },
            "ini" => new[] { ".ini" },
            "csv" => new[] { ".csv" },
            "sql" => new[] { ".sql" },
            "matlab" or "m" => new[] { ".m", ".mlx" },
            "racket" => new[] { ".rkt" },
            "elm" => new[] { ".elm" },
            "graphql" => new[] { ".graphql", ".gql" },
            "dockerfile" => new[] { "Dockerfile" },
            "makefile" => new[] { "Makefile" },
            "cmake" => new[] { "CMakeLists.txt" },
            _ => new[] { ".txt" }
        };
    }
}
