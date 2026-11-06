#!/usr/bin/env bash
set -euo pipefail

usage() {
    cat <<'EOF'
Usage: bash scripts/run-web-player.sh [--port PORT] [--skip-publish]

Publish the Web Player with Release AOT and start a local static server.
  -p, --port PORT    Listen on PORT (default: 5052).
  --skip-publish     Serve the existing artifacts/web-player publication.
  -h, --help         Show this help.

Examples:
  bash scripts/run-web-player.sh --port 5053
  bash scripts/run-web-player.sh --port 5053 --skip-publish
EOF
}

port=5052
skip_publish=false
while (($#)); do
    case "$1" in
        -p|--port)
            if (($# < 2)); then
                echo 'Missing value for --port.' >&2
                exit 2
            fi
            port="$2"
            shift 2
            ;;
        --skip-publish)
            skip_publish=true
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            echo "Unknown argument: $1" >&2
            usage >&2
            exit 2
            ;;
    esac
done

if [[ ! "$port" =~ ^[0-9]{1,5}$ ]] || ((10#$port < 1 || 10#$port > 65535)); then
    echo 'Port must be an integer between 1 and 65535.' >&2
    exit 2
fi
port=$((10#$port))

repository_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
project_file="$repository_directory/src/Zhengyan.DigitalWife.GamePlayer.Web/Zhengyan.DigitalWife.GamePlayer.Web.csproj"
publish_directory="$repository_directory/artifacts/web-player"
web_directory="$publish_directory/wwwroot"

if ! command -v python3 >/dev/null 2>&1; then
    echo 'Python 3 is required to serve the published Web Player.' >&2
    exit 1
fi

if [[ "$skip_publish" == false ]]; then
    if ! command -v dotnet >/dev/null 2>&1; then
        echo 'Install the .NET 10 SDK and wasm-tools workload before publishing.' >&2
        exit 1
    fi
    if ! dotnet publish "$project_file" -c Release -o "$publish_directory" -v minimal; then
        echo 'Web AOT publication failed.' >&2
        exit 1
    fi
fi

if [[ ! -f "$web_directory/index.html" ]]; then
    echo 'The published Web Player is missing. Run without --skip-publish first.' >&2
    exit 1
fi

echo "Web Player: http://127.0.0.1:$port/"
echo 'Press Ctrl+C to stop the local server.'
exec python3 -m http.server "$port" --bind 127.0.0.1 --directory "$web_directory"
