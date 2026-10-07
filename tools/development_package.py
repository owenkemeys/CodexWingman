"""Publish a verified PR candidate, separately from main-only stable releases."""
import argparse
from pathlib import Path
import shutil
import subprocess
import zipfile

import release_provenance as provenance


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--dotnet', default='dotnet')
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    receipt = provenance.verified(root)
    output = args.output.resolve()
    if output.exists():
        raise ValueError('Candidate output must be a new directory')
    package = output / 'Wingman-development'
    subprocess.run([args.dotnet, 'publish', str(root / 'src/CodexWingman/CodexWingman.csproj'),
                    '-c', 'Release', '-r', 'win-x64', '--self-contained', 'false',
                    '-p:EnableWindowsTargeting=true', '-p:NuGetAudit=false',
                    '-p:SourceRevisionId=' + receipt['commit'], '-o', str(package)], check=True)
    if provenance.source(root) != {key: receipt[key] for key in ('commit', 'tree')}:
        raise ValueError('Source changed while publishing the candidate')
    for name in ('CodexWingman.exe', 'Helpers', 'WINDOWS-START-HERE.md', 'Apply-CodexAppUpdate.ps1'):
        if not (package / name).exists():
            raise ValueError('Incomplete candidate: ' + name)
    record = {'schema': 'wingman.development-package.v1', 'channel': 'development',
              'repository': provenance.repository_url(root), 'commit': receipt['commit'],
              'tree': receipt['tree'], 'verification': receipt, 'files': provenance.package_files(package)}
    # Deliberately not release.json: the stable updater must never accept a PR candidate.
    provenance.write_json(package / 'development.json', record)
    archive = output / ('Wingman-development-' + receipt['commit'][:12] + '-win-x64.zip')
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as zipped:
        for path in sorted(package.rglob('*')):
            if path.is_file():
                zipped.write(path, path.relative_to(output).as_posix())
    archive.with_suffix('.zip.sha256').write_text(provenance.digest(archive) + '  ' + archive.name + '\n')
    shutil.rmtree(package)
    print(archive)


if __name__ == '__main__':
    main()
