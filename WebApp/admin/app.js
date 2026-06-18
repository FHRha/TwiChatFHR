/* ═══════════════════════════════════════════════════════════════
   TwiChatFHR Admin Panel — app.js
   Handles: auth, config load/save, proxy management, presets,
            status polling, blacklist, live OBS URL, reconnect.
═══════════════════════════════════════════════════════════════ */

// ── State ──────────────────────────────────────────────────────────────────
let config = {};
let proxies = [];   // live proxy list (not in config directly)
let presets = [{ Name: '' }, { Name: '' }, { Name: '' }];
let saveDebounce = null;
let statusPoll = null;

// ── Bootstrap ──────────────────────────────────────────────────────────────
window.addEventListener('DOMContentLoaded', async () => {
    // Set OBS overlay URL in topbar
    document.getElementById('obs-url').value = `${location.origin}/overlay`;

    // Copy button
    document.getElementById('copy-btn').addEventListener('click', copyObs);

    // Logout
    document.getElementById('logout-btn').addEventListener('click', logout);

    // Auth submit
    document.getElementById('auth-submit').addEventListener('click', handleAuth);
    document.getElementById('auth-password').addEventListener('keydown', e => {
        if (e.key === 'Enter') handleAuth();
    });

    // Wire up all config inputs for auto-save
    wireInputs();

    // Check auth status → decide whether to show login or app
    const status = await fetch('/api/auth/status').then(r => r.json()).catch(() => null);
    if (!status) { showError('Сервер не отвечает'); return; }

    if (!status.configured) {
        // First run: switch to setup mode
        document.getElementById('auth-desc').textContent = 'Первый запуск. Задайте логин и пароль для панели.';
        document.getElementById('auth-submit').textContent = 'Создать аккаунт';
        document.getElementById('auth-submit').onclick = handleSetup;
        showAuth();
        return;
    }

    if (status.loggedIn) {
        await bootApp();
    } else {
        showAuth();
    }
});

// ── Auth ───────────────────────────────────────────────────────────────────
function showAuth() {
    document.getElementById('auth-overlay').style.display = 'flex';
    document.getElementById('app').style.display = 'none';
    setTimeout(() => document.getElementById('auth-password').focus(), 100);
}

async function handleAuth() {
    const username = document.getElementById('auth-username').value.trim();
    const password = document.getElementById('auth-password').value;
    const err = document.getElementById('auth-error');
    err.style.display = 'none';

    const res = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, password })
    });

    if (res.ok) {
        document.getElementById('auth-overlay').style.display = 'none';
        await bootApp();
    } else {
        err.textContent = 'Неверный логин или пароль';
        err.style.display = 'block';
        document.getElementById('auth-password').value = '';
        document.getElementById('auth-password').focus();
    }
}

async function handleSetup() {
    const username = document.getElementById('auth-username').value.trim() || 'admin';
    const password = document.getElementById('auth-password').value;
    const err = document.getElementById('auth-error');
    err.style.display = 'none';

    if (password.length < 4) {
        err.textContent = 'Пароль должен быть минимум 4 символа';
        err.style.display = 'block';
        return;
    }

    const res = await fetch('/api/auth/setup', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, password })
    });

    if (res.ok) {
        document.getElementById('auth-overlay').style.display = 'none';
        await bootApp();
    } else {
        const body = await res.json().catch(() => ({}));
        err.textContent = body.error || 'Ошибка создания аккаунта';
        err.style.display = 'block';
    }
}

async function logout() {
    await fetch('/api/auth/logout', { method: 'POST' });
    stopPolling();
    showAuth();
}

// ── App boot ───────────────────────────────────────────────────────────────
async function bootApp() {
    document.getElementById('app').style.display = 'flex';

    await loadConfig();
    await loadBlacklist();
    startPolling();
}

// ── Config ─────────────────────────────────────────────────────────────────
async function loadConfig() {
    const res = await fetch('/api/config');
    if (!res.ok) return;
    config = await res.json();

    // Preserve proxies & presets separately
    proxies = config.CloudProxies || [];
    presets = config.CustomPresets || [{ Name: '' }, { Name: '' }, { Name: '' }];

    populateUI();
}

