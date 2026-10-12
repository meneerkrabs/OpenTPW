"""x86 differential oracle targets: isolated routines of the identified Windows executables vs the plans.

Same shape as the PowerPC oracle (tools/ppc-analysis/oracle/targets.py), against a different image:

* `binary(sandbox, x, log)`: run the original routine in the sandbox on input x, return a comparable value;
* `plan(x)`: the reference the Mac-derived plans give, in Python (a mismatch is a PC-vs-Mac finding);
* `alternatives`: extra readings of the routine itself (what the code does), compared too;
* `inputs(rng, n)`: n seeded random inputs plus explicit boundaries.

Addresses are virtual addresses of the executable at its preferred base. Stubs are listed per target with the
reason they are safe; any other call outside `allowed` aborts the run.
"""
from __future__ import annotations

import random
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / 'lanes' / 'det'))

import det_model  # noqa: E402  (the Mac plan's reference arithmetic)
from x86_harness import MASK32, Image, Sandbox  # noqa: E402


@dataclass
class Target:
    key: str
    exe: str                      # 'tpw' or 'tpi' (x86_harness.IDENTIFIED)
    title: str
    plan_doc: str
    entry: int
    allowed: list[tuple[int, int]]
    stubs: Callable[['Target'], dict]
    binary: Callable
    plan: Callable | None
    inputs: Callable[[random.Random, int], list]
    alternatives: dict[str, Callable] = field(default_factory=dict)
    equal: Callable = staticmethod(lambda a, b: a == b)
    notes: list[str] = field(default_factory=list)
    log: list = field(default_factory=list)

    def sandbox(self, image: Image) -> Sandbox:
        self.log = []
        return Sandbox(image, self.allowed, self.stubs(self))


def run_binary(target: Target, sandbox: Sandbox, x):
    sandbox.reset()
    target.log.clear()
    return target.binary(sandbox, x, target.log)


# ============================================================================== (a) tp.exe world RNG
WORLD_RNG = 0x1DA708   # object + 0x1da708: the RNG state; +0x1da70c is the tick counter (the Mac world's layout)
WORLD_OBJECT_SIZE = 0x400000   # the state sits ~2 MB in; the sandbox needs the whole object mapped
TPW_RNG_STEP = 0x516330        # thiscall, ecx = object; returns |new state|
TPI_WORLD_RNG = 0x1EAB3C       # TPI Game.exe: the state moved (+0x1eab40 is the tick counter)
TPI_RNG_STEP = 0x6493E0        # thiscall, ecx = object; returns |new state|; code ends with the `ret` at 0x649414


def mask(value: int) -> int:
    return value & MASK32


def ror32(value: int, count: int) -> int:
    return mask((value >> count) | (value << (32 - count)))


def world_rng_binary(sb: Sandbox, state: int, log) -> tuple[int, int]:
    obj = sb.object(WORLD_OBJECT_SIZE, {WORLD_RNG: ('I', state)})
    result = sb.call(TPW_RNG_STEP, this=obj)
    return result.eax, sb.read(obj + WORLD_RNG, 'I')


def tpi_world_rng_binary(sb: Sandbox, state: int, log) -> tuple[int, int]:
    obj = sb.object(WORLD_OBJECT_SIZE, {TPI_WORLD_RNG: ('I', state)})
    result = sb.call(TPI_RNG_STEP, this=obj)
    return result.eax, sb.read(obj + TPI_WORLD_RNG, 'I')


def world_rng_plan(state: int) -> tuple[int, int]:
    """The Mac world LCG (det_model.world_next): store the LCG successor, return its wrapping signed abs."""
    new, returned = det_model.world_next(state)
    return returned, new


def world_rng_routine_reading(state: int) -> tuple[int, int]:
    """What 0x516330 does: LCG successor, rotated right by 13, stored; returns the wrapping signed abs of that."""
    new = ror32(det_model.world_step(state), 13)
    return det_model.wrapping_abs32(new), new


def world_rng_inputs(rng: random.Random, n: int) -> list:
    inverse = pow(1664525, -1, 1 << 32)

    def preimage(successor: int) -> int:
        return mask((successor - 1013904223) * inverse)
    # States whose stored (rotated) successor is INT_MIN and 0, and the Mac plan's INT_MIN and 0.
    boundaries = [0, 1, 0x7FFFFFFF, 0x80000000, MASK32, preimage(ror32(0x80000000, 19)), preimage(0),
                  preimage(0x80000000)]
    return boundaries + [rng.getrandbits(32) for _ in range(n)]


WORLD_RNG_TARGET = Target(
    'tpw-exe-world-rng', 'tpw', 'tp.exe world RNG step 0x516330', 'DET-plan §2.3 / det_model.world_next',
    TPW_RNG_STEP, [(TPW_RNG_STEP, 0x516365)], lambda t: {}, world_rng_binary, world_rng_plan, world_rng_inputs,
    alternatives={'routine-reading': world_rng_routine_reading},
    notes=['Same logical generator as the Mac 0x105328 (ORACLE.md, x86 section): same object offset and constants, '
           'but the PC stores the successor rotated right by 13, so the stream differs.'])

TPI_WORLD_RNG_TARGET = Target(
    'tpi-exe-world-rng', 'tpi', 'Game.exe world RNG step 0x6493e0', 'DET-plan §2.3 / det_model.world_next',
    TPI_RNG_STEP, [(TPI_RNG_STEP, 0x649415)], lambda t: {}, tpi_world_rng_binary, world_rng_plan, world_rng_inputs,
    alternatives={'tpw-pc-reading': world_rng_routine_reading},
    notes=['Same step as the tp.exe 0x516330 (LCG, ror 13, stored, signed abs) at a moved state slot (+0x1eab3c): '
           'the TPI reading is the TPW PC reading, not the Mac plan.'])

TARGETS = [WORLD_RNG_TARGET, TPI_WORLD_RNG_TARGET]
