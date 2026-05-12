# 🎵 MusicWidget - VibeCoding Edition

Windows için tasarlanmış, şık, hafif ve şarkının "akustik imzasına" göre renk değiştiren açık kaynaklı bir medya widget'ı.

## ✨ Özellikler
- **Şarkı İmzası (Hash Color):** Her şarkının adı ve sanatçısına özel benzersiz bir renk tonu.
- **Akıllı Koruma:** Windows'un "Masaüstünü Göster" (Win+D) komutuna karşı inatla ekranda kalma özelliği.
- **Konum Hafızası:** Widget'ı bir kez sabitlediğinizde, bir sonraki açılışta milimetrik olarak aynı yerde doğar.
- **Yerel ve Güvenli:** Hiçbir dış servise veya sertifikaya ihtiyaç duymaz, tamamen şeffaf kod yapısı.

## 🛠️ Teknik Detaylar
- **Dil:** C# / .NET
- **Arayüz:** WPF (Windows Presentation Foundation)
- **Ses Analizi:** NAudio üzerinden Wasapi Loopback Capture.
- **Medya Kontrolü:** Windows System Media Transport Controls (SMTC).

## 🚀 Kurulum ve Kullanım
1. Projeyi bilgisayarınıza indirin.
2. `dotnet publish` komutu ile kendi `.exe` dosyanızı oluşturun.
3. Widget'ı istediğiniz yere sürükleyin ve sağ tıklayıp **"Konumu Sabitle"** deyin.

> "Bu proje, hazır ve güvenilmez araçlar yerine 'VibeCoding' mantığıyla tamamen yerel ihtiyaçlar için geliştirilmiştir."