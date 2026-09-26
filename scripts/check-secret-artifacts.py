"""Fail CI when tracked files or release artifacts contain credential material.

Only file paths and finding categories are reported; file contents are never printed.
"""

import pathlib
import re
import subprocess
import sys


ROOT = pathlib.Path(__file__).resolve().parents[1]
SENSITIVE_NAME = re.compile(r"(?:^|/)(?:secrets\.json|[^/]*service[-_]?account[^/]*\.json|[^/]*\.(?:pem|p12|pfx))$", re.I)
PRIVATE_KEY = re.compile(rb"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----")
SERVICE_ACCOUNT = re.compile(rb'"type"\s*:\s*"service_account"')
PRIVATE_KEY_FIELD = re.compile(rb'"private_key"\s*:')


def tracked_files():
    result = subprocess.run(["git", "ls-files", "-z"], cwd=ROOT, check=True, capture_output=True)
    return [ROOT / item.decode("utf-8", "surrogateescape") for item in result.stdout.split(b"\0") if item]


def scan(paths):
    findings = []
    for path in paths:
        if not path.is_file():
            continue
        label = path.relative_to(ROOT).as_posix() if path.is_relative_to(ROOT) else path.name
        if SENSITIVE_NAME.search(label):
            findings.append((label, "credential filename"))
        data = path.read_bytes()
        if PRIVATE_KEY.search(data) or (SERVICE_ACCOUNT.search(data) and PRIVATE_KEY_FIELD.search(data)):
            findings.append((label, "private key material"))
    return findings


def main():
    paths = tracked_files()
    for argument in sys.argv[1:]:
        target = pathlib.Path(argument).resolve()
        if target.is_file():
            paths.append(target)
        elif target.is_dir():
            paths.extend(path for path in target.rglob("*") if path.is_file())
    findings = scan(set(paths))
    for path, category in findings:
        print(f"FAIL {category}: {path}")
    if findings:
        return 1
    print(f"PASS: scanned {len(set(paths))} tracked and artifact files")
    return 0


if __name__ == "__main__":
    sys.exit(main())
