import UnityPy, collections, sys
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
game = sys.argv[1]
g = TypeTreeGenerator('6000.3.21f1')
g.load_local_game(game)
e = UnityPy.load(game + '/Sephiria_Data/resources.assets')
e.typetree_generator = g
counts = collections.Counter()
for o in e.objects:
    if o.type.name != 'MonoBehaviour': continue
    try:
        d = o.read()
        s = d.m_Script.read()
        counts[s.m_ClassName] += 1
        if s.m_ClassName in ['AnimationSet','CostumeSkinEntity','Animator2D_SpriteRenderer'] and counts[s.m_ClassName] == 1:
            print(s.m_ClassName, o.path_id, o.read_typetree(), flush=True)
    except Exception as ex:
        counts['errors:'+type(ex).__name__] += 1
print(counts)
