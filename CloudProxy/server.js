const WebSocket = require('ws');
const http = require('http');
const https = require('https');

const PORT = process.env.PORT || 8080;
const PROXY_TOKEN = process.env.PROXY_TOKEN;
const TWITCH_WS_URL = 'wss://irc-ws.chat.twitch.tv:443';

if (!PROXY_TOKEN) {
    console.warn('WARNING: PROXY_TOKEN is not set. The proxy will reject all connections.');
}

// ---------------------------------------------------------------------------
// Shared Twitch IRC connection — one per proxy instance.
// All authenticated browser clients receive messages from this single upstream.
// ---------------------------------------------------------------------------

let twitchWs = null;
let twitchConnecting = false;

// Clients waiting for twitchWs to open so we can flush their queued messages.
// Map<clientWs, string[]>
const pendingQueues = new Map();

// Set of currently connected & authenticated browser clients.
const clients = new Set();

// Messages buffered while twitchWs is not yet open.
// These come from *any* client and need to be sent upstream once connected.
const upstreamQueue = [];

function connectToTwitch() {
    if (twitchWs && (twitchWs.readyState === WebSocket.OPEN || twitchWs.readyState === WebSocket.CONNECTING)) {
        return; // Already connected or connecting
    }
    if (twitchConnecting) return;

    twitchConnecting = true;
    console.log(`[${new Date().toISOString()}] Connecting to Twitch IRC...`);
    const ws = new WebSocket(TWITCH_WS_URL);

    ws.on('open', () => {
        twitchWs = ws;
        twitchConnecting = false;
        console.log(`[${new Date().toISOString()}] Connected to Twitch IRC. Flushing ${upstreamQueue.length} queued message(s).`);

        // Flush messages queued before connection was ready
        while (upstreamQueue.length > 0) {
            const { message, isBinary } = upstreamQueue.shift();
            ws.send(message, { binary: isBinary });
        }
    });

    ws.on('message', (message, isBinary) => {
        // Log and broadcast to all connected browser clients
        if (!isBinary) {
            const msgStr = message.toString('utf8');
            const lines = msgStr.split('\r\n').filter(l => l.trim().length > 0);
            lines.forEach(line => {
                console.log(`[${new Date().toISOString()}] [Twitch -> Client] ${line}`);
            });
        }

        for (const clientWs of clients) {
            if (clientWs.readyState === WebSocket.OPEN) {
                clientWs.send(message, { binary: isBinary });
            }
        }
    });

    ws.on('close', (code, reason) => {
        console.log(`[${new Date().toISOString()}] Twitch IRC closed (code=${code}). Reconnecting in 5s...`);
        twitchWs = null;
        twitchConnecting = false;
        setTimeout(connectToTwitch, 5000);
    });

    ws.on('error', (err) => {
        console.error(`[${new Date().toISOString()}] Twitch WS error:`, err.message);
        twitchConnecting = false;
        ws.terminate();
    });
}

// Establish the upstream connection eagerly on startup
connectToTwitch();

// ---------------------------------------------------------------------------
// HTTP server (health-check + HTTP proxy for emotes)
// ---------------------------------------------------------------------------

const server = http.createServer((req, res) => {
    // Health check endpoint
    if (req.url === '/') {
        res.writeHead(200, { 'Content-Type': 'text/plain' });
        res.end('TwiChatFHR Proxy is running.');
        return;
    }

    // HTTP Proxy for Emotes
    if (req.url.startsWith('/proxy')) {
        try {
            const parsedUrl = new URL(req.url, `http://${req.headers.host}`);
            const token = parsedUrl.searchParams.get('token');
            const targetUrlStr = parsedUrl.searchParams.get('url');

            if (!PROXY_TOKEN || token !== PROXY_TOKEN) {
                console.log(`[${new Date().toISOString()}] HTTP Proxy rejected: Unauthorized. Target: ${targetUrlStr}`);
                res.writeHead(401, { 'Content-Type': 'text/plain' });
                res.end('Unauthorized');
                return;
            }

            if (!targetUrlStr) {
                console.log(`[${new Date().toISOString()}] HTTP Proxy rejected: Missing url parameter.`);
                res.writeHead(400, { 'Content-Type': 'text/plain' });
                res.end('Missing url parameter');
                return;
            }

            // Detect if this is a CDN image request (emote images vs JSON API)
            const isImageRequest = /\.(webp|gif|png|jpg|jpeg|avif)(\?|$)/i.test(targetUrlStr) 
                || targetUrlStr.includes('cdn.7tv.app')
                || targetUrlStr.includes('cdn.betterttv.net')
                || targetUrlStr.includes('cdn.frankerfacez.com')
                || targetUrlStr.includes('static-cdn.jtvnw.net');

            // Build headers that closely mimic a real Chrome browser request.
            // HTTP/2 (used by fetch/undici) + these headers bypass Cloudflare datacenter checks.
            const fetchHeaders = {
                'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36',
                'Accept': isImageRequest
                    ? 'image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8'
                    : 'application/json, text/plain, */*',
                'Accept-Language': 'en-US,en;q=0.9,ru;q=0.8',
                'Accept-Encoding': 'gzip, deflate, br',
                'Cache-Control': 'no-cache',
                'Pragma': 'no-cache',
                'Sec-Fetch-Dest': isImageRequest ? 'image' : 'empty',
                'Sec-Fetch-Mode': isImageRequest ? 'no-cors' : 'cors',
                'Sec-Fetch-Site': 'cross-site',
                'Sec-Ch-Ua': '"Google Chrome";v="125", "Chromium";v="125", "Not.A/Brand";v="24"',
                'Sec-Ch-Ua-Mobile': '?0',
                'Sec-Ch-Ua-Platform': '"Windows"',
                'Origin': 'https://7tv.app',
                'Referer': 'https://7tv.app/',
            };

            console.log(`[${new Date().toISOString()}] HTTP Proxying ${isImageRequest ? 'image' : 'API'} request to: ${targetUrlStr}`);

            (async () => {
                try {
                    const controller = new AbortController();
                    const timeoutId = setTimeout(() => controller.abort(), 15000);

                    const response = await fetch(targetUrlStr, {
                        method: 'GET',
                        headers: fetchHeaders,
                        signal: controller.signal,
                        redirect: 'follow',
                    });
                    clearTimeout(timeoutId);

                    console.log(`[${new Date().toISOString()}] HTTP Proxy received ${response.status} from ${targetUrlStr}`);

                    if (!res.headersSent) {
                        // Forward safe response headers
                        const forwardHeaders = {
                            'Access-Control-Allow-Origin': '*',
                            'Cache-Control': 'public, max-age=86400',
                        };
                        const contentType = response.headers.get('content-type');
                        if (contentType) forwardHeaders['Content-Type'] = contentType;
                        const contentLength = response.headers.get('content-length');
                        if (contentLength) forwardHeaders['Content-Length'] = contentLength;

                        res.writeHead(response.status, forwardHeaders);
                    }

                    // Stream body to client
                    const buffer = await response.arrayBuffer();
                    res.end(Buffer.from(buffer));
                } catch (err) {
                    console.error(`[${new Date().toISOString()}] HTTP Proxy fetch error for ${targetUrlStr}:`, err.message);
                    if (!res.headersSent) {
                        res.writeHead(502, { 'Content-Type': 'text/plain' });
                        res.end('Bad Gateway');
                    } else {
                        res.end();
                    }
                }
            })();
        } catch (e) {
            console.log(`[${new Date().toISOString()}] HTTP Proxy rejected: Invalid Request. Error: ${e.message}`);
            res.writeHead(400, { 'Content-Type': 'text/plain' });
            res.end('Invalid Request');
        }
        return;
    }


    res.writeHead(404, { 'Content-Type': 'text/plain' });
    res.end('Not Found');
});

