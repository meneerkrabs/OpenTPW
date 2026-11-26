#!/usr/bin/env python3
"""Verify TPI's advisor descriptor table and its queue rules against the executable.

The descriptor initializer 0x729530 runs in the sandbox and every row is compared with
content/data/tpi-advisor-descriptors.toml (--write-table writes that file). The acceptance
check 0x718B40, the enqueue and replacement 0x718820/0x718E90, the selection 0x718F80 and the
cancel 0x718990 then run on synthetic scorer fields, history rows and queues. With --check the
acceptance cases are read from the DataRows of a C# test file and each expected result is
compared with the native one. No game, audio or UI is launched; the speech stop the cancel
calls is captured instead of run.
"""
import argparse
import re
import struct
from pathlib import Path

from x86_harness import Image, Sandbox, executable_path

TABLE = 0x9DE020
ROW = 56
COUNT_WORD = 0x9E7198
SCORER = 0xC65220
GROUPS = SCORER + 0x2C
CAREER_GLOBAL = 0xBFC484
WORLD_GLOBAL = 0xADE6B0
WORLD_CLOCK = 0x1EAB40
HISTORY = 0x14F0
TUTORIAL_ON = 0xA10219
ADVISOR_PANEL = 0xA568BC
# The test message's row (rain, 0x157) is patched per case: group +8, limit +0x20, count +0x28, rule +0x2C.
TEST_MESSAGE = 0x157
TEST_ROW = 0x9E3E30

ROW_PATTERN = re.compile(
    r'^\s*\{ row = (\d+), kind = (\d), message = 0x([0-9a-f]+), group = (\d+), class = (\d+), callback = 0x([0-9a-f]+), '
    r'duplicates = (\d+), response = (\d+), count = (\d+), variant = (\d+)(?:, score = (-?\d+))?(?:, field = 0x([0-9a-f]+))? \},?$')
CASE_PATTERN = re.compile(r'^\s*\[DataRow\(([^\r\n]+)\)\]\s*$', re.MULTILINE)


def check(actual, expected, label):
    if actual != expected:
        raise AssertionError(f'{label}: expected {expected!r}, got {actual!r}')


def image_bytes(image, address, size):
    return bytes(image.data[address - image.base:address - image.base + size])


def thunk_target(image, address):
    raw = image_bytes(image, address, 5)
    return address + 5 + struct.unpack_from('<i', raw, 1)[0] if raw[0] == 0xE9 else address


def constant_score(image, callback):
    """The score of a callback that is `mov eax, imm32; ret` or `xor eax, eax; ret`, else None."""
    target = thunk_target(image, callback)
    code = image_bytes(image, target, 6)
    if code[0] == 0xB8 and code[5] == 0xC3:
        return target, struct.unpack_from('<i', code, 1)[0]
    if code[:3] == b'\x33\xc0\xc3':
        return target, 0
    return target, None


def getter_field(image, callback):
    """The scorer offset a callback returns when it is `mov eax, [ecx + imm32]; ret`, else None."""
    target = thunk_target(image, callback)
    code = image_bytes(image, target, 7)
    if code[:2] == b'\x8b\x81' and code[6] == 0xC3:
        return target, struct.unpack_from('<I', code, 2)[0]
    return target, None


def native_rows(arena):
    count = arena.read(COUNT_WORD, 'I')
    check(count, 665, 'descriptor rows including the terminator')
    rows = [struct.unpack('<14i', bytes(arena.uc.mem_read(TABLE + ROW * index, ROW))) for index in range(count)]
    check(rows[-1][0], 2, 'terminator kind')
    for index, row in enumerate(rows[:-1]):
        check(row[0] in (0, 1), True, f'row {index} kind')
        check((row[5], row[6], row[7], row[12], row[13]), (0, 0, 0, 0x285, 0), f'row {index} constant words')
    return rows[:-1]


