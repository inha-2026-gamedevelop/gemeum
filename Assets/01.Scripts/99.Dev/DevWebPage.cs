namespace Geumeum.Dev
{
    /// <summary>
    /// 개발 패널 브라우저 화면.
    ///
    /// 파일로 두면 빌드에 포함시키는 규칙(StreamingAssets)이 하나 더 생기고,
    /// 팀원이 빌드에서 패널이 안 뜨는 이유를 찾기 어려워진다. 그래서 문자열로 박아 둔다.
    ///
    /// 안에 겹따옴표를 쓰지 않는다 - C# 축자 문자열에서 겹따옴표를 두 번씩 적어야 해서 읽기 어려워진다.
    /// HTML 속성과 자바스크립트 문자열은 홑따옴표로 쓴다
    /// </summary>
    public static class DevWebPage
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS

        /// <summary> GET / 로 나가는 페이지 전체 </summary>
        public const string HTML = @"<!doctype html>
<html lang='ko'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width, initial-scale=1'>
<title>그믐 개발 패널</title>
<style>
  :root {
    --bg: #16171b; --panel: #1f2127; --line: #2f333c; --text: #e6e6e6;
    --dim: #9aa0aa; --accent: #6fb7ff; --on: #2b6cb0;
    --good: #74e08c; --bad: #ff7373; --warn: #ffd95a; --server: #8ccbff;
  }
  * { box-sizing: border-box; }
  body {
    margin: 0; background: var(--bg); color: var(--text);
    font: 13px/1.5 'Malgun Gothic', 'Segoe UI', system-ui, sans-serif;
  }
  header {
    position: sticky; top: 0; z-index: 2; display: flex; gap: 12px; align-items: center;
    padding: 8px 14px; background: #101116; border-bottom: 1px solid var(--line);
  }
  header h1 { font-size: 14px; margin: 0; font-weight: 600; }
  header .role { color: var(--accent); }
  header .link { margin-left: auto; color: var(--dim); }
  #dot { width: 9px; height: 9px; border-radius: 50%; background: var(--bad); }
  #dot.live { background: var(--good); }
  /* 격자로 놓으면 카메라 패널 하나가 길어질 때 옆 칸이 통째로 비어 버린다. 다단으로 흘려 채운다 */
  main { column-width: 330px; column-gap: 12px; padding: 12px; }
  section {
    background: var(--panel); border: 1px solid var(--line); border-radius: 6px; overflow: hidden;
    break-inside: avoid; display: inline-block; width: 100%; margin: 0 0 12px;
  }
  section > h2 {
    margin: 0; padding: 7px 10px; font-size: 13px; font-weight: 600;
    background: #262931; border-bottom: 1px solid var(--line); cursor: pointer;
  }
  section.closed > .body { display: none; }
  .body { padding: 8px 10px 10px; display: flex; flex-direction: column; gap: 6px; }
  .row { display: flex; gap: 8px; align-items: center; }
  .row > .name { flex: 1 1 auto; }
  .head { margin-top: 6px; color: var(--dim); border-bottom: 1px solid var(--line); padding-bottom: 3px; }
  .text { white-space: pre-wrap; }
  button {
    background: #333844; color: var(--text); border: 1px solid var(--line);
    border-radius: 4px; padding: 5px 9px; font: inherit; cursor: pointer;
  }
  button:hover { background: #3d4351; }
  button.on { background: var(--on); border-color: var(--accent); }
  button.wide { width: 100%; padding: 8px; }
  input[type=text], input[type=number] {
    width: 76px; background: #14161a; color: var(--text);
    border: 1px solid var(--line); border-radius: 4px; padding: 4px 6px; font: inherit;
  }
  input[type=range] { flex: 1 1 auto; }
  label.toggle { display: flex; gap: 7px; align-items: center; cursor: pointer; }
  .choice { display: flex; gap: 5px; flex-wrap: wrap; }
  .log {
    height: 260px; overflow-y: auto; background: #101216; border: 1px solid var(--line);
    border-radius: 4px; padding: 6px; font-family: Consolas, monospace; font-size: 12px;
  }
  .log div { white-space: pre-wrap; }
  .log .success { color: var(--good); }
  .log .fail { color: var(--bad); }
  .log .warn { color: var(--warn); }
  .log .server { color: var(--server); }
  .log .boss { color: #ffa257; }
  canvas { width: 100%; height: 62px; background: #101216; border: 1px solid var(--line); border-radius: 4px; }
  .empty { padding: 24px 14px; color: var(--dim); }
</style>
</head>
<body>
<header>
  <div id='dot'></div>
  <h1 id='title'>그믐 개발 패널</h1>
  <span class='role' id='role'></span>
  <span class='link'>게임 화면에는 기즈모와 이름표만 남는다</span>
</header>
<main id='main'><div class='empty'>게임에서 상태를 기다린다…</div></main>
<script>
const panels = new Map();
let live = false;

async function poll() {
  try {
    const res = await fetch('/api/state', { cache: 'no-store' });
    render(await res.json());
    setLive(true);
  } catch (err) {
    setLive(false);
  }
}

function setLive(isLive) {
  if (isLive === live) { return; }
  live = isLive;
  document.getElementById('dot').className = isLive ? 'live' : '';
}

async function send(panel, control, value) {
  try {
    await fetch('/api/command', {
      method: 'POST',
      body: JSON.stringify({ panel: panel, control: control, value: String(value) })
    });
  } catch (err) {
    setLive(false);
  }
}

function render(snapshot) {
  if (!snapshot || !snapshot.panels) { return; }

  document.getElementById('role').textContent = snapshot.role || '';

  const main = document.getElementById('main');
  const empty = main.querySelector('.empty');
  if (empty) { empty.remove(); }

  const seen = new Set();

  for (const view of snapshot.panels) {
    seen.add(view.id);
    let entry = panels.get(view.id);

    if (!entry) {
      entry = createPanel(view);
      panels.set(view.id, entry);
      main.appendChild(entry.section);
    }

    const sig = (view.controls || []).map((c, i) => c.kind + ':' + (c.id || i)).join('|');
    if (sig !== entry.sig) {
      entry.sig = sig;
      entry.controls = buildBody(view, entry.body);
    }

    updateBody(view, entry.controls);
  }

  // 사라진 패널은 지운다. 씬을 옮기면 그 씬에만 있던 도구가 빠진다
  for (const [id, entry] of panels) {
    if (!seen.has(id)) { entry.section.remove(); panels.delete(id); }
  }
}

function createPanel(view) {
  const section = document.createElement('section');
  const head = document.createElement('h2');
  head.textContent = view.title || view.id;
  head.onclick = () => section.classList.toggle('closed');

  const body = document.createElement('div');
  body.className = 'body';

  section.appendChild(head);
  section.appendChild(body);

  return { section: section, body: body, sig: '', controls: [] };
}

function buildBody(view, body) {
  body.textContent = '';
  const built = [];

  (view.controls || []).forEach((control, index) => {
    const made = buildControl(view.id, control, index);
    body.appendChild(made.root);
    built.push(made);
  });

  return built;
}

function buildControl(panelId, control, index) {
  const kind = control.kind;

  if (kind === 'label') {
    const el = document.createElement('div');
    el.className = (control.label === 'header') ? 'head' : 'text';
    return { kind: kind, root: el, el: el };
  }

  if (kind === 'toggle') {
    const root = document.createElement('label');
    root.className = 'toggle';
    const box = document.createElement('input');
    box.type = 'checkbox';
    const name = document.createElement('span');
    name.textContent = control.label || control.id;
    box.onchange = () => send(panelId, control.id, box.checked ? '1' : '0');
    root.appendChild(box);
    root.appendChild(name);
    return { kind: kind, root: root, el: box, name: name };
  }

  if (kind === 'button') {
    const btn = document.createElement('button');
    btn.className = 'wide';
    btn.textContent = control.label || control.id;
    btn.onclick = () => send(panelId, control.id, '');
    return { kind: kind, root: btn, el: btn };
  }

  if (kind === 'slider') {
    const root = document.createElement('div');
    const top = document.createElement('div');
    top.className = 'row';
    const name = document.createElement('span');
    name.className = 'name';
    name.textContent = control.label || control.id;
    const num = document.createElement('input');
    num.type = 'text';
    const bar = document.createElement('input');
    bar.type = 'range';
    bar.min = control.min;
    bar.max = control.max;
    bar.step = (control.max - control.min) / 500;
    bar.oninput = () => { num.value = bar.value; send(panelId, control.id, bar.value); };
    num.onchange = () => send(panelId, control.id, num.value);
    top.appendChild(name);
    top.appendChild(num);
    root.appendChild(top);
    root.appendChild(bar);
    return { kind: kind, root: root, el: bar, num: num, name: name };
  }

  if (kind === 'text') {
    const root = document.createElement('div');
    root.className = 'row';
    const name = document.createElement('span');
    name.className = 'name';
    name.textContent = control.label || control.id;
    const box = document.createElement('input');
    box.type = 'text';
    box.onchange = () => send(panelId, control.id, box.value);
    root.appendChild(name);
    root.appendChild(box);
    return { kind: kind, root: root, el: box, name: name };
  }

  if (kind === 'choice') {
    const root = document.createElement('div');
    const name = document.createElement('div');
    name.textContent = control.label || '';
    const wrap = document.createElement('div');
    wrap.className = 'choice';
    const buttons = [];

    // 값으로 라벨이 아니라 순번을 보낸다. 라벨을 고쳐도 게임 쪽 분기가 깨지지 않는다
    (control.options || []).forEach((option, i) => {
      const btn = document.createElement('button');
      btn.textContent = option;
      btn.onclick = () => send(panelId, control.id, String(i));
      wrap.appendChild(btn);
      buttons.push(btn);
    });

    root.appendChild(name);
    root.appendChild(wrap);
    return { kind: kind, root: root, el: wrap, buttons: buttons, name: name };
  }

  if (kind === 'log') {
    const el = document.createElement('div');
    el.className = 'log';
    return { kind: kind, root: el, el: el, last: '' };
  }

  if (kind === 'plot') {
    const root = document.createElement('div');
    const name = document.createElement('div');
    const canvas = document.createElement('canvas');
    canvas.width = 480;
    canvas.height = 62;
    root.appendChild(name);
    root.appendChild(canvas);
    return { kind: kind, root: root, el: canvas, name: name };
  }

  const unknown = document.createElement('div');
  unknown.textContent = '모르는 조작: ' + kind;
  return { kind: kind, root: unknown, el: unknown };
}

function updateBody(view, built) {
  (view.controls || []).forEach((control, index) => {
    const made = built[index];
    if (!made) { return; }

    const focused = (document.activeElement === made.el) || (document.activeElement === made.num);

    if (made.kind === 'label') {
      made.el.textContent = control.text || '';
      return;
    }

    if (made.kind === 'toggle') {
      made.name.textContent = control.label || control.id;
      if (!focused) { made.el.checked = control.isOn; }
      return;
    }

    if (made.kind === 'button') {
      made.el.textContent = control.label || control.id;
      return;
    }

    if (made.kind === 'slider') {
      made.name.textContent = control.label || control.id;
      if (!focused) {
        made.el.min = control.min;
        made.el.max = control.max;
        made.el.value = control.value;
        made.num.value = control.text;
      }
      return;
    }

    if (made.kind === 'text') {
      made.name.textContent = control.label || control.id;
      if (!focused) { made.el.value = control.text || ''; }
      return;
    }

    if (made.kind === 'choice') {
      made.name.textContent = control.label || '';
      (control.options || []).forEach((option, i) => {
        const btn = made.buttons[i];
        if (btn) { btn.className = (option === control.text) ? 'on' : ''; }
      });
      return;
    }

    if (made.kind === 'log') {
      const lines = control.lines || [];
      const joined = lines.join('\n');
      if (joined === made.last) { return; }

      // 맨 아래를 보고 있었으면 새 줄을 따라 내린다
      const atBottom = (made.el.scrollTop + made.el.clientHeight + 24) >= made.el.scrollHeight;
      made.last = joined;
      made.el.textContent = '';

      for (const line of lines) {
        const cut = line.indexOf('|');
        const kind = (cut > 0) ? line.substring(0, cut) : '';
        const div = document.createElement('div');
        div.className = kind;
        div.textContent = (cut > 0) ? line.substring(cut + 1) : line;
        made.el.appendChild(div);
      }

      if (atBottom) { made.el.scrollTop = made.el.scrollHeight; }
      return;
    }

    if (made.kind === 'plot') {
      made.name.textContent = control.label || control.id;
      drawPlot(made.el, control);
    }
  });
}

function drawPlot(canvas, control) {
  const samples = control.samples || [];
  const ctx = canvas.getContext('2d');
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  if (samples.length < 2) { return; }

  const span = Math.max(control.max - control.min, 0.0001);
  ctx.strokeStyle = '#6fb7ff';
  ctx.lineWidth = 1.5;
  ctx.beginPath();

  samples.forEach((value, i) => {
    const x = (i / (samples.length - 1)) * canvas.width;
    const y = canvas.height - ((value - control.min) / span) * (canvas.height - 6) - 3;
    if (i === 0) { ctx.moveTo(x, y); } else { ctx.lineTo(x, y); }
  });

  ctx.stroke();
}

poll();
setInterval(poll, 200);
</script>
</body>
</html>";

#else

        /// <summary> 릴리스에서는 서버가 컴파일되지 않으므로 내용도 두지 않는다 </summary>
        public const string HTML = "";

#endif
    }
}
