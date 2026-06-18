#!/bin/bash
# Скрипт автоматического развертывания TwiChatFHR Proxy в Google Cloud Run

echo "==================================================="
echo "🚀 Начало установки TwiChatFHR Proxy"
echo "==================================================="

# 1. Включаем необходимые API
echo "[1/4] Включение Cloud Run и Cloud Build API..."
gcloud services enable run.googleapis.com cloudbuild.googleapis.com
if [ $? -ne 0 ]; then
    echo "❌ Ошибка при включении API. Убедитесь, что у вас привязан биллинг."
    exit 1
fi

# 2. Генерируем безопасный случайный токен
echo "[2/4] Генерация секретного токена..."
PROXY_TOKEN=$(head -c 32 /dev/urandom | base64 | tr -dc 'a-zA-Z0-9' | head -c 24)
echo "Сгенерирован токен безопасности."

# 3. Скачиваем исходники (создаем временные файлы прямо тут)
echo "[3/4] Подготовка файлов сервера..."
mkdir -p twichat-proxy-tmp
cd twichat-proxy-tmp

cat << 'EOF' > package.json
{
  "name": "twichat-proxy",
  "version": "1.0.0",
  "main": "server.js",
  "scripts": { "start": "node server.js" },
  "dependencies": { "ws": "^8.16.0" }
}
EOF

cat << 'EOF' > server.js
const WebSocket = require('ws');
const http = require('http');

const PORT = process.env.PORT || 8080;
const PROXY_TOKEN = process.env.PROXY_TOKEN;
const TWITCH_WS_URL = 'wss://irc-ws.chat.twitch.tv:443';

const server = http.createServer((req, res) => {
    if (req.url === '/') { res.writeHead(200); res.end('TwiChatFHR Proxy is running.'); return; }
    res.writeHead(404); res.end('Not Found');
});

const wss = new WebSocket.Server({ server });

wss.on('connection', (clientWs, req) => {
    const token = req.headers['x-proxy-token'];
    if (!PROXY_TOKEN || token !== PROXY_TOKEN) {
        clientWs.close(4001, 'Unauthorized'); return;
    }
    const twitchWs = new WebSocket(TWITCH_WS_URL);
    clientWs.on('message', msg => { if (twitchWs.readyState === WebSocket.OPEN) twitchWs.send(msg); });
    twitchWs.on('message', msg => { if (clientWs.readyState === WebSocket.OPEN) clientWs.send(msg); });
    clientWs.on('close', () => { if (twitchWs.readyState === WebSocket.OPEN) twitchWs.close(); });
    twitchWs.on('close', () => { if (clientWs.readyState === WebSocket.OPEN) clientWs.close(); });
});

server.listen(PORT);
EOF

cat << 'EOF' > Dockerfile
FROM node:20-alpine
WORKDIR /usr/src/app
COPY package*.json ./
RUN npm install --omit=dev
COPY server.js ./
EXPOSE 8080
CMD ["npm", "start"]
EOF

# 4. Деплой в Cloud Run
echo "[4/4] Деплой сервера в Google Cloud Run (это займет пару минут)..."
REGION="us-central1"
SERVICE_NAME="twichat-proxy-$(date +%s)"

gcloud run deploy $SERVICE_NAME \
  --source . \
  --region $REGION \
  --allow-unauthenticated \
  --set-env-vars PROXY_TOKEN=$PROXY_TOKEN \
  --min-instances 0 \
  --max-instances 1 \
  --concurrency 1000 \
  --cpu 0.5 \
  --memory 512Mi \
  --quiet

if [ $? -ne 0 ]; then
    echo "❌ Ошибка при деплое."
    cd ..
    rm -rf twichat-proxy-tmp
    exit 1
fi

PROXY_URL=$(gcloud run services describe $SERVICE_NAME --region $REGION --format 'value(status.url)' | sed 's/https:\/\//wss:\/\//')

cd ..
rm -rf twichat-proxy-tmp

echo "==================================================="
echo "✅ УСТАНОВКА УСПЕШНО ЗАВЕРШЕНА!"
echo "Скопируйте эти данные и вставьте в настройки TwiChatFHR:"
echo "---------------------------------------------------"
echo "URL Сервера: $PROXY_URL"
echo "Токен (Token): $PROXY_TOKEN"
echo "==================================================="
echo "Обратите внимание: сервис настроен на автомасштабирование в 0."
echo "Он не потребляет ресурсы, пока вы не подключены."
