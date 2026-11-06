(function () {
  'use strict';
  var $ = function (s, r) { return (r || document).querySelector(s); };
  var $$ = function (s, r) { return Array.prototype.slice.call((r || document).querySelectorAll(s)); };

  function closeAllDd(except) { $$('.dd.open').forEach(function (d) { if (d !== except) d.classList.remove('open'); }); }
  function openOv(ov) { ov.classList.add('show'); var f = ov.querySelector('input:not([type=hidden]),select,textarea,button[type=submit]'); if (f) setTimeout(function () { f.focus(); }, 30); }
  function closeOv(ov) { ov.classList.remove('show'); if (ov.id === 'remote') ov.innerHTML = ''; }
  function icon(name) { var t = document.getElementById('ic-' + name); return t ? t.innerHTML : ''; }

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
      var ok = $('#cf-ok'); ok.textContent = cf.getAttribute('data-ok') || 'تأكيد'; ok.className = 'btn ' + (kind === 'd' ? 'd' : kind === 'p' ? 'p' : 'okb'); ok.disabled = false;
      var extra = $('#cf-extra'); extra.innerHTML = '';
      var hidden = cf.getAttribute('data-hidden');
      if (hidden) { try { var o = JSON.parse(hidden); Object.keys(o).forEach(function (k) { var i = document.createElement('input'); i.type = 'hidden'; i.name = k; i.value = o[k]; extra.appendChild(i); }); } catch (_) { } }
      if (cf.getAttribute('data-reason') === '1') { var ta = document.createElement('textarea'); ta.name = 'reason'; ta.required = true; ta.maxLength = 1000; ta.placeholder = 'سبب الإرجاع أو الرفض'; ta.style.cssText = 'width:100%;min-height:72px;margin-top:12px'; extra.appendChild(ta); }
      var ru = document.createElement('input'); ru.type = 'hidden'; ru.name = 'returnUrl'; ru.value = location.pathname + location.search; extra.appendChild(ru);
      openOv(ov); return;
    }

    var rm = t.closest('[data-remote]');
    if (rm) {
      e.preventDefault(); closeAllDd();
      var target = $('#remote');
      fetch(rm.getAttribute('data-remote'), { headers: { 'X-Requested-With': 'fetch' }, credentials: 'same-origin' })
        .then(function (r) { if (!r.ok) throw new Error(r.status); return r.text(); })
        .then(function (html) { target.innerHTML = html; $$('input[name=returnUrl]', target).forEach(function (i) { i.value = location.pathname + location.search; }); openOv(target); })
        .catch(function () { alert('تعذر تحميل البيانات، حدّث الصفحة وحاول مرة أخرى'); });
      return;
    }

    var mo = t.closest('[data-modal]');
    if (mo) {
      e.preventDefault(); closeAllDd();
      var m = $(mo.getAttribute('data-modal')); var f = m.querySelector('form');
      if (f) { f.reset(); $$('.hint.e[data-dyn]', f).forEach(function (h) { h.remove(); }); }
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
    if (f.method && f.method.toLowerCase() === 'post') {
      if (!f.checkValidity()) return;
      setTimeout(function () { $$('button[type=submit]', f).forEach(function (b) { b.disabled = true; }); }, 0);
    }
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
  document.addEventListener('change', function (e) {
    if (e.target.closest('details.msel')) syncMsel();
    var rmode = e.target.closest('[name=mode]');
    if (rmode) { var box = document.getElementById('other-user'); if (box) box.style.display = rmode.value === 'other' && rmode.checked ? '' : 'none'; var sel = box && box.querySelector('select'); if (sel) sel.required = rmode.value === 'other'; }
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
})();
