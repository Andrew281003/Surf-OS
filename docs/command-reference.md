# Command reference

Run `help` inside SurfOS for the runtime-generated command list.

Core groups include Linux-style filesystem commands (`pwd`, `ls`, `cd`, `mkdir`, `touch`, `cat`, `echo`, `head`, `tail`, `grep`, `wc`, `rm`, `cp`, `mv`), process commands (`ps`, `top`, `kill`), services (`service`, `dmesg`), packages (`surf`, `surfos`, and `builder`), backups (`backup`), music (`music`), SurfAI (`surfai`), and SurfCode (`code`).

Filesystem commands accept quoted paths such as `mkdir "My Projects"`. Common examples:

```text
ls -lah [path]
cat -n <file>
echo hello > notes.txt
echo again >> notes.txt
grep -in <pattern> <file>
head -n 5 <file>
tail -n 20 <file>
wc -lwc <file>
rm -rf <path>
cp -rf <source> <destination>
mv -f <source> <destination>
```

`cd..` is accepted as a compact equivalent of `cd ..`.

Filesystem inspection commands:

```text
tree [-a] [path]
find <path> [-name pattern]
stat <path>
du [-h] [path]
df [-h]
```

`tree` hides dot files and Windows hidden entries unless `-a` is supplied.
`find` includes hidden entries and matches basenames with `*` and `?`, ignoring
case; quote patterns and paths containing spaces. `stat` reports host timestamps,
attributes, persisted SurfOS ownership, and octal virtual permissions.
`du` prints one total of apparent file bytes, including hidden and nested files,
not allocated disk blocks. Paths default to the current virtual directory.
Recursive inspection reports an error if it encounters a link, junction, or
inaccessible path; `du` does not print an incomplete total.
`df` shows the shared SurfOS partition from its manifest, excluding system reserves
from available space. Capacity is unavailable without a manifest. Virtual directories
are not counted as separate disks. `du` and `df` use `-h` for readable sizes;
use `--help` or `help <command>` for their help. `--` ends option parsing.

Every top-level command supports `--help` (`-h` also shows help except for `du`
and `df`, where it selects readable sizes), and `help <command>` reads
from the same usage catalog. Non-filesystem examples include:

```text
calc 12.5 '*' 4
ps -a
kill -s TERM 104
ping -c 4 -W 1500 example.com
info --json
sys --json
clock -u
clock --iso
calendar 12 2026
run -v startup.txt
vim notes.txt
mail delete 2
todo clear
alarm disable 1
theme list
dmesg -b
dmesg -c
free
swapon --show
swap status
swap set 85
swap trim
builder new game wave-runner
builder validate wave-runner
builder export wave-runner
```

SurfOS starts disk-backed simulated swap at a 70% logical pressure target. The
swap manager writes small page markers directly to `system/swap/swapfile.sys`
with write-through I/O, while the logical counters represent the larger paged
working set. This makes swap activity visible without intentionally consuming
the same amount of host Windows RAM. `swap set` accepts targets from 0% to 95%.

## Shell sessions and operators

```text
history [clear]
which <command>
alias [name='command']
unalias <name>
env [name]
setenv <name> <value>
unsetenv <name>
sleep <seconds>

alias errors='grep -i error'
setenv LOG errors.log
cat notes.txt | errors > "$LOG"
wc -l < "$LOG"
find /home -name '*.txt' > found.txt
cat missing.txt && echo found || echo missing
ps -r | grep SurfCode
dmesg --errors > errors.log
```

Pipes carry text between commands. `cat`, `grep`, `head`, `tail`, and `wc` accept
pipe input or `< file` when their file operand is omitted (or is `-`). `>` replaces
output and `>>` appends it. Output paths are checked before command execution;
captured output is written only when the command succeeds, preserving existing
files on failure. Diagnostics stay on the terminal. Pipelines are sequential and
buffered, limited to 16 Mi characters per stage; redirected input is limited to
16 MiB. Interactive applications do not accept pipes or redirection.

`&&` runs the next pipeline after success; `||` runs it after failure. They have
equal precedence and evaluate left to right; pipes bind more tightly. A pipeline's
status is its last command's status. `grep` returns failure for no matches.
`$?` expands to the last exit status: 0 means success, 1 means failure, 2 means
invalid arguments, 69 means an unavailable backend, and 127 means unknown command.
External interactive apps may not provide detailed failure status to the shell.

