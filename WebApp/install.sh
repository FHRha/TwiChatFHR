#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════════
#  TwiChatFHR Web — Install Script
#
#  Usage:
#    curl -fsSL https://raw.githubusercontent.com/FHRha/TwiChatFHR/main/WebApp/install.sh | bash
#
#  Or download and run:
#    wget -qO install.sh https://raw.githubusercontent.com/FHRha/TwiChatFHR/main/WebApp/install.sh
#    bash install.sh
# ═══════════════════════════════════════════════════════════════════════════
set -euo pipefail

REPO_URL="https://github.com/FHRha/TwiChatFHR"
INSTALL_DIR="${INSTALL_DIR:-$HOME/twichatfhr}"
PORT="${PORT:-7860}"

# ── Colors ────────────────────────────────────────────────────────────────────
RED='\033[0;31m'; GREEN='\033[0;32m'; CYAN='\033[0;36m'
YELLOW='\033[1;33m'; BOLD='\033[1m'; NC='\033[0m'

info()    { echo -e "${CYAN}[INFO]${NC}  $*"; }
success() { echo -e "${GREEN}[OK]${NC}    $*"; }
warn()    { echo -e "${YELLOW}[WARN]${NC}  $*"; }
error()   { echo -e "${RED}[ERROR]${NC} $*"; exit 1; }

echo -e "${BOLD}"
echo "  ████████╗██╗    ██╗██╗ ██████╗██╗  ██╗ █████╗ ████████╗"
echo "     ██╔══╝██║    ██║██║██╔════╝██║  ██║██╔══██╗╚══██╔══╝"
echo "     ██║   ██║ █╗ ██║██║██║     ███████║███████║   ██║   "
echo "     ██║   ██║███╗██║██║██║     ██╔══██║██╔══██║   ██║   "
echo "     ██║   ╚███╔███╔╝██║╚██████╗██║  ██║██║  ██║   ██║   "
echo "     ╚═╝    ╚══╝╚══╝ ╚═╝ ╚═════╝╚═╝  ╚═╝╚═╝  ╚═╝   ╚═╝   FHR"
echo -e "${NC}"
echo -e "  Web Edition — Self-hosted Twitch Chat Overlay"
echo ""

# ── Check prerequisites ───────────────────────────────────────────────────────
info "Checking prerequisites..."

if ! command -v docker &>/dev/null; then
    error "Docker is not installed. Install it from https://docs.docker.com/get-docker/"
fi

if docker compose version &>/dev/null 2>&1; then
    COMPOSE="docker compose"
elif command -v docker-compose &>/dev/null; then
    COMPOSE="docker-compose"
else
    error "Docker Compose not found. Install Docker Desktop or 'docker-compose'."
fi

success "Docker $(docker --version | cut -d' ' -f3 | tr -d ',')"
success "Compose: $($COMPOSE version --short 2>/dev/null || echo 'v1.x')"

# ── Clone / update repo ───────────────────────────────────────────────────────
if [ -d "$INSTALL_DIR/.git" ]; then
    info "Updating existing installation at $INSTALL_DIR ..."
    git -C "$INSTALL_DIR" pull --ff-only
else
    info "Cloning repository to $INSTALL_DIR ..."
    git clone --depth=1 "$REPO_URL" "$INSTALL_DIR"
fi

cd "$INSTALL_DIR/WebApp"

# ── Interactive setup ─────────────────────────────────────────────────────────
echo ""
echo -e "${BOLD}━━━ Setup ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo ""

# Twitch channel
if [ -z "${TWITCH_CHANNEL:-}" ]; then
    read -rp "  Twitch channel name (e.g. xqc): " TWITCH_CHANNEL
    TWITCH_CHANNEL=$(echo "$TWITCH_CHANNEL" | tr '[:upper:]' '[:lower:]' | tr -d ' ')
fi

# Admin credentials
if [ -z "${ADMIN_USERNAME:-}" ]; then
    read -rp "  Admin username [admin]: " ADMIN_USERNAME
    ADMIN_USERNAME="${ADMIN_USERNAME:-admin}"
fi

if [ -z "${ADMIN_PASSWORD:-}" ]; then
    while true; do
        read -rsp "  Admin password (min 4 chars): " ADMIN_PASSWORD
        echo ""
        if [ ${#ADMIN_PASSWORD} -ge 4 ]; then break; fi
        warn "Password too short, try again."
    done
fi

# Port
read -rp "  Port [${PORT}]: " INPUT_PORT
PORT="${INPUT_PORT:-$PORT}"

echo ""

# ── Write .env ────────────────────────────────────────────────────────────────
cat > .env <<EOF
TWITCH_CHANNEL=${TWITCH_CHANNEL}
ADMIN_USERNAME=${ADMIN_USERNAME}
ADMIN_PASSWORD=${ADMIN_PASSWORD}
PORT=${PORT}
EOF
success ".env written"

# ── Build and start ───────────────────────────────────────────────────────────
info "Building Docker image (this may take a few minutes)..."
$COMPOSE build --no-cache

info "Starting service..."
$COMPOSE up -d

# Wait for health check
info "Waiting for service to start..."
sleep 5
for i in {1..12}; do
    if curl -sf "http://localhost:${PORT}/health" &>/dev/null; then
        break
    fi
    sleep 3
done

echo ""
echo -e "${BOLD}${GREEN}━━━ TwiChatFHR is running! ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo ""
echo -e "  🎛️  Admin panel : ${CYAN}http://localhost:${PORT}/${NC}"
echo -e "  📺  OBS Overlay : ${CYAN}http://localhost:${PORT}/overlay${NC}"
echo ""
echo -e "  ${YELLOW}Login:${NC}    ${ADMIN_USERNAME}"
echo -e "  ${YELLOW}Password:${NC} (as set above)"
echo ""
echo -e "  To stop:   ${BOLD}cd ${INSTALL_DIR}/WebApp && docker compose down${NC}"
echo -e "  To update: ${BOLD}cd ${INSTALL_DIR}/WebApp && git pull && docker compose up -d --build${NC}"
echo -e "  Logs:      ${BOLD}docker compose logs -f${NC}"
echo ""
echo -e "${BOLD}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
