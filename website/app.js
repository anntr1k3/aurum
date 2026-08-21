const featureContent = {
  msi: {
    title: "Аппаратные прерывания (MSI Mode)",
    copy: "Переводит видеокарту, сеть, звук и накопители в режим Message Signaled Interrupts. Высокий приоритет для GPU и сети устраняет очереди IRQ и снижает DPC Latency.",
    result: "Снижение задержки ввода и микрофризов в играх",
    rows: [["NVIDIA RTX GPU", "MSI High Priority"], ["Realtek Gaming 2.5GbE", "MSI High Priority"], ["USB xHCI Controller", "MSI Normal Priority"]],
    count: "1-клик пресет"
  },
  timer: {
    title: "Системный таймер 0.500 мс (2000 Гц)",
    copy: "Нативное управление через ntdll.dll. Повышает точность опроса до 2000 Гц для синхронизации игрового движка с высокоскоростными мышами (1000–8000 Гц).",
    result: "Мгновенное переключение без перезагрузки",
    rows: [["Текущее разрешение", "0.500 мс (2000 Гц)"], ["Глобальный запрос", "Win11 Enabled"], ["Штатный таймер", "15.625 мс"]],
    count: "Native API"
  },
  tweaks: {
    title: "24 гранулярных твика ядра и системы",
    copy: "Win32PrioritySeparation 0x26 для приоритета игр, DisablePagingExecutive для RAM retention, тонкая настройка Defender, UAC и фонового Windows Update с оценкой рисков.",
    result: "Исходное состояние всегда сохраняется",
    rows: [["Win32PrioritySeparation", "0x26 (Quantum Boost)"], ["DisablePagingExecutive", "1 (RAM Kernel)"], ["Defender / UAC / VSS", "8 категорий (24 твика)"]],
    count: "24 параметра"
  },
  power: {
    title: "Core Parking и управление питанием",
    copy: "Распарковывает 100% ядер процессора в отдельной схеме питания Aurum без изменения системных профилей Windows. Переключение без конфликтов.",
    result: "Мгновенная реакция всех ядер CPU",
    rows: [["AC (От сети)", "100% активных ядер"], ["DC (От батареи)", "100% активных ядер"], ["План Aurum", "Изолированный клон"]],
    count: "100% Unpark"
  },
  network: {
    title: "Оптимизация сети и быстрый DNS",
    copy: "1-клик DNS профили: Cloudflare (1.1.1.1), Google (8.8.8.8), Quad9 (9.9.9.9), AdGuard (94.140.14.14) и сброс DHCP. Очистка кэша DNS и нативный 4-точечный ICMP пинг.",
    result: "Снижение задержек и защита от рекламы",
    rows: [["Cloudflare / Google / Quad9", "1-клик переключение"], ["Сброс кэша", "Flush DNS (ipconfig)"], ["TCP Auto-Tuning", "Normal / Disabled"]],
    count: "5 DNS пресетов"
  },
  storage: {
    title: "Глубокая оптимизация SSD и накопителей",
    copy: "Отключение legacy-имен NTFS 8.3 (защита MFT), отключение меток LastAccess для продления ресурса ячеек SSD, управление гибернацией (минус 8–32 ГБ) и ReTrim.",
    result: "Защита ресурса SSD и освобождение места",
    rows: [["NTFS 8.3 Names", "Disabled (Zero MFT Bloat)"], ["LastAccess Updates", "Disabled (SSD Wear Protection)"], ["Hibernation Manager", "Свободно +8...32 GB"]],
    count: "SSD Wear Guard"
  },
  cleanup: {
    title: "Двухфазная безопасная очистка диска",
    copy: "Очистка кэша шейдеров DirectX/Vulkan, дампов сбоев и временных файлов. Сканирование формирует неизменяемый список с проверкой путей по белому списку (защита TOCTOU).",
    result: "Безопасное освобождение памяти без сноса папок",
    rows: [["Shader Cache (DX/VK)", "Кандидаты проверены"], ["User Temp Files", "Allowlist подтвержден"], ["Race Condition (TOCTOU)", "Аппаратная защита"]],
    count: "2-Phase Safe"
  },
  services: {
    title: "6 безопасных пресетов служб Windows",
    copy: "Нативное управление через Win32 SCM и построение обратного дерева зависимостей в RAM. 6 групп: Telemetry, Xbox, Print, Maps/Location, Touch, Insider.",
    result: "0 риска поломки ОС при оптимизации",
    rows: [["Telemetry & WerSvc", "6 безопасных групп"], ["Критические службы ядра", "Protected (Заблокировано)"], ["Зависимости (SCM Graph)", "Построено в RAM"]],
    count: "6 SCM пресетов"
  },
  atlas: {
    title: "Офлайн-контроль целостности AtlasOS",
    copy: "Автономный аудит структуры каталогов AtlasModules и сверка SHA-256 хэшей официальных компонентов без подключения к сети и выполнения сторонних скриптов.",
    result: "Мгновенный аудит целостности системы",
    rows: [["AtlasModules & Desktop", "Структура верифицирована"], ["SHA-256 хэши утилит", "Офлайн сверка с эталоном"], ["Выполнение скриптов", "0 внешних вызовов"]],
    count: "SHA-256 Audit"
  },
  monitoring: {
    title: "Мониторинг без фоновой нагрузки",
    copy: "Загрузка процессора, видеокарты, оперативной памяти и дисков. Нативные счетчики обновляются только пока открыт раздел, не нагружая ПК во время игры.",
    result: "Нулевое влияние на FPS",
    rows: [["CPU Usage & Sparklines", "Нативные счетчики"], ["GPU Load & VRAM", "DirectX/WDDM"], ["RAM & Disk Activity", "GlobalMemoryStatusEx"]],
    count: "0% CPU в фоне"
  }
};

