const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const skin = require('../src/skin.js');

class Element {
  constructor(attrs = {}, tag = 'DIV') { this.attrs = { ...attrs }; this.style = {}; this.tagName = tag; this.isConnected = true; this.children = []; this.listeners = new Map(); this.visible = true; }
  getAttribute(k) { return this.attrs[k] ?? null; }
  setAttribute(k, v) { this.attrs[k] = String(v); }
  removeAttribute(k) { delete this.attrs[k]; }
  get src() { return this.attrs.src || ''; }
  set src(value) { this.attrs.src = String(value); }
  getClientRects() { return this.visible ? [{}] : []; }
  appendChild(e) { this.children.push(e); e.parent = this; return e; }
  contains(e) { return this === e || this.children.some(c => c.contains(e)); }
  remove() { if (this.parent) this.parent.children = this.parent.children.filter(e => e !== this); this.isConnected = false; }
  addEventListener(k, fn) { this.listeners.set(k, fn); }
  removeEventListener(k, fn) { if (this.listeners.get(k) === fn) this.listeners.delete(k); }
}
const trigger = effort => new Element({ 'data-codex-intelligence-trigger': '', 'data-selected-reasoning-effort': effort });
function fixture(native = [], sliders = []) {
  const doc = new Element(); doc.body = new Element(); doc.head = new Element(); doc.documentElement = new Element();
  doc.querySelector = selector => selector === '#root' ? {} : null;
  doc.querySelectorAll = selector => selector.includes('data-codex-intelligence-trigger') ? native : sliders;
  doc.createElement = tag => new Element({}, tag.toUpperCase());
  const frames = new Map(), intervals = new Map(); let seq = 0;
  const win = new Element(); Object.assign(win, {
    document: doc, location: { pathname: '/threads/test', search: '' },
    requestAnimationFrame(fn) { frames.set(++seq, fn); return seq; },
    cancelAnimationFrame(id) { frames.delete(id); },
    setInterval(fn) { intervals.set(++seq, fn); return seq; }, clearInterval(id) { intervals.delete(id); },
    MutationObserver: class { constructor(fn) { this.fn = fn; } observe() { } disconnect() { this.disconnected = true; } }
  });
  return { win, doc, intervals, flush() { const callbacks = [...frames.values()]; frames.clear(); callbacks.forEach(fn => fn()); } };
}
const assets = Array(5).fill('data:image/png;base64,AA==');
const flushPromises = () => new Promise(resolve => setImmediate(resolve));

