import { initializeApp } from "https://www.gstatic.com/firebasejs/12.13.0/firebase-app.js";
import {
  getDatabase,
  limitToLast,
  onChildAdded,
  onValue,
  query,
  ref as databaseRef,
} from "https://www.gstatic.com/firebasejs/12.13.0/firebase-database.js";
import {
  getDownloadURL,
  getMetadata,
  getStorage,
  listAll,
  ref as storageRef,
} from "https://www.gstatic.com/firebasejs/12.13.0/firebase-storage.js";

const firebaseConfig = {
  apiKey: "AIzaSyBkU6zS_GhZ-ziCHGKec5XlbNF1SC8PkVQ",
  authDomain: "iot-smart-trash-212d9.firebaseapp.com",
  databaseURL: "https://iot-smart-trash-212d9-default-rtdb.firebaseio.com",
  projectId: "iot-smart-trash-212d9",
  storageBucket: "iot-smart-trash-212d9.firebasestorage.app",
  messagingSenderId: "327273243341",
  appId: "1:327273243341:web:90c1cbfad3bfa4b1e87f84",
  measurementId: "G-7FSQHS1Y7N",
};

const app = initializeApp(firebaseConfig);
const db = getDatabase(app);
const storage = getStorage(app);

const categoryLabels = {
  H: "Hữu cơ",
  N: "Nhựa / vô cơ",
  G: "Giấy / carton",
};

const bins = {
  huuco: { name: "Hữu cơ",        legacyStatusPath: "sensors/hc-sr04/status_huuco" },
  nhua:  { name: "Nhựa / vô cơ",  legacyStatusPath: "sensors/hc-sr04/status_nhua"  },
  giay:  { name: "Giấy / carton", legacyStatusPath: "sensors/hc-sr04/status_giay"  },
};

const hasLiveBinData = {};
const STORAGE_IMAGE_FOLDER = "trash-images";
const STORAGE_POLL_MS = 5000;
const imageFilePattern = /\.(jpe?g|png|webp)$/i;

let latestRenderedEventAt = 0;
let latestStorageImageMarker = "";

// SVG bin geometry constants
const BIN_TOP    = 40;   // y where bin interior starts
const BIN_BOTTOM = 158;  // y where bin bottom is
const BIN_RANGE  = BIN_BOTTOM - BIN_TOP; // 118 px

const elements = {
  connectionStatus: document.querySelector("#connectionStatus"),
  pirState:         document.querySelector("#pirState"),
  latestCategory:   document.querySelector("#latestCategory"),
  latestTime:       document.querySelector("#latestTime"),
  trashImage:       document.querySelector("#trashImage"),
  photoPlaceholder: document.querySelector("#photoPlaceholder"),
  categoryCode:     document.querySelector("#categoryCode"),
  categoryLabel:    document.querySelector("#categoryLabel"),
};

function setConnection(state, message) {
  elements.connectionStatus.className = `connection ${state}`;
  elements.connectionStatus.textContent = message;
}

function markRealtimeUpdate(message = "Firebase realtime đã cập nhật") {
  const time = new Intl.DateTimeFormat("vi-VN", {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  }).format(new Date());
  setConnection("is-online", `${message} ${time}`);
}

function pulseElement(element) {
  if (!element) return;
  element.classList.remove("is-live-updated");
  void element.offsetWidth;
  element.classList.add("is-live-updated");
}

function clampPercent(value) {
  const p = Number(value);
  if (!Number.isFinite(p)) return 0;
  return Math.max(0, Math.min(100, Math.round(p)));
}

function formatTime(value) {
  const ts = Number(value);
  if (!Number.isFinite(ts) || ts <= 0) return "Chưa có dữ liệu";
  const date = new Date(ts);
  if (Number.isNaN(date.getTime())) return "Chưa có dữ liệu";
  return new Intl.DateTimeFormat("vi-VN", {
    hour: "2-digit", minute: "2-digit", second: "2-digit",
    day: "2-digit", month: "2-digit", year: "numeric",
  }).format(date);
}

