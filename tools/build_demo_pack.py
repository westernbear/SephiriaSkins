"""Bind new shared art to the exact catalog keyframes, without exporting gameplay data."""
import pathlib, json, shutil, math
from PIL import Image
catalog=json.loads(pathlib.Path('catalog/catalog-1.0.33.json').read_text(encoding='utf-8'))
out=pathlib.Path('packs/hachiware'); out.mkdir(parents=True,exist_ok=True)
for f in ['body-unarmed.png','effects-clean.png','sasumata.png','ui.png','PROVENANCE.md','prompts.txt','LiberationSans-OFL.txt']: shutil.copyfile(pathlib.Path('assets/hachiware')/f,out/f)
shutil.copytree('assets/hachiware/audio',out/'audio',dirs_exist_ok=True)
resources={}
def cells(file,columns,rows,prefix,ppu_factor=1):
    w,h=Image.open(out/file).size
    for row in range(rows):
        for col in range(columns):
            x0=round(col*w/columns); x1=round((col+1)*w/columns)
            y0=round((rows-row-1)*h/rows); y1=round((rows-row)*h/rows)
            resources[f'{prefix}.{row}.{col}']=dict(file=file,kind='sprite',rect=[x0,y0,x1-x0,y1-y0],pivot=[.5,.2 if prefix=='body' else .5],pixelsPerUnit=(y1-y0)*ppu_factor)
def isolated_body_cells(file, columns, rows):
    # Generation can shift artwork beyond nominal grid boundaries. Detect the
    # connected character, then reference its rectangle; never edit source PNGs.
    image=Image.open(out/file); w,h=image.size
    ink=bytearray(image.getchannel('A').point(lambda a:255 if a>=128 else 0).tobytes())
    components={}
    for start in range(len(ink)):
        if not ink[start]: continue
        ink[start]=0; stack=[start]; count=0; x0=w; x1=0; y0=h; y1=0
        while stack:
            p=stack.pop(); x=p%w; y=p//w; count+=1
            x0=min(x0,x); x1=max(x1,x); y0=min(y0,y); y1=max(y1,y)
            neighbours=[]
            if x: neighbours.append(p-1)
            if x+1<w: neighbours.append(p+1)
            if y: neighbours.append(p-w)
            if y+1<h: neighbours.append(p+w)
            for q in neighbours:
                if ink[q]: ink[q]=0; stack.append(q)
        if count<500: continue
        col=min(columns-1,int((x0+x1+1)*.5/w*columns)); row=min(rows-1,int((y0+y1+1)*.5/h*rows))
        if (row,col) not in components or components[row,col][0]<count: components[row,col]=(count,x0,y0,x1+1,y1+1)
    assert len(components)==columns*rows, f'Expected {columns*rows} body poses, found {len(components)}'
    for (row,col),(_,x0,y0,x1,y1) in sorted(components.items()):
        x0=max(0,x0-2); y0=max(0,y0-2); x1=min(w,x1+2); y1=min(h,y1+2)
        resources[f'body.{row}.{col}']=dict(file=file,kind='sprite',rect=[x0,h-y1,x1-x0,y1-y0],pivot=[.5,2/(y1-y0)],pixelsPerUnit=160)

isolated_body_cells('body-unarmed.png',8,4)
cells('effects-clean.png',4,4,'fx')
weapon=Image.open(out/'sasumata.png'); bounds=weapon.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
assert bounds, 'Empty sasumata sprite'
x0,y0,x1,y1=bounds; padding=16
x0=max(0,x0-padding); y0=max(0,y0-padding); x1=min(weapon.width,x1+padding); y1=min(weapon.height,y1+padding)
resources['weapon.sasumata']=dict(file='sasumata.png',kind='sprite',rect=[x0,weapon.height-y1,x1-x0,y1-y0],pivot=[.5,.2],pixelsPerUnit=y1-y0)
# Exact rectangles in the generated sheet; border corners remain at a stable UI size.
ui_h=Image.open(out/'ui.png').height
ui_rects=[(32,160,396,261),(478,188,382,233),(904,201,394,220),(1330,201,413,220),
          (73,460,254,264),(379,528,506,158),(908,488,389,237),(1327,544,413,128)]
