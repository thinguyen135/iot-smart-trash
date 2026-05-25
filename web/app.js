import { initializeApp } from "https://www.gstatic.com/firebasejs/12.13.0/firebase-app.js";
import {
  getDatabase,
  onValue,
  ref as databaseRef,
} from "https://www.gstatic.com/firebasejs/12.13.0/firebase-database.js";
import {
  getDownloadURL,
  getStorage,
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
  huuco: {
    name: "Hữu cơ",
    legacyStatusPath: "sensors/hc-sr04/status_huuco",
  },
  nhua: {
    name: "Nhựa / vô cơ",
    legacyStatusPath: "sensors/hc-sr04/status_nhua",
  },
  giay: {
    name: "Giấy / carton",
    legacyStatusPath: "sensors/hc-sr04/status_giay",
  },
};

const hasLiveBinData = {};

const elements = {
  connectionStatus: document.querySelector("#connectionStatus"),
  pirState: document.querySelector("#pirState"),
  latestCategory: document.querySelector("#latestCategory"),
  latestTime: document.querySelector("#latestTime"),
  trashImage: document.querySelector("#trashImage"),
  photoPlaceholder: document.querySelector("#photoPlaceholder"),
  categoryCode: document.querySelector("#categoryCode"),
  categoryLabel: document.querySelector("#categoryLabel"),
};

function setConnection(state, message) {
  elements.connectionStatus.className = `connection ${state}`;
  elements.connectionStatus.textContent = message;
}

function clampPercent(value) {
  const percent = Number(value);
  if (!Number.isFinite(percent)) return 0;
  return Math.max(0, Math.min(100, Math.round(percent)));
}

function formatTime(value) {
  const timestamp = Number(value);
  if (!Number.isFinite(timestamp) || timestamp <= 0) return "Chưa có dữ liệu";

  const date = new Date(timestamp);
  if (Number.isNaN(date.getTime())) return "Chưa có dữ liệu";

  return new Intl.DateTimeFormat("vi-VN", {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
  }).format(date);
}

function renderBin(binKey, value = {}) {
  const percent = clampPercent(value.percent);
  const distance = Number.isFinite(Number(value.distanceCm))
    ? Number(value.distanceCm)
    : 30;
  const status = value.status || (percent >= 90 ? "FULL" : "NOT_FULL");
  const isFull = status === "FULL" || percent >= 90;
  const card = document.querySelector(`[data-bin="${binKey}"]`);

  document.querySelector(`#${binKey}Bar`).style.height = `${percent}%`;
  document.querySelector(`#${binKey}Percent`).textContent = `${percent}%`;
  document.querySelector(`#${binKey}Distance`).textContent = `${distance} cm`;
  document.querySelector(`#${binKey}Updated`).textContent = formatTime(
    value.updatedAt,
  );

  const badge = document.querySelector(`#${binKey}Status`);
  badge.textContent = isFull ? "FULL" : "NOT_FULL";
  badge.classList.toggle("is-full", isFull);
  card.dataset.state = isFull ? "full" : "ready";
}

function renderLegacyStatus(binKey, status) {
  if (hasLiveBinData[binKey]) return;

  const badge = document.querySelector(`#${binKey}Status`);
  const card = document.querySelector(`[data-bin="${binKey}"]`);
  const isFull = status === "FULL";

  if (badge.textContent === "FULL" || badge.textContent === "NOT_FULL") {
    badge.textContent = isFull ? "FULL" : "NOT_FULL";
    badge.classList.toggle("is-full", isFull);
    card.dataset.state = isFull ? "full" : card.dataset.state || "ready";
  }
}

async function renderCurrentEvent(value = {}) {
  const category = String(value.category || "").trim().toUpperCase();
  const label = value.label || categoryLabels[category] || "Chưa có kết quả";

  elements.categoryCode.textContent = category || "-";
  elements.categoryLabel.textContent = label;
  elements.latestCategory.textContent = category ? `${category} - ${label}` : label;
  elements.latestTime.textContent = formatTime(value.createdAt);

  let imageUrl = value.imageUrl || "";
  if (!imageUrl && value.storagePath) {
    try {
      imageUrl = await getDownloadURL(storageRef(storage, value.storagePath));
    } catch (error) {
      console.error("Không lấy được URL ảnh từ Firebase Storage", error);
    }
  }

  if (imageUrl) {
    elements.trashImage.src = imageUrl;
    elements.trashImage.hidden = false;
    elements.photoPlaceholder.hidden = true;
    return;
  }

  elements.trashImage.removeAttribute("src");
  elements.trashImage.hidden = true;
  elements.photoPlaceholder.hidden = false;
}

Object.entries(bins).forEach(([binKey, config]) => {
  onValue(
    databaseRef(db, `bins/${binKey}`),
    (snapshot) => {
      setConnection("is-online", "Firebase đã kết nối");
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
});

onValue(databaseRef(db, "trash_type/category"), (snapshot) => {
  const category = String(snapshot.val() || "").trim().toUpperCase();
  if (!category) return;

  const label = categoryLabels[category] || "Không xác định";
  elements.latestCategory.textContent = `${category} - ${label}`;
  elements.categoryCode.textContent = category;
  elements.categoryLabel.textContent = label;
});

onValue(
  databaseRef(db, "trash_events/current"),
  (snapshot) => {
    renderCurrentEvent(snapshot.val() || {});
  },
  (error) => {
    console.error(error);
    setConnection("is-error", "Lỗi đọc ảnh rác");
  },
);