function getEventTimestamp(value) {
  const timestamp = Number(value);
  return Number.isFinite(timestamp) && timestamp > 0 ? timestamp : 0;
}

function getCacheSafeImageUrl(imageUrl, version) {
  if (!imageUrl || imageUrl.startsWith("data:") || imageUrl.startsWith("blob:")) {
    return imageUrl;
  }

  const cacheValue = encodeURIComponent(version || Date.now());
  const separator = imageUrl.includes("?") ? "&" : "?";
  return `${imageUrl}${separator}_live=${cacheValue}`;
}

function renderBin(binKey, value = {}) {
  const percent  = clampPercent(value.percent);
  const distance = Number.isFinite(Number(value.distanceCm)) ? Number(value.distanceCm) : 30;
  const status   = value.status || (percent >= 90 ? "FULL" : "NOT_FULL");
  const isFull   = status === "FULL" || percent >= 90;
  const card     = document.querySelector(`[data-bin="${binKey}"]`);

  // ── Update SVG fill rect (animate via CSS transition) ──
  const fillHeight = (percent / 100) * BIN_RANGE;
  const fillY      = BIN_BOTTOM - fillHeight;

  const bar = document.querySelector(`#${binKey}Bar`);
  bar.setAttribute("y",      fillY);
  bar.setAttribute("height", fillHeight);

  // Sync the shine overlay rect too
  const shine = document.querySelector(`.bin-fill-shine[data-sync="${binKey}"]`);
  if (shine) {
    shine.setAttribute("y",      fillY);
    shine.setAttribute("height", fillHeight);
  }

  // ── Text values ──
  document.querySelector(`#${binKey}Percent`).textContent  = `${percent}%`;
  document.querySelector(`#${binKey}Distance`).textContent = `${distance} cm`;
  document.querySelector(`#${binKey}Updated`).textContent  = formatTime(value.updatedAt);

  // ── Badge & card state ──
  const badge = document.querySelector(`#${binKey}Status`);
  badge.textContent = isFull ? "FULL" : "NOT_FULL";
  badge.classList.toggle("is-full", isFull);
  card.dataset.state = isFull ? "full" : "ready";
  pulseElement(card);
}

function renderLegacyStatus(binKey, status) {
  if (hasLiveBinData[binKey]) return;
  const badge = document.querySelector(`#${binKey}Status`);
  const card  = document.querySelector(`[data-bin="${binKey}"]`);
  const isFull = status === "FULL";
  if (badge.textContent === "FULL" || badge.textContent === "NOT_FULL") {
    badge.textContent = isFull ? "FULL" : "NOT_FULL";
    badge.classList.toggle("is-full", isFull);
    card.dataset.state = isFull ? "full" : card.dataset.state || "ready";
  }
}

async function renderCurrentEvent(value = {}) {
  const eventAt = getEventTimestamp(value.createdAt);
  if (eventAt && eventAt < latestRenderedEventAt) return;
  if (eventAt) latestRenderedEventAt = eventAt;

  const category = String(value.category || "").trim().toUpperCase();
  const label    = value.label || categoryLabels[category] || "Chưa có kết quả";

  elements.categoryCode.textContent    = category || "-";
  elements.categoryLabel.textContent   = label;
  elements.latestCategory.textContent  = category ? `${category} - ${label}` : label;
  elements.latestTime.textContent      = formatTime(eventAt || value.createdAt);

  let imageUrl = value.imageUrl || "";
  if (!imageUrl && value.storagePath) {
    try {
      imageUrl = await getDownloadURL(storageRef(storage, value.storagePath));
    } catch (error) {
      console.error("Không lấy được URL ảnh từ Firebase Storage", error);
    }
  }

  if (imageUrl) {
    elements.trashImage.src     = getCacheSafeImageUrl(imageUrl, eventAt || value.storagePath);
    elements.trashImage.hidden  = false;
    elements.photoPlaceholder.hidden = true;
    pulseElement(elements.trashImage);
    pulseElement(elements.latestCategory.closest(".signal"));
    pulseElement(elements.latestTime.closest(".signal"));
    return;
  }

  elements.trashImage.removeAttribute("src");
  elements.trashImage.hidden       = true;
  elements.photoPlaceholder.hidden = false;
}

