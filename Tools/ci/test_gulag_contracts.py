"""Offline regressions for the authored Gulag references and faction relations.

Uses stdlib and the selected files' existing block layout, not a general YAML
parser. The full Content.YAMLLinter remains the authoritative semantic check.
"""

import copy
import json
from pathlib import Path
import re
import unittest


ROOT = Path(__file__).resolve().parents[2]
ROLES = "Resources/Prototypes/Civ14/Roles/Jobs"
PRISONERS = {"GulagPrisonerGerman", "GulagPrisonerUkrainian", "GulagPrisonerPolish"}
JOBS = PRISONERS | {"GulagSovietCaptain", "GulagSovietSergeant", "GulagSovietGuard"}


def read(path):
    return (ROOT / path).read_text(encoding="utf-8-sig")


def scalar(block, key):
    match = re.search(rf"(?m)^  {re.escape(key)}: ([^\n]+)$", block)
    if match is None:
        raise AssertionError(f"Missing field {key}")
    return match[1].strip()


def prototypes(text, kind):
    result = {}
    for block in re.split(r"(?m)(?=^- type: )", text):
        if not block.startswith(f"- type: {kind}\n"):
            continue
        identifier = scalar(block, "id")
        if identifier in result:
            raise AssertionError(f"Duplicate {kind} ID: {identifier}")
        result[identifier] = block
    return result


def field_list(block, field):
    match = re.search(rf"(?m)^  {re.escape(field)}:\n((?:    - [^\n]+\n?)*)", block)
    if match is None:
        raise AssertionError(f"Missing list {field}")
    return set(re.findall(r"(?m)^    - (\S+)", match[1]))


def require_references(references, declared):
    missing = set(references) - set(declared)
    if missing:
        raise AssertionError(f"Undefined references: {sorted(missing)}")


def require_relations(factions):
    for identifier in PRISONERS | {"GulagSoviet"}:
        require_references(factions[identifier], factions)
    for prisoner in PRISONERS:
        if prisoner not in factions["GulagSoviet"] or "GulagSoviet" not in factions[prisoner]:
            raise AssertionError(f"Missing reciprocal hostility for {prisoner}")


