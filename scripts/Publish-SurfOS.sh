#!/bin/sh
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
project_root=$(CDPATH= cd -- "$script_dir/.." && pwd)
rid=${1:-}

if [ -z "$rid" ]; then
    case "$(uname -m)" in
        arm64) rid=osx-arm64 ;;
        x86_64) rid=osx-x64 ;;
        *) echo "Unsupported macOS architecture: $(uname -m)" >&2; exit 2 ;;
    esac
fi

case "$rid" in
    osx-arm64|osx-x64) ;;
    *) echo "Supported values: osx-arm64, osx-x64" >&2; exit 2 ;;
esac

output_dir="$project_root/dist/SurfOS-$rid"
archive_path="$project_root/dist/SurfOS-$rid.tar.gz"

dotnet publish "$project_root/src/SurfOS.Console/SurfOS.csproj" \
    --configuration Release \
    --runtime "$rid" \
    --self-contained true \
    -p:PublishSingleFile=true \
    --output "$output_dir"

chmod +x "$output_dir/SurfOS" "$output_dir/Launch-SurfOS.sh"
tar -C "$project_root/dist" -czf "$archive_path" "SurfOS-$rid"
echo "Created $archive_path"
