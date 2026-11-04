(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.DragonPreload = api;
})(typeof globalThis === 'object' ? globalThis : this, function () {
  'use strict';
  const KEY = '__dragonPreloadV1';
  const COMPOSER = '[data-codex-composer-root], [data-chatgpt-composer], [data-codex-composer-request-navigation]';
  const EDITOR = 'textarea, [contenteditable="true"], [contenteditable="plaintext-only"], [contenteditable=""]';
  const SLATE = '[data-slate-sidebar-content]';
  const NAV = '[data-sidebar-navigation-root]';
  const SIDEBAR_AREA = '[data-app-shell-left-panel-appearance], [data-app-shell-page-sidebar], aside';
  const NAV_CONTROL = 'button, a[href], [role="button"], [role="link"], [role="treeitem"]';
  const SETTLE_MS = 650;

  function nodes(scope, selector, limit) {
    const found = scope.querySelectorAll(selector), result = [];
    for (let i = 0; i < Math.min(found.length, limit); i++) result.push(found[i]);
    return result;
  }
  function children(node) { return Array.prototype.slice.call(node.children || [], 0, 32); }
  function hasClass(node, token) { return (node.getAttribute('class') || '').slice(0, 2048).split(/\s+/).some(x => x === token || x.endsWith(':' + token)); }
  function collapsed(node) {
    const value = node.getAttribute('data-sidebar-collapsed');
    return value === '' || value === 'true';
  }
  function geometry(node) {
    const rect = node.getBoundingClientRect();
    const values = [rect.left, rect.top, rect.width, rect.height];
    return values.every(Number.isFinite) ? values.map(Math.round) : null;
  }
  function visible(win, node, semantic = true) {
    if (!node || node.isConnected === false || node.getClientRects().length === 0) return false;
    if (semantic && node.closest('[hidden], [inert], [aria-hidden="true"]')) return false;
    const rect = geometry(node);
    if (!rect || rect[2] <= 0 || rect[3] <= 0) return false;
    const style = win.getComputedStyle ? win.getComputedStyle(node) : null;
    return !style || (style.display !== 'none' && style.visibility !== 'hidden' && style.visibility !== 'collapse');
  }
  function explicitlyHidden(win, node) {
    if (node.hasAttribute('hidden')) return true;
    const style = win.getComputedStyle ? win.getComputedStyle(node) : null;
    return !!style && (style.display === 'none' || style.visibility === 'hidden' || style.visibility === 'collapse');
  }
  function skeletonRow(row) {
    if (row.tagName !== 'DIV' || row.getAttribute('aria-hidden') !== 'true') return false;
    const blocks = [row, ...nodes(row, 'div, span', 24)];
    return blocks.some(n => hasClass(n, 'animate-pulse')) && blocks.some(n => hasClass(n, 'bg-text/10'));
  }
  function repeatedSkeleton(container) { return children(container).filter(skeletonRow).length >= 3; }
  function sidebarLoading(win, scope) {
    for (const status of nodes(scope, 'div[role="status"]', 16)) {
      if (!visible(win, status, false) || !children(status).some(n => n.tagName === 'SPAN' && hasClass(n, 'sr-only'))) continue;
      // Wp uses a direct row list or one/two layout wrappers, depending on density.
      const wrappers = children(status).filter(n => n.tagName === 'DIV');
      const containers = [status, ...wrappers, ...wrappers.flatMap(children)].slice(0, 32);
      if (containers.some(repeatedSkeleton)) return true;
    }
    // The full empty sidebar may mount its placeholder list before the status wrapper.
    // Only recognize a repeated aria-hidden skeleton cluster with no interactive rows.
    if (nodes(scope, 'a, button, [role="treeitem"], [role="link"]', 1).length === 0) {
      const containers = [scope, ...children(scope), ...children(scope).flatMap(children)].slice(0, 64);
      if (containers.some(repeatedSkeleton)) return true;
    }
    return false;
  }
  function structuralSignature(node) {
    return [geometry(node), Math.min(node.children.length, 64),
      nodes(node, 'a, button, [role="treeitem"], [role="link"]', 96).length];
  }
  function composerWithEditor(win, scopes) {
    for (const scope of scopes) {
      if (!visible(win, scope)) continue;
      for (const editor of nodes(scope, EDITOR, 8).filter(n => visible(win, n))) {
        const busy = editor.closest('[aria-busy="true"]');
        // Only the editor's composer subtree matters. Unrelated busy task icons,
        // sidebar controls and application-level activity are not loading gates.
        if (busy && scopes.some(n => n === busy || n.contains(busy))) continue;
        return { scope, editor };
      }
    }
    return null;
  }
  function sample(win) {
    const doc = win.document, root = doc.querySelector('#root');
    const fontsReady = !doc.fonts || doc.fonts.status !== 'loading';
    const missing = { sidebarReady: false, composerReady: false, editorPresent: false, fontsReady, loading: false, shellReady: false, signature: '' };
    if (!root) return missing;
    const frames = nodes(root, '[data-app-shell-frame]', 4);
    const composers = nodes(root, COMPOSER, 8);
    const nativeTriggers = nodes(root, '[data-selected-reasoning-effort]', 8);
    const composerScopes = [...new Set([...composers, ...nativeTriggers.map(n => n.closest(COMPOSER + ', form')).filter(Boolean)])].slice(0, 12);
    const editorPresent = composerScopes.some(n => nodes(n, EDITOR, 1).length > 0);
    // Before the real editor mounts, readiness cannot pass. Do not synchronously
    // flush the growing application's layout or scan sidebar geometry every poll.
    if (!editorPresent) return { ...missing, shellReady: !!(frames.length || composers.length || nativeTriggers.length) };
    if (!visible(win, root)) return { ...missing, editorPresent };
    const frame = frames.find(n => visible(win, n));
    const triggers = nativeTriggers.filter(n => visible(win, n));
    const composer = composerWithEditor(win, composerScopes);
    const shellReady = !!(frame || composers.some(n => visible(win, n)) || triggers.length || nodes(root, '[data-app-shell-main-surface]', 1).some(n => visible(win, n)));
    const slates = nodes(root, SLATE, 4), navs = nodes(root, NAV, 4);
    const expandedSlates = slates.filter(n => !collapsed(n) && visible(win, n));
    const outerClosed = frame && frame.getAttribute('data-app-shell-sidebar-open') === 'false';
    // A visible expanded desktop slate is stronger evidence than an outer atom
    // that can briefly report the navigation rail's state instead.
    const closed = expandedSlates.length === 0 && (outerClosed || [...slates, ...navs].some(collapsed));
    const scopes = expandedSlates.length ? expandedSlates : closed ? [] : navs.filter(n => visible(win, n));
    const hidden = !closed && scopes.length === 0 && !frames.some(n => n.getAttribute('data-app-shell-sidebar-open') === 'true') && [...slates, ...navs].some(n => explicitlyHidden(win, n));
    const loading = scopes.some(n => sidebarLoading(win, n));
    const navigationOwners = [...new Set(scopes.flatMap(n => [n, n.closest(NAV), n.closest(SIDEBAR_AREA)]).filter(n => n && visible(win, n)))].slice(0, 12);
    const controls = navigationOwners.flatMap(n => nodes(n, NAV_CONTROL, 16)).filter(n => visible(win, n)).slice(0, 16);
    const sidebarReady = !!((closed || hidden || (scopes.length > 0 && controls.length > 0)) && !loading);
    return {
      shellReady, sidebarReady, composerReady: !!composer, editorPresent, fontsReady, loading,
      signature: JSON.stringify([geometry(root), frame && geometry(frame), composer && [structuralSignature(composer.scope), geometry(composer.editor)],
        closed ? 'collapsed' : hidden ? 'hidden' : [scopes.map(structuralSignature), navigationOwners.map(structuralSignature), controls.map(geometry)]])
    };
  }
  function install(win) {
    if (win[KEY]) return win[KEY].status();
    let disposed = false, signature = null, since = 0, resets = 0;
    const api = {
      status() {
        const result = { ready: false, shellReady: false, sidebarReady: false, composerReady: false, editorPresent: false, documentFocused: false, documentVisible: false, sampleCostMs: 0, fontsReady: false, loading: false, stableForMs: 0, settlingResets: resets, reason: 'disposed' };
        if (disposed) return result;
        try {
          const started = win.performance && win.performance.now ? win.performance.now() : Date.now();
          const state = sample(win);
          for (const field of ['shellReady', 'sidebarReady', 'composerReady', 'editorPresent', 'fontsReady', 'loading']) result[field] = state[field];
          result.documentFocused = typeof win.document.hasFocus === 'function' && win.document.hasFocus();
          result.documentVisible = win.document.visibilityState === 'visible';
          const reason = !state.shellReady ? 'shell-not-ready' : !state.composerReady ? 'composer-not-ready' : state.loading ? 'sidebar-loading' : !state.sidebarReady ? 'sidebar-not-ready' : !state.fontsReady ? 'fonts-loading' : null;
          const now = win.performance && win.performance.now ? win.performance.now() : Date.now();
          result.sampleCostMs = Math.max(0, Math.floor(now - started));
          if (reason) { if (signature !== null) resets++; signature = null; since = now; result.settlingResets = resets; result.reason = reason; return result; }
          if (signature !== state.signature || now < since) { if (signature !== null) resets++; signature = state.signature; since = now; }
          result.stableForMs = Math.max(0, Math.floor(now - since)); result.settlingResets = resets;
          result.ready = now - since >= SETTLE_MS;
          result.reason = result.ready ? 'ready' : 'settling';
          return result;
        } catch (_) {
          signature = null; result.reason = 'sampling-unavailable'; return result;
        }
      },
      dispose() { disposed = true; signature = null; if (win[KEY] === api) delete win[KEY]; }
    };
    win[KEY] = api;
    return api.status();
  }
  return { install };
});
