"""Hourly release planning; no network, git or real releases are used."""
from pathlib import Path
import sys
import io
import json
import os
import shutil
import subprocess
import tempfile
from unittest.mock import patch
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parent))
import release_train as rt

HEAD = 'a' * 40
GREEN = [{'status': 'completed', 'conclusion': 'success'}]


def decide(tags, at_head=(), since=3, runs=GREEN, active=()):
    return rt.plan(HEAD, list(tags), list(at_head), since, list(runs), list(active))


def run_workflow_shell(script, env):
    # Windows searches System32 before PATH for bare names; use the discovered Bash.
    bash = shutil.which('bash')
    if bash is None:
        raise FileNotFoundError('bash is required for workflow shell tests')
    return subprocess.run([str(Path(bash).resolve()), '-e', '-c', script],
                          env=env, capture_output=True, text=True)


class WorkflowShellLauncherTests(unittest.TestCase):
    def test_uses_the_discovered_absolute_path_instead_of_a_bare_bash_name(self):
        bash = Path('mock shell') / 'bash'
        script = 'printf shell-test'
        env = {'RUNNER_TEMP': 'mock-temp'}
        result = subprocess.CompletedProcess([], 0, stdout='shell-test', stderr='')
        with patch('shutil.which', return_value=str(bash)) as which, \
             patch('subprocess.run', return_value=result) as run:
            self.assertIs(run_workflow_shell(script, env), result)
        which.assert_called_once_with('bash')
        run.assert_called_once_with([str(bash.resolve()), '-e', '-c', script],
                                    env=env, capture_output=True, text=True)

    def test_missing_bash_does_not_fall_back_to_bare_name_lookup(self):
        with patch('shutil.which', return_value=None), patch('subprocess.run') as run:
            with self.assertRaises(FileNotFoundError):
                run_workflow_shell('exit 0', {})
        run.assert_not_called()


class VersionTests(unittest.TestCase):
    def test_next_patch_after_newest_stable(self):
        self.assertEqual(rt.next_version(['v0.8.5', 'v0.8.6']), 'v0.8.7')
        self.assertEqual(rt.next_version(['v0.8.7', 'v0.8.6']), 'v0.8.8')

    def test_numeric_not_lexical_order(self):
        self.assertEqual(rt.next_version(['v0.9.9', 'v0.10.0']), 'v0.10.1')

    def test_preview_tags_are_ignored(self):
        tags = ['v0.8.6', 'v0.8.7-preview.1', 'v0.9.0-preview.2']
        self.assertEqual(rt.last_tag(tags), 'v0.8.6')
        self.assertEqual(rt.next_version(tags), 'v0.8.7')

    def test_unknown_tags_are_ignored(self):
        self.assertEqual(rt.last_tag(['v1', 'vnext', 'v0.8.6-rc1', 'v0.8.6']), 'v0.8.6')
        with self.assertRaises(ValueError):
            rt.next_version(['v0.8.7-preview.1'])

    def test_result_is_always_stable_and_new(self):
        tags = ['v0.8.6', 'v0.8.7', 'v0.8.8-preview.1']
        version = rt.next_version(tags)
        self.assertNotIn(version, tags)
        self.assertIsNotNone(rt.parse_tag(version))


class PlanTests(unittest.TestCase):
    def test_releases_when_green_and_new(self):
        result = decide(['v0.8.6'])
        self.assertEqual((result['action'], result['version'], result['commit']), ('release', 'v0.8.7', HEAD))

    def test_skips_when_head_is_tagged(self):
        result = decide(['v0.8.6', 'v0.8.7'], at_head=['v0.8.7'])
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

    def test_retries_an_unfinished_newest_tag_on_its_own_commit(self):
        other = 'b' * 40
        result = rt.plan(HEAD, ['v0.8.6', 'v0.8.7'], [], 5, [], [], {'v0.8.7': other}, GREEN)
        self.assertEqual((result['action'], result['version'], result['commit']), ('release', 'v0.8.7', other))

    def test_does_not_retry_while_a_release_runs(self):
        result = rt.plan(HEAD, ['v0.8.7'], [], 5, GREEN, [{'id': 1}], {'v0.8.7': HEAD})
        self.assertEqual(result['action'], 'skip')

    def test_published_tag_at_head_is_skipped(self):
        self.assertEqual(rt.plan(HEAD, ['v0.8.6', 'v0.8.7'], ['v0.8.7'], 0, GREEN, [], {})['action'], 'skip')

    def test_skips_while_a_release_is_running(self):
        self.assertEqual(decide(['v0.8.6'], active=[{'id': 1}])['action'], 'skip')

    def test_follows_stable_numbering(self):
        self.assertEqual(decide(['v0.8.6', 'v0.8.7'], since=2)['version'], 'v0.8.8')


