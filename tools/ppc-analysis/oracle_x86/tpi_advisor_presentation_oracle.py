#!/usr/bin/env python3
"""Verify TPI's advisor presentation against the executable.

GenerateAnimSequence 0x715AB0, SetCannedSequence 0x715BC0, the speech 0x716230, the stop 0x716950
and the per-frame advisor 0x716ED0 run in the sandbox with the C runtime's rand() (0x870D38)
replaced by the CRT generator from a given seed, a synthetic clip table written to 0xBEF0D0 and
the presentation clock, the sound engine, the LIP file and the model stubbed. Each case and its
expected outcome is a DataRow of source/OpenTPW.Tests/ThemeParkIncAdvisorPresentationTests.cs
(--check); --golden prints the native outcome of every case's inputs instead. No game, audio or
UI is launched.
"""
import argparse
import re
import struct
from pathlib import Path

from x86_harness import Image, Sandbox, executable_path

DURATIONS = 0xBEF0D0
CLIPS = 0xBEF134
RESPONSES = 0xBEF1F0
SPEAKING = 0xBEF074
BASE = 0xBEF06C
END = 0xBEF070
VOICE_DUE = 0xBEF09C
HANDLE = 0xBEF088
TALKING = 0xBEF078
TIMER = 0xBEF194
HEAD_HIDDEN = 0xBF7240
HEAD_SUPPRESSED = 0xBF7241
ADVISOR_ON = 0xA10218
HELP_RESPONSE = 0x9DDBE8
ANIMATION_WORD = 0xA9D230
GAME_KEYS = 0xA07A28
LIP_LOADED = 0xBEF1B8
LIP_BUFFER = 0xBEF1BC
LIP_INDEX = 0xBEF1B4
LIP_NEXT = 0xBEF190
MOUTH = 0xBEF1A4
MOUTH_NEXT = 0xBEF1AC
SHOWN_MOUTH = 0xBEF1A8
MOUTHS = 0xBEF18C
STEPPED = 0xBEF098
HANDLE_VALUE = 0x55

# The response rows the managed tests use: (response, voice, LIP, animation, +0x10, +0x14, +0x18, tag) as 0x715570 builds them.
RESPONSE_ROWS = {
    642: (642, 763, 763, 25, 1, -1, 0, 62),
    645: (645, 766, 766, 25, 1, -1, 0, 65),
    748: (748, 868, 868, -1, 0, 666, 0, 0x97),
    749: (749, 869, 869, 25, 1, -1, 0, 0x5E),
}

TEST = Path('source/OpenTPW.Tests/ThemeParkIncAdvisorPresentationTests.cs')
WORDS = re.compile(r'TrailerWords\s*=\s*\{([^}]*)\}')
ROW = re.compile(r'^\s*\[DataRow\(([^\r\n]+)\)\]\s*$')
METHOD = re.compile(r'public void (\w+)\(')

# The inputs of --golden: one tuple of the inputs of each case per method.
GOLDEN = {
    'GeneratedSequenceMatchesTheExecutable': [(0, 1), (1, 1), (501, 1), (501, 7), (6000, 1), (6000, 12345), (14000, 3), (100000, 1), (-5, 1)],
    'CannedSequenceMatchesTheExecutable': [(row, 5000, 1) for row in range(20)] + [(9, 0, 1), (17, 0, 1), (17, 3000, 9)],
    'SpeechMatchesTheExecutable': [
        (645, 5526, 1, 0, 0, 0, 10000, 1), (642, 5758, 1, 0, 0, 0, 2000, 1), (748, 20433, 1, 0, 0, 0, 10000, 1), (748, 20433, 1, 0, 0, 0, 10000, 99),
        (748, -1, 1, 0, 0, 0, 10000, 1), (645, -1, 1, 0, 0, 0, 10000, 1), (749, 13978, 1, 0, 0, 1, 50, 1), (645, 5526, 1, 0, 1, 0, 10000, 1),
        (645, 5526, 0, 0, 0, 0, 10000, 1), (645, 5526, 0, 1, 0, 0, 10000, 1), (748, 20433, 0, 1, 0, 0, 10000, 5)],
    'StopMatchesTheExecutable': [(1, 1, 0, 0, 1), (1, 1, 0, 0, 2), (1, 1, 0, 0, 3), (1, 0, 0, 0, 1), (1, 1, 1, 0, 1), (1, 1, 0, 1, 1), (0, 1, 0, 0, 1)],
    'FramesMatchTheExecutable': [
        ('1141088;1539410;2441133;2839455;5441950', 9800, 9800, 31, 200, 1, 0),
        ('1141088;1539410;2441133;2839455;5441950', 9800, 9800, 124, 60, 7, 0),
        ('0;400000;900000', 1000, 1000, 31, 40, 1, 2000),
        ('250000', 500, 400, 62, 12, 3, 0)],
}


