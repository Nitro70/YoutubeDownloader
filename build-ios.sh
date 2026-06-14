#!/usr/bin/env bash
# Build an UNSIGNED iOS IPA for sideloading. Must run on macOS with Xcode
# and the .NET iOS workload installed.
#
#   dotnet workload install ios
#   ./build-ios.sh
#
# Output: dist/YouTubeDownloader-ios.ipa
# Sideload it with AltStore or Sideloadly, which re-sign with your Apple ID.
set -euo pipefail

if [[ "$(uname)" != "Darwin" ]]; then
    echo "ERROR: iOS builds require macOS (Xcode toolchain). This is $(uname)."
    exit 1
fi

PROJ="YouTubeDownloader.iOS/YouTubeDownloader.iOS.csproj"

echo "Publishing unsigned app bundle (device arm64, full AOT)..."
dotnet publish "$PROJ" \
    -c Release -f net8.0-ios -r ios-arm64 \
    -p:EnableCodeSigning=false \
    -p:CodesignKey= \
    -p:MtouchLink=SdkOnly \
    -o ios-out

APP="$(find ios-out -maxdepth 3 -name '*.app' -type d | head -1)"
if [[ -z "$APP" ]]; then
    echo "ERROR: no .app bundle was produced. Contents of ios-out:"
    find ios-out -maxdepth 3
    exit 1
fi
echo "App bundle: $APP"

echo "Packaging IPA..."
rm -rf Payload dist/YouTubeDownloader-ios.ipa
mkdir -p Payload dist
cp -R "$APP" Payload/
( zip -r -y dist/YouTubeDownloader-ios.ipa Payload >/dev/null )
rm -rf Payload

echo
echo "Done: dist/YouTubeDownloader-ios.ipa"
echo "Sideload with AltStore or Sideloadly (it will sign with your Apple ID)."
