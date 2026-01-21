# QGST - Quick GPU Selector Tool

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet" alt=".NET 10">
  <img src="https://img.shields.io/badge/Platform-Windows-0078D6?style=flat-square&logo=windows" alt="Windows">
</p>

> **[Click here for English README](README.md)** 

<p align="center">
  <strong>Windows çoklu GPU sistemleri için basit ve güçlü GPU seçimi</strong>
</p>

QGST, uygulamalarınızı hangi GPU'nun çalıştıracağını seçmenizi sağlar. Entegre + harici GPU'lu dizüstü bilgisayarlar veya birden fazla ekran kartına sahip masaüstü sistemler için mükemmel.

## ✨ Özellikler

- 🎮 **Tek Seferlik Çalıştırma** - Uygulamaları belirli bir GPU ile başlatın (tercih çıkışta otomatik geri alınır)
- 💾 **Varsayılan Olarak Ata** - Uygulamalara kalıcı GPU tercihi atayın
- 🖱️ **Bağlam Menüsü** - `.exe`, `.lnk`, `.bat`, `.cmd`, `.url` dosyalarına sağ tıklayın
- 🎯 **Akıllı Algılama** - Tüm GPU'ları tanımlar, özdeş modelleri ayırt eder
- 🌍 **Çok Dilli** - İngilizce ve Türkçe desteği
- ⚡ **CLI Aracı** - Otomasyon için tam komut satırı arayüzü
- 🔒 **Çökme Güvenli** - Uygulama çökse bile tercihleri otomatik geri alır

## 📋 Gereksinimler

- **İşletim Sistemi**: Windows 10 (1803+) veya Windows 11
- **.NET**: .NET 10 Runtime (x64)
- **GPU**: Modern bir GPU (DirectX 11+ önerilir)

> **Not**: GPU Tercih Deposu için Windows 10 1803+ gereklidir. Eski sürümlerde sınırlı destek vardır.

## 🚀 Kurulum

1. [Releases](../../releases) sayfasından en son sürümü indirin
2. Herhangi bir klasöre çıkartın (örn. `C:\Tools\QGST`)
3. `QGST.UI.exe` dosyasını çalıştırın
4. Ayarlar → **Bağlam Menüsünü Kaydet**

Hepsi bu kadar! Tamamen taşınabilir, kurulum gerektirmez.

## 📖 Kullanım

### Bağlam Menüsü (En Hızlı Yol)

Herhangi bir `.exe`, `.lnk`, `.bat`, `.cmd` veya `.url` dosyasına sağ tıklayın:

- **GPU ile Çalıştır (Tek Seferlik)** - GPU seçin, uygulama bir kez çalışsın, tercih geri alınsın
- **Varsayılan GPU Olarak Ata** - GPU seçin, tercih kalıcı olarak kaydedilsin
- **QGST Değişikliklerini Sıfırla** - GPU tercihini kaldır

### Grafik Arayüz

```powershell
QGST.UI.exe [--target <yol>] [--gpu <id>] [--one-time|--set-default]
```

**Seçenekler:**

| Seçenek | Açıklama |
|---------|----------|
| `--target <yol>` | Hedef uygulamayı önceden yükle |
| `--gpu <id>` | GPU'yu önceden seç |
| `--one-time` / `--set-default` | Mod seç |
| `--reset` | Tercihleri sıfırla |

### Komut Satırı (CLI)

```powershell
qgst <komut> [seçenekler]
```

#### Komutlar

| Komut | Açıklama |
|-------|----------|
| `list-gpus` | Tüm GPU'ları listele |
| `resolve` | Kısayol/batch dosyalarını çözümle |
| `run` | Uygulamayı belirtilen GPU ile çalıştır |
| `set-default` | Kalıcı GPU tercihi ayarla |
| `reset` | Tercihleri sıfırla |
| `export-backup` | Yapılandırmayı yedekle |
| `import-backup` | Yedekten geri yükle |
| `register-context-menu` | Explorer bağlam menüsünü ekle |
| `unregister-context-menu` | Bağlam menüsünü kaldır |
| `diagnostics` | Sistem tanılamalarını dışa aktar |

#### Örnekler

