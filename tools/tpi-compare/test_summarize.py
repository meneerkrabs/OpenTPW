import unittest

import summarize as summary


class SummaryTests(unittest.TestCase):
    def test_common_paths_do_not_claim_renamed_assets_are_different(self):
        before = {'baselineLabel': 'before', 'files': [{'path': 'Data/A', 'sha256': 'one'}, {'path': 'Data/old', 'sha256': 'same'}]}
        after = {'baselineLabel': 'after', 'files': [{'path': 'data/a', 'sha256': 'one'}, {'path': 'Data/new', 'sha256': 'same'}]}
        result = summary.same_paths(before, after)
        self.assertEqual((result['commonPaths'], result['equalHashes'], result['changedHashes']), (1, 1, 0))

    def test_limits_and_reader_rejections_remain_separate(self):
        report = {'baselineLabel': 'fixture', 'fileCount': 3, 'signatureCounts': {}, 'limits': {}, 'files': [
            {'probeFamily': 'MD2', 'readerResult': {'status': 'reader-rejected'}},
            {'probeFamily': 'MD2', 'readerResult': {'status': 'unverified-limit'}},
            {'probeFamily': 'WAD', 'readerResult': {'status': 'parsed-existing-reader', 'sampledMembers': [
                {'signature': 'M3D2', 'readerResult': {'status': 'parsed-existing-reader'}}]}}]}
        result = summary.outcomes(report)
        self.assertEqual(len(result['physicalReaderOutcomes']), 3)
        self.assertEqual(result['memberReaderOutcomes'][0]['count'], 1)


if __name__ == '__main__':
    unittest.main()
