(function () {
  'use strict';
  var $ = function (s, r) { return (r || document).querySelector(s); };
  var $$ = function (s, r) { return Array.prototype.slice.call((r || document).querySelectorAll(s)); };

  function closeAllDd(except) { $$('.dd.open').forEach(function (d) { if (d !== except) d.classList.remove('open'); }); }
  function openOv(ov) { ov.classList.add('show'); var f = ov.querySelector('input:not([type=hidden]),select,textarea,button[type=submit]'); if (f) setTimeout(function () { f.focus(); }, 30); }
  function closeOv(ov) {
    ov.classList.remove('show');
    if (ov.id === 'remote') {
      if (remoteAbort) remoteAbort.abort();
      ov.innerHTML = '';
      ov.removeAttribute('data-seq');
      ov.removeAttribute('aria-busy');
      ov.removeAttribute('aria-labelledby');
    }
  }
  function icon(name) { var t = document.getElementById('ic-' + name); return t ? t.innerHTML : ''; }

  // Page-level wait state. The bar never captures clicks, and a cancelled navigation clears it.
  var navToken = 0, navTimer = 0, nextLabel = '', remoteSeq = 0, remoteAbort = null;
  var navApi = !!(window.navigation && typeof window.navigation.addEventListener === 'function');
  if (!document.getElementById('pageload')) {
    var barHost = document.createElement('div');
    barHost.className = 'pageload';
    barHost.id = 'pageload';
    barHost.setAttribute('aria-hidden', 'true');
    barHost.innerHTML = '<div class="pageload-bar" aria-hidden="true"><i></i></div><div class="pageload-status" role="status" aria-live="polite"><span class="bspin" aria-hidden="true"></span><span data-pageload-text>جارٍ التحميل</span></div>';
    document.body.insertBefore(barHost, document.body.firstChild);
  }
  function showBar(label) {
    var el = $('#pageload'); if (!el) return;
    var text = el.querySelector('[data-pageload-text]');
    if (text) text.textContent = label || 'جارٍ التحميل';
    el.classList.add('on');
    el.setAttribute('aria-hidden', 'false');
    document.documentElement.classList.add('is-navload');
  }
  function hideBar() {
    var el = $('#pageload');
    if (el) { el.classList.remove('on'); el.setAttribute('aria-hidden', 'true'); }
    document.documentElement.classList.remove('is-navload');
  }
  function submitters(f) {
    return $$('button, input', f).filter(function (el) {
      if (el.tagName === 'INPUT') return el.type === 'submit' || el.type === 'image';
      return (el.getAttribute('type') || 'submit').toLowerCase() === 'submit';
    });
  }
  function releaseForm(f) {
    if (!f) return;
    f.removeAttribute('data-submitting');
    submitters(f).forEach(function (b) {
      b.classList.remove('is-busy');
      b.removeAttribute('aria-busy');
      var spin = b.querySelector('.bspin'); if (spin) spin.remove();
      if (b.getAttribute('data-was-disabled') !== '1') b.disabled = false;
      b.removeAttribute('data-was-disabled');
    });
  }
  function releaseBusy() { $$('form[data-submitting]').forEach(releaseForm); }
  function markBusy(f) {
    submitters(f).forEach(function (b) {
      if (b.classList.contains('is-busy')) return;
      if (b.disabled) b.setAttribute('data-was-disabled', '1');
      b.disabled = true;
      b.classList.add('is-busy');
      b.setAttribute('aria-busy', 'true');
      var s = document.createElement('span');
      s.className = 'bspin';
      s.setAttribute('aria-hidden', 'true');
      b.insertBefore(s, b.firstChild);
    });
  }
  function beginNav(label) {
    var token = ++navToken;
    clearTimeout(navTimer);
    showBar(label);
    // Chromium reports a cancelled navigation on the abort signal, including Stop.
    // navigation.transition stays null for a normal full-page load, so it cannot
    // tell a slow response from a cancel. Without the API, unlock only after the
    // document is still here long enough that this was not a slow navigation.
    if (!navApi) {
      navTimer = setTimeout(function () {
        if (token === navToken && document.visibilityState === 'visible') endNav(token);
      }, 30000);
    }
    return token;
  }
  function endNav(token) {
    if (token != null && token !== navToken) return;
    clearTimeout(navTimer);
    navToken++;
    hideBar();
    releaseBusy();
  }
  function isFileUrl(url) {
    try {
      var path = new URL(url, location.href).pathname;
      return /\/Exports\//i.test(path) || /\/Export$/i.test(path);
    } catch (err) { return false; }
  }
  function remoteWait(failed) {
    var title = failed ? 'تعذر التحميل' : 'جارٍ التحميل';
    var text = failed
      ? 'تعذر تحميل البيانات. أعد المحاولة أو حدّث الصفحة.'
      : 'نجهّز البيانات، لن يستغرق ذلك طويلاً.';
    var mark = failed ? icon('x') : '<span class="bspin lg" aria-hidden="true"></span>';
    var foot = failed
      ? '<button type="button" class="btn p" data-remote-retry>إعادة المحاولة</button><button type="button" class="btn" data-close>إغلاق</button>'
      : '<button type="button" class="btn" data-close>إلغاء</button>';
    var skel = failed ? '' : '<div class="remote-skel" aria-hidden="true"><i></i><i></i><i class="short"></i></div>';
    return '<div class="modal remote-wait"><div class="mh"><div class="mic ' + (failed ? 'er' : 'pr') + '" aria-hidden="true">' + mark + '</div><div><h3 id="remote-t">' + title + '</h3><p>' + text + '</p></div></div>' + skel + '<div class="mf">' + foot + '</div></div>';
  }
  function loadRemote(url) {
    var target = $('#remote');
    if (!target || !url) return;
    if (remoteAbort) remoteAbort.abort();
    var ctrl = new AbortController();
    remoteAbort = ctrl;
    var signal = ctrl.signal;
    var seq = String(++remoteSeq);
    target.setAttribute('data-seq', seq);
    target.setAttribute('data-url', url);
    target.innerHTML = remoteWait(false);
    target.setAttribute('aria-busy', 'true');
    target.setAttribute('aria-labelledby', 'remote-t');
    openOv(target);
    fetch(url, { headers: { 'X-Requested-With': 'fetch' }, credentials: 'same-origin', signal: signal })
      .then(function (r) { if (!r.ok) throw new Error(String(r.status)); return r.text(); })
      .then(function (html) {
        if (signal.aborted || target.getAttribute('data-seq') !== seq || !target.classList.contains('show')) return;
        target.innerHTML = html;
        target.removeAttribute('aria-busy');
        target.removeAttribute('aria-labelledby');
        $$('input[name=returnUrl]', target).forEach(function (i) { i.value = location.pathname + location.search; });
        var rf = target.querySelector('form');
        if (rf) syncRecount(rf);
        var focus = target.querySelector('input:not([type=hidden]):not(:disabled),select:not(:disabled),textarea');
        if (focus) setTimeout(function () { focus.focus(); }, 30);
      })
      .catch(function () {
        if (signal.aborted || target.getAttribute('data-seq') !== seq || !target.classList.contains('show')) return;
        target.innerHTML = remoteWait(true);
        target.removeAttribute('aria-busy');
        var retry = target.querySelector('[data-remote-retry]');
        if (retry) retry.focus();
      });
  }

  // Horizontal scrolling stays inside each table; the rest of the page does not move.
  $$('.card > table, .tabpane > table').forEach(function (table) {
    if (table.parentElement && table.parentElement.classList.contains('hscroll')) return;
    var box = document.createElement('div');
    box.className = 'hscroll';
    table.before(box);
    box.appendChild(table);
  });

  // Keep the account card pinned while the menu list scrolls, even before the layout markup is recompiled.
  (function () {
    var side = $('.side');
    if (!side || $('.sidenav', side)) return;
    var me = $('.me', side);
    if (!me) return;
    var items = $$('.grp, .nav', side);
    if (!items.length) return;
    var box = document.createElement('div');
    box.className = 'sidenav';
    items[0].before(box);
    items.forEach(function (el) { box.appendChild(el); });
  })();

  document.addEventListener('click', function (e) {
    var t = e.target;
    var dd = t.closest('[data-dd]');
    if (dd) {
      e.preventDefault();
      var box = dd.closest('.dd');
      var willOpen = !box.classList.contains('open');
      closeAllDd(box);
      box.classList.toggle('open', willOpen);
      if (willOpen) { var r = box.getBoundingClientRect(); box.classList.toggle('up', window.innerHeight - r.bottom < 260 && r.top > 260); }
      return;
    }
    if (!t.closest('.dd .menu')) closeAllDd();

    var cf = t.closest('[data-confirm]');
    if (cf) {
      e.preventDefault(); closeAllDd();
      var ov = $('#confirm'), form = $('#cf-form');
      form.action = cf.getAttribute('data-action');
      $('#cf-t').textContent = cf.getAttribute('data-title') || 'تأكيد';
      $('#cf-p').textContent = cf.getAttribute('data-text') || '';
      var kind = cf.getAttribute('data-kind') || 'ok';
      var ic = $('#cf-ic'); ic.className = 'mic ' + (kind === 'd' ? 'er' : kind === 'p' ? 'pr' : 'ok'); ic.innerHTML = icon(cf.getAttribute('data-icon') || 'check');
      var ok = $('#cf-ok'); ok.textContent = cf.getAttribute('data-ok') || 'تأكيد'; ok.className = 'btn ' + (kind === 'd' ? 'd' : kind === 'p' ? 'p' : 'okb'); ok.disabled = false; ok.removeAttribute('aria-busy');
      var extra = $('#cf-extra'); extra.innerHTML = '';
      var hidden = cf.getAttribute('data-hidden');
      if (hidden) { try { var o = JSON.parse(hidden); Object.keys(o).forEach(function (k) { var i = document.createElement('input'); i.type = 'hidden'; i.name = k; i.value = o[k]; extra.appendChild(i); }); } catch (_) { } }
      if (cf.getAttribute('data-reason') === '1') { var ta = document.createElement('textarea'); ta.name = 'reason'; ta.required = true; ta.maxLength = 1000; ta.placeholder = 'سبب الإرجاع أو الرفض'; ta.style.cssText = 'width:100%;min-height:72px;margin-top:12px'; extra.appendChild(ta); }
      var ru = document.createElement('input'); ru.type = 'hidden'; ru.name = 'returnUrl'; ru.value = location.pathname + location.search; extra.appendChild(ru);
      openOv(ov); return;
    }

    var retry = t.closest('[data-remote-retry]');
    if (retry) {
      e.preventDefault();
      var again = $('#remote');
      if (again && again.getAttribute('data-url')) loadRemote(again.getAttribute('data-url'));
      return;
    }

    var rm = t.closest('[data-remote]');
    if (rm) {
      e.preventDefault(); closeAllDd();
      loadRemote(rm.getAttribute('data-remote'));
      return;
    }

    var mo = t.closest('[data-modal]');
    if (mo) {
      e.preventDefault(); closeAllDd();
      var m = $(mo.getAttribute('data-modal')); var f = m.querySelector('form');
      if (f) { releaseForm(f); f.reset(); $$('.hint.e[data-dyn]', f).forEach(function (h) { h.remove(); }); }
      Array.prototype.forEach.call(mo.attributes, function (a) {
        if (a.name.indexOf('data-v-') === 0) { var key = a.name.substring(7); $$('[name]', m).forEach(function (el) { if (el.name.toLowerCase() === key && el.type !== 'hidden' || (el.type === 'hidden' && el.name.toLowerCase() === key && el.name !== '__RequestVerificationToken')) el.value = a.value; }); }
        if (a.name === 'data-title') { var ts = m.querySelector('[data-title-slot]'); if (ts) ts.textContent = a.value; }
        if (a.name === 'data-sub') { var ss = m.querySelector('[data-sub-slot]'); if (ss) ss.textContent = a.value; }
        if (a.name === 'data-form-action' && f) f.action = a.value;
      });
      openOv(m); return;
    }

    if (t.closest('[data-close]')) { e.preventDefault(); var o2 = t.closest('.ov'); if (o2) closeOv(o2); return; }
    if (t.classList && t.classList.contains('ov')) { closeOv(t); return; }

    var tab = t.closest('.tabs a[data-tab]');
    if (tab) {
      e.preventDefault();
      var bar = tab.closest('.tabs');
      $$('a', bar).forEach(function (a) { a.classList.toggle('on', a === tab); a.setAttribute('aria-selected', a === tab); });
      $$('.tabpane', bar.parentNode).forEach(function (p) { p.classList.toggle('on', p.id === tab.getAttribute('data-tab')); });
      history.replaceState(null, '', '?tab=' + tab.getAttribute('data-tab'));
    }
  });

  document.addEventListener('keydown', function (e) { if (e.key === 'Escape') { $$('.ov.show').forEach(closeOv); closeAllDd(); } });

  document.addEventListener('submit', function (e) {
    var f = e.target;
    if (!f || f.tagName !== 'FORM') return;
    if (e.defaultPrevented) return;
    if (f.getAttribute('data-submitting') === '1') { e.preventDefault(); return; }
    if (!f.noValidate && !f.checkValidity()) return;
    var method = (f.getAttribute('method') || 'get').toLowerCase();
    var download = method === 'get' && isFileUrl(f.action);
    f.setAttribute('data-submitting', '1');
    if (download) {
      setTimeout(function () { if (f.getAttribute('data-submitting') === '1') markBusy(f); }, 0);
      setTimeout(function () { releaseForm(f); }, 1600);
      return;
    }
    nextLabel = method === 'post' ? 'جارٍ الإرسال' : 'جارٍ البحث';
    setTimeout(function () {
      if (f.getAttribute('data-submitting') !== '1') return;
      if (e.defaultPrevented) { releaseForm(f); nextLabel = ''; return; }
      markBusy(f);
      if (!navApi) beginNav(nextLabel);
      nextLabel = '';
    }, 0);
  });

  // multi-select summaries + warehouse filtering by chosen branches
  function syncMsel() {
    var chosenBranches = $$('details.msel[data-kind=branches] input:checked').map(function (i) { return i.value; });
    $$('details.msel[data-kind=warehouses] label[data-parent]').forEach(function (l) {
      var show = chosenBranches.indexOf(l.getAttribute('data-parent')) >= 0;
      l.style.display = show ? '' : 'none';
      if (!show) l.querySelector('input').checked = false;
    });
    $$('details.msel').forEach(function (d) {
      var names = $$('input:checked', d).map(function (i) { return i.getAttribute('data-name'); });
      var s = d.querySelector('summary span');
      s.textContent = names.length ? names.join('، ') : s.getAttribute('data-ph');
      s.classList.toggle('muted', !names.length);
    });
    var none = $('[data-no-wh]');
    if (none) none.style.display = $$('details.msel[data-kind=warehouses] label[data-parent]').some(function (l) { return l.style.display !== 'none'; }) ? 'none' : '';
  }
  function syncRecount(form) {
    if (!form) return;
    var chosen = form.querySelector('input[name=mode]:checked:not(:disabled)');
    var box = form.querySelector('#other-user');
    if (!box) return;
    var other = !!(chosen && chosen.value === 'other');
    box.style.display = other ? '' : 'none';
    var sel = box.querySelector('select[name=userId]');
    if (sel) sel.required = other;
  }
  document.addEventListener('change', function (e) {
    if (e.target.closest('details.msel')) syncMsel();
    if (e.target.closest('input[name=mode]')) syncRecount(e.target.form);
  });
  document.addEventListener('click', function (e) {
    var opt = e.target.closest('#remote .modal .opt');
    if (!opt) return;
    var input = opt.querySelector('input[name=mode]');
    if (!input || input.disabled) return;
    input.checked = true;
    syncRecount(input.form);
  });
  document.addEventListener('toggle', function (e) { if (e.target.matches && e.target.matches('details.msel') && e.target.open) $$('details.msel[open]').forEach(function (d) { if (d !== e.target) d.open = false; }); }, true);
  document.addEventListener('click', function (e) { if (!e.target.closest('details.msel')) $$('details.msel[open]').forEach(function (d) { d.open = false; }); });
  syncMsel();

  // toasts: success fades after 4s, errors stay until closed
  $$('[data-toast]').forEach(function (t) {
    var hide = function () { t.classList.add('hide'); setTimeout(function () { t.remove(); }, 300); };
    var b = t.querySelector('[data-toast-close]'); if (b) b.addEventListener('click', hide);
    if (!t.hasAttribute('data-sticky')) setTimeout(hide, 4000);
  });

  // drag & drop file picker
  function bindDrop(z) {
    var inp = z.querySelector('input[type=file]'), fn = z.querySelector('.fn');
    var show = function () { var f = inp.files && inp.files[0]; z.classList.toggle('has', !!f); z.classList.remove('err'); if (fn) fn.textContent = f ? f.name : ''; };
    inp.addEventListener('change', show);
    ['dragenter', 'dragover'].forEach(function (ev) { z.addEventListener(ev, function (e) { e.preventDefault(); z.classList.add('over'); }); });
    ['dragleave', 'drop'].forEach(function (ev) { z.addEventListener(ev, function (e) { e.preventDefault(); z.classList.remove('over'); }); });
    z.addEventListener('drop', function (e) {
      var f = e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files[0]; if (!f) return;
      if (!/\.(xlsx|xls)$/i.test(f.name)) { z.classList.add('err'); if (fn) fn.textContent = 'يجب أن يكون الملف بصيغة xlsx أو xls'; return; }
      var dt = new DataTransfer(); dt.items.add(f); inp.files = dt.files; show();
    });
    var form = z.closest('form');
    if (form) form.addEventListener('submit', function (e) { if (!inp.files || !inp.files.length) { e.preventDefault(); e.stopImmediatePropagation(); z.classList.add('err'); if (fn) fn.textContent = 'اختر ملف Excel'; } }, true);
  }
  $$('.drop').forEach(bindDrop);
  document.addEventListener('click', function (e) { var b = e.target.closest('[data-close-after]'); if (!b) return; var ov = b.closest('.ov'); var f = b.form; if (ov && f && f.checkValidity()) setTimeout(function () { closeOv(ov); }, 300); });

  // auto-open a modal requested by the server (e.g. after validation)
  var auto = document.querySelector('[data-autoopen]'); if (auto) openOv(auto);

  var appEl = $('.app'), navbtn = $('#navbtn');
  function setNav(open) {
    if (!appEl) return;
    var was = appEl.classList.contains('navopen');
    appEl.classList.toggle('navopen', open);
    document.body.classList.toggle('navlock', open);
    if (navbtn) navbtn.setAttribute('aria-expanded', open ? 'true' : 'false');
    if (!open && was && navbtn) navbtn.focus();
  }
  if (navbtn) navbtn.addEventListener('click', function () { setNav(!appEl.classList.contains('navopen')); });
  var scrim = $('#navscrim'), navx = $('#navx');
  if (scrim) scrim.addEventListener('click', function () { setNav(false); });
  if (navx) navx.addEventListener('click', function () { setNav(false); });
  $$('.side a').forEach(function (a) { a.addEventListener('click', function () { setNav(false); }); });
  document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && appEl && appEl.classList.contains('navopen')) setNav(false); });

  window.addEventListener('pageshow', function () { endNav(); });
  if (navApi) {
    navigation.addEventListener('navigate', function (ev) {
      var same = ev.destination && ev.destination.sameDocument;
      var dest = ev.destination && ev.destination.url;
      if (ev.hashChange || same || ev.downloadRequest != null || ev.navigationType === 'traverse' || isFileUrl(dest)) {
        nextLabel = '';
        return;
      }
      var label = nextLabel || 'جارٍ التحميل';
      nextLabel = '';
      var token = beginNav(label);
      if (ev.signal) ev.signal.addEventListener('abort', function () { endNav(token); });
    });
  } else {
    document.addEventListener('click', function (e) {
      if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
      var a = e.target.closest && e.target.closest('a[href]');
      if (!a || a.hasAttribute('download')) return;
      var target = a.getAttribute('target');
      if (target && target.toLowerCase() !== '_self') return;
      if (a.hasAttribute('data-tab') || a.hasAttribute('data-remote') || a.hasAttribute('data-confirm') || a.hasAttribute('data-modal')) return;
      var href = a.getAttribute('href');
      if (!href || href.charAt(0) === '#') return;
      var url;
      try { url = new URL(a.href, location.href); } catch (err) { return; }
      if (!/^https?:$/i.test(url.protocol) || url.origin !== location.origin || isFileUrl(url.href)) return;
      if (url.pathname === location.pathname && url.search === location.search && url.hash) return;
      beginNav('جارٍ التحميل');
    });
  }
})();
