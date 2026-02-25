# CLAUDE.md — AiCon Project Guide

Bu dosya, Claude'un (AI) proje bağlamını hızlıca anlaması ve geliştirme sırasında doğru kararlar vermesi için hazırlanmıştır.

---

## Proje Özeti

**AiCon**, .NET 10 ve .NET Aspire üzerine inşa edilmiş bir REST API'sidir.
Uçuş bacağı (flight leg) değişikliklerini (uçak kaydı ve taşıyıcı değişimleri) analiz eder.
Analiz için **AWS Bedrock** üzerinden **Claude Haiku** modeli kullanılır.
Metin-konuşma dönüşümü için **AWS Polly** kullanılır.
Sonuçlar emoji + başlık + detaylı İngilizce açıklama içeren JSON olarak döner.

---

## Teknoloji Yığını

| Katman | Teknoloji |
|---|---|
| Runtime | .NET 10 |
| Web framework | ASP.NET Core 10 (Minimal API) |
| Orchestration | .NET Aspire 13.1.1 |
| AI / LLM | AWS Bedrock — Claude Haiku (`eu.anthropic.claude-haiku-4-5-20251001-v1:0`) |
| Text-to-Speech | AWS Polly (Neural TTS) |
| AWS SDK (Bedrock) | `AWSSDK.BedrockRuntime` v4.0.16.1 |
| AWS SDK (Polly) | `AWSSDK.Polly` v4.0.3.16 |
| Observability | OpenTelemetry (logging, metrics, tracing) |
| Container | Docker (multi-stage, `mcr.microsoft.com/dotnet/aspnet:10.0`) |

---

## Dizin Yapısı

```
/aicon
├── AiCon.slnx                        # .NET solution (yeni .slnx formatı)
├── Dockerfile                        # Multi-stage production build
├── docker-compose.yml                # Port 8080 → 8080, Production ortamı
├── .env.example                      # Ortam değişkeni şablonu (.env'e kopyala)
├── CLAUDE.md                         # Bu dosya
└── src/
    ├── AiCon.Api/                    # Ana Web API projesi
    │   ├── Models/
    │   │   ├── FlightChange.cs       # Giriş modeli (LegId, uçak reg, taşıyıcı)
    │   │   ├── LegAnalysis.cs        # Çıkış modeli (LegId, title, analysis)
    │   │   └── SpeakRequest.cs       # TTS giriş modeli (Text, VoiceId?)
    │   ├── Services/
    │   │   ├── FlightChangeAnalyzer.cs  # Bedrock ile iletişim + prompt + parse
    │   │   └── TextToSpeechService.cs   # AWS Polly ile metin-konuşma dönüşümü
    │   ├── Settings/
    │   │   ├── BedrockSettings.cs    # Bedrock konfigürasyon POCO (Region, ModelId, auth)
    │   │   └── PollySettings.cs      # Polly konfigürasyon POCO (Region, VoiceId, Engine)
    │   ├── Program.cs                # DI, endpoint tanımları
    │   ├── appsettings.json          # Prod config (Region: eu-west-1)
    │   └── appsettings.Development.json
    ├── AiCon.AppHost/                # Aspire orchestrator
    │   └── Program.cs                # AddProject<AiCon_Api>("aicon-api")
    └── AiCon.ServiceDefaults/        # Paylaşılan OpenTelemetry + health check config
        └── Extensions.cs
```

---

## İstek Akışı (Request Flow)

### Uçuş Analizi (/analyz)

```
HTTP POST /analyz
  │
  ▼
Program.cs endpoint
  │  List<FlightChange> deserialize
  ▼
FlightChangeAnalyzer.AnalyzeAsync()
  │
  ├─► BuildRequest()
  │     • Anthropic Messages API formatında JSON payload oluşturur
  │     • anthropic_version: "bedrock-2023-05-31"
  │     • max_tokens: 512, temperature: 0.5
  │     • BuildPrompt() → uçuş değişikliklerini satır satır özetler
  │
  ├─► AmazonBedrockRuntimeClient.InvokeModelAsync()
  │     • Model: eu.anthropic.claude-haiku-4-5-20251001-v1:0
  │     • Region: eu-west-1
  │
  └─► ParseResponse()
        • response.Body → JSON → content[0].text
        • StripMarkdownCodeFences() (Claude bazen ```json``` ekler)
        • List<LegAnalysis> deserialize
        • Hata → FallbackAnalysis() (⚠️ mesajı döner)
```

