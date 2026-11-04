const { test } = require('node:test');
const assert = require('node:assert/strict');
const preload = require('../src/preload.js');

function matches(node, selector) {
  return selector.split(',').some(raw => {
    const part = raw.trim();
    if (part === '#root') return node.attrs.id === 'root';
    const tag = part.match(/^[a-z]+/i)?.[0];
    if (tag && node.tagName !== tag.toUpperCase()) return false;
    for (const [, key, value] of part.matchAll(/\[([^=\]]+)(?:="([^"]*)")?\]/g)) {
      if (!(key in node.attrs) || (value !== undefined && node.attrs[key] !== value)) return false;
    }
    return !!tag || part.includes('[');
  });
}
class Element {
  constructor(tag = 'div', attrs = {}) {
    this.tagName = tag.toUpperCase(); this.attrs = { ...attrs }; this.children = []; this.parentElement = null;
    this.isConnected = true; this.rect = { left: 0, top: 0, width: 200, height: 60 }; this.display = 'block';
  }
  append(...nodes) { for (const n of nodes) { this.children.push(n); n.parentElement = this; } return this; }
  getAttribute(name) { return this.attrs[name] ?? null; }
  hasAttribute(name) { return name in this.attrs; }
  getBoundingClientRect() { return { ...this.rect }; }
  getClientRects() { return this.display === 'none' ? [] : [this.getBoundingClientRect()]; }
  closest(selector) { for (let n = this; n; n = n.parentElement) if (matches(n, selector)) return n; return null; }
  contains(node) { return node === this || this.children.some(n => n.contains(node)); }
  querySelectorAll(selector) { const list = []; for (const n of this.children) { if (matches(n, selector)) list.push(n); list.push(...n.querySelectorAll(selector)); } return list; }
  querySelector(selector) { return this.querySelectorAll(selector)[0] || null; }
  get textContent() { throw new Error('content inspection forbidden'); }
  get innerText() { throw new Error('content inspection forbidden'); }
  get value() { throw new Error('input inspection forbidden'); }
}
function fixture({ anchor = 'data-slate-sidebar-content', open = 'true', withSidebar = true } = {}) {
  const root = new Element('div', { id: 'root' }), frame = new Element('div', { 'data-app-shell-frame': '', 'data-app-shell-sidebar-open': open });
  const composer = new Element('div', { 'data-codex-composer-root': '' });
  const editor = new Element('textarea'); composer.append(editor);
  const sidebar = new Element('div', { [anchor]: '' });
  const newChat = new Element('button'); sidebar.append(newChat);
  frame.append(composer); if (withSidebar) frame.append(sidebar); root.append(frame);
  const doc = { head: new Element('head'), body: new Element('body'), querySelector: selector => selector === '#root' ? root : root.querySelector(selector), fonts: { status: 'loaded' }, hasFocus: () => true, visibilityState: 'visible' };
  let time = 0;
  const win = { document: doc, performance: { now: () => time }, getComputedStyle: n => ({ display: n.display, visibility: 'visible' }) };
  Object.defineProperty(win, 'requestAnimationFrame', { get() { throw new Error('rAF dependency forbidden'); } });
  Object.defineProperty(win, 'localStorage', { get() { throw new Error('storage access forbidden'); } });
  return { win, doc, root, frame, composer, editor, sidebar, newChat, advance(ms = 650) { time += ms; return win.__dragonPreloadV1.status(); }, status() { return win.__dragonPreloadV1.status(); } };
}
function skeleton({ wrapped = false, rowDivs = false } = {}) {
  const status = new Element('div', { role: 'status' }), caption = new Element('span', { class: 'sr-only' });
  const rows = new Element();
  for (let i = 0; i < 4; i++) rows.append(new Element('div', { 'aria-hidden': 'true', class: rowDivs ? 'flex motion-safe:animate-pulse' : '' }).append(new Element(rowDivs ? 'div' : 'span', { class: rowDivs ? 'rounded bg-text/10' : 'bg-text/10 motion-safe:animate-pulse' })));
  status.append(caption); if (wrapped) status.append(rows); else status.append(...rows.children);
  return status;
}

