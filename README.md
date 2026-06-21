# 🎵 MusicWidget v1.5

[![MusicWidget v1.5 Showcase](https://img.youtube.com/vi/5It_-2x_BNM/0.jpg)](https://www.youtube.com/watch?v=5It_-2x_BNM)

Windows için geliştirilmiş, görev çubuğuna kenetlenen, odak çalmayan (No-Activate) şık bir medya widget'ı.

## ⬇️ Download & İndirme

**[MusicWidget.exe — v1.5](https://github.com/ibrahimgun-dev/MusicWidget/releases/download/v1.5/MusicWidget.exe)**

> Ek kurulum gerekmez. İndirin, çalıştırın.

## 🚀 Öne Çıkan Özellikler

- **Native Taskbar Docking:** Görev çubuğuna sürükle-bırak ile kenetleme.
- **Focus-Immune (WS_EX_NOACTIVATE):** Widget ile etkileşime girdiğinde aktif pencerenden odağı asla çalmaz.
- **Privacy Cloak (Ctrl+Shift+M):** Widget'ı anında gizle/göster.
- **Anti-Win+D:** Windows'un "Masaüstünü Göster" komutuna dirençli, ekranda sabit kalır.
- **Visual Hash:** Şarkı adına özel renk tonu.
- **Tema Seçeneği:** Sağ tık → Tema → Koyu / Saydam / Açık.

## 🛠️ Kaynaktan Derleme

```
git clone https://github.com/ibrahimgun-dev/MusicWidget
cd MusicWidget
dotnet publish -c Release
```

Çıkan exe: `bin/Release/net8.0-windows10.0.19041.0/win-x64/publish/MusicWidget.exe`

## ⚙️ Teknik Detaylar

- **Framework:** C# / .NET 8, WPF
- **Audio:** NAudio 2.3.0 (WasapiLoopback)
- **Architecture:** Win32 API Interop (DPI Aware)

---
> "Bu proje, hazır ve güvenilmez araçlar yerine 'VibeCoding' mantığıyla  şişkin kütüphaneler olmadan, doğrudan sistem seviyesinde tamamen yerel ihtiyaçlar için geliştirilmiştir."

