// Abbild 自作コントローラー（プロトコル v2）
//
// 送る（20 回/秒）: AB2,心拍bpm,ボタン,加速度X(mg),Y(mg),Z(mg),方向,湿度%,気温℃
//   ボタン : 1 = 決定(A)  2 = もどる(B)
//   方向   : 0 なし / 1 上 / 2 下 / 3 左 / 4 右
// 受け取る（1 行 1 命令。WinForms 版と同じ文字）:
//   LED  : r g b y p w o m c（赤 緑 青 黄 紫 白 橙 桃 水色）
//   音   : N（ピッ）S（成功）D（ダメージ）F（炎）I（氷）T（雷）H（回復）P（毒）
//   OLED : O:ENC O:ATK O:MAG O:DMG O:WIN（USE_OLED を有効にしたときだけ）
//
// 部品（例）: Arduino Uno / 脈波センサー（PulseSensor 互換・アナログ）/ ADXL335（アナログ 3 軸）
//            / タクトスイッチ 6 個 / DHT11 / RGB LED（カソードコモン）/ 圧電ブザー
// 必要なライブラリ: DHT sensor library（Adafruit）。OLED を使うなら Adafruit SSD1306 と GFX。

#include <DHT.h>

// ---- ピン ----
const int PIN_PULSE = A0;
const int PIN_AX = A1, PIN_AY = A2, PIN_AZ = A3;
const int PIN_BTN_A = 2, PIN_BTN_B = 3;
const int PIN_UP = 4, PIN_DOWN = 5, PIN_LEFT = 6, PIN_RIGHT = 7;
const int PIN_DHT = 8;
const int PIN_LED_R = 9, PIN_LED_G = 10, PIN_LED_B = 11;
const int PIN_BUZZER = 12;

// ---- ADXL335 の調整（3.3V 給電・5V 基準の 10bit ADC のとき、0g ≒ 337、1g ≒ 68 カウント）----
const float ACC_ZERO = 337.0;
const float ACC_PER_G = 68.0;

// #define USE_OLED
#ifdef USE_OLED
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>
Adafruit_SSD1306 oled(128, 64, &Wire, -1);
#endif

DHT dht(PIN_DHT, DHT11);

// ---- 心拍（脈波のピークの間隔から出す）----
float pulseAvg = 512, pulseAmp = 30;
bool pulseAbove = false;
unsigned long lastBeat = 0;
unsigned long intervals[6];
int intervalCount = 0, intervalPos = 0;
int bpm = 0;

void updatePulse(unsigned long now) {
  int raw = analogRead(PIN_PULSE);
  pulseAvg += (raw - pulseAvg) * 0.02;
  float dev = raw - pulseAvg;
  pulseAmp += (abs(dev) - pulseAmp) * 0.02;
  float th = max(8.0, pulseAmp * 0.6);
  if (!pulseAbove && dev > th) {
    pulseAbove = true;
    if (lastBeat > 0) {
      unsigned long iv = now - lastBeat;
      if (iv > 300 && iv < 1600) {
        intervals[intervalPos] = iv;
        intervalPos = (intervalPos + 1) % 6;
        if (intervalCount < 6) intervalCount++;
        unsigned long sum = 0;
        for (int i = 0; i < intervalCount; i++) sum += intervals[i];
        bpm = (int)(60000UL * intervalCount / sum);
      }
    }
    lastBeat = now;
  } else if (pulseAbove && dev < th * 0.3) {
    pulseAbove = false;
  }
  if (now - lastBeat > 3000) { bpm = 0; intervalCount = 0; }
}

// ---- LED と音 ----
void led(int r, int g, int b) {
  analogWrite(PIN_LED_R, r);
  analogWrite(PIN_LED_G, g);
  analogWrite(PIN_LED_B, b);
}

void play(const int* notes, const int* lens, int n) {
  for (int i = 0; i < n; i++) {
    if (notes[i] > 0) tone(PIN_BUZZER, notes[i], lens[i]);
    delay(lens[i] + 10);
  }
  noTone(PIN_BUZZER);
}

