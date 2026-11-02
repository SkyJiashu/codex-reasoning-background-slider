const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const allowed = new Set(['/tests/preview.html', '/tests/avatar-frame-preview.html', '/assets/candidates/avatar-rings-v7/extra-high.svg', '/assets/candidates/avatar-rings-v7/ultra.svg', '/src/skin.js', '/src/skin.css', '/assets/startup-hero-v1.png', ...['01_Light', '02_Medium', '03_High', '04_Very_High', '05_Ultra'].map(x => '/' + x + '.png')]);
const server = http.createServer((req, res) => {
  const name = req.url === '/' ? '/tests/preview.html' : req.url;
  if (!allowed.has(name)) { res.writeHead(404).end(); return; }
  res.setHeader('Content-Type', name.endsWith('.svg') ? 'image/svg+xml' : name.endsWith('.png') ? 'image/png' : name.endsWith('.js') ? 'text/javascript; charset=utf-8' : name.endsWith('.css') ? 'text/css; charset=utf-8' : 'text/html; charset=utf-8');
  fs.createReadStream(path.join(root, name)).on('error', () => res.destroy()).pipe(res);
});
for (const name of ['medium','high','extra-high','ultra']) allowed.add('/assets/production-v9/' + name + '.png');
for (const name of ['01_Light','02_Medium','03_High','04_Very_High','05_Ultra']) allowed.add('/assets/stages-v10-review/' + name + '.png');
allowed.add('/assets/stages-v10-review/04_Very_High_barehands.png');
for(const stage of ['medium','high','extra-high','ultra'])allowed.add('/assets/stages-v11-nose/'+stage+'.png');
allowed.add('/tests/nose-review.html');
allowed.add('/tests/preload-preview.html'); allowed.add('/src/preload.js');
allowed.add('/tests/stages-v10-preview.html'); allowed.add('/tests/stages-v10-faces.html');
server.listen(0, '127.0.0.1', () => process.stdout.write('Offline preview: http://127.0.0.1:' + server.address().port + '\n'));