function populateUI() {
    const c = config;

    // Theme & font
    setSelectByValue('cfg-theme', (c.DesignTheme || 'glass').toLowerCase());
    setSelectByValue('cfg-font', (c.Font || 'outfit').toLowerCase());

    // Appearance dropdowns
    setSelectByValue('cfg-border', (c.BorderStyle || 'glass').toLowerCase());
    setSelectByValue('cfg-shape', (c.DesignShape || 'round').toLowerCase());
    setSelectByValue('cfg-layout', (c.DesignLayout || 'inline').toLowerCase());
    setSelectByValue('cfg-anim', (c.AnimationType || 'pop').toLowerCase());

    // Sliders
    setSlider('cfg-spacing', c.MessageSpacing ?? 4, 'spacing-val', v => v + 'px');
    setSlider('cfg-opacity', Math.round((c.GlassOpacity ?? 0.45) * 100), 'opacity-val', v => v + '%');
    setSlider('cfg-fontsize', c.ChatFontSize ?? 14, 'fontsize-val', v => v + 'px');

    // Colors
    setColor('cfg-msgbg', c.MessageBgColor || '#141923');
    setColor('cfg-textcolor', c.CustomTextColor || '#FFFFFF');
    setColor('cfg-broadcaster', c.ColorBroadcaster || '#F59E0B');
    setColor('cfg-mod', c.ColorMod || '#10B981');
    setColor('cfg-vip', c.ColorVip || '#EC4899');
    // GlobalBgColor is ARGB #AARRGGBB → convert to #RRGGBB for color input
    const gbg = c.GlobalBgColor || '#00000000';
    setColor('cfg-globalbg', gbg.length === 9 ? '#' + gbg.slice(3) : gbg);

    // Checkboxes
    setCheck('cfg-grouping', c.EnableMessageGrouping ?? true);
    setCheck('cfg-mentions', c.HighlightMentions ?? false);
    setCheck('cfg-firstmsg', c.HighlightFirstMessage ?? true);
    setCheck('cfg-hidebg', c.HideBackground ?? false);
    setCheck('cfg-hidebadges', c.HideBadges ?? false);
    setCheck('cfg-hidebots', c.HideBotMessages ?? false);
    setCheck('cfg-hidemods', c.HideModMessages ?? false);
    setCheck('cfg-hidevips', c.HideVipMessages ?? false);
    setCheck('cfg-rolecolors', c.EnableRoleColors ?? true);
    setCheck('cfg-outline', c.TextOutline ?? true);
    setCheck('cfg-effects', c.EnableChatEffects ?? false);

    // Emotes
    setCheck('cfg-streamer-emotes', c.ShowStreamerEmotes ?? true);
    setCheck('cfg-global-emotes', c.ShowGlobalEmotes ?? true);
    setCheck('cfg-7tv-emotes', c.ShowGlobal7TVEmotes ?? false);
    setCheck('cfg-bttv-emotes', c.ShowBTTVEmotes ?? false);
    setCheck('cfg-ffz-emotes', c.ShowFFZEmotes ?? false);

    // Connection
    setInput('cfg-channel', c.TwitchChannel || '');
    setCheck('cfg-use-proxy', c.UseTwitchProxy ?? false);
    setCheck('cfg-strict-proxy', c.UseStrictTwitchProxy ?? false);
    setCheck('cfg-proxy-emotes', c.UseTwitchProxyForEmotes ?? false);
    toggleProxySection(c.UseTwitchProxy ?? false);

    setCheck('cfg-use-emote-proxy', c.UseCustomEmoteProxy ?? false);
    setCheck('cfg-strict-emote-proxy', c.UseStrictEmoteProxy ?? false);
    setInput('cfg-worker-url', c.CustomWorkerUrl || '');
    toggleEmoteProxySection(c.UseCustomEmoteProxy ?? false);

    // Preview channel label
    const ch = c.TwitchChannel || '';
    document.getElementById('preview-channel').textContent = ch ? `#${ch}` : 'канал не задан';

    renderProxies();
    renderPresets();
}