for i,(x,y,w,h) in enumerate(ui_rects):
    resources[f'ui.{i}']=dict(file='ui.png',kind='sprite',rect=[x,ui_h-y-h,w,h],border=[60,60,60,60],pixelsPerUnit=500)
unity_pack=pathlib.Path('examples/unity-pack')
if (unity_pack/'skin.json').exists():
    unity_manifest=json.loads((unity_pack/'skin.json').read_text(encoding='utf-8'))
    resources['theme.font']=unity_manifest['resources']['font']
    shutil.copyfile(unity_pack/resources['theme.font']['file'],out/resources['theme.font']['file'])
for f in (out/'audio').glob('*.wav'): resources[f.stem]=dict(file='audio/'+f.name,kind='audio')
m=dict(schemaVersion=1,id='fan.hachiware',version='0.1.2',name='하치와레 · 푸른 모험',author='SephiriaSkins fan project',
    description='파랑·흰색 팬아트와 하치와레의 파란 사스마타.',preview='body.0.0',
    compatibleCatalogs=[catalog['id']],resources=resources,body={},weapons={},effects={},visuals={},ui={},audio={})
def body_frames(state,indices):
    back='_BACK' in state
    row=1 if back else 0
    if state.startswith('MOVE'): poses=[2,3,2,3]
    elif state.startswith('IDLE'): poses=[0,0,0,1]
    elif state=='FALL': poses=[7]
    elif state=='AIRBORNE': poses=[6,4,6]
    elif state=='HITFEEDBACK': poses=[6]
    elif 'ATTACK' in state: poses=[5]
    elif 'GREATSWORD' in state: return [f'body.{3 if back else 2}.{min(7,n*8//max(1,len(indices)))}' for n,_ in enumerate(indices)]
    elif 'WHIRLWINDREADY' in state: poses=[5]
    else: poses=[4,5]
    return [f'body.{row}.{poses[i%len(poses)]}' for i in indices]
def weapon_resource(path):
    return 'weapon.sasumata'

def effect_frames(name, indices):
    name=name.lower()
    row=1 if 'dash' in name else 2 if any(x in name for x in ['bullet','magic','ammo']) else 3 if any(x in name for x in ['guard','parry','cast']) else 0
    fixed=(3,3) if 'revive' in name else (0,3) if 'hit' in name else (2,3) if 'arrow' in name else None
    return [f'fx.{fixed[0]}.{fixed[1]}' if fixed else f'fx.{row}.{min(2,n*3//max(1,len(indices)))}' for n,_ in enumerate(indices)]
for key,a in catalog['animations'].items():
    indices=a['frameIndices']; role=a['role']
    if role=='body': section='body'; frames=body_frames(a['state'],indices)
    elif role=='weapon':
        section='weapons'
        leaf=a['referencePath'].split(' @ ')[0].split('/')[-1].lower()
        frames=effect_frames(a['setName'],indices) if any(x in leaf for x in ['fx','particle']) else [weapon_resource(a['referencePath']) for _ in indices]
    else:
        name=a['setName'].lower()
        # Catalog includes NPC animation sets; only owned local effects ever apply.
        if not any(x in name for x in ['swing','sweep','slash','bullet','cast','dash','guard','parry','revive','hitfx','attackfx','magic','arrow']): continue
        section='effects'; frames=effect_frames(name,indices)
    m[section][key]=dict(frames=frames,frameIndices=indices)
    if role!='body': m[section][key]['fitOriginal']=True
