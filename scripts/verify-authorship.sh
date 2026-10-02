#!/usr/bin/env bash
set -euo pipefail

forbidden_identity_pattern='(^|[^[:alnum:]])(claude|julocae|chatgpt|codex|copilot)([^[:alnum:]]|$)|anthropic|openai'
forbidden_trailer_pattern='^[[:space:]]*(co-authored-by|author|generated-by|assisted-by):.*(claude|julocae|chatgpt|codex|copilot|anthropic|openai)'

failures=0

fail_if_forbidden_identity() {
  local label="$1"
  local value="$2"

  if printf '%s\n' "$value" | grep -Eiq "$forbidden_identity_pattern"; then
    echo "Authorship verification failed: ${label} must identify the real contributor, not an agent or AI service." >&2
    failures=1
  fi
}

fail_if_forbidden_trailer() {
  local message="$1"

  if printf '%s\n' "$message" | grep -Eiq "$forbidden_trailer_pattern"; then
    echo "Authorship verification failed: commit messages must not contain AI or agent attribution trailers." >&2
    failures=1
  fi
}

verify_current_identity_and_message() {
  local message_file="$1"

  test -f "$message_file" || { echo "Authorship verification failed: commit message file was not found." >&2; exit 2; }
  fail_if_forbidden_identity "author identity" "$(git var GIT_AUTHOR_IDENT)"
  fail_if_forbidden_identity "committer identity" "$(git var GIT_COMMITTER_IDENT)"
  fail_if_forbidden_trailer "$(<"$message_file")"
}

verify_commit_range() {
  local range="$1"
  local commit

  while IFS= read -r commit; do
    fail_if_forbidden_identity "author of ${commit}" "$(git show -s --format='%an <%ae>' "$commit")"
    fail_if_forbidden_identity "committer of ${commit}" "$(git show -s --format='%cn <%ce>' "$commit")"
    fail_if_forbidden_trailer "$(git show -s --format='%B' "$commit")"
  done < <(git rev-list "$range")
}

case "${1:-}" in
  --message-file)
    test "$#" -eq 2 || { echo "Usage: $0 --message-file <path>" >&2; exit 2; }
    verify_current_identity_and_message "$2"
    ;;
  --range)
    test "$#" -eq 2 || { echo "Usage: $0 --range <git-range>" >&2; exit 2; }
    verify_commit_range "$2"
    ;;
  *)
    echo "Usage: $0 --message-file <path> | --range <git-range>" >&2
    exit 2
    ;;
esac

exit "$failures"
