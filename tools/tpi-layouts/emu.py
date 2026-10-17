"""Minimal x86-32 harness: maps the PE sections of Game.exe into unicorn, fakes the stream object and runs a class load routine."""
import sys,struct
from unicorn import *
from unicorn.x86_const import *
import os
EXE=os.environ.get('TPI_GAME_EXE') or sys.exit('Set TPI_GAME_EXE to the no-CD Theme Park Inc Game.exe (never commit it).')
data=open(EXE,'rb').read()
pe=struct.unpack_from('<I',data,0x3c)[0]
nsec=struct.unpack_from('<H',data,pe+6)[0]; opt=struct.unpack_from('<H',data,pe+20)[0]
secs=[]
for i in range(nsec):
    o=pe+24+opt+40*i
    vs,va,rs,ro=struct.unpack_from('<IIII',data,o+8)
    secs.append((0x400000+va,vs,ro,rs))
PG=0x1000
def al(x): return (x+PG-1)&~(PG-1)
STUB=0x7f000000
class Emu:
    def __init__(s, budget=50_000_000):
        s.uc=Uc(UC_ARCH_X86,UC_MODE_32)
        uc=s.uc
        uc.mem_map(0x400000,0x1000)  # headers
        uc.mem_write(0x400000,data[:0x1000])
        for base,vs,ro,rs in secs:
            sz=al(max(vs,rs))
            uc.mem_map(base,sz)
            uc.mem_write(base,data[ro:ro+rs])
        uc.mem_map(0x10000000,0x400000)   # stack
        uc.mem_map(0x20000000,0x10000)    # stream
        uc.mem_map(0x30000000,0x2000000)  # this
        uc.mem_map(0x38000000,0x4000000)  # heap
        uc.mem_map(STUB,0x10000)
        uc.mem_write(STUB,b'\xc3'*0x10000)
        s.heap=0x38000000
        uc.mem_map(0x7ffd0000,0x10000)
        GDT=0x7ffe0000; uc.mem_map(GDT,0x1000)
        def desc(base,limit,access,flags):
            return struct.pack('<HHBBBB',limit&0xffff,base&0xffff,(base>>16)&0xff,access,((limit>>16)&0xf)|(flags<<4),(base>>24)&0xff)
        uc.mem_write(GDT, desc(0,0,0,0)+desc(0,0xfffff,0x9b,0xc)+desc(0,0xfffff,0x93,0xc)+desc(0x7ffd0000,0xfff,0x93,0x4))
        uc.reg_write(UC_X86_REG_GDTR,(0,GDT,0x1f,0))
        uc.reg_write(UC_X86_REG_CS,0x8); uc.reg_write(UC_X86_REG_SS,0x10); uc.reg_write(UC_X86_REG_DS,0x10); uc.reg_write(UC_X86_REG_ES,0x10)
        uc.reg_write(UC_X86_REG_FS,0x18)
        s.hooks={}
        s.events=[]; s.cur=0; s.buf=b''; s.lastname=''
        s.budget=budget
        # IAT: fill import address table with stubs
        # find import dir
        impdir=struct.unpack_from('<II',data,pe+24+opt-128+8*1)  # index 1 import
        # simply scan idata section for the IAT: patch every dword in .idata range pointing outside
        s.stubn=0
        for base,vs,ro,rs in secs:
            pass
        s.patch_iat()
        uc.hook_add(UC_HOOK_CODE, s.code_hook)
        uc.hook_add(UC_HOOK_MEM_READ_UNMAPPED|UC_HOOK_MEM_WRITE_UNMAPPED|UC_HOOK_MEM_FETCH_UNMAPPED, s.unmapped)
        s.fail=None
        s.gen=None
        s.lazy=True; s.lazyn=0
        # stream object
        uc.mem_write(0x20000000, struct.pack('<I',0x20001000))
        uc.mem_write(0x20001000, struct.pack('<IIII',0,0,STUB+0x100,STUB+0x110))
        s.hooks[STUB+0x100]=s.h_read; s.hooks[STUB+0x110]=s.h_write
    def patch_iat(s):
        # import descriptors
        impd_rva,impd_sz=struct.unpack_from('<II',data,pe+24+opt-128+8)
        def rva2off(r):
            for base,vs,ro,rs in secs:
                b=base-0x400000
                if b<=r<b+max(vs,rs): return ro+r-b
        o=rva2off(impd_rva)
        while True:
            oft,_,_,name,ft=struct.unpack_from('<IIIII',data,o)
            if name==0: break
            a=ft
            while True:
                v=struct.unpack_from('<I',data,rva2off(a))[0]
                if v==0: break
                s.uc.mem_write(0x400000+a, struct.pack('<I',STUB+0x1000+s.stubn*2))
                s.stubn+=1; a+=4
            o+=20
    def code_hook(s,uc,addr,size,ud):
        h=s.hooks.get(addr)
        if h: h()
    def unmapped(s,uc,access,addr,size,val,ud):
        if access in (UC_MEM_READ_UNMAPPED,UC_MEM_WRITE_UNMAPPED) and s.lazy and addr<0x7f000000:
            base=addr&~0xfff
            try:
                uc.mem_map(base,0x2000); s.lazyn+=1
                return True
            except UcError: pass
        s.fail=('unmapped',access,hex(addr),hex(uc.reg_read(UC_X86_REG_EIP)))
        return False
    def r32(s,a): return struct.unpack('<I',s.uc.mem_read(a,4))[0]
    def reg(s,r): return s.uc.reg_read(r)
    def ret(s,n=0):
        esp=s.reg(UC_X86_REG_ESP); r=s.r32(esp)
        s.uc.reg_write(UC_X86_REG_ESP,esp+4+n); s.uc.reg_write(UC_X86_REG_EIP,r)
    def h_read(s):
        esp=s.reg(UC_X86_REG_ESP); dest=s.r32(esp+4); size=s.r32(esp+8)
        if s.gen:
            chunk=s.gen(size,len(s.events),s.lastname,s.r32(esp+0x10)); s.buf=s.buf[:s.cur]+chunk
        avail=max(0,min(size,len(s.buf)-s.cur))
        s.uc.mem_write(dest,s.buf[s.cur:s.cur+avail]) if avail else None
        s.events.append((s.cur,size,dest,s.r32(esp+0x10),s.lastname))
        s.cur+=avail
        s.uc.reg_write(UC_X86_REG_EAX,avail); s.ret(8)
    def h_name(s):
        esp=s.reg(UC_X86_REG_ESP); p=s.r32(esp+8)
        try:
            b=bytes(s.uc.mem_read(p,80)); s.lastname=b.split(b'\0')[0].decode('latin1')
        except Exception: s.lastname='?'
        s.ret(0xc)
    def h_write(s):
        s.fail=('write called',); s.uc.emu_stop()
    def alloc(s,n):
        a=s.heap; s.heap+=al(n+16); return a
    def hook_new(s):
        esp=s.reg(UC_X86_REG_ESP); n=s.r32(esp+4)
        s.uc.reg_write(UC_X86_REG_EAX,s.alloc(n)); s.ret(0)
    def call(s,fn,args=(),ecx=0,maxins=20_000_000):
        uc=s.uc
        esp=0x10300000
        uc.mem_write(esp,b'\0'*0x100)
        for a in reversed(args):
            esp-=4; uc.mem_write(esp,struct.pack('<I',a&0xffffffff))
        esp-=4; uc.mem_write(esp,struct.pack('<I',STUB+0x200))
        uc.reg_write(UC_X86_REG_ESP,esp); uc.reg_write(UC_X86_REG_ECX,ecx)
        uc.reg_write(UC_X86_REG_EIP,fn)
        s.hooks[STUB+0x200]=lambda: uc.emu_stop()
        s.fail=None
        try:
            uc.emu_start(fn,STUB+0x200,count=maxins)
        except UcError as e:
            s.fail=s.fail or ('uc',str(e),hex(uc.reg_read(UC_X86_REG_EIP)))
        return s.reg(UC_X86_REG_EAX)
