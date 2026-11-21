#!/usr/bin/env python3
"""Verify TPI's four weather advice descriptors and their linked response metadata.

Only bounded native initializers, score/accessor calls, caption lookups and
queue admission run. No game or audio is launched.
"""
import struct

from x86_harness import Image, Sandbox, executable_path, x86

# Message ID, descriptor address, response ID, primary/alternate speech IDs,
# tag ID, TAG_SYSTEM text entry.
FORECASTS = (
    (0x154, 0x9E3D88, 642, 763, 763, 62, 127),
    (0x157, 0x9E3E30, 645, 766, 766, 65, 130),
    (0x15A, 0x9E3ED8, 648, 769, 769, 68, 133),
    (0x15B, 0x9E3F10, 649, 938, 769, 71, 156),
)


def check(actual, expected, label):
    if actual != expected:
        raise AssertionError(f'{label}: expected {expected!r}, got {actual!r}')


def check_rain_queue(arena):
    # Minimal state for the native singleton getters. The clock values and the
    # group-0 interval are test inputs, not claims about retail default timing.
    message = 0x157
    history = arena.object(0x4740)
    row = history + 0x14F0 + 20 * message
    world_clock = arena.object(4)
    arena.write(0xBFC484, 'I', history)
    arena.write(0xADE6B0, 'I', world_clock - 0x1EAB40)
    arena.write(0xC64DC7, 'B', 1)
    arena.write(0xA10219, 'B', 0)
    arena.write(0xC65220 + 0x2C, 'I', 5)
    arena.write(0xC65220 + 0x3C, 'I', 0)
    arena.write(0xC65220 + 0x40, 'I', 0)
    advice = arena.object(24, {0: ('H', 79), 0xC: ('I', message), 0x10: ('i', 10_000), 0x14: ('B', 1)})
    payload = bytes(arena.uc.mem_read(advice, 24))

    def replay(label, queued, previous, current, accepted, written_slot):
        arena.executed = 0
        advisor = arena.object(0x110)
        first_slot = advisor + 0x1C
        arena.write(row, 'I', previous << 2)
        arena.write(row + 0x10, 'I', 0)
        arena.write(world_clock, 'I', current << 2)
        for i, score in enumerate(queued):
            slot = first_slot + 24 * i
            arena.write(slot + 0xC, 'I', message)
            arena.write(slot + 0x10, 'i', score)
            arena.write(slot + 0x14, 'B', 1)
        before = [bytes(arena.uc.mem_read(first_slot + 24 * i, 24)) for i in range(8)]
        result = arena.call(0x718820, (advice,), this=advisor)
        check(result.eax & 0xFF, accepted, f'{label} reported result')
        if written_slot is not None:
            before[written_slot] = payload
        check(bytes(arena.uc.mem_read(first_slot, 8 * 24)), b''.join(before), f'{label} queue bytes')

    replay('first forecast', (), 0, 0, 1, 0)
    replay('already pending forecast', (10_000,), 0, 0, 1, 1)
    replay('one tick before the repeat boundary', (), 100, 104, 0, None)
    replay('at the repeat boundary', (), 100, 105, 1, 0)
    replay('full queue with an equal score', (10_000,) * 8, 0, 0, 1, None)
    replay('full queue with a lower score', (10_000, 9_999) + (10_000,) * 6, 0, 0, 1, 1)
    arena.write(advice + 0x10, 'i', -1)
    payload = bytes(arena.uc.mem_read(advice, 24))
    replay('ineligible score can still enter an empty slot', (), 0, 0, 1, 0)


