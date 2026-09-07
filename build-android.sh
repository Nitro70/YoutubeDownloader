#!/usr/bin/env bash
# Build a self-signed Android APK for sideloading.
#
# Requirements:
#   * .NET 8 SDK (the app targets net8.0-android; a newer SDK mis-resolves the
#     platform version — pin 8.0 via global.json if your default is newer).
#   * Android workload:  dotnet workload install android
#   * An Android SDK (ANDROID_HOME) with platform 34 + build-tools 34, and a JDK.
#
# Output: dist/YouTubeDownloader-android.apk
# Install: enable "install unknown apps" on the device, then open the APK.
set -euo pipefail

PROJ="YouTubeDownloader.Android/YouTubeDownloader.Android.csproj"
KS="app.keystore"

if [[ ! -f "$KS" ]]; then
    echo "Generating a self-signed keystore ($KS)..."
    keytool -genkeypair -v -keystore "$KS" -alias ytd \
        -keyalg RSA -keysize 2048 -validity 10000 \
        -storepass androidsideload -keypass androidsideload \
        -dname "CN=YouTube Downloader, O=Sideload, C=US"
fi

echo "Building signed APK..."
dotnet publish "$PROJ" \
    -c Release -f net8.0-android \
    -p:AndroidPackageFormat=apk \
    -p:AndroidKeyStore=true \
    -p:AndroidSigningKeyStore="$PWD/$KS" \
    -p:AndroidSigningKeyAlias=ytd \
    -p:AndroidSigningKeyPass=androidsideload \
    -p:AndroidSigningStorePass=androidsideload \
    -o android-out

mkdir -p dist
# The signed APK lands under bin/, not the publish -o dir.
APK="$(find . -name '*-Signed.apk' -not -path '*/obj/*' | head -1)"
[[ -n "$APK" ]] || APK="$(find . -name '*.apk' -not -path '*/obj/*' | head -1)"
if [[ -z "$APK" ]]; then
    echo "ERROR: no APK produced."; find . -name '*.apk'
    exit 1
fi
cp "$APK" dist/YouTubeDownloader-android.apk

echo
echo "Done: dist/YouTubeDownloader-android.apk"
