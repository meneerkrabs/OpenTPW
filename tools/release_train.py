#!/usr/bin/env python3
"""Plan the hourly preview release: pick the commit and the version, or say why to skip.

The planning rules are pure functions (tested in test_release_train.py); main() only gathers
facts from git and the GitHub API and prints the decision. It never creates tags or releases.
"""
import argparse
import json
import os
import re
import subprocess
import sys

TAG_RE = re.compile(r'^v(\d+)\.(\d+)\.(\d+)(?:-preview\.(\d+))?$')
SHA_RE = re.compile(r'^[0-9a-f]{40}$')


def parse_tag(tag):
    """Return (major, minor, patch, preview_number_or_None) for stable and preview tags, else None."""
    match = TAG_RE.match(tag)
    if not match:
        return None
    major, minor, patch, preview = match.groups()
    return int(major), int(minor), int(patch), None if preview is None else int(preview)


def sort_key(tag):
    # A stable release sorts after every preview of the same version.
    major, minor, patch, preview = parse_tag(tag)
    return major, minor, patch, 1 if preview is None else 0, preview or 0


def last_tag(tags):
    """The newest tag we know the format of, or None."""
    known = [tag for tag in tags if parse_tag(tag)]
    return max(known, key=sort_key) if known else None


def next_preview_version(tags):
    """vX.Y.(Z+1)-preview.N after the latest stable vX.Y.Z; N counts up per train."""
    stables = [tag for tag in tags if parse_tag(tag) and parse_tag(tag)[3] is None]
    if not stables:
        raise ValueError('no stable release tag to derive a preview version from')
    major, minor, patch, _ = parse_tag(max(stables, key=sort_key))
    base = (major, minor, patch + 1)
    numbers = [parse_tag(tag)[3] for tag in tags
               if parse_tag(tag) and parse_tag(tag)[:3] == base and parse_tag(tag)[3] is not None]
    return 'v%d.%d.%d-preview.%d' % (*base, max(numbers, default=0) + 1)


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


def plan(head, tags, tags_at_head, commits_since_last_tag, build_runs, release_runs_active):
    """Return {'action': 'release'|'skip', 'reason': ..., 'version': ..., 'commit': ...}."""
    def skip(reason):
        return {'action': 'skip', 'reason': reason, 'version': '', 'commit': head}
    if release_runs_active:
        return skip('a release run is still queued or in progress')
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
            'version': next_preview_version(tags), 'commit': head}


def release_notes(added_fragments, repository, commit):
    """Extra notes appended to GitHub's generated list of merged pull requests."""
    if not added_fragments:
        return ''
    lines = ['## Progress notes in this preview', '']
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
    out = sh('gh', 'api', '--method', 'GET', path, *flags, '--jq', '.workflow_runs')
    return json.loads(out or '[]')


def gather(repository, head):
    tags = sh('git', 'tag', '--list', 'v*').split()
    at_head = [t for t in sh('git', 'tag', '--points-at', head).split() if parse_tag(t)]
    previous = last_tag(tags)
    since = int(sh('git', 'rev-list', '--count', '%s..%s' % (previous, head))) if previous else 1
    builds = workflow_runs(repository, 'build.yml', head_sha=head, event='push')
    active = (workflow_runs(repository, 'release.yml', status='queued')
              + workflow_runs(repository, 'release.yml', status='in_progress'))
    fragments = []
    if previous:
        fragments = sh('git', 'diff', '--name-only', '--diff-filter=A', previous, head, '--', 'docs/progress/').split()
    return tags, at_head, since, builds, active, fragments


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
    tags, at_head, since, builds, active, fragments = gather(args.repository, head)
    decision = plan(head, tags, at_head, since, builds, active)
    if args.notes_file:
        with open(args.notes_file, 'w') as handle:
            handle.write(release_notes(fragments, args.repository, head) if decision['action'] == 'release' else '')
    json.dump(decision, sys.stdout)
    sys.stdout.write('\n')


if __name__ == '__main__':
    main()