def check_rain_playback(arena, presentation):
    # No other scan candidate is supplied. Audio returns a controlled duration;
    # the native update still owns selection, the window, and the history writes.
    advisor = arena.object(0x118)
    history = arena.object(0x4740)
    world_clock = arena.object(4, {0: ('I', 200 << 2)})
    message = 0x157
    record = advisor + 0x1C
    repeat = record + 24
    history_row = history + 0x14F0 + 20 * message
    arena.write(0xBFC484, 'I', history)
    arena.write(0xADE6B0, 'I', world_clock - 0x1EAB40)
    arena.write(0xA09728, 'I', arena.object(4))
    arena.write(0xA568BC, 'I', 0)
    arena.write(0xA9D230, 'H', 0)
    arena.write(0xC65B80, 'I', 0)
    arena.write(0x9DDBEC, 'I', 0x285)
    arena.write(history_row + 4, 'i', -1)
    arena.write(advisor + 0xE4, 'I', 1_000)
    arena.write(advisor + 0xE8, 'I', 200)
    for slot in (record, repeat):
        arena.write(slot + 8, 'i', -1)  # Choose a variant from the previous-play record.
        arena.write(slot + 0xC, 'I', message)
        arena.write(slot + 0x10, 'i', 10_000)
        arena.write(slot + 0x14, 'B', 1)

    def step(now, minimum, expected_calls, pending):
        presentation['now'] = now
        presentation['calls'].clear()
        arena.write(0xC65240, 'i', minimum)
        arena.executed = 0
        arena.call(0x718190, this=advisor)
        check(presentation['calls'], expected_calls, f'playback handoffs at {now}/{minimum}')
        check(tuple(arena.read(slot + 0x14, 'B') for slot in (record, repeat)), pending,
              f'playback pending slots at {now}/{minimum}')

    step(1_199, 10, [], (1, 1))
    step(1_200, 10_000, [], (1, 1))  # Exact equality does not pass the minimum score.
    line = [('speech', 645), ('tag', 65)]
    step(1_200, 10, line, (0, 1))  # Earliest equal-scoring slot wins.
    check(arena.read(advisor + 0xE4, 'I'), 1_200, 'playback start clock')
    check(arena.read(advisor + 0xE8, 'I'), 3_750, 'speech duration plus 1,000')
    check(tuple(arena.read(history_row + offset, 'I') for offset in (0, 4, 8, 0x10)),
          (200 << 2, 0, 1, 1), 'first playback history')
    step(4_949, 10, [], (0, 1))
    arena.write(world_clock, 'I', 215 << 2)
    step(4_950, 10, line, (0, 0))  # The one-response cycle stays at variant zero.
    check(tuple(arena.read(history_row + offset, 'I') for offset in (0, 4, 8, 0x10)),
          (215 << 2, 0, 1, 2), 'second playback history')


def check_eligibility_matrix(arena, campaign_thing):
    # The heatwave row's class field is patched so that every context shows which class it admits.
    # The inputs are the mode word, the two game-object flags, and the animation word with the
    # variant the resolved object carries at +0x24.
    class_field = 0x9E3D88 + 0xC
    for label, setup, admitted in (
        ('mode 0', (0, 0, 0, 0, 0), (0, 2)),
        ('mode 1', (1, 0, 0, 0, 0), (2,)),
        ('mode 2', (2, 0, 0, 0, 0), (1, 2)),
        ('mode 3', (3, 0, 0, 0, 0), (3,)),
        ('game flag 2E', (0, 1, 0, 0, 0), (5,)),
        ('game flag 2F', (0, 0, 1, 0, 0), (7,)),
        ('animation variant 0', (0, 0, 0, 1, 0), (6,)),
        ('animation variant 1', (0, 0, 0, 1, 1), (5,)),
        ('animation variant 2', (0, 0, 0, 1, 2), (4,)),
    ):
        mode, flag_2E, flag_2F, word, variant = setup
        arena.write(0xC65B80, 'I', mode)
        arena.write(0xA6E158 + 0x2E, 'B', flag_2E)
        arena.write(0xA6E158 + 0x2F, 'B', flag_2F)
        arena.write(0xA9D230, 'H', word)
        arena.write(campaign_thing + 0x24, 'I', variant)
        seen = []
        for klass in range(8):
            arena.executed = 0
            arena.write(class_field, 'I', klass)
            if arena.call(0x71BDC0, (0x154,)).eax & 0xFF:
                seen.append(klass)
        check(tuple(seen), admitted, f'classes admitted in {label}')
    arena.write(class_field, 'I', 2)
    arena.write(0xA9D230, 'H', 0)
    arena.write(0xA6E158 + 0x2E, 'B', 0)
    arena.write(0xA6E158 + 0x2F, 'B', 0)


def check_notification_messages(arena):
    # A type-25 notification carries advice message 0x1A2 (id 8, the bank's six-month red mail), 0x1A1 (id 6)
    # or 0x1A3 (id 7, the challenge system's lost-challenge mail). Their descriptor, response and tag rows use
    # the same tables as the forecasts and differ only in those.
    for message, descriptor, response, speech, tag, text in (
        (0x1A1, 0x9E4840, 0x2EC, 868, 0x97, None),
        (0x1A2, 0x9E4878, 0x2ED, 869, 0x5E, 166),
        (0x1A3, 0x9E48B0, 0x2EE, 870, 0x5F, None),
    ):
        arena.executed = 0
        check(arena.read(descriptor, 'I'), 1, f'message {message:#x} explicit row')
        check(arena.read(descriptor + 4, 'I'), message, f'message {message:#x} descriptor ID')
        check(arena.read(descriptor + 8, 'I'), 8, f'message {message:#x} message group')
        check(arena.read(descriptor + 0xC, 'I'), 2, f'message {message:#x} eligibility class')
        check(arena.read(descriptor + 0x20, 'I'), 255, f'message {message:#x} pending duplicate limit')
        check(arena.call(0x71B940, (message,)).eax, response, f'message {message:#x} response base')
        check(arena.call(0x71B9C0, (message,)).eax, 1, f'message {message:#x} response count')
        if text is not None:
            arena.executed = 0
            tag_row = arena.call(0x637000, (tag,)).eax
            check(tag_row, 0x9D3480 + tag * 40, f'tag {tag:#x} record address')
            check(arena.read(tag_row + 8, 'I'), text, f'tag {tag:#x} caption entry')