### Metin-Konuşma (/speak)

```
HTTP POST /speak
  │
  ▼
Program.cs endpoint
  │  SpeakRequest deserialize (Text, VoiceId?)
  ▼
TextToSpeechService.SynthesizeAsync()
  │
  ├─► SynthesizeSpeechRequest oluştur
  │     • VoiceId: request.VoiceId ?? PollySettings.VoiceId
  │     • OutputFormat: MP3
  │     • Engine: neural (PollySettings.Engine)
  │
  ├─► AmazonPollyClient.SynthesizeSpeechAsync()
  │     • Region: eu-west-1
  │
  └─► response.AudioStream → HTTP response (audio/mpeg)
        • Disk yazımı yok, stream doğrudan döner
```

---

## AWS Bedrock Entegrasyonu

### Model

```
eu.anthropic.claude-haiku-4-5-20251001-v1:0
```

- **Region**: `eu-west-1` (appsettings.json'da tanımlı)
- **Endpoint**: AWS Bedrock Runtime — `InvokeModelAsync`
- **Protokol**: Anthropic Messages API (`bedrock-2023-05-31`)

### Kimlik Doğrulama (Öncelik Sırası)

`FlightChangeAnalyzer` constructor'ı şu sırayla auth yöntemi seçer:

1. **API Key** (`Bedrock:ApiKey` dolu ise):
   `AWS_BEARER_TOKEN_BEDROCK` env variable olarak set edilir.
   `AnonymousAWSCredentials` ile client oluşturulur (SigV4 bypass).

2. **AccessKey + SecretKey** (`Bedrock:AccessKey` ve `Bedrock:SecretKey` dolu ise):
   `BasicAWSCredentials` kullanılır.

3. **Default credential chain** (IAM role, environment variables, `~/.aws/credentials`):
   `new AmazonBedrockRuntimeClient(region)` — AWS SDK otomatik bulur.

> **Not:** `CreateClientWithApiKey2` metodu mevcuttur ama kullanılmıyor (`Authorization: Bearer` header yaklaşımı). Aktif metot `CreateClientWithApiKey` olup env variable yöntemini kullanır.

### Prompt Yapısı

`BuildPrompt()` her `FlightChange` için şu formatı üretir:

```
• LegId LEG-001: Aircraft Reg: TC-JFG → TC-KLM
• LegId LEG-002: Carrier: TK → PC
• LegId LEG-003: Aircraft Reg: TC-AAA → TC-BBB | Carrier: TK → XQ
```

Model şu kurallara göre yanıt verir:
- ✈ sadece uçak kaydı değişikliği
- 🔄 sadece taşıyıcı değişikliği
- ⚠️ hem uçak hem taşıyıcı değişikliği
- ℹ️ bilgilendirici / küçük değişiklik

### Beklenen Model Çıktısı

```json
[
  {
    "legId": "LEG-001",
    "title": "✈ Aircraft Registration Change: TC-JFG → TC-KLM",
    "analysis": "The aircraft registration has changed from TC-JFG to TC-KLM..."
  }
]
```

---

## AWS Polly Entegrasyonu

`TextToSpeechService` AWS Polly ile metin-konuşma dönüşümü yapar ve MP3 stream döner.

### Kimlik Doğrulama (Öncelik Sırası)

1. **AccessKey + SecretKey** (`Polly:AccessKey` ve `Polly:SecretKey` dolu ise):
   `BasicAWSCredentials` kullanılır.

2. **Default credential chain** (IAM role, environment variables, `~/.aws/credentials`):
   `new AmazonPollyClient(region)` — AWS SDK otomatik bulur.

> **Not:** Polly Bearer token auth'u desteklemez — IAM credentials veya default chain kullanılmalıdır.

### Ses Konfigürasyonu

| Ayar | appsettings.json değeri | Açıklama |
|---|---|---|
| `Region` | `eu-west-1` | AWS bölgesi |
| `VoiceId` | `Matthew` | Polly ses ID (Amy, Joanna, Matthew, vb.) |
| `Engine` | `neural` | `neural` veya `standard` |

---

## Konfigürasyon

### appsettings.json

```json
{
  "Bedrock": {
    "Region": "eu-west-1",
    "ModelId": "eu.anthropic.claude-haiku-4-5-20251001-v1:0",
    "MaxTokens": 2048,
    "ApiKey": "",
    "AccessKey": "",
    "SecretKey": ""
  },
  "Polly": {
    "Region": "eu-west-1",
    "VoiceId": "Matthew",
    "Engine": "neural",
    "AccessKey": "",
    "SecretKey": ""
  }
}
```

`BedrockSettings.SectionName = "Bedrock"` ve `PollySettings.SectionName = "Polly"` ile bağlanır.

> **Not:** `MaxTokens` ayarı şu an `BuildRequest` içinde kullanılmıyor (sabit 512); ileride `_settings.MaxTokens` ile değiştirilebilir.

### Ortam Değişkenleri (Docker / Production)

```
# Genel
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:8080

# AWS Bedrock
Bedrock__Region=eu-west-1
Bedrock__ModelId=eu.anthropic.claude-haiku-4-5-20251001-v1:0
Bedrock__MaxTokens=2048
Bedrock__ApiKey=...           # Bearer token (API key auth)

# AWS Polly
Polly__Region=eu-west-1
Polly__VoiceId=Matthew
Polly__Engine=neural
Polly__AccessKey=...          # IAM credentials
Polly__SecretKey=...
```

### .env.example (Docker Compose için)

`.env.example` kopyalanarak `.env` oluşturulur. `.env` `.gitignore`'dadır, commit edilmemelidir.

```env
# AWS Bedrock
BEDROCK_API_KEY=
BEDROCK_REGION=eu-west-1
BEDROCK_MODEL_ID=eu.anthropic.claude-haiku-4-5-20251001-v1:0
BEDROCK_MAX_TOKENS=2048

# AWS Polly
POLLY_ACCESS_KEY=
POLLY_SECRET_KEY=
POLLY_REGION=eu-west-1
POLLY_VOICE_ID=Matthew
POLLY_ENGINE=neural
```

---

## API Endpoint'leri

### GET /

```
200 OK
"Hello this is an api with .net 10 and aspire framework"
```

### POST /analyz

**Request:**
```json
[
  {
    "legId": "LEG-001",
    "previousAcRegNo": "TC-JFG",
    "currentAcRegNo": "TC-KLM",
    "previousCarrier": null,
    "currentCarrier": null
  },
  {
    "legId": "LEG-002",
    "previousAcRegNo": null,
    "currentAcRegNo": null,
    "previousCarrier": "TK",
    "currentCarrier": "PC"
  }
]
```

**Response (200 OK):**
```json
[
  {
    "legId": "LEG-001",
    "title": "✈ Aircraft Registration Change: TC-JFG → TC-KLM",
    "analysis": "The aircraft registration has been updated..."
  },
  {
    "legId": "LEG-002",
    "title": "🔄 Carrier Change: TK → PC",
    "analysis": "The operating carrier has changed..."
  }
]
```

**Response (400 Bad Request):**
```json
{ "error": "changes list cannot be empty" }
```

### POST /speak

**Request:**
```json
{
  "text": "Flight LEG-001 has an aircraft registration change from TC-JFG to TC-KLM.",
  "voiceId": "Amy"
}
```

- `text`: Seslendirmek istenen metin (zorunlu, Polly limiti ~3000 karakter)
- `voiceId`: Opsiyonel ses override (ör. `"Amy"`, `"Joanna"`, `"Matthew"`); verilmezse `PollySettings.VoiceId` kullanılır

**Response (200 OK):**
- Content-Type: `audio/mpeg`
- Body: MP3 binary stream (disk'e yazılmaz, doğrudan stream edilir)

**Response (400 Bad Request):**
```json
{ "error": "text cannot be empty" }
```

### GET /health & GET /alive

Aspire health check endpoint'leri (`MapDefaultEndpoints` tarafından eklenir).

---

## .NET Aspire Mimarisi

### AppHost (Orchestrator)

`src/AiCon.AppHost/Program.cs` Aspire'ın giriş noktasıdır:

```csharp
var builder = DistributedApplication.CreateBuilder(args);
builder.AddProject<Projects.AiCon_Api>("aicon-api");
builder.Build().Run();
```

- Aspire dashboard: `http://localhost:15023` (dev)
- OTLP endpoint: `http://localhost:19240`
- Resource service: `http://localhost:20182`

### ServiceDefaults (Paylaşılan Yapılandırma)

`AiCon.ServiceDefaults` projesi `IsAspireSharedProject=true` olarak işaretlidir.
`builder.AddServiceDefaults()` çağrısı şunları ekler:

- **OpenTelemetry**: ASP.NET Core + HttpClient tracing, Runtime metrics
- **Health checks**: `/health` (detaylı) ve `/alive` (canlılık)
- **Service discovery**: Aspire üzerinden otomatik servis keşfi altyapısı

---

## Çalıştırma

### Aspire ile (Geliştirme — Önerilen)

```bash
dotnet run --project src/AiCon.AppHost
# Aspire dashboard: https://localhost:17106
# API: https://localhost:7209
```

### Sadece API

```bash
dotnet run --project src/AiCon.Api
# http://localhost:5267
```

### Docker

```bash
docker build -t aicon-api .
docker run -p 8080:8080 \
  -e Bedrock__ApiKey=... \
  -e Polly__AccessKey=... \
  -e Polly__SecretKey=... \
  aicon-api
```

### Docker Compose

```bash
# .env.example'ı .env'e kopyala ve değerleri doldur
cp .env.example .env

docker-compose up
# http://localhost:8080
```

### Test (AiCon.Api.http)

`src/AiCon.Api/AiCon.Api.http` dosyası VS Code REST Client veya Rider ile kullanılabilir.

---

## Önemli Detaylar ve Gotcha'lar

1. **Model bölge prefix'i**: `eu.anthropic.claude-haiku-4-5-20251001-v1:0` — `eu.` prefix'i AWS cross-region inference için gereklidir. Bölge değişirse model ID'si de güncellenmeli.

2. **Markdown kod blokları**: Claude bazen talimat verilmesine rağmen JSON'u ` ```json ``` ` içinde döndürür. `StripMarkdownCodeFences()` bunu temizler.

3. **MaxTokens kullanımı**: `BedrockSettings.MaxTokens` şu an `BuildRequest` içinde kullanılmıyor; payload'da sabit `512` var. İleride `_settings.MaxTokens` ile değiştirilebilir.

4. **`CreateClientWithApiKey2` devre dışı**: Eski `Authorization: Bearer` header yaklaşımı. Aktif metot `CreateClientWithApiKey` olup env variable kullanır.

5. **Solution formatı**: `.slnx` (yeni format). Eski `dotnet sln` komutları çalışmayabilir; `dotnet` 10 SDK gerekir.

6. **Singleton servisler**: `FlightChangeAnalyzer` ve `TextToSpeechService` singleton olarak kayıtlıdır; AWS client'lar tek instance üzerinden paylaşılır (thread-safe).

7. **Polly ses limiti**: AWS Polly tek istekte ~3000 karakter sınırı uygular. Uzun metinler için SynthesisTask API kullanılmalıdır (şu an implemente edilmemiş).

---

## Geliştirme Sırasında Claude'a İpuçları

- Yeni analiz yeteneği eklenecekse → `FlightChangeAnalyzer` ve `Models/` düzenle.
- Prompt değişikliği → `BuildPrompt()` metodu (`FlightChangeAnalyzer.cs`).
- TTS ses/motor değişikliği → `appsettings.json` `Polly` bölümü veya `PollySettings.cs`.
- TTS servis mantığı değişikliği → `TextToSpeechService.cs`.
- Yeni AWS servisi entegrasyonu → `AiCon.AppHost/Program.cs`'e ekle, `ServiceDefaults`'a dependency ekle.
- Yeni endpoint → `Program.cs` (`src/AiCon.Api/Program.cs`).
- Bedrock auth değişikliği → `FlightChangeAnalyzer` constructor + `BedrockSettings`.
- Polly auth değişikliği → `TextToSpeechService` constructor + `PollySettings`.
- OpenTelemetry konfigürasyonu → `AiCon.ServiceDefaults/Extensions.cs`.
- Docker ortam değişkenleri → `docker-compose.yml` + `.env.example`.
