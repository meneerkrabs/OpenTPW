#!/usr/bin/env python3
"""GATE-006 synthetic sensitivity checks, NOT native or C# acceptance evidence.

Run with python3 -I queue_walk_synthetic.py. No assets, imports of binary
inspection modules, or execution of walk_evidence.main/inspect are required.
Only explicitly selected mathematical definitions are loaded from the sibling
source. Unsupported neighbour ordering and corner-wall physics remain unknown.
"""
import ast
from collections import Counter
import hashlib
from itertools import product
import json
import math
from pathlib import Path

CONSTANTS = set('ONE RADIUS SPEED_FLOOR SPEED_CAP MAX_SPEED_FACTOR MAX_FORCE_FACTOR WEIGHTS ARRIVE_FACTOR WAYPOINT_FACTOR RAMP_DIVISOR STUCK_FRACTION STUCK_WINDOW'.split())
FUNCTIONS = set('fix_mul fix_div isqrt length octile truncate speed_caps arrival_radius waypoint_radius cell_of centre walk'.split())


def load_math(path):
    source = path.read_bytes()
    nodes = []
    names = set()
    for node in ast.parse(source, filename=str(path)).body:
        if isinstance(node, ast.FunctionDef) and node.name in FUNCTIONS:
            nodes.append(node)
            names.add(node.name)
        elif isinstance(node, ast.Assign):
            targets = {target.id for target in node.targets if isinstance(target, ast.Name)}
            if targets and targets <= CONSTANTS:
                nodes.append(node)
                names.update(targets)
    missing = (CONSTANTS | FUNCTIONS) - names
    if missing:
        raise ValueError('Missing mathematical definitions: ' + ', '.join(sorted(missing)))
    namespace = {'math': math}
    exec(compile(ast.Module(body=nodes, type_ignores=[]), str(path), 'exec'), namespace)
    return namespace, hashlib.sha256(source).hexdigest()


def routes():
    # Every simple cardinal route up to three steps, including left/right turns.
    yield [(0, 0)]
    directions = ((1, 0), (0, 1), (-1, 0), (0, -1))
    for size in (1, 2, 3):
        for steps in product(directions, repeat=size):
            route = [(0, 0)]
            for dx, dy in steps:
                x, y = route[-1]
                route.append((x + dx, y + dy))
            if len(set(route)) == len(route):
                yield route
    # Long straight sensitivity case; queue depth is not a modelled guest queue.
    for dx, dy in directions:
        yield [(dx * i, dy * i) for i in range(26)]


def enumerate_cases(model):
    centre, walk = model['centre'], model['walk']
    for route in routes():
        for speed, rounding, separation, offset, stale in product(
                (0.59, 2.0), ('nearest', 'floor'), ('off', 'oppose'),
                (-8192, 0, 8192), (-1, 0, 1)):
            x, y = centre(route[0])
            maximum, _ = model['speed_caps'](speed)
            velocity = (stale * maximum, 0)
            spec = {'route': route, 'speed': speed, 'rounding': rounding,
                    'separation': separation, 'start': (x + offset, y),
                    'velocity': velocity}
            count, details = walk(spec['start'], velocity, centre(route[-1]),
                                  route, speed, rounding=rounding,
                                  separation=separation, fences=False,
                                  strip=set(route), limit=5000)
            yield spec, count, details


def report(model, source_sha):
    outcomes, maxima, witnesses = Counter(), {}, {}
    total = 0
    for spec, count, details in enumerate_cases(model):
        total += 1
        outcome = 'arrived' if count is not None else details['reason']
        outcomes[outcome] += 1
        key = str(len(spec['route']) - 1)
        if count is not None and count > maxima.get(key, 0):
            maxima[key] = count
            witnesses['max_steps_' + key] = {'case': spec, 'updates': count, 'details': details}
        if count is None:
            witnesses.setdefault(outcome, {'case': spec, 'updates': count, 'details': details})
    return {'source_sha256': source_sha, 'cases': total, 'outcomes': dict(outcomes),
            'max_arrival_updates_by_route_steps': maxima, 'witnesses': witnesses,
            'limitations': [
                'Synthetic restatement only: not executed C#, native trace, or a universal bound.',
                'No corner-wall forces: fences=False. Route-cell escape is reported, not repaired.',
                'Only off/oppose separation proxies; no neighbour order, held, riding or staff semantics.',
                'Offsets and initial velocities sample the x-axis only: selected sensitivity cases, not exhaustive vector enumeration.',
                'Route steps 0/1/2/3/25 do not establish queue occupancy bounds 0/1/25.',
                '212 updates across a 25-step route is not a per-cell time and does not refute the accepted 27.',
                'Failures and long walks are sensitivity witnesses, not proven native counterexamples.',
                'Runtime QueueCellWalkTurns=27 and all accepted gates remain unchanged.']}


def main():
    model, sha = load_math(Path(__file__).with_name('walk_evidence.py'))
    result = report(model, sha)
    assert sum(result['outcomes'].values()) == result['cases']
    print(json.dumps(result, indent=2, sort_keys=True))


if __name__ == '__main__':
    main()
