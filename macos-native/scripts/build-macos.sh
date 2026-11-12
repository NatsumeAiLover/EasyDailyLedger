#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION=1.3.0
DIST="$PWD/release"
STAGE="$DIST/stage"
APP="$STAGE/日常账本.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
swift build -c release --arch arm64
BIN_DIR="$(swift build -c release --arch arm64 --show-bin-path)"
cp "$BIN_DIR/DailyLedgerNative" "$APP/Contents/MacOS/DailyLedgerNative"
chmod 755 "$APP/Contents/MacOS/DailyLedgerNative"
cp Resources/Info.plist "$APP/Contents/Info.plist"
if [ -f Resources/AppIcon.icns ]; then cp Resources/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"; fi
plutil -lint "$APP/Contents/Info.plist"
codesign --force --sign - "$APP"
codesign --verify --deep --strict --verbose=2 "$APP"
file "$APP/Contents/MacOS/DailyLedgerNative"
otool -L "$APP/Contents/MacOS/DailyLedgerNative" | tee "$DIST/linked-frameworks.txt"
if grep -E 'Electron|Chromium|WebKit' "$DIST/linked-frameworks.txt"; then echo 'Unexpected browser runtime'; exit 1; fi
cp 使用说明.txt "$STAGE/使用说明.txt"
cp 安装到我的应用.command "$STAGE/安装到我的应用.command"
chmod 755 "$STAGE/安装到我的应用.command"
ditto -c -k --sequesterRsrc --keepParent "$STAGE" "$DIST/DailyLedger-${VERSION}-macOS27-arm64.zip"
( cd "$DIST"; shasum -a 256 "DailyLedger-${VERSION}-macOS27-arm64.zip" > SHA256SUMS.txt )
du -h "$DIST/DailyLedger-${VERSION}-macOS27-arm64.zip"
