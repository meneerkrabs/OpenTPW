import hashlib
import json
import unittest

from compact_audit import compact, fingerprint


class CompactAuditTests(unittest.TestCase):
    def test_preserves_coverage_partial_results_and_unverified_limits(self):
        partial = {'status': 'partial-reader-result', 'errorType': None, 'parsedEntries': 103, 'declaredEntries': 143}
        rejected = {'status': 'reader-rejected', 'errorType': 'UnsupportedFormat'}
        full = {'schema': 1, 'inventories': [{'hashedRecords': 15007, 'unsupportedOrPartial': [partial] + [rejected] * 20}],
                'intersections': [{'uniqueEqualPayloads': 1, 'allEqualPayloadHashes': [{'sha256': 'abc'}]}],
                'standardSam': {'changes': [{'state': 'added'}], 'limitation': 'First token only'},
                'tpiTrackAndTrainingKeys': {'keyOccurrences': [{'Key': 'Training.Experience'}], 'trackInfoKeys': []},
                'cosStructures': {'allHashes': [{'sha256': 'def'}], 'limitation': 'No track schema'},
                'shpiStructures': {'limitation': 'No proven pixel semantics'}, 'opcodes': [{'limitation': 'No runtime proof'}]}
        raw = json.dumps(full).encode()
        result = compact(full, raw, '/external/audit.json', 'regenerate')
        self.assertEqual(hashlib.sha256(raw).hexdigest(), result['fullAudit']['sha256'])
        self.assertEqual(len(raw), result['fullAudit']['bytes'])
        self.assertEqual(15007, result['inventories'][0]['hashedRecords'])
        diagnostics = result['inventories'][0]['diagnostics']
        self.assertEqual(21, diagnostics['count'])
        self.assertEqual([partial], diagnostics['allPartialReaderResults'])
        self.assertEqual(3, len(next(g for g in diagnostics['groups'] if g['count'] == 20)['examples']))
        self.assertEqual(fingerprint(full['intersections'][0]['allEqualPayloadHashes']), result['intersections'][0]['equalPayloadRecordsSha256'])
        self.assertEqual('No track schema', result['cosStructures']['limitation'])
        self.assertEqual(full['shpiStructures'], result['shpiStructures'])
        self.assertEqual(full['opcodes'], result['opcodes'])
        self.assertNotIn('allEqualPayloadHashes', result['intersections'][0])


if __name__ == '__main__':
    unittest.main()