class NotesTests(unittest.TestCase):
    def test_links_fragments_at_the_commit(self):
        notes = rt.release_notes(['docs/progress/2026-10-16-b.md', 'docs/progress/2026-10-16-a.md'], 'o/r', HEAD)
        self.assertLess(notes.index('2026-10-16-a'), notes.index('2026-10-16-b'))
        self.assertIn('(https://github.com/o/r/blob/%s/docs/progress/2026-10-16-a.md)' % HEAD, notes)

    def test_no_fragments_no_notes(self):
        self.assertEqual(rt.release_notes([], 'o/r', HEAD), '')


class RetryGateTests(unittest.TestCase):
    def test_unfinished_tag_requires_its_own_green_ci(self):
        for runs in ([], [{'status': 'queued'}],
                     [{'status': 'completed', 'conclusion': 'failure'}],
                     [{'status': 'completed', 'conclusion': 'cancelled'}]):
            with self.subTest(runs=runs):
                result = rt.plan(HEAD, ['v0.8.7'], [], 3, GREEN, [],
                                 {'v0.8.7': 'b' * 40}, runs)
                self.assertEqual(result['action'], 'skip')

    def test_repeated_event_while_active_or_published_starts_nothing(self):
        self.assertEqual(decide(['v0.8.6'])['action'], 'release')
        for status in ('queued', 'in_progress', 'waiting', 'pending', 'requested'):
            self.assertEqual(decide(['v0.8.6'], active=[{'status': status}])['action'], 'skip')
        self.assertEqual(decide(['v0.8.6', 'v0.8.7'], at_head=['v0.8.7'])['action'], 'skip')

    def test_pending_run_is_not_hidden_by_success(self):
        self.assertEqual(decide(['v0.8.6'], runs=GREEN + [{'status': 'queued'}])['action'], 'skip')


class GatherTests(unittest.TestCase):
    def test_paginates_run_queries(self):
        pages = ' {"workflow_runs": [{"id": 1}]}\n{"workflow_runs": [{"id": 2}]}\n'
        with patch.object(rt, 'sh', return_value=pages) as shell:
            self.assertEqual(rt.workflow_runs('o/r', 'build.yml', head_sha=HEAD), [{'id': 1}, {'id': 2}])
            args = shell.call_args.args
            self.assertIn('--paginate', args)
            self.assertNotIn('--slurp', args)
            self.assertNotIn('--jq', args)
            self.assertIn('head_sha=' + HEAD, args)

    def test_later_page_failure_or_pending_run_blocks_green_first_page(self):
        for later in ({'status': 'completed', 'conclusion': 'failure'}, {'status': 'queued'}):
            pages = json.dumps({'workflow_runs': GREEN}) + '\n' + json.dumps({'workflow_runs': [later]})
            with self.subTest(later=later), patch.object(rt, 'sh', return_value=pages):
                runs = rt.workflow_runs('o/r', 'build.yml', head_sha=HEAD)
                self.assertEqual(decide(['v0.8.6'], runs=runs)['action'], 'skip')

    def test_retry_queries_tag_commit_and_all_active_states(self):
        tagged = 'b' * 40
        def git(*args):
            return {('git', 'tag', '--list', 'v*'): 'v0.8.7',
                    ('git', 'tag', '--points-at', HEAD): '',
                    ('git', 'rev-list', '--count', 'v0.8.7..' + HEAD): '3',
                    ('git', 'rev-list', '-n', '1', 'v0.8.7'): tagged}[args]
        with patch.object(rt, 'sh', side_effect=git), \
             patch.object(rt, 'release_is_unfinished', return_value=True), \
             patch.object(rt, 'workflow_runs', return_value=GREEN) as runs:
            facts = rt.gather('o/r', HEAD)
        self.assertEqual(facts[-1], GREEN)
        self.assertEqual(facts[-2], {'v0.8.7': tagged})
        self.assertEqual(facts[5], [])  # Do not append unrelated newer-main notes to a retry.
        self.assertIn(unittest.mock.call('o/r', 'build.yml', head_sha=tagged,
                                         event='push', branch='main'), runs.call_args_list)
        for status in ('queued', 'in_progress', 'waiting', 'pending', 'requested'):
            self.assertIn(unittest.mock.call('o/r', 'release.yml', status=status), runs.call_args_list)

    def test_explicit_fresh_head_is_used_for_planning(self):
        fresh = 'b' * 40
        with patch.object(rt, 'gather', return_value=(['v0.8.6'], [], 3, GREEN, [], [], {}, [])) as gather, \
             patch('sys.stdout', new_callable=io.StringIO) as output:
            rt.main(['--repository', 'o/r', '--head', fresh])
        gather.assert_called_once_with('o/r', fresh)
        self.assertEqual(json.loads(output.getvalue())['commit'], fresh)


