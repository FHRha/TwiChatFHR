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
// HTTP server (health-check + HTTP proxy for emotes)
// ---------------------------------------------------------------------------

const server = http.createServer((req, res) => {
    // Health check endpoint
    if (req.url === '/') {
        res.writeHead(200, { 'Content-Type': 'text/plain' });
        res.end('TwiChatFHR Proxy is running.');
        return;
    }

    // HTTP Proxy for Emotes / API
    if (req.url.startsWith('/proxy')) {
        try {
            const parsedUrl = new URL(req.url, `http://${req.headers.host}`);
            const token = parsedUrl.searchParams.get('token');
            const targetUrlStr = parsedUrl.searchParams.get('url');

            if (!PROXY_TOKEN || token !== PROXY_TOKEN) {
                console.log(`[${new Date().toISOString()}] HTTP Proxy rejected: Unauthorized.`);
                res.writeHead(401, { 'Content-Type': 'text/plain' });
                res.end('Unauthorized');
                return;
            }

            if (!targetUrlStr) {
                res.writeHead(400, { 'Content-Type': 'text/plain' });
                res.end('Missing url parameter');
                return;
            }

            // Detect request type: CDN images vs JSON API
            const isImageRequest = /\.(webp|gif|png|jpg|jpeg|avif)(\?|$)/i.test(targetUrlStr)
                || targetUrlStr.includes('cdn.7tv.app')
                || targetUrlStr.includes('cdn.betterttv.net')
                || targetUrlStr.includes('cdn.frankerfacez.com')
                || targetUrlStr.includes('static-cdn.jtvnw.net');

            // Full browser-like headers — needed to bypass Cloudflare on datacenter IPs.
            // fetch() uses HTTP/2 (undici in Node 20) which greatly improves CF compatibility.
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

            console.log(`[${new Date().toISOString()}] HTTP Proxy ${isImageRequest ? 'image' : 'API '}: ${targetUrlStr}`);

            (async () => {
                try {
                    const controller = new AbortController();
                    const timeoutId = setTimeout(() => controller.abort(), 20000);

                    const response = await fetch(targetUrlStr, {
                        method: 'GET',
                        headers: fetchHeaders,
                        signal: controller.signal,
                        redirect: 'follow',
                    });
                    clearTimeout(timeoutId);

                    console.log(`[${new Date().toISOString()}] HTTP Proxy → ${response.status} from ${targetUrlStr}`);

                    if (!res.headersSent) {
                        const forwardHeaders = {
                            'Access-Control-Allow-Origin': '*',
                            'Cache-Control': isImageRequest ? 'public, max-age=86400' : 'no-cache',
                        };
                        const contentType = response.headers.get('content-type');
                        if (contentType) forwardHeaders['Content-Type'] = contentType;

                        res.writeHead(response.status, forwardHeaders);
                    }

                    const buffer = await response.arrayBuffer();
                    res.end(Buffer.from(buffer));
                } catch (err) {
                    console.error(`[${new Date().toISOString()}] HTTP Proxy error for ${targetUrlStr}:`, err.message);
                    if (!res.headersSent) {
                        res.writeHead(502, { 'Content-Type': 'text/plain' });
                        res.end('Bad Gateway');
                    } else {
                        res.end();
                    }
                }
            })();
        } catch (e) {
            console.log(`[${new Date().toISOString()}] HTTP Proxy bad request: ${e.message}`);
            if (!res.headersSent) {
                res.writeHead(400, { 'Content-Type': 'text/plain' });
                res.end('Invalid Request');
            }
        }
        return;
    }

    res.writeHead(404, { 'Content-Type': 'text/plain' });
    res.end('Not Found');
});

// ---------------------------------------------------------------------------
// WebSocket server — each client gets its OWN Twitch IRC connection.
//
// WHY per-client (not shared):
//   The shared-upstream model fails because auth (CAP/PASS/NICK/JOIN) is
//   owned by the C# client, not the proxy. When Twitch drops an
//   unauthenticated shared connection (~30s), the proxy reconnects without
//   auth and all existing clients silently lose their IRC session.
//
// WHY duplicates are now safe:
//   Duplicate messages from reconnects were caused by two concurrent
//   ReceiveLoopAsync tasks in the C# app (old loop calling ScheduleReconnect
//   in its finally block after CancellationToken fired). That is fixed in
//   TwitchIrcClient.cs — ReceiveLoopAsync now uses a CancellationToken and
//   won't call ScheduleReconnect on intentional disconnect.
// ---------------------------------------------------------------------------

const wss = new WebSocket.Server({ server });

