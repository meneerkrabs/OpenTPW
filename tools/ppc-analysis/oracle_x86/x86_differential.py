"""Runs the x86 oracle targets: the original routine vs the plan formula and vs the routine readings.

Usage: python3 -I x86_differential.py [--count N] [--seed S] [--target KEY ...] [--json]
       with OPENTPW_TPW_EXE / OPENTPW_TPI_EXE naming the identified executables (outside the checkout)

Prints, per target and comparison, the number of inputs run, the number of mismatches and the first counterexample.
Exit status 0 unless a run aborted; mismatches are findings, not errors (tests decide which comparisons must agree).
Dev-only: needs Unicorn (pip install unicorn==2.1.4).
"""
from __future__ import annotations

import argparse
import json
import sys
from dataclasses import dataclass, field
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from x86_harness import UNICORN_HINT, IDENTIFIED, Image, executable_path, have_unicorn, seeded  # noqa: E402
from x86_targets import TARGETS, Target, run_binary  # noqa: E402

DEFAULT_COUNT = 500


@dataclass
class Comparison:
    target: str
    side: str
    inputs: int = 0
    mismatches: int = 0
    first: dict | None = None
    skipped: str | None = None
    examples: list = field(default_factory=list)

    @property
    def agrees(self) -> bool:
        return self.skipped is None and self.mismatches == 0 and self.inputs > 0


def target_inputs(target: Target, count: int, seed: str) -> list:
    return target.inputs(seeded(f'{seed}:{target.key}'), count)


def compare(target: Target, image: Image, count: int = DEFAULT_COUNT, seed: str = 'oracle-v1', plan=None,
            keep: int = 3) -> list[Comparison]:
    """Original vs the plan (or the given plan function) and vs each alternative reading."""
    inputs = target_inputs(target, count, seed)
    sandbox = target.sandbox(image)
    originals = [run_binary(target, sandbox, x) for x in inputs]
    results = []
    plan = plan or target.plan
    if plan is not None:
        results.append(_tally(target, 'plan', inputs, originals, [plan(x) for x in inputs], keep))
    for name, alternative in target.alternatives.items():
        results.append(_tally(target, name, inputs, originals, [alternative(x) for x in inputs], keep))
    return results


def _tally(target: Target, side: str, inputs, originals, others, keep: int) -> Comparison:
    result = Comparison(target.key, side, inputs=len(inputs))
    for x, original, other in zip(inputs, originals, others):
        if not target.equal(original, other):
            result.mismatches += 1
            example = {'input': x, 'original': original, side: other}
            if result.first is None:
                result.first = example
            if len(result.examples) < keep:
                result.examples.append(example)
    return result


def _jsonable(value):
    return value.hex() if isinstance(value, (bytes, bytearray)) else repr(value)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--count', type=int, default=DEFAULT_COUNT)
    parser.add_argument('--seed', default='oracle-v1')
    parser.add_argument('--target', action='append')
    parser.add_argument('--json', action='store_true')
    args = parser.parse_args(argv)
    if not have_unicorn():
        print(UNICORN_HINT, file=sys.stderr)
        return 2
    images: dict[str, Image] = {}
    report = []
    for target in TARGETS:
        if args.target and target.key not in args.target:
            continue
        if target.exe not in images:
            path = executable_path(target.exe)
            if path is None:
                print(f'skip  {target.key}: {IDENTIFIED[target.exe]["variable"]} is not set to the identified file')
                continue
            images[target.exe] = Image.load(path)
        for comparison in compare(target, images[target.exe], args.count, args.seed):
            report.append(comparison)
            if not args.json:
                mark = 'agree' if comparison.agrees else 'DIFF '
                print(f'{mark} {target.key} vs {comparison.side}: {comparison.inputs} inputs, '
                      f'{comparison.mismatches} mismatches')
                if comparison.first:
                    print('      first: ' + json.dumps(comparison.first, default=_jsonable))
    if args.json:
        print(json.dumps([vars(c) for c in report], indent=2))
    return 0


if __name__ == '__main__':
    sys.exit(main())
