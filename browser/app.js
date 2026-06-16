const chatContainer = document.getElementById('chat-container');

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
    if (data.Role && data.Role !== 'none') {
        msgElement.classList.add(`role-${data.Role}`);
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
    userElement.innerHTML = badgesHtml + data.Username + ':';
    
    const textElement = document.createElement('span');
    textElement.className = 'chat-text';
    if (data.TextHtml) {
        textElement.innerHTML = data.TextHtml;
    } else {
        textElement.innerText = data.Text;
    }
    
    msgElement.appendChild(userElement);
    msgElement.appendChild(textElement);
    
    chatContainer.appendChild(msgElement);
    
    window.scrollTo(0, document.body.scrollHeight);
    
    if (chatContainer.children.length > 50) {
        chatContainer.removeChild(chatContainer.firstChild);
    }
}

function connect() {
    // Connect to the local WebSocket server
    const host = window.location.host;
    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    const ws = new WebSocket(`${protocol}//${host}/ws`);

    ws.onopen = () => {
        console.log('Connected to local chat hub');
        addMessage({
            Username: 'System',
            Text: 'Connected to local server',
            Color: '#00FF00'
        });
    };

    ws.onmessage = (event) => {
        try {
            const data = JSON.parse(event.data);
            if (data.Type === 'ConfigUpdate') {
                const root = document.documentElement;
                const body = document.body;
                if (data.FontSize !== undefined) root.style.setProperty('--font-size', data.FontSize + 'px');
                if (data.Spacing !== undefined) root.style.setProperty('--msg-spacing', data.Spacing + 'px');
                if (data.Opacity !== undefined) root.style.setProperty('--glass-opacity', data.Opacity);
                if (data.TextColor !== undefined) root.style.setProperty('--text-color', data.TextColor);
                if (data.ColorBroadcaster !== undefined) root.style.setProperty('--color-broadcaster', data.ColorBroadcaster);
                if (data.ColorMod !== undefined) root.style.setProperty('--color-mod', data.ColorMod);
                if (data.ColorVip !== undefined) root.style.setProperty('--color-vip', data.ColorVip);
                
                if (data.HideBackground !== undefined) body.classList.toggle('hide-background', data.HideBackground);
                if (data.HideBadges !== undefined) body.classList.toggle('hide-badges', data.HideBadges);
                if (data.EnableRoleColors !== undefined) body.classList.toggle('disable-role-colors', !data.EnableRoleColors);
                if (data.TextOutline !== undefined) body.classList.toggle('text-outline', data.TextOutline);
                
                if (data.ShowStreamerEmotes !== undefined) {
                    body.classList.toggle('hide-channel-emotes', !data.ShowStreamerEmotes);
                }
                if (data.ShowGlobalEmotes !== undefined) {
                    body.classList.toggle('hide-twitch-emotes', !data.ShowGlobalEmotes);
                }
                if (data.ShowGlobal7TVEmotes !== undefined) {
                    body.classList.toggle('hide-global-7tv-emotes', !data.ShowGlobal7TVEmotes);
                }
            } else if (data.Type === 'ClearMessage') {
                const msg = document.querySelector(`.chat-message[data-id="${data.Id}"]`);
                if (msg) msg.remove();
            } else if (data.Type === 'ClearChat') {
                if (data.Username) {
                    document.querySelectorAll(`.chat-message[data-username="${data.Username}"]`).forEach(el => el.remove());
                } else {
                    chatContainer.innerHTML = '';
                }
            } else {
                addMessage(data);
            }
        } catch (e) {
            console.error('Failed to parse message', e);
        }
    };

    ws.onclose = () => {
        console.log('Disconnected. Reconnecting in 2s...');
        setTimeout(connect, 2000);
    };
}

connect();
