"""Runs a load routine on a generated stream and returns the reads it made.

The stream is produced on demand: every member is zero, except that `counts` gives the entry count of each history ring (and the tag count) in the order the routine reads them.
Each read is (stream offset, size, destination offset in the object, caller, member name); the names come from the field-name helper 0x441da0.
"""
import struct
import emu
from emu import Emu
from models import MODELS, THIS, DUMMY

def run(model, counts=(), values=None, maxins=300_000_000):
    load, setup = MODELS[model]
    e = Emu()
    e.hooks[0x4052a4] = e.hook_new      # operator new (object allocation)
    e.hooks[0x86fed9] = e.hook_new      # operator new (list nodes)
    e.hooks[0x441da0] = e.h_name        # field-name log
    for i in range(40):                 # the guest's sprite table counts are filled at run time; the load divides by them
        e.uc.mem_write(0x9d7008 + 12 * i, struct.pack('<I', 4))
    state = {'k': 0}
    def gen(size, index, name, caller):
        if name in ('mNumEntries', 'numtags'):
            n = counts[state['k']] if state['k'] < len(counts) else 0
            state['k'] += 1
            return (n & 0xffffffff).to_bytes(4, 'little')
        if values and name in values:
            return values[name].to_bytes(size, 'little')
        return bytes(size)
    e.gen = gen
    for step in setup:
        if step[0] == 'c':
            e.call(step[1], (DUMMY,), ecx=step[2])
        else:
            e.call(0x402423, (step[1], step[2], step[3], step[4]))
    e.call(load, (0x20000000, 0, 1, 0x10200000), ecx=THIS, maxins=maxins)   # (stream, 0 = load, version 1, id)
    return e
