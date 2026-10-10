"""Selected catalog registration/row factory/font-bank witnesses, no table dumps."""
import hashlib
from pathlib import Path

from phase2 import MAC_LANGUAGE_SHA
from witness import APP_SHA, identified, d_form, branch, relocation, require
from corpus import string_labels


def inspect(bin_root: Path, strings: Path, characters: Path) -> dict:
    app = identified(bin_root / 'SimThemePark.data', APP_SHA)
    # The buy builder obtains actual layout control 504, then attaches callbacks.
    d_form(app, 0x16477c, 14, (4, 0, 504))
    require(branch(app, 0x164780), 0x17f770, 'control lookup')
    for at, target in [(0x164794, 0x180264), (0x1647a4, 0x1781c0),
                        (0x1647b4, 0x178070), (0x1647c4, 0x178070), (0x1647d4, 0x178070)]:
        require(branch(app, at), target, 'catalog registration')
    callbacks = []
    for toc, vector, code, purpose in [(0x1d78, 0x8088, 0x163e7c, 'category commands'),
                                      (0x3fe8, 0x8078, 0x164140, 'row factory'),
                                      (0x3fe4, 0x8070, 0x164268, 'row initializer')]:
        require(relocation(app, toc, app.data_section.index), vector, 'catalog callback TOC')
        require(relocation(app, vector, app.code.index), code, 'catalog callback entry')
        callbacks.append({'vector_data': vector, 'code': code, 'purpose': purpose})
    d_form(app, 0x18026c, 36, (4, 3, 272))
    # Category handler recognizes original command code 257 / argument 1.
    d_form(app, 0x163e8c, 11, (0, 28, 257))
    d_form(app, 0x163eac, 11, (0, 30, 1))
    categories = []
    for set_at, value, label_at, label, control in [
            (0x163edc, 0, 0x163ef0, 119, 507),
            (0x163f0c, 1, 0x163f20, 120, 509),
            (0x163f3c, 2, 0x163f50, 121, 506),
            (0x163f6c, 3, 0x163f80, 122, 508)]:
        d_form(app, set_at, 14, (3, 0, value))
        d_form(app, label_at, 14, (3, 0, label))
        require(branch(app, set_at + 4), 0x163414, 'catalog category setter')
        categories.append({'control_id': control, 'category': value, 'uitext_id': label})
    # Row height is measured font height *1536/drawable height +6; cached until
    # the caller clears its global. It is not an arbitrary icon count/row height.
    d_form(app, 0x164178, 32, (12, 3, 16))
    d_form(app, 0x16417c, 32, (12, 12, 12))
    d_form(app, 0x164190, 7, (4, 0, 1536))
    d_form(app, 0x164194, 42, (0, 3, 0))
    d_form(app, 0x16419c, 14, (0, 3, 6))
    # Generic list factory uses actual template height and content region to
    # allocate rows, then initializes each via the registered callback.
    for at, fields in [(0x17820c, (0, 28, 368)), (0x178228, (0, 28, 370))]:
        d_form(app, at, 44, fields)
    for at, fields in [(0x178210, (4, 28, 318)), (0x178214, (3, 28, 322)),
                       (0x178218, (0, 28, 368))]:
        d_form(app, at, 42, fields)
    # Setting the display selector writes the same field consumed by the bank
    # choice; original option labels provide the associated base dimensions.
    require(branch(app, 0x157e20), 0x1261a8, 'display settings setter')
    d_form(app, 0x1261b4, 36, (4, 3, 4))
    d_form(app, 0x11f0f8, 32, (0, 3, 4))
    for at, label in [(0x157e50, 341), (0x157e58, 342), (0x157e60, 343), (0x157e68, 344)]:
        d_form(app, at, 14, (28, 0, label))
    require(hashlib.sha256(strings.read_bytes()).hexdigest(), MAC_LANGUAGE_SHA['uitext'], 'Mac UITEXT identity')
    require(hashlib.sha256(characters.read_bytes()).hexdigest(), MAC_LANGUAGE_SHA['mbtouni'], 'Mac BFMU identity')
    labels = string_labels(strings.read_bytes(), characters.read_bytes(), [119, 120, 121, 122, 341, 342, 343, 344])
    for category in categories:
        category['label'] = labels[category['uitext_id']]
    return {'app_sha256': APP_SHA, 'catalog_control': 504, 'callbacks': callbacks,
            'categories': categories, 'row_height': 'font pixel height *1536/drawable pixel height +6',
            'row_count': 'content-region height / template-row height, integer truncation',
            'display_bank_labels': {index: labels[341 + index] for index in range(4)},
            'remaining': ['actual display-mode creation consumer', 'row data fill/sort',
                          'scroll dispatch and row selected item command paths', 'render clipping and fonts']}
