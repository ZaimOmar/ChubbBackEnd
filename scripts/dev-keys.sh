#!/usr/bin/env bash
# Generates the dev RS256 key pair for running WITHOUT Docker (docker compose uses the
# jwt-keys init container instead). Output goes to the git-ignored .dev-keys/ directory.
# Safe to re-run: existing keys are kept so tokens stay valid.
set -euo pipefail

command -v openssl >/dev/null || { echo "openssl is required but not installed" >&2; exit 1; }

keys_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/.dev-keys"
private_key="$keys_dir/private.pem"
public_key="$keys_dir/public.pem"

mkdir -p "$keys_dir"
if [[ -f "$private_key" && -f "$public_key" ]]; then
  echo "Dev keys already exist in $keys_dir (kept)."
  exit 0
fi

# umask keeps the private key unreadable to other users from the moment it is created.
(umask 077 && openssl genrsa -out "$private_key" 2048 2>/dev/null)
openssl rsa -in "$private_key" -pubout -out "$public_key" 2>/dev/null
chmod 600 "$private_key"
chmod 644 "$public_key"
echo "Generated dev keys in $keys_dir"
echo "  Jwt__PrivateKeyPath=$private_key  (Identity only)"
echo "  Jwt__PublicKeyPath=$public_key"
