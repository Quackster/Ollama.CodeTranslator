# CodeTranslator

A command-line tool that automatically translates source code between different programming languages using AI models via Ollama API.

## Table of Contents

1. [Features](#features)
2. [Requirements](#requirements)
3. [Installation](#installation)
4. [Usage](#usage)
   - [Basic Usage](#basic-usage)
   - [Advanced Usage](#advanced-usage)
   - [Command Line Options](#command-line-options)
   - [Supported Languages](#supported-languages)
5. [File Extensions Configuration](#file-extensions-configuration)
   - [extensions.json](#extensionsjson)
   - [Adding Custom Extensions](#adding-custom-extensions)
   - [Example Configuration](#example-configuration)
6. [Custom Prompts](#custom-prompts)
   - [Prompt Variables](#prompt-variables)
   - [Example Prompt File](#example-prompt-file)
7. [Examples](#examples)
8. [Output Structure](#output-structure)
9. [Logging](#logging)
10. [Error Handling](#error-handling)
11. [Contributing](#contributing)
12. [License](#license)

---

## Features

- **Multi-language Support**: Translate between 40+ programming languages
- **Batch Processing**: Convert entire directories at once
- **Custom Prompts**: Fine-tune translation behavior with custom prompt files
- **Flexible Output**: Custom output directories and file organization
- **Detailed Logging**: Track translation progress
- **Safety Features**: Overwrite protection and dry-run mode
- **AI Integration**: Works with any Ollama-compatible model
- **Configurable Extensions**: Customize file extensions through extensions.json

## Requirements

- .NET 6.0 or later
- [Ollama](https://ollama.ai/) running locally or access to an Ollama-compatible API endpoint
- An AI model capable of code translation (default: `qwen2.5-coder:3b`)

## Installation

1. Clone the repository:
```bash
git clone https://github.com/Quackster/Ollama.CodeTranslator.git
cd Ollama.CodeTranslator
```

2. Build the project:
```bash
dotnet build -c Release
```
## Usage

### Basic Usage

```bash
CodeTranslator --directory /path/to/source/code
```

This will translate all Java files to C# by default.

### Advanced Usage

```bash
CodeTranslator --directory ./my-project \
               --source Java \
               --target Python \
               --model llama2:13b \
               --output ./converted \
               --log ./translation.log \
               --verbose
```

### Command Line Options

| Option | Description | Default |
|--------|-------------|---------|
| `--directory` | **Required.** Input directory containing source files | - |
| `--source` | Source programming language | `Java` |
| `--target` | Target programming language | `CSharp` |
| `--model` | Ollama model name | `qwen2.5-coder:3b` |
| `--api-url` | Ollama API endpoint | `http://localhost:11434/api/generate` |
| `--output` | Output directory | `{input-dir}/converted_{target}` |
| `--log` | Log file path | `{input-dir}/translation_dispatch.log` |
| `--overwrite` | Overwrite existing output files | `false` |
| `--dry-run` | Show what would be translated without doing it | `false` |
| `--verbose` | Enable verbose logging output | `false` |

### Supported Languages

**Programming Languages:**
C#, VB.NET, F#, Java, Kotlin, Scala, Groovy, C, C++, Objective-C, Rust, Go, JavaScript, TypeScript, Python, Ruby, PHP, Perl, Dart, Lua, CoffeeScript, R, Shell/Bash, PowerShell, Haskell, Clojure, Elixir, Erlang, Lisp, Scheme, MATLAB, Racket, Elm

**Markup & Data Languages:**
HTML, XML, CSS, JSON, YAML, Markdown, TOML, INI, CSV, SQL, GraphQL

**Build Systems:**
Dockerfile, Makefile, CMake

## File Extensions Configuration

### extensions.json

CodeTranslator uses a configurable `extensions.json` file to map programming languages to their corresponding file extensions. This file is automatically created in your working directory when you first run the tool.

**Key Features:**
- **Auto-generation**: The file is created automatically with sensible defaults if it doesn't exist
- **Case-insensitive**: Language names are matched case-insensitively
- **Multiple extensions**: Each language can have multiple file extensions
- **Customizable**: You can edit the file to add your own extensions or modify existing ones

### Adding Custom Extensions

To add support for additional file extensions or new languages:

1. **Run the tool once** to generate the default `extensions.json` file
2. **Edit the file** to add your custom mappings
3. **Use your custom language names** in the `--source` and `--target` parameters

### Example Configuration

Here's a sample of what the `extensions.json` file looks like:

```json
{
  "csharp": [".cs"],
  "java": [".java"],
  "python": [".py", ".pyw"],
  "javascript": [".js", ".mjs", ".cjs"],
  "typescript": [".ts", ".tsx"],
  "cpp": [".cpp", ".cc", ".cxx"],
  "lingo": [".ls"],
  "shockwave": [".ls"]
}
```

**Adding a new language:**
```json
{
  "myCustomLang": [".mcl", ".custom"],
  "assembly": [".asm", ".s"]
}
```

**Multiple aliases for the same language:**
```json
{
  "csharp": [".cs"],
  "c#": [".cs"],
  "cs": [".cs"]
}
```

**Note**: After modifying `extensions.json`, the changes take effect immediately on the next run.

## Custom Prompts

CodeTranslator supports custom prompt files to fine-tune translation behavior. Create a prompt file with one of these naming patterns:

- `{source}-to-{target}.prompt` (e.g., `Java-to-Python.prompt`)
- `{target}.prompt` (e.g., `Python.prompt`) 
- `{source}.prompt` (e.g., `Java.prompt`)

### Prompt Variables

Your prompt file can use these placeholders:
- `{sourceLang}` - Source language name
- `{targetLang}` - Target language name  
- `{code}` - The source code to translate

### Example Prompt File

```
You are an expert code translator specializing in {sourceLang} to {targetLang} conversion.

Convert the following {sourceLang} code to idiomatic {targetLang}, following these guidelines:
- Preserve all functionality and logic
- Use {targetLang} naming conventions
- Add appropriate comments explaining complex translations
- Ensure the code follows {targetLang} best practices

Source code:
{code}

Provide only the translated {targetLang} code, no explanations or markdown formatting.
```

## Examples

### Translate Java project to C#
```bash
CodeTranslator --directory ./java-project --source Java --target CSharp
```

### Convert Python scripts to JavaScript with custom model
```bash
CodeTranslator --directory ./python-scripts \
               --source Python \
               --target JavaScript \
               --model codellama:13b-instruct
```

### Dry run to preview translations
```bash
CodeTranslator --directory ./source --dry-run --verbose
```

### Use remote Ollama instance
```bash
CodeTranslator --directory ./code \
               --api-url http://remote-server:11434/api/generate
```

### Working with custom extensions
```bash
# First run creates extensions.json with defaults
CodeTranslator --directory ./my-code

# Edit extensions.json to add your language mappings
# Then use your custom language names:
CodeTranslator --directory ./legacy-code --source Lingo --target JavaScript
```

## Output Structure

CodeTranslator preserves the directory structure of your input files:

```
input-directory/
├── src/
│   ├── Main.java
│   └── utils/
│       └── Helper.java
├── extensions.json          # Auto-generated config file
└── converted_CSharp/
    └── src/
        ├── Main.cs
        └── utils/
            └── Helper.cs
```

## Logging

All translation activities are logged with timestamps:

```
2024-03-15T10:30:00.000Z SENT src/Main.java -> src/Main.cs
2024-03-15T10:30:15.000Z TRANSLATED src/Main.java -> src/Main.cs
2024-03-15T10:30:20.000Z SENT src/utils/Helper.java -> src/utils/Helper.cs
```

## Error Handling

- **Missing directories**: Tool exits with error code 2 if input directory doesn't exist
- **Missing prompt files**: Exits with error if user-specified prompt file is not found  
- **Existing files**: Skipped with warning unless `--overwrite` is specified
- **API errors**: Individual file failures are logged and processing continues
- **Incomplete translations**: Detected by keywords like "incomplete" or "provide more details"
- **Network timeouts**: 30-minute timeout per API request to handle large files
- **File system errors**: Proper error handling for file I/O operations

**Exit Codes:**
- `0`: Success - all files processed
- `1`: Invalid command line arguments
- `2`: Missing input directory or prompt file

## License

This project is licensed under the Apache 2.0 license.
