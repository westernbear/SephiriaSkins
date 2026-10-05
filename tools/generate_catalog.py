"""Read metadata only from the local installation; never redistribute game images/DLLs."""
import argparse, hashlib, json, pathlib, collections, uuid, struct
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

p = argparse.ArgumentParser()
p.add_argument('game', type=pathlib.Path)
p.add_argument('--out', type=pathlib.Path, default=pathlib.Path('catalog/catalog-1.0.33.json'))
p.add_argument('--runtime', type=pathlib.Path, help='Merge UI/audio from a game-exported catalog of the same installation')
a = p.parse_args()
data = a.game / 'Sephiria_Data'
paths = [x for x in data.iterdir() if x.name.endswith('.assets') or x.name in ['level0', 'level1', 'level2']]
env = UnityPy.load(*(str(x) for x in paths))
files = {pathlib.Path(k).name: v for k, v in env.files.items()}
version = files['globalgamemanagers.assets'].unity_version
generator = TypeTreeGenerator(version)
generator.load_local_dll_folder(str(data / 'Managed'))
counts, failures = collections.Counter(), []
def resolve(obj, ref):
    if not ref or ref['m_PathID'] == 0: return None
    f = obj.assets_file
    if ref['m_FileID']:
        name = pathlib.PurePosixPath(f.externals[ref['m_FileID'] - 1].path.replace('\\', '/')).name
        f = files.get(name)
    return f.objects.get(ref['m_PathID']) if f else None
def identity(o): return (o.assets_file.name, o.path_id)
mono, trees, transforms, gameobjects, sprite_names, renderers = {}, {}, {}, {}, {}, {}
for o in env.objects:
    k = identity(o)
    if o.type.name == 'MonoBehaviour':
        base = o.read_typetree(check_read=False)
        script = resolve(o, base.get('m_Script'))
        if not script: continue
        s = script.read_typetree()
        cls = s['m_ClassName']
        mono[k] = (cls, o, base, s)
        counts[cls] += 1
    elif o.type.name in ['Transform', 'RectTransform']: transforms[k] = (o, o.read_typetree())
    elif o.type.name == 'GameObject': gameobjects[k] = o.read_typetree()
    elif o.type.name == 'Sprite': sprite_names[k] = o.read_typetree()['m_Name']
    elif o.type.name == 'SpriteRenderer': renderers[k] = (o, o.read_typetree())

def tree(k):
    if k in trees: return trees[k]
    cls, o, base, s = mono[k]
    fullname = (s['m_Namespace'] + '.' if s['m_Namespace'] else '') + cls
    try: d = o.read_typetree(nodes=generator.get_nodes_up(s['m_AssemblyName'], fullname))
    except Exception as ex:
        failures.append({'asset': list(k), 'class': cls, 'error': str(ex)})
        d = base
    trees[k] = d
    return d

def refkey(o, ref):
    r = resolve(o, ref)
    return identity(r) if r else None
go_transforms = {}
for k, (o, d) in transforms.items(): go_transforms[refkey(o, d['m_GameObject'])] = k
def hierarchy(go):
    result = []
    while go in gameobjects:
        name = gameobjects[go]['m_Name'].replace('(Clone)', '').strip()
        tk = go_transforms.get(go)
        if not tk: result.append(name); break
        o, d = transforms[tk]
        parent = refkey(o, d['m_Father'])
        if parent and parent in transforms:
            po, pd = transforms[parent]
            index = 0
            for ref in pd['m_Children']:
                ck = refkey(po, ref)
                if ck == tk: break
                if ck in transforms:
                    co, cd = transforms[ck]
                    cg = refkey(co, cd['m_GameObject'])
                    if cg in gameobjects and gameobjects[cg]['m_Name'] == gameobjects[go]['m_Name']: index += 1
            if index: name += '[' + str(index) + ']'
        result.append(name)
        if not parent or parent not in transforms: break
        po, pd = transforms[parent]
        go = refkey(po, pd['m_GameObject'])
    return '/'.join(reversed(result))

catalog = dict(schemaVersion=1, id='sephiria-1.0.33', gameVersion='1.0.33', unityVersion=version,
    assemblySha256=hashlib.sha256((data/'Managed/Assembly-CSharp.dll').read_bytes()).hexdigest(), complete=False,
    animations={}, visuals={}, ui={}, audio={}, costumes={}, weaponTypes={})
# Find animation roles from prefab roots rather than ambiguous asset names.
roots = {}
for k, (cls,o,base,s) in mono.items():
    if cls == 'PlayerAvatarCostume': roots[refkey(o,base['m_GameObject'])] = 'body'
    elif cls.startswith('WeaponSimple') or cls.startswith('NewWeapon') and cls not in ['NewWeaponController','NewWeaponControlAnimator','NewWeaponAnimationTransitionController']:
        go = refkey(o,base['m_GameObject'])
        if go: roots[go] = 'weapon'
