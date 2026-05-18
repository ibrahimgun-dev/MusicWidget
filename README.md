# 🎵 MusicWidget - VibeCoding Edition (v1.1)

Windows için tasarlanmış, şık, hafif ve şarkının "akustik imzasına" göre renk değiştiren açık kaynaklı bir medya widget'ı.

## ✨ Özellikler
- **Global Kısayol Tuşu (Yeni!):** `Ctrl + Shift + M` kombinasyonu ile widget'ı anında gizleyin veya geri çağırın. Arka planda ses analizi ve akıllı koruma sistemi kesintisiz çalışmaya devam eder.
- **Şarkı İmzası (Hash Color):** Her şarkının adı ve sanatçısına özel benzersiz bir renk tonu üretilir (MD5 algoritması ile).
- **Akıllı Koruma:** Windows'un "Masaüstünü Göster" (Win+D) komutuna karşı inatla ekranda kalma özelliği.
- **Konum Hafızası:** Widget'ı bir kez sabitlediğinizde, bir sonraki açılışta milimetrik olarak aynı yerde doğar (Sıfır zıplama optimizasyonu).
- **Yerel ve Güvenli:** Hiçbir dış servise veya sertifikaya ihtiyaç duymaz, tamamen şeffaf kod yapısı.

## 🛠️ Teknik Detaylar
- **Dil:** C# / .NET
- **Arayüz:** WPF (Windows Presentation Foundation)
- **Ses Analizi:** NAudio üzerinden Wasapi Loopback Capture ile donanım seviyesinde spektrum okuma.
- **Medya Kontrolü:** Windows System Media Transport Controls (SMTC).
- **Sistem Entegrasyonu:** Görünürlük döngüsü ve Anti-Minimize koruması için düşük seviyeli Win32 API kancaları (Hook).

## 🚀 Kurulum ve Kullanım
1. [Releases](https://github.com/ibrahimgun-dev/MusicWidget/releases) sekmesinden en güncel sürümü indirin veya projeyi yerelinize klonlayın.
2. Bağımsız bir `.exe` oluşturmak için terminalde şu komutu çalıştırın:
   `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`
3. Çıktı alınan Widget'ı istediğiniz yere sürükleyin ve sağ tıklayıp **"Konumu Sabitle"** (Pin) deyin.
4. İstediğiniz an ekranda gizlemek veya göstermek için klavyeden **Ctrl + Shift + M** tuşlarına basın.

> *"Bu proje, hazır ve güvenilmez araçlar yerine 'VibeCoding' mantığıyla tamamen yerel ihtiyaçlar için geliştirilmiştir."*
