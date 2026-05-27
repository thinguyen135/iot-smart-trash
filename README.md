# iot-smart-trash

Dự án Thùng rác thông minh phân loại tự động sử dụng AI (Gemini Flash) và kiến trúc IoT Phân tán (Pub/Sub) mô phỏng trên Wokwi.

## Giới thiệu
Hệ thống IST là một giải pháp AIoT giúp tự động nhận diện và phân loại rác thải thành 3 nhóm (Hữu cơ, Nhựa, Giấy). Thay vì xử lý nguyên khối, dự án được thiết kế theo kiến trúc phân tán (Distributed Architecture) nhằm tối ưu hiệu năng vi điều khiển và tăng tính mở rộng cho phần mềm Backend.

## Kiến trúc Hệ thống (System Architecture)
Dự án bao gồm 3 module hoạt động độc lập, giao tiếp với nhau qua giao thức HTTP REST API thông qua Firebase Realtime Database:
Việc 2 mạch ESP32 không nói chuyện trực tiếp với nhau mà thông qua một cơ sở dữ liệu trung gian (Firebase) được gọi là mô hình Publish-Subscribe (Pub/Sub) gián tiếp qua Cloud.
Về mặt giao thức mạng tầng dưới, hai con ESP32 đang giao tiếp với Firebase bằng Giao thức HTTP REST API (sử dụng các phương thức GET để lấy dữ liệu về và PUT để đẩy dữ liệu lên)


1. **ESP32 - pir (Tang tren):** Nhận diện rác cảm biến PIR, điều khiển mở nắp và gửi tín hiệu Trigger (`obstacle = 1`) lên Cloud.
<img width="1920" height="1080" alt="Screenshot (272)" src="https://github.com/user-attachments/assets/467f5faa-6c4a-4096-9040-4c28edcf60f8" />

2. **PYTHON (Picture_and_Ai):** Lắng nghe sự kiện từ Firebase (`obstacle = 1`), cho phép upload ảnh rác --> Lưu trữ lịch sử lên firebase Storage -->Gọi API Google Gemini phân tích --> Đẩy kết quả nhận diện (H, N, G) ngược lên Cloud.
<img width="1920" height="1080" alt="image" src="https://github.com/user-attachments/assets/86ad5e0c-b00f-4f8b-b23e-ec1c9267adf9" />


3. **ESP32 - servo (Tang duoi):** Lắng nghe kết quả AI từ Cloud để điều khiển hệ thống Servo gạt rác vào đúng thùng. Đồng thời liên tục giám sát bằng sóng siêu âm (HC-SR04) để cập nhật trạng thái "Đầy thùng" (FULL) theo thời gian thực.
<img width="1920" height="1080" alt="Screenshot (270)" src="https://github.com/user-attachments/assets/efe38023-3a13-410c-9be4-ad640273cff6" />

4. **WEB**
Web hiển thị ảnh rác, độ đầy, kết quả AI

5. **Firebase:** chứa các dữ liệu từ cảm biến
trong file data_cloud.json

6. **Supabase:** Chứa ảnh rác để AI phân loại
<img width="1920" height="1080" alt="image" src="https://github.com/user-attachments/assets/a2adf7c8-254b-4bb1-b462-646f9dd947ea" />



##  Tính năng nổi bật
* **Event-Driven AI:** Gọi AI phân tích hình ảnh hoàn toàn tự động dựa trên tín hiệu phần cứng.
* **Database Realtime:** Tốc độ đồng bộ tín hiệu điều khiển giữa phần cứng và phần mềm máy tính cực nhanh.
* **Storage Tối ưu:** Tích hợp Supabase để quản lý và lưu trữ hình ảnh rác thải.
* **Cảnh báo đầy thùng:** Tự động giám sát độ cao rác trong 3 ngăn chứa riêng biệt.

##  Công nghệ & Ngôn ngữ sử dụng
* **Mô phỏng IoT:** Wokwi Platform, C/C++ (ESP32)
* **Backend Application:** C# WinForms (.NET Framework)
* **Cloud Database:** Firebase Realtime Database
* **Cloud Storage:** Supabase (PostgreSQL lõi)
* **Artificial Intelligence:** Google Gemini 2.5 Flash API (Zero-shot Image Classification)

##  Hướng dẫn Setup & Chạy thử nghiệm
### 1. (Wokwi)
* Truy cập link dự án Wokwi hoặc chạy bằng PlatformIO: 
    - Tang_tren (tương đương esp32-cam ngoài đời): https://wokwi.com/projects/464352887695258625
    - Tang duoi (tương đương arduino nano ngoài đời): https://wokwi.com/projects/464894275895112705
* Bấm **Play** để khởi động 2 mạch ESP32.


### 2. Phần mềm (Python)
* Mở Picture_and_a=Ai project bằng **Visual Studio Codde**.
* Cập nhật API Keys vào file `form_backend.cs`:

* Nhấn **Start (F5)** để chạy ứng dụng lắng nghe.

### 3. Kịch bản Demo
1. Tang_tren, kích hoạt cảm biến PIR (Simulate Motion).
2. Ứng dụng Python sẽ bật thông báo --> Chọn một file ảnh rác bất kỳ.
3. Chờ AI xử lý.
4. Xem kết quả gạt Servo và trạng thái thùng rác trên LCD/Terminal của Tang_duoi.

### 4. Chạy mô phỏng
* Tang_tren
<img width="1920" height="1080" alt="Screenshot (271)" src="https://github.com/user-attachments/assets/ec8e7e97-9084-41ea-9fff-425cd31958dd" />

* Tang_duoi
<img width="1920" height="1080" alt="Screenshot (269)" src="https://github.com/user-attachments/assets/12c83c2a-75b5-455a-b704-93e57237aa00" />

* Picture_and_AI
<img width="1920" height="1080" alt="Screenshot (274)" src="https://github.com/user-attachments/assets/f24ace67-8a63-4a7b-aca0-d41e71a8109a" />

* Web
<img width="1920" height="1080" alt="image" src="https://github.com/user-attachments/assets/eabb49a6-dc14-4176-8a42-6dac03e13957" />
