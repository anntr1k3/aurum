const featureContent = {
  msi: {
    title: "Аппаратные прерывания (MSI Mode)",
    copy: "Включает Message Signaled Interrupts для видеокарты, сети, звука и накопителей и задаёт приоритет прерываний. Исходные значения каждого устройства сохраняются до изменения.",
    result: "Обратимая настройка MSI, без обещаний FPS",
    rows: [["NVIDIA RTX GPU", "MSI, высокий приоритет"], ["Realtek Gaming 2.5GbE", "MSI, высокий приоритет"], ["USB xHCI Controller", "MSI, обычный приоритет"]],
    count: "1-клик пресет"
  },
  timer: {
    title: "Разрешение системного таймера",
    copy: "Задаёт разрешение таймера через NtSetTimerResolution. Действует, пока процесс держит запрос, и сбрасывается без перезагрузки. Полезно измерить до и после, а не принимать как обязательный шаг.",
    result: "Включается и снимается без перезагрузки",
    rows: [["Запрошенное разрешение", "0.500 мс"], ["Снятие запроса", "без перезагрузки"], ["Штатный таймер Windows", "обычно 15.625 мс"]],
    count: "Native API"
  },
  tweaks: {
    title: "Твики, которые можно объяснить и откатить",
    copy: "Каталог поддерживаемых настроек Windows: приоритет активного процесса, удержание ядра в RAM, сетевой троттлинг и другие. Каждая запись называет точные значения и хранит снимок до применения.",
    result: "Исходное состояние всегда сохраняется",
    rows: [["Win32PrioritySeparation", "снимок до записи"], ["DisablePagingExecutive", "снимок до записи"], ["NetworkThrottlingIndex", "снимок до записи"]],
    count: "24 настройки"
  },
  power: {
    title: "Core Parking и схемы питания",
    copy: "Переключает существующую схему Windows или применяет парковку ядер только к клону плана Aurum. Встроенные схемы не переписываются. На гибридных процессорах полное распарковывание — осознанный выбор, не универсальная кнопка.",
    result: "Исходная схема восстанавливается целиком",
    rows: [["Исходный план", "GUID сохранён"], ["План Aurum", "изолированный клон"], ["Откат", "активация исходного GUID"]],
    count: "Изолированный клон"
  },
  network: {
    title: "DNS и диагностика сети",
    copy: "Профили DNS (Cloudflare, Google, AdGuard, Quad9), сброс кэша и проверка задержки ICMP после явного нажатия. TCP-параметры меняются только через документированные команды Windows.",
    result: "DNS возвращается к DHCP одним откатом",
    rows: [["Cloudflare DNS", "1.1.1.1 / 1.0.0.1"], ["Сброс кэша", "ipconfig /flushdns"], ["Замер", "4 ICMP-запроса по кнопке"]],
    count: "Профили DNS"
  },
  storage: {
    title: "Обслуживание дисков и ReTrim",
    copy: "Диагностика накопителей и команда ReTrim только для SSD и NVMe, у которых Windows подтвердила TRIM. На вращающихся дисках действие недоступно.",
    result: "TRIM не предлагается для HDD",
    rows: [["NVMe SSD", "ReTrim по подтверждению"], ["SATA SSD", "ReTrim по подтверждению"], ["HDD", "исключён из TRIM"]],
    count: "Защита HDD"
  },
  monitoring: {
    title: "Мониторинг без фонового демона",
    copy: "Загрузка процессора, видеокарты, памяти, диска и сети. Счётчики обновляются, только пока открыт раздел мониторинга, и никуда не отправляются.",
    result: "Нет опроса в фоне и нет телеметрии",
    rows: [["CPU", "нативные счётчики PDH"], ["GPU", "если Windows их отдаёт"], ["RAM", "GlobalMemoryStatusEx"]],
    count: "Только на экране"
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
    row.querySelector("code").textContent = item.rows[index][0];
    row.querySelector("span").textContent = item.rows[index][1];
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
