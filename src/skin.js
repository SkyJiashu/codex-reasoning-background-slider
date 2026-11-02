(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.DragonSkin = api;
})(typeof globalThis === 'object' ? globalThis : this, function () {
  'use strict';
  const KEY = '__dragonSkinV1';
  const WARM_KEY = '__dragonSkinWarmV1';
  const ROOT = 'dragon-skin-v1';
  const SELECTOR = 'input[type="range"], [role="slider"]';
  const LABEL = /reason|effort|thinking|推理|思考/i;
  const NATIVE = '[data-codex-intelligence-trigger][data-selected-reasoning-effort]';
  const POWER = '[data-model-picker-power-slider], [data-reasoning-slider]';
  const EFFORTS = { none: 0, minimal: 0, low: 0, medium: 1, high: 2, xhigh: 3, max: 4, ultra: 4 };

  function mapStop(value, min, max) {
    if (![value, min, max].every(Number.isFinite) || max <= min || value < min || value > max) return null;
    return Math.min(4, Math.round((value - min) / (max - min) * 4));
  }
  function labelStage(text) {
    if (/\bultra\b|\bmaximum\b|\bmax\b|极致|最高|超强/i.test(text)) return 4;
    if (/\bxhigh\b|extra\s*high|very\s*high|超高/i.test(text)) return 3;
    if (/\bhigh\b|高(?:强度|推理|级)?/i.test(text)) return 2;
    if (/\bmedium\b|中(?:等|强度|推理|级)?/i.test(text)) return 1;
    if (/\blow\b|\bminimal\b|\blight\b|低|轻量/i.test(text)) return 0;
    return null;
  }
  function snapshot(node) {
    const attr = name => node.getAttribute(name) || '';
    const label = [attr('aria-label'), attr('aria-valuetext'), attr('title'), attr('data-testid')].join(' ');
    const nativePower = !!node.closest?.(POWER);
    if ((!LABEL.test(label) && !nativePower) || !node.isConnected || node.getClientRects().length === 0) return null;
    const namedStage = labelStage(label);
    // Only explicitly identified native model/effort controls may drive a theme.
    const raw = attr('aria-valuenow') || (node.tagName === 'INPUT' ? node.value : '');
    const lo = attr('aria-valuemin') || (node.tagName === 'INPUT' ? node.min : '');
    const hi = attr('aria-valuemax') || (node.tagName === 'INPUT' ? node.max : '');
    const position = raw !== '' && lo !== '' && hi !== '' ? mapStop(Number(raw), Number(lo), Number(hi)) : null;
    if (namedStage === null && (!nativePower || position === null)) return null;
    const disabled = node.disabled || attr('aria-disabled') === 'true' || attr('aria-busy') === 'true';
    if (disabled) return { status: 'pending', stage: null, source: 'native-control' };
    return { status: 'confirmed', stage: position === null ? namedStage : position, source: 'native-control' };
  }
  function read(doc) {
    const native = Array.from(doc.querySelectorAll(NATIVE)).filter(n => n.isConnected && n.getClientRects().length > 0);
    if (native.length > 1) return { status: 'unknown', stage: null, source: 'ambiguous' };
    if (native.length === 1) {
      const node = native[0], effort = node.getAttribute('data-selected-reasoning-effort');
      if (node.getAttribute('aria-busy') === 'true' || /pending|loading/.test(node.getAttribute('data-state') || '')) return { status: 'pending', stage: null, source: 'native-effort-attribute' };
      if (!Object.prototype.hasOwnProperty.call(EFFORTS, effort)) return { status: 'unknown', stage: null, source: 'unsupported-effort' };
      return { status: 'confirmed', stage: EFFORTS[effort], source: 'native-effort-attribute' };
    }
    const candidates = Array.from(doc.querySelectorAll(SELECTOR)).slice(0, 40).map(snapshot).filter(Boolean);
    // Ambiguous controls (including multiple visible composers) never select a highest theme.
    if (candidates.length !== 1) return { status: 'unknown', stage: null, source: candidates.length ? 'ambiguous' : 'not-found' };
    return candidates[0];
  }
  function validateAssets(assets) {
    if (!Array.isArray(assets) || assets.length !== 5 || assets.some(x => typeof x !== 'string' || !/^data:image\/png;base64,[A-Za-z0-9+/=]+$/.test(x))) throw new Error('invalid-assets');
  }
  function artwork(win, assets) {
    const existing = win[WARM_KEY];
    if (existing && existing.matchesAssets(assets)) return existing;
    if (existing) existing.dispose();
    const sourceUrls = assets.slice();
    const images = sourceUrls.map(src => {
      const img = win.document.createElement('img'); img.src = src; img.alt = ''; img.draggable = false;
      return img;
    });
    let disposed = false;
    const warm = {
      images, decoded: false, status: 'warming', promise: null,
      matchesAssets: candidate => !disposed && sourceUrls.every((src, i) => src === candidate[i]),
      dispose() {
        if (disposed) return;
        disposed = true; warm.decoded = false; warm.status = 'disposed';
        images.forEach(img => img.removeAttribute('src'));
        if (win[WARM_KEY] === warm) delete win[WARM_KEY];
      }
    };
    // Handle rejection immediately, even when React/body has not mounted yet.
    // The shared promise resolves a boolean; it never leaves an unhandled decode rejection.
    warm.promise = Promise.all(images.map(img => {
      try { return typeof img.decode === 'function' ? img.decode() : Promise.reject(new Error('image-decode-unavailable')); }
      catch (_) { return Promise.reject(new Error('image-decode-failed')); }
    })).then(() => {
      if (disposed) return false;
      warm.decoded = true; warm.status = 'decoded'; return true;
    }, () => { if (!disposed) warm.status = 'failed'; return false; });
    win[WARM_KEY] = warm;
    return warm;
  }
  function prewarm(win, assets) {
    validateAssets(assets);
    if (win[KEY]) return { status: 'installed', assetsDecoded: win[KEY].status().assetsDecoded };
    const warm = artwork(win, assets);
    return { status: warm.status, assetsDecoded: warm.decoded };
  }
  function install(win, assets, css, startupStage = null) {
    validateAssets(assets);
    if (typeof css !== 'string' || css.length > 60000) throw new Error('invalid-css');
    if (startupStage !== null && (!Number.isInteger(startupStage) || startupStage < 0 || startupStage > 4)) throw new Error('invalid-startup-stage');
    if (win[KEY]) return win[KEY].status();
    const warm = artwork(win, assets);
    const doc = win.document;
    if (!doc.head || !doc.body) return { status: 'waiting-for-shell', assetsDecoded: warm.decoded };
    const style = doc.createElement('style'); style.id = ROOT + '-style'; style.textContent = css;
    const layer = doc.createElement('div'); layer.id = ROOT; layer.setAttribute('aria-hidden', 'true');
    const images = warm.images;
    images.forEach(img => layer.appendChild(img));
    const stars = doc.createElement('div'); stars.className = 'dragon-stars';
    for (let i = 0; i < 6; i++) { const star = doc.createElement('span'); star.textContent = '✧'; stars.appendChild(star); }
    layer.appendChild(stars);
    // Resolve the host's actual surface (including custom palettes), not the OS theme name.
    const surface = doc.createElement('span');
    surface.style.cssText = 'position:absolute;visibility:hidden;width:0;height:0;background:var(--color-surface,Canvas)';
    layer.appendChild(surface);
    const canvas = doc.createElement('canvas'); canvas.width = canvas.height = 1;
    const context = canvas.getContext?.('2d');
    let sampledColor = '';
    function updateTone() {
      if (!context || !win.getComputedStyle) return;
      const color = win.getComputedStyle(surface).backgroundColor;
      if (color === sampledColor) return;
      context.clearRect(0, 0, 1, 1); context.fillStyle = color; context.fillRect(0, 0, 1, 1);
      const [r, g, b, a] = context.getImageData(0, 0, 1, 1).data;
      if (a !== 255) return; // An unresolved/transparent surface keeps the existing dark appearance.
      sampledColor = color;
      layer.setAttribute('data-tone', .2126 * r + .7152 * g + .0722 * b > 160 ? 'light' : 'dark');
    }
    doc.head.appendChild(style); doc.body.appendChild(layer);
    let last = { status: 'unknown', stage: null, source: 'not-found' };
    let startupActive = startupStage !== null, visualStage = null, provisional = false;
    let disposed = false, scheduled = 0, paintFrame = 0, assetsDecoded = warm.decoded, paintReady = false, dragRoot = null, route = win.location.pathname + win.location.search;
    warm.promise.then(decoded => {
      if (disposed || !decoded) return;
      assetsDecoded = true;
      paintFrame = win.requestAnimationFrame(() => {
        if (disposed) return;
        paintFrame = win.requestAnimationFrame(() => { paintFrame = 0; if (!disposed) paintReady = true; });
      });
    }).catch(() => { /* A failed decode never becomes a prepared image. */ });
    const onPointerDown = event => {
      dragRoot = event.target.closest?.(POWER) || event.target.closest?.(SELECTOR) || null;
      if (dragRoot) schedule();
    };
    function update() {
      scheduled = 0;
      if (disposed) return;
      updateTone();
      const nextRoute = win.location.pathname + win.location.search;
      if (nextRoute !== route) { route = nextRoute; dragRoot = null; }
      const committed = read(doc);
      if (committed.status === 'confirmed') startupActive = false;
      let preview = null;
      if (dragRoot?.isConnected) {
        const nodes = dragRoot.matches?.(SELECTOR) ? [dragRoot] : Array.from(dragRoot.querySelectorAll(SELECTOR));
        const candidates = nodes.map(snapshot).filter(s => s?.status === 'confirmed');
        if (candidates.length === 1) preview = { ...candidates[0], status: 'preview', source: 'native-drag-preview' };
      }
      last = preview || committed;
      const stage = last.status === 'confirmed' || last.status === 'preview' ? last.stage : null;
      // A startup portrait provides continuity before native controls mount. It is
      // decoration only: no inferred effort, Ultra palette, or Ultra stars.
      provisional = startupActive && (last.status === 'pending' || (last.status === 'unknown' && last.source === 'not-found'));
      visualStage = provisional ? startupStage : stage;
      if (visualStage === null) doc.documentElement.removeAttribute('data-dragon-skin');
      else doc.documentElement.setAttribute('data-dragon-skin', !provisional && stage === 4 ? 'crystal' : 'normal');
      images.forEach((img, i) => img.setAttribute('data-visible', String(visualStage === i)));
      layer.setAttribute('data-confirmed', String(last.status === 'confirmed'));
      layer.setAttribute('data-preview', String(last.status === 'preview'));
      layer.setAttribute('data-provisional', String(provisional));
      layer.setAttribute('data-stage', stage === null ? '' : String(stage));
    }
    const schedule = () => { if (!disposed && !scheduled) scheduled = win.requestAnimationFrame(update); };
    const release = () => { dragRoot = null; schedule(); };
    const move = () => { if (dragRoot) schedule(); };
    const key = event => { if (event.key === 'Escape') release(); else schedule(); };
    const observer = new win.MutationObserver(records => {
      if (records.some(r => r.target !== layer && !layer.contains(r.target) && r.target !== style && !(r.target === doc.documentElement && r.attributeName === 'data-dragon-skin'))) schedule();
    });
    observer.observe(doc.body, { subtree: true, childList: true, attributes: true, attributeFilter: ['data-selected-reasoning-effort', 'data-state', 'aria-label', 'aria-valuetext', 'aria-valuenow', 'aria-valuemin', 'aria-valuemax', 'aria-disabled', 'aria-busy', 'value', 'hidden'] });
    const themeObserver = new win.MutationObserver(schedule);
    for (const node of [doc.documentElement, doc.body]) themeObserver.observe(node, { attributes: true, attributeFilter: ['class', 'style', 'data-theme', 'data-color-mode'] });
    const listeners = [['input', schedule], ['change', schedule], ['pointerdown', onPointerDown], ['pointermove', move], ['pointerup', release], ['pointercancel', release], ['keydown', key], ['focusin', schedule], ['click', schedule]];
    listeners.forEach(([name, fn]) => doc.addEventListener(name, fn, true));
    win.addEventListener('popstate', release); win.addEventListener('hashchange', release);
    win.addEventListener('blur', release);
    // A low-frequency route check covers client-side pushState without patching the host.
    const timer = win.setInterval(schedule, 1000);
    const api = {
      status: () => ({ installed: !disposed, assetsDecoded, paintReady, status: last.status, stage: last.stage, source: last.source, visualStage, provisional }),
      dispose() {
        if (disposed) return; disposed = true; observer.disconnect(); themeObserver.disconnect(); win.clearInterval(timer);
        startupActive = false; visualStage = null; provisional = false;
        if (scheduled) win.cancelAnimationFrame(scheduled);
        if (paintFrame) win.cancelAnimationFrame(paintFrame);
        listeners.forEach(([name, fn]) => doc.removeEventListener(name, fn, true));
        win.removeEventListener('popstate', release); win.removeEventListener('hashchange', release);
        win.removeEventListener('blur', release);
        doc.documentElement.removeAttribute('data-dragon-skin'); layer.remove(); style.remove(); warm.dispose(); delete win[KEY];
      }
    };
    win[KEY] = api; update();
    if (win[WARM_KEY] === warm) delete win[WARM_KEY];
    return api.status();
  }
  return { mapStop, labelStage, snapshot, read, prewarm, install };
});
