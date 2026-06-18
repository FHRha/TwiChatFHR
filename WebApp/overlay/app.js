// Identical to browser/app.js — overlay logic for WebSocket chat display
const chatContainer = document.getElementById('chat-container');
let lastUsername = null;
let lastMessageTime = 0;
let enableMessageGrouping = true;

function adjustLuminance(hex) {
    if (!hex) return '#FFFFFF';
    let r, g, b;
    if (hex.length === 7) {
        r = parseInt(hex.slice(1, 3), 16);
        g = parseInt(hex.slice(3, 5), 16);
        b = parseInt(hex.slice(5, 7), 16);
    } else return hex;
    let luma = 0.2126 * r + 0.7152 * g + 0.0722 * b;
    if (luma < 70) {
        let factor = 70 / Math.max(luma, 1);
        r = Math.min(255, Math.floor(r * factor + 50));
        g = Math.min(255, Math.floor(g * factor + 50));
        b = Math.min(255, Math.floor(b * factor + 50));
        return `rgb(${r}, ${g}, ${b})`;
    }
    return hex;
}

function addMessage(data) {
    const msgElement = document.createElement('div');
    msgElement.className = 'chat-message';
    if (data.Id) msgElement.dataset.id = data.Id;
    if (data.Username) msgElement.dataset.username = data.Username;
    if (data.Role && data.Role !== 'none') msgElement.classList.add(`role-${data.Role}`);
    if (data.IsBot) { msgElement.dataset.isBot = 'true'; msgElement.classList.add('role-bot'); }
    if (data.IsMention) msgElement.classList.add('mention-highlight');

    let lastMsg = chatContainer.lastElementChild;
    while (lastMsg && (lastMsg.classList.contains('msg-vanishing') || lastMsg.classList.contains('msg-deleted-crumble'))) {
        lastMsg = lastMsg.previousElementSibling;
    }
    if (enableMessageGrouping && lastMsg && lastMsg.dataset.username === data.Username) {
        msgElement.classList.add('grouped');
        lastMsg.classList.add('group-parent');
    }

    let badgesHtml = '';
    if (data.Badges && data.Badges.length > 0) {
        data.Badges.forEach(url => {
            const proxyUrl = `/cache/image?url=${encodeURIComponent(url)}`;
            badgesHtml += `<img class="chat-badge" src="${proxyUrl}" alt="badge" />`;
        });
    }

    const userElement = document.createElement('span');
    userElement.className = 'username';
    userElement.style.color = adjustLuminance(data.Color) || '#FFFFFF';
    let firstMsgHtml = data.IsFirstMessage ? '<span class="first-message-badge">👋 First Time</span>' : '';
    userElement.innerHTML = firstMsgHtml + badgesHtml + data.Username + ':';

    const textElement = document.createElement('span');
    textElement.className = 'chat-text';
    if (data.TextHtml) textElement.innerHTML = data.TextHtml;
    else textElement.innerText = data.Text;

    msgElement.appendChild(userElement);
    msgElement.appendChild(textElement);
    if (data.Effect && data.Effect !== '') textElement.classList.add('msg-effect-' + data.Effect);

    chatContainer.appendChild(msgElement);
    scheduleScroll();

    let childrenCount = chatContainer.children.length;
    if (childrenCount > 55) {
        let activeCount = 0, firstActive = null;
        for (let i = childrenCount - 1; i >= 0; i--) {
            let c = chatContainer.children[i];
            if (!c.classList.contains('msg-vanishing') && !c.classList.contains('msg-deleted-crumble')) {
                activeCount++;
                firstActive = c;
            }
        }
        if (activeCount > 50 && firstActive) {
            firstActive.classList.add('msg-vanishing');
            let removed = false;
            firstActive.addEventListener('animationend', () => { if (!removed) { removed = true; firstActive.remove(); } });
            setTimeout(() => { if (!removed && firstActive.parentNode) { removed = true; firstActive.remove(); } }, 500);
        }
    }
    if (childrenCount > 100) {
        let diff = childrenCount - 100;
        for (let i = 0; i < diff; i++) { if (chatContainer.firstElementChild) chatContainer.firstElementChild.remove(); }
    }
}

let scrollTimeout = null;
function scheduleScroll() {
    if (!scrollTimeout) {
        scrollTimeout = setTimeout(() => { window.scrollTo(0, document.body.scrollHeight); scrollTimeout = null; }, 16);
    }
}

