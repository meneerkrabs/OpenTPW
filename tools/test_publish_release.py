"""Release publication state transitions; no network or real releases are used."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / 'scripts' / 'publish-release.sh'
ARCHIVES = ['OpenTPW-osx-arm64.tar.gz', 'OpenTPW-win-arm64.zip', 'OpenTPW-win-x64.zip',
            'OpenTPW-win-x86.zip', 'OpenTPW-linux-x64.tar.gz', 'OpenTPW-linux-arm64.tar.gz']
FAKE_GH = r'''#!/usr/bin/env python3
import json,os,pathlib,sys
p=pathlib.Path(os.environ['RELEASE_TEST_STATE'])
state=json.loads(p.read_text())
args=sys.argv[1:]
state['calls'].append(args)
command=args[:2]
code=0
if command==['release','view']:
    if state['release'] is None: code=1
    else: print(json.dumps(state['release']))
elif command==['release','create']:
    state['release']={'isDraft':True,'isPrerelease':False,'assets':[]}
elif command==['release','edit']:
    for a in args:
        if a.startswith('--draft='): state['release']['isDraft']=a.endswith('true')
        if a.startswith('--prerelease='): state['release']['isPrerelease']=a.endswith('true')
elif command==['release','upload']:
    if os.environ.get('RELEASE_TEST_FAIL_UPLOAD'): code=1
    else: state['release']['assets']=[{'name':pathlib.Path(a).name} for a in args if a.endswith(('.zip','.tar.gz','.txt'))]
p.write_text(json.dumps(state))
sys.exit(code)
'''


class ReleasePublicationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.run_git('init', '-q')
        self.run_git('-c', 'user.name=Test', '-c', 'user.email=test@example.invalid', 'commit', '--allow-empty', '-qm', 'fixture')
        self.sha = self.run_git('rev-parse', 'HEAD').stdout.strip()
        self.archives = self.root / 'release'
        self.archives.mkdir()
        for name in ARCHIVES:
            (self.archives / name).write_bytes(b'synthetic package')
        self.bin = self.root / 'bin'
        self.bin.mkdir()
        (self.bin / 'gh').write_text(FAKE_GH)
        (self.bin / 'gh').chmod(0o755)
        self.state = self.root / 'state.json'
        self.state.write_text(json.dumps({'release': None, 'calls': []}))
        self.env = dict(os.environ, PATH=str(self.bin) + os.pathsep + os.environ['PATH'],
                        RELEASE_TEST_STATE=str(self.state), RELEASE_VERSION='v0.7.2',
                        RELEASE_PRERELEASE='false', GITHUB_REPOSITORY='fixture/repo', GITHUB_SHA=self.sha)

    def tearDown(self):
        self.temp.cleanup()

    def run_git(self, *args):
        return subprocess.run(['git', *args], cwd=self.root, capture_output=True, text=True, check=True)

    def publish(self):
        return subprocess.run(['bash', str(SCRIPT), str(self.archives)], cwd=self.root,
                              env=self.env, capture_output=True, text=True)

    def test_all_builds_and_checksums_precede_publication(self):
        result = self.publish()
        self.assertEqual(result.returncode, 0, result.stderr)
        state = json.loads(self.state.read_text())
        self.assertFalse(state['release']['isDraft'])
        self.assertEqual({a['name'] for a in state['release']['assets']}, set(ARCHIVES) | {'SHA256SUMS.txt'})
        commands = [call[:2] for call in state['calls']]
        self.assertLess(commands.index(['release', 'upload']), commands.index(['release', 'edit']))

    def test_upload_failure_leaves_a_draft(self):
        self.env['RELEASE_TEST_FAIL_UPLOAD'] = '1'
        self.assertNotEqual(self.publish().returncode, 0)
        self.assertTrue(json.loads(self.state.read_text())['release']['isDraft'])

    def test_existing_empty_release_is_repaired(self):
        self.run_git('tag', 'v0.7.2')
        self.state.write_text(json.dumps({'release': {'isDraft': False, 'isPrerelease': False, 'assets': []}, 'calls': []}))
        result = self.publish()
        self.assertEqual(result.returncode, 0, result.stderr)
        state = json.loads(self.state.read_text())
        self.assertFalse(state['release']['isDraft'])
        self.assertEqual(len(state['release']['assets']), 7)

    def test_missing_build_never_creates_a_tag_or_release(self):
        (self.archives / ARCHIVES[0]).unlink()
        self.assertNotEqual(self.publish().returncode, 0)
        self.assertEqual(json.loads(self.state.read_text())['calls'], [])

    def test_version_cannot_reuse_a_different_source_commit(self):
        self.run_git('tag', 'v0.7.2')
        self.run_git('-c', 'user.name=Test', '-c', 'user.email=test@example.invalid', 'commit', '--allow-empty', '-qm', 'next')
        self.env['GITHUB_SHA'] = self.run_git('rev-parse', 'HEAD').stdout.strip()
        self.assertNotEqual(self.publish().returncode, 0)
        self.assertEqual(json.loads(self.state.read_text())['calls'], [])


if __name__ == '__main__':
    unittest.main()
