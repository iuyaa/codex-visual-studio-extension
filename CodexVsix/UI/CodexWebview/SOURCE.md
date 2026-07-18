# Codex webview bundle

This directory contains the frozen official Codex webview bundle used by the
CyberVinci/Theia port at:

`C:\Users\Rodrigo\Desktop\CyberVinci\Modificacoes\Codex\packages\codex\resources`

The synchronized host target identifies the upstream UI as
`OpenAI.chatgpt` `26.5527.31454`. The Visual Studio host injects its own
WebView2 transport, IDE theme variables, locale, and app-server bridge at
runtime; the bundled UI assets are otherwise kept unchanged.

`codex-visual-studio-history-guard.js` is owned by this Visual Studio host and
is intentionally not copied from the CyberVinci source. It adds bounded
history controls and browser-native lazy rendering without changing the
frozen official bundle or its synchronized shim.

`codex-visual-studio-diagnostics.js` is also host-owned. It contributes the
opt-in diagnostics switch to the official settings surface while preserving
the frozen upstream bundle. Detailed browser logging remains disabled until
that setting is explicitly enabled.

Run `scripts/Sync-CyberVinciCodexWebview.ps1` to refresh this frozen copy from
the local CyberVinci source.
