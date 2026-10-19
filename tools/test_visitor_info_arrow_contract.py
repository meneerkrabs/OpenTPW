"""UI-910 source integration guards and derived wrapping checks; not C# execution or portrait fidelity proof."""
from itertools import permutations
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return (ROOT / path).read_text(encoding="utf-8-sig")


def body(text, signature):
    start = text.index("{", text.index(signature))
    depth, end = 1, start + 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[start + 1:end - 1]


class VisitorInfoArrowContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.hud = read("source/OpenTPW/Hud/ParkHud.VisitorInfoWindow.cs")
        cls.locator = read("source/OpenTPW/Hud/ParkHud.LocatorPanel.cs")
        cls.tests = read("source/OpenTPW.Tests/VisitorInfoArrowOrderTests.cs")

    def test_hud_passes_the_unfiltered_roster_and_existing_pool_to_one_shared_projection(self):
        cycle = body(self.hud, "public bool CycleVisitorInfo(")
        self.assertIn("VisitorInfoRules.Cycle( LocatorOrder.VisitorIds( level.Guests?.Guests ?? Array.Empty<Guest>(), level.Guests?.Things ), guest.Id, forward )", cycle)
        for forbidden in (".Where(", "level.Things", "NextVisitor(", "OrderBy", "Allocate(", "OpenVisitorInfoWindow(", "Camera.", "GameAudio.", "OpenLocator("):
            self.assertNotIn(forbidden, cycle)
        self.assertEqual(1, self.locator.count("IEnumerable<int> VisitorIds("))
        self.assertIn("public static IEnumerable<int> VisitorIds(", self.locator)
        self.assertIn("foreach ( var id in VisitorIds( guests, things ) )", self.locator)

    def test_shared_projection_preserves_strict_first_claim_and_pool_free_fallback(self):
        candidates = body(self.locator, "public static IEnumerable<int> VisitorIds(")
        for expected in ("guest.Id > 0", "byId.TryAdd( guest.Id, guest )", "things.UsedNewestFirst()", "slot.Model == SaveThingModel.Guest",
                         "!things.IsQueued( slot.Id )", "!things.IsHeld( slot.Id )", "byId.TryGetValue( slot.Id, out var guest )",
                         "guest.State != GuestState.Gone", "!(things?.IsUsed( guest.Id ) ?? false)", "OrderBy( guest => guest.Id )"):
            self.assertIn(expected, candidates)
        claims = candidates.split("if ( things != null )")[0]
        self.assertNotIn("guest.State", claims, "the first Gone claim must survive before projection")
        for forbidden in ("IsVisible", "IsInPark", "IsFinite", "GuestState.Using", "GuestState.HeldByGuard"):
            self.assertNotIn(forbidden, candidates)

    def test_helper_preserves_supplied_order_and_distinct_host_only_endpoint_fallbacks(self):
        cycle = body(self.hud, "public static int? Cycle(")
        for expected in ("var list = orderedIds.ToArray();", "if ( list.Length == 0 )", "return null;", "Array.IndexOf( list, currentId )",
                         "return forward ? list[0] : list[^1];", "list[index < list.Length - 1 ? index + 1 : 0]", "list[index > 0 ? index - 1 : list.Length - 1]"):
            self.assertIn(expected, cycle)
        for forbidden in ("OrderBy", ".Sort(", ".Reverse(", "Distinct("):
            self.assertNotIn(forbidden, cycle)
        self.assertIn("host fixtures; the HUD separately rejects a missing or Gone selection", self.hud)

    def test_missing_selection_and_singleton_return_before_refresh_or_retarget(self):
        cycle = body(self.hud, "public bool CycleVisitorInfo(")
        guard = "if ( InfoVisitor is not { } guest )\n\t\t\treturn false;"
        unchanged = "if ( next == null || next == guest.Id )\n\t\t\treturn false;"
        self.assertIn(guard, cycle)
        self.assertIn(unchanged, cycle)
        self.assertLess(cycle.index(guard), cycle.index("LocatorOrder.VisitorIds"))
        self.assertLess(cycle.index(unchanged), cycle.index("visitorInfoId = next.Value"))
        self.assertLess(cycle.index("visitorInfoTitleNumber ="), cycle.index("RefreshVisitorInfo();"))
        update = body(self.hud.split("private sealed class VisitorInfoScreen", 1)[1], "public override bool Update(")
        self.assertIn("if ( owner.InfoVisitor == null )", update)
        self.assertIn("owner.CloseVisitorInfoWindow();", update)
        self.assertIn("owner.visitorInfoAge >= VisitorInfoRefreshSeconds", update)

    def test_real_arrow_callbacks_and_full_regression_boundaries_are_present(self):
        self.assertIn('Button( "previousVisitor", "b_arup", () => CycleVisitorInfo( false ) )', self.hud)
        self.assertIn('Button( "nextVisitor", "b_ardown", () => CycleVisitorInfo( true ) )', self.hud)
        for expected in ('"nextVisitor" : "previousVisitor"', ").Clicked!", "Arrow( hud, forward )();", "Cycle( hud, true, 9, 7, 2 )",
                         "Cycle( hud, false, 7, 9, 2 )", "GuestState.Using", "GuestState.HeldByGuard", "GuestState.Queueing", "GuestState.ChangingOut",
                         "GuestState.Gone", "Hold( 2 )", "QueueDestroy( 9 )", "Visitor( 7, GuestState.Gone ), Visitor( 7 )",
                         "Visitor( 0 )", "Visitor( -1 )", "CaptureSnapshot()", "ComputeStateHash()", "RandomState", "CollectionAssert.AreEqual( roster, guests )",
                         "RestoreSnapshot(", "Allocate( SaveThingModel.Guest )", "CloseVisitorInfoWindow()", "Update( hud, 3 )", "Update( hud, 1 )",
                         '"a singleton never invokes the change refresh"', '"missing selection must not silently retarget"'):
            self.assertIn(expected, self.tests)

    def test_derived_wrap_matches_independent_neighbor_links_in_every_small_permutation(self):
        # Arithmetic oracle only. Production behavior is covered by the separately executable C# tests.
        for order in permutations((2, 9, 7, 1)):
            newer = dict(zip(order, order[-1:] + order[:-1]))
            older = dict(zip(order, order[1:] + order[:1]))
            for index, current in enumerate(order):
                self.assertEqual(older[current], order[index + 1 if index < len(order) - 1 else 0])
                self.assertEqual(newer[current], order[index - 1 if index > 0 else len(order) - 1])

    def test_residual_stays_open_with_bounded_ordinary_portrait_and_unchanged_arrow_fallback(self):
        annotation = self.hud.split("// [APPROX:UI-910]", 1)[1].splitlines()[0]
        self.assertIn("portrait frame", annotation)
        self.assertIn("sources stay empty", annotation)
        self.assertIn("host-only fallback", annotation)
        self.assertNotIn("speed 8.0", annotation)
        self.assertIn("new UiSpriteFigure", self.hud)
        self.assertIn("native clone VM timing", annotation)
        self.assertIn('("UI-910",', read("source/OpenTPW/UI/Original/UiApproximations.cs"))

    def test_ci_runs_source_guards_and_existing_full_csharp_suite(self):
        workflow = read(".github/workflows/build.yml")
        self.assertIn('python -m unittest discover -s tools -p "test_visitor_info_arrow_contract.py" -v', workflow)
        self.assertIn('python -m unittest discover -s tools -p "test_locator_visitor_contract.py" -v', workflow)
        self.assertIn("dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj --configuration Release --no-build --nologo", workflow)


if __name__ == "__main__":
    unittest.main()