def table_lines(image, rows):
    lines = []
    for index, row in enumerate(rows):
        _, score = constant_score(image, row[4] & 0xFFFFFFFF)
        _, field = getter_field(image, row[4] & 0xFFFFFFFF)
        line = (f'  {{ row = {index}, kind = {row[0]}, message = 0x{row[1]:x}, group = {row[2]}, class = {row[3]}, '
                f'callback = 0x{row[4] & 0xFFFFFFFF:x}, duplicates = {row[8] & 0xFF}, response = {row[9]}, count = {row[10]}, '
                f'variant = {row[11]}{"" if score is None else f", score = {score}"}{"" if field is None else f", field = 0x{field:x}"} }},')
        lines.append(line)
    return lines


HEADER = '''# Advisor message descriptors of Theme Park Inc (no-CD Game.exe, SHA-256
# 77ea0c41410aad7c94a5eda658e31cb4ac05f7253aaf6746ad5e8a90afbd967a), for interoperability: all 664 rows of the
# descriptor table at 0x9DE020 before its terminator, as the initializer 0x729530 fills them, written by
# tools/ppc-analysis/oracle_x86/tpi_advisor_queue_oracle.py --write-table; see docs/reverse/TPI-ADVISOR.md.
#
# row: the table index (lookups take the first row of a message); kind: 0 the condition scan evaluates the row,
# 1 code posts the message; message: message id (+4); group: message group (+8); class: the advice context class
# (+0xC); callback: the score callback's address (+0x10); duplicates: pending duplicates allowed (+0x20);
# response: first response id (+0x24); count: responses (+0x28); variant: the variant rule (+0x2C); score: the
# callback's constant result where the callback is a constant (mov eax, imm32; ret); field: the scorer offset the
# callback returns where it reads one word of the scorer (mov eax, [ecx + imm32]; ret), an Advisor.sam key.

messages = ['''


def check_table(image, rows, path):
    lines = [line for line in Path(path).read_text(encoding='utf-8').splitlines() if ROW_PATTERN.match(line)]
    expected = table_lines(image, rows)
    check(len(lines), len(expected), f'{path} rows')
    for index, (line, native) in enumerate(zip(lines, expected)):
        check(line.rstrip(','), native.rstrip(','), f'{path} row {index}')


def check_getters(arena, image, rows):
    """Each field getter runs on a synthetic scorer whose words are their own offsets."""
    scorer = arena.object(0x1000)
    for offset in range(0, 0x1000, 4):
        arena.write(scorer + offset, 'I', 0x5A000000 | offset)
    seen = 0
    for row in rows:
        _, field = getter_field(image, row[4] & 0xFFFFFFFF)
        if field is None:
            continue
        arena.executed = 0
        check(arena.call(row[4] & 0xFFFFFFFF, this=scorer).eax, 0x5A000000 | field, f'callback {row[4]:#x} field')
        seen += 1
    return seen


def check_constants(arena, image, rows):
    seen = 0
    for row in rows:
        target, score = constant_score(image, row[4] & 0xFFFFFFFF)
        if score is None:
            continue
        arena.executed = 0
        check(arena.call(row[4] & 0xFFFFFFFF, this=SCORER).eax, score & 0xFFFFFFFF, f'callback {row[4]:#x} constant')
        seen += 1
    return seen


class Queue:
    """Synthetic advisor, career and world for the queue routines."""

    def __init__(self, arena):
        self.arena = arena
        self.career = arena.object(0x4740)
        self.world = arena.object(4)
        arena.write(CAREER_GLOBAL, 'I', self.career)
        arena.write(WORLD_GLOBAL, 'I', self.world - WORLD_CLOCK)
        arena.write(0xC64DC7, 'B', 1)  # The scorer is constructed (no Advisor.sam load).
        self.advisor = arena.object(0x118)

    def history(self, message):
        return self.career + HISTORY + 20 * message

    def set_history(self, message, time, variant, played, slaps, plays):
        row = self.history(message)
        self.arena.write(row, 'I', time & 0xFFFFFFFF)
        self.arena.write(row + 4, 'i', variant)
        self.arena.write(row + 8, 'B', played)
        self.arena.write(row + 0xC, 'i', slaps)
        self.arena.write(row + 0x10, 'i', plays)

    def set_group(self, group, intervals, once, slaps):
        for index, value in enumerate((*intervals, once, slaps)):
            self.arena.write(GROUPS + 24 * group + 4 * index, 'I', value)

    def set_slot(self, index, message, score, valid=1, group_row=None):
        slot = self.advisor + 0x1C + 24 * index
        self.arena.write(slot + 0xC, 'I', message)
        self.arena.write(slot + 0x10, 'i', score)
        self.arena.write(slot + 0x14, 'B', valid)

    def slots(self):
        return [struct.unpack('<HxxiiiiBBBx', bytes(self.arena.uc.mem_read(self.advisor + 0x1C + 24 * i, 24))) for i in range(8)]

    def clear(self):
        self.arena.uc.mem_write(self.advisor, b'\0' * 0x118)


