"""Runs the oracle targets: the original routine vs the plan formula and vs OpenTPW's C# (when bridged).

Usage: python3 -I differential.py BIN_ROOT [--count N] [--seed S] [--target KEY ...] [--no-csharp] [--json]

Prints, per target and comparison, the number of inputs run, the number of mismatches and the first
counterexample (input, original, other side). Exit status 0 unless a run aborted; mismatches are
findings, not errors (tests decide which comparisons must agree).
"""
from __future__ import annotations

import argparse
import json
import math
import sys
from dataclasses import dataclass, field
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import bridge as bridge_module  # noqa: E402
from harness import Image, seeded  # noqa: E402
from targets import DOMAINS, TARGETS, Target, run_binary  # noqa: E402

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


def jsonable(value):
    if isinstance(value, float):
        return repr(value) if not math.isfinite(value) else value
    if isinstance(value, dict):
        return {k: jsonable(v) for k, v in value.items()}
    if isinstance(value, (list, tuple)):
        return [jsonable(v) for v in value]
    return value


def target_inputs(target: Target, count: int, seed: str) -> list:
    inputs = target.inputs(seeded(f'{seed}:{target.key}'), count)
    domain = DOMAINS.get(target.key)
    return [x for x in inputs if domain is None or domain(x)]


def compare(target: Target, image: Image, count: int = DEFAULT_COUNT, seed: str = 'oracle-v1',
            csharp: bool = True, plan=None, keep: int = 3, bridge=None) -> list[Comparison]:
    """Original vs plan (or the given plan function) and vs C# when the bridge provides the function."""
    inputs = target_inputs(target, count, seed)
    sandbox = target.sandbox(image)
    originals = [run_binary(target, sandbox, x) for x in inputs]
    results = []
    plan = plan or target.plan
    if plan is not None:
        results.append(_tally(target, 'plan', inputs, originals, [plan(x) for x in inputs], keep))
    for name, alternative in target.alternatives.items():
        results.append(_tally(target, name, inputs, originals, [alternative(x) for x in inputs], keep))
    results.append(_csharp(target, 'csharp', None, inputs, originals, csharp, keep, bridge))
    for side, mapping in target.csharp_variants.items():
        results.append(_csharp(target, side, mapping, inputs, originals, csharp, keep, bridge))
    return results


def _csharp(target: Target, side: str, mapping, inputs, originals, enabled: bool, keep: int, bridge=None) -> Comparison:
    name = target.csharp[0] if target.csharp else None
    if not enabled:
        return Comparison(target.key, side, skipped='C# comparison disabled')
    if name is None:
        return Comparison(target.key, side, skipped='no C# implementation on main; see notes for the hook')
    bridge = bridge or bridge_module.get()
    if bridge is None:
        return Comparison(target.key, side, skipped='no .NET 10 SDK (OPENTPW_ORACLE_DOTNET, OPENTPW_DOTNET, default, PATH)')
    request, response = mapping or target.csharp[1:]
    try:
        if name not in bridge.capabilities():
            return Comparison(target.key, side, skipped=f'bridge lacks {name}')
        answers = bridge.run([request(x) for x in inputs])
    except RuntimeError as error:  # build or run failure (bridge.BridgeError): the C# side only is skipped
        return Comparison(target.key, side, skipped=str(error))
    return _tally(target, side, inputs, originals, [response(x, a) for x, a in zip(inputs, answers)], keep)


def _tally(target: Target, side: str, inputs, originals, others, keep: int) -> Comparison:
    result = Comparison(target.key, side, inputs=len(inputs))
    for x, original, other in zip(inputs, originals, others):
        if not target.equal(original, other):
            result.mismatches += 1
            example = {'input': jsonable(x), 'original': jsonable(original), side: jsonable(other)}
            if result.first is None:
                result.first = example
            if len(result.examples) < keep:
                result.examples.append(example)
    return result


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('bin_root', type=Path)
    parser.add_argument('--count', type=int, default=DEFAULT_COUNT)
    parser.add_argument('--seed', default='oracle-v1')
    parser.add_argument('--target', action='append')
    parser.add_argument('--no-csharp', action='store_true')
    parser.add_argument('--json', action='store_true')
    args = parser.parse_args(argv)
    image = Image.load(args.bin_root)
    report = []
    for target in TARGETS:
        if args.target and target.key not in args.target:
            continue
        for comparison in compare(target, image, args.count, args.seed, not args.no_csharp):
            report.append(comparison)
            if not args.json:
                if comparison.skipped:
                    print(f'skip  {target.key} vs {comparison.side}: {comparison.skipped}')
                    continue
                mark = 'agree' if comparison.agrees else 'DIFF '
                print(f'{mark} {target.key} vs {comparison.side}: {comparison.inputs} inputs, '
                      f'{comparison.mismatches} mismatches')
                if comparison.first:
                    print('      first: ' + json.dumps(comparison.first))
    if args.json:
        print(json.dumps([vars(c) for c in report], indent=2))
    return 0


if __name__ == '__main__':
    sys.exit(main())
