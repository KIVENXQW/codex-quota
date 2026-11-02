const numberOrNull = value => typeof value === 'number' && Number.isFinite(value) ? value : null;

function windowLabel(minutes) {
  if (minutes === 10080) return '本周额度';
  if (minutes === 1440) return '每日额度';
  if (minutes && minutes % 60 === 0) return `${minutes / 60} 小时额度`;
  return minutes ? `${minutes} 分钟额度` : '使用额度';
}

export function normalizeLimits(raw = {}) {
  const buckets = raw.rateLimitsByLimitId && Object.keys(raw.rateLimitsByLimitId).length
    ? Object.values(raw.rateLimitsByLimitId) : raw.rateLimits ? [raw.rateLimits] : [];
  return {
    limits: buckets.filter(Boolean).map(bucket => ({
      id: bucket.limitId || 'codex',
      name: bucket.limitName || 'Codex',
      planType: bucket.planType ?? null,
      windows: ['primary', 'secondary'].filter(kind => bucket[kind]).map(kind => {
        const window = bucket[kind];
        const used = numberOrNull(window.usedPercent);
        const minutes = numberOrNull(window.windowDurationMins);
        return {
          kind, label: windowLabel(minutes), usedPercent: used,
          remainingPercent: used === null ? null : Math.min(100, Math.max(0, 100 - used)),
          windowDurationMins: minutes, resetsAt: numberOrNull(window.resetsAt)
        };
      }),
      credits: bucket.credits ? {
        balance: bucket.credits.balance == null ? null : String(bucket.credits.balance),
        hasCredits: bucket.credits.hasCredits === true,
        unlimited: bucket.credits.unlimited === true
      } : null
    })),
    resetCreditsCount: numberOrNull(raw.rateLimitResetCredits?.availableCount)
  };
}
