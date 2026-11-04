// Read-only: reports whether the installed Codex front end still ships the attributes the skin reads.
// Run after a Codex update: node scripts/check-client-compat.cjs
const fs = require('fs'), path = require('path'), { execFileSync } = require('child_process');
const REQUIRED = ['data-selected-reasoning-effort', 'data-app-shell-frame', 'data-codex-composer-root', 'codexWindowType', 'sendMessageFromView', 'quit-app'];

function scan(asarPath, needles) {
  const file = fs.openSync(asarPath, 'r');
  try {
    const head = Buffer.alloc(16); fs.readSync(file, head, 0, 16, 0);
    const size = head.readUInt32LE(4), length = head.readUInt32LE(12);
    const json = Buffer.alloc(length); fs.readSync(file, json, 0, length, 16);
    const base = 8 + size, missing = new Set(needles);
    (function walk(node) {
      for (const [name, entry] of Object.entries(node.files || {})) {
        if (!missing.size) return;
        if (entry.files) { walk(entry); continue; }
        if (entry.unpacked || !/\.(js|mjs|cjs|html)$/.test(name)) continue;
        const data = Buffer.alloc(Number(entry.size)); fs.readSync(file, data, 0, data.length, base + Number(entry.offset));
        const text = data.toString('latin1');
        for (const needle of [...missing]) if (text.includes(needle)) missing.delete(needle);
      }
    })(JSON.parse(json.toString()));
    return [...missing];
  } finally { fs.closeSync(file); }
}

if (require.main === module) {
  const [version, root] = execFileSync('powershell.exe', ['-NoProfile', '-Command', "$p=Get-AppxPackage OpenAI.Codex;$p.Version;$p.InstallLocation"], { encoding: 'utf8' }).trim().split(/\r?\n/);
  const missing = scan(path.join(root, 'app', 'resources', 'app.asar'), REQUIRED);
  console.log(missing.length ? `Codex ${version}: skin needs adapting, missing ${missing.join(', ')}` : `Codex ${version}: all skin attributes present`);
  process.exit(missing.length ? 1 : 0);
}
module.exports = { scan };
