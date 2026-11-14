# Linting and formatting

## Per-developer setup
1. Install the VS Code extensions (VS Code suggests them from `.vscode/extensions.json`):
   - `SonarSource.sonarlint-vscode` (SonarLint / SonarQube for IDE)
   - `ms-dotnettools.csharp` + `ms-dotnettools.csdevkit`
   - `EditorConfig.EditorConfig`
2. Enable the git hook (once per clone):
   ```
   git config core.hooksPath .githooks
   ```

## Rules
- `.editorconfig`: **tabs** (not spaces) for everything except YAML, JSON, XML/csproj and Markdown. C# files use CRLF, matching the Windows working tree (`core.autocrlf=true`).
- Linting: SonarLint in the editor plus `dotnet format`. `Directory.Build.props` is untouched on purpose: it sets `TreatWarningsAsErrors=true`, so enabling style analyzers in the build would fail on the existing space-indented code.
- `pre-commit` hook: runs `dotnet format whitespace --verify-no-changes` only on the staged `.cs` files. A violation blocks the commit.

## Commands
```
dotnet format whitespace delivery-schedule-management.slnx --verify-no-changes   # check only
dotnet format whitespace delivery-schedule-management.slnx --include <file>      # fix one file
```

## Note
Existing code is indented with 4 spaces. The hook only checks files you touch; migrating the whole repo to tabs should be its own commit (`dotnet format whitespace`).
