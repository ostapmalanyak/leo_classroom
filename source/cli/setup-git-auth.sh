#!/usr/bin/env bash
# One-time git setup per device: browser sign-in for Forgejo, token remembered afterwards.
# Usage: cli/setup-git-auth.sh <forgejo-base-url> [oauth-client-id]
#   e.g. cli/setup-git-auth.sh http://10.191.18.44:3000 1a2b3c...   (host and port exactly as in the clone URL)
# The client id comes from DEPLOY.md step 7b and is public.
set -euo pipefail
H=${1:?base url missing, e.g. http://10.191.18.44:3000}; H=${H%/}
# Default ids of the shared instances; the id is public. A new Forgejo needs its own (DEPLOY.md 7b).
case $H in
  http://localhost:3000) DEF=80bcd4ca-b040-4910-8717-a1252b54a889 ;;
  https://vm123.htl-leonding.ac.at:3000) DEF=40caed35-efaf-45e9-b4dc-c3db5df64ce2 ;;
  *) DEF= ;;
esac
ID=${2:-$DEF}; [ -n "$ID" ] || { echo "oauth client id missing (2nd argument)"; exit 1; }

command -v git-credential-oauth >/dev/null || {
  echo "git-credential-oauth not found. Install it first:"
  echo "  Fedora/Arch/Homebrew: package 'git-credential-oauth'; else https://github.com/hickford/git-credential-oauth/releases"
  exit 1; }

# oauth only fetches the token; a second helper must keep it.
case "$(uname -s)" in
  Darwin) STORE=osxkeychain ;;
  *) STORE=$(ls /usr/libexec/git-core/git-credential-libsecret /usr/lib/git-core/git-credential-libsecret 2>/dev/null | head -1 || true)
     STORE=${STORE:-'cache --timeout 28800'}  # no keychain helper: remember 8 h in memory
     [[ $STORE == cache* ]] && echo "note: install git-credential-libsecret to keep the login across reboots" ;;
esac

k=credential.$H
git config --global --unset-all "$k.helper" 2>/dev/null || true
git config --global --add "$k.helper" "$STORE"
git config --global --add "$k.helper" oauth
git config --global "$k.oauthClientId" "$ID"
git config --global "$k.oauthAuthURL" /login/oauth/authorize
git config --global "$k.oauthTokenURL" /login/oauth/access_token
echo "done. First git command against $H opens the browser, later ones are silent."
