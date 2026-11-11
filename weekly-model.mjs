const unavailable = error => ({ available: false, remainingPercent: null, resetsAt: null, error });

export function selectWeekly(raw, nowSeconds = Date.now() / 1000) {
  const buckets = raw?.rateLimitsByLimitId;
  const codex = buckets?.codex ?? Object.values(buckets ?? {}).find(value => value?.limitId === 'codex');
  const fallback = raw?.rateLimits;
  const selected = codex ?? (!fallback?.limitId || fallback.limitId === 'codex' ? fallback : null);
  const window = [selected?.primary, selected?.secondary].find(value => value?.windowDurationMins === 10080);
  if (!window) return unavailable('周额度暂不可用');
  const { usedPercent, resetsAt } = window;
  if (!Number.isFinite(usedPercent) || usedPercent < 0 || usedPercent > 100 ||
      !Number.isSafeInteger(resetsAt) || resetsAt <= 0 || !Number.isFinite(new Date(resetsAt * 1000).getTime())) {
    return unavailable('周额度数据不完整');
  }
  if (resetsAt <= nowSeconds) return unavailable('重置时间已到，等待刷新');
  return { available: true, remainingPercent: 100 - usedPercent, resetsAt, error: null };
}
