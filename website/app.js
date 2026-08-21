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
    title: "Твики ядра и памяти без мифов",
    copy: "Win32PrioritySeparation 0x26 для приоритета активной игры, DisablePagingExecutive для удержания ядра в RAM и отключение троттлинга сетевых пакетов.",
    result: "Исходное состояние всегда сохраняется",
    rows: [["Win32PrioritySeparation", "0x26 (Quantum Boost)"], ["DisablePagingExecutive", "1 (RAM Kernel)"], ["NetworkThrottlingIndex", "0xFFFFFFFF (Disabled)"]],
    count: "35+ параметров"
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
    copy: "Переключение DNS в 1 клик (Cloudflare, Google, AdGuard, Quad9), сброс кэша DNS и безопасная настройка глобального профиля TCP.",
    result: "Снижение задержек и защита от рекламы",
    rows: [["Cloudflare Gaming DNS", "1.1.1.1 / 1.0.0.1"], ["Сброс кэша", "Flush DNS"], ["TCP Window", "Normal / Disabled"]],
    count: "Быстрый DNS"
  },
  storage: {
    title: "Обслуживание дисков и ReTrim",
    copy: "Диагностика накопителей, отправка команды ReTrim только на SSD и NVMe накопители с защитой от запуска на вращающихся магнитных дисках (HDD).",
    result: "Поддержание максимальной скорости SSD",
    rows: [["NVMe SSD", "ReTrim поддерживается"], ["SATA SSD", "ReTrim поддерживается"], ["HDD Magnetic", "Защищено от TRIM"]],
    count: "Защита HDD"
  },
  monitoring: {
    title: "Мониторинг без фоновой нагрузки",
    copy: "Загрузка процессора, видеокарты, оперативной памяти и дисков. Нативные счетчики обновляются только пока открыт раздел, не нагружая ПК во время игры.",
    result: "Нулевое влияние на FPS",
    rows: [["CPU Usage", "Нативные счетчики"], ["GPU Load & VRAM", "DirectX/WDDM"], ["RAM Available", "GlobalMemoryStatusEx"]],
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
    row.querySelector("code").textContent = item.rows[index][0];
    row.querySelector("span").textContent = item.rows[index][1];
  });

  detail.animate(
    [{ opacity: .35, transform: "translateY(5px)" }, { opacity: 1, transform: "translateY(0)" }],
    { duration: 260, easing: "cubic-bezier(.22, 1, .36, 1)" }
  );
}

featureTabs.forEach((tab) => tab.addEventListener("click", () => selectFeature(tab.dataset.feature)));

const detailsToggle = document.querySelector(".detail-toggle");
const mockDetails = document.querySelector("#mock-details");
detailsToggle?.addEventListener("click", () => {
  const expanded = detailsToggle.getAttribute("aria-expanded") === "true";
  detailsToggle.setAttribute("aria-expanded", String(!expanded));
  detailsToggle.firstChild.textContent = expanded ? "Показать детали " : "Скрыть детали ";
  mockDetails.hidden = expanded;
});

const checkButton = document.querySelector("[data-check-button]");
checkButton?.addEventListener("click", () => {
  if (checkButton.classList.contains("is-checking")) return;
  checkButton.classList.add("is-checking");
  checkButton.querySelector("span").textContent = "Проверяем…";
  window.setTimeout(() => {
    checkButton.classList.remove("is-checking");
    checkButton.querySelector("span").textContent = "Проверка завершена";
  }, 850);
});

const applyButton = document.querySelector("[data-apply-button]");
applyButton?.addEventListener("click", () => {
  applyButton.classList.toggle("is-applied");
  applyButton.querySelector("span").textContent = applyButton.classList.contains("is-applied") ? "Применено" : "Применить";
});

const repositoryUrl = document.documentElement.dataset.repositoryUrl;
if (repositoryUrl) {
  document.querySelectorAll(".source-link").forEach((link) => link.setAttribute("href", repositoryUrl));
}

const scanButton = document.querySelector("[data-scan-button]");
scanButton?.addEventListener("click", () => {
  if (scanButton.classList.contains("is-scanning")) return;
  scanButton.classList.add("is-scanning");
  scanButton.querySelector("span").textContent = "Проверяем логику…";
  window.setTimeout(() => {
    scanButton.classList.remove("is-scanning");
    scanButton.querySelector("span").textContent = "Система проверена";
  }, 950);
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
