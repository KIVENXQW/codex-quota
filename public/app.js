'use strict';

(() => {
  const byId = (id) => document.getElementById(id);
  const elements = {
    refresh: byId('refresh'), notice: byId('notice'), loading: byId('loading'),
    content: byId('content'), limits: byId('limits'), extras: byId('extras'),
    usage: byId('usage'), chart: byId('usage-chart'), bars: byId('chart-bars'),
    axis: byId('chart-axis'), readout: byId('chart-readout'),
    summary: byId('usage-summary'), usageNote: byId('usage-note'), status: byId('update-status'),
  };
  const numberFormat = new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 1 });
  const exactNumberFormat = new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 0 });
  const dateFormat = new Intl.DateTimeFormat('zh-CN', {
    timeZone: 'Asia/Shanghai', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', hour12: false,
  });
  const timeFormat = new Intl.DateTimeFormat('zh-CN', {
    timeZone: 'Asia/Shanghai', hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false,
  });
  let snapshot = null;
  let isFetching = false;
  let requestTimer = null;
  let countdowns = [];

  function node(tag, className, text) {
    const element = document.createElement(tag);
    if (className) element.className = className;
    if (text !== undefined && text !== null) element.textContent = String(text);
    return element;
  }

  function finite(value) {
    return typeof value === 'number' && Number.isFinite(value);
  }

  function percent(value) {
    return finite(value) ? Math.max(0, Math.min(100, value)) : null;
  }

  function validDate(value, unixSeconds = false) {
    if (value === null || value === undefined || value === '') return null;
    const date = new Date(unixSeconds ? value * 1000 : value);
    return Number.isFinite(date.getTime()) ? date : null;
  }

  function formatTokens(value) {
    if (!finite(value)) return '未提供';
    if (value >= 100000000) return `${numberFormat.format(value / 100000000)} 亿`;
    if (value >= 10000) return `${numberFormat.format(value / 10000)} 万`;
    return exactNumberFormat.format(value);
  }

  function durationLabel(minutes) {
    if (!finite(minutes) || minutes <= 0) return '';
    if (minutes % 1440 === 0) return `${numberFormat.format(minutes / 1440)} 天周期`;
    if (minutes % 60 === 0) return `${numberFormat.format(minutes / 60)} 小时周期`;
    return `${numberFormat.format(minutes)} 分钟周期`;
  }

  function setNotice(message) {
    elements.notice.textContent = message || '';
    elements.notice.hidden = !message;
  }

  function renderWindow(quota, index) {
    const section = node('section', 'quota-window');
    const remaining = percent(quota.remainingPercent);
    const used = percent(quota.usedPercent);
    if (remaining !== null && remaining <= 10) section.classList.add('is-low');
    if (remaining === 0) section.classList.add('is-exhausted');
    const label = quota.label || `额度窗口 ${index + 1}`;
    const heading = node('div', 'window-heading');
    heading.append(node('h3', '', label));
    const duration = durationLabel(quota.windowDurationMins);
    if (duration) heading.append(node('span', 'window-duration', duration));
    section.append(heading);

    const valueRow = node('div', 'quota-value-row');
    const value = node('div', 'quota-value');
    value.append(node('span', 'quota-number', remaining === null ? '—' : numberFormat.format(remaining)));
    if (remaining !== null) value.append(node('span', 'quota-percent', '%'));
    value.append(node('span', 'quota-remaining-label', remaining === null ? '暂不可用' : '剩余'));
    valueRow.append(value);
    valueRow.append(node('span', 'quota-used', used === null ? '已用未提供' : `已用 ${numberFormat.format(used)}%`));
    section.append(valueRow);

    if (remaining !== null) {
      const progress = node('progress', 'quota-progress');
      progress.max = 100;
      progress.value = remaining;
      progress.setAttribute('aria-label', `${label}剩余 ${numberFormat.format(remaining)}%`);
      section.append(progress);
    } else {
      const track = node('div', 'quota-unavailable');
      track.setAttribute('aria-hidden', 'true');
      section.append(track);
    }

    const reset = validDate(quota.resetsAt, true);
    const details = node('dl', 'reset-details');
    details.append(node('dt', '', '重置时间'));
    const resetValue = node('dd');
    if (reset) {
      const time = node('time', '', dateFormat.format(reset));
      time.dateTime = reset.toISOString();
      resetValue.append(time);
    } else {
      resetValue.textContent = '未提供';
    }
    details.append(resetValue);
    if (reset) {
      const countdown = node('dd', 'reset-countdown');
      details.append(countdown);
      countdowns.push({ element: countdown, resetAt: reset.getTime() });
    }
    section.append(details);
    return section;
  }

  function renderLimits(limits) {
    elements.limits.replaceChildren();
    countdowns = [];
    let windowCount = 0;
    const groups = Array.isArray(limits) ? limits : [];
    for (const limit of groups) {
      const windows = Array.isArray(limit.windows) ? limit.windows : [];
      if (windows.length === 0) continue;
      const group = node('section', 'limit-group');
      if (groups.length > 1 || (limit.name && !/^codex$/i.test(limit.name))) {
        group.append(node('h2', 'limit-group-heading', limit.name || '账户额度'));
      }
      windows.forEach((quota, index) => group.append(renderWindow(quota, index)));
      windowCount += windows.length;
      elements.limits.append(group);
    }
    if (!windowCount) {
      const empty = node('section', 'empty-state');
      empty.append(node('h2', '', '暂未提供额度信息'));
      empty.append(node('p', '', '当前账户没有返回额度窗口，可稍后刷新查看。'));
      elements.limits.append(empty);
    }
    tickCountdowns();
  }

  function extraRow(label, value) {
    const row = node('div', 'extra-row');
    row.append(node('span', 'extra-label', label), node('span', 'extra-value', value));
    return row;
  }

  function renderExtras(data) {
    elements.extras.replaceChildren();
    const groups = Array.isArray(data.limits) ? data.limits : [];
    const seen = new Set();
    for (const group of groups) {
      const credits = group.credits;
      if (!credits) continue;
      const balance = credits.balance === null || credits.balance === undefined ? null : String(credits.balance);
      if (!credits.unlimited && !credits.hasCredits && balance === null) continue;
      const creditValue = credits.unlimited ? '无限额' : balance === null ? '可用 · 余额未提供' : balance;
      const key = JSON.stringify([creditValue, credits.unlimited]);
      if (seen.has(key)) continue;
      seen.add(key);
      const label = groups.length > 1 && group.name ? `${group.name} 额外额度` : '额外额度';
      elements.extras.append(extraRow(label, creditValue));
    }
    if (finite(data.resetCreditsCount)) {
      elements.extras.append(extraRow('可用重置次数', `${exactNumberFormat.format(data.resetCreditsCount)} 次`));
    }
    elements.extras.hidden = elements.extras.childElementCount === 0;
  }

  function bucketDate(date) {
    const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(String(date));
    return match ? `${Number(match[2])}/${Number(match[3])}` : String(date);
  }

  function renderUsage(data) {
    elements.bars.replaceChildren();
    elements.axis.replaceChildren();
    elements.summary.replaceChildren();
    elements.usageNote.hidden = true;
    elements.chart.hidden = true;
    elements.usage.hidden = true;
    const usage = data.usage;
    if (!usage) {
      if (data.usageError) {
        elements.usageNote.textContent = '每日 Token 用量暂不可用，额度信息不受影响。';
        elements.usageNote.hidden = false;
      }
      return;
    }
    const buckets = (Array.isArray(usage.dailyUsageBuckets) ? usage.dailyUsageBuckets : [])
      .filter((bucket) => bucket && finite(bucket.tokens) && bucket.tokens >= 0 && /^\d{4}-\d{2}-\d{2}$/.test(bucket.startDate))
      .sort((a, b) => a.startDate.localeCompare(b.startDate))
      .slice(-14);

    if (buckets.length) {
      const max = Math.max(...buckets.map((bucket) => bucket.tokens), 1);
      const selectBucket = (bucket, selectedButton) => {
        elements.readout.replaceChildren(node('span', '', `${bucketDate(bucket.startDate)}　`), node('strong', '', `${formatTokens(bucket.tokens)} Token`));
        for (const bar of elements.bars.children) bar.setAttribute('aria-pressed', String(bar === selectedButton));
      };
      for (const bucket of buckets) {
        const bar = node('button', 'chart-bar');
        bar.type = 'button';
        bar.setAttribute('aria-label', `${bucket.startDate}，${exactNumberFormat.format(bucket.tokens)} Token`);
        bar.title = `${bucket.startDate} · ${exactNumberFormat.format(bucket.tokens)} Token`;
        bar.setAttribute('aria-pressed', 'false');
        const fill = node('span', 'chart-bar-fill');
        fill.style.height = `${bucket.tokens > 0 ? Math.max(2, bucket.tokens / max * 100) : 0}%`;
        bar.append(fill);
        bar.addEventListener('click', () => selectBucket(bucket, bar));
        bar.addEventListener('focus', () => selectBucket(bucket, bar));
        bar.addEventListener('pointerenter', () => selectBucket(bucket, bar));
        elements.bars.append(bar);
      }
      selectBucket(buckets[buckets.length - 1], elements.bars.lastElementChild);
      elements.axis.append(node('span', '', bucketDate(buckets[0].startDate)));
      if (buckets.length > 1) elements.axis.append(node('span', '', bucketDate(buckets[buckets.length - 1].startDate)));
      elements.chart.hidden = false;
    }

    const summary = usage.summary || {};
    const summaryValues = [
      ['累计 Token', summary.lifetimeTokens, formatTokens],
      ['单日最高', summary.peakDailyTokens, formatTokens],
      ['连续使用', summary.currentStreakDays, (value) => `${exactNumberFormat.format(value)} 天`],
    ];
    for (const [label, value, format] of summaryValues) {
      if (!finite(value)) continue;
      const row = node('div', 'summary-row');
      row.append(node('dt', '', label), node('dd', '', format(value)));
      elements.summary.append(row);
    }
    elements.usage.hidden = !buckets.length && elements.summary.childElementCount === 0;
  }

  function tickCountdowns() {
    const now = Date.now();
    for (const { element, resetAt } of countdowns) {
      const seconds = Math.max(0, Math.ceil((resetAt - now) / 1000));
      if (!seconds) {
        element.textContent = '已到重置时间，等待更新';
        continue;
      }
      const days = Math.floor(seconds / 86400);
      const hours = Math.floor(seconds % 86400 / 3600);
      const minutes = Math.floor(seconds % 3600 / 60);
      const remainder = seconds % 60;
      element.textContent = days > 0
        ? `${days} 天 ${hours} 小时 ${minutes} 分后重置`
        : hours > 0
          ? `${hours} 小时 ${minutes} 分 ${remainder} 秒后重置`
          : `${minutes} 分 ${remainder} 秒后重置`;
    }
  }

  function updateStatus(data, failed = false) {
    const updatedAt = data ? validDate(data.updatedAt) : null;
    const status = failed || (data && data.stale) ? '显示上次数据' : '已更新';
    elements.status.textContent = updatedAt ? `${status} · ${timeFormat.format(updatedAt)}` : failed ? '暂时无法连接' : '已读取账户数据';
  }

  function render(data) {
    renderLimits(data.limits);
    renderExtras(data);
    renderUsage(data);
    setNotice(data.stale ? data.error || '暂时无法更新，以下为上次读取的数据。' : '');
    elements.loading.hidden = true;
    elements.content.hidden = false;
    updateStatus(data);
  }

  function scheduleRefresh() {
    clearTimeout(requestTimer);
    if (!document.hidden) requestTimer = setTimeout(fetchUsage, 60000);
  }

  async function fetchUsage() {
    if (isFetching) return;
    isFetching = true;
    elements.refresh.disabled = true;
    elements.refresh.setAttribute('aria-label', '正在刷新额度');
    elements.refresh.setAttribute('aria-busy', 'true');
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 20000);
    try {
      const response = await fetch('/api/usage', { cache: 'no-store', signal: controller.signal });
      const data = await response.json();
      if (!response.ok || !data || !data.ok) throw new Error(data && data.error || '暂时无法读取账户额度，请稍后刷新。');
      snapshot = data;
      render(data);
    } catch (error) {
      const message = error.name === 'AbortError'
        ? '读取超时，请稍后刷新。'
        : error instanceof TypeError || error instanceof SyntaxError
          ? '暂时无法连接本地服务，请确认额度面板服务已启动后刷新。'
          : error.message || '暂时无法读取账户额度，请稍后刷新。';
      setNotice(snapshot ? `${message} 以下保留上次读取的数据。` : message);
      elements.loading.hidden = true;
      updateStatus(snapshot, true);
    } finally {
      clearTimeout(timeout);
      isFetching = false;
      elements.refresh.disabled = false;
      elements.refresh.setAttribute('aria-label', '刷新额度');
      elements.refresh.removeAttribute('aria-busy');
      scheduleRefresh();
    }
  }

  elements.refresh.addEventListener('click', fetchUsage);
  document.addEventListener('visibilitychange', () => {
    clearTimeout(requestTimer);
    if (!document.hidden) {
      tickCountdowns();
      fetchUsage();
    }
  });
  setInterval(() => { if (!document.hidden) tickCountdowns(); }, 1000);
  fetchUsage();
})();
