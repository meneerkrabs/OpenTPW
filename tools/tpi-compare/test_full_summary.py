import unittest

import full_summary as summary


class FullSummaryTests(unittest.TestCase):
    def test_renamed_and_duplicate_payloads_match_by_hash(self):
        before = {'baselineLabel': 'old', 'files': [{'path': 'old.wad', 'sha256': 'container', 'readerResult': {'sampledMembers': [
            {'path': 'same.rse', 'sha256': 'payload'}, {'path': 'duplicate.rse', 'sha256': 'payload'}]}}]}
        after = {'baselineLabel': 'new', 'files': [{'path': 'renamed.wad', 'sha256': 'different', 'readerResult': {'sampledMembers': [
            {'path': 'renamed.rse', 'sha256': 'payload'}]}}]}
        result = summary.intersection(before, after)
        self.assertEqual(result['uniqueEqualPayloads'], 1)
        self.assertEqual(result['equalBeforeOccurrences'], 2)
        self.assertEqual(result['equalAfterOccurrences'], 1)
        self.assertEqual(result['commonLogicalPaths'], 0)

    def test_hooked_opcode_is_not_claimed_as_full_gameplay_support(self):
        report = {'baselineLabel': 'fixture', 'files': [{'path': 'one.rse', 'sha256': 'hash', 'readerResult': {
            'reader': 'RideScriptFile', 'status': 'parsed-existing-reader', 'opcodeCounts': {'42': 3, '600': 1}}}]}
        result = summary.scripts(report, {42: 'HUSH'}, {'HUSH': 'Hooked'})
        self.assertEqual(result['unknownEnumIds'], [600])
        self.assertEqual(result['noRegisteredHandlerIds'], [600])
        self.assertEqual(result['opcodeCounts'][0]['vmStatus'], 'Hooked')


if __name__ == '__main__':
    unittest.main()
