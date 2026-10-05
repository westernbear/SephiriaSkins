"""Bind new shared art to the exact catalog keyframes, without exporting gameplay data."""
import pathlib, json, shutil, math
from PIL import Image
catalog=json.loads(pathlib.Path('catalog/catalog-1.0.33.json').read_text(encoding='utf-8'))
out=pathlib.Path('packs/hachiware'); out.mkdir(parents=True,exist_ok=True)
for f in ['atlas.png','effects.png','ui.png','PROVENANCE.md','prompts.txt','LiberationSans-OFL.txt']: shutil.copyfile(pathlib.Path('assets/hachiware')/f,out/f)
shutil.copytree('assets/hachiware/audio',out/'audio',dirs_exist_ok=True)
resources={}
def cells(file,columns,rows,prefix,ppu_factor=1):
    w,h=Image.open(out/file).size
    for row in range(rows):
        for col in range(columns):
            x0=round(col*w/columns); x1=round((col+1)*w/columns)
            y0=round((rows-row-1)*h/rows); y1=round((rows-row)*h/rows)
            resources[f'{prefix}.{row}.{col}']=dict(file=file,kind='sprite',rect=[x0,y0,x1-x0,y1-y0],pivot=[.5,.2 if prefix=='body' else .5],pixelsPerUnit=(y1-y0)*ppu_factor)
cells('atlas.png',8,4,'body')
cells('effects.png',4,2,'fx',.75)
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
m=dict(schemaVersion=1,id='fan.hachiware',version='0.1.0',name='하치와레 · 푸른 모험',author='SephiriaSkins fan project',
    description='새 픽셀 팬아트 · 파랑/흰색 · 비언어 합성 데모 발성 및 오리지널 음악. 정식 v1 출시 전 검증 중.',preview='body.3.0',
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
    elif 'GREATSWORD' in state: return [f'body.2.{[0,1,2][min(2,math.floor(i*3/max(1,len(indices))))]}' for i in indices]
    elif 'WHIRLWINDREADY' in state: poses=[5]
    else: poses=[4,5]
    return [f'body.{row}.{poses[i%len(poses)]}' for i in indices]
def weapon_resource(path):
    text=path.lower()
    col=1
    if 'katana' in text: col=2
    elif 'dagger' in text: col=3
    elif 'shield' in text: col=4
    elif 'crossbow' in text: col=6
    elif 'bow' in text: col=5
    elif 'staff' in text or 'spear' in text: col=7
    return f'body.3.{col}'
for key,a in catalog['animations'].items():
    indices=a['frameIndices']; role=a['role']
    if role=='body': section='body'; frames=body_frames(a['state'],indices)
    elif role=='weapon': section='weapons'; frames=[weapon_resource(a['referencePath']) for _ in indices]
    else:
        name=a['setName'].lower()
        # Catalog includes NPC animation sets; only owned local effects ever apply.
        if not any(x in name for x in ['swing','sweep','slash','bullet','cast','dash','guard','parry','revive','hitfx','attackfx','magic','arrow']): continue
        row,col=(0,0)
        if 'dash' in name: row,col=0,1
        elif 'bullet' in name or 'magic' in name: row,col=0,2
        elif 'guard' in name or 'parry' in name: row,col=0,3
        elif 'revive' in name: row,col=1,0
        elif 'hit' in name: row,col=1,1
        elif 'cast' in name: row,col=1,2
        elif 'arrow' in name: row,col=1,3
        section='effects'; frames=[f'fx.{row}.{col}' for _ in indices]
    m[section][key]=dict(frames=frames,frameIndices=indices)
for key,visual in catalog['visuals'].items():
    if visual['role']=='weapon' and visual['spriteName']:
        m['visuals'][key]=dict(sprite=weapon_resource(visual['referencePath']))
for key,u in catalog['ui'].items():
    path=u['referencePath'].lower(); leaf=path.split('/')[-1]
    if u['component']=='Image' and any(x in leaf for x in ['face','portrait']) and 'player' in path:
        m['ui'][key]=dict(sprite='body.3.0')
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
shutil.copyfile(out/'atlas.png',example/'atlas.png')
simple=dict(schemaVersion=1,id='example.simple',version='1.0.0',name='간편형 예제',author='SephiriaSkins',compatibleCatalogs=[catalog['id']],
    description='모든 코스튬의 앞 방향 대기만 교체합니다. 움직임·공격·뒤 방향은 원본입니다.',
    resources={'idle':resources['body.0.0']},preview='idle',body={key:dict(frames=['idle']*len(a['frameIndices']),frameIndices=a['frameIndices'])
        for key,a in catalog['animations'].items() if a['role']=='body' and a['state']=='IDLE'})
(example/'skin.json').write_text(json.dumps(simple,ensure_ascii=False,indent=2),encoding='utf-8')
print('Hachiware', {k:len(m[k]) for k in ['body','weapons','effects','visuals','ui','audio']})