class WorkflowTests(unittest.TestCase):
    path = Path(__file__).resolve().parents[1] / '.github/workflows/release-train.yml'

    def test_trigger_and_dry_run_contract(self):
        workflow = self.path.read_text()
        self.assertIn('workflows: [Cross-platform build and CPU tests]', workflow)
        self.assertIn('types: [completed]', workflow)
        self.assertIn('branches: [main]', workflow)
        self.assertIn("github.event.workflow_run.event == 'push'", workflow)
        self.assertIn('github.event.workflow_run.head_repository.full_name == github.repository', workflow)
        self.assertIn("github.event.workflow_run.conclusion == 'success'", workflow)
        self.assertIn("cron: '17 * * * *'", workflow)
        self.assertIn('default: true', workflow)
        self.assertIn("steps.plan.outputs.action == 'release' && inputs.dry_run != true", workflow)
        self.assertIn('group: release-train', workflow)
        self.assertIn('cancel-in-progress: false', workflow)
        self.assertIn('ref: main', workflow)  # Never execute a triggering PR checkout with a write token.
        release = self.path.with_name('release.yml').read_text()
        self.assertIn('group: release-${{ inputs.version || github.ref_name }}', release)

    def test_green_main_skipped_during_release_catches_up_once_after_completion(self):
        tags = ['v0.8.6']
        self.assertEqual(decide(tags, active=[{'status': 'in_progress'}])['action'], 'skip')
        # Release A finishes on an older commit; its completion wakes the same planner for green main B.
        tags.append('v0.8.7')
        result = decide(tags)
        self.assertEqual((result['action'], result['version'], result['commit']), ('release', 'v0.8.8', HEAD))
        self.assertEqual(decide(tags, active=[{'status': 'queued'}])['action'], 'skip')
        tags.append('v0.8.8')
        self.assertEqual(decide(tags, at_head=['v0.8.8'])['action'], 'skip')
        # Completion never substitutes for green main, and another active release still blocks it.
        for runs in ([], [{'status': 'queued'}], [{'status': 'completed', 'conclusion': 'failure'}]):
            self.assertEqual(decide(['v0.8.7'], runs=runs)['action'], 'skip')

    def test_catchup_dispatch_contract_and_bounded_wait(self):
        workflow = self.path.read_text()
        self.assertIn("if: inputs.wait_for_release_run != ''", workflow)
        self.assertIn('timeout-minutes: 3', workflow)
        self.assertLess(workflow.index('name: Wait for the completed release'), workflow.index('uses: actions/checkout@'))
        release = self.path.with_name('release.yml').read_text()
        self.assertIn("github.event_name == 'workflow_dispatch' && github.ref == 'refs/heads/main' && needs.configure.outputs.prerelease == 'false'", release)
        self.assertIn('-f dry_run=false -f wait_for_release_run="$GITHUB_RUN_ID"', release)
        self.assertLess(release.index('name: Create GitHub release'), release.index('name: Reconsider green main'))

    @unittest.skipUnless(shutil.which('bash') and shutil.which('jq'), 'bash and jq needed for workflow shell test')
    def test_actual_catchup_wait_rejects_wrong_source_or_unsuccessful_completion(self):
        workflow = self.path.read_text()
        section = workflow.split('      - name: Wait for the completed release\n', 1)[1]
        script = section.split('        run: |\n', 1)[1].split('      - uses:', 1)[0]
        script = '\n'.join(line[10:] for line in script.splitlines())
        valid = dict(path='.github/workflows/release.yml', event='workflow_dispatch', head_branch='main',
                     repository=dict(full_name='o/r'), status='completed', conclusion='success')
        for changes, run_id, succeeds in (
                ({}, '123', True),
                ({'initial_status': 'in_progress'}, '123', True),
                ({}, 'not-a-run', False),
                ({'path': '.github/workflows/build.yml'}, '123', False),
                ({'event': 'push'}, '123', False),
                ({'head_branch': 'feature'}, '123', False),
                ({'repository': {'full_name': 'fork/r'}}, '123', False),
                ({'status': 'in_progress', 'conclusion': None}, '123', False),
                ({'conclusion': 'failure'}, '123', False),
                ({'conclusion': 'cancelled'}, '123', False)):
            with self.subTest(changes=changes, run_id=run_id), tempfile.TemporaryDirectory() as temp:
                root = Path(temp)
                (root / 'fixture.json').write_text(json.dumps(valid | changes))
                env = dict(os.environ, RUNNER_TEMP=temp, RELEASE_RUN=run_id, GITHUB_REPOSITORY='o/r')
                mocks = r'''gh() {
  local first=false
  if [[ ! -f "$RUNNER_TEMP/api-calls" ]]; then first=true; fi
  printf '%s\n' "$@" >> "$RUNNER_TEMP/api-calls"
  if [[ "$first" == true ]] && jq -e '.initial_status == "in_progress"' "$RUNNER_TEMP/fixture.json" > /dev/null; then
    jq '.status = "in_progress" | .conclusion = null' "$RUNNER_TEMP/fixture.json"
  else
    cat "$RUNNER_TEMP/fixture.json"
  fi
}
sleep() { :; }
'''
                result = run_workflow_shell(mocks + script, env)
                self.assertEqual(result.returncode == 0, succeeds, result.stdout + result.stderr)
                if run_id == 'not-a-run':
                    self.assertFalse((root / 'api-calls').exists())
                elif changes.get('initial_status') == 'in_progress':
                    self.assertEqual((root / 'api-calls').read_text().count('api\n'), 2)
                elif changes.get('status') == 'in_progress':
                    self.assertEqual((root / 'api-calls').read_text().count('api\n'), 31)
                else:
                    self.assertEqual((root / 'api-calls').read_text().count('api\n'), 1)

    @unittest.skipUnless(shutil.which('bash') and shutil.which('jq'), 'bash and jq needed for workflow shell test')
    def test_actual_dispatch_shell_rechecks_changed_plan_and_pins_commit(self):
        workflow = self.path.read_text()
        section = workflow.split('      - name: Start the release\n', 1)[1]
        script = section.split('        run: |\n', 1)[1].split('      - name:', 1)[0]
        script = '\n'.join(line[10:] for line in script.splitlines())
        for action, version, commit, dispatch in (
                ('release', 'v0.8.7', HEAD, True),
                ('skip', '', HEAD, False),  # pending/failed CI, active run or already tagged
                ('release', 'v0.8.8', HEAD, False),  # another train/manual release took the version
                ('release', 'v0.8.7', 'b' * 40, False)):  # main moved, even if green
            with self.subTest(action=action, version=version, commit=commit), tempfile.TemporaryDirectory() as temp:
                root = Path(temp)
                (root / 'fixture.json').write_text(json.dumps(dict(action=action, version=version, commit=commit)))
                env = dict(os.environ, RUNNER_TEMP=temp, GITHUB_STEP_SUMMARY=str(root / 'summary'),
                           VERSION='v0.8.7', COMMIT=HEAD, GITHUB_REPOSITORY='o/r')
                # Only shell functions are invoked; no network, git fetch or release dispatch is possible.
                mocks = r'''git() {
  if [[ "$1" == rev-parse ]]; then printf '%s\n' bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb; fi
}
python() {
  printf '%s\n' "$@" > "$RUNNER_TEMP/python-args"
  cat "$RUNNER_TEMP/fixture.json"
}
gh() { printf '%s\n' "$@" > "$RUNNER_TEMP/dispatched"; }
'''
                result = run_workflow_shell(mocks + script, env)
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertIn('--head\n' + 'b' * 40, (root / 'python-args').read_text())
                self.assertEqual((root / 'dispatched').exists(), dispatch)
                if dispatch:
                    args = (root / 'dispatched').read_text()
                    self.assertIn('commit=' + HEAD, args)
                    self.assertIn('version=v0.8.7', args)
                    self.assertIn('prerelease=false', args)


if __name__ == '__main__':
    unittest.main()
