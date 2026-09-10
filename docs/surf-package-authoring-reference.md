# Surf package authoring reference

This reference explains how to make installable SurfOS packages. A program, game, or extension package contains one `.surf` script. A `.surf` script is not a separate programming language: SurfOS reads it one line at a time and executes each non-empty, non-comment line through the normal shell. That means package scripts can use the shell commands and operators documented below.

Use the Package Builder for new packages. It creates the required project structure, checks the manifest and payload, calculates the payload checksum, and exports a validated `.surfpkg` archive.

## Quick start

```text
builder new program hello-surf
# Edit /apps/PackageBuilder/hello-surf/src/main.surf
builder validate hello-surf
builder export hello-surf
publish hello-surf --private
```

For a game, replace `program` with `game`. Available templates are `theme`, `game`, `program`, and `extension`.

The created program project looks like this:

```text
/apps/PackageBuilder/hello-surf/
  package.json       # Builder project manifest
  .surfignore        # Files omitted from a published source snapshot
  README.md
  src/
    main.surf        # Installable script payload
```

## Builder commands

| Command | Purpose |
| --- | --- |
| `builder` | Open the interactive Package Builder. |
| `builder new <type> <id>` | Create a `theme`, `game`, `program`, or `extension` project. |
| `builder list` | List projects in `/apps/PackageBuilder`. |
| `builder validate <id>` | Validate the project manifest and its payload. |
| `builder export <id>` | Create `/apps/PackageBuilder/.exports/<id>-<version>.surfpkg`. |
| `validate <project-id>` | Validate a builder project from the shell. |
| `validate <file.surfpkg>` | Validate an exported archive, including its checksum. |
| `publish <project-id> [--private|--public]` | Validate, export, create a filtered source snapshot, and upload. Private is the default. |
| `publish <file.surfpkg> [--private|--public]` | Upload a previously exported archive; no source snapshot is sent. |
| `sign <file.surfpkg>` | Create a detached local RSA SHA-256 signature. |
| `sign verify <file.surfpkg>` | Verify that detached signature. |

Publishing requires an HTTPS endpoint in `SURFCLOUD_PUBLISH_URL`. `SURFCLOUD_TOKEN` is optional and must be kept outside the project and archive.

## package json

`package.json` is the local builder manifest. The builder creates it and requires the following fields:

```json
{
  "Id": "hello-surf",
  "Name": "Hello Surf",
  "Version": "1.0.0",
  "Author": "Your name",
  "Description": "A short description.",
  "Template": "Program",
  "EntryFile": "src/main.surf"
}
```

| Field | Rules |
| --- | --- |
| `Id` | Must match the project directory. Up to 80 characters: letters, numbers, `.`, `-`, and `_` only. |
| `Name` | Required display name. |
| `Version` | Required numeric .NET version such as `1.0.0`. Increase it for updates. |
| `Author` | Required author name. |
| `Description` | Package description. |
| `Template` | `Theme`, `Game`, `Program`, or `Extension`. |
| `EntryFile` | Existing, relative path inside the project. The payload must be 4 MB or smaller. |

For program, game, and extension templates, `EntryFile` normally is `src/main.surf`. For a theme it is `src/<id>.theme.json`.

## Exported archive format

A `.surfpkg` is a ZIP archive with exactly two entries:

```text
manifest.json
payload/<payload-file>
```

The builder writes `manifest.json` automatically. It includes the package ID, name, version, author, description, category, install path, launch command, minimum SurfOS version, and SHA-256 of the payload. The launcher for a script package is:

```text
run /apps/<id>/<id>.surf
```

Script packages install only under `apps/<id>/`; themes install under `Packages/`. The archive validator rejects paths outside those locations, archives with additional files, a payload that does not match `InstallPath`, incompatible SurfOS versions, and invalid or mismatched checksums.

## .surf script rules

Each line is a command. Empty lines and lines whose trimmed text starts with `#` are ignored.

```surf
# Greeting example
echo "Welcome to Hello Surf"
mkdir -p /projects/hello-surf
echo "Created by $USER" > /projects/hello-surf/readme.txt
cat /projects/hello-surf/readme.txt
```

Use `run [-v] <script-file>` to execute a script. `-v` prints each executed line. Scripts are limited to 32 nested shell executions; a script can use `run` again, but should not recurse.

