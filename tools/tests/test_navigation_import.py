import importlib.util
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("navigation_import", Path(__file__).resolve().parents[1] / "Import-Navigation.py")
navigation = importlib.util.module_from_spec(spec)
spec.loader.exec_module(navigation)


def records():
    return [
        {"id": 1, "name": "Start", "width": 6, "height": 6, "portals": []},
        {"id": 2, "name": "Bridge", "width": 6, "height": 6,
         "portals": [{"from": {"X": 5, "Y": 2}, "map": 3, "to": {"X": 1, "Y": 2}}]},
        {"id": 3, "name": "Bank", "width": 6, "height": 6, "portals": []},
    ]


def legacy(*destinations):
    return "mappedMaps1.Number = 1;\n" + "\n".join(
        f"mappedMaps1.ConnectedTo.Add({destination}, new Location(4, 2));" for destination in destinations)


class NavigationImportTests(unittest.TestCase):
    def test_duplicate_ids_are_rejected(self):
        entries = records()
        entries.append(entries[0].copy())
        with self.assertRaises(ValueError):
            navigation.normalize_records(entries)

    def test_invalid_dimensions_are_rejected(self):
        entries = records()
        entries[0]["width"] = 0
        with self.assertRaises(ValueError):
            navigation.normalize_records(entries)

    def test_invalid_landing_is_excluded_and_reported(self):
        entries = records()
        entries[1]["portals"][0]["to"]["X"] = 6
        normalized, excluded = navigation.normalize_records(entries)
        self.assertEqual([], normalized[2]["portals"])
        self.assertEqual(1, len(excluded))

    def test_hint_never_fabricates_a_landing(self):
        normalized, _ = navigation.normalize_records(records())
        hints, excluded, _ = navigation.legacy_hints(legacy(2), normalized)
        self.assertEqual(1, len(hints))
        self.assertNotIn("to", hints[0])
        self.assertEqual([], excluded)

    def test_legacy_npc_approach_is_not_imported_as_a_walking_exit(self):
        entries = records()
        entries[0]["npcTransfers"] = [{"Map": 2, "Npc": "Fictional captain", "Anchor": {"X": 3, "Y": 2}}]
        normalized, _ = navigation.normalize_records(entries)
        hints, excluded, _ = navigation.legacy_hints(legacy(2), normalized)
        self.assertEqual([], hints)
        self.assertIn("NPC", excluded[0]["reason"])

    def test_current_portal_wins_over_conflicting_legacy_trigger(self):
        entries = records()
        entries[0]["portals"] = [{"from": {"X": 4, "Y": 2}, "map": 3, "to": {"X": 1, "Y": 2}}]
        normalized, _ = navigation.normalize_records(entries)
        hints, excluded, _ = navigation.legacy_hints(legacy(2), normalized)
        self.assertEqual([], hints)
        self.assertIn("Conflicts", excluded[0]["reason"])

    def test_ambiguous_shared_trigger_is_excluded(self):
        normalized, _ = navigation.normalize_records(records())
        hints, excluded, _ = navigation.legacy_hints(legacy(2, 3), normalized)
        self.assertEqual([], hints)
        self.assertEqual(2, len(excluded))

    def test_existing_world_map_entrance_corroborates_shared_trigger(self):
        entries = records()
        entries[0]["portals"] = [{"from": {"X": 4, "Y": 2}, "map": 3, "to": {"X": 1, "Y": 2}, "worldMap": True}]
        normalized, _ = navigation.normalize_records(entries)
        hints, excluded, _ = navigation.legacy_hints(legacy(2, 3), normalized)
        self.assertTrue(hints[0]["worldMap"])
        self.assertEqual([], excluded)

    def test_missing_endpoint_is_accounted_for_without_dimensions(self):
        normalized, edges = navigation.normalize_records(records())
        hints, excluded, references = navigation.legacy_hints(legacy(99), normalized)
        report = navigation.coverage(normalized, hints, {1, 2, 3}, references, edges, excluded, bank=3)
        missing = next(row for row in report["maps"] if row["id"] == 99)
        self.assertFalse(missing["dimensionsKnown"])
        self.assertFalse(missing["combinedBankTopology"])
        self.assertEqual(4, report["summary"]["referencedMaps"])

    def test_collision_padding_is_validated(self):
        normalized, _ = navigation.normalize_records(records())
        pack = {"schemaVersion": 1, "serverProfile": "datester-7.41", "sotpSha256": "0" * 64,
                "maps": [{"id": 1, "width": 6, "height": 6, "sourceSha256": "0" * 64,
                          "blockedBits": "AAAAAPA="}], "missingMapIds": []}
        with self.assertRaises(ValueError):
            navigation.validate_collision(pack, normalized)


if __name__ == "__main__":
    unittest.main()