def check(actual, expected, label):
    if actual != expected:
        raise AssertionError(f'{label}: expected {expected!r}, got {actual!r}')


class Crt:
    """The C runtime's rand(): seed × 214013 + 2531011, bits 16..30."""

    def __init__(self, seed):
        self.seed = seed & 0xFFFFFFFF
        self.draws = 0

    def __call__(self, _arena):
        self.seed = (self.seed * 0x343FD + 0x269EC3) & 0xFFFFFFFF
        self.draws += 1
        return (self.seed >> 16) & 0x7FFF, 0


def trailer_words(text):
    match = WORDS.search(text)
    if not match:
        raise AssertionError(f'{TEST} has no TrailerWords')
    return [int(word.strip().rstrip('u'), 0) for word in match.group(1).split(',') if word.strip()]


def durations(words):
    """The table 0x715966 fills: (word × 1000) / 30 per clip, clip 14 zeroed (0xBEF104)."""
    table = [((word * 1000) & 0xFFFFFFFF) // 30 for word in words]
    table[13] = 0
    return table


def value(text):
    text = text.strip()
    if text in ('true', 'false'):
        return int(text == 'true')
    if text.startswith('"'):
        return text.strip('"')
    return int(text.rstrip('u'), 0)


def cases_from(text):
    cases, pending = {}, []
    for line in text.splitlines():
        if (row := ROW.match(line)):
            pending.append(tuple(value(part) for part in re.findall(r'\s*("[^"]*"|[^,]+)', row.group(1))))
        elif (method := METHOD.search(line)):
            if pending:
                cases[method.group(1)] = pending
            pending = []
    return cases


class Presentation:
    def __init__(self, image, words):
        self.table = durations(words)
        self.rand = Crt(1)
        self.now = 0
        self.plays = []
        self.lengths = {}
        self.stops = []
        self.model = None
        self.arena = Sandbox(image, [
            (0x715AB0, 0x715B78), (0x715BC0, 0x715C6E), (0x403C8D, 0x403C92), (0x40A51A, 0x40A51F),
            (0x716230, 0x7167E0), (0x40A574, 0x40A579), (0x54D3E0, 0x54D45D), (0x406A14, 0x406A19), (0x5D0820, 0x5D082C),
            (0x716950, 0x716B52), (0x716ED0, 0x717278),
        ], stubs={
            0x870D38: lambda arena: self.rand(arena),
            0x403E45: lambda _: (0, 0),  # log
            0x409674: lambda _: (0, 0),  # the presenter (0x54D4D0): no window shows the speech
            0x40B460: lambda _: (0, 0),
            0x40C211: lambda _: (self.now, 0),  # presentation clock
            0x40C57C: lambda _: (0, 0),  # not paused
            0x40823D: lambda _: (0, 0), 0x404962: lambda _: (0, 0),  # head nodes
            0x86F844: lambda _: (0, 0),  # sprintf of the LIP name
            0x40399F: lambda _: (0, 0),  # the LIP file does not open
            0x40C99B: lambda _: (0, 0), 0x40B6CC: lambda _: (0, 0), 0x40B0CD: lambda _: (0, 0), 0x4010EB: lambda _: (0, 0),
            0x402DAB: self.play,
            0x40A45C: lambda arena: (self.lengths.get(arena.arg(0), 1), 0),  # the sample length (1 without a playing sound)
            0x40CF1D: lambda arena: (self.stops.append(arena.arg(0)), (0, 0))[1],
            0x403B2A: lambda _: (0, 0), 0x4057AE: lambda _: (0, 0), 0x4069E2: lambda _: (0, 0), 0x86FF86: lambda _: (0, 0),
            0x40C63A: lambda arena: (arena.arg(0), 0),  # the voice still plays
            0x40A8FD: lambda _: (0, 0), 0x401456: lambda _: (0, 0),
            0x40A597: lambda _: (self.model, 0),
        }, budget=400_000)
        self.arena.set_fpcw(0x7F)

    def play(self, arena):
        self.plays.append((arena.arg(1), arena.arg(2)))
        return self.handle, 0

    def reset(self, seed):
        arena = self.arena
        arena.reset()
        arena.set_fpcw(0x7F)
        for index, length in enumerate(self.table):
            arena.write(DURATIONS + 4 * index, 'I', length)
        self.rand = Crt(seed)
        self.plays, self.stops = [], []
        self.handle = HANDLE_VALUE
        nodes = arena.object(0x400)
        inner = arena.object(0x80, {0x70: ('I', nodes)})
        outer = arena.object(0x10, {4: ('I', inner)})
        self.model = arena.object(0x10, {8: ('I', outer)})
        arena.write(GAME_KEYS, 'I', arena.object(8, {6: ('H', 1)}))
        arena.write(ADVISOR_ON, 'B', 1)
        arena.write(HELP_RESPONSE, 'I', 0x3F6)
        arena.write(MOUTHS, 'I', 5)  # the init 0x715850's 0xBEF18C

    def clips(self):
        found = []
        for index in range(64):
            word = self.arena.read(CLIPS + 4 * index, 'I')
            if word == 0:
                return found
            found.append(word & 0xFF80FFFF)
        raise AssertionError('clip list without a terminator')

    def generated(self, budget, seed):
        self.reset(seed)
        total = self.arena.call(0x715AB0, (budget & 0xFFFFFFFF,)).eax
        return (total, ','.join(map(str, self.clips())), self.rand.draws)

    def canned(self, row, budget, seed):
        self.reset(seed)
        self.arena.write(VOICE_DUE, 'I', 1)
        total = self.arena.call(0x715BC0, (row, budget)).eax
        timer = self.arena.read(TIMER, 'I')
        return (total, ','.join(map(str, self.clips())), timer if timer else -1, int(self.arena.read(VOICE_DUE, 'I') == 0), self.rand.draws)

    def speech(self, response, voice, advisor_on, help_flag, speaking, animation, now, seed):
        self.reset(seed)
        arena = self.arena
        arena.uc.mem_write(RESPONSES, struct.pack('<8i', *RESPONSE_ROWS[response]))
        arena.write(RESPONSES + 0x20, 'I', 0x270F)
        arena.write(ADVISOR_ON, 'B', advisor_on)
        arena.write(HELP_RESPONSE, 'I', response if help_flag else 0x3F6)
        arena.write(SPEAKING, 'I', speaking)
        arena.write(ANIMATION_WORD, 'H', animation)
        self.now = now
        self.handle = HANDLE_VALUE if voice >= 0 else 0
        self.lengths = {HANDLE_VALUE: voice} if voice >= 0 else {}
        duration = arena.call(0x716230, (response,)).eax
        if duration == 0:
            return (0, 0, 0, 0, 0, 0, self.rand.draws)
        live = int(arena.read(HANDLE, 'I') != 0)
        return (duration, arena.read(BASE, 'I'), arena.read(END, 'I'), arena.read(VOICE_DUE, 'I'), live, arena.read(HEAD_HIDDEN, 'B'), self.rand.draws)

    def stop(self, speaking, live, hidden, suppressed, seed):
        self.reset(seed)
        arena = self.arena
        arena.write(SPEAKING, 'I', speaking)
        arena.write(HANDLE, 'I', HANDLE_VALUE if live else 0)
        arena.write(HEAD_HIDDEN, 'B', hidden)
        arena.write(HEAD_SUPPRESSED, 'B', suppressed)
        arena.call(0x716950)
        sounds = [sound for bank, sound in self.plays]
        check(len(sounds) <= 1, True, 'one stop sound at most')
        return (sounds[0] if sounds else 0, self.rand.draws)

    def frames(self, marks, base, start, step, count, seed, mouth_next):
        self.reset(seed)
        arena = self.arena
        values = [int(mark) for mark in marks.split(';')]
        buffer = arena.object(4 * (len(values) + 1))
        for index, mark in enumerate(values + [-1]):
            arena.write(buffer + 4 * index, 'i', mark)
        arena.write(SPEAKING, 'I', 1)
        arena.write(HANDLE, 'I', 0)
        arena.write(END, 'I', 0x7FFFFFFF)
        arena.write(STEPPED, 'I', 1)
        arena.write(LIP_LOADED, 'I', 1)
        arena.write(LIP_BUFFER, 'I', buffer)
        arena.write(LIP_INDEX, 'I', 1)
        arena.write(LIP_NEXT, 'i', base + int(values[0] / 1000))
        arena.write(TALKING, 'I', 1)
        arena.write(BASE, 'I', base)
        arena.write(MOUTH_NEXT, 'i', mouth_next)
        arena.write(SHOWN_MOUTH, 'i', -1)
        mouths = []
        for frame in range(count):
            self.now = start + frame * step
            arena.executed = 0
            arena.call(0x716ED0)
            mouths.append(arena.read(MOUTH, 'i'))
        return (','.join(map(str, mouths)), arena.read(TALKING, 'I'), self.rand.draws)


RUNNERS = {
    'GeneratedSequenceMatchesTheExecutable': (2, 'generated'),
    'CannedSequenceMatchesTheExecutable': (3, 'canned'),
    'SpeechMatchesTheExecutable': (8, 'speech'),
    'StopMatchesTheExecutable': (5, 'stop'),
    'FramesMatchTheExecutable': (7, 'frames'),
}


def literal(item):
    if isinstance(item, str):
        return f'"{item}"'
    return str(item)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', type=Path, default=TEST)
    parser.add_argument('--golden', action='store_true')
    args = parser.parse_args()
    text = args.check.read_text(encoding='utf-8')
    presentation = Presentation(Image.load(executable_path('tpi')), trailer_words(text))
    if args.golden:
        for method, (inputs, runner) in RUNNERS.items():
            print(method)
            for case in GOLDEN[method]:
                outcome = getattr(presentation, runner)(*case)
                print(f'\t[DataRow( {", ".join(literal(item) for item in (*case, *outcome))} )]')
        return
    cases = cases_from(text)
    total = 0
    for method, (inputs, runner) in RUNNERS.items():
        found = cases.get(method, [])
        if not found:
            raise AssertionError(f'{args.check} has no DataRows for {method}')
        for case in found:
            check(getattr(presentation, runner)(*case[:inputs]), tuple(case[inputs:]), f'{method} {case[:inputs]}')
        total += len(found)
    print(f'{total} presentation cases match: ' + ', '.join(f'{len(cases[method])} {runner}' for method, (_, runner) in RUNNERS.items()))


if __name__ == '__main__':
    main()
