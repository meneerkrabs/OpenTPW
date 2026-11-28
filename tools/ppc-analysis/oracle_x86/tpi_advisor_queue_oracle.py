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

from x86_harness import SENTINEL, Image, Sandbox, executable_path, x86

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


def check_senders(image):
    """The P4 senders' native paths the managed tests repeat (ThemeParkIncAdvisorSenderTests): the history callbacks 0x727410 and
    0x728580, the unread counter 0x54F1A0 and the stored-tutorial repeat 0x718AB0. Returns the number of cases checked."""
    cases = 0
    posts = []
    advisor_holder = {}

    def record(arena):
        # 0x71A930(message, locate word, locate data, variant, bypass, score) on the record at ecx: capture and build it
        record_at = arena.uc.reg_read(x86.UC_X86_REG_ECX)
        posts.append(tuple(arena.arg(index) for index in range(6)))
        arena.write(record_at + 0xC, 'I', arena.arg(0))
        return record_at, 0x18

    def enqueue(arena):
        posts.append(('enqueue', arena.arg(0)))
        return 1, 4

    arena = Sandbox(image, [
        (0x727410, 0x727436), (0x728580, 0x7285A9), (0x71B4F0, 0x71B506), (0x71B520, 0x71B536), (0x42A3B0, 0x42A41D),
        (0x54F1A0, 0x54F2C8), (0x4313D0, 0x4313DD), (0x718AB0, 0x718B13),
        *((address, address + 5) for address in (0x40694C, 0x406753, 0x40B528, 0x40C1F3)),
    ], stubs={
        0x4077F2: lambda _: (advisor_holder['advisor'], 0),  # 0x64EA00: the world's advisor thing
        0x40A592: record,
        0x40A600: enqueue,
    }, budget=200_000)
    career = arena.object(0x4740)
    arena.write(CAREER_GLOBAL, 'I', career)
    arena.write(0xC64DC7, 'B', 1)  # The scorer is constructed (no Advisor.sam load).
    advisor = arena.object(0x118)
    advisor_holder['advisor'] = advisor

    def history_played(message, played):
        arena.write(career + HISTORY + 20 * message + 8, 'B', played)

    for callback, message in ((0x727410, 0x1FA), (0x728580, 0x21D)):
        for played, expected in ((0, 10_000), (1, 0)):
            history_played(message, played)
            arena.executed = 0
            check(arena.call(callback, this=SCORER).eax, expected, f'callback {callback:#x} with {message:#x} played {played}')
            cases += 1

    # The unread counter: total +0, threshold +4, the category counts at [+8]; the HUD panel 0xA6011C is absent
    counts = arena.object(16)
    counter = arena.object(12, {0: ('I', 0), 4: ('I', 3), 8: ('I', counts)})
    arena.write(0xA6011C, 'I', 0)
    seen = []
    for letter in range(1, 13):
        posts.clear()
        arena.executed = 0
        arena.call(0x54F1A0, (2, 0), this=counter)
        for post in posts:
            if post[0] != 'enqueue':
                seen.append(post[0])
                check(post[1:], (0, 0, 0xFFFFFFFF, 0, 0xFFFFFFFF), 'locate 0, data 0, variant -1, bypass 0, score -1 (the descriptor\'s)')
        check(arena.read(counter, 'I'), letter, f'unread total after letter {letter}')
        cases += 1
    check(seen, [0x198, 0x15D, 0x15D], 'the counter posts 0x198 at 3, then 0x15D at 6 and 12')
    check(arena.read(counter + 4, 'I'), 24, 'the threshold doubles')
    posts.clear()
    arena.executed = 0
    arena.call(0x54F1A0, (2, 1), this=counter)
    check(posts, [], 'a silent count (a load) posts nothing')
    cases += 1

    # The stored-tutorial repeat: nothing stored refuses; a stored record takes HelpMessage.ContextHelpScore (0xC656C0), its explicit and bypass bytes
    arena.write(advisor + 0x100, 'B', 0)
    arena.executed = 0
    check(arena.call(0x718AB0, this=advisor).eax & 0xFF, 0, 'nothing stored')
    arena.write(advisor + 0x100, 'B', 1)
    arena.write(advisor + 0xEC + 0xC, 'I', 0x1FA)
    arena.write(0xC656C0, 'I', 900)
    posts.clear()
    arena.executed = 0
    check(arena.call(0x718AB0, this=advisor).eax & 0xFF, 1, 'a stored record is offered')
    check(posts, [('enqueue', advisor + 0xEC)], 'the stored record itself goes to 0x718820')
    check((arena.read(advisor + 0xEC + 0x10, 'i'), arena.read(advisor + 0xEC + 0x15, 'B'), arena.read(advisor + 0xEC + 0x16, 'B')), (900, 1, 1),
          'score, bypass and explicit bytes')
    cases += 2
    # The challenge senders' cancels: the time-out 0x5C2150 cancels word 4 of the sender's row at 0x9CD000, the uncalled 0x5C2100 word 1
    cancels = []

    def cancel(arena_):
        cancels.append(arena_.arg(0))
        return 0, 4
    challenge = Sandbox(image, [(0x5C2100, 0x5C213D), (0x5C2150, 0x5C218D)],
                        stubs={0x4077F2: lambda _: (advisor, 0), 0x4092AF: cancel}, budget=10_000)
    for entry, expected in ((0x5C2150, (0x23A, 0x25D, 0x246, 0x266, 0x256)), (0x5C2100, (0x239, 0x25C, 0x245, 0x265, 0x255))):
        cancels.clear()
        for sender in range(2, 7):
            challenge.executed = 0
            challenge.call(entry, (sender,))
            cases += 1
        check(tuple(cancels), expected, f'{entry:#x} cancels by sender 2 to 6')
    return cases