Single quotes preserve literal text. Double quotes permit `$NAME` and `${NAME}`
expansion; quoted operators are ordinary text. Variable values stay single
arguments and cannot introduce shell operators. `HOME`, `PWD`, and `USER` are
read-only shell variables. Other variables, aliases, and the last 1000 commands
belong to the login session and clear on logout/reboot. Shell variables are
separate from Windows environment variables. Aliases expand when a command line
is parsed; define an alias on a separate line before using it. Alias and script
recursion are bounded. Background execution with `&` is not supported.

## System and networking

```text
hostname [new-name --force]
uptime
uname [-a|-r|-m]
version
reboot [--force]
ip [addr|route|status]
netstat [-a]
dns lookup <host>
traceroute <host>
curl <url>
wget <url> [-o file]
network [status|profiles|connect [Private|Public]|disconnect]
devices [list|info <id>]
meminfo
cpuinfo
hwinfo
fsck [path]
```

`hostname` changes only SurfOS's saved name; changes require administrator
authorization and `--force`. `reboot` unwinds the session, stops SurfOS services,
and runs its boot/login sequence again. Neither command restarts or renames Windows.
`uname` and `version` identify the standalone runtime, not an attached Surf Kernel.

Network diagnostics read real host data and label it accordingly. `ip route`
shows default gateways rather than a full routing table. `netstat -a` includes
listeners. `traceroute` uses up to 30 IPv4 ICMP probes; blocked probes appear as
`*`. DNS lookup has a 10-second timeout. HTTP(S) requests have a 30-second timeout,
reject embedded URL credentials, fail on HTTP errors, and stop at 16 MiB.
`curl` decodes UTF-8 text; `wget` preserves binary bytes and writes atomically to
virtual storage. Network-profile changes persist to `options.json`. The Offline
profile and safe mode block shell network requests; host adapters remain unchanged.

`meminfo` distinguishes host-process memory from simulated SurfOS swap. `devices`
lists stable IDs usable with `devices info`. `fsck` checks virtual traversal and
file sizes without repairing storage or claiming to validate NTFS structures.

## Packages and backend availability

```text
validate <builder-project-id|package.surfpkg>
sign <package>
sign verify <package>
publish <build> [--public|--private]
kernel [info|modules|config|verify|boot <os-id>]
lsmod
modinfo <module>
modload <module> --force
modunload <module> --force
mount [list|<device> <path>]
umount <path>
os list
os info <id>
os boot <id>
os stop <id>
os install <package>
os remove <id> --force
sandbox create <name>
sandbox list
sandbox run <name>
sandbox delete <name> --force
ln [-s] <target> <link>
chmod <mode> <path>
chown <user> <path> --force
```

`validate` uses Package Builder validation for project IDs. For exported `.surfpkg`
files it checks the manifest, compatible version, install path, archive structure,
and payload SHA-256 without extracting or executing code. `.sos` validation needs
the external Surf Kernel backend.

`sign` writes `<package>.sig.json` using RSA-SHA256-PSS and a non-exportable key
in the current Windows user's CNG key store, scoped by SurfOS installation and
profile. The private key is not written into SurfOS storage. Verification checks
the supplied public key and prints its fingerprint; a valid signature does not
automatically establish publisher trust. `publish` validates first, then reports
that the SurfCloud upload backend is unavailable; it does not upload anything.

The remaining commands expose these explicit limits:

| Commands | Current behavior |
| --- | --- |
| `kernel info/config` | Describe the standalone runtime and its configuration. |
| `lsmod`, `kernel modules`, `modinfo`, `modload`, `modunload` | Inspect, start, and stop service-backed SurfOS runtime modules; mutations require administrator force. |
| `kernel verify` | Print the runtime checksum; return unavailable because no trusted baseline exists. |
| `kernel boot` | Report the unavailable external hosting bridge. |
| `os list`, `os info surfos` | Show the running standalone SurfOS application. |
| Other `os` actions | Report unavailable Surf Kernel hosting bridge. |
| `mount`, `mount list` | List built-in virtual mappings sharing the installation partition. |
| `mount <device> <path>`, `umount` | Report unavailable dynamic mount backend. |
| `sandbox create/list/delete` | Manage staging folders under `~/sandboxes`; deletion requires administrator force. |
| `sandbox run` | Report unavailable enforced execution isolation. |
| `ln`, `chmod`, `chown` | Create native links and persist virtual mode/owner metadata. File modes without write bits also set the Windows read-only attribute; ownership does not alter Windows ACLs. |

Use `help extended` for the additional command catalog. Backend-dependent commands
return nonzero status without claiming that the requested operation succeeded.
