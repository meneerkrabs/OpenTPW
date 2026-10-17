"""Prints the reads of one load routine: python3 dump.py <model> [ring counts...]. Needs TPI_GAME_EXE and `pip install unicorn`."""
import sys
from run import run
from models import THIS

model = sys.argv[1]
e = run(model, [int(x) for x in sys.argv[2:]])
print(model, 'consumed', e.cur, 'failure', e.fail)
for cur, size, dest, caller, name in e.events:
    print(f'{cur:6} size={size:3} dest={dest - THIS:#x} caller={caller:x} {name}')
