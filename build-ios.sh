#!/usr/bin/env bash
# Build an UNSIGNED iOS IPA for sideloading. Must run on macOS with the .NET 10 SDK,
# the .NET iOS workload, and Xcode 26 or newer.
#
#   dotnet workload install ios
#   ./build-ios.sh
#
# The app targets the iOS 26.0 SDK pack, which .NET normally only accepts with Xcode
# 26.0.x (CI selects exactly that). With a newer Xcode this script turns that check off
# (checked with Xcode 27.0); set DEVELOPER_DIR to pick an Xcode without changing the
# system-wide one.
#
# Output: dist/YouTubeDownloader-ios.ipa
# Sideload it with AltStore or Sideloadly, which re-sign with your Apple ID.
set -euo pipefail

if [[ "$(uname)" != "Darwin" ]]; then
    echo "ERROR: iOS builds require macOS (Xcode toolchain). This is $(uname)."
    exit 1
fi

PROJ="YouTubeDownloader.iOS/YouTubeDownloader.iOS.csproj"

XCODE_VERSION="$(xcodebuild -version | awk 'NR == 1 { print $2 }')"
EXTRA=()
case "$XCODE_VERSION" in
    26.0|26.0.*) ;;
    2[6-9].*|[3-9][0-9].*)
        echo "Note: Xcode $XCODE_VERSION, not 26.0; building without .NET's Xcode version check."
        EXTRA+=(-p:ValidateXcodeVersion=false) ;;
    *)
        echo "ERROR: Xcode 26 or newer is needed; this is Xcode $XCODE_VERSION."
        exit 1 ;;
esac

echo "Publishing unsigned app bundle (device arm64, full AOT)..."
dotnet publish "$PROJ" \
    -c Release -f net10.0-ios26.0 -r ios-arm64 \
    -p:EnableCodeSigning=false \
    -p:CodesignKey= \
    ${EXTRA[@]+"${EXTRA[@]}"} \
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