test('empty history is ready after 650ms and samples without reading private content or scheduling frames', () => {
  const f = fixture();
  assert.equal(preload.install(f.win).reason, 'settling');
  assert.equal(f.advance(649).ready, false);
  assert.deepEqual(f.advance(1), { ready: true, shellReady: true, sidebarReady: true, composerReady: true, editorPresent: true, documentFocused: true, documentVisible: true, sampleCostMs: 0, fontsReady: true, loading: false, stableForMs: 650, settlingResets: 0, reason: 'ready' });
  assert.deepEqual(Object.keys(f.status()).sort(), ['ready', 'shellReady', 'sidebarReady', 'composerReady', 'editorPresent', 'documentFocused', 'documentVisible', 'sampleCostMs', 'fontsReady', 'loading', 'stableForMs', 'settlingResets', 'reason'].sort());
});
test('both native sidebar shells work independently; delayed open sidebar cannot be treated as ready', () => {
  for (const anchor of ['data-slate-sidebar-content', 'data-sidebar-navigation-root']) {
    const f = fixture({ anchor }); preload.install(f.win); assert.equal(f.advance().ready, true);
  }
  const f = fixture({ withSidebar: false }); assert.equal(preload.install(f.win).reason, 'sidebar-not-ready');
  f.advance(1000); f.frame.append(f.sidebar); assert.equal(f.status().reason, 'settling'); assert.equal(f.advance().ready, true);
});
test('specific direct and wrapped sidebar skeletons delay readiness; ordinary task pulse and busy status do not', () => {
  for (const options of [{}, { wrapped: true, rowDivs: true }]) {
    const f = fixture(); f.sidebar.append(skeleton(options));
    assert.equal(preload.install(f.win).loading, true); assert.equal(f.advance(5000).ready, false);
    f.sidebar.children = [f.newChat]; assert.equal(f.status().reason, 'settling'); assert.equal(f.advance().ready, true);
  }
  const f = fixture(); f.sidebar.append(new Element('button', { 'aria-busy': 'true' }).append(new Element('span', { class: 'animate-pulse' })));
  preload.install(f.win); assert.equal(f.advance().ready, true);
});
test('collapsed and hidden sidebars are optional, but a visible expanded slate wins over a stale outer flag', () => {
  const rail = fixture({ open: 'false', withSidebar: false }); preload.install(rail.win); assert.equal(rail.advance().ready, true);
  for (const value of ['', 'true']) {
    const f = fixture(); f.sidebar.attrs['data-sidebar-collapsed'] = value; f.sidebar.attrs.inert = ''; f.sidebar.append(skeleton());
    preload.install(f.win); assert.equal(f.advance().ready, true);
  }
  const hidden = fixture({ open: undefined }); delete hidden.frame.attrs['data-app-shell-sidebar-open']; hidden.sidebar.display = 'none';
  preload.install(hidden.win); assert.equal(hidden.advance().ready, true);
  const stale = fixture({ open: 'false' }); stale.sidebar.append(skeleton());
  stale.sidebar.attrs['data-sidebar-collapsed'] = 'false';
  assert.equal(preload.install(stale.win).reason, 'sidebar-loading'); assert.equal(stale.advance(2000).ready, false);
});
test('geometry, structural changes, loaders and font loading each reset stability', () => {
  const f = fixture(); preload.install(f.win); assert.equal(f.advance().ready, true);
  f.composer.rect.width += 20; assert.equal(f.status().reason, 'settling'); assert.equal(f.advance().ready, true);
  f.sidebar.append(new Element('button')); assert.equal(f.status().reason, 'settling'); assert.equal(f.advance().ready, true);
  f.sidebar.append(skeleton()); assert.equal(f.status().reason, 'sidebar-loading'); f.sidebar.children.pop();
  assert.equal(f.status().reason, 'settling'); assert.equal(f.advance().ready, true);
  f.doc.fonts.status = 'loading'; assert.equal(f.status().reason, 'fonts-loading'); f.advance(2000);
  f.doc.fonts.status = 'loaded'; assert.equal(f.status().ready, false); assert.equal(f.advance().ready, true);
});
test('missing root, hidden composer and shell-less markup never become ready; trigger fallback and disposal work', () => {
  const f = fixture(); f.doc.querySelector = () => null; assert.equal(preload.install(f.win).reason, 'shell-not-ready');
  const plain = fixture(); plain.frame.attrs = {}; plain.composer.attrs = {}; assert.equal(preload.install(plain.win).reason, 'shell-not-ready');
  const hidden = fixture(); hidden.composer.display = 'none'; preload.install(hidden.win); assert.equal(hidden.advance().reason, 'composer-not-ready');
  const fallback = fixture(); fallback.frame.children = [fallback.sidebar];
  fallback.frame.append(new Element('form').append(new Element('textarea'), new Element('button', { 'data-selected-reasoning-effort': 'high' })));
  preload.install(fallback.win); assert.equal(fallback.advance().ready, true);
  const api = fallback.win.__dragonPreloadV1; api.dispose(); api.dispose(); assert.equal(api.status().reason, 'disposed'); assert.equal(fallback.win.__dragonPreloadV1, undefined);
});
test('lazy composer wrappers and trigger-only placeholders wait for a real non-loading editor', () => {
  const f = fixture();
  f.composer.children = [];
  f.composer.append(new Element('div', { 'aria-busy': 'true' }), new Element('button', { 'data-selected-reasoning-effort': 'high' }));
  assert.equal(preload.install(f.win).reason, 'composer-not-ready');
  assert.equal(f.advance(5000).ready, false);
  f.composer.children = []; f.composer.append(f.editor);
  assert.equal(f.status().reason, 'settling'); assert.equal(f.advance().ready, true);
  f.composer.attrs['aria-busy'] = 'true'; assert.equal(f.status().reason, 'composer-not-ready');
  delete f.composer.attrs['aria-busy']; assert.equal(f.status().reason, 'settling'); assert.equal(f.advance().ready, true);
  f.composer.append(new Element('span', { 'aria-busy': 'true', class: 'animate-pulse' }));
  assert.equal(f.status().reason, 'settling'); assert.equal(f.advance().ready, true, 'an unrelated busy icon does not replace editor readiness');
  const triggerOnly = fixture(); triggerOnly.frame.children = [triggerOnly.sidebar];
  triggerOnly.frame.append(new Element('button', { 'data-selected-reasoning-effort': 'high' }));
  assert.equal(preload.install(triggerOnly.win).reason, 'composer-not-ready');
  assert.equal(triggerOnly.advance(5000).ready, false);
});
test('contenteditable editors qualify, but role=textbox without editability is not an editor', () => {
  const f = fixture(); f.composer.children = [];
  const editable = new Element('div', { role: 'textbox' }); f.composer.append(editable);
  assert.equal(preload.install(f.win).reason, 'composer-not-ready');
  editable.attrs.contenteditable = 'true'; assert.equal(f.status().reason, 'settling'); assert.equal(f.advance().ready, true);
  editable.display = 'none'; assert.equal(f.status().composerReady, false);
});
test('an open sidebar waits for visible navigation controls while allowing zero history rows', () => {
  const f = fixture(); f.sidebar.children = [];
  assert.equal(preload.install(f.win).reason, 'sidebar-not-ready'); assert.equal(f.advance(5000).ready, false);
  // Enhanced desktop places hydrated header controls outside the nested slate.
  const navigation = new Element('div', { 'data-sidebar-navigation-root': '' });
  f.frame.children = [f.composer]; navigation.append(f.newChat, f.sidebar); f.frame.append(navigation);
  assert.equal(f.status().reason, 'settling'); assert.equal(f.advance().ready, true);
  assert.equal(f.sidebar.children.length, 0, 'history remains empty');
  f.newChat.display = 'none'; assert.equal(f.status().reason, 'sidebar-not-ready');
});