test('stop mapping handles alternate ranges and rejects invalid or single-choice ranges', () => {
  assert.deepEqual([1, 2, 3].map(v => skin.mapStop(v, 1, 3)), [0, 2, 4]);
  for (const values of [[NaN, 0, 5], [1, 1, 1], [-1, 0, 4], [5, 0, 4], [0, 4, 0]]) assert.equal(skin.mapStop(...values), null);
});
test('actual native effort attributes take precedence over unrelated ranges', () => {
  const f = fixture([trigger('ultra')], [new Element({ 'aria-label': 'Volume', 'aria-valuetext': 'High' })]);
  assert.deepEqual(skin.read(f.doc), { status: 'confirmed', stage: 4, source: 'native-effort-attribute' });
  for (const [effort, stage] of [['low', 0], ['medium', 1], ['high', 2], ['xhigh', 3], ['max', 4]]) {
    f.doc.querySelectorAll = selector => selector.includes('data-codex-intelligence-trigger') ? [trigger(effort)] : [];
    assert.equal(skin.read(f.doc).stage, stage);
  }
});
test('unknown, pending, hidden and ambiguous selections do not become maximum', () => {
  assert.equal(skin.read(fixture([trigger('new-unknown-effort')]).doc).stage, null);
  const pending = trigger('ultra'); pending.setAttribute('aria-busy', 'true');
  assert.equal(skin.read(fixture([pending]).doc).status, 'pending');
  assert.equal(skin.read(fixture([trigger('low'), trigger('ultra')]).doc).source, 'ambiguous');
  const hidden = trigger('ultra'); hidden.visible = false;
  assert.equal(skin.read(fixture([hidden]).doc).stage, null);
  const disabled = trigger('high'); disabled.disabled = true;
  assert.equal(skin.read(fixture([disabled]).doc).stage, 2); // Running tasks can disable a picker without invalidating its current selection.
});
test('fallback accepts explicitly labelled effort ranges, not volume controls', () => {
  const slider = new Element({ 'aria-label': '推理强度', 'aria-valuetext': '高', 'aria-valuenow': '3', 'aria-valuemin': '1', 'aria-valuemax': '3' });
  assert.equal(skin.read(fixture([], [slider]).doc).stage, 4);
  slider.setAttribute('aria-label', 'Volume'); slider.setAttribute('aria-valuetext', 'High');
  assert.equal(skin.read(fixture([], [slider]).doc).stage, null);
});
test('installation follows changes, never writes original theme and fully disposes', () => {
  const node = trigger('ultra'), f = fixture([node]);
  f.doc.documentElement.setAttribute('data-theme', 'light');
  assert.equal(skin.install(f.win, assets, '').stage, 4);
  assert.equal(f.doc.documentElement.getAttribute('data-dragon-skin'), 'crystal');
  assert.equal(f.doc.documentElement.getAttribute('data-theme'), 'light');
  skin.install(f.win, assets, ''); assert.equal(f.doc.body.children.length, 1);
  node.setAttribute('data-selected-reasoning-effort', 'low');
  [...f.intervals.values()][0](); f.flush();
  assert.equal(f.win.__dragonSkinV1.status().stage, 0);
  assert.equal(f.doc.documentElement.getAttribute('data-dragon-skin'), 'normal');
  node.setAttribute('data-selected-reasoning-effort', 'unsupported');
  [...f.intervals.values()][0](); f.flush();
  assert.equal(f.doc.documentElement.getAttribute('data-dragon-skin'), null);
  const api = f.win.__dragonSkinV1; api.dispose(); api.dispose();
  assert.equal(f.doc.body.children.length, 0); assert.equal(f.doc.head.children.length, 0);
  assert.equal(f.doc.listeners.size, 0); assert.equal(f.win.listeners.size, 0); assert.equal(f.intervals.size, 0);
  assert.equal(f.win.__dragonSkinV1, undefined); assert.equal(f.doc.documentElement.getAttribute('data-theme'), 'light');
});
test('asset boundary rejects external URLs and incomplete sets', () => {
  assert.throws(() => skin.install(fixture().win, ['https://example.com/image.png'], ''), /invalid-assets/);
  assert.throws(() => skin.install(fixture().win, [...assets.slice(0, 4), 'data:image/png;base64,invalid!'], ''), /invalid-assets/);
});
test('art visibility follows actual surface colors across live theme changes without changing the host palette', () => {
  const f = fixture([trigger('ultra')]);
  let pixels = [248, 248, 250, 255];
  f.win.getComputedStyle = () => ({ backgroundColor: `rgb(${pixels.slice(0, 3).join(',')})` });
  const create = f.doc.createElement;
  f.doc.createElement = tag => {
    const node = create(tag);
    if (tag === 'canvas') node.getContext = () => ({ clearRect() {}, fillRect() {}, getImageData: () => ({ data: pixels }) });
    return node;
  };
  // The theme name stays unchanged, as with a custom host palette.
  f.doc.documentElement.setAttribute('data-theme', 'custom');
  skin.install(f.win, assets, '');
  const layer = f.doc.body.children[0];
  assert.equal(layer.getAttribute('data-tone'), 'light');
  pixels = [20, 20, 24, 255]; [...f.intervals.values()][0](); f.flush();
  assert.equal(layer.getAttribute('data-tone'), 'dark');
  pixels = [245, 235, 213, 255]; [...f.intervals.values()][0](); f.flush();
  assert.equal(layer.getAttribute('data-tone'), 'light');
  assert.equal(f.doc.documentElement.getAttribute('data-theme'), 'custom');
  assert.equal(f.win.__dragonSkinV1.status().stage, 4);
  f.win.__dragonSkinV1.dispose();
  assert.equal(f.doc.body.children.length, 0);
});
test('native dragging previews before release and never changes committed effort', () => {
  const committed = trigger('low'), f = fixture([committed]);
  const thumb = new Element({ 'aria-valuenow': '0', 'aria-valuemin': '0', 'aria-valuemax': '5' });
  const power = new Element(); power.matches = () => false; power.querySelectorAll = () => [thumb];
  thumb.closest = selector => selector.includes('data-model-picker-power-slider') ? power : null;
  skin.install(f.win, assets, '');
  f.doc.listeners.get('pointerdown')({ target: thumb }); f.flush();
  thumb.setAttribute('aria-valuenow', '5'); f.doc.listeners.get('pointermove')({ target: thumb }); f.flush();
  assert.equal(f.win.__dragonSkinV1.status().status, 'preview');
  assert.equal(f.win.__dragonSkinV1.status().stage, 4);
  assert.equal(f.doc.documentElement.getAttribute('data-dragon-skin'), 'crystal');
  assert.equal(committed.getAttribute('data-selected-reasoning-effort'), 'low');
  f.doc.listeners.get('pointerup')(); f.flush();
  assert.equal(f.win.__dragonSkinV1.status().stage, 0);
  assert.equal(f.doc.documentElement.getAttribute('data-dragon-skin'), 'normal');
  f.win.__dragonSkinV1.dispose();
});
test('paint diagnostic follows decoded images and two frame opportunities; disposal cancels acknowledgement', async () => {
  const f = fixture([trigger('ultra')]), decoders = [];
  const create = f.doc.createElement;
  f.doc.createElement = tag => {
    const node = create(tag);
    if (tag === 'img') node.decode = () => new Promise(resolve => decoders.push(resolve));
    return node;
  };
  skin.install(f.win, assets, '');
  const api = f.win.__dragonSkinV1;
  assert.equal(api.status().paintReady, false);
  assert.equal(api.status().assetsDecoded, false);
  decoders.slice(0, 4).forEach(resolve => resolve()); await flushPromises(); f.flush();
  assert.equal(api.status().paintReady, false, 'four decoded assets cannot release the cover');
  decoders[4](); await flushPromises();
  assert.equal(api.status().assetsDecoded, true, 'occluded preparation does not depend on rAF');
  f.flush(); assert.equal(api.status().paintReady, false, 'one frame is not a presentation acknowledgement');
  f.flush(); assert.equal(api.status().paintReady, true);
  api.dispose();
  skin.install(f.win, assets, ''); const replacement = f.win.__dragonSkinV1;
  decoders.slice(5).forEach(resolve => resolve()); await flushPromises();
  f.flush(); replacement.dispose(); f.flush();
  assert.equal(replacement.status().paintReady, false);
});
test('prewarm decodes five detached images before shell mount and install reuses the same images and promise', async () => {
  const f = fixture([trigger('ultra')]), body = f.doc.body, head = f.doc.head, created = [], decoders = [];
  const create = f.doc.createElement;
  f.doc.body = null; f.doc.querySelector = () => null;
  f.doc.createElement = tag => {
    const node = create(tag);
    if (tag === 'img') { created.push(node); node.decode = () => new Promise(resolve => decoders.push(resolve)); }
    else assert.ok(f.doc.body, 'prewarm may create only detached image nodes');
    return node;
  };
  assert.deepEqual(skin.prewarm(f.win, assets), { status: 'warming', assetsDecoded: false });
  const warm = f.win.__dragonSkinWarmV1;
  assert.equal(created.length, 5); assert.equal(decoders.length, 5);
  assert.ok(created.every(img => !img.parent && img.alt === '' && img.draggable === false));
  assert.equal(body.children.length, 0); assert.equal(head.children.length, 0); assert.equal(f.intervals.size, 0);
  assert.deepEqual(f.doc.documentElement.attrs, {});
  assert.equal(skin.install(f.win, assets, '').status, 'waiting-for-shell');
  skin.prewarm(f.win, [...assets]); assert.equal(f.win.__dragonSkinWarmV1, warm); assert.equal(decoders.length, 5);
  decoders.forEach(resolve => resolve()); await flushPromises();
  assert.deepEqual(skin.prewarm(f.win, assets), { status: 'decoded', assetsDecoded: true });
  f.doc.body = body; f.doc.querySelector = selector => selector === '#root' ? {} : null;
  assert.equal(skin.install(f.win, assets, '').assetsDecoded, true);
  assert.deepEqual(body.children[0].children.slice(0, 5), created);
  assert.equal(decoders.length, 5); assert.equal(created.length, 5);
  assert.equal(f.win.__dragonSkinWarmV1, undefined);
  await flushPromises(); f.flush(); f.flush();
  assert.equal(f.win.__dragonSkinV1.status().paintReady, true);
  f.win.__dragonSkinV1.dispose();
  assert.ok(created.every(img => img.src === ''), 'restoration also releases the warmed image sources');
  assert.equal(body.children.length, 0); assert.equal(head.children.length, 0);
});
test('prewarm validation, pending disposal and rejected decode never leak transient state or unhandled rejection', async () => {
  const f = fixture(), images = [], rejects = [];
  const create = f.doc.createElement;
  f.doc.createElement = tag => {
    const node = create(tag);
    if (tag === 'img') { images.push(node); node.decode = () => new Promise((_, reject) => rejects.push(reject)); }
    return node;
  };
  assert.throws(() => skin.prewarm(f.win, ['https://example.com/hero.png']), /invalid-assets/);
  assert.throws(() => skin.prewarm(f.win, [...assets.slice(0, 4), 'data:image/png;base64,invalid!']), /invalid-assets/);
  assert.equal(images.length, 0); assert.equal(f.win.__dragonSkinWarmV1, undefined);
  skin.prewarm(f.win, assets); const warm = f.win.__dragonSkinWarmV1;
  warm.dispose(); warm.dispose();
  assert.equal(f.win.__dragonSkinWarmV1, undefined); assert.ok(images.every(img => img.src === ''));
  rejects.forEach(reject => reject(new Error('cancelled'))); await flushPromises();
  assert.equal(await warm.promise, false); assert.equal(warm.decoded, false);
  skin.prewarm(f.win, assets); const failed = f.win.__dragonSkinWarmV1;
  rejects.slice(5).forEach(reject => reject(new Error('bad image'))); await flushPromises();
  assert.deepEqual(skin.prewarm(f.win, assets), { status: 'failed', assetsDecoded: false });
  assert.equal(await failed.promise, false);
  failed.dispose(); assert.equal(f.win.__dragonSkinWarmV1, undefined);
});