There are no `.surf`-specific declarations, functions, variables, `if` statements, loops, labels, or `goto`. Build behavior from shell commands, `&&`/`||`, files, and scripts. A nonzero command status is available as `$?`.

### Shell features

| Feature | Syntax | Notes |
| --- | --- | --- |
| Comments | `# text` | Only whole-line comments are skipped by `run`. |
| Quotes | `echo "two words"` or `echo 'two words'` | Use quotes for arguments containing spaces. |
| Escape | `\ `, `\"` | Backslash escapes the next character except inside single quotes. |
| Variables | `setenv NAME value`, then `$NAME` or `${NAME}` | Session-only variables. `HOME`, `PWD`, and `USER` are read-only. |
| Exit status | `$?` | Exit status of the preceding command. |
| Alias | `alias hi='echo hello'` | Aliases are session-only; use `unalias hi` to remove one. |
| Pipe | `command1 | command2` | Pass text output to the next command. |
| Input redirect | `command < file` | Reads text from a virtual file. |
| Output redirect | `command > file` | Overwrites a virtual file. |
| Append redirect | `command >> file` | Appends to a virtual file. |
| On success | `command1 && command2` | Runs the second command only if the first succeeds. |
| On failure | `command1 || command2` | Runs the second command only if the first fails. |

Interactive commands cannot be piped or redirected. In particular, avoid using full-screen tools, package publishing, `run`, the builder, shutdown, logout, or the store in a non-interactive app flow.

## Script commands

All commands below are available to a `.surf` file. Commands that alter accounts, package state, or system configuration should be used only when that side effect is the package's explicit purpose. Commands ending in `--force` require an administrator session.

### Files and text

| Command | Syntax |
| --- | --- |
| Current directory | `pwd` |
| List files | `ls [-alh] [path]` |
| Change directory | `cd [path]` |
| Directory tree | `tree [-a] [path]` |
| Find paths | `find <path> [-name pattern]` |
| File metadata | `stat <path>` |
| Disk usage | `du [-h] [path]` and `df [-h]` |
| Create directories | `mkdir [-p] <directory>...` |
| Create or update a file | `touch <file>...` |
| Read files | `cat [-n] <file>...` |
| Print or write text | `echo [text...] [> file | >> file]` |
| Read beginning or end | `head [-n count] <file>` and `tail [-n count] <file>` |
| Search text | `grep [-in] <pattern> <file>` |
| Count text | `wc [-lwc] <file>` |
| Copy | `cp [-rf] <source> <destination>` |
| Move or rename | `mv [-f] <source> <destination>` |
| Delete | `rm [-rf] <path>...` and `rmdir <directory>` |
| Create link | `ln [-s] <target> <link>` |
| Change virtual mode | `chmod <mode> <path>` |
| Change virtual owner | `chown <user> <path> --force` |
| Check virtual files | `fsck [path]` |
| Edit text | `edit <file.txt>` or `vim <file>` |
| Open code editor | `code [workspace]` |

### Shell and script utilities

| Command | Syntax |
| --- | --- |
| Help | `help [section|command]` |
| Command history | `history [clear]` |
| Resolve command | `which <command>` |
| Aliases | `alias [name='command']` and `unalias <name>` |
| List variable values | `env [name]` |
| Set or remove variable | `setenv <name> <value>` and `unsetenv <name>` |
| Pause | `sleep <seconds>` — 0 to 86400, decimals accepted |
| Execute a script | `run [-v] <script_file>` |
| Clear display | `clear [-d seconds]` or `clearT <seconds>` |
| Terminal animation | `anim [status|on|off]` |
| Font size | `fontsize <number>` |
| Calculator | `calc [<number> <operator> <number>]` |

### Packages applications and themes

| Command | Syntax |
| --- | --- |
| Package browser | `store` |
| Search packages | `surf search [name]` |
| Install package | `surf install <package>` |
| Remove package | `surf remove <package>` |
| List installed packages | `surf list` |
| Refresh and inspect updates | `surf update` or `surf updates` |
| Package details | `surf info <package>` |
| List or launch apps | `app --list`, `app --open <name>`, `app --config` |
| List or play games | `game [list|play <name>]` |
| Themes | `theme list`, `theme load <name>`, `theme default <name>`, `theme remove <name>`, `theme unload`, `theme link` |
| SurfCode IDE | `code [workspace]` |

