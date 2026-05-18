# FloodLevelIOT-New (backend)

ASP.NET WebAPI trong `HCM_Flood_Level/WebAPI`. Dự báo cho công dân dùng **Google Gemini** (`Infrastructure/Services/FloodForecastService.cs`).

## Gemini

API key đọc từ `Gemini:ApiKey` hoặc biến môi trường `GEMINI_API_KEY`. Model mặc định: `gemini-2.0-flash` (ghi đè bằng `Gemini:Model` / `GEMINI_MODEL`).

- **Docker Compose:** trước khi `docker compose up`, export key (PowerShell):

  ` $env:GEMINI_API_KEY = "<key>"; docker compose up --build `

  Hoặc tạo file `.env` cạnh `docker-compose.yml` chứa `GEMINI_API_KEY=...` (Compose tự dùng file này khi substitute biến).

- **Chạy local `dotnet run`:** file `appsettings*.json` không commit; dùng User Secrets trong thư mục WebAPI:

  ` dotnet user-secrets set "Gemini:ApiKey" "<key>" --project HCM_Flood_Level/WebAPI `

Endpoint (cần JWT role Citizen): `POST /api/citizen/flood-forecast/run` với body `{ "latitude", "longitude", "radiusKm" }`.