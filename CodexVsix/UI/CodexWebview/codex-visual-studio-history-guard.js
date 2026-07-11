// Visual Studio host-owned controls for bounded Codex conversation history.
(function () {
    'use strict';

    function postToHost(message) {
        if (typeof window.acquireVsCodeApi !== 'function') return;
        window.acquireVsCodeApi().postMessage(message);
    }

    let historyWindowState;
    let historyCompactionState;

    function getHistoryStrings() {
        const language = (document.documentElement.lang || navigator.language || 'en').toLowerCase();
        if (language.startsWith('pt')) {
            return {
                summary: 'Exibindo {turns} turnos recentes ({size}). O hist\u00f3rico anterior foi pausado para manter o Visual Studio responsivo.',
                load: 'Carregar {count} anteriores',
                loading: 'Carregando...',
                compact: 'Compactar contexto',
                compacting: 'Compactando...',
                compacted: 'Contexto compactado.',
                compactFailed: 'N\u00e3o foi poss\u00edvel compactar o contexto.',
                autoCompact: 'Compactar automaticamente em 85%'
            };
        }
        if (language.startsWith('es')) {
            return {
                summary: 'Mostrando {turns} turnos recientes ({size}). El historial anterior est\u00e1 pausado para mantener Visual Studio \u00e1gil.',
                load: 'Cargar {count} anteriores',
                loading: 'Cargando...',
                compact: 'Compactar contexto',
                compacting: 'Compactando...',
                compacted: 'Contexto compactado.',
                compactFailed: 'No se pudo compactar el contexto.',
                autoCompact: 'Compactar autom\u00e1ticamente al 85%'
            };
        }
        if (language.startsWith('fr')) {
            return {
                summary: 'Affichage de {turns} tours r\u00e9cents ({size}). L\u2019historique ant\u00e9rieur est suspendu pour pr\u00e9server la r\u00e9activit\u00e9 de Visual Studio.',
                load: 'Charger {count} pr\u00e9c\u00e9dents',
                loading: 'Chargement...',
                compact: 'Compacter le contexte',
                compacting: 'Compactage...',
                compacted: 'Contexte compact\u00e9.',
                compactFailed: 'Impossible de compacter le contexte.',
                autoCompact: 'Compacter automatiquement \u00e0 85 %'
            };
        }
        if (language.startsWith('de')) {
            return {
                summary: '{turns} aktuelle Durchl\u00e4ufe werden angezeigt ({size}). Der \u00e4ltere Verlauf wurde pausiert, damit Visual Studio reaktionsf\u00e4hig bleibt.',
                load: '{count} \u00e4ltere laden',
                loading: 'Wird geladen...',
                compact: 'Kontext komprimieren',
                compacting: 'Wird komprimiert...',
                compacted: 'Kontext komprimiert.',
                compactFailed: 'Der Kontext konnte nicht komprimiert werden.',
                autoCompact: 'Bei 85 % automatisch komprimieren'
            };
        }
        return {
            summary: 'Showing {turns} recent turns ({size}). Older history is paused to keep Visual Studio responsive.',
            load: 'Load {count} earlier',
            loading: 'Loading...',
            compact: 'Compact context',
            compacting: 'Compacting...',
            compacted: 'Context compacted.',
            compactFailed: 'The context could not be compacted.',
            autoCompact: 'Automatically compact at 85%'
        };
    }

    function formatHistoryText(template, values) {
        return Object.keys(values).reduce(function (text, key) {
            return text.replace('{' + key + '}', String(values[key]));
        }, template);
    }

    function formatBytes(value) {
        const bytes = Number(value) || 0;
        if (bytes < 1024) return bytes + ' B';
        if (bytes < 1024 * 1024) return Math.round(bytes / 1024) + ' KB';
        return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
    }

    function installHistoryPerformanceStyles() {
        if (document.getElementById('codex-vs-history-performance')) return;
        const style = document.createElement('style');
        style.id = 'codex-vs-history-performance';
        style.textContent = [
            '[data-testid="dynamic-tool-call-group-body"],',
            '[data-testid="pending-mcp-tool-calls-body"],',
            'pre, table {',
            '  content-visibility: auto;',
            '  contain-intrinsic-size: auto 180px;',
            '}',
            '#codex-vs-history-window {',
            '  position: fixed;',
            '  right: 14px;',
            '  bottom: 92px;',
            '  z-index: 2147483000;',
            '  display: none;',
            '  max-width: min(560px, calc(100vw - 28px));',
            '  padding: 10px 12px;',
            '  border: 1px solid var(--vscode-notifications-border, var(--token-border-default, rgba(127,127,127,.35)));',
            '  border-radius: 12px;',
            '  color: var(--vscode-notifications-foreground, var(--token-foreground, inherit));',
            '  background: var(--vscode-notifications-background, var(--token-background-primary, rgba(32,32,32,.96)));',
            '  box-shadow: 0 10px 30px rgba(0,0,0,.22);',
            '  font-family: var(--vscode-font-family, inherit);',
            '  font-size: 12px;',
            '  line-height: 1.4;',
            '  backdrop-filter: blur(10px);',
            '}',
            '#codex-vs-history-window[data-visible="true"] { display: block; }',
            '#codex-vs-history-actions { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; margin-top: 8px; }',
            '#codex-vs-history-window button {',
            '  min-height: 26px;',
            '  padding: 3px 9px;',
            '  border: 1px solid var(--vscode-button-border, transparent);',
            '  border-radius: 7px;',
            '  color: var(--vscode-button-foreground, #fff);',
            '  background: var(--vscode-button-background, #0e639c);',
            '  cursor: pointer;',
            '  font: inherit;',
            '}',
            '#codex-vs-history-window button.codex-vs-secondary {',
            '  color: var(--vscode-button-secondaryForeground, var(--vscode-notifications-foreground, inherit));',
            '  background: var(--vscode-button-secondaryBackground, rgba(127,127,127,.18));',
            '}',
            '#codex-vs-history-window button:disabled { opacity: .62; cursor: default; }',
            '#codex-vs-history-auto { display: inline-flex; align-items: center; gap: 6px; cursor: pointer; }',
            '#codex-vs-history-result { min-height: 1em; opacity: .8; margin-top: 4px; }',
            '@media (max-width: 520px) { #codex-vs-history-window { left: 10px; right: 10px; bottom: 84px; max-width: none; } }',
            '@media (prefers-reduced-motion: no-preference) { #codex-vs-history-window[data-visible="true"] { animation: codex-vs-history-in .16s ease-out; } }',
            '@keyframes codex-vs-history-in { from { opacity: 0; transform: translateY(6px); } to { opacity: 1; transform: translateY(0); } }'
        ].join('\n');
        (document.head || document.documentElement).appendChild(style);
    }

    function ensureHistoryWindow() {
        let windowNode = document.getElementById('codex-vs-history-window');
        if (windowNode || !document.body) return windowNode;

        windowNode = document.createElement('section');
        windowNode.id = 'codex-vs-history-window';
        windowNode.setAttribute('role', 'status');
        windowNode.setAttribute('aria-live', 'polite');

        const summary = document.createElement('div');
        summary.id = 'codex-vs-history-summary';
        windowNode.appendChild(summary);

        const result = document.createElement('div');
        result.id = 'codex-vs-history-result';
        windowNode.appendChild(result);

        const actions = document.createElement('div');
        actions.id = 'codex-vs-history-actions';

        const loadButton = document.createElement('button');
        loadButton.id = 'codex-vs-history-load';
        loadButton.type = 'button';
        loadButton.addEventListener('click', function () {
            if (!historyWindowState) return;
            postToHost({ type: 'history-load-older', threadId: historyWindowState.threadId });
        });
        actions.appendChild(loadButton);

        const compactButton = document.createElement('button');
        compactButton.id = 'codex-vs-history-compact';
        compactButton.type = 'button';
        compactButton.className = 'codex-vs-secondary';
        compactButton.addEventListener('click', function () {
            if (!historyWindowState) return;
            postToHost({ type: 'history-compact', threadId: historyWindowState.threadId });
        });
        actions.appendChild(compactButton);

        const autoLabel = document.createElement('label');
        autoLabel.id = 'codex-vs-history-auto';
        const autoCheckbox = document.createElement('input');
        autoCheckbox.id = 'codex-vs-history-auto-checkbox';
        autoCheckbox.type = 'checkbox';
        autoCheckbox.addEventListener('change', function () {
            if (!historyWindowState) return;
            postToHost({
                type: 'history-auto-compact-set',
                threadId: historyWindowState.threadId,
                enabled: autoCheckbox.checked
            });
        });
        const autoText = document.createElement('span');
        autoText.id = 'codex-vs-history-auto-text';
        autoLabel.appendChild(autoCheckbox);
        autoLabel.appendChild(autoText);
        actions.appendChild(autoLabel);

        windowNode.appendChild(actions);
        document.body.appendChild(windowNode);
        return windowNode;
    }

    function updateHistoryWindow() {
        const windowNode = ensureHistoryWindow();
        if (!windowNode || !historyWindowState) return;
        const strings = getHistoryStrings();
        const isVisible = historyWindowState.isVisible === true;
        windowNode.dataset.visible = isVisible ? 'true' : 'false';
        if (!isVisible) return;

        document.getElementById('codex-vs-history-summary').textContent = formatHistoryText(strings.summary, {
            turns: historyWindowState.loadedTurns || 0,
            size: formatBytes(historyWindowState.loadedBytes)
        });

        const loadButton = document.getElementById('codex-vs-history-load');
        loadButton.textContent = historyWindowState.isLoading
            ? strings.loading
            : formatHistoryText(strings.load, { count: historyWindowState.loadBatchSize || 20 });
        loadButton.disabled = historyWindowState.isLoading || historyWindowState.canLoadMore === false;

        const isCompacting = historyCompactionState && historyCompactionState.isCompacting === true;
        const compactButton = document.getElementById('codex-vs-history-compact');
        compactButton.textContent = isCompacting ? strings.compacting : strings.compact;
        compactButton.disabled = isCompacting;

        const autoCheckbox = document.getElementById('codex-vs-history-auto-checkbox');
        autoCheckbox.checked = historyWindowState.autoCompactEnabled === true;
        document.getElementById('codex-vs-history-auto-text').textContent = strings.autoCompact;

        const result = document.getElementById('codex-vs-history-result');
        result.textContent = historyCompactionState && historyCompactionState.succeeded === true
            ? strings.compacted
            : historyCompactionState && historyCompactionState.succeeded === false
                ? strings.compactFailed
                : '';
    }

    window.addEventListener('message', function (event) {
        const data = event.data;
        if (!data || typeof data !== 'object') return;
        if (data.type === 'history-window-state') {
            historyWindowState = data;
            updateHistoryWindow();
        }
        if (data.type === 'history-compaction-state') {
            historyCompactionState = data;
            updateHistoryWindow();
        }
    });

    installHistoryPerformanceStyles();
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', updateHistoryWindow, { once: true });
    }
})();
