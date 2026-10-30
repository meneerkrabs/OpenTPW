"""Theme Park Inc's weather spell roll 0x642770 run in the x86 oracle sandbox and compared with the model that ThemeParkIncWeatherSpells ports (ARRIVAL-008, [EXT:tpi-weather-spell-seed]).

Dev-only tooling like the rest of oracle_x86: Unicorn and the identified Theme Park Inc no-CD Game.exe (OPENTPW_TPI_EXE, SHA-256 pinned) are needed, nothing here is shipped. The routine runs with the calendar
read 0x401A14 (month), the years read 0x4026E4 and the CRT rand() 0x870D38 stubbed (the draws are given), the table words at 0xA024A0.. and the quality ranges at 0xA02500.. written as synthetic settings.

Usage: python3 -I tools/ppc-analysis/oracle_x86/tpi_weather_roll_oracle.py [cases]   (prints the number of mismatches)
"""
from __future__ import annotations

import random
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import x86_harness as H  # noqa: E402
from x86_harness import executable_path  # noqa: E402


def clock_stub(sb):
    sb.write(sb.arg(2), 'i', state['month'])
    return 0, 0x24


def years_stub(sb):
    return state['years'], 0


state: dict = {}
sb = None


def run(month,years,rolls,rows,tail,yrs,q):
    sb.reset()
    state.update(month=month,years=years)
    seq=iter(rolls)
    state['r']=None
    g=sb.object(0x2000,{})
    sb.write(0xADE6B0,'I',g); sb.write(0xAA1484,'I',g)
    for i,v in enumerate(rows): sb.write(0xA024A0+4*i,'i',v)
    for i,v in enumerate(tail): sb.write(0xA024E0+4*i,'i',v)
    sb.write(0xA024EC,'i',yrs)
    addr={'NH':0x04,'NL':0x08,'HH':0x0C,'HL':0x10,'SL':0x14,'SH':0x18,'RL':0x1C,'RH':0x20,'LL':0x24,'LH':0x28}
    for k,v in q.items(): sb.write(0xA02500+addr[k],'i',v)
    calls=[]
    def rand(sb_):
        v=next(seq); calls.append(v); return v,0
    sb.stubs[0x870D38]=rand
    rec=sb.object(0x20,{})
    sb.call(0x642770,args=(rec,),this=0)
    return [sb.read(rec+4*i,'i') for i in range(4)]+[sb.read(rec+0x10,'B')], len(calls)

def model(month,years,rolls,rows,tail,yrs,q):
    it=iter(rolls)
    draw=lambda b: next(it)%b
    qq=month%12+1; row=qq//3; pos=qq%3 or 3; start=month+1
    allw=rows+tail+[yrs]
    if pos==3 and years>=yrs:
        ch=[allw[row*4+i] for i in range(4)]
        r=draw(100); kind=0
        for i in range(4):
            if kind==0:
                if r<ch[i]: kind=i+1
                else: r-=ch[i]
        if kind:
            lo,hi={1:(q['HL'],q['HH']),2:(q['RL'],q['RH']),3:(q['LL'],q['LH']),4:(q['SL'],q['SH'])}[kind]
            return [start,kind,(draw(hi-lo)+lo) if hi-lo>0 else lo,3,1]
    return [start,0,draw(q['NH']-q['NL'])+q['NL'],pos,1]
def main(cases: int) -> int:
  global sb
  path = executable_path('tpi')
  if path is None:
    raise SystemExit('needs OPENTPW_TPI_EXE (tools/ppc-analysis/oracle_x86/README.md)')
  sb = H.Sandbox(H.Image.load(path), [(0x642770, 0x642A00)], {0x401A14: clock_stub, 0x4026E4: years_stub, 0x870D38: lambda s: (0, 0)})
  rng=random.Random(11); bad=0
  for n in range(cases):
    month=rng.randrange(1,13); years=rng.randrange(0,8)
    rows=[rng.choice([0,0,5,10,15,30,100]) for _ in range(16)]
    tail=[4,rng.choice([0,4]),rng.choice([0,7])]
    yrs=rng.randrange(0,6)
    q={k:rng.randrange(0,100) for k in ('NH','NL','HH','HL','SL','SH','RL','RH','LL','LH')}
    for k in 'NH','HH','SH','RH','LH':
        lo=k[0]+'L'; q[k]=q[lo]+rng.randrange(1,60)
    rolls=[rng.randrange(0,32768) for _ in range(2)]
    exp=model(month,years,rolls,rows,tail,yrs,q)
    got,_=run(month,years,rolls,rows,tail,yrs,q)
    if got!=exp:
        bad+=1
        if bad<6: print('MISMATCH',month,years,rows[:8],yrs,got,exp)
  print('mismatches', bad, 'in', cases, 'cases')
  return 1 if bad else 0


if __name__ == '__main__':
    raise SystemExit(main(int(sys.argv[1]) if len(sys.argv) > 1 else 3000))

