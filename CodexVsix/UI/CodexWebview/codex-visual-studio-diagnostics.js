// Host-owned Visual Studio diagnostics setting. Kept outside the frozen official bundle.
(function () {
    'use strict';

    const settingId = 'codex-vs-diagnostics-setting';
    const initialRouteNode = document.querySelector('meta[name="initial-route"]');
    let currentRoute = (initialRouteNode && initialRouteNode.getAttribute('content')) || '/';
    let enabled = document.querySelector('meta[name="codex-diagnostic-logging-enabled"]')?.getAttribute('content') === 'true';
    let mountScheduled = false;
    let observer = null;

    function strings() {
        const language = (document.documentElement.lang || navigator.language || 'en').toLowerCase();
        if (language.startsWith('pt')) {
            return {
                group: 'Diagnóstico',
                label: 'Habilitar logs de diagnóstico',
                description: 'Grava diagnósticos técnicos do renderizador e do WebView em arquivos locais rotativos. Desativado por padrão.',
                composerHeadings: ['Editor', 'Composer']
            };
        }
        if (language.startsWith('es')) {
            return {
                group: 'Diagnóstico',
                label: 'Habilitar registros de diagnóstico',
                description: 'Guarda diagnósticos técnicos del renderizador y WebView en archivos locales rotativos. Desactivado por defecto.',
                composerHeadings: ['Editor', 'Redactor', 'Composer']
            };
        }
        if (language.startsWith('fr')) {
            return {
                group: 'Diagnostic',
                label: 'Activer les journaux de diagnostic',
                description: 'Enregistre les diagnostics techniques du moteur de rendu et de WebView dans des fichiers locaux rotatifs. Désactivé par défaut.',
                composerHeadings: ['Éditeur', 'Composer']
            };
        }
        if (language.startsWith('de')) {
            return {
                group: 'Diagnose',
                label: 'Diagnoseprotokolle aktivieren',
                description: 'Schreibt technische Renderer- und WebView-Diagnosen in rotierende lokale Dateien. Standardmäßig deaktiviert.',
                composerHeadings: ['Editor', 'Composer']
            };
        }
        return {
            group: 'Diagnostics',
            label: 'Enable diagnostic logs',
            description: 'Write renderer and WebView diagnostics to local rotating files. Disabled by default.',
            composerHeadings: ['Composer']
        };
    }

    function post(message) {
        try {
            window.acquireVsCodeApi().postMessage(message);
        } catch {
            // The host bridge may not be available during the first DOM pass.
        }
    }

    function isGeneralSettingsRoute() {
        const normalized = String(currentRoute || '/')
            .split(/[?#]/)[0]
            .replace(/\/+$/, '')
            .toLowerCase();
        return normalized === '/settings'
            || normalized === '/settings/general'
            || normalized === '/settings/general-settings';
    }

    function installStyles() {
        if (document.getElementById('codex-vs-diagnostics-styles')) return;
        const style = document.createElement('style');
        style.id = 'codex-vs-diagnostics-styles';
        style.textContent = [
            '#codex-vs-diagnostics-setting { display:flex; flex-direction:column; }',
            '#codex-vs-diagnostics-setting .codex-vs-diagnostics-heading {',
            '  display:flex; min-height:var(--height-toolbar, 40px); align-items:center;',
            '  color:var(--color-token-text-primary, var(--vscode-foreground));',
            '  font-size:var(--text-base, 14px); font-weight:500;',
            '}',
            '#codex-vs-diagnostics-setting .codex-vs-diagnostics-group {',
            '  overflow:hidden; border:1px solid var(--color-token-border, var(--vscode-widget-border, rgba(127,127,127,.28)));',
            '  border-radius:8px; background:var(--color-background-panel, var(--color-token-bg-fog, var(--vscode-sideBar-background)));',
            '}',
            '#codex-vs-diagnostics-setting .codex-vs-diagnostics-row {',
            '  box-sizing:border-box; display:flex; width:100%; min-height:58px; align-items:center;',
            '  justify-content:space-between; gap:20px; border:0; padding:10px 12px;',
            '  color:inherit; background:transparent; cursor:pointer; font:inherit; text-align:start;',
            '}',
            '#codex-vs-diagnostics-setting .codex-vs-diagnostics-row:hover {',
            '  background:var(--color-token-bg-secondary, var(--vscode-list-hoverBackground, rgba(127,127,127,.12)));',
            '}',
            '#codex-vs-diagnostics-setting .codex-vs-diagnostics-row:focus-visible {',
            '  outline:2px solid var(--vscode-focusBorder, #007fd4); outline-offset:-2px;',
            '}',
            '#codex-vs-diagnostics-setting .codex-vs-diagnostics-copy { display:flex; min-width:0; flex:1; flex-direction:column; gap:3px; }',
            '#codex-vs-diagnostics-setting .codex-vs-diagnostics-label { color:var(--color-token-text-primary, var(--vscode-foreground)); font-size:13px; font-weight:500; }',
            '#codex-vs-diagnostics-setting .codex-vs-diagnostics-description { color:var(--color-token-text-tertiary, var(--vscode-descriptionForeground)); font-size:12px; line-height:1.4; }',
            '#codex-vs-diagnostics-setting .codex-vs-diagnostics-switch {',
            '  position:relative; width:36px; height:20px; flex:0 0 auto; border:1px solid var(--color-token-border, rgba(127,127,127,.4));',
            '  border-radius:999px; background:var(--color-token-bg-tertiary, rgba(127,127,127,.25)); transition:background-color 120ms ease;',
            '}',
            '#codex-vs-diagnostics-setting .codex-vs-diagnostics-thumb {',
            '  position:absolute; inset-block-start:2px; inset-inline-start:2px; width:14px; height:14px;',
            '  border-radius:50%; background:var(--color-token-text-secondary, var(--vscode-foreground)); transition:transform 120ms ease;',
            '}',
            '#codex-vs-diagnostics-setting .codex-vs-diagnostics-row[aria-checked="true"] .codex-vs-diagnostics-switch {',
            '  border-color:var(--vscode-button-background, #0e639c); background:var(--vscode-button-background, #0e639c);',
            '}',
            '#codex-vs-diagnostics-setting .codex-vs-diagnostics-row[aria-checked="true"] .codex-vs-diagnostics-thumb {',
            '  transform:translateX(16px); background:var(--vscode-button-foreground, #fff);',
            '}',
            '@media (prefers-reduced-motion:reduce) { #codex-vs-diagnostics-setting * { transition:none !important; } }'
        ].join('\n');
        (document.head || document.documentElement).appendChild(style);
    }

    function findComposerSection(labels) {
        const candidates = document.querySelectorAll('section div, section h1, section h2, section h3, section h4');
        for (const candidate of candidates) {
            if (!labels.includes((candidate.textContent || '').trim())) continue;
            const section = candidate.closest('section');
            if (section) return section;
        }
        return null;
    }

    function updateSwitch() {
        const row = document.querySelector('#' + settingId + ' .codex-vs-diagnostics-row');
        if (row) row.setAttribute('aria-checked', enabled ? 'true' : 'false');
    }

    function removeSetting() {
        document.getElementById(settingId)?.remove();
    }

    function mountSetting() {
        mountScheduled = false;
        if (!isGeneralSettingsRoute()) {
            removeSetting();
            return;
        }

        const existing = document.getElementById(settingId);
        if (existing) {
            updateSwitch();
            return;
        }

        const localized = strings();
        const anchor = findComposerSection(localized.composerHeadings);
        if (!anchor || !anchor.parentElement) return;

        installStyles();
        const section = document.createElement('section');
        section.id = settingId;

        const heading = document.createElement('div');
        heading.className = 'codex-vs-diagnostics-heading';
        heading.textContent = localized.group;

        const group = document.createElement('div');
        group.className = 'codex-vs-diagnostics-group';
        const row = document.createElement('button');
        row.type = 'button';
        row.className = 'codex-vs-diagnostics-row';
        row.setAttribute('role', 'switch');
        row.setAttribute('aria-label', localized.label);
        row.setAttribute('aria-checked', enabled ? 'true' : 'false');

        const copy = document.createElement('span');
        copy.className = 'codex-vs-diagnostics-copy';
        const label = document.createElement('span');
        label.className = 'codex-vs-diagnostics-label';
        label.textContent = localized.label;
        const description = document.createElement('span');
        description.className = 'codex-vs-diagnostics-description';
        description.textContent = localized.description;
        copy.append(label, description);

        const track = document.createElement('span');
        track.className = 'codex-vs-diagnostics-switch';
        track.setAttribute('aria-hidden', 'true');
        const thumb = document.createElement('span');
        thumb.className = 'codex-vs-diagnostics-thumb';
        track.appendChild(thumb);
        row.append(copy, track);
        row.addEventListener('click', function () {
            enabled = !enabled;
            updateSwitch();
            post({ type: 'diagnostic-settings-set', enabled: enabled });
        });

        group.appendChild(row);
        section.append(heading, group);
        anchor.insertAdjacentElement('afterend', section);
        post({ type: 'diagnostic-settings-request' });
    }

    function scheduleMount() {
        if (mountScheduled) return;
        mountScheduled = true;
        window.requestAnimationFrame(mountSetting);
    }

    function updateObserverForRoute() {
        if (!isGeneralSettingsRoute()) {
            if (observer) observer.disconnect();
            observer = null;
            mountScheduled = false;
            removeSetting();
            return;
        }

        if (!observer) {
            observer = new MutationObserver(scheduleMount);
            observer.observe(document.documentElement, { childList: true, subtree: true });
        }
        scheduleMount();
    }

    window.addEventListener('codex-host-message', function (event) {
        const message = event.detail;
        if (!message || typeof message !== 'object') return;
        if (message.type === 'navigate-to-route' && typeof message.path === 'string') {
            currentRoute = message.path;
            updateObserverForRoute();
        } else if (message.type === 'diagnostic-settings-state' || message.type === 'diagnostic-logging-changed') {
            enabled = message.enabled === true;
            updateSwitch();
        }
    });

    updateObserverForRoute();
    post({ type: 'diagnostic-settings-request' });
})();
