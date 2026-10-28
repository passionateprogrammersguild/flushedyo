#!/usr/bin/env bash
# Unlocks the Bitwarden vault and exports the session key as BW_SESSION.
#
# Must be SOURCED so the export persists in your current shell:
#   source ./scripts/bw-unlock.sh
#
# Without a terminal (editor plugins, CI) the master password can be supplied
# through a named environment variable instead, which is passed to
# `bw unlock --passwordenv` so it never appears in argv:
#   export FLUSHEDYO_BW_PASSWORD='...'
#   BW_UNLOCK_PASSWORD_ENV=FLUSHEDYO_BW_PASSWORD source ./scripts/bw-unlock.sh

_bw_unlock() {
    if ! command -v bw >/dev/null 2>&1; then
        {
            echo "error: 'bw' (Bitwarden CLI) is not installed or not on your PATH."
            echo "Install it with one of:"
            echo "  brew install bitwarden-cli"
            echo "  npm install -g @bitwarden/cli"
            echo "More options: https://bitwarden.com/help/cli/"
            echo "Then run 'bw login' once and re-run: source ./scripts/bw-unlock.sh"
        } >&2
        return 127
    fi

    local -a unlock_args=(unlock --raw)

    if [[ -n "${BW_UNLOCK_PASSWORD_ENV:-}" ]]; then
        if [[ -z "$(printenv "$BW_UNLOCK_PASSWORD_ENV" 2>/dev/null)" ]]; then
            echo "error: BW_UNLOCK_PASSWORD_ENV=$BW_UNLOCK_PASSWORD_ENV is unset or empty." >&2
            return 1
        fi
        unlock_args+=(--passwordenv "$BW_UNLOCK_PASSWORD_ENV")
    fi

    local session
    # Interactively, bw prompts for the master password on the terminal
    # (stderr/tty). With --passwordenv it reads the password from the
    # environment instead. --raw prints only the session key to stdout.
    if ! session="$(bw "${unlock_args[@]}")" || [[ -z "$session" ]]; then
        if [[ -n "${BW_UNLOCK_PASSWORD_ENV:-}" ]]; then
            echo "error: failed to unlock vault non-interactively (check the password in '$BW_UNLOCK_PASSWORD_ENV' and that 'bw login' has run)." >&2
        else
            echo "error: failed to unlock vault (wrong password or not logged in? try 'bw login')." >&2
        fi
        return 1
    fi

    export BW_SESSION="$session"
    echo "Vault unlocked. BW_SESSION exported for this shell." >&2
}

# Detect whether the script is being sourced (bash or zsh).
_bw_sourced=0
if [[ -n "${ZSH_VERSION:-}" ]]; then
    [[ "${ZSH_EVAL_CONTEXT:-}" == *:file* ]] && _bw_sourced=1
elif [[ "${BASH_SOURCE[0]}" != "$0" ]]; then
    _bw_sourced=1
fi

if [[ $_bw_sourced -eq 0 ]]; then
    echo "error: this script must be sourced so BW_SESSION persists:" >&2
    echo "  source ./scripts/bw-unlock.sh" >&2
    exit 1
fi

_bw_unlock
_bw_rc=$?
unset -f _bw_unlock
unset _bw_sourced
return $_bw_rc 2>/dev/null
