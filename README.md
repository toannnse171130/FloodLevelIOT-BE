# FloodLevelIOT-New (backend)

ASP.NET WebAPI trong `HCM_Flood_Level/WebAPI`. Dự báo cho công dân dùng **Google Gemini** (`Infrastructure/Services/FloodForecastService.cs`).

## Gemini

API key đọc từ `Gemini:ApiKey` hoặc biến môi trường `GEMINI_API_KEY`. Model mặc định: `gemini-2.0-flash` (ghi đè bằng `Gemini:Model` / `GEMINI_MODEL`).

- **Docker Compose:** trước khi `docker compose up`, export key (PowerShell):

  ` $env:GEMINI_API_KEY = "<key>"; docker compose up --build `

  Hoặc tạo file `.env` cạnh `docker-compose.yml` chứa `GEMINI_API_KEY=...` (Compose tự dùng file này khi substitute biến).

- **Chạy local `dotnet run`:** file `appsettings*.json` không commit; dùng User Secrets trong thư mục WebAPI:

  ` dotnet user-secrets set "Gemini:ApiKey" "<key>" --project HCM_Flood_Level/WebAPI `

Endpoint (cần JWT role Citizen): `POST /api/citizen/flood-forecast/run` với body `{ "latitude", "longitude", "radiusKm", "dataDaysBack" }`.

## Deploy Railway

1. Push code lên GitHub repo `FloodLevelIOT-BE` (Railway thường auto-deploy từ `main`):
   ```powershell
   cd FloodLevelIOT-New
   git push origin main
   ```
2. Trên [Railway](https://railway.app) → project **floodleveliot-be-production** → **Variables**:
   - `ConnectionStrings__DefaultConnection` — Postgres URL
   - `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience`, `Jwt__ExpiresMinutes`
   - `Gemini__ApiKey` — key Google AI (không commit vào git)
   - `Gemini__Model` = `gemini-2.0-flash` (tùy chọn)
3. Service dùng **Dockerfile** ở root (đã có `railway.toml`). Port **8080**.
4. Sau deploy: mở `https://floodleveliot-be-production.up.railway.app/swagger`