def acceptance(queue, case):
    (group, i1, i2, i3, ix, once, slaps_limit, time, variant, played, slaps, plays, now, tutorial, bypass, help_flag,
     pending, limit, rule, count, _expected) = case
    arena = queue.arena
    arena.executed = 0
    queue.clear()
    arena.write(TEST_ROW + 8, 'I', group)
    arena.write(TEST_ROW + 0x20, 'B', limit)
    arena.write(TEST_ROW + 0x28, 'I', count)
    arena.write(TEST_ROW + 0x2C, 'I', rule)
    queue.set_group(group, (i1, i2, i3, ix), once, slaps_limit)
    queue.set_history(TEST_MESSAGE, time, variant, played, slaps, plays)
    arena.write(queue.world, 'I', now & 0xFFFFFFFF)
    arena.write(TUTORIAL_ON, 'B', tutorial)
    arena.write(queue.advisor + 0x10C, 'B', help_flag)
    for index in range(pending):
        queue.set_slot(index, TEST_MESSAGE, 10_000)
    return arena.call(0x718B40, (TEST_MESSAGE, bypass), this=queue.advisor).eax & 0xFF


def check_enqueue_and_selection(arena, queue, clock, stops):
    """Fixed scenarios the managed tests mirror (ThemeParkIncAdvisorTests)."""
    rain_group = arena.read(TEST_ROW + 8, 'I')
    arena.write(TEST_ROW + 8, 'I', 0)
    arena.write(TEST_ROW + 0x20, 'B', 255)
    arena.write(TEST_ROW + 0x28, 'I', 1)
    arena.write(TEST_ROW + 0x2C, 'I', 2)
    queue.set_group(0, (0, 0, 0, 0), 0, 0)
    queue.set_history(TEST_MESSAGE, 0, -1, 0, 0, 0)
    arena.write(TUTORIAL_ON, 'B', 1)
    arena.write(ADVISOR_PANEL, 'I', 0)

    def enqueue(message, score):
        arena.executed = 0
        record = arena.object(24, {0xC: ('I', message), 0x10: ('i', score), 0x14: ('B', 1), 8: ('i', -1)})
        return arena.call(0x718820, (record,), this=queue.advisor).eax & 0xFF

    def scores():
        return tuple(slot[4] if slot[5] else None for slot in queue.slots())

    # A full queue of mail (group 8) is never replaced: the enqueue fails.
    queue.clear()
    for index in range(8):
        queue.set_slot(index, 0x1A2, 10_000)
    check(enqueue(TEST_MESSAGE, 20_000), 0, 'a queue full of group-8 mail refuses')
    # Replacement skips group 8 and takes the first lowest of the rest; a tie is reported as a success without a write.
    queue.clear()
    for index, (message, score) in enumerate(((0x1A2, 1), (TEST_MESSAGE, 50), (TEST_MESSAGE, 30), (TEST_MESSAGE, 30),
                                              (TEST_MESSAGE, 70), (TEST_MESSAGE, 90), (0x1A2, 2), (TEST_MESSAGE, 40))):
        queue.set_slot(index, message, score)
    check(enqueue(TEST_MESSAGE, 30), 1, 'an equal score reports success')
    check(scores(), (1, 50, 30, 30, 70, 90, 2, 40), 'an equal score leaves the queue')
    check(enqueue(TEST_MESSAGE, 31), 1, 'a higher score replaces')
    check(scores(), (1, 50, 31, 30, 70, 90, 2, 40), 'the first lowest non-mail slot is replaced')

    def select():
        arena.executed = 0
        return arena.call(0x718F80, this=queue.advisor).eax

    # The selection: the first highest positive score; a positive group-8 slot wins over any score, the last one of them.
    queue.clear()
    for index, (message, score) in enumerate(((TEST_MESSAGE, 0), (TEST_MESSAGE, 40), (TEST_MESSAGE, 90), (TEST_MESSAGE, 90))):
        queue.set_slot(index, message, score)
    check(select(), 2, 'the first highest score')
    queue.set_slot(5, 0x1A2, 1)
    queue.set_slot(6, 0x1A3, 2)
    check(select(), 6, 'the last positive mail slot')
    queue.set_slot(6, 0x1A3, 0)
    check(select(), 5, 'a mail slot needs a positive score')
    queue.clear()
    queue.set_slot(0, TEST_MESSAGE, 0)
    queue.set_slot(1, TEST_MESSAGE, -1)
    check(select() & 0xFFFFFFFF, 0xFFFFFFFF, 'no positive score selects nothing')
    # With the advisor panel up only groups 1 and 8 are considered (0x271's first row is group 1, 0x1FC's group 0).
    queue.clear()
    queue.set_slot(0, TEST_MESSAGE, 90)
    queue.set_slot(1, 0x271, 10)
    queue.set_slot(2, 0x1FC, 20)
    check(select(), 0, 'without the panel the higher score wins')
    arena.write(ADVISOR_PANEL, 'I', 1)
    check(select(), 1, 'the panel restricts the selection to groups 1 and 8')
    arena.write(ADVISOR_PANEL, 'I', 0)

    # The cancel: every pending slot of the message is cleared; a playing message is stopped, its play flag cleared,
    # its slap count raised and the window zeroed. A message that is not playing keeps its history.
    queue.clear()
    queue.set_slot(0, TEST_MESSAGE, 10)
    queue.set_slot(3, TEST_MESSAGE, 20)
    queue.set_slot(4, 0x1A2, 30)
    queue.set_history(TEST_MESSAGE, 0x40, 0, 1, 2, 3)
    arena.write(queue.advisor + 0xE4, 'I', 1_000)
    arena.write(queue.advisor + 0xE8, 'I', 500)
    arena.write(queue.advisor + 0x108, 'I', TEST_MESSAGE)

    def cancel(now):
        arena.executed = 0
        clock['now'] = now
        stops.clear()
        return arena.call(0x718990, (TEST_MESSAGE,), this=queue.advisor).eax & 0xFF

    check(cancel(1_500), 1, 'a pending message is cancelled')
    check(tuple(slot[5] for slot in queue.slots()), (0, 0, 0, 0, 1, 0, 0, 0), 'its slots are cleared, others kept')
    check(len(stops), 0, 'a window that has ended stops nothing')
    check(struct.unpack('<IiBxxxII', bytes(arena.uc.mem_read(queue.history(TEST_MESSAGE), 20))), (0x40, 0, 1, 2, 3),
          'the history of a message that is not playing')
    check(cancel(1_499), 1, 'a playing message is cancelled')
    check(len(stops), 1, 'the speech is stopped')
    check(struct.unpack('<IiBxxxII', bytes(arena.uc.mem_read(queue.history(TEST_MESSAGE), 20))), (0x40, 0, 0, 3, 3),
          'the play flag is cleared and a slap counted')
    check(arena.read(queue.advisor + 0xE8, 'I'), 0, 'the window is zeroed')
    check(cancel(1_499), 0, 'with no slot and no window nothing is cancelled')
    arena.write(TEST_ROW + 8, 'I', rain_group)


