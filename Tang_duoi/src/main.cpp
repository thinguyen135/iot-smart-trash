#include <Arduino.h>
#include <WiFi.h>
#include <HTTPClient.h>
#include <ESP32Servo.h>

const char* ssid = "Wokwi-GUEST";
const char* password = "";

String firebaseBaseUrl = "https://iot-smart-trash-212d9-default-rtdb.firebaseio.com";

const int EMPTY_DISTANCE_CM = 30;
const int FULL_DISTANCE_CM = 5;

Servo servoTang1;
Servo servoTang2;

#define PIN_SERVO_TANG1 13
#define PIN_SERVO_TANG2 14

#define TRIG_HC_HUUCO 27
#define ECHO_HC_HUUCO 15

#define TRIG_HC_NHUA 18
#define ECHO_HC_NHUA 19

#define TRIG_HC_GIAY 32
#define ECHO_HC_GIAY 33

int doKhoangCach(int trigPin, int echoPin) {
  digitalWrite(trigPin, LOW);
  delayMicroseconds(2);
  digitalWrite(trigPin, HIGH);
  delayMicroseconds(10);
  digitalWrite(trigPin, LOW);

  long duration = pulseIn(echoPin, HIGH, 30000);
  if (duration == 0) return 999;

  return duration * 0.034 / 2;
}

int tinhPhanTram(int distanceCm) {
  if (distanceCm >= EMPTY_DISTANCE_CM || distanceCm == 999) return 0;
  if (distanceCm <= FULL_DISTANCE_CM) return 100;

  int percent = (EMPTY_DISTANCE_CM - distanceCm) * 100 / (EMPTY_DISTANCE_CM - FULL_DISTANCE_CM);
  return constrain(percent, 0, 100);
}

void sendToFirebase(String path, String data) {
  if (WiFi.status() != WL_CONNECTED) {
    Serial.println("[Firebase PUT] WiFi mat ket noi, bo qua " + path);
    return;
  }

  HTTPClient http;
  http.begin(firebaseBaseUrl + path);
  http.setTimeout(3000);
  http.addHeader("Content-Type", "application/json");
  int httpCode = http.PUT(data);
  Serial.print("[Firebase PUT] ");
  Serial.print(path);
  Serial.print(" -> HTTP ");
  Serial.println(httpCode);
  http.end();
}

String getFromFirebase(String path) {
  String payload = "";
  if (WiFi.status() != WL_CONNECTED) return payload;

  HTTPClient http;
  http.begin(firebaseBaseUrl + path);
  http.setTimeout(3000);
  int httpCode = http.GET();
  if (httpCode > 0) {
    payload = http.getString();
    payload.replace("\"", "");
  }
  http.end();
  return payload;
}

void sendBinState(String binKey, int distanceCm) {
  int percent = tinhPhanTram(distanceCm);
  String status = distanceCm <= FULL_DISTANCE_CM ? "FULL" : "NOT_FULL";

  String payload = "{";
  payload += "\"distanceCm\":" + String(distanceCm) + ",";
  payload += "\"percent\":" + String(percent) + ",";
  payload += "\"status\":\"" + status + "\",";
  payload += "\"updatedAt\":{\".sv\":\"timestamp\"}";
  payload += "}";

  sendToFirebase("/bins/" + binKey + ".json", payload);
}

void printBinState(String label, int distanceCm) {
  int percent = tinhPhanTram(distanceCm);
  String status = distanceCm <= FULL_DISTANCE_CM ? "FULL" : "NOT_FULL";

  Serial.print(label);
  Serial.print(": ");
  Serial.print(distanceCm);
  Serial.print(" cm, ");
  Serial.print(percent);
  Serial.print("%, ");
  Serial.println(status);
}