test('slow-start hint appears only on the bare bootstrap page and leaves with it', () => {
  const source = require('node:fs').readFileSync(require('node:path').join(__dirname, '../src/Launcher.cs'), 'utf8');
  const expression = JSON.parse(source.match(/const string SlowStartHint = ("[^\n]*");/)[1]);
  let control = null, node = null;
  const document = {
    querySelector: () => control,
    getElementById: () => node,
    createElement: () => ({ style: {}, setAttribute() {}, remove() { node = null; } }),
    body: { append(element) { node = element; } },
  };
  const run = () => require('node:vm').runInNewContext(expression, { document });
  assert.equal(run(), true); assert.match(node.textContent, /不要关闭/);
  const shown = node; assert.equal(run(), true); assert.equal(node, shown, 'repeat polls do not stack hints');
  control = {}; assert.equal(run(), false, 'a login form, error page or shell settles the hint'); assert.equal(node, null);
  assert.equal(run(), false); assert.equal(node, null);
});

test('one native poll samples structural readiness once and installs/reads skin in the same document', () => {
  const source = require('node:fs').readFileSync(require('node:path').join(__dirname, '../src/Launcher.cs'), 'utf8');
  const expression = JSON.parse(source.match(/const string PollExpression = ("[^\n]*");/)[1]);
  const f = fixture(); f.win.codexWindowType = 'electron'; preload.install(f.win);
  let sampled = 0, installs = 0;
  const status = f.win.__dragonPreloadV1.status;
  f.win.__dragonPreloadV1.status = () => { sampled++; return status(); };
  f.win.__dragonSkinWarmV1 = { decoded: true };
  const skin = { installed: true, assetsDecoded: true, paintReady: false };
  f.win.__dragonInstallPreparedV1 = () => {
    installs++; f.win.__dragonSkinV1 = { status: () => skin }; delete f.win.__dragonInstallPreparedV1; return skin;
  };
  const run = () => require('node:vm').runInNewContext(expression, { window: f.win, document: f.doc });
  f.composer.children = [];
  const originalQuery = f.doc.querySelector; f.doc.querySelector = () => null;
  let result = run(); assert.equal(sampled, 1); assert.equal(installs, 1);
  assert.equal(result.state.assetsDecoded, true); assert.equal(result.preparation.ready, false);
  assert.equal(result.shell, false, 'real application shell has not mounted');
  assert.equal(result.warming, false, 'head/body install ready artwork before the root or editor');
  f.doc.querySelector = originalQuery; f.composer.append(f.editor); sampled = 0;
  result = run(); assert.equal(sampled, 1); assert.equal(installs, 1);
  assert.equal(result.state.assetsDecoded, true); assert.equal(result.preparation.ready, false);
  f.advance(); sampled = 0; result = run();
  assert.equal(sampled, 1); assert.equal(installs, 1); assert.equal(result.preparation.ready, true);
  assert.equal(result.state.paintReady, false, 'no browser paint acknowledgment is needed');
  f.doc.querySelector = () => null; result = run(); assert.equal(result.preparation.ready, false);
  assert.equal(result.state.installed, true, 'missing native structure cannot be replaced by our installed skin');
  f.win.codexWindowType = 'other'; sampled = 0; result = run();
  assert.equal(result.renderer, false); assert.equal(sampled, 0, 'unverified window is not sampled');
});

