"""Run: python3 -I test_queue_walk_synthetic.py. Standard library only."""
from pathlib import Path
import runpy
import tempfile
import unittest

HERE = Path(__file__).parent
HARNESS = runpy.run_path(str(HERE / 'queue_walk_synthetic.py'))


class SyntheticQueueWalkTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.model, cls.sha = HARNESS['load_math'](HERE / 'walk_evidence.py')

    def test_loader_excludes_binary_paths(self):
        self.assertTrue(HARNESS['FUNCTIONS'] <= self.model.keys())
        self.assertTrue({'inspect', 'main', 'pef', 'queue', 'load_identified'}.isdisjoint(self.model))

    def test_missing_definitions_fail_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'missing.py'
            path.write_text('ONE = 65536\n')
            with self.assertRaisesRegex(ValueError, 'Missing mathematical definitions'):
                HARNESS['load_math'](path)

    def test_domain_is_unique_simple_cardinal_routes(self):
        routes = list(HARNESS['routes']())
        self.assertEqual(len(routes), 57)
        self.assertEqual(len({tuple(route) for route in routes}), 57)
        self.assertEqual({len(route) - 1 for route in routes}, {0, 1, 2, 3, 25})
        for route in routes:
            self.assertEqual(len(set(route)), len(route))
            for a, b in zip(route, route[1:]):
                self.assertEqual(abs(a[0] - b[0]) + abs(a[1] - b[1]), 1)

    def test_report_is_reproducible_and_accounts_for_all_cases(self):
        first = HARNESS['report'](self.model, self.sha)
        self.assertEqual(first, HARNESS['report'](self.model, self.sha))
        self.assertEqual(first['cases'], 4104)
        self.assertEqual(sum(first['outcomes'].values()), 4104)
        self.assertEqual(first['outcomes'], {'arrived': 4104})
        self.assertEqual(first['max_arrival_updates_by_route_steps'],
                         {'0': 1, '1': 10, '2': 19, '3': 27, '25': 212})

    def test_maximum_witnesses_replay(self):
        result = HARNESS['report'](self.model, self.sha)
        for witness in result['witnesses'].values():
            case = witness['case']
            count, details = self.model['walk'](
                case['start'], case['velocity'], self.model['centre'](case['route'][-1]),
                case['route'], case['speed'], rounding=case['rounding'],
                separation=case['separation'], fences=False, strip=set(case['route']), limit=5000)
            self.assertEqual((count, details), (witness['updates'], witness['details']))

    def test_failure_is_reported_without_increasing_a_bound(self):
        model = dict(self.model)
        model['walk'] = lambda *args, **kwargs: (None, {'reason': 'reroute', 'update': 15})
        result = HARNESS['report'](model, self.sha)
        self.assertEqual(result['outcomes'], {'reroute': 4104})
        self.assertEqual(result['max_arrival_updates_by_route_steps'], {})
        self.assertIsNone(result['witnesses']['reroute']['updates'])


if __name__ == '__main__':
    unittest.main()