async function syncLatestStorageImage() {
  try {
    const folderRef = storageRef(storage, STORAGE_IMAGE_FOLDER);
    const result = await listAll(folderRef);
    const imageRefs = result.items.filter((itemRef) => imageFilePattern.test(itemRef.name));
    if (imageRefs.length === 0) return;

    const imageEntries = await Promise.all(
      imageRefs.map(async (itemRef) => {
        const metadata = await getMetadata(itemRef);
        const updatedAt = Date.parse(metadata.updated || metadata.timeCreated || "");
        return {
          itemRef,
          metadata,
          updatedAt: Number.isFinite(updatedAt) ? updatedAt : 0,
        };
      }),
    );

    imageEntries.sort((a, b) => b.updatedAt - a.updatedAt);
    const latest = imageEntries[0];
    const marker = `${latest.itemRef.fullPath}:${latest.metadata.generation || latest.metadata.updated || latest.updatedAt}`;
    if (marker === latestStorageImageMarker) return;

    latestStorageImageMarker = marker;
    const imageUrl = await getDownloadURL(latest.itemRef);
    await renderCurrentEvent({
      category: "",
      label: "Ảnh mới, đang chờ phân loại",
      imageUrl,
      storagePath: latest.itemRef.fullPath,
      createdAt: latest.updatedAt || Date.now(),
    });
    markRealtimeUpdate("Đã nhận ảnh mới từ Storage");
  } catch (error) {
    console.warn("Không kiểm tra được ảnh mới trong Firebase Storage", error);
  }
}

// ── Firebase listeners ──────────────────────────────────────────
Object.entries(bins).forEach(([binKey, config]) => {
  onValue(
    databaseRef(db, `bins/${binKey}`),
    (snapshot) => {
      markRealtimeUpdate("Wokwi đã cập nhật");
      hasLiveBinData[binKey] = snapshot.exists();
      renderBin(binKey, snapshot.val() || {});
    },
    (error) => {
      console.error(error);
      setConnection("is-error", "Lỗi đọc Firebase");
    },
  );

  onValue(databaseRef(db, config.legacyStatusPath), (snapshot) => {
    renderLegacyStatus(binKey, snapshot.val());
  });
});

onValue(databaseRef(db, "sensors/pir/obstacle"), (snapshot) => {
  const value = Number(snapshot.val());
  elements.pirState.textContent = value === 1 ? "Có rác đang chờ AI" : "Sẵn sàng";
  pulseElement(elements.pirState.closest(".signal"));
  markRealtimeUpdate("Wokwi đã cập nhật");
});

onValue(databaseRef(db, "trash_type/category"), (snapshot) => {
  const category = String(snapshot.val() || "").trim().toUpperCase();
  if (!category) return;
  const label = categoryLabels[category] || "Không xác định";
  elements.latestCategory.textContent = `${category} - ${label}`;
  elements.categoryCode.textContent   = category;
  elements.categoryLabel.textContent  = label;
  pulseElement(elements.latestCategory.closest(".signal"));
  markRealtimeUpdate("AI đã cập nhật");
});

onValue(
  databaseRef(db, "trash_events/current"),
  (snapshot) => {
    renderCurrentEvent(snapshot.val() || {});
    markRealtimeUpdate("Ảnh mới đã cập nhật");
  },
  (error) => {
    console.error(error);
    setConnection("is-error", "Lỗi đọc ảnh rác");
  },
);

onChildAdded(
  query(databaseRef(db, "trash_events/history"), limitToLast(1)),
  (snapshot) => {
    renderCurrentEvent(snapshot.val() || {});
    markRealtimeUpdate("Event mới đã cập nhật");
  },
  (error) => {
    console.error(error);
    setConnection("is-error", "Lỗi đọc lịch sử ảnh");
  },
);

syncLatestStorageImage();
setInterval(syncLatestStorageImage, STORAGE_POLL_MS);