class GulagContracts(unittest.TestCase):
    def test_authored_job_trackers_resolve_uniquely(self):
        jobs = {}
        for filename in ("gulag_prisoner.yml", "gulag_soviet.yml"):
            jobs.update(prototypes(read(f"{ROLES}/RPTDM/{filename}"), "job"))
        self.assertEqual(set(jobs), JOBS)
        references = {scalar(block, "playTimeTracker") for block in jobs.values()}
        self.assertEqual(references, {f"Job{job}" for job in JOBS})
        declared = {}
        paths = ["Resources/Prototypes/Roles/play_time_trackers.yml", f"{ROLES}/TDM/playtimetrackers.yml"]
        alternate = f"{ROLES}/RPTDM/playtimetrackers.yml"
        if (ROOT / alternate).exists():
            paths.append(alternate)
        for path in paths:
            for identifier in prototypes(read(path), "playTimeTracker"):
                self.assertNotIn(identifier, declared, f"Duplicate tracker: {identifier}")
                declared[identifier] = path
        require_references(references, declared)

    def test_undefined_and_duplicate_trackers_are_rejected(self):
        with self.assertRaisesRegex(AssertionError, "Undefined references"):
            require_references({"JobGulagMissing"}, {f"Job{job}" for job in JOBS})
        duplicate = "- type: playTimeTracker\n  id: JobGulagDuplicate\n" * 2
        with self.assertRaisesRegex(AssertionError, "Duplicate"):
            prototypes(duplicate, "playTimeTracker")

    def test_factions_resolve_and_are_reciprocally_hostile(self):
        blocks = prototypes(read("Resources/Prototypes/ai_factions.yml"), "npcFaction")
        factions = {identifier: field_list(block, "hostile") if "  hostile:" in block else set()
                    for identifier, block in blocks.items()}
        require_relations(factions)
        for source, target in (("GulagSoviet", "GulagPrisonerPolish"),
                               ("GulagPrisonerPolish", "GulagSoviet")):
            with self.subTest(source=source):
                one_way = copy.deepcopy(factions)
                one_way[source].remove(target)
                with self.assertRaisesRegex(AssertionError, "reciprocal"):
                    require_relations(one_way)
        invalid = copy.deepcopy(factions)
        invalid["GulagSoviet"].add("GulagPrisoner")
        with self.assertRaisesRegex(AssertionError, "Undefined references"):
            require_relations(invalid)

    def test_polish_spawn_reference_preserves_the_serialized_marker_id(self):
        markers = prototypes(read("Resources/Prototypes/Civ14/Entities/Markers/jobs.yml"), "entity")
        block = markers["SpawnPointGulagPrisonerPoland"]
        self.assertIn("      faction: GulagPrisonerPolish\n", block)
        self.assertNotIn("      faction: GulagPrisonerPoland\n", block)
        self.assertIn("- proto: SpawnPointGulagPrisonerPoland\n", read("Resources/Maps/civ/tdm/gulag14.yml"))

    def test_localization_keys_resolve_without_changing_the_labels(self):
        text = read("Resources/Prototypes/Civ14/Entities/Structures/Craft/gulag_commissary.yml")
        for kind, identifier, field, path, label in (
                ("material", "Roubles", "unit", "materials/units.ftl", "Rbl"),
                ("latheCategory", "GulagCommissary", "name", "lathe/lathe-categories.ftl", "Gulag Commissary")):
            block = prototypes(text, kind)[identifier]
            reference = scalar(block, field)
            messages = re.findall(r"(?m)^([A-Za-z][A-Za-z0-9_-]*)\s*=\s*(.*)$",
                                  read(f"Resources/Locale/en-US/{path}"))
            self.assertEqual(len(messages), len(dict(messages)), "Duplicate locale key")
            require_references({reference}, dict(messages))
            self.assertEqual(dict(messages)[reference], label)
            with self.assertRaisesRegex(AssertionError, "Undefined references"):
                require_references({label}, dict(messages))

    def test_unsupported_item_state_is_removed_and_world_artwork_is_retained(self):
        text = read("Resources/Prototypes/Civ14/Entities/Structures/Craft/gulag_commissary.yml")
        item = prototypes(text, "entity")["Roubles"]
        item_component = re.search(r"(?m)^    - type: Item\n((?:      [^\n]*\n)*)", item)
        self.assertIsNotNone(item_component)
        self.assertNotRegex(item_component[1], r"(?m)^      state:")
        self.assertIn("      sprite: Civ14/Objects/trash.rsi\n", item_component[1])
        self.assertIn("      state: money\n", item)
        self.assertIn("  price: 1\n", prototypes(text, "material")["Roubles"])
        self.assertIn("    Roubles: 15\n", prototypes(text, "latheRecipe")["GulagCommissaryInventoryNutribrick"])

    def test_railing_variants_select_their_existing_artwork(self):
        blocks = prototypes(read("Resources/Prototypes/Civ14/Entities/Structures/Walls/barricades.yml"), "entity")
        asset = ROOT / "Resources/Textures/Civ14/Objects/railing.rsi"
        states = {entry["name"] for entry in json.loads((asset / "meta.json").read_text())["states"]}
        for variant, state in (("Sandstone", "sandstone"), ("Brick", "brick")):
            with self.subTest(variant=variant):
                block = blocks[f"BarricadeRailing{variant}"]
                self.assertIn("       sprite: Civ14/Objects/railing.rsi\n", block)
                self.assertRegex(block, rf"(?m)^       state: {state}$")
                self.assertIn(state, states)
                self.assertTrue((asset / f"{state}.png").is_file())


if __name__ == "__main__":
    unittest.main()
