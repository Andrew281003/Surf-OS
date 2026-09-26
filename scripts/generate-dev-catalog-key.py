"""Create a local Ed25519 test pair without printing or committing key material."""

import base64
import os
from pathlib import Path

from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric import ed25519


root = Path(__file__).resolve().parents[1] / ".local" / "catalog-dev-keys"
private_path = root / "catalog-dev-private.key"
public_path = root / "catalog-dev-public.pub"
if private_path.exists() or public_path.exists():
    raise SystemExit("Development catalog key files already exist; leaving them untouched.")
root.mkdir(parents=True, exist_ok=True)
private = ed25519.Ed25519PrivateKey.generate()
seed = private.private_bytes(
    encoding=serialization.Encoding.Raw,
    format=serialization.PrivateFormat.Raw,
    encryption_algorithm=serialization.NoEncryption(),
)
public = private.public_key().public_bytes(
    encoding=serialization.Encoding.Raw,
    format=serialization.PublicFormat.Raw,
)
with private_path.open("x", encoding="ascii") as stream:
    stream.write(base64.b64encode(seed).decode("ascii") + "\n")
os.chmod(private_path, 0o600)
with public_path.open("x", encoding="ascii") as stream:
    stream.write(base64.b64encode(public).decode("ascii") + "\n")
print("Created ignored local Ed25519 development key pair in .local/catalog-dev-keys.")
