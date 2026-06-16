const chatContainer = document.getElementById('chat-container');

function addMessage(data) {
    const msgElement = document.createElement('div');
    msgElement.className = 'chat-message';
    
    let badgesHtml = '';
    if (data.Badges && data.Badges.length > 0) {
        data.Badges.forEach(url => {
            const proxyUrl = `/cache/badge?url=${encodeURIComponent(url)}`;
            badgesHtml += `<img class="chat-badge" src="${proxyUrl}" alt="badge" />`;
        });
    }

    const userElement = document.createElement('span');
    userElement.className = 'username';
    userElement.style.color = data.Color || '#FFFFFF';
    userElement.innerHTML = badgesHtml + data.Username + ': ';
    
    const textElement = document.createElement('span');
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
            addMessage(data);
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
