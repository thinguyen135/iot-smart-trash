"""
AI Trash Classifier - Firebase
Chuyển đổi từ C# WinForms sang Python (tkinter + requests + Pillow)

Yêu cầu cài đặt:
    pip install requests Pillow
"""

import base64
import json
import mimetypes
import os
import threading
import time
import tkinter as tk
from tkinter import filedialog, messagebox
from io import BytesIO

import requests
from PIL import Image, ImageTk

# ──────────────────────────────────────────────
# Cấu hình Firebase / Gemini
# ──────────────────────────────────────────────
FIREBASE_RTDB_URLS = [
    "https://iot-smart-trash-212d9-default-rtdb.firebaseio.com",
    "https://iot-smart-trash-212d9-default-rtdb.asia-southeast1.firebasedatabase.app",
]
FIREBASE_STORAGE_BUCKETS = [
    "iot-smart-trash-212d9.firebasestorage.app",
    "iot-smart-trash-212d9.appspot.com",
]
FIREBASE_API_KEY = "AIzaSyBkU6zS_GhZ-ziCHGKec5XlbNF1SC8PkVQ"
GEMINI_API_KEY   = ""          # ← điền Gemini API key vào đây
GEMINI_MAX_RETRIES = 4
FIREBASE_POLL_INTERVAL_MS = 1500   # 1.5 giây


# ──────────────────────────────────────────────
# Helpers
# ──────────────────────────────────────────────
def get_mime_type(file_path: str) -> str:
    ext = os.path.splitext(file_path)[1].lower()
    return {"png": "image/png", ".jpg": "image/jpeg", ".jpeg": "image/jpeg"}.get(ext, "application/octet-stream")


def normalize_category(raw: str) -> str:
    """Trả về 'H', 'N', 'G' hoặc '' từ chuỗi AI trả về."""
    for ch in (raw or "").strip().upper():
        if ch in ("H", "N", "G"):
            return ch
    return ""


def get_trash_label(category: str) -> str:
    return {"H": "Hữu cơ", "N": "Nhựa / vô cơ", "G": "Giấy / carton"}.get(category, "Không xác định")


def create_preview_data_url(file_path: str, max_side: int = 900) -> str:
    """Thu nhỏ ảnh rồi mã hóa base64 để gửi qua Realtime Database."""
    with Image.open(file_path) as img:
        w, h = img.size
        scale = min(1.0, max_side / max(w, h))
        new_size = (max(1, round(w * scale)), max(1, round(h * scale)))
        resized = img.resize(new_size, Image.LANCZOS)
        buf = BytesIO()
        resized.convert("RGB").save(buf, format="JPEG", quality=72)
        b64 = base64.b64encode(buf.getvalue()).decode()
        return f"data:image/jpeg;base64,{b64}"


def is_retryable_gemini_error(status_code: int) -> bool:
    return status_code in (429, 500, 502, 503, 504)


# ──────────────────────────────────────────────
# Firebase helpers (synchronous – gọi từ thread)
# ──────────────────────────────────────────────
def firebase_get(path: str, active_url_holder: list) -> str | None:
    last_error = ""
    for url in FIREBASE_RTDB_URLS:
        try:
            r = requests.get(url + path, timeout=10)
            if r.ok:
                active_url_holder[0] = url
                return r.text
            last_error = f"{url}: {r.status_code} {r.text}"
        except Exception as ex:
            last_error = f"{url}: {ex}"
    raise RuntimeError(last_error)


def firebase_put(path: str, value, active_url_holder: list):
    body = json.dumps(value)
    r = requests.put(active_url_holder[0] + path, data=body,
                     headers={"Content-Type": "application/json"}, timeout=10)
    if not r.ok:
        raise RuntimeError("Firebase RTDB write failed: " + r.text)


def firebase_upload_image(file_path: str, storage_path: str) -> str:
    with open(file_path, "rb") as f:
        file_bytes = f.read()
    escaped = requests.utils.quote(storage_path, safe="")
    mime = get_mime_type(file_path)
    last_error = ""

    for bucket in FIREBASE_STORAGE_BUCKETS:
        url = (f"https://firebasestorage.googleapis.com/v0/b/{bucket}/o"
               f"?uploadType=media&name={escaped}&key={FIREBASE_API_KEY}")
        r = requests.post(url, data=file_bytes,
                          headers={"Content-Type": mime}, timeout=30)
        if r.ok:
            cache_breaker = int(time.time() * 1000)
            return (f"https://firebasestorage.googleapis.com/v0/b/{bucket}/o/"
                    f"{escaped}?alt=media&t={cache_breaker}")
        last_error = f"Bucket {bucket}: {r.text}"

    raise RuntimeError(
        "Firebase Storage upload failed. Kiểm tra Storage đã được tạo và Rules đúng chưa. "
        "Chi tiết: " + last_error
    )


