# OpenSteamTool GUI — Windows için masaüstü yönetim aracı

[English](../README.md) · [Windows indirmeleri](https://github.com/maliv2/OpenSteamToolGUI/releases) · [Uyumluluk](../COMPATIBILITY.md) · [Hata bildirimi](https://github.com/maliv2/OpenSteamToolGUI/issues)

**OpenSteamTool GUI (OpenSteamToolGUI)**, Windows 10 ve Windows 11 x64 için ücretsiz ve açık kaynaklı bir OpenSteamTool yönetim uygulamasıdır. C# ve .NET 10 WPF ile geliştirilmiştir. Resmî OpenSteamTool sürümlerini kurmak ve güncellemek, Steam Lua dosyaları ile depot manifest dosyalarını içe aktarmak, yedekleri yönetmek ve ayarları düzenlemek için grafik arayüz sunar.

Bu uygulama bağımsız bir projedir. Valve veya OpenSteamTool geliştiricileri tarafından yayımlanmaz. [Resmî OpenSteamTool deposu](https://github.com/OpenSteam001/OpenSteamTool) ayrı bir projedir.

## Özellikler

- OpenSteamTool kurulumu, güncellemesi, devre dışı bırakılması ve kaldırılması.
- Oyun adı veya AppID ile arama; Lua, manifest ve ZIP dosyaları için içe aktarma önizlemesi.
- Dosya değişikliklerini onaylamadan önce hedefleri ve çakışmaları inceleme.
- Yönetilen değişiklikler için yedekleme ve geri yükleme.
- Ayarlar, tanılama, Koyu, Açık ve Sistem görünümü.
- Türkçe ve İngilizce dâhil on arayüz dili.

![OpenSteamTool GUI Windows masaüstü uygulamasının kurulum ve yedekleme paneli](screenshots/dashboard.png)

## İndirme ve başlangıç

[GitHub Releases](https://github.com/maliv2/OpenSteamToolGUI/releases) sayfasındaki yayımlanmış Windows sürümlerini kullanın. Henüz bir sürüm listelenmiyorsa [kaynak koddan derleme yönergelerini](../README.md#build) izleyin.

- **Portable:** .NET 10'u içerir.
- **Lightweight:** .NET 10 Desktop Runtime kurulumu gerektirir.

Her ZIP tek bir `OpenSteamToolGUI.exe` içerir. ZIP'i çıkarın, uygulamayı çalıştırın ve gerekirse `steam.exe` dosyasının bulunduğu Steam klasörünü seçin. İlk açılış dili İngilizcedir; App Settings bölümünden Türkçeyi seçebilirsiniz.

Lua, manifest ve ZIP içe aktarmaları önizleme ve onay gerektirir. Farklı içeriğe sahip mevcut dosyalar, değiştirmeyi seçmediğiniz sürece korunur. Steam çalışıyorsa yönetilen DLL değişiklikleri öncesinde kapatma onayı istenir ve işlem sonrasında Steam yeniden başlatılır; çalışan oyunlar etkilenebilir.

Bir dosyanın kaydedilmesi Steam veya OpenSteamTool tarafından uygulandığını kanıtlamaz. Sürüm sınırları için [COMPATIBILITY.md](../COMPATIBILITY.md) belgesini inceleyin.

## Destek ve lisans

[GitHub Issues](https://github.com/maliv2/OpenSteamToolGUI/issues) üzerinden hata bildirebilir veya özellik isteyebilirsiniz. Uygulama sürümünü, Windows sürümünü ve hatayı yeniden üretme adımlarını belirtin; kişisel bilgileri tanılama çıktısından çıkarın.

GUI [MIT lisansıyla](../LICENSE) sunulur. Yalnızca yönetme yetkiniz olan yazılım, hesap ve dosyalarla kullanın. Ayrıntılı kullanım, derleme ve sorumluluk açıklaması için [İngilizce README](../README.md) belgesine bakın.
