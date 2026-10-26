"""Offline tests of the runtime probe oracle; these do not run the renderer."""

import base64
from pathlib import Path
import tempfile
import unittest

from check_map_renderer import check_missing_resource, check_success, MAP_ID, MISSING_RSI, PROTOTYPE, ROOT


class MapRendererOracleTests(unittest.TestCase):
    def test_missing_resource_requires_nonzero_exit_and_specific_diagnostic(self):
        diagnostic = f"Unable to load RSI '/Textures/{MISSING_RSI}'"
        check_missing_resource(1, diagnostic)
        with self.assertRaises(AssertionError):
            check_missing_resource(0, diagnostic)
        with self.assertRaises(AssertionError):
            check_missing_resource(1, "unrelated compilation or startup failure")

    def test_success_requires_zero_exit_and_nonempty_png(self):
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary)
            image = output / MAP_ID / f"{MAP_ID}-0.png"
            with self.assertRaises(FileNotFoundError):
                check_success(0, output)
            image.parent.mkdir()
            image.write_bytes(base64.b64decode(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/lXcAAAAASUVORK5CYII="))
            check_success(0, output)
            with self.assertRaises(AssertionError):
                check_success(1, output)
            image.write_bytes(b"not a PNG")
            with self.assertRaises(AssertionError):
                check_success(0, output)

    def test_authored_railing_references_match_the_tracked_asset_case(self):
        authored = PROTOTYPE.read_text(encoding="utf-8")
        self.assertEqual(authored.count("Civ14/Objects/railing.rsi"), 4)
        self.assertNotIn("Civ14/Objects/Railing.rsi", authored)
        # Inspect each path component's spelling even on case-insensitive hosts.
        current = ROOT / "Resources/Textures"
        for component in ("Civ14", "Objects", "railing.rsi", "meta.json"):
            self.assertIn(component, [entry.name for entry in current.iterdir()])
            current /= component

    def test_runtime_target_matches_the_current_integration_map_contract(self):
        pool = (ROOT / "Content.IntegrationTests/PoolManager.cs").read_text(encoding="utf-8-sig")
        self.assertIn(f'public const string TestMap = "{MAP_ID}";', pool)
        prototype = (ROOT / "Resources/Prototypes/Maps/civ/nomads.yml").read_text(encoding="utf-8")
        self.assertIn(f"  id: {MAP_ID}\n", prototype)
        self.assertIn("  mapPath: /Maps/civ/nomads_classic.yml", prototype)
        self.assertTrue((ROOT / "Resources/Maps/civ/nomads_classic.yml").is_file())


if __name__ == "__main__":
    unittest.main()