function collectConfig() {
    const theme = document.getElementById('cfg-theme').value;
    const font = document.getElementById('cfg-font').value;
    const border = document.getElementById('cfg-border').value;
    const shape = document.getElementById('cfg-shape').value;
    const layout = document.getElementById('cfg-layout').value;
    const anim = document.getElementById('cfg-anim').value;

    const spacing = parseInt(document.getElementById('cfg-spacing').value);
    const opacity = parseInt(document.getElementById('cfg-opacity').value) / 100.0;
    const fontsize = parseInt(document.getElementById('cfg-fontsize').value);

    const msgbg = document.getElementById('cfg-msgbg').value;
    const textcolor = document.getElementById('cfg-textcolor').value;
    const broadcaster = document.getElementById('cfg-broadcaster').value;
    const mod = document.getElementById('cfg-mod').value;
    const vip = document.getElementById('cfg-vip').value;
    // GlobalBgColor: store as #FFRRGGBB (fully opaque) from #RRGGBB color picker
    const gbg = document.getElementById('cfg-globalbg').value;
    const globalBgColor = '#FF' + gbg.slice(1).toUpperCase();

    return {
        ...config,
        DesignTheme: capitalizeFirst(theme),
        Font: capitalizeFirst(font),
        BorderStyle: capitalizeFirst(border),
        DesignShape: capitalizeFirst(shape),
        DesignLayout: capitalizeFirst(layout),
        AnimationType: capitalizeFirst(anim),
        MessageSpacing: spacing,
        GlassOpacity: opacity,
        ChatFontSize: fontsize,
        MessageBgColor: msgbg.toUpperCase(),
        CustomTextColor: textcolor.toUpperCase(),
        ColorBroadcaster: broadcaster.toUpperCase(),
        ColorMod: mod.toUpperCase(),
        ColorVip: vip.toUpperCase(),
        GlobalBgColor: globalBgColor,
        EnableMessageGrouping: getCheck('cfg-grouping'),
        HighlightMentions: getCheck('cfg-mentions'),
        HighlightFirstMessage: getCheck('cfg-firstmsg'),
        HideBackground: getCheck('cfg-hidebg'),
        HideBadges: getCheck('cfg-hidebadges'),
        HideBotMessages: getCheck('cfg-hidebots'),
        HideModMessages: getCheck('cfg-hidemods'),
        HideVipMessages: getCheck('cfg-hidevips'),
        EnableRoleColors: getCheck('cfg-rolecolors'),
        TextOutline: getCheck('cfg-outline'),
        EnableChatEffects: getCheck('cfg-effects'),
        ShowStreamerEmotes: getCheck('cfg-streamer-emotes'),
        ShowGlobalEmotes: getCheck('cfg-global-emotes'),
        ShowGlobal7TVEmotes: getCheck('cfg-7tv-emotes'),
        ShowBTTVEmotes: getCheck('cfg-bttv-emotes'),
        ShowFFZEmotes: getCheck('cfg-ffz-emotes'),
        TwitchChannel: document.getElementById('cfg-channel').value.trim().toLowerCase(),
        UseTwitchProxy: getCheck('cfg-use-proxy'),
        UseStrictTwitchProxy: getCheck('cfg-strict-proxy'),
        UseTwitchProxyForEmotes: getCheck('cfg-proxy-emotes'),
        UseCustomEmoteProxy: getCheck('cfg-use-emote-proxy'),
        UseStrictEmoteProxy: getCheck('cfg-strict-emote-proxy'),
        CustomWorkerUrl: document.getElementById('cfg-worker-url').value.trim(),
        CloudProxies: proxies,
        CustomPresets: presets,
    };
}

function scheduleSave() {
    setSaveStatus('saving', 'Сохранение...');
    clearTimeout(saveDebounce);
    saveDebounce = setTimeout(saveNow, 600);
}

