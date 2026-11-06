/* Hyundai stock interactive test scenario — offline */
(function () {
  "use strict";

  const STORAGE_KEY = "hyundai-scenario-v1";

  const ACCOUNTS = [
    { role: "مسؤول رئيسي", name: "المسؤول الرئيسي (تجريبي)", phone: "07790000000", where: "اللوحة", scope: "كل الفروع", kind: "admin" },
    { role: "موظف جرد", name: "أحمد الجرد - المنصور (تجريبي)", phone: "07790000001", where: "التطبيق", scope: "مخزني المنصور 9011/9012", kind: "mobile" },
    { role: "أدمن فرع", name: "أدمن المنصور (تجريبي)", phone: "07790000002", where: "اللوحة", scope: "المنصور", kind: "admin" },
    { role: "مدير مخزن", name: "مدير مخزن المنصور (تجريبي)", phone: "07790000003", where: "اللوحة", scope: "مخازن المنصور", kind: "admin" },
    { role: "مدير قطع", name: "مدير القطع (تجريبي)", phone: "07790000004", where: "اللوحة", scope: "المنصور", kind: "admin" },
    { role: "محاسب", name: "محاسب المنصور (تجريبي)", phone: "07790000005", where: "اللوحة", scope: "المنصور", kind: "admin" },
    { role: "سوبر أدمن", name: "سوبر أدمن (تجريبي)", phone: "07790000006", where: "اللوحة", scope: "كل الفروع", kind: "admin" },
    { role: "محاسب البصرة", name: "محاسب البصرة (تجريبي)", phone: "07790000007", where: "اللوحة", scope: "البصرة فقط", kind: "admin" },
    { role: "موظف الكرادة", name: "علي - الكرادة (تجريبي)", phone: "07790000008", where: "التطبيق", scope: "مخازن الكرادة", kind: "mobile" },
    { role: "موظف زيونة", name: "حسين - زيونة (تجريبي)", phone: "07790000009", where: "التطبيق", scope: "مخازن زيونة", kind: "mobile" },
    { role: "موظف البصرة", name: "مصطفى - البصرة (تجريبي)", phone: "07790000010", where: "التطبيق", scope: "مخازن البصرة", kind: "mobile" },
    { role: "موظف أربيل", name: "آرام - أربيل (تجريبي)", phone: "07790000011", where: "التطبيق", scope: "مخازن أربيل", kind: "mobile" }
  ];

  const PARTS = [
    { code: "58101D3A01", loc: "A-01", sys: 9, count: 10, diff: "+1", price: "85,000" },
    { code: "2435508000", loc: "B-12", sys: 6, count: 8, diff: "+2", price: "125,000" },
    { code: "230602F930", loc: "C-03", sys: 2, count: 3, diff: "+1", price: "95,000" },
    { code: "58310BWA40", loc: "D-07", sys: 8, count: 6, diff: "-2", price: "65,000" },
    { code: "S2630035505", loc: "F-02", sys: 10, count: 12, diff: "+2", price: "12,000" }
  ];

  /** @type {Array<{id:string,group:string,roles:string[],platform:string,title:string,detail:string,expect:string,images?:Array<{src:string,cap:string}>,negative?:boolean}>} */
  const STEPS = [
    {
      id: "s1", group: "setup", roles: ["مسؤول رئيسي", "الكل"], platform: "dash",
      title: "شغّل الباكند والريلي",
      detail: "على ويندوز: Development + Seed__Demo=true + Seed__DemoPassword=Test1234 على بورت 5169، والريلي شغال لنفس الوقت. لا تخلط بورت صفحة السيناريو 5180 ويا 5169.",
      expect: "اللوحة المحلية تفتح، والرابط العام ما يطلع Backend offline أو Local backend unavailable.",
      images: [{ src: "images/dash/login.png", cap: "صفحة الدخول باللوحة" }]
    },
    {
      id: "s2", group: "setup", roles: ["مسؤول رئيسي", "الكل"], platform: "dash",
      title: "تأكد إعادة التوجيه https",
      detail: "من نافذة خاصة افتح https://hyundai.cloudapps.trade/Requests بدون حساب.",
      expect: "يتحول إلى https /Account/Login?ReturnUrl=... بدون نزول http وبدون 500.",
      images: [{ src: "images/dash/login.png", cap: "Login عبر https" }]
    },
    {
      id: "s3", group: "setup", roles: ["مسؤول رئيسي"], platform: "dash",
      title: "دخول المسؤول التجريبي وجولة الصفحات",
      detail: "ادخل 07790000000 / Test1234. افتح الرئيسية، المستخدمين، المشرفين، الفروع، المخازن، الطلبات، إضافة قطعة، قطع الغيار، الفروقات، المركبات والجلسات.",
      expect: "صفحات سليمة وداتا متعددة، فروقات نقص/زيادة، أسعار غير صفرية. تصحيح VIN يظهر فقط لأدمن الفرع ومدير المخزن.",
      images: [
        { src: "images/dash/home.png", cap: "الرئيسية" },
        { src: "images/dash/users.png", cap: "المستخدمين" },
        { src: "images/dash/admins.png", cap: "المشرفين" },
        { src: "images/dash/branches.png", cap: "الفروع" },
        { src: "images/dash/warehouses.png", cap: "المخازن" },
        { src: "images/dash/sessions.png", cap: "الجلسات" }
      ]
    },
    {
      id: "s4", group: "setup", roles: ["مسؤول رئيسي", "محاسب"], platform: "dash",
      title: "اختبارات دخول سلبية",
      detail: "جرّب كلمة سر غلط، حساب معطّل، ودخول المحاسب من التطبيق.",
      expect: "رفض واضح بدون JWT. التطبيق فقط لموظفي الجرد.",
      negative: true
    },
    {
      id: "c1", group: "count", roles: ["موظف جرد"], platform: "app",
      title: "دخول أحمد واختيار مخزن 9011",
      detail: "سجّل بالتطبيق 07790000001 / Test1234. فعّل الكاميرا والموقع. اختار مخزن المنصور التجريبي 9011 (warehouseId من /api/v1/me مو الرقم نفسه).",
      expect: "دخول ناجح، صلاحية موقع وكاميرا، المخزن ظاهر.",
      images: [{ src: "images/mobile/login.svg", cap: "شاشة دخول التطبيق" }]
    },
    {
      id: "c2", group: "count", roles: ["موظف جرد"], platform: "app",
      title: "امسح القطع الخمسة واحفظ العدّ",
      detail: "افتح docs/test-barcodes.html أو بطاقات الطباعة. كبّر الشاشة وامسح الأكواد بالمواقع والكميات المحددة بالجدول.",
      expect: "بعد كل حفظ: نفس الكمية والموظف والمخزن والموقع والوقت باللوحة والتطبيق. مجموع فروقات الخمسة = 324,000 د.ع.",
      images: [
        { src: "images/mobile/scan-part.svg", cap: "مسح باركود القطعة" },
        { src: "images/mobile/history.svg", cap: "سجلي بعد الحفظ" },
        { src: "images/dash/parts.png", cap: "قطع الغيار باللوحة" }
      ]
    },
    {
      id: "c3", group: "count", roles: ["موظف جرد"], platform: "app",
      title: "رفض GPS 0,0",
      detail: "جرّب حفظ عدّ بإحداثيات 0,0 (عبر API على قاعدة التجربة، لا تزور GPS على الجهاز الحقيقي إلا للسالب الموثّق).",
      expect: "400 ورسالة عربية، بدون حفظ.",
      negative: true,
      images: [{ src: "images/mobile/gps-reject.svg", cap: "رفض الإحداثيات" }]
    },
    {
      id: "r1", group: "requests", roles: ["مدير مخزن", "موظف جرد"], platform: "dash",
      title: "طلب إعادة عد من مدير المخزن",
      detail: "مدير المخزن 07790000003 يفتح تفاصيل 58101D3A01 بمخزن 9011 ويطلب إعادة عد لأحمد. المحاسب يحاول يطلب إعادة عد.",
      expect: "الطلب يظهر بالتطبيق/إعادة العد. المحاسب ما يقدر يطلب إعادة عد.",
      images: [
        { src: "images/dash/recount.png", cap: "مودال إعادة العد" },
        { src: "images/mobile/recount.svg", cap: "إعادة العد بالتطبيق" }
      ]
    },
    {
      id: "r2", group: "requests", roles: ["موظف جرد"], platform: "app",
      title: "أحمد يكمل إعادة العد",
      detail: "أحمد يعدّها 10 بنفس الموقع وبـ GPS صحيح.",
      expect: "إعادة العد مكتملة وسجل القطعة محافظ على التاريخ."
    },
    {
      id: "r3", group: "requests", roles: ["موظف جرد", "مدير مخزن", "محاسب"], platform: "app",
      title: "طلب تعديل كمية 10→11",
      detail: "أحمد يرسل تعديل كمية لـ 58101D3A01 من 10 إلى 11 بسبب «كارتون إضافي بعد المطابقة». المحاسب يحاول يوافق قبل المدير، بعدها المدير يوافق، ثم محاسب المنصور يوافق نهائياً.",
      expect: "محاولة المحاسب الأولى تُرفض. بعد المدير: بانتظار المالية والكمية تبقى 10. بعد المحاسب: مقبول، الكمية 11، فرق +2 = 170,000 د.ع.",
      images: [
        { src: "images/dash/qty-request.png", cap: "طلب تعديل كمية" },
        { src: "images/dash/requests.png", cap: "قائمة الطلبات" },
        { src: "images/mobile/requests.svg", cap: "طلباتي بالتطبيق" }
      ]
    },
    {
      id: "r4", group: "requests", roles: ["محاسب"], platform: "dash",
      title: "قاعدة مشغولة عند الموافقة النهائية",
      detail: "حدّث الصفحة وأعد نفس الموافقة من تبويب ثانٍ. إذا SQLite مشغولة راقب الرسالة.",
      expect: "ماكو تعديل مزدوج. إذا مشغولة تظهر: «قاعدة البيانات مشغولة، حدّث حالة الطلب قبل إعادة المحاولة» بدل انتظار طويل.",
      negative: true
    },
    {
      id: "r5", group: "requests", roles: ["موظف جرد", "مدير قطع", "محاسب"], platform: "app",
      title: "قطعة غير موجودة QA2630035506",
      detail: "أحمد يرسل طلب إضافة: وصف «جلدة باب تجريبية / Door seal»، كمية 2، موقع QA-Z-01، GPS حقيقي. مدير القطع يوافق ثم المحاسب يثبت. طلب ثاني: المدير يرجعه وأحمد يلغيه.",
      expect: "ما تنخلق قطعة قبل الموافقات. بعد التثبيت: كمية نظام 0 ومعدودة 2 وسعر 0. الراجع ثم الملغي ما يضيف قطعة. تكرار رمز مفتوح = 409.",
      images: [{ src: "images/dash/requests.png", cap: "طلبات الإضافة" }]
    },
    {
      id: "v1", group: "cars", roles: ["موظف جرد"], platform: "app",
      title: "مسح ستيكر الزجاج (OCR)",
      detail: "اختار مخزن مركبات 9012. صوّر السطر النصي تحت HYUNDAI (مو Code128). جرّب تكبير وإضاءة وأفقي/عمودي.",
      expect: "VIN 17 خانة، نوع Elantra/Tucson 2025، حفظ مع المستخدم والمخزن والموقع، ونفسه باللوحة والتطبيق.",
      images: [
        { src: "images/mobile/scan-vin.svg", cap: "مسح VIN" },
        { src: "images/dash/cars.png", cap: "المركبات باللوحة" }
      ]
    },
    {
      id: "v2", group: "cars", roles: ["موظف جرد"], platform: "app",
      title: "تكرار VIN وأحرف ممنوعة",
      detail: "امسح نفس الرقم مرة ثانية. جرّب O/I/Q أو طول 16/18.",
      expect: "التكرار: 409 «رقم VIN ممسوح مسبقًا». الأحرف/الطول: 400.",
      negative: true
    },
    {
      id: "v3", group: "cars", roles: ["موظف جرد", "أدمن فرع", "محاسب"], platform: "app",
      title: "تصحيح VIN",
      detail: "من التطبيق اطلب تصحيح VIN لرقم صالح غير ممسوح. أدمن المنصور يوافق، وطلب ثاني يرفض. المحاسب يحاول يصحح.",
      expect: "المقبول يغيّر VIN. المرفوض ما يغيّره. المحاسب بلا صلاحية تصحيح VIN.",
      images: [{ src: "images/dash/vin-requests.png", cap: "طلبات تصحيح VIN" }]
    },
    {
      id: "n1", group: "negative", roles: ["الكل"], platform: "app",
      title: "إحداثيات خارج المدى ومخزن غلط",
      detail: "Latitude=91/-91، Longitude=181/-181، موقع ناقص، أحمد يرسل مخزن البصرة بعملية المنصور، محاسب البصرة يلمس طلب المنصور.",
      expect: "400/403 حسب الحالة، بدون حفظ أو تسريب نطاق.",
      negative: true
    },
    {
      id: "n2", group: "negative", roles: ["مسؤول رئيسي"], platform: "dash",
      title: "تكرار هاتف وحقول باطلة",
      detail: "إنشاء موظف/مشرف بنفس رقم موجود، كمية سالبة، HTML بطلب إضافة، CSV بـ BranchNo غلط.",
      expect: "رسائل تكرار/تحقق واضحة، استيراد مرفوض بدون جلسة جزئية.",
      negative: true
    },
    {
      id: "x1", group: "close", roles: ["مسؤول رئيسي", "سوبر أدمن"], platform: "dash",
      title: "مراجعة الفروقات وإنهاء الجلسة التجريبية",
      detail: "نزّل تقرير الفروقات، راجع الصفوف الخمسة والقيمة. المسؤول/السوبر ينهي الجلسة الحية التجريبية فقط.",
      expect: "مكتمل، التقارير تبقى، الطلبات المفتوحة تنغلق، sessions/current = null، عد/VIN جديد = 409. إعادة seed ما تعيد فتح الجلسة.",
      images: [
        { src: "images/dash/session-end.png", cap: "تأكيد إنهاء الجلسة" },
        { src: "images/dash/parts.png", cap: "الفروقات/القطع" }
      ]
    }
  ];

  const GROUPS = [
    { id: "setup", title: "1. تجهيز اللوحة والرابط" },
    { id: "count", title: "2. جرد القطع من التطبيق" },
    { id: "requests", title: "3. إعادة عد · تعديل كمية · قطعة جديدة" },
    { id: "cars", title: "4. جرد السيارات / ستيكر الزجاج" },
    { id: "negative", title: "5. اختبارات سلبية لازم توثّقها" },
    { id: "close", title: "6. الفروقات وإنهاء الجلسة" }
  ];



  const FLOWS = [
    {
      id: "f-newpart", title: "1) طلب إضافة قطعة للجرد", exists: true,
      img: ["images/mobile/requests.svg", "images/mobile/resubmit-newpart.svg", "images/dash/newparts.png", "images/dash/parts.png"],
      body: `أحمد POST /api/v1/new-part-requests (مثال QA2630035506) → مدير القطع يوافق → محاسب يثبّت → قطعة بنظام 0 ومعدودة 2 وسعر 0.
جديد PR#6: إذا اترجع الطلب، أحمد PUT /api/v1/new-part-requests/{id} «تعديل وإعادة إرسال» بنفس الرقم؛ نفس id ويرجع بانتظار مدير القطع.`
    },
    {
      id: "f-spare", title: "2) طلبات قطع الغيار", exists: "partial",
      img: ["images/dash/newparts.png"],
      body: `هي منيو «طلبات إضافة قطعة» /NewParts فقط. مدير القطع ما يشوف /Parts ولا /Requests. عينات DEMO-NEW-1..4. شاشة ثانية بنفس الاسم: غير موجود حالياً.`
    },
    {
      id: "f-diff", title: "3) فروقات المخزون", exists: true,
      img: ["images/dash/parts.png", "images/mobile/history.svg"],
      body: `بعد العد تظهر بـ /Parts. 58101D3A01: 9→10 = +1 × 85000. مجموع الخمس قطع = 324,000 د.ع.`
    },
    {
      id: "f-finance", title: "4) المالية", exists: true,
      img: ["images/dash/finance.png"],
      body: `/Finance: القيمة = (معدودة − نظام) × السعر. بعد قبول تعديل لـ11: فرق +2 = 170,000. تصدير Financial-Variance.`
    },
    {
      id: "f-qty", title: "5) تعديل الكمية (+ تعديل الطلب)", exists: true,
      img: ["images/mobile/edit-qty.svg", "images/dash/requests.png", "images/dash/requests-wm.png"],
      body: `POST qty-requests ثم قبل الموافقة: PUT qty-requests/{id} من التطبيق (زر تعديل الطلب) أو POST /Requests/Edit من مدير المخزن.
بعد موافقة المدير يختفي زر التعديل (409). محاسب يكمل القبول النهائي.`
    },
    {
      id: "f-recount", title: "6) إعادة الجرد", exists: true,
      img: ["images/dash/recount.png", "images/mobile/recount.svg", "images/mobile/recount-done.svg"],
      body: `مدير المخزن يطلب إعادة عد. أحمد يرسل النتيجة.
جديد: GET recounts?includeCompleted=true يعرض «مكتمل» والكمية بدون إعادة إرسال. الافتراضي يبقى المعلّق فقط.`
    },
    {
      id: "f-correct", title: "7) تصحيح البيانات (VIN)", exists: true,
      img: ["images/mobile/vin-correction.svg", "images/dash/vin-requests.png", "images/dash/cars.png"],
      body: `تصحيح VIN فقط (مو اسم/وصف عام). أدمن الفرع 07790000002 أو مدير مخزن يوافق/يرفض من /VinRequests؛ الحالة والرقم يرجعان للتطبيق بعد السحب للتحديث.`
    },
    {
      id: "f-vin", title: "8) مسح VIN", exists: true,
      img: ["images/mobile/scan-vin.svg", "images/dash/cars.png"],
      body: `مخزن 9012، OCR نص، GPS مطلوب. تكرار 409. I/O/Q أو طول ≠17 → 400.`
    },
    {
      id: "f-gps", title: "9) رفض GPS", exists: true,
      img: ["images/mobile/gps-reject.svg"],
      body: `0,0 أو خارج المدى → 400 «تعذر تحديد موقعك، يرجى المحاولة مرة أخرى» بدون حفظ.`
    },
    {
      id: "f-ui", title: "10) واجهة محدّثة", exists: true,
      img: ["images/dash/home.png", "images/dash/warehouses.png"],
      body: `الرئيسية بدون قسم «قريباً». المخازن بدون عمود/حقل النوع (اسم + رقم + فرع فقط). التطبيق يعرض اسم المخزن بدون نوع 1/2.`
    }
  ];


  const ROLES = ["الكل", "مسؤول رئيسي", "موظف جرد", "أدمن فرع", "مدير مخزن", "مدير قطع", "محاسب", "سوبر أدمن"];

  function loadState() {
    try { return JSON.parse(localStorage.getItem(STORAGE_KEY) || "{}"); }
    catch { return {}; }
  }
  function saveState(state) {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
  }

  const state = Object.assign({ done: {}, result: {}, notes: {}, filter: "الكل" }, loadState());

  const el = {
    progressLabel: document.getElementById("progress-label"),
    progressBar: document.getElementById("progress-bar"),
    stepsRoot: document.getElementById("steps"),
    accountsBody: document.getElementById("accounts-body"),
    partsBody: document.getElementById("parts-body"),
    filters: document.getElementById("role-filters"),
    accountFilters: document.getElementById("account-filters"),
    toast: document.getElementById("toast"),
    lightbox: document.getElementById("lightbox"),
    lightboxMedia: document.getElementById("lightbox-media"),
    statsDone: document.getElementById("stat-done"),
    statsPass: document.getElementById("stat-pass"),
    statsFail: document.getElementById("stat-fail"),
    statsLeft: document.getElementById("stat-left")
  };

  function toast(msg) {
    el.toast.hidden = false;
    el.toast.textContent = msg;
    clearTimeout(toast._t);
    toast._t = setTimeout(() => { el.toast.hidden = true; }, 1800);
  }

  function copyText(text) {
    navigator.clipboard.writeText(text).then(() => toast("تم النسخ: " + text)).catch(() => {
      const ta = document.createElement("textarea");
      ta.value = text; document.body.appendChild(ta); ta.select();
      document.execCommand("copy"); ta.remove();
      toast("تم النسخ: " + text);
    });
  }

  function visibleSteps() {
    const f = state.filter || "الكل";
    if (f === "الكل") return STEPS;
    return STEPS.filter(s => s.roles.includes(f) || s.roles.includes("الكل"));
  }

  function updateProgress() {
    const all = STEPS;
    const doneCount = all.filter(s => state.done[s.id]).length;
    const passCount = all.filter(s => state.result[s.id] === "pass").length;
    const failCount = all.filter(s => state.result[s.id] === "fail").length;
    el.progressLabel.textContent = doneCount + " من " + all.length;
    el.progressBar.style.width = (all.length ? (doneCount / all.length) * 100 : 0) + "%";
    el.statsDone.textContent = String(doneCount);
    el.statsPass.textContent = String(passCount);
    el.statsFail.textContent = String(failCount);
    el.statsLeft.textContent = String(all.length - doneCount);
  }

  function renderAccounts(kind) {
    const rows = ACCOUNTS.filter(a => !kind || kind === "all" || a.kind === kind);
    el.accountsBody.innerHTML = rows.map(a => `
      <tr data-kind="${a.kind}">
        <td>${a.role}</td>
        <td>${a.name}</td>
        <td>
          <button type="button" class="copy" data-copy="${a.phone}">${a.phone}</button>
          <span class="sub">Test1234</span>
        </td>
        <td>${a.where}</td>
        <td>${a.scope}</td>
      </tr>`).join("");
  }

  function renderParts() {
    el.partsBody.innerHTML = PARTS.map(p => `
      <tr>
        <td><button type="button" class="copy" data-copy="${p.code}">${p.code}</button></td>
        <td class="mono">${p.loc}</td>
        <td>${p.sys}</td>
        <td>${p.count}</td>
        <td>${p.diff}</td>
        <td>${p.price}</td>
      </tr>`).join("");
  }

  function platformBadge(p) {
    if (p === "dash") return '<span class="badge dash">لوحة التحكم</span>';
    if (p === "app") return '<span class="badge app">تطبيق الموبايل</span>';
    return '<span class="badge">الاثنين</span>';
  }

  function renderSteps() {
    const vis = new Set(visibleSteps().map(s => s.id));
    el.stepsRoot.innerHTML = GROUPS.map(g => {
      const items = STEPS.filter(s => s.group === g.id);
      const shown = items.filter(s => vis.has(s.id));
      if (!shown.length) return "";
      return `<article class="group" data-group="${g.id}">
        <h3>${g.title}</h3>
        <ol class="steps">${shown.map(stepHtml).join("")}</ol>
      </article>`;
    }).join("");
  }

  function stepHtml(s) {
    const done = !!state.done[s.id];
    const res = state.result[s.id] || "";
    const notes = state.notes[s.id] || "";
    const cls = ["step", done ? "done" : "", res === "fail" ? "failed" : ""].filter(Boolean).join(" ");
    const imgs = (s.images || []).map(img => {
      const isSvg = img.src.endsWith(".svg");
      const media = isSvg
        ? `<object data="${img.src}" type="image/svg+xml" aria-label="${img.cap}"></object>`
        : `<img src="${img.src}" alt="${img.cap}" loading="lazy" data-full="${img.src}">`;
      return `<figure>${media}<figcaption>${img.cap}</figcaption></figure>`;
    }).join("");
    return `<li class="${cls}" data-id="${s.id}">
      <div class="step-top">
        <input class="step-check" type="checkbox" ${done ? "checked" : ""} aria-label="أنجزت الخطوة">
        <div class="step-body">
          <div class="step-meta">
            ${platformBadge(s.platform)}
            ${s.negative ? '<span class="badge neg">اختبار سلبي</span>' : ""}
            ${s.roles.filter(r => r !== "الكل").map(r => `<span class="badge">${r}</span>`).join("")}
          </div>
          <h4>${s.title}</h4>
          <p>${s.detail}</p>
          <div class="expect"><strong>المتوقع:</strong> ${s.expect}</div>
          ${imgs ? `<div class="media">${imgs}</div>` : ""}
          <div class="step-actions">
            <div class="result-btns">
              <button type="button" class="btn sm pass" data-result="pass" aria-pressed="${res === "pass"}">ناجح</button>
              <button type="button" class="btn sm fail" data-result="fail" aria-pressed="${res === "fail"}">فاشل</button>
            </div>
          </div>
          <label class="muted" style="display:block;margin-top:8px">ملاحظات
            <textarea class="notes" placeholder="اكتب النتيجة الفعلية، الجهاز، نسخة APK، رقم الطلب...">${notes.replace(/</g, "&lt;")}</textarea>
          </label>
        </div>
      </div>
    </li>`;
  }


  function renderFlows() {
    const root = document.getElementById("flows-root");
    const jumps = document.getElementById("flow-jumps");
    if (!root || !jumps) return;
    jumps.innerHTML = FLOWS.map(f => `<a class="chip" href="#${f.id}">${f.title.replace(/^\d+\)\s*/, "")}</a>`).join("");
    root.innerHTML = FLOWS.map(f => {
      const tag = f.exists === true ? '<span class="badge app">موجود بالكود</span>'
        : f.exists === "partial" ? '<span class="badge neg">جزئي / توضيح اسم</span>'
        : '<span class="badge neg">غير موجود حالياً</span>';
      const imgs = (f.img || []).map(src => {
        const isSvg = src.endsWith(".svg");
        const media = isSvg ? `<object data="${src}" type="image/svg+xml"></object>` : `<img src="${src}" alt="" loading="lazy" data-full="${src}">`;
        return `<figure>${media}</figure>`;
      }).join("");
      return `<article class="group" id="${f.id}" style="margin-top:18px">
        <div class="step">
          <div class="step-meta">${tag}</div>
          <h4>${f.title}</h4>
          <pre style="white-space:pre-wrap;font-family:inherit;margin:8px 0;background:#F8FBFF;border:1px solid #D9E8F7;border-radius:12px;padding:12px">${f.body.replace(/</g,"&lt;")}</pre>
          <div class="media">${imgs}</div>
        </div>
      </article>`;
    }).join("");
  }

  function renderFilters() {
    el.filters.innerHTML = ROLES.map(r =>
      `<button type="button" class="chip ${state.filter === r ? "active" : ""}" data-role="${r}">${r}</button>`
    ).join("");
  }

  function persist() {
    saveState({
      done: state.done,
      result: state.result,
      notes: state.notes,
      filter: state.filter
    });
    updateProgress();
  }

  function exportJson() {
    const payload = {
      exportedAt: new Date().toISOString(),
      timezoneHint: "Europe/Budapest",
      passwordNote: "Demo password is Test1234 — do not put secrets here",
      progress: {
        done: Object.keys(state.done).filter(k => state.done[k]).length,
        total: STEPS.length
      },
      steps: STEPS.map(s => ({
        id: s.id,
        title: s.title,
        group: s.group,
        done: !!state.done[s.id],
        result: state.result[s.id] || null,
        notes: state.notes[s.id] || "",
        expect: s.expect
      }))
    };
    const blob = new Blob([JSON.stringify(payload, null, 2)], { type: "application/json" });
    const a = document.createElement("a");
    a.href = URL.createObjectURL(blob);
    a.download = "hyundai-scenario-results.json";
    a.click();
    URL.revokeObjectURL(a.href);
    toast("تم تصدير النتائج");
  }

  document.addEventListener("click", (e) => {
    const t = e.target;
    if (!(t instanceof Element)) return;

    const copyBtn = t.closest("[data-copy]");
    if (copyBtn) { copyText(copyBtn.getAttribute("data-copy")); return; }

    const chip = t.closest(".chip[data-role]");
    if (chip) {
      state.filter = chip.getAttribute("data-role");
      persist(); renderFilters(); renderSteps();
      return;
    }

    const achip = t.closest(".chip[data-akind]");
    if (achip) {
      document.querySelectorAll(".chip[data-akind]").forEach(c => c.classList.remove("active"));
      achip.classList.add("active");
      renderAccounts(achip.getAttribute("data-akind"));
      return;
    }

    if (t.id === "reset-progress") {
      if (!confirm("تمسح كل العلامات والملاحظات؟")) return;
      state.done = {}; state.result = {}; state.notes = {};
      persist(); renderSteps(); toast("تم المسح");
      return;
    }
    if (t.id === "export-json") { exportJson(); return; }
    if (t.id === "print-page") { window.print(); return; }

    const step = t.closest(".step");
    if (step) {
      const id = step.getAttribute("data-id");
      if (t.classList.contains("step-check")) {
        state.done[id] = t.checked;
        if (!t.checked) { /* keep result/notes */ }
        persist();
        step.classList.toggle("done", t.checked);
        return;
      }
      const rb = t.closest("[data-result]");
      if (rb) {
        const val = rb.getAttribute("data-result");
        state.result[id] = state.result[id] === val ? "" : val;
        if (state.result[id]) state.done[id] = true;
        persist();
        renderSteps();
        return;
      }
      const img = t.closest("img[data-full]");
      if (img) {
        el.lightboxMedia.innerHTML = `<img src="${img.getAttribute("data-full")}" alt="">`;
        el.lightbox.classList.add("open");
        return;
      }
    }

    if (t.id === "lightbox-close" || t.id === "lightbox") {
      el.lightbox.classList.remove("open");
    }
  });

  document.addEventListener("input", (e) => {
    const t = e.target;
    if (!(t instanceof HTMLTextAreaElement) || !t.classList.contains("notes")) return;
    const step = t.closest(".step");
    if (!step) return;
    state.notes[step.getAttribute("data-id")] = t.value;
    persist();
  });

  renderFilters();
  renderAccounts("all");
  renderParts();
  renderSteps();
  renderFlows();
  updateProgress();
})();