### System account and diagnostics

| Command | Syntax |
| --- | --- |
| Current profile | `whoami [-u|--id]` |
| Installation details | `info [--json]` |
| Runtime information | `sys [--json]`, `uptime`, `uname [-a|-r|-m]`, `version` |
| CPU and memory | `cpuinfo`, `meminfo`, `hwinfo`, `free [--bytes]` |
| Swap | `swapon --show`, `swap [status|set <0-95>|trim]` |
| Processes | `ps [-a] [-r]`, `top`, `kill [-s TERM] <pid>` |
| Services | `service [list|status <name>|start <name>|stop <name>|restart <name>]` |
| Kernel messages | `dmesg [all|boot|-b|errors|--errors|clear|-c]` |
| Service modules | `lsmod`, `modinfo <module>`, `modload <module> --force`, `modunload <module> --force` |
| Devices and mappings | `devices [list|info <id>]`, `mount [list|<device> <path>]`, `umount <path>` |
| Host name | `hostname [new-name --force]` |
| User accounts | `user list`, `user add <name> <password> --force`, `user remove <name> --force` |
| Backups | `backup <name>` and `backup list` |
| Health check | `check --system` or `check --app <name>` |

`mount`, `umount`, several `kernel`/`os` actions, and sandbox execution can report that their underlying backend is unavailable. Treat them as diagnostics, not package dependencies.

### Network media productivity and AI

| Command | Syntax |
| --- | --- |
| Network policy | `network [status|profiles|connect [Private|Public]|disconnect]` |
| Network inspection | `ip [addr|route|status]`, `netstat [-a]`, `dns lookup <host>`, `traceroute <host>` |
| HTTP text | `curl <url>` |
| Download file | `wget <url> [-o file]` |
| Ping | `ping [-c count] [-W timeout_ms] <address>` |
| Mail | `mail [list]`, `mail send <user> <message>`, `mail delete <id>`, `mail clear` |
| Music | `music [open|list|cloud|play <file>|pause|stop|download <song>|playlist ...]` |
| Calendar and clock | `clock [-u|--iso]`, `calendar [month] [year]` |
| Alarms | `alarm [list|add <HH:mm> [message]|remove <id>|enable <id>|disable <id>]` |
| To-do list | `todo [list|add <text>|complete <id>|remove <id>|clear]` |
| SurfAI | `surfai [question]` or `ai [question]` |

### Advanced and session-ending commands

| Command | Syntax |
| --- | --- |
| Kernel inspection | `kernel [info|modules|config|verify|boot <os-id>]` |
| OS management | `os list`, `os info <id>`, `os boot <id>`, `os stop <id>`, `os install <package>`, `os remove <id> --force` |
| Sandbox staging | `sandbox create <name>`, `sandbox list`, `sandbox run <name>`, `sandbox delete <name> --force` |
| Log out | `logout` |
| Restart | `reboot [--force]` |
| Shut down | `shutdown` or `exit` |
| Remove installation | `uninstall [--force]` |

Do not place the session-ending or uninstall commands in an ordinary package script. A script stops when logout or reboot ends the current shell.

## Source snapshot filtering

When publishing a builder project, SurfOS also uploads a source snapshot. `.surfignore` controls this snapshot only; it cannot exclude the `EntryFile`, because that file must be packaged.

The syntax is gitignore-like:

```text
# comment and blank lines are ignored
bin/
obj/
*.pdb
.env
secrets.json
!src/keep-this-file.txt
```

Patterns are case-insensitive. A pattern without `/` matches any path segment; patterns ending in `/` match the directory and everything beneath it. `*` and `?` are supported, and a leading `!` re-includes a later match. The uncompressed source snapshot may not exceed 32 MB.

## Author checklist

1. Use a valid, stable package ID and increase `Version` for every update.
2. Keep the entry payload at or below 4 MB.
3. Use absolute virtual paths such as `/apps/<id>/data.txt` when the package must not depend on the user's current directory.
4. Quote paths and messages with spaces.
5. Test with `run -v src/main.surf` where appropriate, then run `builder validate <id>`.
6. Export with `builder export <id>` and validate the resulting archive with `validate <file.surfpkg>`.
7. Keep tokens, API keys, `.env` files, build output, and local settings out of the project or exclude them with `.surfignore`.
