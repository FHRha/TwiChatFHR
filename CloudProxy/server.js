const WebSocket = require('ws');
const http = require('http');

const PORT = process.env.PORT || 8080;
const PROXY_TOKEN = process.env.PROXY_TOKEN;
const TWITCH_WS_URL = 'wss://irc-ws.chat.twitch.tv:443';

if (!PROXY_TOKEN) {
    console.warn('WARNING: PROXY_TOKEN is not set. The proxy will reject all connections.');
}

const server = http.createServer((req, res) => {
    // Health check endpoint for Cloud Run
    if (req.url === '/') {
        res.writeHead(200, { 'Content-Type': 'text/plain' });
        res.end('TwiChatFHR Proxy is running.');
        return;
    }
    
    res.writeHead(404, { 'Content-Type': 'text/plain' });
    res.end('Not Found');
});

const wss = new WebSocket.Server({ server });

wss.on('connection', (clientWs, req) => {
    const token = req.headers['x-proxy-token'];

    if (!PROXY_TOKEN || token !== PROXY_TOKEN) {
        console.log(`[${new Date().toISOString()}] Rejected connection: Invalid or missing token.`);
        clientWs.close(4001, 'Unauthorized');
        return;
    }

    console.log(`[${new Date().toISOString()}] Client connected. Proxying to Twitch...`);

    const twitchWs = new WebSocket(TWITCH_WS_URL);

    // Relay messages from client to Twitch
    clientWs.on('message', (message) => {
        if (twitchWs.readyState === WebSocket.OPEN) {
            twitchWs.send(message);
        }
    });

    // Relay messages from Twitch to client
    twitchWs.on('message', (message) => {
        if (clientWs.readyState === WebSocket.OPEN) {
            clientWs.send(message);
        }
    });

    // Handle connection closures
    clientWs.on('close', () => {
        console.log(`[${new Date().toISOString()}] Client disconnected.`);
        if (twitchWs.readyState === WebSocket.OPEN) {
            twitchWs.close();
        }
    });

    twitchWs.on('close', () => {
        console.log(`[${new Date().toISOString()}] Twitch closed connection.`);
        if (clientWs.readyState === WebSocket.OPEN) {
            clientWs.close();
        }
    });

    // Handle errors
    clientWs.on('error', (err) => {
        console.error(`[${new Date().toISOString()}] Client WS error:`, err);
    });

    twitchWs.on('error', (err) => {
        console.error(`[${new Date().toISOString()}] Twitch WS error:`, err);
    });
});

server.listen(PORT, () => {
    console.log(`TwiChatFHR Proxy listening on port ${PORT}`);
    
    // Auto-detect Hugging Face Spaces URL
    const hfHost = process.env.SPACE_HOST;
    if (hfHost) {
        console.log(`\n======================================================`);
        console.log(`🚀 ПРОКСИ УСПЕШНО ЗАПУЩЕН НА HUGGING FACE!`);
        console.log(`🔗 СКОПИРУЙТЕ ЭТОТ URL В ПРИЛОЖЕНИЕ:`);
        console.log(`   wss://${hfHost}`);
        console.log(`======================================================\n`);
    }
});
