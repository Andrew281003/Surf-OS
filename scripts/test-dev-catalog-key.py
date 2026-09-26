"""Check the ignored local development Ed25519 pair without showing key material."""

import base64
from pathlib import Path

from cryptography.hazmat.primitives.asymmetric import ed25519
from cryptography.exceptions import InvalidSignature


root = Path(__file__).resolve().parents[1] / ".local" / "catalog-dev-keys"
private_path = root / "catalog-dev-private.key"
public_path = root / "catalog-dev-public.pub"
seed = base64.b64decode(private_path.read_text(encoding="ascii").strip(), validate=True)
public_bytes = base64.b64decode(public_path.read_text(encoding="ascii").strip(), validate=True)
if len(seed) != 32 or len(public_bytes) != 32:
    raise SystemExit("FAIL: development key length")
message = b"SurfCloud development catalog test"
signature = ed25519.Ed25519PrivateKey.from_private_bytes(seed).sign(message)
try:
    ed25519.Ed25519PublicKey.from_public_bytes(public_bytes).verify(signature, message)
    ed25519.Ed25519PublicKey.from_public_bytes(public_bytes).verify(signature, message + b" tampered")
except InvalidSignature:
    print("PASS: ignored development key pair signs and verifies; tampering is rejected")
else:
    raise SystemExit("FAIL: tampered message verified")
