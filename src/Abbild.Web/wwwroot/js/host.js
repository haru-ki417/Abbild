// Abbild ブラウザー版 — 読み込み・画面のボタン・セーブ
//   ・ゲームの絵とフォント（Content/manifest.txt の一覧）を取ってきて C# に渡す（ゲームはファイルとして読む）
//   ・スマホ・タブレットでは、画面に十字キーと決定・もどる・息・メニューのボタンを出す
//   ・セーブ（冒険の書・設定）は、この端末のブラウザー（localStorage）にだけ残す
(function () {
  const SAVE = 'abbild.save:';
  const ACT = { up: 0, down: 1, left: 2, right: 3, confirm: 4, cancel: 5, menu: 6, breath: 7 };
  let dotnet = null, mask = 0;
  const $ = id => document.getElementById(id);

  function setProgress(done, total, text) {
    const p = total ? done / total : 0;
    const bar = $('load-bar'); if (bar) bar.style.width = (p * 100).toFixed(1) + '%';
    const t = $('load-text'); if (t) t.textContent = text;
  }

  async function loadContent() {
    const list = (await (await fetch('Content/manifest.txt', { cache: 'no-cache' })).text())
      .split(/\r?\n/).map(s => s.trim().replace(/\\/g, '/')).filter(s => s && !/\.(txt)$/i.test(s) || /^(credits|howto)\.txt$/i.test(s));
    let done = 0;
    const queue = list.slice();
    const worker = async () => {
      while (queue.length) {
        const path = queue.shift();
        const res = await fetch('Content/' + path.split('/').map(encodeURIComponent).join('/'));
        if (!res.ok) throw new Error(path + ' を読めませんでした');
        const bytes = new Uint8Array(await res.arrayBuffer());
        await dotnet.invokeMethodAsync('PutContent', path, bytes);
        done++;
        setProgress(done, list.length, `絵と文字を読み込んでいます… ${done} / ${list.length}`);
      }
    };
    await Promise.all(Array.from({ length: 6 }, worker));
  }

  async function restoreSaves() {
    try {
      for (let i = 0; i < localStorage.length; i++) {
        const key = localStorage.key(i);
        if (key && key.startsWith(SAVE)) await dotnet.invokeMethodAsync('PutSave', key.slice(SAVE.length), localStorage.getItem(key));
      }
    } catch { /* 保存が使えないブラウザーでも遊べる（セーブは残らない） */ }
  }

  function tick() {
    try { dotnet.invokeMethod('TickDotNet'); } catch (e) { console.error(e); }
    requestAnimationFrame(tick);
  }

  // ---- 画面のボタン
  function setAct(act, on) {
    const bit = 1 << ACT[act];
    const next = on ? (mask | bit) : (mask & ~bit);
    if (next !== mask) { mask = next; dotnet.invokeMethod('SetTouch', mask); }
  }

  function bindPad() {
    const pad = $('pad');
    const dirs = ['up', 'down', 'left', 'right'];
    const active = new Map();
    const update = () => {
      const want = new Set();
      for (const p of active.values()) {
        const r = pad.getBoundingClientRect();
        const dx = p.x - (r.left + r.width / 2), dy = p.y - (r.top + r.height / 2);
        const dead = r.width * 0.12;
        if (Math.hypot(dx, dy) < dead) continue;
        const ang = Math.atan2(dy, dx) * 180 / Math.PI; // 右 0、下 90
        if (ang > -67.5 && ang < 67.5) want.add('right');
        if (ang > 112.5 || ang < -112.5) want.add('left');
        if (ang > 22.5 && ang < 157.5) want.add('down');
        if (ang < -22.5 && ang > -157.5) want.add('up');
      }
      for (const d of dirs) { setAct(d, want.has(d)); pad.classList.toggle('on-' + d, want.has(d)); }
    };
    pad.addEventListener('pointerdown', e => { pad.setPointerCapture(e.pointerId); active.set(e.pointerId, { x: e.clientX, y: e.clientY }); update(); e.preventDefault(); });
    pad.addEventListener('pointermove', e => { if (active.has(e.pointerId)) { active.set(e.pointerId, { x: e.clientX, y: e.clientY }); update(); } });
    const end = e => { active.delete(e.pointerId); update(); };
    pad.addEventListener('pointerup', end); pad.addEventListener('pointercancel', end);
  }

  function bindButtons() {
    for (const el of document.querySelectorAll('[data-act]')) {
      const act = el.dataset.act;
      const ids = new Set();
      el.addEventListener('pointerdown', e => { el.setPointerCapture(e.pointerId); ids.add(e.pointerId); el.classList.add('on'); setAct(act, true); e.preventDefault(); });
      const end = e => { ids.delete(e.pointerId); if (!ids.size) { el.classList.remove('on'); setAct(act, false); } };
      el.addEventListener('pointerup', end); el.addEventListener('pointercancel', end);
      el.addEventListener('contextmenu', e => e.preventDefault());
    }
    $('touch-toggle').addEventListener('click', () => document.body.classList.toggle('touch'));
  }

  window.abbildHost = {
    async boot(ref) {
      dotnet = ref;
      if (window.matchMedia('(pointer: coarse)').matches || navigator.maxTouchPoints > 0) document.body.classList.add('touch');
      window.addEventListener('touchstart', () => document.body.classList.add('touch'), { once: true, passive: true });
      try {
        await loadContent();
        await restoreSaves();
      } catch (e) {
        setProgress(0, 1, '読み込めませんでした。ページを読み込み直してください。（' + (e && e.message ? e.message : e) + '）');
        return;
      }
      setProgress(1, 1, '準備ができました');
      $('start').hidden = false;
      $('start').addEventListener('click', () => {
        $('loading').remove();
        bindPad(); bindButtons();
        // スマホ・タブレットは全画面・横向きにする（できる端末だけ）
        if (document.body.classList.contains('touch')) {
          const el = document.documentElement;
          (el.requestFullscreen ? el.requestFullscreen({ navigationUI: 'hide' }) : Promise.reject()).then(() => screen.orientation && screen.orientation.lock && screen.orientation.lock('landscape').catch(() => { })).catch(() => { });
        }
        // ゲームを作る前に、キャンバスを画面の大きさにしておく（ゲームはこの大きさで描き始める）
        const cv = $('theCanvas');
        cv.width = window.innerWidth; cv.height = window.innerHeight;
        cv.focus();
        requestAnimationFrame(tick);
        setTimeout(() => window.dispatchEvent(new Event('resize')), 50);
      }, { once: true });
    },
    storeSave(name, text) { try { localStorage.setItem(SAVE + name, text); } catch { } },
    removeSave(name) { try { localStorage.removeItem(SAVE + name); } catch { } },
    fullscreen(on) {
      try {
        if (on && !document.fullscreenElement) document.documentElement.requestFullscreen().catch(() => { });
        if (!on && document.fullscreenElement) document.exitFullscreen().catch(() => { });
      } catch { }
    },
    isFullscreen() { return !!document.fullscreenElement; },
  };

  // 矢印キー・スペース・ホイールでページが動かないように
  window.addEventListener('keydown', e => { if ([' ', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Tab'].includes(e.key)) e.preventDefault(); });
  window.addEventListener('wheel', e => e.preventDefault(), { passive: false });
})();
