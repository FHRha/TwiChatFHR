# TwiChatFHR

> [!IMPORTANT]
> Лёгкий и красивый чат-оверлей для OBS со **встроенным обходом блокировок**.  
> Работает и как **десктопное приложение (Windows)**, и как **веб-сервер** (HuggingFace, VPS, Docker).

---

## 🌐 Веб-версия — деплой на HuggingFace за 2 минуты

Решает главную проблему: Twitch заблокирован, а свой сервер решает это раз и навсегда.

### Шаг 1 — Создать Space

Заходишь на [huggingface.co/new-space](https://huggingface.co/new-space):
- **SDK**: выбираешь **Docker**
- Имя — любое, например `twichatfhr`
- Нажимаешь **Create Space**

### Шаг 2 — Вставить Dockerfile

В Space нажимаешь **"+ Add file" → "Create new file"**, называешь файл `Dockerfile` и вставляешь:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

RUN apt-get update && apt-get install -y --no-install-recommends git \
    && git clone --depth=1 https://github.com/FHRha/TwiChatFHR.git . \
    && rm -rf /var/lib/apt/lists/*

RUN dotnet publish WebApp/TwiChatWeb.csproj \
    -c Release -o /app --no-self-contained -r linux-x64

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
RUN mkdir -p /app/cache
ENV PORT=7860
EXPOSE 7860
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD curl -f http://localhost:7860/health || exit 1
ENTRYPOINT ["dotnet", "TwiChatWeb.dll"]
```

Нажимаешь **Commit** — Space начнёт сборку (~3-5 минут).

### Шаг 3 — Добавить секреты

В Space → **Settings → Variables and secrets → New secret**:

| Secret | Значение |
|--------|---------|
| `ADMIN_USERNAME` | любой логин |
| `ADMIN_PASSWORD` | любой пароль (мин. 4 символа) |
| `TWITCH_CHANNEL` | имя канала (необязательно) |

После сохранения Space перезапустится.

### Готово ✅

| | URL |
|---|---|
| **Панель настроек** | `https://ВАШ_НИК-twichatfhr.hf.space/` |
| **OBS Browser Source** | `https://ВАШ_НИК-twichatfhr.hf.space/overlay` |

---

## 🖥️ Десктопное приложение (Windows)

Скачать готовую сборку: [Releases](../../releases)

Или собрать самому:

```bash
git clone https://github.com/FHRha/TwiChatFHR.git
cd TwiChatFHR
dotnet run
```

> Требуется .NET 8 SDK и Microsoft Edge WebView2 Runtime.

---

## ⚙️ Самостоятельный хостинг (VPS / Docker)

```bash
curl -fsSL https://raw.githubusercontent.com/FHRha/TwiChatFHR/main/WebApp/install.sh | bash
```

Скрипт спросит канал, логин и пароль — и запустит всё через Docker Compose.

---

## Ключевые возможности

- Прямое подключение к Twitch IRC без браузера
- Эмоуты: 7TV, BTTV, FFZ + обход блокировок через прокси
- Настраиваемый дизайн: темы, шрифты, цвета, анимации, лэйаут
- Модерация: блэклист пользователей, авто-бан по фразам
- Роли с подсветкой: Broadcaster, Mod, VIP
- Режим тестирования (введи `test` как имя канала)

> [!TIP]
> Введи `test` в поле канала — запустится встроенный симулятор чата для настройки дизайна без стрима.
