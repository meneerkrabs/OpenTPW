"""The load routines of the Theme Park Inc thing models (Game.exe addresses after following the 0x4xxxxx jump thunks) and how to prepare an object.

Setup steps: ('c', constructor, this) calls a constructor with one dummy argument; ('vec', ptr, size, count, ctor) runs the vector constructor 0x402423.
`this` is a zero-filled object at THIS; all the load routines were run on a zero-filled object plus the constructors listed in the dispatch cases.
"""
THIS = 0x30000000
DUMMY = 0x10200100
MODELS = {
    'guest': (0x4045a2, [('c', 0x4023b5, THIS), ('vec', THIS + 0x212, 2, 4, 0x405498)]),
    'mechanic': (0x40ab14, [('c', 0x409967, THIS)]),
    'handyman': (0x40c130, [('c', 0x409967, THIS)]),
    'entertainer': (0x40401b, [('c', 0x409967, THIS)]),
    'guard': (0x40622b, [('c', 0x409967, THIS)]),
    'researcher': (0x4032c4, [('c', 0x409967, THIS)]),
    'gardener': (0x4079a5, [('c', 0x409967, THIS)]),
    'staffhq': (0x408edb, [('vec', THIS + 0x14, 0xc, 0x14, 0x402a86), ('c', 0x40b3e8, THIS + 0x140), ('vec', THIS + 0x15c, 0xc, 6, 0x405bfa)]),
    'advisor': (0x40a7a9, []),
    'analyser': (0x40978c, []),
    'lab': (0x4016cc, []),
    'weather': (0x401014, []),
    'challenge': (0x408d9b, []),
    'tag': (0x6355d0, []),          # also needs the list allocator 0x86fed9 hooked (see run.py)
    'mechhq': (0x404d81, []),
    'ui': (0x404e9e, []),
    'objh': (0x402379, []),
    'cuts': (0x4052d6, []),
    'trnr': (0x4038f0, []),
    'bank': (0x40213a, []),
}
# model name in ThemeParkIncBodyLayout.Data.cs -> constant
CONSTANTS = {
    'guest': 'GuestBody', 'mechanic': 'MechanicBody', 'handyman': 'HandymanBody', 'entertainer': 'EntertainerBody', 'guard': 'GuardBody',
    'researcher': 'ResearcherBody', 'gardener': 'GardenerBody', 'staffhq': 'StaffHqBody', 'advisor': 'AdvisorBody', 'analyser': 'AnalyserBody',
    'lab': 'ResearchLabBody', 'weather': 'WeatherBody', 'challenge': 'ChallengeBody', 'tag': 'TagSystemBody',
}
