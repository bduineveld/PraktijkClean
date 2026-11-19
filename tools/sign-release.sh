#!/bin/zsh
# Ondertekent een release-exe voor "bijwerken met één klik" (zie docs/BIJWERKEN.md).
# Gebruik: tools/sign-release.sh dist/PraktijkClean-1.4.0.exe
# Maakt PraktijkClean-1.4.0.exe.sig (base64, RSA-3072 / SHA-256 / PKCS#1 v1.5) en controleert die meteen.
# De geheime sleutel staat NIET in deze map en NIET op GitHub, maar alleen hier:
KEY="${PRAKTIJKCLEAN_SIGNING_KEY:-$HOME/.config/praktijkclean/update-signing-key.pem}"
set -euo pipefail
EXE="${1:?Geef het pad van de .exe}"
[[ -f "$EXE" ]] || { echo "Niet gevonden: $EXE" >&2; exit 1; }
[[ -f "$KEY" ]] || { echo "Ondertekensleutel niet gevonden: $KEY" >&2; exit 1; }
TMP=$(mktemp -d); trap 'rm -rf "$TMP"' EXIT
openssl dgst -sha256 -sign "$KEY" -out "$TMP/sig.bin" "$EXE"
openssl rsa -in "$KEY" -pubout -out "$TMP/pub.pem" 2>/dev/null
openssl dgst -sha256 -verify "$TMP/pub.pem" -signature "$TMP/sig.bin" "$EXE" >/dev/null
base64 < "$TMP/sig.bin" | tr -d '\n' > "$EXE.sig"
echo "Ondertekend: $EXE.sig"
