# ── Build stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy the entire repo (build context is repo root)
COPY . .

# Restore and publish only the web project
RUN dotnet publish WebApp/TwiChatWeb.csproj \
    -c Release \
    -o /app \
    --no-self-contained \
    -r linux-x64

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0

# Install curl (used by health checks and install scripts)
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

# Create volume mount point for persistent data (config, cache, blacklist)
RUN mkdir -p /app/cache
VOLUME ["/app/cache"]

# HuggingFace Spaces default port; override with PORT env var
ENV PORT=7860
EXPOSE 7860

# Health check — used by Docker and HF Spaces
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:${PORT}/health || exit 1

ENTRYPOINT ["dotnet", "TwiChatWeb.dll"]
