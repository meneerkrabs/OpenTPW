"""Pinned catalog input/callback boundaries; no production UI or native execution.

Names describe consumers rather than assigning unproved physical mouse/key
semantics. Small reference decoders reject unsupported metadata domains.
"""
from pathlib import Path

from corpus import layout_table
from witness import APP_SHA, identified, require, d_form, branch, relocation, class_slot, mask_form, import_at, x_form


def decode_sort_word(word, columns=3):
    """Decode the established signed one-based selector, not final sort order."""
    if type(word) is not int or type(columns) is not int or not 1 <= columns <= 32 or word == 0 or not -columns <= word <= columns:
        raise ValueError('unsupported signed sort selector')
    return {'column': abs(word) - 1, 'direction_bit': int(word < 0)}


def header_column(identifier, columns=3):
    if type(identifier) is not int or type(columns) is not int or not 1 <= columns <= 32 or not 16 <= identifier < 16 + columns:
        raise ValueError('unsupported header child identifier')
    return identifier - 16


def scroll_range(first, visible, total):
    """Exact two range payloads; do not invent inclusive/exclusive semantics."""
    if not all(type(value) is int for value in (first, visible, total)) or not 0 <= first < total or not 0 < visible <= 512 or total > 65535:
        raise ValueError('unsupported scroll range domain')
    return first, min(first + visible, total - 1)


def linked(app, at, expected):
    require(branch(app, at), expected, f'catalog call at {at:#x}')


def conditional(app, at, branch_option, condition_bit, expected):
    word = int.from_bytes(app.code.data[at:at + 4], 'big')
    require((word >> 26, word >> 21 & 31, word >> 16 & 31, word & 3),
            (16, branch_option, condition_bit, 0), f'catalog conditional at {at:#x}')
    displacement = word & 65532
    if displacement & 32768:
        displacement -= 65536
    require(at + displacement, expected, 'catalog conditional target')


