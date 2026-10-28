<p align="center">
  <img src="src/Glance.App/Assets/logo-128.png" width="96" alt="Glance logosu">
</p>

<h1 align="center">Glance</h1>

<p align="center">
  <b>Bir kez kur, her şeyi aç — anında.</b><br>
  Windows 10 ve 11 için hızlı, ücretsiz ve açık kaynak görüntü görüntüleyici.
</p>

<p align="center">
  <a href="../../releases/latest"><b>⬇ Son sürümü indir</b></a>
  &nbsp;·&nbsp;
  <a href="README.md">English</a>
</p>

![Ortam ışığı ve film şeridiyle bir fotoğrafı gösteren Glance](docs/screenshots/main.jpg)

## Neden Glance?

- **Windows'un açamadıklarını açar.** iPhone'unuzdaki HEIC, AVIF, WebP, JPEG XL, fotoğraf makinesi RAW dosyaları, Photoshop dosyaları, HDR/EXR — Microsoft Store codec eklentisi gerekmez, başka hiçbir şey kurmanız gerekmez.
- **Hızlıdır.** Görüntüler arka planda ekran boyutunda çözülür, GPU'da çizilir ve gezindiğiniz yöndeki sonraki 16 fotoğraf önceden hazırlanır. Film şeridindeki küçük nokta hangilerinin anında açılacağını gösterir.
- **Explorer küçük resimleri:** Windows'un normalde boş ikon olarak gösterdiği formatlar için de.
- **Gizlidir.** Telemetri yok, hesap yok, internet bağlantısı yok. Fotoğraflarınız bilgisayarınızdan çıkmaz.
- **Ücretsiz ve açık kaynak**, GPL-3.0 lisanslı.

## Özellikler

| | |
|---|---|
| **Görüntüleme** | İmleç etrafında yakınlaştırma, momentumlu kaydırma, sığdır / %100 geçişi, döndürme, tam ekran, dokunmatik ve touchpad hareketleri |
| **Gezinme** | Dosya Gezgini sırasıyla klasör gezinme, küçük resimli film şeridi, sürükle-bırak, son açılanlar, tek pencere |
| **Görünüm** | Mica / Acrylic arka plan, fotoğrafın renklerini yayan ortam ışığı, kayarak geçişler, açık ve koyu tema |
| **Slayt gösterisi** | Yumuşak Ken Burns hareketiyle tam ekran (**S**) |
| **Bilgi paneli** | Canlı RGB histogram, fotoğraf makinesi, lens, pozlama (diyafram · enstantane · ISO · odak uzaklığı), çekim tarihi, GPS konumu (**I**) |
| **Düzenleme** | Kayıpsız JPEG döndürme, kırpma (**C**), kalite ve boyut ayarıyla JPEG / PNG / WebP / AVIF / JPEG XL / TIFF / BMP olarak farklı kaydet (**Ctrl+S**). Orijinal dosya habersizce değiştirilmez: yerinde kaydetmede orijinal önce Geri Dönüşüm Kutusu'na taşınır |
| **Karşılaştırma** | İki fotoğraf yan yana, yakınlaştırma ve kaydırma senkronize (**K**) |
| **Büyüteç** | 8 kat büyüteç ve renk seçici; tıklayınca `#RRGGBB` kopyalanır (**L**) |
| **Renk teması** | Bilgi panelinde fotoğrafın baskın renkleri ve renk çemberi uyumu (tamamlayıcı, üçlü, …); temanın tamamı CSS değişkenleri, Tailwind v4 `@theme` bloğu, SCSS değişkenleri, JSON ya da düz hex listesi olarak kopyalanır veya kaydedilir |
| **Animasyon** | GIF ve animasyonlu WebP; oynat / duraklat ve kare kare ilerleme |
| **Renk** | Gömülü ICC profilleri sRGB'ye dönüştürülür (iPhone'un Display P3 fotoğrafları doğru görünür), HDR ton eşlenir |
| **Güvenlik ağı** | Silme Geri Dönüşüm Kutusu'na gider, 5 saniye içinde geri alınabilir |
| **Diller** | Türkçe ve İngilizce |

## Desteklenen formatlar

| Tür | Formatlar |
|---|---|
| Günlük | JPEG, PNG / APNG, GIF, BMP, TIFF, ICO |
| Modern | **HEIC / HEIF**, **AVIF**, **WebP**, **JPEG XL**, JPEG XR, JPEG 2000, QOI |
| Fotoğraf makinesi RAW | CR2, CR3, NEF, NRW, ARW, DNG, RAF, ORF, RW2, PEF, SRW, 3FR, IIQ, X3F ve fazlası — makinenin önizlemesi anında görünür, yakınlaştırınca tam RAW işlenir |
| Profesyonel | PSD / PSB, TGA, EXR, Radiance HDR, SVG, DDS, PCX, PNM |
| Eski ve özel | XPM / XBM, SGI, Sun raster, WBMP, MNG / JNG, DPX, Cineon, FITS, CUR (imleçler), JPX / JPM |
| Belgeler | PDF (ilk sayfa), PDF uyumlu kaydedilmiş Illustrator `.ai`, gömülü önizlemesi olan EPS — Windows'un kendi PDF motoruyla, Ghostscript olmadan |

Glance HEIC dosyası *yazmaz* (patentli bir format); yalnızca okur.

## Klavye kısayolları

Uygulamada **?** tuşuna basarak hepsini görebilirsiniz.