async function saveNow() {
    clearTimeout(saveDebounce);
    const body = collectConfig();
    // Update preview channel label
    const ch = body.TwitchChannel || '';
    document.getElementById('preview-channel').textContent = ch ? `#${ch}` : 'канал не задан';

    try {
        const res = await fetch('/api/config', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (res.ok) {
            config = body;
            setSaveStatus('saved', '✓ Сохранено');
            setTimeout(() => setSaveStatus('', 'Изменения сохраняются автоматически'), 2000);
        } else {
            setSaveStatus('error', '✗ Ошибка сохранения');
        }
    } catch {
        setSaveStatus('error', '✗ Ошибка соединения');
    }
}

function setSaveStatus(cls, text) {
    const el = document.getElementById('save-status');
    el.className = 'save-status ' + cls;
    el.textContent = text;
}

// ── Wire all inputs ─────────────────────────────────────────────────────────
function wireInputs() {
    const ids = [
        'cfg-theme', 'cfg-font', 'cfg-border', 'cfg-shape', 'cfg-layout', 'cfg-anim',
        'cfg-spacing', 'cfg-opacity', 'cfg-fontsize',
        'cfg-msgbg', 'cfg-textcolor', 'cfg-broadcaster', 'cfg-mod', 'cfg-vip', 'cfg-globalbg',
        'cfg-grouping', 'cfg-mentions', 'cfg-firstmsg', 'cfg-hidebg', 'cfg-hidebadges',
        'cfg-hidebots', 'cfg-hidemods', 'cfg-hidevips', 'cfg-rolecolors', 'cfg-outline', 'cfg-effects',
        'cfg-streamer-emotes', 'cfg-global-emotes', 'cfg-7tv-emotes', 'cfg-bttv-emotes', 'cfg-ffz-emotes',
        'cfg-channel', 'cfg-use-proxy', 'cfg-strict-proxy', 'cfg-proxy-emotes',
        'cfg-use-emote-proxy', 'cfg-strict-emote-proxy', 'cfg-worker-url',
    ];

    ids.forEach(id => {
        const el = document.getElementById(id);
        if (!el) return;
        const evt = el.type === 'range' || el.type === 'color' ? 'input' : 'change';
        el.addEventListener(evt, () => {
            onInputChange(id, el);
        });
    });

    // Slider live labels
    document.getElementById('cfg-spacing').addEventListener('input', e => {
        document.getElementById('spacing-val').textContent = e.target.value + 'px';
    });
    document.getElementById('cfg-opacity').addEventListener('input', e => {
        document.getElementById('opacity-val').textContent = e.target.value + '%';
    });
    document.getElementById('cfg-fontsize').addEventListener('input', e => {
        document.getElementById('fontsize-val').textContent = e.target.value + 'px';
    });
}

function onInputChange(id, el) {
    // Toggle proxy/emote-proxy panels
    if (id === 'cfg-use-proxy') toggleProxySection(el.checked);
    if (id === 'cfg-use-emote-proxy') toggleEmoteProxySection(el.checked);
    scheduleSave();
}

function toggleProxySection(show) {
    document.getElementById('proxy-section').style.display = show ? 'flex' : 'none';
    if (show) document.getElementById('proxy-section').style.flexDirection = 'column';
}
function toggleEmoteProxySection(show) {
    document.getElementById('emote-proxy-section').style.display = show ? 'flex' : 'none';
    if (show) document.getElementById('emote-proxy-section').style.flexDirection = 'column';
}

// ── Proxies ─────────────────────────────────────────────────────────────────
function renderProxies() {
    const list = document.getElementById('proxies-list');
    list.innerHTML = '';
    proxies.forEach((p, i) => {
        const card = document.createElement('div');
        card.className = 'proxy-card';
        card.innerHTML = `
            <div class="proxy-card-header">
                <span class="proxy-name">${escHtml(p.Name || `Прокси ${i+1}`)}</span>
                <div style="display:flex;align-items:center;gap:8px;">
                    <span class="proxy-status" id="proxy-status-${i}" style="color:${p.StatusColor||'var(--text-4)'};">${p.StatusText||''}</span>
                    <button class="btn-remove" onclick="removeProxy(${i})">✕</button>
                </div>
            </div>
            <div class="gap-6">
                <input type="text" placeholder="Имя прокси" value="${escHtml(p.Name||'')}" oninput="proxies[${i}].Name=this.value;scheduleSave();" />
                <input type="text" placeholder="wss://your-proxy.hf.space" value="${escHtml(p.Url||'')}" oninput="proxies[${i}].Url=this.value;scheduleSave();" />
                <input type="text" placeholder="X-Proxy-Token" value="${escHtml(p.Token||'')}" oninput="proxies[${i}].Token=this.value;scheduleSave();" />
            </div>
        `;
        list.appendChild(card);
    });
}

function addProxy() {
    proxies.push({ Name: `Прокси ${proxies.length + 1}`, Url: '', Token: '', IsEnabled: true, StatusText: '', StatusColor: '' });
    renderProxies();
    scheduleSave();
}

function removeProxy(i) {
    proxies.splice(i, 1);
    renderProxies();
    scheduleSave();
}

// ── Presets ─────────────────────────────────────────────────────────────────
function renderPresets() {
    const list = document.getElementById('preset-list');
    list.innerHTML = '';
    presets.forEach((p, i) => {
        const row = document.createElement('div');
        row.className = 'preset-slot';
        row.innerHTML = `
            <input type="text" placeholder="Пресет ${i+1}" value="${escHtml(p.Name||'')}" oninput="presets[${i}].Name=this.value;" />
            <button class="btn-sm" title="Сохранить" onclick="savePreset(${i})">💾</button>
            <button class="btn-sm" title="Загрузить" onclick="loadPreset(${i})">📂</button>
            <button class="btn-sm" title="Экспорт JSON" onclick="exportPreset(${i})">⬇</button>
        `;
        list.appendChild(row);
    });
}

function savePreset(i) {
    const c = collectConfig();
    presets[i] = {
        IsSaved: true,
        Name: presets[i].Name || `Пресет ${i+1}`,
        Font: c.Font,
        ChatFontSize: c.ChatFontSize,
        GlassOpacity: c.GlassOpacity,
        MessageSpacing: c.MessageSpacing,
        HideBackground: c.HideBackground,
        HideBadges: c.HideBadges,
        TextOutline: c.TextOutline,
        EnableRoleColors: c.EnableRoleColors,
        AnimationType: c.AnimationType,
        EnableMessageGrouping: c.EnableMessageGrouping,
        DesignShape: c.DesignShape,
        BorderStyle: c.BorderStyle,
        DesignLayout: c.DesignLayout,
        MessageBgColor: c.MessageBgColor,
        GlobalBgColor: c.GlobalBgColor,
        CustomTextColor: c.CustomTextColor,
        ColorBroadcaster: c.ColorBroadcaster,
        ColorMod: c.ColorMod,
        ColorVip: c.ColorVip,
    };
    scheduleSave();
    flashBtn(event.target);
}

function loadPreset(i) {
    const p = presets[i];
    if (!p || !p.IsSaved) return;
    // Apply preset values to config
    config.Font = p.Font;
    config.ChatFontSize = p.ChatFontSize;
    config.GlassOpacity = p.GlassOpacity;
    config.MessageSpacing = p.MessageSpacing;
    config.HideBackground = p.HideBackground;
    config.HideBadges = p.HideBadges;
    config.TextOutline = p.TextOutline;
    config.EnableRoleColors = p.EnableRoleColors;
    config.AnimationType = p.AnimationType;
    config.EnableMessageGrouping = p.EnableMessageGrouping;
    config.DesignShape = p.DesignShape;
    config.BorderStyle = p.BorderStyle;
    config.DesignLayout = p.DesignLayout;
    config.MessageBgColor = p.MessageBgColor;
    config.GlobalBgColor = p.GlobalBgColor;
    config.CustomTextColor = p.CustomTextColor;
    config.ColorBroadcaster = p.ColorBroadcaster;
    config.ColorMod = p.ColorMod;
    config.ColorVip = p.ColorVip;
    config.DesignTheme = 'Custom';
    populateUI();
    scheduleSave();
    flashBtn(event.target);
}

function exportPreset(i) {
    const p = presets[i];
    const json = JSON.stringify(p, null, 2);
    const blob = new Blob([json], { type: 'application/json' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = `twichat-preset-${i+1}.json`;
    a.click();
}

function flashBtn(btn) {
    btn.classList.add('success');
    setTimeout(() => btn.classList.remove('success'), 1200);
}

// ── Blacklist ───────────────────────────────────────────────────────────────
async function loadBlacklist() {
    const res = await fetch('/api/blacklist');
    if (res.ok) {
        document.getElementById('blacklist-text').value = await res.text();
    }
}

async function saveBlacklist() {
    const content = document.getElementById('blacklist-text').value;
    await fetch('/api/blacklist', { method: 'POST', body: content });
}

function toggleBlacklist() {
    const s = document.getElementById('blacklist-section');
    const open = s.classList.toggle('open');
    document.getElementById('blacklist-btn').textContent = open ? '✕ Закрыть блэклист' : '✏️ Редактировать блэклист';
}

// ── Reconnect ───────────────────────────────────────────────────────────────
async function reconnect() {
    const btn = document.getElementById('reconnect-btn');
    btn.textContent = '⏳ Переподключение...';
    btn.disabled = true;
    await fetch('/api/reconnect', { method: 'POST' }).catch(() => {});
    setTimeout(() => { btn.textContent = '🔄 Переподключиться к Twitch'; btn.disabled = false; }, 2000);
}

// ── Copy OBS URL ────────────────────────────────────────────────────────────
async function copyObs() {
    const url = document.getElementById('obs-url').value;
    try {
        await navigator.clipboard.writeText(url);
        const btn = document.getElementById('copy-btn');
        btn.classList.add('copied');
        btn.title = 'Скопировано!';
        setTimeout(() => { btn.classList.remove('copied'); btn.title = 'Скопировать ссылку'; }, 2000);
    } catch {
        document.getElementById('obs-url').select();
        document.execCommand('copy');
    }
}

// ── Status polling ──────────────────────────────────────────────────────────
function startPolling() {
    pollStatus();
    statusPoll = setInterval(pollStatus, 5000);
}

function stopPolling() {
    clearInterval(statusPoll);
}

async function pollStatus() {
    const res = await fetch('/api/status').catch(() => null);
    if (!res || !res.ok) {
        setStatusUI(false, 'Нет соединения');
        return;
    }
    const data = await res.json();
    const label = data.connected
        ? (data.activeProxy ? `Через прокси: ${data.activeProxy}` : `#${data.channel}`)
        : 'Не подключён';
    setStatusUI(data.connected, label);
}

function setStatusUI(connected, text) {
    document.getElementById('status-dot').className = 'status-dot ' + (connected ? 'connected' : 'error');
    document.getElementById('status-text').textContent = text;
}

// ── Helpers ─────────────────────────────────────────────────────────────────
function setSelectByValue(id, value) {
    const el = document.getElementById(id);
    if (!el) return;
    for (const opt of el.options) {
        if (opt.value.toLowerCase() === value.toLowerCase()) { el.value = opt.value; return; }
    }
}
function setSlider(id, value, valId, fmt) {
    const el = document.getElementById(id);
    if (el) el.value = value;
    const lbl = document.getElementById(valId);
    if (lbl) lbl.textContent = fmt(value);
}
function setCheck(id, val) {
    const el = document.getElementById(id);
    if (el) el.checked = !!val;
}
function getCheck(id) {
    const el = document.getElementById(id);
    return el ? el.checked : false;
}
function setInput(id, val) {
    const el = document.getElementById(id);
    if (el) el.value = val;
}
function setColor(id, hex) {
    const el = document.getElementById(id);
    if (!el) return;
    // Ensure 7-char hex
    if (hex && hex.length === 7) el.value = hex;
    else if (hex && hex.length > 7) el.value = '#' + hex.slice(-6);
}
function capitalizeFirst(s) {
    if (!s) return s;
    return s.charAt(0).toUpperCase() + s.slice(1).toLowerCase();
}
function escHtml(str) {
    return String(str).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
}
function showError(msg) {
    console.error(msg);
}