def prove(app):
    identity = class_slot(app, 0x4fef4, 'InterfaceListControl2', 3, 0x1773f0)
    d_form(app, 0x1773f0, 14, (3, 0, 7))
    scrollbar = class_slot(app, 0x48054, 'InterfaceScrollBar', 8, 0x17b9d4)
    class_slot(app, 0x48054, 'InterfaceScrollBar', 3, 0x17b558)
    d_form(app, 0x17b558, 14, (3, 0, 3))
    d_form(app, 0x17b9d4, 32, (3, 3, 320))
    require(relocation(app, 0x1ee8, app.data_section.index), 0x4fef4, 'list constructor RTTI table')
    require(relocation(app, 0x1ee4, app.data_section.index), 0x8600, 'default list callback vector')
    require(relocation(app, 0x8600, app.code.index), 0x17a5fc, 'default list callback code')
    for at, fields in [(0x177250, (0, 3, 272)), (0x177254, (0, 3, 276)),
                       (0x181ae4, (31, 3, 272))]:
        d_form(app, at, 36, fields)
    require(relocation(app, 0x1d7c, app.data_section.index), 0x8090, 'root window callback argument')
    require(relocation(app, 0x8090, app.code.index), 0x163748, 'catalog root callback')
    d_form(app, 0x16450c, 32, (5, 2, -25220))
    linked(app, 0x164528, 0x181aac)
    d_form(app, 0x18137c, 32, (12, 3, 276))
    d_form(app, 0x1813a4, 32, (12, 3, 272))
    conditional(app, 0x1813ac, 12, 2, 0x1813bc)
    linked(app, 0x163fa8, 0x181370)
    # Header command 256 goes through the default handler, unlike custom
    # category command 257/argument1. Header helper gates on attributes bit16.
    d_form(app, 0x17a658, 11, (0, 29, 256))
    linked(app, 0x17a6ec, 0x17a444)
    d_form(app, 0x17a454, 14, (30, 4, -16))
    mask_form(app, 0x17a464, (0, 0, 0, 27, 27, 1))
    conditional(app, 0x17a468, 12, 2, 0x17a5bc)
    d_form(app, 0x17a474, 42, (0, 31, 356))
    d_form(app, 0x17a484, 42, (0, 31, 360))
    d_form(app, 0x17a560, 44, (30, 31, 360))
    # Changed direction/column emits +/- (column+1) synchronously to parent.
    for at, fields in [(0x17a4c4, (6, 3, 1)), (0x17a534, (6, 3, 1)),
                       (0x17a570, (6, 3, 1))]:
        d_form(app, at, 14, fields)
    for at in (0x17a4d4, 0x17a544, 0x17a580):
        d_form(app, at, 14, (4, 0, 1030))
    for at in (0x17a4dc, 0x17a54c, 0x17a588):
        linked(app, at, 0x170f98)
    linked(app, 0x17a4e8, 0x1773f8)
    linked(app, 0x17a594, 0x1773f8)
    d_form(app, 0x1637f8, 11, (0, 29, 1030))
    require(relocation(app, 0x3ff4, app.data_section.index), 0x4d1dc, 'persisted catalog sort word')
    d_form(app, 0x163db4, 32, (3, 2, -16396))
    d_form(app, 0x163db8, 36, (27, 3, 0))
    require(int.from_bytes(app.data_section.data[0x4d1dc:0x4d1e0], 'big'), 1, 'initial catalog sort selector')
    linked(app, 0x164800, 0x17a2a8)
    require(import_at(app, branch(app, 0x17a2d4))['symbol'], 'labs', 'signed sort selector normalization')
    d_form(app, 0x17a2dc, 14, (4, 3, -1))
    d_form(app, 0x17a2f0, 44, (4, 30, 360))
    # Catalog row columns have text/numeric selector flags, independent of the
    # separately stored row identity. Generic comparator uses UTF-16 wmemcmp.
    for at, index, flag in [(0x1647ac, 0, 0), (0x1647bc, 1, 1), (0x1647cc, 2, 1)]:
        d_form(app, at, 14, (4, 0, index))
        d_form(app, at + 4, 14, (5, 0, flag))
        linked(app, at + 8, 0x178070)
    d_form(app, 0x17808c, 7, (0, 4, 12))
    d_form(app, 0x178098, 44, (4, 3, 10))
    linked(app, 0x177564, 0x17abf0)
    require(import_at(app, branch(app, 0x17ad0c))['symbol'], 'wmemcmp', 'generic text sort comparator')
    # Scrollbar changes current value, emits 2048, and list queries that actual
    # child1 getter. A distinct 0x11008 branch subtracts the input delta.
    d_form(app, 0x17b990, 14, (4, 0, 2048))
    conditional(app, 0x17b988, 12, 2, 0x17b99c)
    linked(app, 0x17b998, 0x170f98)
    d_form(app, 0x17a60c, 11, (0, 29, 2048))
    d_form(app, 0x17aa38, 14, (4, 0, 1))
    d_form(app, 0x17aa4c, 32, (12, 12, 40))
    d_form(app, 0x17aa5c, 32, (0, 28, 340))
    d_form(app, 0x17aa68, 36, (3, 24, 0))
    d_form(app, 0x17aa58, 14, (24, 28, 340))
    conditional(app, 0x17aa64, 12, 2, 0x17abc8)
    d_form(app, 0x17a6d4, 14, (0, 3, 4104))
    d_form(app, 0x17aae0, 32, (12, 12, 40))
    d_form(app, 0x17aaf8, 32, (12, 12, 36))
    x_form(app, 0x17aaf0, 40, (4, 30, 3))
    d_form(app, 0x17aa74, 32, (5, 24, 0))
    d_form(app, 0x17aa78, 42, (0, 28, 370))
    d_form(app, 0x17aa7c, 32, (3, 28, 336))
    d_form(app, 0x17aa8c, 14, (6, 3, -1))
    for at in (0x17aa94, 0x17a288):
        d_form(app, at, 14, (4, 0, 1029))
    linked(app, 0x17aa98, 0x170f98)
    linked(app, 0x17a28c, 0x170f98)
    # Selection and activation are different messages/delivery policies.
    d_form(app, 0x179ee8, 14, (4, 0, 1025))
    linked(app, 0x179ef8, 0x170ec8)
    d_form(app, 0x17aa24, 14, (4, 0, 1024))
    d_form(app, 0x17aa0c, 11, (0, 30, 0))
    conditional(app, 0x17aa10, 4, 2, 0x17abc8)
    conditional(app, 0x17aa1c, 12, 0, 0x17abc8)
    linked(app, 0x17aa2c, 0x170ec8)
    linked(app, 0x170f20, 0x16fd80)
    linked(app, 0x170f78, 0x16fd80)
    for at, fields in [(0x16fda4, (4, 8, 0)), (0x16fdb8, (5, 4, 4)),
                       (0x16fdcc, (6, 4, 8)), (0x16fde0, (7, 4, 12))]:
        d_form(app, at, 36, fields)
    linked(app, 0x17100c, 0x181398)
    linked(app, 0x16ff98, 0x181398)
    d_form(app, 0x1637d4, 11, (0, 29, 1025))
    d_form(app, 0x1637ec, 11, (0, 29, 1024))
    linked(app, 0x163994, 0x17991c)
    d_form(app, 0x163970, 11, (0, 27, -1))
    conditional(app, 0x163974, 12, 2, 0x163e54)
    linked(app, 0x163d70, 0x17991c)
    d_form(app, 0x1799ac, 32, (0, 3, 4))
    d_form(app, 0x1799b0, 36, (0, 30, 0))
    mask_form(app, 0x1639a4, (0, 0, 0, 16, 31, 0))
    d_form(app, 0x1639ac, 44, (0, 1, 140))
    # Category rebuild creates row payloads, stores item ID separately and
    # calls original list refresh after append; no root-name hash is substituted.
    linked(app, 0x163eec, 0x162308)
    d_form(app, 0x162344, 14, (4, 0, 504))
    linked(app, 0x1624e4, 0x178768)
    linked(app, 0x16256c, 0x177d44)
    layout = layout_table(app.data_section.data, 0x4cd94)
    selected = {node['id']: node for node in layout['windows'] if node['id'] == 504 or node['parent'] == 504}
    require((selected[504]['type'], selected[504]['attributes']), (7, 0x91), 'actual catalog type/attributes')
    require(selected[1]['type'], 3, 'actual scrollbar child type')
    require([selected[i]['parent'] for i in (16, 17, 18)], [504] * 3, 'actual sort header parents')
    return {'identity': identity, 'scrollbar_identity': scrollbar,
            'root_callback': 0x163748, 'list_override_callback': 0x163e7c, 'list_default_callback': 0x17a5fc,
            'callback_fields': {'override': 272, 'default': 276},
            'headers': [16, 17, 18], 'header_command': 256, 'category_command': 257,
            'sort_event': 1030, 'sort_fields': {'column': 360, 'direction_bit': 362},
            'sort_initial_word': 1, 'column_types': [0, 1, 1], 'text_compare': 'UTF-16 wmemcmp then length',
            'catalog_drawing_key': selected[504]['command_1'],
            'scrollbar_child': 1, 'scrollbar_event': 2048, 'scroll_delta_input': 0x11008,
            'first_visible_field': 340, 'visible_count_field': 370, 'row_count_field': 336,
            'range_event': 1029, 'range_payload': 'first, min(first+visible,total-1)',
            'selection_event': 1025, 'activation_event': 1024,
            'delivery': {'sort_and_range': 'synchronous 0x170f98', 'selection_and_activation': 'queued 0x170ec8'},
            'row_identifier': 'record+4, then low16 for catalog item consumers; never root-name hash',
            'remaining': ['physical input producer/names', 'full linked sort final order/tie stability',
                          'runtime keyboard-enable writer', 'purchase business checks/tool commit', 'row clipping/render pixels']}


def inspect(bin_root: Path):
    return {'app_sha256': APP_SHA, **prove(identified(bin_root / 'SimThemePark.data', APP_SHA))}