test('decoded startup portrait installs before native root without claiming effort or Ultra styling', async () => {
  const f = fixture(), images = []; let decodes = 0;
  const create = f.doc.createElement;
  f.doc.querySelector = () => { throw new Error('native root is not a preinstallation requirement'); };
  f.doc.documentElement.setAttribute('data-theme', 'custom');
  f.doc.createElement = tag => {
    const node = create(tag);
    if (tag === 'img') { images.push(node); node.decode = () => { decodes++; return Promise.resolve(); }; }
    return node;
  };
  skin.prewarm(f.win, assets); await flushPromises();
  const state = skin.install(f.win, assets, '', 4), layer = f.doc.body.children[0];
  assert.equal(state.installed, true); assert.equal(state.assetsDecoded, true);
  assert.equal(state.status, 'unknown'); assert.equal(state.stage, null); assert.equal(state.source, 'not-found');
  assert.equal(state.visualStage, 4); assert.equal(state.provisional, true);
  assert.equal(f.doc.documentElement.getAttribute('data-dragon-skin'), 'normal');
  assert.equal(f.doc.documentElement.getAttribute('data-theme'), 'custom');
  assert.equal(layer.getAttribute('data-stage'), '', 'Ultra stars need a real native selection');
  assert.deepEqual(layer.children.slice(0, 5), images);
  assert.deepEqual(images.map(img => img.getAttribute('data-visible')), ['false', 'false', 'false', 'false', 'true']);
  assert.equal(decodes, 5); assert.equal(images.length, 5, 'preinstallation reuses predecoded nodes');
  const api = f.win.__dragonSkinV1; api.dispose();
  assert.equal(api.status().visualStage, null); assert.equal(api.status().provisional, false);
  assert.equal(f.doc.body.children.length, 0); assert.equal(f.doc.head.children.length, 0);
  assert.ok(images.every(img => img.src === ''));
});

