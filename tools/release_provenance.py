"""Commit-bound Wingman verification and package provenance. No network credentials stored."""
from pathlib import Path
import argparse, datetime, hashlib, json, os, re, subprocess, sys

SCHEMA = 'codexwingman.release.v1'

def run(args, root, capture=True):
    result = subprocess.run([str(x) for x in args], cwd=root, check=True,
                            stdout=subprocess.PIPE if capture else None,
                            stderr=subprocess.PIPE if capture else None, text=True)
    return result.stdout.strip() if capture else None

def git(root, *args):
    return run(['git', *args], root)

def source(root):
    root = Path(root).resolve()
    if Path(git(root, 'rev-parse', '--show-toplevel')).resolve() != root:
        raise ValueError('Project root must be the repository root')
    if git(root, 'status', '--porcelain', '--untracked-files=all'):
        raise ValueError('Uncommitted or untracked source: commit the scoped work before verification or publishing')
    return {'commit': git(root, 'rev-parse', 'HEAD'), 'tree': git(root, 'rev-parse', 'HEAD^{tree}')}

def receipt_path(root):
    path = Path(git(root, 'rev-parse', '--git-path', 'wingman/verified.json'))
    return path if path.is_absolute() else root / path

def digest(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''): h.update(block)
    return h.hexdigest()

def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + '.tmp')
    temp.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')
    temp.replace(path)

def verified(root):
    state = source(root)
    record = json.loads(receipt_path(root).read_text(encoding='utf-8'))
    if record.get('schema') != 'codexwingman.verification.v1' or any(record.get(k) != v for k, v in state.items()):
        raise ValueError('Verification receipt does not match this exact commit and tree')
    if record.get('checks') != ['release-contract', 'helpers', 'core', 'windows-build']:
        raise ValueError('Required verification checks are incomplete')
    return record

def verify_source(root, node, dotnet):
    before = source(root)
    receipt_path(root).unlink(missing_ok=True)
    commands = [
        [sys.executable, '-m', 'unittest', 'discover', '-s', 'tests/release', '-p', 'test_*.py'],
        [node, '--test', *[str(p.relative_to(root)) for p in sorted((root / 'tests/helpers').glob('*.test.mjs'))]],
        [dotnet, 'run', '--project', 'tests/CodexWingman.Core.Tests/CodexWingman.Core.Tests.csproj', '-c', 'Release'],
        [dotnet, 'build', 'src/CodexWingman/CodexWingman.csproj', '-c', 'Release', '-p:EnableWindowsTargeting=true'],
    ]
    for command in commands: run(command, root, capture=False)
    if source(root) != before: raise ValueError('Source changed while verification was running')
    record = {'schema': 'codexwingman.verification.v1', **before,
              'checks': ['release-contract', 'helpers', 'core', 'windows-build'],
              'verifiedAt': datetime.datetime.now(datetime.timezone.utc).isoformat()}
    write_json(receipt_path(root), record)
    return record

def repository_url(root):
    config = root / 'release-repository.json'
    url = json.loads(config.read_text(encoding='utf-8'))['repository'] if config.exists() else 'https://github.com/owenkemeys/CodexWingman'
    if not re.fullmatch(r'https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', url):
        raise ValueError('Release repository must be an explicit GitHub HTTPS repository URL')
    return url

def require_remote_main(root, commit):
    remote = git(root, 'remote', 'get-url', 'origin')
    url = repository_url(root)
    if remote not in (url, url + '.git', 'git@github.com:' + url.removeprefix('https://github.com/') + '.git'):
        raise ValueError('Release origin must match the configured GitHub repository')
    live = git(root, 'ls-remote', '--exit-code', 'origin', 'refs/heads/main').split()
    if not live or live[0] != commit:
        raise ValueError('Only the current published main commit may produce an installable package')

def package_files(root):
    entries = {}
    for path in sorted(root.rglob('*')):
        if path.is_symlink(): raise ValueError('Package links are not allowed')
        if path.is_file() and path.relative_to(root).as_posix() != 'release.json':
            entries[path.relative_to(root).as_posix()] = digest(path)
    return entries

def seal(root, package):
    record = verified(root)
    require_remote_main(root, record['commit'])
    if not (package / 'CodexWingman.exe').is_file() or not (package / 'Helpers').is_dir():
        raise ValueError('Package is missing Wingman or its helpers')
    # Check every packaged helper against committed source, including overridden working copies.
    sources = {}
    for manifest in (root / 'helpers').glob('*/wingman.json'):
        sources[json.loads(manifest.read_text(encoding='utf-8-sig'))['id']] = manifest.parent
    for manifest in (package / 'Helpers').glob('*/wingman.json'):
        helper_id = json.loads(manifest.read_text(encoding='utf-8-sig'))['id']
        original = sources.get(helper_id)
        if original is None: raise ValueError('Untracked packaged helper: ' + helper_id)
        for path in manifest.parent.rglob('*'):
            if path.is_file():
                other = original / path.relative_to(manifest.parent)
                if not other.is_file() or not git(root, 'ls-files', '--error-unmatch', str(other.relative_to(root))) or digest(path) != digest(other):
                    raise ValueError('Packaged helper differs from committed source: ' + helper_id)
    result = {'schema': SCHEMA, 'repository': repository_url(root),
              'commit': record['commit'], 'tree': record['tree'], 'verification': record,
              'files': package_files(package)}
    write_json(package / 'release.json', result)
    return {'commit': result['commit'], 'files': len(result['files'])}

def verify_package(package):
    record = json.loads((package / 'release.json').read_text(encoding='utf-8'))
    if record.get('schema') != SCHEMA or not record.get('commit') or not record.get('files'):
        raise ValueError('Missing or invalid package provenance')
    if package_files(package) != record['files']:
        raise ValueError('Package has missing, added, or modified files; do not install')
    return {'commit': record['commit'], 'files': len(record['files'])}

def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('command',choices=['verify-source','check-source','seal','verify-package'])
    p.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[1])
    p.add_argument('--package',type=Path)
    p.add_argument('--node',default='node');p.add_argument('--dotnet',default='dotnet')
    args=p.parse_args();root=args.root.resolve()
    try:
        if args.command=='verify-source': result=verify_source(root,args.node,args.dotnet)
        elif args.command=='check-source': result=verified(root); require_remote_main(root,result['commit'])
        elif not args.package: raise ValueError('--package is required')
        elif args.command=='seal':result=seal(root,args.package.resolve())
        else:result=verify_package(args.package.resolve())
        print(json.dumps(result))
    except (ValueError,OSError,KeyError,subprocess.CalledProcessError) as e:
        print('Release gate failed: '+str(e),file=sys.stderr);return 1
    return 0
if __name__=='__main__':sys.exit(main())
