"""UI-091 staff-role source integration guards; these do not execute or replace the C# regressions."""
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


class StaffRoleOrderContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.owner = read("source/OpenTPW/Economy/ParkStaff.RoleOrder.cs")
        cls.staff = read("source/OpenTPW/Economy/ParkStaff.cs")
        cls.save = read("source/OpenTPW/Economy/ParkSaveFile.cs")
        cls.importer = read("source/OpenTPW/Economy/OriginalEconomyImport.RoleOrder.cs")
        cls.hud = read("source/OpenTPW/Hud/ParkHud.LocatorPanel.cs")

    def test_role_radio_reads_roster_owner_not_world_snapshots(self):
        dispatch = self.hud.split("private int NextLocatorActor", 1)[1].split("private bool TryLocatorActorPosition", 1)[0]
        self.assertIn("Staff.NextInRole( LocatorModel.StaffTypeOf( role ), current )", dispatch)
        self.assertNotIn("Snapshots", dispatch)
        self.assertNotIn("NextStaff", self.hud)
        successor = body(self.owner, "public int NextInRole(")
        for unsupported in ("CellX", "CellY", "State", "OrderBy", "ThingId", "World."):
            self.assertNotIn(unsupported, successor)

    def test_hire_and_fire_materialize_before_roster_change_and_preserve_survivors(self):
        hire = body(self.staff, "public StaffMember Hire(")
        fire = body(self.staff, "public StaffMember Fire(")
        self.assertLess(hire.index("MaterializeRoleOrder()"), hire.index("members.Add( member )"))
        self.assertLess(fire.index("MaterializeRoleOrder()"), fire.index("members.Remove( member )"))
        self.assertIn("roleOrder![(int)member.Type].Insert( 0, member.Id )", hire)
        self.assertIn("roleOrder![(int)member.Type].Remove( member.Id )", fire)
        self.assertNotIn("roleOrder", body(self.staff, "internal void AttachThings("))
        self.assertIn("roleOrder = null", body(self.staff, "internal void Restore("))

    def test_legacy_reads_are_projections_and_mutation_is_the_only_materializer(self):
        self.assertIn("private void MaterializeRoleOrder() => roleOrder ??= ProjectLegacyRoleOrder()", self.owner)
        capture = self.owner.split("internal int[][] CaptureRoleOrder()", 1)[1].split(";", 1)[0]
        self.assertIn("roleOrder ?? ProjectLegacyRoleOrder()", capture)
        self.assertNotIn("??=", capture)
        projection = body(self.owner, "private List<int>[] ProjectLegacyRoleOrder()")
        self.assertIn("things.UsedNewestFirst()", projection)
        self.assertIn("slot.Model == ThingModelOf( member.Type )", projection)
        self.assertIn("members.Count - 1", projection)
        for mutation in (".Allocate(", ".QueueDestroy(", ".Sort(", "random", "roleOrder ="):
            self.assertNotIn(mutation, projection)

    def test_native_data_is_optional_fully_validated_and_defensively_copied(self):
        self.assertIn("public int[][]? RoleOrder { get; init; }", self.save)
        self.assertIn("RoleOrder = park.Staff.CaptureRoleOrder()", self.save)
        self.assertIn("ParkStaff.ValidateRoleOrder( data.Staff.RoleOrder", self.save)
        self.assertIn("park.Staff.RestoreRoleOrder( data.Staff.RoleOrder )", self.save)
        validation = body(self.owner, "internal static void ValidateRoleOrder(")
        for required in ("BalanceSettings.StaffTypeCount", "ThingPool.Capacity", "ids == null", "seen.Add( id )",
                         "(int)type != role", "seen.Count != roster.Count", "InvalidDataException"):
            self.assertIn(required, validation)
        restore = body(self.owner, "internal void RestoreRoleOrder(")
        self.assertLess(restore.index("ValidateRoleOrder("), restore.index("roleOrder ="))
        self.assertIn("ids.ToList()", restore)
        self.assertIn("ids.ToArray()", self.owner)

    def test_original_chains_validate_completely_before_one_install(self):
        for required in ("variables.FirstHandyman", "variables.FirstMechanic", "variables.FirstEntertainer",
                         "variables.FirstGuard", "variables.FirstResearcher", "saved.RoleNextId", "membersByThing",
                         "!seen.Add( id )", "walked.Count >= save.Staff.Count", "ThingPool.Capacity",
                         "model != expectedModel", "walked.Count != fallback.Length"):
            self.assertIn(required, self.importer)
        self.assertIn("order[role] = failure == null ? walked.ToArray() : fallback", self.importer)
        self.assertEqual(1, self.importer.count("staff.RestoreRoleOrder( order )"))
        self.assertIn("Staff role order fallback for {type}: {failure}", self.importer)
        file_layer = read("source/OpenTPW.Files/Formats/Save/SaveWorld.cs")
        self.assertIn("variables.ThingReferences()", file_layer)
        self.assertIn("target.Model != model", file_layer)

    def test_only_nested_role_order_is_newly_excluded_from_gameplay_hash(self):
        hashing = read("source/OpenTPW/World/WorldStateHash.cs")
        # the digest's canonical text leaves only the staff's role order out, inside the save's staff (WorldStateHash.LeftOut and PartOf)
        self.assertIn("EconomyPart.Staff => name == nameof( ParkSaveFile.StaffData.RoleOrder ),", hashing)
        self.assertIn("(EconomyPart.Save, nameof( ParkSaveFile.Data.Staff )) => EconomyPart.Staff,", hashing)
        top_level = next(line for line in hashing.splitlines() if line.strip().startswith("EconomyPart.Save =>"))
        self.assertNotIn("ParkSaveFile.Data.Staff", top_level)
        self.assertNotIn("ParkSaveFile.StaffData.HaveEverTrained", hashing)
        self.assertIn("public const int SchemaVersion = 21", hashing)
        employee = read("source/OpenTPW/Hud/ParkHud.EmployeeWindow.cs")
        self.assertIn("EmployeeWindowRules.Cycle( StaffMembers, member.Id, forward, (Status.EconomySource as ParkEconomy)?.Staff.Things )", employee)
        self.assertNotIn("NextInRole", employee)

    def test_synthetic_regressions_include_callbacks_imports_saves_and_positive_controls(self):
        owner_tests = read("source/OpenTPW.Tests/StaffRoleOrderTests.cs")
        hud_tests = read("source/OpenTPW.Tests/LocatorStaffRoleOrderTests.cs")
        for required in ("OriginalEconomyImport.Apply(", "roleNext:", "cycle", "orphan", "wrong role", "RestoreRoleOrder(",
                         "JsonSerializer.SerializeToNode", "AttachThings(", "HaveEverTrained", "Happiness", "TrainingBudget"):
            self.assertIn(required, owner_tests)
        for required in ("UiButton", ".Clicked!()", "StaffState.PickedUp", "CloseLocator()", "nextEmployee",
                         "previousEmployee", "camera.Target", "economy.World.State", "WorldStateHash.Compute"):
            self.assertIn(required, hud_tests)


if __name__ == "__main__":
    unittest.main()
