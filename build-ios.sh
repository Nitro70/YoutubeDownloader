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

mkdir -p dist
rm -f dist/YouTubeDownloader-ios.ipa

# .NET iOS device publish usually emits an .ipa directly; use it if present.
IPA="$(find ios-out -maxdepth 2 -name '*.ipa' | head -1)"
if [[ -n "$IPA" ]]; then
    echo "Using produced IPA: $IPA"
    cp "$IPA" dist/YouTubeDownloader-ios.ipa
else
    APP="$(find ios-out -maxdepth 3 -name '*.app' -type d | head -1)"
    if [[ -z "$APP" ]]; then
        echo "ERROR: no .app or .ipa was produced. Contents of ios-out:"
        find ios-out -maxdepth 3
        exit 1
    fi
    echo "Packaging app bundle: $APP"
    rm -rf Payload
    mkdir Payload
    cp -R "$APP" Payload/
    ( zip -r -y dist/YouTubeDownloader-ios.ipa Payload >/dev/null )
    rm -rf Payload
fi

echo
echo "Done: dist/YouTubeDownloader-ios.ipa"
echo "Sideload with AltStore or Sideloadly (it will sign with your Apple ID)."
