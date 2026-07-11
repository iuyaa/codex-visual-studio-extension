# Visual Codex Studio

Run Codex inside Visual Studio without leaving the IDE.

`Visual Codex Studio` adds a docked Codex tool window to Visual Studio 2022 and 2026. It uses your local Codex CLI installation, keeps the conversation inside Visual Studio, and stays compatible with the active Visual Studio theme and the extension's current UI languages.

## ⚠️ Project not actively maintained

This repository is no longer actively maintained.

The code remains available for reference and forking under the existing license, and main eventually be revisted.
Issues and pull requests may not be reviewed in a timely manner (or ever).

Users are encouraged to fork the project if they want to continue development.

## ⚠️ Disclaimer

This extension is an independent project and is not affiliated with, endorsed by, or officially associated with OpenAI or ChatGPT. Any logos or references used are for integration purposes only.

## Highlights

- Docked chat-style Codex panel inside Visual Studio
- VS Code-style Codex header, composer controls, and settings access
- Independent settings tab in the Visual Studio document well, with immediate synchronization back to the chat
- Visual Studio commands matching Codex for VS Code: open sidebar, new agent, add selection/file to thread, review selection, and implement with Codex
- Normal mode and plan mode
- Separate additional-information prompt window so the chat stays visible while answering plan questions
- Markdown output with a render/text toggle for easier reading and copy/paste
- Model selection
- Reasoning effort selection (`minimal`, `low`, `medium`, `high`, `xhigh`)
- Verbosity selection (`low`, `medium`, `high`)
- `approval_policy` and `sandbox_mode` selection
- Atomic local settings persistence with Windows user-level protection for secrets and prompt history
- Bounded local prompt history with an explicit clear action
- Bounded long-conversation hydration in the official WebView: 120 recent turns or 2 MB initially, followed by explicit 20-turn batches
- Lazy rendering and display-only limits for large Markdown, diffs, tool output, and streaming output while the full Codex session remains intact
- Manual context compaction plus optional automatic compaction when context usage reaches 85%
- Image attachment from the clipboard or file picker
- Local image input through the Codex app-server protocol
- Solution-aware `@file` search while typing
- Session usage and rate-limit visibility in the Visual Studio UI
- VS Code-compatible composer defaults: Enter sends, Shift+Enter inserts a newline, and Ctrl+Enter always sends

## Authentication and Provider Support

Authentication belongs to the person running Visual Studio. The recommended setup is to run `codex login` and sign in with that user's own ChatGPT account; the extension then reuses the local Codex CLI session.

Advanced user-local setups remain supported:

- `OPENAI_API_KEY` defined in that user's environment
- Provider-based configuration in `~/.codex/config.toml`
- Profile-based configuration that selects a provider from `config.toml`

No ChatGPT cookie, token, API key, `auth.json`, or publisher credential is bundled in the repository or VSIX. Each installation uses only the authentication available in that Windows user's local Codex configuration. The WebView is not given `OPENAI_API_KEY` from the Visual Studio process.

## Requirements

- Visual Studio 2022 or Visual Studio 2026, 64-bit
- Codex CLI installed locally
- A working local Codex authentication or provider configuration

## Notes

- The extension calls your local Codex CLI or app-server flow; it does not bundle the runtime.
- If your local Codex setup already works in the terminal, the extension is designed to reuse that setup.
- Image attachments are sent as `localImage` inputs through `codex app-server`; the selected local file must remain readable until the turn starts.
- Theme support uses Visual Studio theme resources so the UI works in light and dark themes.
- UI strings remain localized through the extension's existing localization pipeline.

## Manual Future-Proofing

The extension keeps its local settings in `%LOCALAPPDATA%\CodexVsix\settings.json`, outside the source tree and outside the VSIX package. If new Codex or provider capabilities appear before the extension is updated, users can open this file from the Codex settings UI or edit non-sensitive fields directly. Environment variables, raw TOML overrides, additional CLI arguments, and prompt history are encrypted for the current Windows user with DPAPI. Never copy this user-local file or `~/.codex/auth.json` into the repository.

Useful fields:

- `FollowUpQueueMode`: compatible with VS Code values `queue`, `steer`, or `interrupt`.
- `AutoCompactLongConversations`: opt in to context compaction after a turn completes at 85% context usage.
- `ComposerEnterBehavior`: `enter`, `cmdIfMultiline`, or `ctrlEnter`.
- `ReviewDelivery`: `inline` or `detached`.
- `OpenOnStartup`: focuses the Codex tool window when Visual Studio opens a solution.
- `CustomModels`: extra model ids shown in the model selector.
- `CustomReasoningEfforts`: extra reasoning effort values shown in the reasoning selector.
- `CustomVerbosityOptions`: extra verbosity values shown in the verbosity selector.
- `CustomServiceTiers`: extra service tier values shown in the speed selector.

Manual option entries can be either a raw value, such as `"minimal"`, or a label/value pair, such as `"Very high|very_high"` or `"Very high=very_high"`.

Provider and profile configuration should continue to live in `~/.codex/config.toml`; the extension starts the local Codex runtime and lets that runtime resolve provider-specific behavior.

## Build

Release packaging uses full Visual Studio MSBuild.

```powershell
$vswhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
$install = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
$msbuild = Join-Path $install "MSBuild\Current\Bin\MSBuild.exe"

dotnet restore CodexVs2026Extension.sln --locked-mode
dotnet test CodexVs2026Extension.sln -c Release --no-restore

& $msbuild `
  CodexVsix\CodexVsix.csproj `
  /t:Rebuild `
  /p:Configuration=Release `
  /p:BuildVsixPackage=true

.\scripts\Test-VsixPackage.ps1 `
  -VsixPath CodexVsix\CodexVsix.vsix `
  -ExpectedVersion 1.3.0
```

`dotnet build` can compile the project, but VSIX packaging targets should be produced with Visual Studio `MSBuild.exe`.

The CI workflow restores locked packages with NuGet auditing, runs the tests, builds the VSIX, and validates its contents. Releases use the committed version from `Directory.Build.props`; run `scripts/Set-VsixVersion.ps1` and commit the result before creating a tag. The release workflow signs the package when `VSIX_SIGNING_PFX_BASE64`, `VSIX_SIGNING_PFX_PASSWORD`, and `VSIX_SIGNING_CERT_SHA256` are configured, otherwise it publishes the verified unsigned package. Visual Studio Marketplace publication is intentionally manual and is never triggered by the GitHub release workflow.

Third-party code and its reviewed bundle hash are documented in `THIRD-PARTY-NOTICES.md` and included in the VSIX.

## Repository

- Extension project: `CodexVsix/CodexVsix.csproj`
- Marketplace overview: `marketplace/overview.md`
