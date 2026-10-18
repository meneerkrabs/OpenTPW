#!/usr/bin/env python3
"""Plan the hourly release: pick the commit and the version, or say why to skip.

The planning rules are pure functions (tested in test_release_train.py); main() only gathers
facts from git and the GitHub API and prints the decision. It never creates tags or releases.
"""
import argparse
import json
import os
import re
import subprocess
import sys

STABLE_RE = re.compile(r'^v(\d+)\.(\d+)\.(\d+)$')
SHA_RE = re.compile(r'^[0-9a-f]{40}$')


def parse_tag(tag):
    """Return (major, minor, patch) for a stable vX.Y.Z tag; anything else (previews, other tags) is None."""
    match = STABLE_RE.match(tag)
    return tuple(int(part) for part in match.groups()) if match else None


def stable_tags(tags):
    return [tag for tag in tags if parse_tag(tag)]


def last_tag(tags):
    """The newest stable tag, or None. Preview tags are left alone and never count."""
    known = stable_tags(tags)
    return max(known, key=parse_tag) if known else None


def next_version(tags):
    """vX.Y.(Z+1) after the newest stable vX.Y.Z. The result never names an existing tag."""
    previous = last_tag(tags)
    if previous is None:
        raise ValueError('no stable release tag to derive the next version from')
    major, minor, patch = parse_tag(previous)
    return 'v%d.%d.%d' % (major, minor, patch + 1)


def check_state(runs):
    """Classify workflow runs of one commit: 'green', 'pending', 'failed' or 'none'."""
    if not runs:
        return 'none'
    states = set()
    for run in runs:
        if run.get('status') != 'completed':
            states.add('pending')
        elif run.get('conclusion') == 'success':
            states.add('green')
        else:
            states.add('failed')
    if 'failed' in states:
        return 'failed'
    if 'pending' in states:
        return 'pending'
    return 'green'


def plan(head, tags, tags_at_head, commits_since_last_tag, build_runs, release_runs_active, unfinished=None, retry_build_runs=None):
    """Return {'action': 'release'|'skip', 'reason': ..., 'version': ..., 'commit': ...}."""
    def skip(reason):
        return {'action': 'skip', 'reason': reason, 'version': '', 'commit': head}
    if release_runs_active:
        return skip('a release run is still queued or in progress')
    # The newest stable tag exists but its release is missing or still a draft: an earlier run died after tagging.
    # Retry that exact version on the commit it was tagged at (the publish script repairs it), never a new version.
    if unfinished:
        version = max(unfinished, key=parse_tag)
        commit = unfinished[version]
        if check_state(retry_build_runs or []) != 'green':
            return skip('unfinished release %s needs successful CI on its exact commit %s' % (version, commit))
        return {'action': 'release', 'reason': 'retrying the unfinished release of %s' % version,
                'version': version, 'commit': commit}
    if tags_at_head:
        return skip('HEAD is already tagged (%s)' % ', '.join(sorted(tags_at_head)))
    previous = last_tag(tags)
    if previous and commits_since_last_tag == 0:
        return skip('no commit is newer than %s' % previous)
    state = check_state(build_runs)
    if state == 'failed':
        return skip('CI failed on %s; fix main and the next train will pick it up' % head[:10])
    if state != 'green':
        return skip('CI has not finished on %s (%s); the next train retries' % (head[:10], state))
    return {'action': 'release', 'reason': 'main is green and has new commits',
            'version': next_version(tags), 'commit': head}


def release_notes(added_fragments, repository, commit):
    """Extra notes appended to GitHub's generated list of merged pull requests."""
    if not added_fragments:
        return ''
    lines = ['## Progress notes in this release', '']
    for path in sorted(added_fragments):
        title = os.path.splitext(os.path.basename(path))[0]
        lines.append('- [%s](https://github.com/%s/blob/%s/%s)' % (title, repository, commit, path))
    return '\n'.join(lines) + '\n'


def sh(*args):
    return subprocess.run(args, check=True, capture_output=True, text=True).stdout


def workflow_runs(repository, workflow, **query):
    path = 'repos/%s/actions/workflows/%s/runs' % (repository, workflow)
    flags = []
    for key, value in query.items():
        flags += ['-f', '%s=%s' % (key, value)]
    # --paginate emits one JSON object per page. Decode the stream ourselves:
    # gh does not allow combining --slurp and --jq, and older runners lack --slurp.
    out = sh('gh', 'api', '--method', 'GET', path, *flags, '--paginate')
    decoder = json.JSONDecoder()
    runs = []
    while out.strip():
        page, end = decoder.raw_decode(out.lstrip())
        runs.extend(page['workflow_runs'])
        out = out.lstrip()[end:]
    return runs


def release_is_unfinished(repository, tag):
    """True when the tag has no GitHub release yet or its release is still a draft."""
    result = subprocess.run(['gh', 'release', 'view', tag, '--repo', repository, '--json', 'isDraft', '--jq', '.isDraft'],
                            capture_output=True, text=True)
    if result.returncode != 0:
        if 'not found' in result.stderr.lower():
            return True
        raise RuntimeError('cannot read the release of %s: %s' % (tag, result.stderr.strip()))  # transient: do nothing this hour
    return result.stdout.strip() == 'true'


def gather(repository, head):
    tags = sh('git', 'tag', '--list', 'v*').split()
    at_head = stable_tags(sh('git', 'tag', '--points-at', head).split())
    previous = last_tag(tags)
    since = int(sh('git', 'rev-list', '--count', '%s..%s' % (previous, head))) if previous else 1
    builds = workflow_runs(repository, 'build.yml', head_sha=head, event='push', branch='main')
    # Include approval/concurrency waits, not just runners that already started.
    active = [run for status in ('queued', 'in_progress', 'waiting', 'pending', 'requested')
              for run in workflow_runs(repository, 'release.yml', status=status)]
    unfinished = {}
    if previous and release_is_unfinished(repository, previous):
        unfinished[previous] = sh('git', 'rev-list', '-n', '1', previous).strip()
    retry_builds = []
    if unfinished:
        retry_builds = workflow_runs(repository, 'build.yml', head_sha=unfinished[previous], event='push', branch='main')
    fragments = []
    if previous and not unfinished:
        fragments = sh('git', 'diff', '--name-only', '--diff-filter=A', previous, head, '--', 'docs/progress/').split()
    return tags, at_head, since, builds, active, fragments, unfinished, retry_builds


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repository', default=os.environ.get('GITHUB_REPOSITORY'), required=False)
    parser.add_argument('--head', help='commit to release; default: git rev-parse HEAD')
    parser.add_argument('--notes-file', help='write the extra release notes here')
    args = parser.parse_args(argv)
    if not args.repository:
        parser.error('--repository or GITHUB_REPOSITORY is required')
    head = args.head or sh('git', 'rev-parse', 'HEAD').strip()
    if not SHA_RE.match(head):
        parser.error('--head must be a full lowercase commit SHA')
    tags, at_head, since, builds, active, fragments, unfinished, retry_builds = gather(args.repository, head)
    decision = plan(head, tags, at_head, since, builds, active, unfinished, retry_builds)
    if args.notes_file:
        with open(args.notes_file, 'w') as handle:
            handle.write(release_notes(fragments, args.repository, head) if decision['action'] == 'release' else '')
    json.dump(decision, sys.stdout)
    sys.stdout.write('\n')


if __name__ == '__main__':
    main()
