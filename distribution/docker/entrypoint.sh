#!/bin/sh
# Starts Cinnabarr as the user and group given by PUID and PGID (default 1000), so the files it writes into /config and
# into your media folders belong to you. Started as a non-root user (docker run --user ...), it just runs.
set -e

if [ "$(id -u)" = "0" ]; then
    PUID="${PUID:-1000}"
    PGID="${PGID:-1000}"

    groupmod -o -g "$PGID" cinnabarr
    usermod -o -u "$PUID" cinnabarr

    mkdir -p /config

    # Only walk the whole folder when its owner is wrong (a first start, or new PUID and PGID values).
    if [ "$(stat -c '%u:%g' /config)" != "$PUID:$PGID" ]; then
        chown -R "$PUID:$PGID" /config
    fi

    if [ -n "$UMASK" ]; then
        umask "$UMASK"
    fi

    exec setpriv --reuid="$PUID" --regid="$PGID" --init-groups "$@"
fi

exec "$@"
