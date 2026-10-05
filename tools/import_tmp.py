"""Import Unity's bundled TMP Essential Resources for unattended editor builds."""
import pathlib, tarfile, sys
package=pathlib.Path(sys.argv[1]); project=pathlib.Path(sys.argv[2]).resolve()
with tarfile.open(package,'r:gz') as archive:
    groups={}
    for entry in archive.getmembers():
        if entry.isfile():
            parts=entry.name.lstrip('./').split('/')
            groups.setdefault(parts[0],{})[parts[-1]]=entry
    for files in groups.values():
        if 'pathname' not in files or 'asset' not in files: continue
        path=archive.extractfile(files['pathname']).read().decode('utf-8').strip()
        target=(project/path).resolve()
        if not path.startswith('Assets/') or not target.is_relative_to(project/'Assets'): raise ValueError('Unsafe Unity package path')
        target.parent.mkdir(parents=True,exist_ok=True)
        target.write_bytes(archive.extractfile(files['asset']).read())
        if 'asset.meta' in files: target.with_name(target.name+'.meta').write_bytes(archive.extractfile(files['asset.meta']).read())
print('Imported TMP resources')
