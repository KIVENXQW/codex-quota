import { readFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';

const repository = 'KIVENXQW/codex-quota';
const releaseApi = `https://api.github.com/repos/${repository}/releases`;
const versionPattern = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/;
const assetName = 'CodexWeekly-win-x64.zip';

function weeklyVersion(release) {
  if (release?.draft !== false || release.prerelease !== false) return null;
  if (typeof release.tag_name !== 'string' || !release.tag_name.startsWith('weekly-v')) return null;
  const version = release.tag_name.slice('weekly-v'.length);
  if (!versionPattern.test(version) || !Array.isArray(release.assets)) return null;
  const uploaded = release.assets.some(asset =>
    asset?.name === assetName && asset.state === 'uploaded' && asset.size > 0);
  return uploaded ? version : null;
}

function compareVersions(left, right) {
  const a = left.split('.').map(BigInt);
  const b = right.split('.').map(BigInt);
  for (let i = 0; i < a.length; i++) {
    if (a[i] > b[i]) return 1;
    if (a[i] < b[i]) return -1;
  }
  return 0;
}

export async function checkWeeklyUpdate(installed, fetchPage = fetch) {
  const version = typeof installed === 'string' && versionPattern.test(installed) ? installed : '';
  const unavailable = { status: 'unavailable', version, url: null };
  if (!version) return unavailable;
  const options = {
    signal: AbortSignal.timeout(6000),
    redirect: 'error',
    headers: { Accept: 'application/vnd.github+json', 'User-Agent': 'CodexWeekly-Update' }
  };
  try {
    let latest = null;
    for (let page = 1; ; page++) {
      const response = await fetchPage(`${releaseApi}?per_page=100&page=${page}`, options);
      if (!response.ok) return unavailable;
      const releases = await response.json();
      if (!Array.isArray(releases)) return unavailable;
      for (const release of releases) {
        const candidate = weeklyVersion(release);
        if (candidate && (!latest || compareVersions(candidate, latest) > 0)) latest = candidate;
      }
      if (!/;\s*rel="next"/i.test(response.headers.get('link') || '')) break;
    }
    if (!latest) return { status: 'unpublished', version, url: null };
    if (compareVersions(latest, version) <= 0) return { status: 'current', version, url: null };
    return {
      status: 'available', version: latest,
      url: `https://github.com/${repository}/releases/tag/weekly-v${latest}`
    };
  } catch {
    return unavailable;
  }
}

if (process.argv[1] && pathToFileURL(process.argv[1]).href === import.meta.url) {
  let version;
  try {
    ({ version } = JSON.parse(await readFile(new URL('./version.json', import.meta.url), 'utf8')));
  } catch {
    // Keep the one-line result available to the tray when the local file is damaged.
  }
  console.log(JSON.stringify(await checkWeeklyUpdate(version)));
}
