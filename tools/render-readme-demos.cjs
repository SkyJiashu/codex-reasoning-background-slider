// Local-only documentation fixture. It uses production skin code and public artwork.
// Start: node tools/render-readme-demos.cjs --serve
// Record the printed localhost URL in an isolated browser; no Codex session is used.
// At a 1080x640 viewport, use Playwright CLI video-start (15fps) and run-code
// --filename tools/render-readme-demos.capture.js, then video-stop.
// Convert via tools/render-readme-demos.encode.ps1 -StartupMovie <local cache MP4>
// -SliderVideo <the isolated page recording>. Source MP4s/WebMs are not published.
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const artwork = [
  'assets/stages-v10-review/01_Light.png',
  ...['medium', 'high', 'extra-high', 'ultra'].map(x => `assets/stages-v11-nose/${x}.png`)
];
const html = `<!doctype html><html lang="en" class="light"><meta charset="utf-8"><title>Reasoning background slider demo</title>
<style>
*{box-sizing:border-box}html,body{margin:0;width:100%;height:100%;font-family:"Segoe UI","Microsoft YaHei UI",sans-serif;color:#ebe9f0;background:#17161c;--color-surface:#17161c}
#root{height:100vh;display:grid;grid-template-columns:198px 1fr}.sidebar{padding:29px 20px;background:#1d1c22;border-right:1px solid #ffffff0b}.brand{font-size:16px;font-weight:600;letter-spacing:.3px}.new{margin:26px 0 34px;padding:11px;border:1px solid #ffffff15;border-radius:11px;color:#d6d2de;font-size:13px}.navtitle{font-size:11px;letter-spacing:1.4px;text-transform:uppercase;color:#7f7b8b;margin:24px 0 13px}.nav{font-size:13px;color:#aaa5b5;padding:8px 0}.main{position:relative;display:flex;flex-direction:column;min-width:0}.top{height:74px;padding:25px 30px;border-bottom:1px solid #ffffff08;font-size:14px;color:#b5afc1}.badge{float:right;font-size:10px;letter-spacing:1.3px;color:#8d879c;border:1px solid #ffffff15;padding:5px 8px;border-radius:6px;margin-top:-3px}.intro{position:absolute;left:36px;top:92px}.eyebrow{color:#a99bbf;font-size:11px;letter-spacing:2px}.intro h1{font-size:30px;line-height:1.3;font-weight:500;letter-spacing:-.5px;margin:12px 0}.intro p{font-size:13px;color:#8e889d;margin:0;line-height:1.7}.panel{position:absolute;left:36px;bottom:98px;width:370px;border:1px solid #ffffff14;border-radius:18px;background:#24222bd9;padding:22px;box-shadow:0 14px 36px #0002;backdrop-filter:blur(10px)}.selection{display:flex;justify-content:space-between;align-items:center;margin-bottom:22px}.label{font-size:23px;font-weight:500}.english{font-size:11px;text-transform:uppercase;letter-spacing:1.6px;color:#a69cb7}.level{font-size:11px;color:#8e849f}input[type=range]{display:block;margin:0;width:100%;height:20px;accent-color:#c0aadf;cursor:grab}input[type=range]:active{cursor:grabbing}.stops{display:flex;justify-content:space-between;font-size:10px;color:#8e869b;margin-top:13px}.hint{font-size:11px;color:#a89bb8;margin-top:19px}.composer{position:absolute;left:36px;right:30px;bottom:25px;display:flex;justify-content:space-between;align-items:center;background:#26242ccc;border:1px solid #ffffff11;border-radius:17px;padding:16px 19px;color:#8f899c;font-size:12px}.trigger{background:none;border:0;color:#c9c0d7;font-size:12px}.cursor{position:fixed;z-index:1000;pointer-events:none;width:17px;height:21px;filter:drop-shadow(0 2px 2px #0008);transform:translate(-2px,-2px)}.cursor:before{content:"";position:absolute;border-left:7px solid #fff;border-top:13px solid transparent;transform:rotate(-24deg);left:3px;top:0}.cursor:after{content:"";position:absolute;width:4px;height:10px;background:white;transform:rotate(-27deg);left:8px;top:11px}.click-ring{position:fixed;z-index:999;pointer-events:none;width:36px;height:36px;border:1px solid #cbb4ee88;border-radius:50%;transform:translate(-18px,-18px);opacity:0}.click-ring.down{opacity:1}
</style>
<style>
.theme{float:right;margin:-6px 14px 0 0;padding:5px 10px;background:transparent;border:1px solid #ffffff22;border-radius:7px;color:#bdb5cb;font-size:11px;cursor:pointer}html.light,html.light body{color:#302938;background:#f0edf5;--color-surface:#f0edf5}html.light .sidebar{background:#e8e4ee;border-right-color:#43364b12}html.light .new{color:#665b72;border-color:#43364b20}html.light .navtitle{color:#93879d}html.light .nav{color:#7e718a}html.light .top{color:#7a6c86;border-color:#43364b12}html.light .badge{color:#8a7b95;border-color:#43364b20}html.light .eyebrow{color:#8f78a7}html.light .intro p{color:#8c7f98}html.light .panel{background:#faf8fddf;border-color:#917ca52e;box-shadow:0 14px 36px #78628d0f}html.light .english,html.light .hint{color:#9a83ae}html.light .level,html.light .stops{color:#a494b1}html.light .composer{background:#faf8fddf;border-color:#917ca526;color:#a89ab6}html.light .trigger{color:#8a739f}html.light .theme{color:#8a739f;border-color:#917ca53d}
</style>
<div id="root" data-app-shell-frame>
<aside class="sidebar"><div class="brand">Codex</div><div class="new">＋ 新任务 / New task</div><div class="navtitle">Workspace</div><div class="nav">◈ Playground</div><div class="nav">◇ Design notes</div><div class="nav">⌘ Sample project</div><div class="navtitle">Appearance</div><div class="nav">Reasoning backgrounds</div><div class="nav">Independent launcher</div></aside>
<main class="main" data-app-shell-main-surface><header class="top" data-app-shell-titlebar-slot>Reasoning Background Slider <span class="badge">ISOLATED DEMO</span><button id="theme" class="theme">☾ Dark</button></header>
<section class="intro"><div class="eyebrow">SLIDE TO SEE</div><h1>让推理强度，成为可见的风景。</h1><p>Drag to preview each character.<br>背景实时跟随拖动 · No need to release.</p></section>
<section class="panel"><div class="selection"><div><div id="label" class="label">轻度</div><div id="english" class="english">Light</div></div><div id="level" class="level">01 / 05</div></div><div data-model-picker-power-slider><input id="effort" type="range" min="0" max="4" step="1" value="0" aria-label="Reasoning effort" aria-valuemin="0" aria-valuemax="4" aria-valuenow="0"></div><div class="stops"><span>轻度</span><span>中</span><span>高</span><span>极高</span><span>Ultra</span></div><div id="hint" class="hint">拖动预览 / Drag to preview</div></section>
<div class="composer" data-codex-composer-root><span>描述你想完成的任务… / What would you like to build?</span><button class="trigger" data-codex-intelligence-trigger data-selected-reasoning-effort="low">Reasoning <span id="native-label">Light</span> ⌄</button></div>
</main></div><div id="cursor" class="cursor" style="left:400px;top:565px"></div><div id="ring" class="click-ring"></div>
<script src="/skin.js"></script><script>
const stages=[['轻度','Light','low'],['中','Medium','medium'],['高','High','high'],['极高','Extra high','xhigh'],['Ultra','Ultra','ultra']];
const slider=document.getElementById('effort'),trigger=document.querySelector('[data-codex-intelligence-trigger]');
document.getElementById('theme').addEventListener('click',()=>{const light=document.documentElement.classList.toggle('light');document.getElementById('theme').textContent=light?'☾ Dark':'☀ Light';});
function caption(){const n=Number(slider.value),s=stages[n];slider.setAttribute('aria-valuenow',String(n));document.getElementById('label').textContent=s[0];document.getElementById('english').textContent=s[1];document.getElementById('level').textContent=String(n+1).padStart(2,'0')+' / 05';document.getElementById('hint').textContent=n===4?'Ultra · 紫晶配色与星星 / Crystal accents':'拖动中实时预览 / Live drag preview';}
slider.addEventListener('input',caption);
slider.addEventListener('change',()=>{const s=stages[Number(slider.value)];trigger.setAttribute('data-selected-reasoning-effort',s[2]);document.getElementById('native-label').textContent=s[1];});
document.addEventListener('pointermove',e=>{for(const id of ['cursor','ring']){const n=document.getElementById(id);n.style.left=e.clientX+'px';n.style.top=e.clientY+'px';}});
document.addEventListener('pointerdown',()=>document.getElementById('ring').classList.add('down'));document.addEventListener('pointerup',()=>document.getElementById('ring').classList.remove('down'));
Promise.all([fetch('/skin.css').then(r=>r.text()),...${JSON.stringify(artwork)}.map(p=>fetch('/'+p).then(r=>r.blob()).then(b=>new Promise(resolve=>{const reader=new FileReader();reader.onload=()=>resolve(reader.result);reader.readAsDataURL(b)})))]).then(([css,...assets])=>{window.DragonSkin.install(window,assets,css);window.demoReady=true;});
</script></html>`;
const allowed = new Set([...artwork.map(p => '/'+p), '/skin.js', '/skin.css']);
const server = http.createServer((req,res) => {
  const url = req.url.split('?')[0];
  if (url === '/') { res.setHeader('Content-Type','text/html;charset=utf-8'); res.end(html); return; }
  if (!allowed.has(url)) {res.writeHead(404).end(); return;}
  const source = url.startsWith('/skin.') ? 'src'+url : url.slice(1);
  res.setHeader('Content-Type', url.endsWith('.png')?'image/png':url.endsWith('.css')?'text/css;charset=utf-8':'text/javascript;charset=utf-8');
  fs.createReadStream(path.join(root,source)).pipe(res);
});
server.listen(0,'127.0.0.1',()=>console.log('Readme demo: http://127.0.0.1:'+server.address().port));