const menuButton = document.querySelector(".menu-button");
const siteNav = document.querySelector(".site-nav");

menuButton?.addEventListener("click", () => {
  const isOpen = menuButton.getAttribute("aria-expanded") === "true";
  menuButton.setAttribute("aria-expanded", String(!isOpen));
  siteNav.classList.toggle("is-open", !isOpen);
});

siteNav?.addEventListener("click", (event) => {
  if (event.target.closest("a")) {
    menuButton?.setAttribute("aria-expanded", "false");
    siteNav.classList.remove("is-open");
  }
});

const detail = document.querySelector("#feature-detail");
const featureTabs = [...document.querySelectorAll(".feature-tab")];

function selectFeature(key) {
  const item = featureContent[key];
  if (!item || !detail) return;

  featureTabs.forEach((tab) => {
    const selected = tab.dataset.feature === key;
    tab.classList.toggle("is-active", selected);
    tab.setAttribute("aria-selected", String(selected));
  });

  detail.querySelector("[data-detail-title]").textContent = item.title;
  detail.querySelector("[data-detail-copy]").textContent = item.copy;
  detail.querySelector("[data-detail-result]").textContent = item.result;
  detail.querySelector(".audit-head strong").textContent = item.count;
  detail.querySelectorAll(".audit-line").forEach((row, index) => {
    if (item.rows[index]) {
      row.querySelector("code").textContent = item.rows[index][0];
      row.querySelector("span").textContent = item.rows[index][1];
    }
  });

  detail.animate(
    [{ opacity: .35, transform: "translateY(5px)" }, { opacity: 1, transform: "translateY(0)" }],
    { duration: 260, easing: "cubic-bezier(.22, 1, .36, 1)" }
  );
}

featureTabs.forEach((tab) => tab.addEventListener("click", () => selectFeature(tab.dataset.feature)));

const repositoryUrl = document.documentElement.dataset.repositoryUrl;
if (repositoryUrl) {
  document.querySelectorAll(".source-link").forEach((link) => link.setAttribute("href", repositoryUrl));
}

const scanButton = document.querySelector("[data-scan-button]");
scanButton?.addEventListener("click", () => {
  if (scanButton.classList.contains("is-scanning")) return;
  scanButton.classList.add("is-scanning");
  scanButton.querySelector("span").textContent = "Проверяем систему…";
  window.setTimeout(() => {
    scanButton.classList.remove("is-scanning");
    scanButton.querySelector("span").textContent = "38/38 проверок пройдено";
  }, 950);
});

/* FAQ Accordion Toggle */
const faqTriggers = document.querySelectorAll(".faq-trigger");
faqTriggers.forEach((trigger) => {
  trigger.addEventListener("click", () => {
    const item = trigger.closest(".faq-item");
    const isExpanded = trigger.getAttribute("aria-expanded") === "true";
    
    faqTriggers.forEach((t) => {
      if (t !== trigger) {
        t.setAttribute("aria-expanded", "false");
        t.closest(".faq-item")?.classList.remove("is-open");
      }
    });

    trigger.setAttribute("aria-expanded", String(!isExpanded));
    item?.classList.toggle("is-open", !isExpanded);
  });
});

const header = document.querySelector("[data-header]");
let scrollFrame = 0;
window.addEventListener("scroll", () => {
  if (scrollFrame) return;
  scrollFrame = window.requestAnimationFrame(() => {
    header?.classList.toggle("is-scrolled", window.scrollY > 24);
    scrollFrame = 0;
  });
}, { passive: true });

const revealItems = document.querySelectorAll("[data-reveal]");
if ("IntersectionObserver" in window && !window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
  const observer = new IntersectionObserver((entries) => {
    entries.forEach((entry) => {
      if (entry.isIntersecting) {
        entry.target.classList.add("is-visible");
        observer.unobserve(entry.target);
      }
    });
  }, { threshold: .12, rootMargin: "0px 0px -40px" });
  revealItems.forEach((item) => observer.observe(item));
} else {
  revealItems.forEach((item) => item.classList.add("is-visible"));
}

/* Liquid Glass Dynamic Cursor Spotlight */
if (!window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
  const glassCards = document.querySelectorAll(".liquid-glass, .liquid-card, .mode-card, .faq-item, .specs-card");
  glassCards.forEach((card) => {
    let ticking = false;
    card.addEventListener("mousemove", (e) => {
      if (ticking) return;
      window.requestAnimationFrame(() => {
        const rect = card.getBoundingClientRect();
        const x = e.clientX - rect.left;
        const y = e.clientY - rect.top;
        card.style.setProperty("--mouse-x", `${x}px`);
        card.style.setProperty("--mouse-y", `${y}px`);
        ticking = false;
      });
      ticking = true;
    }, { passive: true });

    card.addEventListener("mouseleave", () => {
      card.style.removeProperty("--mouse-x");
      card.style.removeProperty("--mouse-y");
    }, { passive: true });
  });
}