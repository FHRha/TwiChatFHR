# TwiChatFHR Web Edition

Веб-версия TwiChatFHR — самостоятельно хостируемый сервис чата Twitch для OBS.  
Решает проблему доступа к Twitch через прокси: деплой на любой сервер (VPS, HuggingFace, Railway, Render).

## Маршруты

| URL | Доступ | Назначение |
|-----|--------|------------|
| `/` | 🔒 Логин | Панель настроек (admin) |
| `/overlay` | 🌐 Публичный | Оверлей для OBS Browser Source |
| `/ws` | 🌐 Публичный | WebSocket (overlay + превью) |
| `/api/*` | 🔒 Логин | REST API |
| `/health` | 🌐 Публичный | Health check |

## Быстрая установка (Docker)

```bash
curl -fsSL https://raw.githubusercontent.com/FHRha/TwiChatFHR/main/WebApp/install.sh | bash
```

Скрипт спросит:
- Twitch канал
- Логин и пароль для панели
- Порт (по умолчанию 7860)

## Ручная установка

```bash
git clone https://github.com/FHRha/TwiChatFHR
cd TwiChatFHR/WebApp
cp .env.example .env
# Заполните .env
docker compose up -d
```

## Переменные окружения

| Переменная | По умолчанию | Описание |
|------------|-------------|----------|
| `PORT` | `7860` | Порт сервера |
| `TWITCH_CHANNEL` | — | Начальный канал (опционально) |
| `ADMIN_USERNAME` | `admin` | Логин для панели |
| `ADMIN_PASSWORD` | — | Пароль (хешируется при первом старте) |

## Локальная разработка (без Docker)

```bash
cd TwiChatFHR   # корень репозитория
dotnet run --project WebApp/TwiChatWeb.csproj
```

Откройте `http://localhost:7860/`

## HuggingFace Spaces

1. Fork репозиторий
2. В Space settings → Secrets добавьте:
   - `ADMIN_USERNAME`, `ADMIN_PASSWORD`, `TWITCH_CHANNEL`
3. В `Dockerfile` уже настроен порт 7860

## Структура WebApp/

```
WebApp/
├── TwiChatWeb.csproj       # net8.0 проект
├── Program.cs              # Headless entry point
├── WebServerManager.cs     # ASP.NET Core сервер + REST API
├── AdminAuth.cs            # Хеширование паролей
├── AppSettingsWeb.cs       # Поля auth в AppSettings
├── ServerExtensions.cs     # Partial расширения Server классов
├── ConfigManagerWeb.cs     # UpdateSettings()
├── admin/
│   ├── index.html          # Панель настроек
│   └── app.js
├── overlay/
│   ├── index.html          # Оверлей для OBS
│   └── app.js
├── Dockerfile
├── docker-compose.yml
├── install.sh
└── .env.example
```