def classify_with_gemini(file_path: str, status_cb) -> str:
    with open(file_path, "rb") as f:
        image_bytes = f.read()
    b64_image = base64.b64encode(image_bytes).decode()
    mime = get_mime_type(file_path)
    api_url = (f"https://generativelanguage.googleapis.com/v1beta/"
               f"models/gemini-2.5-flash:generateContent?key={GEMINI_API_KEY}")
    prompt = (
        "Bạn là hệ thống AI phân loại rác. Dựa vào ảnh, hãy trả về đúng 1 ký tự duy nhất: "
        "'H' nếu là rác hữu cơ như thức ăn, rau củ; "
        "'N' nếu là rác nhựa, kim loại hoặc vô cơ; "
        "'G' nếu là giấy hoặc carton. Không giải thích gì thêm."
    )
    body = {
        "contents": [{
            "parts": [
                {"text": prompt},
                {"inline_data": {"mime_type": mime, "data": b64_image}}
            ]
        }]
    }

    for attempt in range(1, GEMINI_MAX_RETRIES + 1):
        r = requests.post(api_url, json=body, timeout=60)
        if r.ok:
            return _extract_gemini_text(r.json())
        if is_retryable_gemini_error(r.status_code) and attempt < GEMINI_MAX_RETRIES:
            wait = attempt * 3
            status_cb(f"Gemini đang quá tải, thử lại {attempt + 1}/{GEMINI_MAX_RETRIES} sau {wait}s...")
            time.sleep(wait)
            continue
        raise RuntimeError("Gemini error: " + r.text)

    raise RuntimeError("Gemini đang quá tải. Vui lòng thử lại sau vài phút.")


def _extract_gemini_text(data: dict) -> str:
    try:
        return data["candidates"][0]["content"]["parts"][0]["text"]
    except (KeyError, IndexError, TypeError):
        return ""


