"""Original procedural demo sounds/music; no sampled game or character recordings."""
import math, pathlib, struct, wave, random
RATE = 22050
OUT = pathlib.Path('assets/hachiware/audio')
OUT.mkdir(parents=True, exist_ok=True)
rng = random.Random(331)
def save(name, samples):
    with wave.open(str(OUT/(name+'.wav')), 'wb') as f:
        f.setparams((1,2,RATE,0,'NONE','not compressed'))
        f.writeframes(b''.join(struct.pack('<h',int(max(-1,min(1,x))*24000)) for x in samples))
def exclamation(name, notes):
    result=[]
    for frequency,duration,vowel in notes:
        n=int(duration*RATE)
        for i in range(n):
            t=i/RATE; env=min(1,t/0.02)*min(1,(duration-t)/0.06)
            phase=2*math.pi*frequency*(t+0.008*math.sin(t*25))
            # A small voiced chirp with vowel-like formants, not a character voice imitation.
            tone=sum(math.sin(phase*k)*math.exp(-((frequency*k-vowel)/900)**2)/k for k in range(1,12))
            result.append(env*tone*.3)
        result.extend([0.0]*int(.025*RATE))
    save(name,result)
exclamation('attack',[(510,.14,1100),(640,.10,1600)])
exclamation('hurt',[(720,.12,1700),(450,.17,950)])
exclamation('death',[(570,.13,1250),(420,.19,850),(290,.24,650)])
exclamation('revive',[(470,.12,1100),(590,.12,1400),(740,.24,1900)])
for name,duration,pitch in [('slash',.18,900),('dash',.22,1200),('impact',.16,280),('menu',.12,1000)]:
    samples=[]
    for i in range(int(duration*RATE)):
        t=i/RATE; env=math.exp(-t*22)*min(1,t*250)
        samples.append(env*(math.sin(2*math.pi*pitch*t*(1-t/duration*.6))*.35 + (rng.random()*2-1)*(.13 if name!='menu' else .01)))
    save(name,samples)
def music(name,progression,bpm,lead):
    beat=60/bpm; duration=beat*32; result=[0.0]*int(duration*RATE)
    def note(start,length,midi,volume):
        freq=440*2**((midi-69)/12)
        for i in range(int(length*RATE)):
            at=int(start*RATE)+i
            if at>=len(result): break
            t=i/RATE; env=min(1,t/.01)*min(1,(length-t)/.08)*math.exp(-t*1.8)
            result[at]+=env*volume*(math.sin(2*math.pi*freq*t)+.22*math.sin(2*math.pi*freq*2*t))
    for b in range(32):
        root=progression[(b//4)%len(progression)]
        note(b*beat,beat*.95,root-12,.075)
        note(b*beat,beat*.48,root+[0,4,7,12][b%4],.09)
        note((b+.5)*beat,beat*.48,root+[7,12,4,7][b%4],.065)
        if b%2==0: note(b*beat,beat*1.6,lead[(b//2)%len(lead)],.13)
    # Periodic phrase with a quiet end; game events control loop/stop.
    save(name,result)
music('menu_music',[60,57,65,67],100,[72,76,79,76,74,72,71,74])
music('explore_music',[62,59,67,69],112,[74,78,81,78,76,74,73,76])
music('battle_music',[57,53,60,55],148,[69,72,76,72,67,71,74,71])
print('Created 8 original demo cues and 3 original music loops')