function applyConfig(data) {
    const root = document.documentElement;
    const body = document.body;
    if (data.FontSize !== undefined) root.style.setProperty('--font-size', data.FontSize + 'px');
    if (data.Spacing !== undefined) root.style.setProperty('--msg-spacing', data.Spacing + 'px');
    if (data.Opacity !== undefined) root.style.setProperty('--glass-opacity', data.Opacity);
    if (data.TextColor !== undefined) root.style.setProperty('--text-color', data.TextColor);
    if (data.ColorBroadcaster !== undefined) root.style.setProperty('--color-broadcaster', data.ColorBroadcaster);
    if (data.ColorMod !== undefined) root.style.setProperty('--color-mod', data.ColorMod);
    if (data.ColorVip !== undefined) root.style.setProperty('--color-vip', data.ColorVip);
    if (data.MessageBgColor !== undefined) {
        let hex = data.MessageBgColor;
        if (hex.length === 7) {
            let r = parseInt(hex.slice(1,3),16), g = parseInt(hex.slice(3,5),16), b = parseInt(hex.slice(5,7),16);
            root.style.setProperty('--msg-bg-rgb', `${r}, ${g}, ${b}`);
        }
    }
    if (data.GlobalBgColor !== undefined) {
        let hex = data.GlobalBgColor;
        if (hex.length === 9) {
            let a = parseInt(hex.slice(1,3),16)/255, r = parseInt(hex.slice(3,5),16), g = parseInt(hex.slice(5,7),16), b = parseInt(hex.slice(7,9),16);
            root.style.setProperty('--global-bg', `rgba(${r}, ${g}, ${b}, ${a})`);
        }
    }
    if (data.Font !== undefined) {
        if (data.Font === 'couriernew') root.style.setProperty('--font-family', "'Courier New', Courier, monospace");
        else if (data.Font === 'comicsans') root.style.setProperty('--font-family', "'Comic Sans MS', cursive, sans-serif");
        else if (data.Font === 'roboto') root.style.setProperty('--font-family', "'Roboto', sans-serif");
        else if (data.Font === 'impact') root.style.setProperty('--font-family', "'Impact', sans-serif");
        else root.style.setProperty('--font-family', "'Outfit', -apple-system, BlinkMacSystemFont, sans-serif");
    }
    if (data.HideBackground !== undefined) body.classList.toggle('hide-background', data.HideBackground);
    if (data.HideBadges !== undefined) body.classList.toggle('hide-badges', data.HideBadges);
    if (data.HideBotMessages !== undefined) body.classList.toggle('hide-bot-messages', data.HideBotMessages);
    if (data.HideModMessages !== undefined) body.classList.toggle('hide-mod-messages', data.HideModMessages);
    if (data.HideVipMessages !== undefined) body.classList.toggle('hide-vip-messages', data.HideVipMessages);
    if (data.EnableRoleColors !== undefined) body.classList.toggle('disable-role-colors', !data.EnableRoleColors);
    if (data.TextOutline !== undefined) body.classList.toggle('text-outline', data.TextOutline);
    if (data.ShowStreamerEmotes !== undefined) body.classList.toggle('hide-channel-emotes', !data.ShowStreamerEmotes);
    if (data.ShowGlobalEmotes !== undefined) body.classList.toggle('hide-twitch-emotes', !data.ShowGlobalEmotes);
    if (data.ShowGlobal7TVEmotes !== undefined) body.classList.toggle('hide-global-7tv-emotes', !data.ShowGlobal7TVEmotes);
    if (data.EnableMessageGrouping !== undefined) enableMessageGrouping = data.EnableMessageGrouping;
    if (data.HighlightMentions !== undefined) body.classList.toggle('disable-mentions', !data.HighlightMentions);
    if (data.HighlightFirstMessage !== undefined) body.classList.toggle('disable-first-msg', !data.HighlightFirstMessage);
    const updatePrefixClass = (prefix, newValue) => {
        Array.from(body.classList).filter(c => c.startsWith(prefix)).forEach(c => body.classList.remove(c));
        if (newValue) body.classList.add(`${prefix}${newValue}`);
    };
    if (data.AnimationType !== undefined) updatePrefixClass('anim-', data.AnimationType);
    if (data.BorderStyle !== undefined) updatePrefixClass('border-', data.BorderStyle);
    if (data.DesignShape !== undefined) updatePrefixClass('shape-', data.DesignShape);
    if (data.DesignLayout !== undefined) updatePrefixClass('layout-', data.DesignLayout);
}

function connect() {
    const host = window.location.host;
    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    const ws = new WebSocket(`${protocol}//${host}/ws`);

    ws.onopen = () => console.log('Overlay: Connected to chat hub');

    ws.onmessage = (event) => {
        try {
            const data = JSON.parse(event.data);
            if (data.Type === 'BlacklistUpdate') {
                if (Array.isArray(data.Users)) {
                    const blacklisted = data.Users.map(u => u.toLowerCase());
                    document.querySelectorAll('.chat-message').forEach(msg => {
                        if (msg.dataset.username && blacklisted.includes(msg.dataset.username.toLowerCase())) {
                            msg.style.setProperty('display', 'none', 'important');
                            msg.classList.add('msg-blacklisted');
                        } else if (msg.classList.contains('msg-blacklisted')) {
                            msg.style.removeProperty('display');
                            msg.classList.remove('msg-blacklisted');
                        }
                    });
                }
                return;
            }
            if (data.Type === 'ConfigUpdate') { applyConfig(data); return; }
            if (data.Type === 'ClearMessage') {
                const msg = document.querySelector(`.chat-message[data-id="${data.Id}"]`);
                if (msg) {
                    msg.classList.add('msg-deleted-crumble');
                    let removed = false;
                    msg.addEventListener('animationend', () => { if (!removed) { removed = true; msg.remove(); } });
                    setTimeout(() => { if (!removed && msg.parentNode) { removed = true; msg.remove(); } }, 1000);
                }
                return;
            }
            if (data.Type === 'ClearChat') {
                if (data.Username) {
                    document.querySelectorAll(`.chat-message[data-username="${data.Username}"]`).forEach(el => {
                        el.classList.add('msg-deleted-crumble');
                        let removed = false;
                        el.addEventListener('animationend', () => { if (!removed) { removed = true; el.remove(); } });
                        setTimeout(() => { if (!removed && el.parentNode) { removed = true; el.remove(); } }, 1000);
                    });
                } else { chatContainer.innerHTML = ''; }
                return;
            }
            addMessage(data);
        } catch (e) { console.error('Failed to parse message', e); }
    };

    ws.onclose = () => { console.log('Overlay: Disconnected. Reconnecting in 2s...'); setTimeout(connect, 2000); };
    ws.onerror = (e) => console.error('Overlay WS error:', e);
}

connect();
