"""Discover local Wingman settings; report first, apply only with --apply. No network access."""
from pathlib import Path
import argparse, copy, datetime, json, os, shutil, tempfile


def read_object(path):
    if not path.exists():
        return {}
    value = json.loads(path.read_text(encoding='utf-8-sig'))
    if not isinstance(value, dict):
        raise ValueError(f'Expected a JSON object: {path}')
    return value


def plan(settings_path, registry_path, explicit_vaults=(), helper_root=None):
    current = read_object(settings_path)
    proposed = copy.deepcopy(current)
    warnings = []
    for key in ('helperConfig', 'helperEnabled'):
        if key in proposed and not isinstance(proposed[key], dict):
            raise ValueError(f'{key} must be an object; existing settings were not changed')
    config = proposed.setdefault('helperConfig', {})
    enabled = proposed.setdefault('helperEnabled', {})
    if helper_root is not None:
        if not helper_root.is_dir():
            raise ValueError('The installed Helpers directory does not exist; no settings were changed')
        seen_ids = set()
        for manifest in sorted(helper_root.glob('*/wingman.json')):
            value = read_object(manifest)
            helper_id, inherited = value.get('id'), value.get('config')
            if not isinstance(helper_id, str) or not helper_id:
                raise ValueError('Installed helper has no valid ID: ' + str(manifest))
            if helper_id in seen_ids:
                raise ValueError('Duplicate installed helper ID: ' + helper_id)
            seen_ids.add(helper_id)
            if not isinstance(inherited, dict) or not inherited:
                continue
            if 'mappings' in inherited and not inherited['mappings']:
                continue
            config.setdefault(helper_id, copy.deepcopy(inherited))
    candidates = []
    if explicit_vaults:
        for item in explicit_vaults:
            name, separator, raw = item.partition('=')
            if not separator or not name or any(c in name for c in '/\\'):
                raise ValueError('--vault must be VaultName=absolute-directory')
            candidates.append((name, Path(raw)))
    elif 'obsidian-links' not in config:
        registry = read_object(registry_path)
        vaults = registry.get('vaults', {})
        if not isinstance(vaults, dict):
            raise ValueError('Obsidian registry vaults must be an object')
        for value in vaults.values():
            if isinstance(value, dict) and isinstance(value.get('path'), str):
                path = Path(value['path'])
                candidates.append((path.name, path))
    mappings = []
    seen = set()
    duplicates = {name.casefold() for name, _ in candidates if sum(n.casefold() == name.casefold() for n, _ in candidates) > 1}
    for name, path in candidates:
        if name.casefold() in duplicates:
            warnings.append(f'Ambiguous vault name {name!r}; choose its correct registered vault and mapping explicitly.')
            continue
        if not path.is_absolute() or not path.is_dir() or not (path / '.obsidian').is_dir():
            warnings.append(f'Vault {name!r} is unavailable or has no .obsidian directory; left unconfigured.')
            continue
        prefix = path.as_posix().rstrip('/') + '/'
        if prefix.casefold() not in seen:
            mappings.append({'sourcePrefix': prefix, 'vault': name})
            seen.add(prefix.casefold())
    if explicit_vaults or 'obsidian-links' not in config:
        config['obsidian-links'] = {'mappings': mappings, 'uriAction': 'open'}
        if mappings:
            enabled.setdefault('obsidian-links', True)
        else:
            enabled['obsidian-links'] = False
            warnings.append('Obsidian links are disabled until a vault is configured. Other helpers can be used now.')
    elif not isinstance(config['obsidian-links'], dict):
        warnings.append('Existing Obsidian configuration is not an object; review it before enabling the helper.')
    elif not config['obsidian-links'].get('mappings'):
        warnings.append('Existing Obsidian configuration has no mappings; configure a vault or keep this helper disabled.')
    elif config['obsidian-links'].get('uriAction') != 'open':
        warnings.append('Existing Obsidian configuration uses Wait for Note; confirm that plugin is installed, or set uriAction to open.')
    config.setdefault('jarvis-file-links', {'mappings': [], 'excludePrefixes': []})
    enabled.setdefault('json-debug', False)
    enabled.setdefault('hook-trace', False)
    proposed.setdefault('usageDialsEnabled', True)
    proposed.setdefault('additionalSessionRoots', [])
    proposed.setdefault('forceHighPerformanceGpu', False)
    warnings.append('Remote paths and nonstandard Codex session roots are not guessed. Configure them only if this user needs them.')
    return current, proposed, warnings


def save(path, original, proposed):
    # Re-read before mutation so a concurrent tray/settings edit is not overwritten.
    if read_object(path) != original:
        raise ValueError('Settings changed during discovery; rerun the setup report')
    path.parent.mkdir(parents=True, exist_ok=True)
    backup = None
    if path.exists():
        backup = path.with_name(path.name + '.backup-' + datetime.datetime.now().strftime('%Y%m%dT%H%M%S%f'))
        shutil.copy2(path, backup)
    fd, temporary = tempfile.mkstemp(prefix='.wingman-settings-', dir=path.parent)
    try:
        with os.fdopen(fd, 'w', encoding='utf-8') as stream:
            json.dump(proposed, stream, indent=2)
            stream.write('\n')
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)
    return str(backup) if backup else None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--settings', type=Path, default=Path(os.environ.get('LOCALAPPDATA', Path.home())) / 'CodexWingman/settings.json')
    parser.add_argument('--obsidian-registry', type=Path, default=Path(os.environ.get('APPDATA', Path.home())) / 'obsidian/obsidian.json')
    parser.add_argument('--vault', action='append', default=[], help='VaultName=absolute-directory; repeat for multiple vaults')
    parser.add_argument('--apply', action='store_true')
    parser.add_argument('--import-helper-config', type=Path, help='Existing installation Helpers directory; preserve manifest customization in local settings without changing package files')
    args = parser.parse_args()
    try:
        current, proposed, warnings = plan(args.settings, args.obsidian_registry, args.vault, args.import_helper_config)
        backup = save(args.settings, current, proposed) if args.apply and proposed != current else None
        print(json.dumps({'applied': args.apply, 'changed': proposed != current, 'settingsPath': str(args.settings), 'backup': backup,
                          'settings': proposed, 'notices': warnings, 'next': 'After applying, restart only the identified Wingman process (or reload helpers); verify one real note link.'}, indent=2))
    except (ValueError, OSError) as error:
        parser.exit(1, f'Setup stopped without replacing settings: {error}\n')


if __name__ == '__main__':
    main()
