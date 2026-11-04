"""tp.exe's UI part batcher 0x581DB0 run in the x86 oracle sandbox (docs/reverse/ORACLE.md, finding X70; UI-004).

Dev-only tooling like the rest of oracle_x86. The batcher runs with its real state builder 0x581790 and texture-table lookup 0x575F90; the primitive builder 0x580530, the texture preparation 0x581880 and the record finish 0x5658C0 are stubbed to log the order the batcher calls them in. A part is a list of
materials (texture key, alpha type); the key stands for the texture's page surface [slot+0x1C] (valid with slot flag 0x1000). Usage: python3 -I tools/ppc-analysis/oracle_x86/ui_batch_oracle.py (prints the emit order of a few synthetic frames).
"""
import struct, sys
from pathlib import Path
sys.path.insert(0,str(__import__('pathlib').Path(__file__).resolve().parent))
import x86_harness as h
h.STACK_BASE=0x5C000000; h.STACK_SIZE=0x03000000
from x86_harness import Image, Sandbox
from x86_harness import executable_path
img=Image.load(executable_path('tpw'))
log=[]
def s881(sb):   # 0x581880(this=part ptr via ecx; mat idx, &out)
    part=sb.reg('ecx'); mat=sb.arg(0); out=sb.arg(1)
    sb.write(out,'I',1)
    log.append(('prep',part,mat)); return 1,8
def s530(sb):
    part=sb.reg('ecx'); log.append(('emit',part,sb.arg(1))); return 1,0xc
def s8c0(sb):
    log.append(('fin',)); return 1,4
sb=Sandbox(img,[(0x581DB0,0x58216E),(0x581790,0x581880),(0x575F90,0x575F9D),(0x67B110,0x67B140),(0x590C40,0x590CA8)],
           stubs={0x581880:s881,0x580530:s530,0x5658C0:s8c0},budget=2_000_000)
def build(parts):
    """parts: list of dict(texture=int id, alpha=0..3, nmat) each one material (or list of materials)"""
    sb.reset(); log.clear()
    sb.write(0x879AB0,'I',0)  # device ptr
    dev=sb.alloc(0x100); sb.write(0x879AB0,'I',dev); sb.write(dev+0xC,'I',0x3000)
    sb.write(0x8BCBC8,'I',0x410100); sb.write(0x8BD550,'I',0)
    L=sb.alloc(8+20*len(parts)+16); sb.write(L,'I',len(parts))
    tex={}
    def slot(tid,alpha,page=None):
        if tid in tex: return tex[tid]
        S=sb.alloc(0x80); sb.write(S+8,'I',0x1000|(alpha<<8)); sb.write(S+0x1c,'I',0x1000+(tid if page is None else page)*0x10)
        arr=sb.alloc(8); sb.write(arr,'I',S)
        T=sb.alloc(0x20); sb.write(T+8,'I',arr)
        R=sb.alloc(0x20); sb.write(R+4,'I',T); sb.write(R+2,'H',0)
        tex[tid]=R; return R
    for i,p in enumerate(parts):
        M=sb.alloc(0x100); sb.write(M,'I',0)
        mats=p['mats']
        sb.write(M+0x5a,'H',len(mats))
        arr=sb.alloc(16*len(mats)+16); sb.write(M+0x6c,'I',arr)
        for k,mat in enumerate(mats):
            sb.write(arr+16*k,'I',slot(*mat))
        e=L+4+20*i
        sb.write(e,'I',i+1); sb.write(e+4,'I',M); sb.write(e+8,'I',p.get('flags',0x3000040))
    sb.call(0x581DB0,args=(),this=L)
    out=[]
    for e in log:
        if e[0]=='fin': out.append('F')
        else: out.append(f"{e[0][0]}(p{(e[1]-(L+4))//20},m{e[2]})")
    return out
if __name__=='__main__':
    # panel (tex 1), button (tex 2), label (tex 1)
    for name,parts in (('panel,button',[dict(mats=[(1,0)]),dict(mats=[(2,0)])]),
                       ('panel,button,panel2',[dict(mats=[(1,0)]),dict(mats=[(2,0)]),dict(mats=[(1,0)])]),
                       ('panel page 1, icons share page 2',[dict(mats=[(1,0,1)]),dict(mats=[(2,0,2)]),dict(mats=[(3,0,2)]),dict(mats=[(4,0,2)])]),
                       ('3 textures',[dict(mats=[(1,0)]),dict(mats=[(2,0)]),dict(mats=[(3,0)])])):
        print(name,' '.join(build(parts)))
