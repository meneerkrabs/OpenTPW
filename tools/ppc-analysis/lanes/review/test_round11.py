"""Round-11 source bindings: committed integration 0d58bd4 (f468f03 + 778ea5d) against origin
e31c804. Everything is read with git from committed objects; nothing needs original assets, a
network mount or an owner's working tree. Tests skip when the commits are not in this clone.
"""
from __future__ import annotations

import re
import subprocess
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[4]
OURS, THEIRS, BASE = '0d58bd4', 'e31c804', '778ea5d'


def git(*args: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(['git', '-C', str(ROOT), *args], capture_output=True, text=True, check=False)


def show(rev: str, path: str) -> str:
    out = git('show', f'{rev}:{path}')
    if out.returncode:
        raise AssertionError(out.stderr)
    return out.stdout


def have(*revs: str) -> bool:
    return all(git('cat-file', '-e', f'{rev}^{{commit}}').returncode == 0 for rev in revs)


@unittest.skipUnless(have(OURS, THEIRS, BASE), 'round-11 commits are not in this clone')
class MergeHazards(unittest.TestCase):
    def merge(self) -> tuple[str, str]:
        out = git('merge-tree', '--write-tree', '--name-only', OURS, THEIRS)
        self.assertEqual(out.returncode, 1, 'the merge is expected to conflict')
        lines = out.stdout.splitlines()
        return lines[0], out.stdout

    def test_only_register_and_setup_wizard_conflict(self):
        _, text = self.merge()
        conflicts = sorted(re.findall(r'CONFLICT \(content\): Merge conflict in (\S+)', text))
        self.assertEqual(conflicts, ['docs/FIDELITY-REGISTER.md', 'source/OpenTPW/Client/Setup/SetupWizard.cs'])

    def test_neither_setup_side_compiles_alone(self):
        tree, _ = self.merge()
        merged = show(tree, 'source/OpenTPW/Client/Setup/SetupWizard.cs')
        outside = re.sub(r'<<<<<<< .*?>>>>>>> [^\n]*\n', '', merged, flags=re.S)
        # The auto-merged enum dropped the CD page ...
        self.assertIn('private enum Page { Welcome, GameFolder, Done }', outside)
        self.assertNotIn('private string cdPath', outside)
        # ... while auto-merged async-inspection lines still use it, so "take theirs" fails ...
        for stale in ('inspectingCd ? cdPath : gamePath', 'if ( inspectingCd ) cdReport = report;',
                      'page == Page.Cd ? cdReport?.Path'):
            self.assertIn(stale, outside)
        # ... and "take ours" reintroduces Page.Cd, which the enum no longer has.
        ours = '\n'.join(re.findall(r'<<<<<<< [^\n]*\n(.*?)=======', merged, flags=re.S))
        self.assertIn('Page.Cd', ours)
        # Theirs also reintroduces in-process Directory.Exists on a dropped path.
        theirs = '\n'.join(re.findall(r'=======\n(.*?)>>>>>>> ', merged, flags=re.S))
        self.assertIn('Directory.Exists( dropped )', theirs)

    def test_game_files_screen_bypasses_bounded_child(self):
        screen = show(THEIRS, 'source/OpenTPW/UI/Original/Options/GameFilesScreen.cs')
        self.assertEqual(screen.count('GameInstallation.Inspect( path )'), 2)
        self.assertIn('Task.Run( () => FolderPicker.Pick(', screen)
        discovery = show(OURS, 'source/OpenTPW/Client/Setup/InstallationDiscovery.cs')
        for api in ('InspectAsync(', 'PickAsync(', 'DefaultTimeout = TimeSpan.FromSeconds( 3 )'):
            self.assertIn(api, discovery)
        self.assertNotEqual(git('cat-file', '-e', f'{BASE}:source/OpenTPW/Client/Setup/InstallationDiscovery.cs').returncode, 0)

    def test_text_focus_gates_park_keys(self):
        flow = show(THEIRS, 'source/OpenTPW/Client/GameFlow.cs')
        reset, hud = flow.index('Input.TextEntryActive = false;'), flow.index('Hud.Update( Context, input )')
        self.assertLess(reset, hud)
        input_cs = show(THEIRS, 'source/OpenTPW/Global/Input.cs')
        self.assertIn('bool isPressed = !TextEntryActive;', input_cs)
        self.assertEqual(input_cs.count('&& !TextEntryActive )'), 4)
        hud_cs = show(THEIRS, 'source/OpenTPW/Hud/ParkHud.cs')
        self.assertIn('public bool Paused => Stack.Screens.Count > 1;', hud_cs)
        self.assertIn('if ( !paused && input.Has( UiKeys.Pause ) )', hud_cs)
        screen = show(THEIRS, 'source/OpenTPW/UI/Original/UiScreen.cs')
        self.assertIn('return true;', screen[screen.index('Input.TextEntryActive = true;'):])

    def test_publish_respects_read_only_visit(self):
        online = show(THEIRS, 'source/OpenTPW/Online/OnlineScreens.cs')
        self.assertIn('() => level != null && !level.IsReadOnlyVisit', online)
        sharing = show(THEIRS, 'source/OpenTPW/Online/ParkSharing.cs')
        self.assertIn('if ( level.IsReadOnlyVisit )', sharing)

    def test_root_ui_models_replace_origin_copy_without_conflict(self):
        widgets = show(THEIRS, 'source/OpenTPW/UI/Original/UiWidgets.cs')
        self.assertIn('public sealed class UiModels', widgets)
        self.assertNotIn('public sealed class UiModels', show(OURS, 'source/OpenTPW/UI/Original/UiWidgets.cs'))
        tree, _ = self.merge()
        self.assertNotIn('public sealed class UiModels', show(tree, 'source/OpenTPW/UI/Original/UiWidgets.cs'))
        self.assertIn('public sealed class UiModels', show(tree, 'source/OpenTPW/UI/Original/UiModels.cs'))


if __name__ == '__main__':
    unittest.main()