# ──────────────────────────────────────────────
# GUI Application
# ──────────────────────────────────────────────
class TrashClassifierApp(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("AI Trash Classifier - Firebase")
        self.resizable(False, False)

        self._is_processing = False
        self._active_url_holder = [FIREBASE_RTDB_URLS[0]]
        self._selected_image_path = ""
        self._photo_ref = None            # giữ tham chiếu ảnh để tránh bị GC

        self._build_ui()
        self._start_firebase_poll()

    # ── UI ──────────────────────────────────────
    def _build_ui(self):
        PAD = 24

        # Canvas hiển thị ảnh (760×420)
        self.canvas = tk.Canvas(self, width=760, height=420,
                                bg="#F0F4EE", highlightthickness=1,
                                highlightbackground="#C0C8BA")
        self.canvas.place(x=PAD, y=PAD)
        self._canvas_image_id = self.canvas.create_image(380, 210, anchor="center")

        # Nút chọn ảnh
        self.btn_upload = tk.Button(self, text="Chọn ảnh rác",
                                    width=16, height=2,
                                    command=self._on_btn_upload)
        self.btn_upload.place(x=PAD, y=470)

        # Nhãn trạng thái
        self.lbl_status = tk.Label(self, text="Đang kết nối Firebase...",
                                   anchor="w", justify="left")
        self.lbl_status.place(x=195, y=481)

        # Nhãn kết quả AI
        self.lbl_result = tk.Label(self, text="Kết quả AI: Chưa có",
                                   font=("Segoe UI", 10, "bold"),
                                   anchor="w", justify="left")
        self.lbl_result.place(x=PAD, y=530)

        self.geometry(f"810x580")

    def _set_status(self, text: str):
        """Thread-safe cập nhật nhãn trạng thái."""
        self.after(0, lambda: self.lbl_status.config(text=text))

    def _set_result(self, text: str):
        self.after(0, lambda: self.lbl_result.config(text=text))

    def _set_btn_enabled(self, enabled: bool):
        self.after(0, lambda: self.btn_upload.config(
            state="normal" if enabled else "disabled"))

    def _show_image(self, file_path: str):
        def _load():
            try:
                img = Image.open(file_path)
                img.thumbnail((760, 420), Image.LANCZOS)
                photo = ImageTk.PhotoImage(img)
                def _update():
                    self._photo_ref = photo
                    self.canvas.itemconfig(self._canvas_image_id, image=photo)
                self.after(0, _update)
            except Exception:
                pass
        threading.Thread(target=_load, daemon=True).start()

    # ── Firebase polling ─────────────────────────
    def _start_firebase_poll(self):
        self._poll_firebase()

    def _poll_firebase(self):
        threading.Thread(target=self._check_obstacle_signal, daemon=True).start()
        self.after(FIREBASE_POLL_INTERVAL_MS, self._poll_firebase)

    def _check_obstacle_signal(self):
        if self._is_processing:
            return
        try:
            response = firebase_get("/sensors/pir/obstacle.json", self._active_url_holder)
            self._set_status("Firebase đã kết nối")
            if response and response.strip() == "1":
                self.after(0, self._trigger_selection_and_classification)
        except Exception as ex:
            self._set_status("Chưa kết nối RTDB, vẫn có thể chọn ảnh thủ công.")
            print("Lỗi Firebase:", ex)

    def _trigger_selection_and_classification(self):
        messagebox.showinfo("Phát hiện rác", "Phát hiện rác mới. Hãy chọn ảnh để AI phân loại.")
        selected = self._pick_image_and_classify()
        if not selected:
            threading.Thread(
                target=lambda: self._safe_put("/sensors/pir/obstacle.json", 0,
                                              "Đã hủy chọn ảnh, obstacle đã reset về 0."),
                daemon=True
            ).start()

    def _safe_put(self, path, value, status_text):
        try:
            firebase_put(path, value, self._active_url_holder)
            self._set_status(status_text)
        except Exception as ex:
            self._set_status(f"Lỗi ghi Firebase: {ex}")

    # ── Chọn ảnh & phân loại ────────────────────
    def _on_btn_upload(self):
        self._pick_image_and_classify()

    def _pick_image_and_classify(self) -> bool:
        if self._is_processing:
            return False
        self._is_processing = True
        try:
            file_path = filedialog.askopenfilename(
                filetypes=[("Image Files", "*.jpg *.jpeg *.png")]
            )
            if not file_path:
                return False
            self._selected_image_path = file_path
            self._show_image(file_path)
            threading.Thread(
                target=self._process_selected_image,
                args=(file_path,),
                daemon=True
            ).start()
            return True
        except Exception:
            self._is_processing = False
            return False

    def _process_selected_image(self, image_path: str):
        self._set_btn_enabled(False)
        try:
            # 1. Upload ảnh lên Storage
            self._set_status("Đang upload ảnh lên Firebase Storage...")
            storage_path = "trash-images/current_trash.jpg"
            try:
                image_url = firebase_upload_image(image_path, storage_path)
            except Exception as ex:
                storage_path = ""
                image_url = create_preview_data_url(image_path)
                self._set_status("Storage chưa dùng được, tạm gửi ảnh preview qua Realtime Database...")
                print(ex)

            # 2. Gửi preview lên Firebase
            try:
                self._send_preview_to_firebase(image_url, storage_path)
                self._set_status("Đã gửi ảnh lên web, đang phân loại bằng Gemini...")
            except Exception as ex:
                self._set_status("Không gửi được ảnh preview, vẫn tiếp tục phân loại...")
                print(ex)

            # 3. Gọi Gemini
            self._set_status("Đang phân loại bằng Gemini...")
            raw_result = classify_with_gemini(image_path, self._set_status)
            ai_result = normalize_category(raw_result)

            if not ai_result:
                self._set_status("AI không trả về H, N hoặc G")
                return

            label = get_trash_label(ai_result)
            self._set_result(f"Kết quả AI: {ai_result} - {label}")

            # 4. Ghi kết quả lên Firebase
            self._set_status("Đang ghi kết quả lên Realtime Database...")
            self._send_result_to_firebase(ai_result, label, image_url, storage_path)
            self._set_status("Hoàn tất. Web dashboard sẽ cập nhật realtime.")

        except Exception as ex:
            self._set_status(f"Lỗi xử lý: {ex}")
            self.after(0, lambda: messagebox.showerror("Lỗi", f"Lỗi xử lý ảnh: {ex}"))
        finally:
            self._set_btn_enabled(True)
            self._is_processing = False

    # ── Firebase write helpers ───────────────────
    def _send_preview_to_firebase(self, image_url: str, storage_path: str):
        event = {
            "category": "",
            "label": "Đang phân loại...",
            "imageUrl": image_url,
            "storagePath": storage_path,
            "createdAt": int(time.time() * 1000),
        }
        firebase_put("/trash_events/current.json", event, self._active_url_holder)

    def _send_result_to_firebase(self, category: str, label: str,
                                  image_url: str, storage_path: str):
        created_at = int(time.time() * 1000)
        event = {
            "category": category,
            "label": label,
            "imageUrl": image_url,
            "storagePath": storage_path,
            "createdAt": created_at,
        }
        firebase_put("/trash_type/category.json", category, self._active_url_holder)
        firebase_put("/trash_events/current.json", event, self._active_url_holder)
        firebase_put(f"/trash_events/history/{created_at}.json", event, self._active_url_holder)


# ──────────────────────────────────────────────
if __name__ == "__main__":
    app = TrashClassifierApp()
    app.mainloop()