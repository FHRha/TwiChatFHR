# ════════════════════════════════════════════════════════════════════
#  TwiChatFHR — HuggingFace Space Dockerfile
#  Просто создай Space (SDK: Docker) и вставь этот файл.
# ════════════════════════════════════════════════════════════════════

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Скачиваем исходный код с GitHub
RUN apt-get update && apt-get install -y --no-install-recommends git \
    && git clone --depth=1 https://github.com/FHRha/TwiChatFHR.git . \
    && rm -rf /var/lib/apt/lists/*

# Собираем веб-проект
RUN dotnet publish WebApp/TwiChatWeb.csproj \
    -c Release \
    -o /app \
    --no-self-contained \
    -r linux-x64

# ── Runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

RUN mkdir -p /app/cache
VOLUME ["/app/cache"]

ENV PORT=7860
EXPOSE 7860

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD curl -f http://localhost:7860/health || exit 1

ENTRYPOINT ["dotnet", "TwiChatWeb.dll"]