for key,visual in catalog['visuals'].items():
    if visual['role']=='weapon' and visual['spriteName']:
        leaf=visual['referencePath'].split('/')[-1].lower()
        # Keep native stencil channels aligned; cosmetic add-ons must not draw
        # another complete weapon on top of the main sasumata.
        if any(x in leaf for x in ['addon','additive','unlit']): m['visuals'][key]=dict(hide=True)
        elif leaf in ['bodysprite','weaponstencil','subweaponstencil','scabbard','dualbodysprite','dualbodyspritestencil','bladesprite']:
            m['visuals'][key]=dict(sprite=weapon_resource(visual['referencePath']),fitOriginal=True)
for key,u in catalog['ui'].items():
    path=u['referencePath'].lower(); leaf=path.split('/')[-1]
    if u['component']=='Image' and any(x in leaf for x in ['face','portrait']) and 'player' in path:
        m['ui'][key]=dict(sprite='body.0.0')
    elif u['component']=='Image' and any(x in leaf for x in ['background','header','panelbg','windowframe']):
        index=7 if 'header' in leaf else 6 if 'tooltip' in path else 0 if any(x in path for x in ['conversation','dialog','messagebox']) else 1
        m['ui'][key]=dict(sprite=f'ui.{index}',imageType='sliced',color=[1,1,1,1])
    elif u['component']=='Image' and any(x in leaf for x in ['buttonbg','buttonbackground','selectedbackground']):
        m['ui'][key]=dict(sprite='ui.3' if 'selected' in leaf else 'ui.2',imageType='sliced',color=[1,1,1,1])
    elif u['component']=='TextMeshProUGUI':
        if 'theme.font' in resources: m['ui'][key]=dict(font='theme.font')
        if any(x in leaf for x in ['title','header']): m['ui'].setdefault(key,{})['color']=[.67,.88,1,1]
for key,a in catalog['audio'].items():
    path=a['path'].lower()
    if path.startswith('guid:'): continue # Runtime bank path enrichment makes semantic mappings reviewable.
    resource=None; channel='sfx'; scope='local'; loop=False
    if a['channel']=='music':
        resource='menu_music' if any(x in path for x in ['title','menu','town','maintheme','eventselection']) else 'battle_music' if any(x in path for x in ['boss','battle','combat']) else 'explore_music'
        channel='music'; scope='client'; loop=True
    elif any(x in path for x in ['revive','resurrect']): resource='revive'
    elif any(x in path for x in ['player/dead','player/die','player/death','dieplayer']): resource='death'
    elif 'player' in path and any(x in path for x in ['hurt','damage','hit']): resource='hurt'
    elif 'player' in path and 'attack' in path: resource='attack'
    elif any(x in path for x in ['swing','slash','sweep']): resource='slash'
    elif 'dash' in path: resource='dash'
    elif path.startswith('event:/ui/'): resource='menu'; scope='client'
    if resource: m['audio'][key]=dict(resource=resource,scope=scope,channel=channel,loop=loop,volume=.7)
(out/'skin.json').write_text(json.dumps(m,ensure_ascii=False,indent=2),encoding='utf-8')
example=pathlib.Path('examples/simple-pack'); example.mkdir(parents=True,exist_ok=True)
shutil.copyfile(out/'body-unarmed.png',example/'body-unarmed.png')
simple=dict(schemaVersion=1,id='example.simple',version='1.0.0',name='간편형 예제',author='SephiriaSkins',compatibleCatalogs=[catalog['id']],
    description='모든 코스튬의 앞 방향 대기만 교체합니다. 움직임·공격·뒤 방향은 원본입니다.',
    resources={'idle':resources['body.0.0']},preview='idle',body={key:dict(frames=['idle']*len(a['frameIndices']),frameIndices=a['frameIndices'])
        for key,a in catalog['animations'].items() if a['role']=='body' and a['state']=='IDLE'})
(example/'skin.json').write_text(json.dumps(simple,ensure_ascii=False,indent=2),encoding='utf-8')
print('Hachiware', {k:len(m[k]) for k in ['body','weapons','effects','visuals','ui','audio']})