ROLES = {'Handyman': 0, 'Mechanic': 1, 'Entertainer': 2, 'Guard': 3, 'Researcher': 4, 'Gardener': 5}
# The message kinds' first virtual functions (mov eax, kind; ret) and their vtables.
KIND_VTABLES = {0x11: 0x93A334, 0x17: 0x93A370, 0x08: 0x93A2EC, 0x07: 0x93A2E0, 0x13: 0x93A34C}
KIND_FUNCTIONS = ((0x409C4B, 0x435A20), (0x40ADCB, 0x435CE0), (0x406E51, 0x435740), (0x40B0A5, 0x4356C0), (0x4085A8, 0x435B20))


def rows_of(text, method):
    """The DataRows of the C# test method `method`, as lists of their argument texts."""
    match = re.search(r'((?:\s*\[DataRow\([^\r\n]*\)\]\s*)+)public void ' + method + r'\(', text)
    if match is None:
        raise AssertionError(f'{method} has no DataRows')
    return [[part.strip() for part in row.split(',')] for row in re.findall(r'\[DataRow\(([^\r\n]*)\)\]', match.group(1))]


def number(value):
    value = value.rstrip('f')
    return ROLES[value.split('.')[-1]] if value.startswith('ThemeParkIncStaffRole.') else {'true': 1, 'false': 0}.get(value) if value in ('true', 'false') \
        else float(value) if '.' in value else int(value, 0)