test('settling telemetry counts real resets without inspecting content or changing the readiness gate', () => {
  const f = fixture(); preload.install(f.win); f.advance(300);
  assert.equal(f.status().stableForMs, 300); assert.equal(f.status().settlingResets, 0);
  f.composer.rect.width += 5; assert.equal(f.status().settlingResets, 1);
  assert.equal(f.status().stableForMs, 0); assert.equal(f.advance(649).ready, false);
  assert.equal(f.advance(1).ready, true);
  f.sidebar.append(skeleton()); assert.equal(f.status().settlingResets, 2);
  assert.equal(f.advance(1000).settlingResets, 2, 'unchanged loader does not inflate reset count');
});

test('before an editor exists, boot polling does not read layout or scan sidebar geometry', () => {
  const f = fixture(); f.composer.children = [];
  for (const node of [f.root, f.frame, f.composer, f.sidebar, f.newChat]) {
    node.getClientRects = () => { throw new Error('layout flush before editor mount'); };
    node.getBoundingClientRect = () => { throw new Error('layout flush before editor mount'); };
  }
  assert.equal(preload.install(f.win).reason, 'composer-not-ready');
  assert.equal(f.advance(4000).reason, 'composer-not-ready');
  assert.equal(f.status().editorPresent, false); assert.equal(f.status().ready, false);
  for (const node of [f.root, f.frame, f.composer, f.sidebar, f.newChat]) {
    delete node.getClientRects; delete node.getBoundingClientRect;
  }
  f.composer.append(f.editor); f.doc.hasFocus = () => false;
  assert.equal(f.status().editorPresent, true); assert.equal(f.status().documentFocused, false);
  assert.equal(f.status().ready, false); assert.equal(f.advance().ready, true, 'focus is diagnostic, not another loading gate');
});
