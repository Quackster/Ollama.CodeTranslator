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
5. [Custom Prompts](#custom-prompts)
   - [Prompt Variables](#prompt-variables)
   - [Example Prompt File](#example-prompt-file)
6. [Examples](#examples)
7. [Output Structure](#output-structure)
8. [Logging](#logging)
9. [Error Handling](#error-handling)
10. [Contributing](#contributing)
11. [License](#license)

---

## Features

- **Multi-language Support**: Translate between 40+ programming languages
- **Batch Processing**: Convert entire directories at once
- **Custom Prompts**: Fine-tune translation behavior with custom prompt files
- **Flexible Output**: Custom output directories and file organization
- **Detailed Logging**: Track translation progress
- **Safety Features**: Overwrite protection and dry-run mode
- **AI Integration**: Works with any Ollama-compatible model

## Requirements

- .NET 6.0 or later
- [Ollama](https://ollama.ai/) running locally or access to an Ollama-compatible API endpoint
- An AI model capable of code translation (default: `qwen2.5-coder:3b`)

## Installation

1. Clone the repository:
```bash
git clone https://github.com/yourusername/CodeTranslator.git
cd CodeTranslator
```

2. Build the project:
```bash
dotnet build -c Release
```

3. (Optional) Create a global tool:
```bash
dotnet pack -c Release
dotnet tool install --global --add-source ./bin/Release CodeTranslator
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

## Output Structure

CodeTranslator preserves the directory structure of your input files:

```
input-directory/
├── src/
│   ├── Main.java
│   └── utils/
│       └── Helper.java
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

## License

This project is licensed under the Apache 2.0 license.