def role_of(go):
    while go:
        if go in roots: return roots[go]
        tk = go_transforms.get(go)
        if not tk: break
        o,d=transforms[tk]; parent=refkey(o,d['m_Father'])
        if parent not in transforms: break
        po,pd=transforms[parent]; go=refkey(po,pd['m_GameObject'])
    return 'effect'

def audio_refs(value, path):
    if isinstance(value,dict):
        if 'Guid' in value and isinstance(value['Guid'],dict):
            gd=value['Guid']
            # FMOD GUID.ToString uses four 32-bit chunks; record raw metadata as well.
            if any(isinstance(v,int) and v for v in gd.values()):
                key='guid:{' + str(uuid.UUID(bytes_le=struct.pack('<iiii',*[gd['Data'+str(i)] for i in range(1,5)]))) + '}'
                catalog['audio'].setdefault(key,dict(path=key,channel='music' if 'bgm' in path.lower() else 'sfx',referencePath=path))
        for key,v in value.items(): audio_refs(v,path+'/'+key)
    elif isinstance(value,list):
        for i,v in enumerate(value): audio_refs(v,path+'/'+str(i))

for k,(cls,o,base,s) in mono.items():
    if cls == 'CostumeSkinEntity':
        d=tree(k); catalog['costumes'][d.get('skinID',str(k))]=d.get('relatedCostumeID','')
    elif cls == 'WeaponEntity':
        d=tree(k); prefab=refkey(o,d.get('mainWeaponPrefab')); catalog['weaponTypes'][str(d.get('id',k))]=gameobjects.get(prefab,{}).get('m_Name','')
    if cls in ['Image','RawImage','TextMeshProUGUI','TextMeshPro']:
        path=hierarchy(refkey(o,base['m_GameObject']))
        key='ui/'+path+'/'+cls
        catalog['ui'][key]=dict(role=path.split('/')[0],referencePath=path,component=cls)
    if cls.startswith('Animator2D_'):
        d=tree(k); setkey=refkey(o,d.get('currentSet'))
        if not setkey or setkey not in mono or mono[setkey][0]!='AnimationSet': continue
        sd=tree(setkey); so=mono[setkey][1]
        role=role_of(refkey(o,base['m_GameObject']))
        for state in sd.get('sprites',[]):
            timeline=state.get('timeline',[])
            indices=[f['frameIdx'] for f in timeline]
            names=[sprite_names.get(refkey(so,f['sprite']),'') for f in timeline]
            signature=sd['m_Name']+'\n'+state['state']+'\n'+str(state['fps'])+'\n'+('1' if state['repeat'] else '0')+'\n'+','.join(map(str,indices))+'\n'+'\n'.join(names)
            key=role+'/'+sd['m_Name']+'/'+state['state']+'/'+hashlib.sha256(signature.encode()).hexdigest()[:12]
            catalog['animations'][key]=dict(role=role,setName=sd['m_Name'],state=state['state'],fps=state['fps'],repeat=bool(state['repeat']),
                referencePath=hierarchy(refkey(o,base['m_GameObject']))+' @ '+str(k),frameIndices=indices,spriteNames=names,
                events=[str(f['frame'])+':'+ev['componentName']+'.'+ev['methodName'] for f in state.get('frameEvents',[]) for ev in f['events']])
    if cls in ['AnimationSet','NewWeaponFireData','SoundManager'] or cls.startswith('NewWeaponFireData_'):
        audio_refs(tree(k),str(k))
for k,(o,d) in renderers.items():
    go=refkey(o,d['m_GameObject']); role=role_of(go)
    if role!='weapon': continue
    path=hierarchy(go)
    catalog['visuals']['weapon/'+path+'/SpriteRenderer']=dict(role=role,referencePath=path,spriteName=sprite_names.get(refkey(o,d.get('m_Sprite')),''))
catalog['complete']=not failures
if a.runtime:
    runtime=json.loads(a.runtime.read_text(encoding='utf-8-sig'))
    for field in ['id','unityVersion','assemblySha256']:
        if runtime.get(field) != catalog[field]: raise ValueError('Runtime catalog mismatch: '+field)
    # Runtime roles of inactive costume prefabs are ambiguous. Keep static animation identities.
    for section in ['ui','audio']: catalog[section].update(runtime[section])
a.out.parent.mkdir(parents=True,exist_ok=True)
a.out.write_text(json.dumps(catalog,ensure_ascii=False,indent=2),encoding='utf-8')
(a.out.parent/'extraction-report.json').write_text(json.dumps(dict(counts=counts,failures=failures),ensure_ascii=False,indent=2),encoding='utf-8')
print('Catalog:',len(catalog['animations']),'animations,',len(catalog['ui']),'UI roles,',len(catalog['audio']),'event references;',len(failures),'decode failures',flush=True)