def cases_from(path):
    cases = []
    for match in CASE_PATTERN.finditer(Path(path).read_text(encoding='utf-8')):
        values = [part.strip() for part in match.group(1).split(',')]
        if len(values) != 21:
            continue
        cases.append(tuple(int(value, 0) for value in values))
    return cases


def build_arena():
    """The sandbox of the queue routines, with the presentation clock and the speech stop stubbed."""
    image = Image.load(executable_path('tpi'))
    stops = []
    clock = {'now': 0}

    def stop(_):
        stops.append(1)
        return 0, 0
    arena = Sandbox(image, [
        (0x729530, 0x7392B1),
        (0x718820, 0x71893E), (0x718990, 0x718A6B), (0x718B40, 0x718DB2), (0x718E90, 0x718F4E), (0x718F80, 0x719058),
        (0x71B770, 0x71B7B6), (0x71B940, 0x71BA17), (0x71BA40, 0x71BA97), (0x71BAC0, 0x71BD9B), (0x71C030, 0x71C08D),
        (0x42A3B0, 0x42A41D), (0x448E50, 0x448E56), (0x448EA0, 0x448EB6), (0x501440, 0x50144E),
        *((address, address + 5) for address in (
            0x40AE2F, 0x40489A, 0x40B6A9, 0x40367A, 0x40B528, 0x40BB68, 0x404764, 0x40AC27, 0x40C789, 0x4048EA,
            0x403BD4, 0x406483, 0x40B618, 0x402F13, 0x407CBB, 0x4049BC,
        )),
    ], stubs={
        0x40C211: lambda _: (clock['now'], 0),  # Presentation clock.
        0x405FA1: stop,  # Speech stop 0x716950 (its presentation and rand() are P2's): captured, not run.
    }, budget=200_000)
    arena.set_fpcw(0x7F)

    return image, arena, clock, stops


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--write-table', type=Path)
    parser.add_argument('--table', type=Path, default=Path('content/data/tpi-advisor-descriptors.toml'))
    parser.add_argument('--check', type=Path, default=Path('source/OpenTPW.Tests/ThemeParkIncAdvisorTests.cs'))
    args = parser.parse_args()

    image, arena, clock, stops = build_arena()
    arena.call(0x729530)
    rows = native_rows(arena)
    targets = {}
    getters = {}
    for row in rows:
        target, _ = constant_score(image, row[4] & 0xFFFFFFFF)
        targets[row[4] & 0xFFFFFFFF] = target
        if getter_field(image, row[4] & 0xFFFFFFFF)[1] is not None:
            getters[row[4] & 0xFFFFFFFF] = target
    if args.write_table:
        args.write_table.write_text(HEADER + '\n' + '\n'.join(table_lines(image, rows)) + '\n]\n', encoding='utf-8')
        print(f'{len(rows)} rows written to {args.write_table}')
        return
    check_table(image, rows, args.table)
    check(len({row[1] for row in rows}), 606, 'distinct message ids')
    check(sum(1 for row in rows if row[0] == 0), 290, 'scan rows')
    check({row[11] for row in rows}, {2}, 'variant rules')

    constant_arena = Sandbox(image, [(0x729530, 0x7392B1), *((target, target + 6) for target in set(targets.values())),
                                     *((callback, callback + 5) for callback, target in targets.items() if callback != target)],
                             budget=200_000)
    constant_arena.call(0x729530)
    constants = check_constants(constant_arena, image, rows)
    getter_arena = Sandbox(image, [*((target, target + 7) for target in set(getters.values())),
                                   *((callback, callback + 5) for callback, target in getters.items() if callback != target)],
                           budget=200_000)
    fields = check_getters(getter_arena, image, rows)

    queue = Queue(arena)
    cases = cases_from(args.check)
    if not cases:
        raise AssertionError(f'{args.check} has no acceptance DataRows')
    for case in cases:
        check(acceptance(queue, case), case[-1], f'acceptance case {case}')
    check_enqueue_and_selection(arena, queue, clock, stops)
    print(f'{len(rows)} descriptor rows ({constants} constant scores, {fields} field getters), {len(cases)} acceptance cases, '
          '3 enqueue, 6 selection and 3 cancel scenarios match')


if __name__ == '__main__':
    main()
