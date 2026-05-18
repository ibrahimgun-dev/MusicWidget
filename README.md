# 🎵 MusicWidget (v1.2 - Global Edition)

A lightweight, open-source, and zero-dependency desktop music widget built with C# and WPF. It captures system audio natively to render a hardware-based spectrum visualizer and dynamically generates colors based on the playing track.

Abonelik sistemlerine ve dayatmacı yazılımlara karşı; tamamen yerel, hafif, açık kaynaklı ve görev çubuğuna gömülebilen C# WPF müzik widget'ı.

---

## 📺 Project Showcase & VibeCoding Story
Click the image below to watch how this widget was built, its features, and the "Anti-Minimize" demonstration in action!

[![MusicWidget Showcase](https://img.youtube.com/vi/5GT3bpoNrGc/0.jpg)](https://youtu.be/5GT3bpoNrGc)

---

## ✨ Features / Özellikler

### English 🌐
- **Pixel-Perfect Taskbar Integration:** Exactly 48px height to blend seamlessly into the Windows 11 taskbar without overflowing.
- **Fluent Semi-Transparent Look:** Transparent background (`#33808080`) mimicking the Windows 11 widget style.
- **Multi-language Support:** Instantly switch between English and Turkish via the right-click menu. (Saves preference to `lang.txt`).
- **Windows Resistent (Anti-Minimize):** Resists `Win+D` or "Show Desktop" commands. It stays anchored to your desktop/taskbar.
- **Mathematical Hash Color:** Dynamically generates a unique HSL color palette based on the track title and artist name.
- **Hardware Audio Spectrum:** Real-time system audio capture using `NAudio` (WasapiLoopbackCapture) with zero lag.
- **Global Hotkey:** Toggle widget visibility instantly from anywhere using `Ctrl + Shift + M`.

### Türkçe 🇹🇷
- **Milimetrik Görev Çubuğu Entegrasyonu:** Windows 11 görev çubuğuna (48px) tam oturan, taşma yapmayan mikro kapsül tasarımı.
- **Saydam Modern Görünüm:** Görev çubuğu dokusunu arkadan hissettiren yarı saydam (`#33808080`) şık arka plan.
- **Canlı Dil Desteği:** Sağ tık menüsünden anında İngilizce ve Türkçe arasında geçiş (Tercihinizi `lang.txt` içinde hatırlar).
- **Windows'a Direnen Yapı (Anti-Minimize):** `Win+D` yapıldığında veya masaüstünü göster dendiğinde gizlenmez, yerini terk etmez.
- **Matematiksel Akustik Renk İmzası:** Şarkı adı ve sanatçı bilgisinin Hash kodundan dinamik ve benzersiz renk paleti üretir.
- **Donanımsal Ses Spektrumu:** `NAudio` altyapısı ile ses kartından gecikmesiz, anlık frekans yakalama ve görselleştirme.
- **Global Kısayol Tuşu:** `Ctrl + Shift + M` kombinasyonu ile widget'ı her yerden anında gizleyebilir veya gösterebilirsiniz.

---

## ⌨️ Controls & Shortcuts / Kontroller ve Kısayollar

- `Ctrl + Shift + M` : Toggle Visibility (Gizle / Göster)
- `Mouse Left Click + Drag` : Move Widget (Eğer konum sabitlenmemişse sürükle)
- `Right Click` : Opens Context Menu / Sağ Tık Menüsü:
  - **Pin Position / Konumu Sabitle** (Sürüklemeyi kapatır ve konumu `pos.txt` içine kaydeder)
  - **Language / Dil** (Anında EN/TR arası canlı geçiş yapar)
  - **Exit / Çıkış**

---

## 📦 How to Run / Nasıl Çalıştırılır?

1. Go to the [Releases](https://github.com/ibrahimgun-dev/MusicWidget/releases) section.
2. Download the latest `MusicWidget.exe`.
3. Run it! (No installation required, single portable file).

*Not: Uygulama ilk açıldığında ekranın sağ altına hizalanır. Sağ tıklayıp "Konumu Sabitle" tikini kaldırarak görev çubuğunuzun üzerine sürükleyebilir, ardından tekrar sabitleyebilirsiniz.*

---

## 🛠️ Built With
- **C# / .NET 8**
- **WPF (Windows Presentation Foundation)**
- **NAudio** - For hardware-level loopback audio capture
- **Windows.Media.Control** - For global system media transport handling

---
Concept developed under the **VibeCoding** philosophy. Pure logic, zero bloatware. Made by [ibrahimgun-dev](https://github.com/ibrahimgun-dev).