| Tuşlar | İşlem |
|---|---|
| ← → · PgUp PgDn | Önceki / sonraki görüntü |
| Home · End | Klasördeki ilk / son görüntü |
| Fare tekerleği · + − | Yakınlaştır / uzaklaştır |
| 0 · 1 · çift tık | Pencereye sığdır · gerçek boyut · ikisi arasında geçiş |
| R · Shift+R | Sağa / sola döndür |
| F · F11 · Esc | Tam ekran / çıkış |
| S | Slayt gösterisi |
| B | Ortam ışığını aç / kapat |
| I | Bilgi paneli |
| C · Ctrl+S | Kırp · Farklı kaydet |
| K · L | Karşılaştır · Büyüteç ve renk seçici |
| Boşluk · , · . | Animasyonu oynat / duraklat · önceki / sonraki kare |
| Del · Ctrl+Z | Geri Dönüşüm Kutusu'na taşı · geri al |
| Ctrl+C · Ctrl+Shift+C | Görüntüyü kopyala · dosya yolunu kopyala |
| Ctrl+O · Ctrl+E | Aç · Dosya Gezgini'nde göster |

## Kurulum

[Son sürümden](../../releases/latest) indirin:

- **`Glance-x.y.z-x64-setup.exe`** — önerilen. Yalnızca sizin kullanıcınız için kurulur (yönetici izni gerekmez), Glance'i *Birlikte aç* menüsüne ve *Ayarlar › Uygulamalar › Varsayılan uygulamalar* listesine ekler, Explorer küçük resimlerini etkinleştirir. Diğer uygulamalar gibi *Ayarlar › Uygulamalar*'dan kaldırılır.
- **`Glance-x.y.z-x64-portable.zip`** — istediğiniz yere açın ve `Glance.exe`'yi çalıştırın. İlk açılıştaki pencere aynı entegrasyonu önerir; `Glance.exe --unregister` ile geri alınır.
- **ARM64** sürümleri (Surface Pro X, Snapdragon dizüstüler) de yanlarında.

> Kurulum dosyası henüz kod imzalı değil, bu yüzden Windows SmartScreen ilk açılışta uyarabilir. **Ek bilgi → Yine de çalıştır**'ı seçin. Her sürüm bu depodan GitHub Actions ile derlenir; her indirmenin derleme kaydını kontrol edebilirsiniz.

**Gereksinimler:** Windows 10 sürüm 1809 veya üzeri ya da Windows 11. Başka hiçbir şey — .NET çalışma zamanı ve tüm decoder'lar dahildir.

### Glance'i varsayılan görüntüleyici yapmak

Varsayılan uygulamayı yalnızca siz seçebilirsiniz (Windows kuralı). Glance ilk açılışta *Ayarlar › Varsayılan uygulamalar* sayfasını doğrudan kendi bölümünde açmayı önerir (Windows 11); orada Glance'i seçip varsayılan yapın.

## Gizlilik

Glance hiçbir ağ bağlantısı kurmaz. Telemetri, güncelleme kontrolü ya da hesap yoktur. Tarayıcı yalnızca bilgi panelinde "Haritada aç"a tıkladığınızda açılır. Hata kayıtları bilgisayarınızda, `%LOCALAPPDATA%\Glance\logs` klasöründe kalır.

## Kaynaktan derleme

Gereksinimler: Windows, [.NET 10 SDK](https://dotnet.microsoft.com/download) ve Explorer küçük resim DLL'i için Visual Studio C++ derleme araçları (NativeAOT bağlayıcısı).

```bash
dotnet build src/Glance.App
```

```bash
dotnet test --solution Glance.slnx
```

GitHub Actions'ın yaptığıyla birebir aynı sürüm derlemesi (trim edilmiş, bağımsız, küçük resim eklentisiyle):

```bash
dotnet publish src/Glance.App -c Release -r win-x64 -o out/Glance
```

`v0.2.0` gibi bir etiket push'landığında `.github/workflows/release.yml` x64 ve ARM64 kurulum dosyalarını ve taşınabilir zip'leri derleyip bir GitHub Release'e ekler.

## Nasıl yapıldı

| Proje | Ne yapar |
|---|---|
| `src/Glance.App` | WinUI 3 uygulaması: Win2D tuval, view model, tek örnek aktivasyonu, sistem kaydı |
| `src/Glance.Core` | Arayüzsüz: klasör gezinme ve dosya izleme, bellek sınırlı LRU önbellek, ön yükleme sırası, ayarlar |
| `src/Glance.Imaging` | Decoder'lar: WIC (üretilmiş COM ile), geri kalan her şey için Magick.NET, RAW önizleme tarayıcısı, metadata, düzenleme |
| `src/Glance.Thumbnails` | NativeAOT ile derlenen Explorer küçük resim eklentisi (Explorer'ın sürecine .NET yüklenmez) |
| `tests/` | xUnit v3: decoder'lar, 8 EXIF yönünün tamamı, renk yönetimi, formatlar, düzenleme, sistem kaydı |
| `benchmarks/` | BenchmarkDotNet ile decode ve soğuk başlangıç ölçümleri, uygulama açılış betiği ([ayrıntı](benchmarks/README.md)) |
| `docs/adr` | Mimari karar kayıtları — her seçimin "neden"i |

## Katkı

Hata bildirimleri ve pull request'ler memnuniyetle karşılanır. Çeviriler `src/Glance.App/Strings/<dil>/Resources.resw` dosyalarındadır; yeni bir dil eklemek için `en-US` klasörünü kopyalayın.

## Lisans

Glance, **GNU Genel Kamu Lisansı v3.0 veya sonrası** altında özgür yazılımdır — bkz. [LICENSE](LICENSE).
Glance adı ve logosu bu lisansın kapsamında değildir.
Birlikte gelen üçüncü taraf bileşenler ve lisansları: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
Ekran görüntüsündeki örnek fotoğraf [raw.pixls.us](https://raw.pixls.us) kaynaklıdır (CC0).
