"""Hourly preview release planning; no network, git or real releases are used."""
import unittest

import release_train as rt

HEAD = 'a' * 40
GREEN = [{'status': 'completed', 'conclusion': 'success'}]


def decide(tags, at_head=(), since=3, runs=GREEN, active=()):
    return rt.plan(HEAD, list(tags), list(at_head), since, list(runs), list(active))


class VersionTests(unittest.TestCase):
    def test_first_preview_after_stable(self):
        self.assertEqual(rt.next_preview_version(['v0.8.5', 'v0.8.6']), 'v0.8.7-preview.1')

    def test_preview_number_increments(self):
        tags = ['v0.8.6', 'v0.8.7-preview.1', 'v0.8.7-preview.2']
        self.assertEqual(rt.next_preview_version(tags), 'v0.8.7-preview.3')

    def test_new_stable_restarts_numbering(self):
        tags = ['v0.8.6', 'v0.8.7-preview.4', 'v0.8.7']
        self.assertEqual(rt.next_preview_version(tags), 'v0.8.8-preview.1')

    def test_numeric_not_lexical_order(self):
        tags = ['v0.9.9', 'v0.10.0']
        self.assertEqual(rt.next_preview_version(tags), 'v0.10.1-preview.1')
        self.assertEqual(rt.next_preview_version(['v0.8.6'] + ['v0.8.7-preview.%d' % n for n in (2, 10, 9)]),
                         'v0.8.7-preview.11')

    def test_stable_is_newer_than_its_previews(self):
        self.assertEqual(rt.last_tag(['v0.8.7-preview.3', 'v0.8.7', 'v0.8.6']), 'v0.8.7')

    def test_unknown_tags_are_ignored(self):
        self.assertEqual(rt.last_tag(['v1', 'vnext', 'v0.8.6-rc1', 'v0.8.6']), 'v0.8.6')
        with self.assertRaises(ValueError):
            rt.next_preview_version(['v0.8.7-preview.1'])


class PlanTests(unittest.TestCase):
    def test_releases_when_green_and_new(self):
        result = decide(['v0.8.6'])
        self.assertEqual((result['action'], result['version'], result['commit']), ('release', 'v0.8.7-preview.1', HEAD))

    def test_skips_when_head_is_tagged(self):
        result = decide(['v0.8.6', 'v0.8.7-preview.1'], at_head=['v0.8.7-preview.1'])
        self.assertEqual(result['action'], 'skip')
        self.assertIn('already tagged', result['reason'])

    def test_skips_without_newer_commits(self):
        result = decide(['v0.8.6'], since=0)
        self.assertEqual(result['action'], 'skip')
        self.assertIn('no commit is newer', result['reason'])

    def test_skips_while_ci_runs(self):
        result = decide(['v0.8.6'], runs=[{'status': 'in_progress', 'conclusion': None}])
        self.assertEqual(result['action'], 'skip')
        self.assertIn('not finished', result['reason'])

    def test_skips_without_any_ci_run(self):
        self.assertEqual(decide(['v0.8.6'], runs=[])['action'], 'skip')

    def test_skips_when_ci_failed_even_if_another_run_passed(self):
        runs = GREEN + [{'status': 'completed', 'conclusion': 'failure'}]
        result = decide(['v0.8.6'], runs=runs)
        self.assertEqual(result['action'], 'skip')
        self.assertIn('CI failed', result['reason'])

    def test_cancelled_run_is_not_green(self):
        self.assertEqual(decide(['v0.8.6'], runs=[{'status': 'completed', 'conclusion': 'cancelled'}])['action'], 'skip')

    def test_skips_while_a_release_is_running(self):
        self.assertEqual(decide(['v0.8.6'], active=[{'id': 1}])['action'], 'skip')

    def test_never_plans_a_stable_version(self):
        self.assertIn('-preview.', decide(['v0.8.6'])['version'])


class NotesTests(unittest.TestCase):
    def test_links_fragments_at_the_commit(self):
        notes = rt.release_notes(['docs/progress/2026-10-16-b.md', 'docs/progress/2026-10-16-a.md'], 'o/r', HEAD)
        self.assertLess(notes.index('2026-10-16-a'), notes.index('2026-10-16-b'))
        self.assertIn('(https://github.com/o/r/blob/%s/docs/progress/2026-10-16-a.md)' % HEAD, notes)

    def test_no_fragments_no_notes(self):
        self.assertEqual(rt.release_notes([], 'o/r', HEAD), '')


if __name__ == '__main__':
    unittest.main()
