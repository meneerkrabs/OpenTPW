"""Prints the widths quoted in ThemeParkIncThingBodyTests: python3 golden.py (needs TPI_GAME_EXE)."""
from run import run
analyser = [3] * 4 + [144] * 21 + [50, 5] + [50] * 22
for model, counts in [('mechanic', [3, 0, 30, 1, 0, 7]), ('guard', [30] * 6), ('researcher', [1, 2, 3, 4, 5, 6]), ('handyman', [0] * 6), ('entertainer', [5, 4, 3, 2, 1, 0]),
                      ('gardener', [30, 0, 0, 0, 0, 29]), ('analyser', analyser), ('analyser', [0] * 4 + [144] * 21 + [50, 5] + [50] * 22), ('analyser', [1, 2, 3, 0]),
                      ('analyser', [3] * 4 + [144] * 21 + [50, 5] + [51] * 22), ('tag', [0]), ('tag', [1]), ('tag', [2]), ('tag', [3]), ('tag', [0xffffffff]),
                      ('guest', []), ('staffhq', []), ('advisor', []), ('lab', []), ('weather', []), ('challenge', []),
                      ('mechhq', []), ('ui', []), ('objh', []), ('cuts', []), ('trnr', []), ('bank', [])]:
    e = run(model, counts)
    print(f'{model:12} counts {counts[:6]}{"..." if len(counts) > 6 else ""} -> {e.cur} bytes, failure {e.fail}')
