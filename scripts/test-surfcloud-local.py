"""Non-destructive SurfCloud repository security checks; no cloud access."""
import json
import pathlib
import re
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]


class SurfCloudLocalChecks(unittest.TestCase):
    def test_no_service_account_key_in_console_tree(self):
        # Report path only; never print key material.
        for path in (ROOT / "src" / "SurfOS.Console").rglob("*.json"):
            if "bin" in path.parts or "obj" in path.parts:
                continue
            try:
                data = json.loads(path.read_text(encoding="utf-8"))
            except (OSError, ValueError):
                continue
            self.assertFalse(data.get("type") == "service_account" and bool(data.get("private_key")),
                             f"Service-account private key in {path.relative_to(ROOT)}")

    def test_firestore_rules_deny_direct_access(self):
        rules = (ROOT / "firestore.rules").read_text(encoding="utf-8")
        grants = re.findall(r"allow\s+[^:;]+:\s*if\s*([^;]+);", rules)
        self.assertTrue(grants, "No Firestore allow statements found")
        self.assertTrue(all(condition.strip() == "false" for condition in grants),
                        "Review Firestore rules: a direct client grant is present")

    def test_seed_manifest_ids_and_hashes(self):
        manifest = json.loads((ROOT / "surfcloud-seed" / "packages.json").read_text(encoding="utf-8"))
        for package in manifest["packages"]:
            with self.subTest(package=package.get("id")):
                self.assertRegex(package["id"], r"^[A-Za-z0-9_.-]{1,80}$")
                self.assertNotIn(package["id"], (".", ".."))
                if package.get("downloadUrl") or package.get("encodedDownloadUrl"):
                    self.assertRegex(package.get("sha256", ""), r"^[A-Fa-f0-9]{64}$")


if __name__ == "__main__":
    unittest.main(verbosity=2)