// ---------------------------------------------------------------------------
// WebSocket server — browser clients connect here
// ---------------------------------------------------------------------------

const wss = new WebSocket.Server({ server });

wss.on('connection', (clientWs, req) => {
    const token = req.headers['x-proxy-token'];

    if (!PROXY_TOKEN || token !== PROXY_TOKEN) {
        console.log(`[${new Date().toISOString()}] Rejected connection: Invalid or missing token.`);
        clientWs.close(4001, 'Unauthorized');
        return;
    }

    console.log(`[${new Date().toISOString()}] Client connected. Total clients: ${clients.size + 1}`);
    clients.add(clientWs);

    // Make sure the upstream Twitch connection is alive
    connectToTwitch();

    // Relay messages from this browser client upstream to Twitch
    clientWs.on('message', (message, isBinary) => {
        if (!isBinary) {
            const msgStr = message.toString('utf8');
            const lines = msgStr.split('\r\n').filter(l => l.trim().length > 0);
            lines.forEach(line => {
                let logMsg = line;
                if (line.startsWith('PASS')) logMsg = 'PASS ***';
                console.log(`[${new Date().toISOString()}] [Client -> Twitch] ${logMsg}`);
            });
        }

        if (twitchWs && twitchWs.readyState === WebSocket.OPEN) {
            twitchWs.send(message, { binary: isBinary });
        } else {
            // Buffer until upstream reconnects
            upstreamQueue.push({ message, isBinary });
        }
    });

    clientWs.on('close', () => {
        clients.delete(clientWs);
        console.log(`[${new Date().toISOString()}] Client disconnected. Remaining clients: ${clients.size}`);
    });

    clientWs.on('error', (err) => {
        console.error(`[${new Date().toISOString()}] Client WS error:`, err.message);
        clients.delete(clientWs);
    });
});

server.listen(PORT, () => {
    console.log(`TwiChatFHR Proxy listening on port ${PORT}`);

    // Fetch and display the commit version of server.js
    https.get('https://api.github.com/repos/FHRha/TwiChatFHR/commits?path=CloudProxy/server.js&per_page=1', {
        headers: { 'User-Agent': 'TwiChatFHR-Proxy' }
    }, (res) => {
        let data = '';
        res.on('data', chunk => data += chunk);
        res.on('end', () => {
            try {
                const commits = JSON.parse(data);
                if (commits && commits.length > 0) {
                    const commit = commits[0];
                    console.log(`[Version] Running latest server.js from commit:`);
                    console.log(`[Version] "${commit.commit.message}" (${commit.sha.substring(0, 7)})`);
                    console.log(`[Version] Date: ${commit.commit.author.date}`);
                }
            } catch (e) {
                // ignore
            }
        });
    }).on('error', () => {});

    // Auto-detect Hugging Face Spaces URL
    const hfHost = process.env.SPACE_HOST;
    const spaceId = process.env.SPACE_ID;

    let url = "";
    if (hfHost) {
        url = `wss://${hfHost}`;
    } else if (spaceId) {
        url = `wss://${spaceId.replace('/', '-').toLowerCase()}.hf.space`;
    }

    if (url) {
        console.log(`\n======================================================`);
        console.log(`🚀 ПРОКСИ УСПЕШНО ЗАПУЩЕН НА HUGGING FACE!`);
        console.log(`🔗 СКОПИРУЙТЕ ЭТОТ URL В ПРИЛОЖЕНИЕ:`);
        console.log(`   ${url}`);
        console.log(`======================================================\n`);
    }
});
