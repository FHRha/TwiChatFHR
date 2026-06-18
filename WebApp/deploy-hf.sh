#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════════
#  Деплой TwiChatFHR Web → HuggingFace Spaces
#
#  Использование:
#    bash WebApp/deploy-hf.sh <hf-username> <space-name>
#
#  Пример:
#    bash WebApp/deploy-hf.sh FHRha twichatfhr
# ═══════════════════════════════════════════════════════════════════════════
set -euo pipefail

HF_USER="${1:?Usage: $0 <hf-username> <space-name>}"
HF_SPACE="${2:?Usage: $0 <hf-username> <space-name>}"
HF_REPO="https://huggingface.co/spaces/${HF_USER}/${HF_SPACE}"

CYAN='\033[0;36m'; GREEN='\033[0;32m'; BOLD='\033[1m'; NC='\033[0m'
info()    { echo -e "${CYAN}[INFO]${NC}  $*"; }
success() { echo -e "${GREEN}[OK]${NC}    $*"; }

# Detect repo root (script may be called from anywhere)
REPO_ROOT="$(git -C "$(dirname "$0")" rev-parse --show-toplevel)"

info "Preparing HuggingFace deploy package..."

DEPLOY_DIR="$(mktemp -d)/hf-deploy"
mkdir -p "$DEPLOY_DIR"

# Copy WebApp source
cp -r "$REPO_ROOT/WebApp" "$DEPLOY_DIR/WebApp"
# Copy shared source (referenced by csproj)
cp -r "$REPO_ROOT/Core"   "$DEPLOY_DIR/Core"
cp -r "$REPO_ROOT/Server" "$DEPLOY_DIR/Server"

# HuggingFace needs Dockerfile at repo root
cp "$DEPLOY_DIR/WebApp/Dockerfile" "$DEPLOY_DIR/Dockerfile"

# Write README.md (required by HF Spaces)
cat > "$DEPLOY_DIR/README.md" <<EOF
---
title: TwiChatFHR
emoji: 💬
colorFrom: indigo
colorTo: cyan
sdk: docker
app_port: 7860
pinned: false
---

# TwiChatFHR Web

Self-hosted Twitch Chat Overlay server.

- Admin panel: [your-space.hf.space](https://huggingface.co/spaces/${HF_USER}/${HF_SPACE})
- OBS Overlay URL: \`https://${HF_USER}-${HF_SPACE}.hf.space/overlay\`

Set secrets in Space settings: \`ADMIN_USERNAME\`, \`ADMIN_PASSWORD\`, \`TWITCH_CHANNEL\`
EOF

info "Initializing git and pushing to HuggingFace Spaces..."
cd "$DEPLOY_DIR"
git init
git add -A
git commit -m "Deploy TwiChatFHR Web"

info "Pushing to ${HF_REPO}.git ..."
info "(You will be asked for your HF token as password)"
git push --force "${HF_REPO}.git" main

success "Done! Your Space is building at:"
echo ""
echo -e "  ${BOLD}${CYAN}${HF_REPO}${NC}"
echo ""
echo -e "  OBS Overlay URL:"
echo -e "  ${BOLD}${CYAN}https://${HF_USER}-${HF_SPACE}.hf.space/overlay${NC}"
echo ""
echo "  Build takes ~3-5 minutes. Check logs in Space → Logs tab."

# Cleanup
rm -rf "$DEPLOY_DIR"
