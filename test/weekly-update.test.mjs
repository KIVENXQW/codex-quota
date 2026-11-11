import assert from 'node:assert/strict';
import test from 'node:test';
import { checkWeeklyUpdate } from '../weekly-update.mjs';

const installed = '1.9.0';
const asset = { name: 'CodexWeekly-win-x64.zip', state: 'uploaded', size: 1234 };
const release = (tag_name, overrides = {}) => ({
  tag_name, draft: false, prerelease: false, assets: [asset], ...overrides
});
const page = (entries, link) => Response.json(entries, { headers: link ? { link } : {} });
const simple = (status) => ({ status, version: installed, url: null });

test('selects the highest weekly version across pages and constructs the official release URL', async () => {
  const result = await checkWeeklyUpdate(installed, async url => {
    const endpoint = new URL(url);
    assert.equal(endpoint.origin + endpoint.pathname, 'https://api.github.com/repos/KIVENXQW/codex-quota/releases');
    if (endpoint.searchParams.get('page') === '1') {
      return page([release('weekly-v1.9.1'), release('v99.0.0')],
        '<https://api.github.com/repos/KIVENXQW/codex-quota/releases?per_page=100&page=2>; rel="next"');
    }
    assert.equal(endpoint.searchParams.get('page'), '2');
    return page([release('weekly-v1.10.0', { html_url: 'https://example.com/foreign' }), release('weekly-v1.8.4')]);
  });
  assert.deepEqual(result, {
    status: 'available', version: '1.10.0', url: 'https://github.com/KIVENXQW/codex-quota/releases/tag/weekly-v1.10.0'
  });
});

test('filters other editions, drafts, prereleases, malformed tags and unusable ZIPs', async () => {
  const entries = [
    release('v2.0.0'), release('weekly-v2.0.0', { draft: true }),
    release('weekly-v2.0.0', { prerelease: true }), release('weekly-v2.0.0-rc.1'),
    release('weekly-v2.0.0/extra'), release('weekly-v2.0.0', { assets: [] }),
    ...[{ name: 'CodexQuota-win-x64.zip' }, { state: 'starter' }, { size: 0 }].map(change =>
      release('weekly-v2.0.0', { assets: [{ ...asset, ...change }] }))
  ];
  assert.deepEqual(await checkWeeklyUpdate(installed, () => page(entries)), simple('unpublished'));
});

test('equal and older official releases leave the installed version current', async () => {
  for (const tag of ['weekly-v1.9.0', 'weekly-v1.8.12']) {
    assert.deepEqual(await checkWeeklyUpdate(installed, () => page([release(tag)])), simple('current'));
  }
});

test('errors, rate limits and incomplete pagination return unavailable', async () => {
  const results = [
    () => { throw new Error('offline'); },
    () => new Response(null, { status: 403 }),
    () => page({ message: 'invalid' }),
    url => new URL(url).searchParams.get('page') === '1'
      ? page([release('weekly-v4.0.0')], '<https://api.github.com/repos/KIVENXQW/codex-quota/releases?page=2>; rel="next"')
      : new Response(null, { status: 503 })
  ];
  for (const source of results) {
    assert.deepEqual(await checkWeeklyUpdate(installed, source), simple('unavailable'));
  }
});
