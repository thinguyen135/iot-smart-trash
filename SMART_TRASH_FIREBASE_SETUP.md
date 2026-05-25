# Smart Trash Firebase Setup

## Cùng dùng một Firebase

Tất cả module trong workspace này đã được đổi sang project:

- Realtime Database: `https://iot-smart-trash-212d9-default-rtdb.firebaseio.com`
- Storage bucket: `iot-smart-trash-212d9.firebasestorage.app`

Các nhánh dữ liệu chính:

- `sensors/pir/obstacle`: ESP32 tầng trên báo có rác.
- `trash_type/category`: C# AI ghi `H`, `N`, hoặc `G`; ESP32 tầng dưới đọc để gạt servo.
- `bins/huuco`, `bins/nhua`, `bins/giay`: ESP32 tầng dưới ghi phần trăm và trạng thái đầy.
- `trash_events/current`: C# AI ghi ảnh mới nhất, mã loại rác, nhãn loại rác.

## Chạy dashboard web

Mở file `web/index.html` trong trình duyệt, hoặc deploy thư mục `web` bằng Firebase Hosting.

Nếu dùng Firebase CLI:

```powershell
firebase deploy --only hosting,database,storage
```

## Chạy C# AI app

Mở solution:

```text
Picture_and_AI/send_picture_to_cloud_iot/send_picture_to_cloud_iot.sln
```

Trong `form_backend.cs`, thay `Your-api-key-here` bằng Gemini API key thật.

## Firebase rules cho demo

`database.rules.json` và `storage.rules` đang mở read/write để demo nhanh. Khi nộp thật hoặc đưa lên mạng công khai, cần đổi sang Firebase Authentication hoặc service account để khóa quyền ghi.
