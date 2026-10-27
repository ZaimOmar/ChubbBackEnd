#!/usr/bin/env bash
# Backend security gate: secret scan, vulnerable packages, format check, image scan.
# Exits non-zero on any finding OR if a required tool is missing (a skipped check must
# never look like a pass). Safe to re-run: it only reads and prints.
# Usage: scripts/security-check.sh            (all checks; trivy only if images exist)
set -uo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

declare -a results=()
failures=0

record() { # name status(PASS|FAIL|MISSING)
  results+=("$(printf '%-28s %s' "$1" "$2")")
  [[ "$2" == "PASS" ]] || failures=$((failures + 1))
}

have() { command -v "$1" >/dev/null 2>&1; }

# 1. Secrets: working tree and full git history (a secret removed later is still leaked).
if have gitleaks; then
  gitleaks detect --source . --no-git --redact --no-banner && tree=PASS || tree=FAIL
  record "gitleaks (working tree)" "$tree"
  if [[ -d .git ]]; then
    gitleaks detect --source . --redact --no-banner && hist=PASS || hist=FAIL
    record "gitleaks (git history)" "$hist"
  fi
else
  echo "MISSING TOOL: gitleaks (https://github.com/gitleaks/gitleaks#installing)" >&2
  record "gitleaks" "MISSING"
fi

# 2. Dependencies: --include-transitive because most CVEs arrive indirectly.
# The command exits 0 even with findings, so the output is inspected.
if have dotnet; then
  audit="$(dotnet list package --vulnerable --include-transitive 2>&1)"
  echo "$audit"
  if grep -q "has the following vulnerable packages" <<<"$audit"; then
    record "dotnet vulnerable packages" "FAIL"
  elif grep -qiE "error|unable to load|failed|no assets file" <<<"$audit"; then
    record "dotnet vulnerable packages" "FAIL"   # could not audit (e.g. offline): not a pass
  else
    record "dotnet vulnerable packages" "PASS"
  fi

  # 3. Format and analyser rules, per .editorconfig.
  dotnet format --verify-no-changes && fmt=PASS || fmt=FAIL
  record "dotnet format" "$fmt"
else
  echo "MISSING TOOL: dotnet SDK" >&2
  record "dotnet audit/format" "MISSING"
fi

# 4. Images: scan only the compose-built images that exist locally.
if have trivy && have docker; then
  scanned=0
  for image in $(docker compose config --images 2>/dev/null | grep '^chubb-claims-' || true); do
    docker image inspect "$image" >/dev/null 2>&1 || continue
    scanned=$((scanned + 1))
    trivy image --quiet --exit-code 1 --severity HIGH,CRITICAL --ignore-unfixed "$image" \
      && st=PASS || st=FAIL
    record "trivy $image" "$st"
  done
  [[ $scanned -gt 0 ]] || echo "No built images found; skipping trivy (run docker compose build first)."
else
  echo "NOTE: trivy or docker not available; image scan skipped (optional)." >&2
fi

echo
echo "===== security-check summary ====="
printf '%s\n' "${results[@]}"
if [[ $failures -gt 0 ]]; then
  echo "RESULT: FAILED ($failures check(s) failed or missing)"
  exit 1
fi
echo "RESULT: OK"