def check_senders_b(image, test_text):
    """The P5 senders' native paths with arithmetic or a table (docs/reverse/TPI-ADVISOR.md section 9.3), against the DataRows of
    ThemeParkIncAdvisorSenderBTests: the requirements-met and lake callbacks, the status refresh 0x54D660, the advisor's message
    handler 0x719470 for kinds 0x11, 0x13, 0x17 and 8, the mechanics' HQ 0x5DAEB0 for kind 7, the wear tick's imminent test and
    the special rides' activation advice. Returns the number of cases checked."""
    cases = 0
    posts = []
    holder = {}

    def record(arena):
        record_at = arena.uc.reg_read(x86.UC_X86_REG_ECX)
        posts.append((arena.arg(0), arena.read(arena.uc.reg_read(x86.UC_X86_REG_ESP) + 8, 'H'), arena.arg(2), arena.arg(3), arena.arg(4), arena.arg(5)))
        arena.write(record_at + 0xC, 'I', arena.arg(0))
        return record_at, 0x18

    def enqueue(_):
        return 1, 4

    def ignore(_):
        return 0, 0

    common = {0x4077F2: lambda _: (holder['advisor'], 0), 0x40A592: record, 0x40A600: enqueue, 0x403E45: ignore, 0x401983: ignore}
    thunks = (0x40694C, 0x406753, 0x40B528, 0x40C1F3, 0x4041B0, 0x402F40, 0x40A457, 0x40A268, 0x406CAD, 0x402B7B)

    # The requirements-met callbacks 0x726FA0 (0x1C7) and 0x726F50 (0x1E3) and the lake's 0x729020 (0x26D)
    met = {'value': 0}
    callbacks = Sandbox(image, [
        (0x726F50, 0x726F8B), (0x726FA0, 0x726FDB), (0x729020, 0x729049), (0x723250, 0x723257), (0x71B4F0, 0x71B506), (0x71B520, 0x71B536),
        (0x42A3B0, 0x42A41D), *((address, address + 5) for address in thunks),
    ], stubs={**common, 0x4051AA: lambda _: (met['value'], 0)}, budget=10_000)
    career = callbacks.object(0x4740)
    callbacks.write(CAREER_GLOBAL, 'I', career)
    holder['advisor'] = callbacks.object(0x118)
    for callback, message in ((0x726FA0, 0x1C7), (0x726F50, 0x1E3)):
        for word in (0, 7, 0x40000001, 0x7FFFFFFF):
            callbacks.write(SCORER + 0x7B8, 'i', word if word < 0x80000000 else word - 0x100000000)
            for flag in (0, 1):
                for played in (0, 1):
                    met['value'] = flag
                    callbacks.write(career + HISTORY + 20 * message + 8, 'B', played)
                    callbacks.executed = 0
                    expected = 0 if played or not flag else (word << 1) & 0xFFFFFFFF
                    check(callbacks.call(callback, this=SCORER).eax & 0xFFFFFFFF, expected, f'{callback:#x}: word {word:#x}, met {flag}, played {played}')
                    cases += 1
    for played, expected in ((0, 10_000), (1, 0)):
        callbacks.write(career + HISTORY + 20 * 0x26D + 8, 'B', played)
        callbacks.executed = 0
        check(callbacks.call(0x729020, this=SCORER).eax, expected, f'0x729020 with 0x26D played {played}')
        cases += 1

    # The status refresh 0x54D660: the requirements (0x55A620) and the career's counter +0x105C pick the advice
    refresh = Sandbox(image, [(0x54D660, 0x54D72D), (0x42A3B0, 0x42A41D), (0x4313D0, 0x4313DD), *((address, address + 5) for address in thunks)],
                      stubs={**common, 0x4051AA: lambda _: (met['value'], 0), 0x402D3D: ignore}, budget=10_000)
    status_career = refresh.object(0x4740)
    refresh.write(CAREER_GLOBAL, 'I', status_career)
    refresh.write(0xA1465C, 'B', 0x16)  # the interface's branch to 0x7BAE30 (presentation)
    holder['advisor'] = refresh.object(0x118)
    for row in rows_of(test_text, 'TheStatusRefreshPicksTheCountersAdvice'):
        counter, expected = (number(value) for value in row)
        for flag in (1, 0):
            met['value'] = flag
            refresh.write(status_career + 0x105C, 'I', counter)
            posts.clear()
            refresh.executed = 0
            refresh.call(0x54D660)
            check([post[0] for post in posts], [expected] if flag else [], f'0x54D660 at counter {counter}, met {flag}')
            if flag:
                check(posts[0][1:], (0, 0, 0xFFFFFFFF, 0, 0xFFFFFFFF), 'locate 0, data 0, variant -1, bypass 0, score -1')
            cases += 1

    # The advisor's message handler 0x719470 for the kinds 0x11, 0x13, 0x17 and 8
    handler = Sandbox(image, [
        (0x719470, 0x71A40B), (0x7190A0, 0x7191E4), (0x719270, 0x7192F9), (0x4313D0, 0x4313DD), (0x42A3B0, 0x42A41D), (0x4F9420, 0x4F9436),
        (0x62B7D0, 0x62B7E4), *((function, function + 6) for _, function in KIND_FUNCTIONS), *((thunk, thunk + 5) for thunk, _ in KIND_FUNCTIONS),
        *((address, address + 5) for address in thunks),
    ], stubs=common, budget=50_000)
    advisor = handler.object(0x118)
    holder['advisor'] = advisor
    handler_career = handler.object(0x4740)
    handler.write(CAREER_GLOBAL, 'I', handler_career)
    handler.write(0xC64DC7, 'B', 1)  # The scorer is constructed (no Advisor.sam load).
    handler.write(0xC64DBE, 'B', 1)  # The game object 0xC65B80 is constructed.
    world = handler.object(0x1EAB90)
    handler.write(WORLD_GLOBAL, 'I', world)
    handler.write(0xAA131C, 'I', handler.object(4, {0: ('I', world)}))
    mechanic = handler.object(0x600)
    handler.write(0xAA18B0 + 20 * 7, 'I', mechanic)

    def message(kind, word8, dword_c=0, dword_10=0):
        return handler.object(0x14, {0: ('I', KIND_VTABLES[kind]), 8: ('I', word8), 0xC: ('I', dword_c), 0x10: ('I', dword_10)})

    def handle(item):
        posts.clear()
        handler.executed = 0
        handler.call(0x719470, (item,), this=advisor)
        return [post[0] for post in posts]

    for row in rows_of(test_text, 'TheResearchCompletionAdviceFollowsTheCategory'):
        category, upgrader, played, expected = (number(value) for value in row)
        handler.write(world + 0x1EAB80, 'H', 7 if upgrader else 0)
        handler.write(mechanic + 0x218, 'I', 1)
        handler.write(mechanic + 0x5D0, 'H', 0)
        handler.write(handler_career + HISTORY + 20 * 0x18C + 8, 'B', played)
        check(handle(message(0x11, 1160, category & 0xFFFFFFFF)), [] if expected == -1 else [expected], f'kind 0x11, category {category}, upgrader {upgrader}, played {played}')
        if expected != -1:
            check(posts[0][1:3], (0, 1160), 'locate 0, the object as locate dword')
        cases += 1
    for row in rows_of(test_text, 'TheStrikeAdviceIsTheReasonsOrTheKindsPlusTheRolesWord'):
        role, kind, reason, expected = (number(value) for value in row)
        check(handle(message(0x17, role, kind, reason)), [] if expected == -1 else [expected], f'kind 0x17, role {role}, kind {kind}, reason {reason}')
        cases += 1
    for sub, mode, expected in ((2, 0, [0x66]), (3, 0, [0x77]), (4, 0, [0x78]), (0, 0, []), (0, 2, [0xFF]), (1, 0, []), (5, 0, []), (6, 0, []), (9, 0, [])):
        handler.write(0xC65B80, 'I', mode)
        check(handle(message(0x13, sub)), expected, f'kind 0x13 case {sub}, mode {mode}')
        cases += 1
    handler.write(0xC65B80, 'I', 0)
    check(handle(message(0x08, 0x2A)), [0xDB], 'kind 8: the condemned ride')
    check(posts[0][1:3], (0x2A, 0), 'the ride as locate word')
    cases += 1

    # The mechanics' HQ 0x5DAEB0 for kind 7: 0x41 without mechanics, else 0x42 unless one goes to the ride or is free
    hq = Sandbox(image, [(0x5DAEB0, 0x5DB076), (0x62B010, 0x62B078), (0x5E3B70, 0x5E3B74), (0x4313D0, 0x4313DD), (0x4356C0, 0x4356C6), (0x40B0A5, 0x40B0AA),
                         (0x4086AC, 0x4086B1),
                         *((address, address + 5) for address in thunks)], stubs=common, budget=20_000)
    holder['advisor'] = hq.object(0x118)
    hq_world = hq.object(0x1EAB90)
    hq.write(WORLD_GLOBAL, 'I', hq_world)
    ride = hq.object(0x100, {0x16: ('H', 1101)})
    # the 0x43 branch reads the request through the thunk 0x4086AC
    hq.write(0xAA18B0 + 20 * 0x2A, 'I', ride)
    worker = hq.object(0x600)
    hq.write(0xAA18B0 + 20 * 9, 'I', worker)
    broken = hq.object(0x14, {0: ('I', KIND_VTABLES[0x07]), 8: ('I', 0x2A)})
    for mechanics, target, state, requested, expected in ((0, 0, 0, 0, [0x41]), (1, 0x2A, 21, 0, [0x43]), (1, 0x2A, 21, 1, []), (1, 0, 0, 0, []), (1, 0, 2, 0, [0x42]),
                                                          (1, 0, 4, 0, [0x42]), (1, 0x30, 0, 0, [0x42]), (1, 0, 0x22, 0, [])):
        hq.write(ride + 0x78, 'I', requested)
        hq.write(hq_world + 0x1EAB80, 'H', 9 if mechanics else 0)
        hq.write(worker + 0x5D8, 'H', target)
        hq.write(worker + 0x1B4, 'I', state)
        hq.write(worker + 0x5D0, 'H', 0)
        posts.clear()
        hq.executed = 0
        hq.call(0x5DAEB0, (broken,), this=hq.object(0x100))
        check([post[0] for post in posts], expected, f'0x5DAEB0: mechanics {mechanics}, target {target:#x}, state {state:#x}, requested {requested}')
        if expected:
            check(posts[0][1:3], (0x2A, 1101), 'the ride as locate word, its type as locate dword')
        cases += 1

    # The wear tick's imminent test (0x5E276E..0x5E27A2, at the 24-bit control word): the post path or the two returns
    outcome = {}

    def mark(name):
        def stub(arena):
            outcome['path'] = name
            arena.write(arena.uc.reg_read(x86.UC_X86_REG_ESP), 'I', SENTINEL)
            return 0, 0
        return stub
    wear = Sandbox(image, [(0x5E276E, 0x5E27A2), (0x86FAB0, 0x86FAD7)],
                   stubs={0x5E27A2: mark('post'), 0x5E27F3: mark('none'), 0x5E27F5: mark('none')}, budget=1_000)
    item = wear.object(0x100)
    kind = wear.object(0x400)
    extra = [['39', '0.99999994f', '40f', 'true'], ['39', '1f', 'NaN', 'false'], ['39', 'NaN', '40f', 'false']]
    for row in rows_of(test_text, 'TheImminentAdviceNeedsTheRepairByteToCrossItsLine') + extra:
        repair = number(row[0])
        worn = float('nan') if row[1] == 'NaN' else number(row[1])
        imminent = float('nan') if row[2] == 'NaN' else number(row[2])
        expected = number(row[3])
        wear.reset()
        wear.set_fpcw(0x7F)
        wear.write(item + 0x54, 'f', float(repair))
        wear.write(kind + 0x3C8, 'f', imminent)
        wear.call(0x5E276E, (0, 0, 0, struct.unpack('<I', struct.pack('<f', worn))[0], 0, 0, 0, 0), regs={'esi': item, 'ebx': kind})
        check(outcome.pop('path') == 'post', bool(expected), f'imminent test: byte {repair}, wear {worn}, line {imminent}')
        cases += 1

    # The special rides' activation advice (0x6240D0..0x624127): 0x21F for a type record whose +0x10 is 0x488, else 0x1E7
    record_of = {}
    special = Sandbox(image, [(0x6240D0, 0x624127), (0x4313D0, 0x4313DD), (0x40C1F3, 0x40C1F8)],
                      stubs={**common, 0x4022F2: lambda _: (record_of['record'], 0)}, budget=1_000)
    holder['advisor'] = special.object(0x118)
    for row in rows_of(test_text, 'ASpecialRideReadyToActivateNamesItsAdvice'):
        info, expected = (number(value) for value in row)
        record_of['record'] = special.object(0x20, {0x10: ('I', info)})
        posts.clear()
        special.executed = 0
        special.call(0x6240D0, (0, 0, 0, 0, 0, 0, 0, 0), stop=0x624127)
        check([post[0] for post in posts], [expected], f'0x6240D0 for Info.Id {info}')
        cases += 1
    return cases


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
    parser.add_argument('--senders', type=Path, default=Path('source/OpenTPW.Tests/ThemeParkIncAdvisorSenderBTests.cs'))
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
    senders = check_senders(image)
    senders_b = check_senders_b(image, args.senders.read_text(encoding='utf-8'))
    print(f'{len(rows)} descriptor rows ({constants} constant scores, {fields} field getters), {len(cases)} acceptance cases, '
          f'3 enqueue, 6 selection and 3 cancel scenarios, {senders} P4 sender cases and {senders_b} P5 sender cases match')


if __name__ == '__main__':
    main()
