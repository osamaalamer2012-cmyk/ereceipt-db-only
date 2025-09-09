# EReceiptAllInOne — DB only (PostgreSQL), no Redis

## Local dev
```
dotnet restore
dotnet run --urls=http://0.0.0.0:8080
```
SQLite default; set `Storage__SqlProvider=Postgres` and `Storage__ConnectionStrings__Postgres=...` to use Postgres.

## Deploy (Render)
- Create a free **PostgreSQL** database in Render
- Put the connection string in env var `Storage__ConnectionStrings__Postgres`
- Apply `render.yaml` as a Blueprint or create a Web Service from repo
- After first deploy, set `Shortener__ShortBaseUrl` and `Shortener__ViewBaseUrl` to your live URL and redeploy.