test('native confirmation overrides startup decoration and permanently retires the fallback', () => {
  const native = [], f = fixture(native);
  skin.install(f.win, assets, '', 4);
  const refresh = () => { [...f.intervals.values()][0](); f.flush(); return f.win.__dragonSkinV1.status(); };
  native.push(trigger('high'));
  let state = refresh();
  assert.equal(state.stage, 2); assert.equal(state.visualStage, 2); assert.equal(state.provisional, false);
  assert.equal(f.doc.documentElement.getAttribute('data-dragon-skin'), 'normal');
  native.length = 0; state = refresh();
  assert.equal(state.status, 'unknown'); assert.equal(state.visualStage, null); assert.equal(state.provisional, false);
  assert.equal(f.doc.documentElement.getAttribute('data-dragon-skin'), null);
  native.push(trigger('ultra')); state = refresh();
  assert.equal(state.stage, 4); assert.equal(state.provisional, false);
  assert.equal(f.doc.documentElement.getAttribute('data-dragon-skin'), 'crystal');
  f.win.__dragonSkinV1.dispose();
});

test('initial pending controls keep provisional art without claiming effort; later pending cannot revive it', () => {
  const pending = trigger('ultra'); pending.setAttribute('aria-busy', 'true');
  const f = fixture([pending]), initial = skin.install(f.win, assets, '', 4);
  assert.equal(initial.status, 'pending'); assert.equal(initial.stage, null);
  assert.equal(initial.visualStage, 4); assert.equal(initial.provisional, true);
  assert.equal(f.doc.documentElement.getAttribute('data-dragon-skin'), 'normal');
  assert.equal(f.doc.body.children[0].getAttribute('data-stage'), '', 'pending native control does not activate Ultra stars');
  pending.removeAttribute('aria-busy'); pending.setAttribute('data-selected-reasoning-effort', 'medium');
  [...f.intervals.values()][0](); f.flush();
  assert.equal(f.win.__dragonSkinV1.status().stage, 1);
  assert.equal(f.win.__dragonSkinV1.status().provisional, false);
  pending.setAttribute('aria-busy', 'true'); [...f.intervals.values()][0](); f.flush();
  const later = f.win.__dragonSkinV1.status();
  assert.equal(later.status, 'pending'); assert.equal(later.stage, null);
  assert.equal(later.visualStage, null); assert.equal(later.provisional, false);
  assert.equal(f.doc.documentElement.getAttribute('data-dragon-skin'), null);
  f.win.__dragonSkinV1.dispose();
});

