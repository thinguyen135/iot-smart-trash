#include <Arduino.h>
#include <WiFi.h>
#include <HTTPClient.h>
#include <ESP32Servo.h>

const char* ssid = "Wokwi-GUEST";
const char* password = "";

String firebaseBaseUrl = "https://iot-smart-trash-212d9-default-rtdb.firebaseio.com";

int sensorPin = 27;

Servo servoNap;
#define PIN_SERVO_NAP 12

#define TRIG_NAP 25
#define ECHO_NAP 26

const int LID_OPEN_DISTANCE_CM = 30;
const int LID_RESET_DISTANCE_CM = 35;
const unsigned long PIR_TRIGGER_COOLDOWN_MS = 12000;

bool lidAlreadyOpened = false;
unsigned long lastPirTriggerMs = 0;

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

void putFirebase(String path, String jsonValue) {
  if (WiFi.status() != WL_CONNECTED) {
    Serial.println("[Firebase PUT] WiFi mat ket noi, bo qua " + path);
    return;
  }

  HTTPClient http;
  http.begin(firebaseBaseUrl + path);
  http.setTimeout(3000);
  http.addHeader("Content-Type", "application/json");
  int httpCode = http.PUT(jsonValue);
  Serial.print("[Firebase PUT] ");
  Serial.print(path);
  Serial.print(" = ");
  Serial.print(jsonValue);
  Serial.print(" -> HTTP ");
  Serial.println(httpCode);
  http.end();
}

String getFirebase(String path) {
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
  Serial.print("[Firebase GET] ");
  Serial.print(path);
  Serial.print(" -> HTTP ");
  Serial.print(httpCode);
  Serial.print(", value=");
  Serial.println(payload);
  http.end();
  return payload;
}

void setup() {
  Serial.begin(115200);
  Serial.println("\n=== TANG_TREN FIREBASE TRIGGER V2 ===");
  pinMode(sensorPin, INPUT);
  pinMode(TRIG_NAP, OUTPUT);
  pinMode(ECHO_NAP, INPUT);

  servoNap.attach(PIN_SERVO_NAP);
  servoNap.write(90);

  Serial.print("Connecting to WiFi");
  WiFi.begin(ssid, password, 6);
  while (WiFi.status() != WL_CONNECTED) {
    delay(500);
    Serial.print(".");
  }
  Serial.println(" Connected!");
  Serial.println("Dang doc cam bien nap/PIR va gui sensors/pir/obstacle len Firebase...");
}

void loop() {
  Serial.println("\n--- LOOP TANG TREN ---");
  int khoangCachNap = doKhoangCach(TRIG_NAP, ECHO_NAP);
  Serial.print("Khoang cach nap: ");
  Serial.print(khoangCachNap);
  Serial.println(" cm");

  if (khoangCachNap > 0 && khoangCachNap < LID_OPEN_DISTANCE_CM && !lidAlreadyOpened) {
    Serial.println("Moi bo rac vao!");
    servoNap.write(0);
    delay(5000);
    servoNap.write(90);
    lidAlreadyOpened = true;
    Serial.println("Da dong nap, cho cam bien ra xa de mo lai lan tiep theo.");
  }

  if (khoangCachNap >= LID_RESET_DISTANCE_CM || khoangCachNap == 999) {
    if (lidAlreadyOpened) {
      Serial.println("Cam bien nap da ra xa, san sang mo nap lan moi.");
    }
    lidAlreadyOpened = false;
  }

  int pirState = digitalRead(sensorPin);
  Serial.print("PIR state: ");
  Serial.println(pirState);

  if (pirState == 1 && WiFi.status() == WL_CONNECTED && millis() - lastPirTriggerMs > PIR_TRIGGER_COOLDOWN_MS) {
    Serial.println("\n[ESP32 Tang tren] Co rac, gui obstacle = 1 len Firebase...");
    putFirebase("/sensors/pir/obstacle.json", "1");
    lastPirTriggerMs = millis();

    String trashCategory = "";
    Serial.print("[ESP32 Tang tren] Dang doi AI phan loai");

    int cnt = 0;
    bool receivedResult = true;
    while (trashCategory == "" || trashCategory == "null") {
      delay(1000);
      Serial.print(".");
      cnt++;

      if (cnt > 10) {
        putFirebase("/sensors/pir/obstacle.json", "0");
        Serial.println("\nKhong nhan duoc phan hoi tu AI");
        receivedResult = false;
        break;
      }

      trashCategory = getFirebase("/trash_type/category.json");
    }

    if (receivedResult) {
      Serial.println("\n[ESP32 Tang tren] AI da phan loai xong!");
      Serial.print("[ESP32 Tang tren] Loai rac: ");
      Serial.println(trashCategory);
      delay(5000);
    }
  } else if (pirState == 1) {
    Serial.println("PIR dang HIGH nhung dang trong thoi gian cooldown, khong gui obstacle lap lai.");
  }

  delay(1000);
}
