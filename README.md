# CodeTranslator

Bulk translate source code files from one language to another using Ollama LLM API.

## Features

- **Batch translate** all source files in a directory
- **Configurable via command-line:** source/target language, Ollama model, API URL, output folder, logging, and more
- **Automatic language detection** for file extensions
- **Custom prompt** support: use a `.prompt` file in your project dir with `{src}`, `{tgt}`, `{code}` tokens
- **Skip or overwrite** existing output files
- **Dry run** mode for previewing actions
- **Verbose** mode for debug/monitoring

## Requirements

- [.NET 6+ SDK](https://dotnet.microsoft.com/download)
- [Ollama](https://ollama.com/) running locally, with your chosen model pulled (default: `qwen2.5-coder:3b`)

## Installation

```bash
# Optional: add System.CommandLine if using .NET 6
dotnet add package System.CommandLine --prerelease
```

## Usage

### Basic Usage
```bash
dotnet run --project CodeTranslator --directory ./MyProject
```

### Full Options
```bash
dotnet run --project CodeTranslator --directory <input-dir>
    [--source <SourceLang>]
    [--target <TargetLang>]
    [--model <OllamaModel>]
    [--api-url <APIEndpoint>]
    [--output <OutputDir>]
    [--log <LogFile>]
    [--overwrite]
    [--dry-run]
    [--verbose]
```

### Examples

**Translate all Java files to C# in ./MyProject:**
```bash
dotnet run --project CodeTranslator --directory ./MyProject
```

**Translate Python to Go using DeepSeek, with output to ./out:**
```bash
dotnet run --project CodeTranslator --directory ./src --source Python --target Go --model deepseek-coder:6.7b --output ./out
```

**Overwrite existing output and enable verbose logging:**
```bash
dotnet run --project CodeTranslator --directory ./src --overwrite --verbose
```

**Just show what would be done, don't translate:**
```bash
dotnet run --project CodeTranslator --directory ./src --dry-run
```

## Custom Prompt

Place a `.prompt` file in your input directory.

You can use these tokens in the prompt, which will be auto-replaced:
- `{src}` — Source language name
- `{tgt}` — Target language name
- `{code}` — File content

**Example .prompt:**
```
Convert this {src} code to {tgt}. Preserve all comments and match code style if possible.

{code}
```

If no `.prompt` is found, a default prompt is used.

## Output

**Translated files:**
By default in `converted_<TargetLang>` folder inside your input directory (or as set by `--output`)

**Log file:**
`translation_dispatch.log` (or as set by `--log`)

## Supported Languages

All common languages supported, including:
`Java`, `CSharp`, `Python`, `Go`, `C`, `C++`, `Kotlin`, `TypeScript`, `JavaScript`, `Ruby`, `PHP`, `Rust`, `Haskell`, `Swift`, `Scala`, and more.
## Contributing

[Add contributing guidelines here]