void handleCommand(String c) {
  c.trim();
  if (c.length() == 0) return;
#ifdef USE_OLED
  if (c.startsWith("O:")) { drawOled(c.substring(2)); return; }
#endif
  if (c.length() != 1) return;
  switch (c[0]) {
    case 'r': led(255, 0, 0); break;
    case 'g': led(0, 255, 0); break;
    case 'b': led(0, 0, 255); break;
    case 'y': led(255, 180, 0); break;
    case 'p': led(160, 0, 255); break;
    case 'w': led(255, 255, 255); break;
    case 'o': led(255, 80, 0); break;
    case 'm': led(255, 0, 140); break;
    case 'c': led(0, 255, 255); break;
    case 'N': tone(PIN_BUZZER, 1200, 40); break;
    case 'S': { const int n[] = {1047, 1319, 1568, 2093}; const int l[] = {60, 60, 60, 120}; play(n, l, 4); break; }
    case 'D': { const int n[] = {330, 220, 110}; const int l[] = {60, 60, 120}; play(n, l, 3); break; }
    case 'F': { const int n[] = {200, 260, 200, 300}; const int l[] = {40, 40, 40, 80}; play(n, l, 4); break; }
    case 'I': { const int n[] = {2093, 2637, 3136}; const int l[] = {50, 50, 100}; play(n, l, 3); break; }
    case 'T': { const int n[] = {1500, 400, 1800, 300}; const int l[] = {30, 30, 30, 90}; play(n, l, 4); break; }
    case 'H': { const int n[] = {784, 988, 1175, 1568}; const int l[] = {70, 70, 70, 140}; play(n, l, 4); break; }
    case 'P': { const int n[] = {300, 360, 280}; const int l[] = {80, 80, 120}; play(n, l, 3); break; }
  }
}

#ifdef USE_OLED
void drawOled(String a) {
  oled.clearDisplay();
  oled.setTextSize(2);
  oled.setTextColor(SSD1306_WHITE);
  oled.setCursor(8, 24);
  if (a == "ENC") oled.print("ENCOUNTER");
  else if (a == "ATK") oled.print("ATTACK!");
  else if (a == "MAG") oled.print("MAGIC!");
  else if (a == "DMG") oled.print("DAMAGE");
  else if (a == "WIN") oled.print("VICTORY!");
  oled.display();
}
#endif

// ---- メイン ----
String line;
unsigned long lastSend = 0, lastDht = 0;
float humidity = 0, temperature = 0;

void setup() {
  Serial.begin(9600);
  pinMode(PIN_BTN_A, INPUT_PULLUP);
  pinMode(PIN_BTN_B, INPUT_PULLUP);
  pinMode(PIN_UP, INPUT_PULLUP);
  pinMode(PIN_DOWN, INPUT_PULLUP);
  pinMode(PIN_LEFT, INPUT_PULLUP);
  pinMode(PIN_RIGHT, INPUT_PULLUP);
  pinMode(PIN_LED_R, OUTPUT);
  pinMode(PIN_LED_G, OUTPUT);
  pinMode(PIN_LED_B, OUTPUT);
  pinMode(PIN_BUZZER, OUTPUT);
  dht.begin();
#ifdef USE_OLED
  oled.begin(SSD1306_SWITCHCAPVCC, 0x3C);
  oled.clearDisplay();
  oled.display();
#endif
  led(0, 0, 60);
}

void loop() {
  unsigned long now = millis();
  updatePulse(now);

  while (Serial.available()) {
    char ch = (char)Serial.read();
    if (ch == '\n' || ch == '\r') { handleCommand(line); line = ""; }
    else if (line.length() < 16) line += ch;
  }

  if (now - lastDht >= 1000) {
    lastDht = now;
    float h = dht.readHumidity();
    float t = dht.readTemperature();
    if (!isnan(h)) humidity = h;
    if (!isnan(t)) temperature = t;
  }

  if (now - lastSend >= 50) {
    lastSend = now;
    int ax = (int)((analogRead(PIN_AX) - ACC_ZERO) / ACC_PER_G * 1000);
    int ay = (int)((analogRead(PIN_AY) - ACC_ZERO) / ACC_PER_G * 1000);
    int az = (int)((analogRead(PIN_AZ) - ACC_ZERO) / ACC_PER_G * 1000);
    int buttons = (digitalRead(PIN_BTN_A) == LOW ? 1 : 0) | (digitalRead(PIN_BTN_B) == LOW ? 2 : 0);
    int dir = digitalRead(PIN_UP) == LOW ? 1 : digitalRead(PIN_DOWN) == LOW ? 2 : digitalRead(PIN_LEFT) == LOW ? 3 : digitalRead(PIN_RIGHT) == LOW ? 4 : 0;
    Serial.print("AB2,");
    Serial.print(bpm); Serial.print(',');
    Serial.print(buttons); Serial.print(',');
    Serial.print(ax); Serial.print(',');
    Serial.print(ay); Serial.print(',');
    Serial.print(az); Serial.print(',');
    Serial.print(dir); Serial.print(',');
    Serial.print(humidity, 1); Serial.print(',');
    Serial.println(temperature, 1);
  }
}