```powershell
# GPU'ları listele
qgst list-gpus

# JSON çıktısı
qgst list-gpus --json

# Bir oyunu belirli GPU ile çalıştır (tek seferlik)
qgst run --target "C:\Oyunlar\Oyun.exe" --gpu 1 --one-time

# Kalıcı GPU ata
qgst set-default --target "C:\Oyunlar\Oyun.exe" --gpu 0

# Belirli bir uygulamanın tercihini sıfırla
qgst reset --target "C:\Oyunlar\Oyun.exe"

# Her şeyi sıfırla
qgst reset --all

# Tanılamaları dışa aktar
qgst diagnostics --out "tanilamalar.json"
```

## 🔧 Nasıl Çalışır

QGST, Windows GPU Tercih Deposu'na yazar:
```
HKCU\Software\Microsoft\DirectX\UserGpuPreferences
```

**Tercih Değerleri:**
- `1` = Güç Tasarrufu (entegre GPU)
- `2` = Yüksek Performans (harici GPU)
- Birden fazla harici GPU için: Hassas hedefleme için LUID/Device ID kullanır

**Tek Seferlik Mod:**
1. Mevcut tercihi kaydet
2. İstenen GPU'yu ayarla
3. Uygulamayı başlat ve çıkış bekle
4. Orijinal tercihe geri dön
5. QGST çökerse otomatik temizleme

**Dosya Çözümleme:**
- `.lnk` → Windows Shell üzerinden çözümleme
- `.bat`/`.cmd` → Çalıştırılabilir yolları için ayrıştırma
- `.url` → Steam oyunlarını algılama

## 📂 Veri Konumu

`%LOCALAPPDATA%\QGST\`

```
QGST/
├── config/         # Ayarlar ve eşlemeler
├── state/          # Uygulanan tercihler, bekleyen geri almalar
├── cache/          # GPU envanter önbelleği
├── logs/           # Günlük dosyalar
├── backup/         # Yapılandırma yedekleri
└── locales/        # Dil dosyaları
```

## 🏗️ Proje Yapısı

```
QGST/
├── QGST.Core/       # Çekirdek kütüphane
│   ├── Models/      # Veri modelleri
│   ├── Services/    # İş mantığı
│   └── Data/        # Yerelleştirme dosyaları
├── QGST.UI/         # WPF arayüzü
└── QGST.CLI/        # Komut satırı aracı
```

## 🛠️ Kaynak Koddan Derleme

**Gereksinimler:** .NET 10 SDK, Windows 10 SDK

```powershell
git clone https://github.com/yourusername/QGST.git
cd QGST
dotnet build -c Release

# Çıktı: build/Release/
```

## 🌍 Yerelleştirme

**Desteklenen:** İngilizce (en), Türkçe (tr)

**Yeni dil eklemek için:**
1. `QGST.Core/Data/locales/en.json` dosyasını `de.json` olarak kopyalayın
2. Tüm değerleri çevirin
3. `LocalizationService.cs` içindeki `AvailableLanguages` dizisini güncelleyin

## 🔍 Sorun Giderme

**GPU algılanamıyor**
- GPU sürücülerini güncelleyin
- Çalıştırın: `qgst list-gpus --refresh`

**Bağlam menüsü görünmüyor**
- Ayarlar → Bağlam Menüsünü Kaydet
- Explorer'ı yeniden başlatın: `Stop-Process -Name explorer -Force`

**Tercih çalışmıyor**
- Bazı UWP uygulamaları GPU seçimini desteklemez
- Oyun .exe dosyasını doğrudan çalıştırmayı deneyin (başlatıcı yerine)
- NVIDIA/AMD çakışan ayarları kontrol edin

**Tanılamaları dışa aktar:**
```powershell
qgst diagnostics --out tanilamalar.json
```

**Tam sıfırlama:**
```powershell
qgst reset --all
```

## 🤝 Katkıda Bulunma

Katkılar hoş karşılanır!

1. Repository'yi fork edin
2. Özellik dalı oluşturun: `git checkout -b feature/harika`
3. Değişiklikleri commit edin: `git commit -m 'Harika özellik ekle'`
4. Push yapın: `git push origin feature/harika`
5. Pull Request açın

**Katkı alanları:** Çeviriler, hata düzeltmeleri, özellikler, dokümantasyon, UI/UX iyileştirmeleri.

---

<p align="center">
  <strong>Çoklu GPU sistemleri için ❤️ ile yapıldı</strong>
  <br>
  <sub>© 2026 QGST Projesi</sub>
</p>
