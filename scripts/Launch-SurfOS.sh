#!/bin/sh
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)

if [ -x "$script_dir/SurfOS" ]; then
    exec "$script_dir/SurfOS"
fi

if [ -f "$script_dir/SurfOS.dll" ]; then
    exec dotnet "$script_dir/SurfOS.dll"
fi

project_path="$script_dir/../src/SurfOS.Console/SurfOS.csproj"
if [ -f "$project_path" ]; then
    exec dotnet run --project "$project_path"
fi

echo "SurfOS executable or project could not be found." >&2
exit 1