def check_red_month_responses(arena):
    # The rows at 0xBF4D10.. are built by the same constructor as the forecast responses; each call
    # below is one row's argument sequence, ending before the next row's first push. The null tag
    # 0x97 is not looked up, so response 0x2EC keeps the constructor's fallback words.
    for start, stop, row, expected in (
        (0x7104A0, 0x7104C9, 0xBF4D10, (0x2EC, 868, 868, -1, 0, 666, 0, 0x97)),
        (0x7104C9, 0x7104EF, 0xBF4D30, (0x2ED, 869, 869, 25, 1, -1, 0, 0x5E)),
        (0x7104EF, 0x710515, 0xBF4D50, (0x2EE, 870, 870, 25, 1, -1, 0, 0x5F)),
    ):
        arena.executed = 0
        arena.call(start, stop=stop)
        check(struct.unpack('<8i', arena.uc.mem_read(row, 32)), expected, f'red-month response {expected[0]:#x}')


def main():
    image = Image.load(executable_path('tpi'))
    caption_requests = []
    presentation = {'now': 0, 'calls': []}

    def capture_string_lookup(arena):
        caption_requests.append(arena.arg(3))
        return 0, 0  # The caller cleans up; the native formatter ignores the return value.

    def capture_speech(arena):
        presentation['calls'].append(('speech', arena.arg(0)))
        return 2_750, 0

    def capture_tag(arena):
        presentation['calls'].append(('tag', arena.read(arena.arg(0) + 8, 'I')))
        return 0, 4

    arena = Sandbox(image, [
        (0x729530, 0x7392B1),  # Descriptor initializer.
        (0x70F59E, 0x70F6B6), (0x403CD3, 0x403CD8), (0x715570, 0x7155D9),
        (0x407248, 0x40724D), (0x636F70, 0x636FE0), (0x637000, 0x637072),
        (0x71B7E0, 0x71B84F), (0x71B940, 0x71BA17), (0x71BA40, 0x71BA97),
        (0x408F7B, 0x408F80), (0x71BDC0, 0x71BFA4),
        (0x40371A, 0x40371F), (0x64ED60, 0x64ED74),
        (0x408413, 0x408418), (0x5D0770, 0x5D0810),
        (0x404B51, 0x404B56), (0x40BD57, 0x40BD5C), (0x5BEEA0, 0x5BEEE4),
        (0x4047EB, 0x4047F0), (0x402AB8, 0x402ABD), (0x4022FC, 0x402301),
        (0x407F59, 0x407F5E), (0x409921, 0x409926), (0x4048F4, 0x4048F9), (0x40746E, 0x407473),
        (0x724F50, 0x725036), (0x6365A0, 0x6365EC),
        (0x718820, 0x71893E), (0x718B40, 0x718DB2), (0x718E90, 0x718F4E),
        (0x71B770, 0x71B7B6), (0x71BAC0, 0x71BD9B), (0x71C030, 0x71C08D),
        (0x42A3B0, 0x42A41D), (0x448E50, 0x448E56), (0x448EA0, 0x448EB6),
        *((address, address + 5) for address in (
            0x40AE2F, 0x40489A, 0x40B6A9, 0x40367A, 0x40B528, 0x40BB68,
            0x404764, 0x40AC27, 0x40C789, 0x4048EA, 0x403BD4, 0x406483,
            0x40B618, 0x402F13,
        )),
        (0x7104A0, 0x710516),
        (0x718190, 0x718628), (0x718F80, 0x719058), (0x71A9F0, 0x71AC6F),
        (0x71B870, 0x71B90E), (0x448DA0, 0x448DAE), (0x716D10, 0x716D50),
        (0x435D30, 0x435D60), (0x501440, 0x50144E), (0x5D0820, 0x5D082C),
        *((address, address + 5) for address in (
            0x406A14, 0x40AC2C, 0x407CBB, 0x4049BC, 0x40B285, 0x401D52,
            0x4036E3, 0x4056C3, 0x402BB2, 0x40BCDF,
        )),
    ], stubs={
        0x8934A0: capture_string_lookup,
        0x40C211: lambda _: (presentation['now'], 0),  # Presentation clock.
        0x40C25C: lambda _: (0x285, 4),  # No automatic scan candidate in this forecast-only scenario.
        0x408477: capture_speech,
        0x405E98: lambda _: (0, 0),  # Secondary advice-group history is outside this check.
        0x40B76C: capture_tag,
    }, budget=50_000)
    arena.call(0x729530)
    arena.call(0x70F59E, stop=0x70F6B6)
    check(arena.uc.reg_read(x86.UC_X86_REG_EIP), 0x70F6B6, 'end of response initializer slice')
    check(arena.read(0x9E7198, 'I'), 665, 'descriptor row count including terminator')
    check(arena.read(0x9DE020 + 664 * 56, 'I'), 2, 'descriptor terminator')

    # The eligibility routine assumes these singletons are already initialized.
    # It reads a handle at world+0x1EAB72, then resolves that handle via a table.
    world_handle = arena.object(2, {0: ('H', 1)})
    campaign_thing = arena.object(0x40)
    arena.write(0xAA1484, 'I', world_handle - 0x1EAB72)
    arena.write(0xAA18B0 + 20, 'I', campaign_thing)
    arena.write(0xC64DB5, 'B', 1)

    # Response IDs are a separate namespace: the descriptor lookup refuses this one up front.
    check(arena.call(0x71B7E0, (0x285,), this=0xC65220).eax, 0xFFFFFFFF, 'response ID 0x285 is not a message')

    for message, descriptor, response, voice, alternate, tag, text in FORECASTS:
        arena.executed = 0  # Keep initialized image data, but start a new instruction budget.
        label = f'message {message:#x}'
        check(arena.read(descriptor, 'I'), 1, f'{label} explicit row')
        check(arena.read(descriptor + 4, 'I'), message, f'{label} descriptor ID')
        check(arena.read(descriptor + 0xC, 'I'), 2, f'{label} eligibility class')
        check(arena.call(0x71B940, (message,)).eax, response, f'{label} response base')
        check(arena.call(0x71B9C0, (message,)).eax, 1, f'{label} response count')
        check(arena.call(0x71BA40, (message,)).eax & 0xFF, 255, f'{label} pending duplicate limit')

        arena.executed = 0
        for mode, campaign, score in ((0, 0, 10_000), (3, 0, 0xFFFFFFFF), (0, 1, 0xFFFFFFFF)):
            arena.write(0xC65B80, 'I', mode)
            arena.write(0xA9D230, 'H', campaign)
            check(arena.call(0x71B7E0, (message,), this=0xC65220).eax, score,
                  f'{label} score (mode {mode}, campaign flag {campaign})')

        # The two bytes on the game object at 0xA6E158 pick the classes 5 and 7, which the four
        # forecast rows are not, so either one rejects them while the mode is the default.
        arena.executed = 0
        for flag_2E, flag_2F, score in ((1, 0, 0xFFFFFFFF), (0, 1, 0xFFFFFFFF), (1, 1, 0xFFFFFFFF), (0, 0, 10_000)):
            arena.write(0xC65B80, 'I', 0)
            arena.write(0xA9D230, 'H', 0)
            arena.write(0xA6E158 + 0x2E, 'B', flag_2E)
            arena.write(0xA6E158 + 0x2F, 'B', flag_2F)
            check(arena.call(0x71B7E0, (message,), this=0xC65220).eax, score,
                  f'{label} score (game flags {flag_2E}/{flag_2F})')
        arena.write(0xA6E158 + 0x2E, 'B', 0)
        arena.write(0xA6E158 + 0x2F, 'B', 0)

        response_row = 0xBF3FD0 + (response - 642) * 32
        words = struct.unpack('<8i', arena.uc.mem_read(response_row, 32))
        check(words, (response, voice, alternate, 25, 1, -1, 0, tag), f'{label} response metadata')
        tag_row = arena.call(0x637000, (tag,)).eax
        check(tag_row, 0x9D3480 + tag * 40, f'{label} tag address')
        check(arena.read(tag_row + 8, 'I'), text, f'{label} caption entry')
        check(arena.read(tag_row + 0x18, 'I'), 0, f'{label} caption formatter case')
        caption_requests.clear()
        check(arena.call(0x6365A0, (tag_row, 0, 0, 0), this=arena.object(0x414)).eax & 0xFF,
              1, f'{label} plain formatter result')
        check(caption_requests, [text], f'{label} string lookup handoff')

    check_red_month_responses(arena)
    check_notification_messages(arena)
    check_eligibility_matrix(arena, campaign_thing)
    check_rain_queue(arena)
    check_rain_playback(arena, presentation)
    print('4 descriptors, 28 scores, 72 eligibility classes, 4 responses, 4 caption lookups, '
          '3 notification messages, 7 queue cases and 5 playback ticks match')


if __name__ == '__main__':
    main()
