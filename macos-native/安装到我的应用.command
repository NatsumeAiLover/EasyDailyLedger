#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
TARGET="$HOME/Applications/日常账本原生版.app"
SOURCE="$PWD/日常账本.app"
echo '日常账本 · Apple 原生版 · macOS 27 / Apple Silicon'
if [ "$(uname -m)" != arm64 ]; then echo '此包需要 Apple Silicon Mac。'; exit 1; fi
if [ "$(sw_vers -productVersion | cut -d. -f1)" -lt 27 ]; then echo '此包面向 macOS 27 或更新版本。'; exit 1; fi
if [ ! -f "$SOURCE/Contents/MacOS/DailyLedgerNative" ]; then echo '请先解压完整发布包。'; exit 1; fi
if pgrep -x DailyLedgerNative >/dev/null; then echo '请先关闭原生版日常账本。'; exit 1; fi
read -r -p '安装到个人 Applications 文件夹？输入 y 确认：' ANSWER
if [ "$ANSWER" != y ]; then exit 0; fi
mkdir -p "$HOME/Applications"
if [ -e "$TARGET" ]; then mv "$TARGET" "$HOME/Applications/日常账本原生版-旧版-$(date +%Y%m%d-%H%M%S).app"; fi
ditto "$SOURCE" "$TARGET"
codesign --verify --deep --strict "$TARGET"
xattr -dr com.apple.quarantine "$TARGET" || true
open "$TARGET"
