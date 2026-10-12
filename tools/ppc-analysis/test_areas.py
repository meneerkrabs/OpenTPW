"""Synthetic tests for areas.py's labelling and report (no game binary needed)."""

import collections
import json
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import areas  # noqa: E402

CONFIG = {
    "weights": {"guests": 5, "interface": 4, "paths": 5},
    "functions": {"0x10F00100": "paths"},
    "files": [["^docs/reverse/PPC-guests\\.md$", "guests"], ["^source/OpenTPW/Hud/", "interface"],
              ["^docs/reverse/PATH-plan\\.md$", "paths"]],
    "weak_files": [["^docs/reverse/UI-MAP\\.md$", "interface"]],
    "classes": [["^Interface", "interface"], [".", "platform"]],
    "libraries": [["^sound shared$", "audio"], [".", "platform"]],
    "stop_strings": "ThingArray",
    "strings": [["peep", "guests"], ["window", "interface"]],
    "approximations": {"WALK": "guests", "UI": "interface"},
}


def facts(address, **kwargs):
    return areas.Facts(address, kwargs.pop("size", 16), **kwargs)


class LabelTests(unittest.TestCase):
    def setUp(self):
        self.labeller = areas.Labeller(CONFIG)

    def test_precedence_override_repository_class_library_strings(self):
        functions = [
            facts(0x10F00100, cited_files=["docs/reverse/PPC-guests.md"]),
            facts(0x10F00200, cited_files=["docs/reverse/PPC-guests.md"], classes=["InterfaceButton"]),
            facts(0x10F00300, classes=["InterfaceButton"], strings=["peep is lost"]),
            facts(0x10F00400, library="sound shared", strings=["peep"]),
            facts(0x10F00500, strings=["peep is lost"]),
        ]
        labels, _ = self.labeller.label(functions)
        self.assertEqual(labels, [("paths", "override"), ("guests", "repository"), ("interface", "class"),
                                  ("audio", "library"), ("guests", "strings")])

    def test_generic_strings_are_ignored(self):
        labels, _ = self.labeller.label([facts(0x10F00100 + 4, strings=["peep in ThingArray"])])
        self.assertEqual(labels, [(areas.UNKNOWN, "unknown")])

    def test_research_notes_outweigh_source_files_and_conflicts_are_counted(self):
        cited = ["source/OpenTPW/Hud/ParkHud.cs", "docs/reverse/PATH-plan.md"]
        labels, conflicts = self.labeller.label([facts(0x10F00200, cited_files=cited)])
        self.assertEqual(labels, [("paths", "repository")])
        self.assertEqual(conflicts, 1)

    def test_an_index_is_used_only_after_strings(self):
        functions = [facts(0x10F00200, cited_files=["docs/reverse/UI-MAP.md"], strings=["peep"]),
                     facts(0x10F00300, cited_files=["docs/reverse/UI-MAP.md"])]
        labels, _ = self.labeller.label(functions)
        self.assertEqual(labels, [("guests", "strings"), ("interface", "index")])

    def test_imports_count_only_non_platform_libraries(self):
        functions = [facts(0x10F00200, called_libraries=["c/c++ shared", "sound shared", "sound shared"]),
                     facts(0x10F00300, called_libraries=["c/c++ shared"])]
        labels, _ = self.labeller.label(functions)
        self.assertEqual(labels, [("audio", "imports"), (areas.UNKNOWN, "unknown")])

    def test_labels_spread_from_callers_to_callees_only(self):
        functions = [
            facts(0x10F00200, strings=["peep"]),          # 0: guests
            facts(0x10F00300, callers=[0]),               # 1: called by 0 -> guests
            facts(0x10F00400, callers=[1]),               # 2: two steps down -> guests
            facts(0x10F00500),                            # 3: calls 0 only (0 lists no caller) -> unknown
        ]
        labels, _ = self.labeller.label(functions)
        self.assertEqual([area for area, _ in labels], ["guests", "guests", "guests", areas.UNKNOWN])
        self.assertEqual(labels[1][1], "propagated")

    def test_majority_ties_are_deterministic(self):
        self.assertEqual(areas.majority(["paths", "guests"]), "guests")
        self.assertIsNone(areas.majority([]))


class StatusAndReportTests(unittest.TestCase):
    def test_status_split(self):
        self.assertEqual(areas.status(["docs/reverse/PATH-plan.md", "source/OpenTPW/World/Level.cs"]), "built")
        self.assertEqual(areas.status(["docs/reverse/PATH-plan.md"]), "researched")
        self.assertEqual(areas.status([]), "uncited")

    def test_data_ranges_need_size_and_no_data_references(self):
        self.assertTrue(areas.is_data_range(113320, {"call", "tailcall"}))
        self.assertFalse(areas.is_data_range(113320, {"call", "string"}))
        self.assertFalse(areas.is_data_range(4096, {"call"}))

    def test_open_approximations_skip_retired_ids_and_count_each_id_once(self):
        import fidelity_register  # noqa: F401  (areas imports it from tools/)

        register = "\n".join([
            "| WALK-001 | scope | rule | x | y |",
            "| WALK-001 | `a.cs:1` | text |",
            "| UI-002 | scope | rule | x | y |",
            "| GATE-003 | scope | rule | x | y |",
            areas_retired_heading(),
            "| UI-009 | retired |",
        ])
        counts = areas.open_approximations(register, CONFIG["approximations"])
        self.assertEqual(counts, collections.Counter({"guests": 1, "interface": 1, areas.UNKNOWN: 1}))

    def test_report_ranks_by_weight_times_open_kilobytes_and_omits_approx_markers(self):
        functions = [facts(0x10F00200, size=4096, cited_files=["docs/reverse/PPC-guests.md"]),
                     facts(0x10F00300, size=2048, cited_files=["source/OpenTPW/Hud/ParkHud.cs"]),
                     facts(0x10F00400, size=8192, strings=["window"])]
        labels, conflicts = areas.Labeller(CONFIG).label(functions)
        text = areas.render(CONFIG, functions, labels, conflicts, collections.Counter({"guests": 2}), {}, [])
        rows = [line for line in text.splitlines() if line.startswith("| guests |") or line.startswith("| interface |")]
        self.assertTrue(rows[0].startswith("| interface |"), "interface: 4 x 8 KB uncited outranks guests: 5 x 4 KB")
        self.assertIn("| guests | 5 | 1 | 4 | 0 | 4 | 0 | 2 | 20 |", text)
        self.assertNotIn("APPROX:", text)
        self.assertEqual(text, areas.render(CONFIG, functions, labels, conflicts, collections.Counter({"guests": 2}), {}, []))

    def test_shipped_configuration_is_valid(self):
        with open(areas.CONFIG, encoding="utf-8") as handle:
            config = json.load(handle)
        labeller = areas.Labeller(config)
        mapped = set(config["weights"])
        for rules in ("files", "weak_files", "classes", "libraries", "strings"):
            for _, area in config[rules]:
                self.assertIn(area, mapped, f"{rules}: {area} has no weight")
        for area in list(config["functions"].values()) + list(config["approximations"].values()):
            self.assertIn(area, mapped)
        self.assertEqual(labeller.direct(facts(0x10084EA4))[0], "paths & construction")


def areas_retired_heading():
    import fidelity_register
    return fidelity_register.RETIRED_HEADING


if __name__ == "__main__":
    unittest.main()