void setup() {
  Serial.begin(115200);
  Serial.println("\n=== TANG_DUOI FIREBASE BIN MONITOR V2 ===");
  Serial.print("\nKet noi WiFi...");

  WiFi.begin(ssid, password, 6);
  while (WiFi.status() != WL_CONNECTED) {
    delay(500);
    Serial.print(".");
  }
  Serial.println("\nWiFi OK!");

  pinMode(TRIG_HC_HUUCO, OUTPUT);
  pinMode(ECHO_HC_HUUCO, INPUT);
  pinMode(TRIG_HC_NHUA, OUTPUT);
  pinMode(ECHO_HC_NHUA, INPUT);
  pinMode(TRIG_HC_GIAY, OUTPUT);
  pinMode(ECHO_HC_GIAY, INPUT);

  ESP32PWM::allocateTimer(0);
  ESP32PWM::allocateTimer(1);

  servoTang1.attach(PIN_SERVO_TANG1);
  servoTang2.attach(PIN_SERVO_TANG2);

  servoTang1.write(90);
  servoTang2.write(90);

  Serial.println("ESP32 PHAN LOAI - San sang nhan lenh tu AI!");
  Serial.println("Dang doc Firebase va gui bins/huuco, bins/nhua, bins/giay moi 1 giay...");
}

void phanLoaiHuuCo() {
  Serial.println("-> Thuc thi: Gat rac HUU CO");
  servoTang1.write(45);
  delay(1500);
  servoTang1.write(90);
}

void phanLoaiNhua() {
  Serial.println("-> Thuc thi: Gat rac NHUA");
  servoTang1.write(135);
  delay(500);
  servoTang2.write(45);
  delay(1500);
  servoTang1.write(90);
  servoTang2.write(90);
}

void phanLoaiGiay() {
  Serial.println("-> Thuc thi: Gat rac GIAY");
  servoTang1.write(135);
  delay(500);
  servoTang2.write(135);
  delay(1500);
  servoTang1.write(90);
  servoTang2.write(90);
}

void loop() {
  Serial.println("\n--- LOOP TANG DUOI ---");
  
  // 1. Kiểm tra xem có lệnh phân loại từ tầng trên / AI không
  String obstacleState = getFromFirebase("/sensors/pir/obstacle.json");
  
  if (obstacleState == "1") {
    String trashCategory = ""; 
    Serial.print("[ESP32 Tang duoi] Dang doi AI phan loai");

    int cnt = 0;
    bool receivedResult = true;
    
    // 2. Chờ AI trả kết quả (tối đa 10 giây)
    while (trashCategory == "" || trashCategory == "null") {
      delay(1000);
      Serial.print(".");
      cnt++;

      if (cnt > 10) {
        sendToFirebase("/sensors/pir/obstacle.json", "0");
        Serial.println("\n[LỖI] Khong nhan duoc phan hoi tu AI (Timeout). Huy bo!");
        receivedResult = false;
        break;
      }

      trashCategory = getFromFirebase("/trash_type/category.json");
    }

    // 3. Tiến hành phân loại nếu lấy được kết quả
    if (receivedResult) {
      Serial.println(); // Xuống dòng cho đẹp log
      if (trashCategory == "H" || trashCategory == "N" || trashCategory == "G") {
        Serial.println("[NHAN LENH] AI tra ve ket qua: " + trashCategory);

        // Chạy servo
        if (trashCategory == "H") phanLoaiHuuCo();
        else if (trashCategory == "N") phanLoaiNhua();
        else if (trashCategory == "G") phanLoaiGiay();

        // Xong việc thì reset Firebase để tầng trên biết
        sendToFirebase("/sensors/pir/obstacle.json", "0");
        sendToFirebase("/trash_type/category.json", "\"\"");
        Serial.println("[ESP32] Da reset obstacle/category, san sang luot tiep theo.\n");
      } else {
        Serial.println("[LỖI] Ma rac khong hop le: " + trashCategory);
        sendToFirebase("/sensors/pir/obstacle.json", "0"); // Gặp lỗi vẫn phải reset cờ
      }
    }
  }

  // 4. Cập nhật dung lượng 3 thùng rác (luôn chạy kể cả khi không có rác rơi)
  int kcHuuCo = doKhoangCach(TRIG_HC_HUUCO, ECHO_HC_HUUCO);
  int kcNhua = doKhoangCach(TRIG_HC_NHUA, ECHO_HC_NHUA);
  int kcGiay = doKhoangCach(TRIG_HC_GIAY, ECHO_HC_GIAY);

  printBinState("Huu co", kcHuuCo);
  printBinState("Nhua", kcNhua);
  printBinState("Giay", kcGiay);

  sendBinState("huuco", kcHuuCo);
  sendBinState("nhua", kcNhua);
  sendBinState("giay", kcGiay);

  delay(1000);
}
