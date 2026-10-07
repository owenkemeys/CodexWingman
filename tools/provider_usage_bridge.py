"""Read-only Grok billing companion. Publishes quota, never authentication material."""
import argparse
import datetime
import json
import math
import os
from pathlib import Path
import time
import urllib.request


def timestamp(value):
    try:
        parsed = datetime.datetime.fromisoformat(value.replace('Z', '+00:00'))
        return parsed if parsed.tzinfo else None
    except (ValueError, TypeError, AttributeError):
        return None


def normalize_grok_billing(body, checked_at):
    config = body.get('config') if isinstance(body, dict) else None
    if not isinstance(config, dict):
        return None
    period = config.get('currentPeriod')
    if not isinstance(period, dict):
        return None
    kind = {'USAGE_PERIOD_TYPE_WEEKLY': 'weekly', 'USAGE_PERIOD_TYPE_MONTHLY': 'monthly'}.get(period.get('type'))
    start, end, checked = timestamp(period.get('start')), timestamp(period.get('end')), timestamp(checked_at)
    if not kind or not start or not end or not checked or not start <= checked < end:
        return None
    percent = config.get('creditUsagePercent')
    if 'creditUsagePercent' not in config:
        # Matches Grok's actual /usage UI and credit_balance_from_config.
        # Do not apply that default to null/missing configs, failed reads,
        # legacy accounts, or a response without a real current period.
        if config.get('isUnifiedBillingUser') is not True:
            return None
        percent = 0
    if isinstance(percent, bool) or not isinstance(percent, (int, float)) or not math.isfinite(percent) or not 0 <= percent <= 100:
        return None
    window = {'id': 'subscription', 'kind': kind, 'label': kind.title(),
              'usedPercent': percent, 'resetsAt': period['end']}
    minutes = (end - start).total_seconds() / 60
    if minutes.is_integer() and minutes > 0:
        window['windowDurationMins'] = int(minutes)
    return {'checkedAt': checked_at, 'windows': [window]}


def collect(environment_id, instance_id, output):
    now = datetime.datetime.now(datetime.timezone.utc).isoformat()
    snapshot = {'schemaVersion': 1, 'entries': []}
    try:
        home = Path(os.environ.get('GROK_HOME') or Path.home() / '.grok')
        credentials = json.loads((home / 'auth.json').read_text())
        account = credentials.get('https://auth.x.ai::b1a00492-073a-47ea-816f-4c329264a828') or credentials.get('https://accounts.x.ai/sign-in')
        if not account or account.get('auth_mode') == 'api_key' or not account.get('email'):
            raise ValueError('No subscription account')
        request = urllib.request.Request('https://cli-chat-proxy.grok.com/v1/billing?format=credits',
                                        headers={'Authorization': 'Bearer ' + account['key'], 'Cache-Control': 'no-cache'})
        with urllib.request.urlopen(request, timeout=10) as response:
            limits = normalize_grok_billing(json.load(response), now)
        if limits is None:
            raise ValueError('No valid current billing config')
        snapshot['entries'] = [{'environmentId': environment_id, 'instanceId': instance_id,
                                'driver': 'grok', 'accountEmail': account['email'].strip(),
                                'usageLimits': limits}]
    except Exception as error:
        # Clear old quota on failure; no response body or credentials in logs.
        print('Grok usage unavailable: ' + type(error).__name__, flush=True)
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = output.with_suffix('.tmp')
    with temporary.open('w') as stream:
        os.chmod(temporary, 0o600)
        json.dump(snapshot, stream)
        stream.write('\n')
    temporary.replace(output)
    print('Grok usage snapshot refreshed: ' + str(len(snapshot['entries'])) + ' account', flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--environment-id', required=True)
    parser.add_argument('--instance-id', required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--interval', type=int, default=60)
    parser.add_argument('--once', action='store_true')
    args = parser.parse_args()
    if args.interval < 60:
        parser.error('Interval must be at least 60 seconds')
    while True:
        collect(args.environment_id, args.instance_id, args.output)
        if args.once:
            return
        time.sleep(args.interval)


if __name__ == '__main__':
    main()