test('startup decoration does not override unsupported or ambiguous native selections', () => {
  for (const native of [[trigger('unsupported')], [trigger('low'), trigger('ultra')]]) {
    const f = fixture(native), state = skin.install(f.win, assets, '', 4);
    assert.equal(state.stage, null); assert.equal(state.visualStage, null); assert.equal(state.provisional, false);
    assert.equal(f.doc.documentElement.getAttribute('data-dragon-skin'), null);
    assert.ok(f.doc.body.children[0].children.slice(0, 5).every(img => img.getAttribute('data-visible') === 'false'));
    f.win.__dragonSkinV1.dispose();
  }
  const neutral = fixture();
  assert.equal(skin.install(neutral.win, assets, '').visualStage, null, 'omitting startupStage keeps the original neutral behavior');
  neutral.win.__dragonSkinV1.dispose();
  for (const invalid of [-1, 5, 1.5, '4', NaN, {}]) {
    assert.throws(() => skin.install(fixture().win, assets, '', invalid), /invalid-startup-stage/);
  }
});

test('boot navigation preserves provisional art until first native confirmation, then cannot revive it', () => {
  const native = [], f = fixture(native); skin.install(f.win, assets, '', 1);
  assert.equal(f.win.__dragonSkinV1.status().visualStage, 1);
  f.win.location.pathname += '-next'; [...f.intervals.values()][0](); f.flush();
  assert.equal(f.win.__dragonSkinV1.status().visualStage, 1);
  assert.equal(f.win.__dragonSkinV1.status().provisional, true);
  assert.equal(f.win.__dragonSkinV1.status().stage, null);
  native.push(trigger('high')); [...f.intervals.values()][0](); f.flush();
  assert.equal(f.win.__dragonSkinV1.status().stage, 2);
  assert.equal(f.win.__dragonSkinV1.status().provisional, false);
  native.length = 0; f.win.location.pathname += '-another'; [...f.intervals.values()][0](); f.flush();
  assert.equal(f.win.__dragonSkinV1.status().visualStage, null);
  assert.equal(f.win.__dragonSkinV1.status().provisional, false);
  assert.equal(f.doc.documentElement.getAttribute('data-dragon-skin'), null);
  f.win.__dragonSkinV1.dispose();
});
test('Escape, focus loss, route change and pointer cancellation end visual previews', () => {
  const committed = trigger('medium'), f = fixture([committed]);
  const thumb = new Element({ 'aria-valuenow': '5', 'aria-valuemin': '0', 'aria-valuemax': '5' });
  const power = new Element(); power.matches = () => false; power.querySelectorAll = () => [thumb];
  thumb.closest = () => power;
  skin.install(f.win, assets, '');
  for (const cancel of [() => f.doc.listeners.get('keydown')({ key: 'Escape' }), () => f.win.listeners.get('blur')(), () => f.doc.listeners.get('pointercancel')(), () => { f.win.location.pathname += 'a'; [...f.intervals.values()][0](); }]) {
    f.doc.listeners.get('pointerdown')({ target: thumb }); f.flush(); assert.equal(f.win.__dragonSkinV1.status().stage, 4);
    cancel(); f.flush(); assert.equal(f.win.__dragonSkinV1.status().stage, 1);
  }
  f.win.__dragonSkinV1.dispose();
});
test('theme does not override semantic danger, success or warning colors', () => {
  const css = fs.readFileSync(require.resolve('../src/skin.css'), 'utf8');
  assert.doesNotMatch(css, /--color-[a-z-]*(?:danger|warning|success)[a-z-]*\s*:/);
  assert.match(css, /prefers-reduced-motion/);
  assert.match(css, /pointer-events:none/);
});
test('Ultra accents stay local and preserve native sidebar text and surfaces', () => {
  const css = fs.readFileSync(require.resolve('../src/skin.css'), 'utf8');
  assert.doesNotMatch(css, /html\[data-dragon-skin="crystal"\]\s*(?:body\s*)?\{/);
  assert.doesNotMatch(css, /--color-text-(?:default|secondary)\s*:/);
  assert.doesNotMatch(css, /\[data-app-shell-(?:frame|workspace-row)\][^{]*\{[^}]*background-color:transparent/);
  assert.match(css, /\[data-app-shell-main-surface\],\[data-app-shell-titlebar-slot\],\[data-codex-composer-request-navigation\]/);
  assert.doesNotMatch(css, /--color-[\w-]+\s*:/, 'native palette tokens remain owned by the host in every theme');
  assert.doesNotMatch(css, /background(?:-color)?\s*:\s*#[\da-f]{3,6}\s*[!;]/i, 'no opaque hardcoded theme backgrounds');
});
test('light-surface stars remain distinguishable on white and visible rules stay Ultra-only', () => {
  const css = fs.readFileSync(require.resolve('../src/skin.css'), 'utf8');
  const rules = [...css.matchAll(/([^{}]+)\{([^{}]+)\}/g)].filter(([, selector]) => selector.includes('.dragon-stars'));
  for (const [, selector, declarations] of rules) {
    const opacity = declarations.match(/opacity\s*:\s*([.\d]+)/);
    if (opacity && Number(opacity[1]) > 0) assert.match(selector, /\[data-stage="4"\]/, 'only Ultra may make stars visible');
  }
  const light = rules.find(([, selector]) => selector.includes('[data-tone="light"]') && /\.dragon-stars\s*$/.test(selector));
  const ink = rules.find(([, selector]) => selector.includes('[data-tone="light"]') && /\.dragon-stars span\s*$/.test(selector));
  assert.ok(light && ink, 'light surface has its own local star treatment');
  const opacity = Number(light[2].match(/opacity\s*:\s*([.\d]+)/)[1]);
  const hex = ink[2].match(/color\s*:\s*#([\da-f]{6})/i)[1];
  const channels = [0, 2, 4].map(offset => (255 * (1 - opacity) + parseInt(hex.slice(offset, offset + 2), 16) * opacity) / 255);
  const linear = channels.map(c => c <= .04045 ? c / 12.92 : ((c + .055) / 1.055) ** 2.4);
  const luminance = .2126 * linear[0] + .7152 * linear[1] + .0722 * linear[2];
  assert.ok(1.05 / (luminance + .05) >= 2, 'decorative stars retain at least 2:1 contrast against white');
});