wss.on('connection', (clientWs, req) => {
    const token = req.headers['x-proxy-token'];

    if (!PROXY_TOKEN || token !== PROXY_TOKEN) {
        console.log(`[${new Date().toISOString()}] Rejected WS connection: Invalid or missing token.`);
        clientWs.close(4001, 'Unauthorized');
        return;
    }

    console.log(`[${new Date().toISOString()}] Client connected. Opening dedicated Twitch IRC connection...`);

    const twitchWs = new WebSocket(TWITCH_WS_URL);
    const messageQueue = [];

    // ── Client → Twitch ──────────────────────────────────────────────────────
    clientWs.on('message', (message, isBinary) => {
        if (!isBinary) {
            const msgStr = message.toString('utf8');
            // Only log auth/control lines (not PING which happens every 30s)
            const lines = msgStr.split('\r\n').filter(l => l.trim().length > 0);
            lines.forEach(line => {
                if (!line.startsWith('PING')) {
                    let logMsg = line;
                    if (line.startsWith('PASS')) logMsg = 'PASS ***';
                    console.log(`[${new Date().toISOString()}] [Client -> Twitch] ${logMsg}`);
                }
            });
        }

        if (twitchWs.readyState === WebSocket.OPEN) {
            twitchWs.send(message, { binary: isBinary });
        } else if (twitchWs.readyState === WebSocket.CONNECTING) {
            messageQueue.push({ message, isBinary });
        }
    });

    // ── Twitch open: flush queued messages ───────────────────────────────────
    twitchWs.on('open', () => {
        console.log(`[${new Date().toISOString()}] Twitch IRC ready. Flushing ${messageQueue.length} queued message(s).`);
        while (messageQueue.length > 0) {
            const msg = messageQueue.shift();
            twitchWs.send(msg.message, { binary: msg.isBinary });
        }
    });

    // ── Twitch → Client ──────────────────────────────────────────────────────
    // IMPORTANT: Do NOT log every message here.
    // console.log is synchronous in Node.js. On a busy channel (5+ msg/sec),
    // logging every PRIVMSG fills the stdout pipe buffer and BLOCKS the event
    // loop — causing message bursts and apparent chat freezes on the client.
    let msgCount = 0;
    twitchWs.on('message', (message, isBinary) => {
        if (!isBinary) {
            const msgStr = message.toString('utf8');
            const lines = msgStr.split('\r\n').filter(l => l.trim().length > 0);
            lines.forEach(line => {
                // Log only non-chat control messages (RECONNECT, PING, errors, JOIN ack, etc.)
                if (!line.includes(' PRIVMSG ') && !line.includes(' USERSTATE ') &&
                    !line.includes(' USERNOTICE ') && !line.includes('PONG')) {
                    console.log(`[${new Date().toISOString()}] [Twitch -> Client] ${line}`);
                }
                msgCount++;
            });
        }

        if (clientWs.readyState === WebSocket.OPEN) {
            clientWs.send(message, { binary: isBinary });
        }
    });

    // Log message throughput every 60 seconds for diagnostics
    const statsInterval = setInterval(() => {
        if (msgCount > 0) {
            console.log(`[${new Date().toISOString()}] Twitch throughput: ${msgCount} lines in last 60s`);
            msgCount = 0;
        }
    }, 60000);

    // ── Closures ─────────────────────────────────────────────────────────────
    clientWs.on('close', (code) => {
        console.log(`[${new Date().toISOString()}] Client disconnected (code=${code}). Closing Twitch connection.`);
        if (twitchWs.readyState === WebSocket.OPEN || twitchWs.readyState === WebSocket.CONNECTING) {
            twitchWs.close();
        }
    });

    twitchWs.on('close', (code) => {
        clearInterval(statsInterval);
        console.log(`[${new Date().toISOString()}] Twitch IRC closed (code=${code}).`);
        if (clientWs.readyState === WebSocket.OPEN) {
            clientWs.close(1001, 'Twitch connection closed');
        }
    });

    // ── Errors ───────────────────────────────────────────────────────────────
    clientWs.on('error', (err) => {
        console.error(`[${new Date().toISOString()}] Client WS error:`, err.message);
    });

    twitchWs.on('error', (err) => {
        console.error(`[${new Date().toISOString()}] Twitch WS error:`, err.message);
    });
});

server.listen(PORT, () => {
    console.log(`TwiChatFHR Proxy listening on port ${PORT}`);

    // Show the commit version running
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
            } catch (e) { /* ignore */ }
        });
    }).on('error', () => {});

    // Auto-detect Hugging Face Spaces URL
    const hfHost = process.env.SPACE_HOST;
    const spaceId = process.env.SPACE_ID;
    let url = '';
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
