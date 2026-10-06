import importlib.util
import pathlib
import unittest


class GrokBillingTests(unittest.TestCase):
    def test_native_zero_requires_a_real_current_billing_config(self):
        path = pathlib.Path(__file__).parents[2] / 'tools/provider_usage_bridge.py'
        spec = importlib.util.spec_from_file_location('provider_usage_bridge', path)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        period = {'type': 'USAGE_PERIOD_TYPE_WEEKLY',
                  'start': '2026-10-05T01:46:48.747158+00:00',
                  'end': '2026-10-12T01:46:48.747158+00:00'}
        config = {'currentPeriod': period, 'isUnifiedBillingUser': True}
        result = module.normalize_grok_billing({'config': config}, '2026-10-06T01:14:00Z')
        self.assertEqual(result['windows'][0]['usedPercent'], 0)
        self.assertEqual(result['windows'][0]['windowDurationMins'], 10080)
        self.assertEqual(result['windows'][0]['resetsAt'], period['end'])
        config['creditUsagePercent'] = 17.5
        self.assertEqual(module.normalize_grok_billing({'config': config}, result['checkedAt'])['windows'][0]['usedPercent'], 17.5)
        config['creditUsagePercent'] = 'invalid'
        self.assertIsNone(module.normalize_grok_billing({'config': config}, result['checkedAt']))
        for body in [{}, {'config': None}, {'config': {}}, {'config': {'currentPeriod': period}}]:
            self.assertIsNone(module.normalize_grok_billing(body, result['checkedAt']))


if __name__ == '__main__':
    unittest.main()
