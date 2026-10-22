# Despliegue (M14)

Cómo poner el sistema en internet en **un servidor propio (VPS) con Docker**. Todo lo necesario está en `deploy/`.
Lo armé y probé por partes en esta PC (sin Docker, que no está instalado acá): la API en modo Production, la compilación para
Linux con SkiaSharp, la Row Level Security con un rol sin privilegios y todas las pruebas de punta a punta. **El
`docker compose` completo no se pudo levantar acá**: la primera vez hacerlo con calma y revisar los logs.

## Qué se levanta

| Servicio | Qué es | Puerto |
|---|---|---|
| `caddy` | HTTPS automático (Let's Encrypt) + el panel React ya compilado | 80 y 443 (los únicos abiertos) |
| `api` | La API (.NET). Aplica las migraciones al arrancar y corre la limpieza cada hora | interno 8080 |
| `public` | La tienda pública (Razor Pages) | interno 8080 |
| `postgres` | La base | interno 5432 |
| `respaldo` | `pg_dump` diario en `deploy/respaldos/` (últimos 14 días) | — |

Dos dominios: el **panel** (`panel.midominio.com`: React + `/api` + `/archivos`) y las **tiendas**
(`tienda.midominio.com/{slug}`). Los dos tienen que apuntar (registro A) a la IP del servidor **antes** de levantar Caddy,
si no Let's Encrypt no entrega el certificado.

## Servidor recomendado

Un VPS chico alcanza para empezar (decisión pendiente #7 del plan): 2 GB de RAM, 1–2 vCPU, 40 GB de disco, Ubuntu 24.04.
Hetzner, DigitalOcean, Vultr o Contabo (US$5–10/mes). Más cerca de Bolivia (São Paulo / Miami) = menos demora.

## Primera vez

```bash
# 1. Docker
curl -fsSL https://get.docker.com | sh

# 2. Los DOS repos, uno al lado del otro (Caddy compila el panel desde ../Hermes.Web)
mkdir -p /opt/hermes && cd /opt/hermes
git clone <repo-backend> Hermes
git clone <repo-front> Hermes.Web

# 3. Configuración (nunca se sube a git)
cd Hermes/deploy
cp .env.example .env
nano .env        # dominios, email, POSTGRES_PASSWORD, APP_DB_PASSWORD, JWT_SECRET, INSTRUCCIONES_DE_PAGO
#   openssl rand -hex 32      → para cada contraseña de Postgres (solo letras y números)
#   openssl rand -base64 48   → para JWT_SECRET

# 4. Firewall: solo SSH, 80 y 443
ufw allow OpenSSH && ufw allow 80 && ufw allow 443 && ufw enable

# 5. Levantar
docker compose up -d --build
docker compose ps            # todos "healthy" / "running"
docker compose logs -f api   # las migraciones se aplican solas al arrancar
```

Probar: `https://panel.midominio.com` (registrarse), `https://panel.midominio.com/salud` → `{"estado":"ok"}`.

## El administrador de la plataforma

Se da SOLO por comando (nunca desde la web). Primero registrarse en el panel con esa cuenta, después:

```bash
docker compose exec api dotnet Hermes.Api.dll promover-admin tu@email.com
```

Hay que cerrar sesión y volver a entrar para que aparezca "Administración".

## Actualizar a una versión nueva

```bash
cd /opt/hermes/Hermes && git pull && cd ../Hermes.Web && git pull
cd ../Hermes/deploy && docker compose up -d --build
```

Las migraciones nuevas se aplican solas al arrancar la API (`Base__MigrarAlArrancar`). Antes de una actualización grande,
hacer un respaldo a mano (abajo).

## Respaldos

- Automático: uno por día en `deploy/respaldos/hermes-AAAAMMDD-HHMM.dump`, se guardan `DIAS_RESPALDO` días.
- **Copiarlos fuera del servidor** (si el servidor se pierde, se pierden con él). Por ejemplo con `rclone` a Google Drive o
  a un bucket, en un cron diario:
  `0 4 * * * rclone copy /opt/hermes/Hermes/deploy/respaldos remoto:hermes-respaldos`
- A mano: `docker compose exec respaldo sh -c 'pg_dump --format=custom --file=/respaldos/manual-$(date +%s).dump'`
- **Restaurar** (borra lo que haya y pone el respaldo):

```bash
docker compose stop api public
docker compose exec -T postgres pg_restore -U hermes -d hermes --clean --if-exists < respaldos/hermes-AAAAMMDD-HHMM.dump
docker compose start api public
```

**Probar una restauración de verdad al menos una vez** (en otra máquina o en una base aparte): un respaldo que nunca se
restauró no es un respaldo.

## Seguridad que ya viene puesta

- **HTTPS** obligatorio (Caddy redirige y renueva los certificados; HSTS). La cookie de sesión sale con `Secure`.
- **Proxy**: la API y el sitio público solo confían en `X-Forwarded-For` de la red interna de Docker (`Proxy__Redes__*`); así el
  límite por IP ve la IP real del cliente y nadie de afuera puede falsificarla.
- **Row Level Security** (segunda barrera multi-tienda): la app entra con `hermes_app`, un rol sin privilegios (lo crea
  `deploy/postgres-init/01-rol-de-la-app.sh` la primera vez que arranca Postgres). Postgres mismo no le deja ver ni tocar filas de
  otra tienda aunque una consulta se escape del filtro de EF. Las migraciones y los respaldos usan el dueño (`hermes`).
  **Ojo**: el script de inicio corre SOLO con el volumen vacío. Si la base ya existía, crear el rol a mano (el mismo SQL del
  script, con `psql` como `hermes`) y darle permisos sobre las tablas existentes:
  `GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA hermes TO hermes_app; GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA hermes TO hermes_app;`
- **Secretos por variables de entorno** (`.env`), nunca en `appsettings.json`. Scalar/OpenAPI no se publican en producción.
- Los contenedores .NET corren con un usuario sin privilegios; Postgres no tiene puertos abiertos al exterior.

## Monitoreo

- `GET /salud` en los dos dominios: 200 si la base responde, 503 si no. Conectarlo a un monitor gratuito (UptimeRobot, Better
  Stack) para que avise por email/Telegram si se cae.
- **Errores**: con `SENTRY_DSN` en `.env` (cuenta gratuita en sentry.io) cada error 500 llega a Sentry con su contexto (sin datos
  personales: `SendDefaultPii = false`). Sin DSN no se manda nada afuera.
- **Logs**: `docker compose logs -f api` (JSON de una línea por evento). Cada pedido HTTP queda con método, ruta, código y
  duración (sin la consulta `?…`).

## Limpieza automática

La API corre cada hora (`Limpieza__*`): borra los refresh tokens vencidos y las fotos que nadie usa (subidas hace más de 24 h
que no quedaron en ningún producto, logo ni comprobante). Con varias instancias de la API, dejarla encendida en una sola.

## Conectar con Instagram (M12)

Sin esto, todo funciona y las dueñas pegan el texto de sus publicaciones a mano. Para que las publicaciones lleguen solas
hace falta una **app de Meta** (gratis). Según la documentación de Meta (octubre de 2026; **verificar en el panel de Meta**,
cambia seguido):

1. En [developers.facebook.com](https://developers.facebook.com) crear una app (tipo *Business*) y agregarle el producto
   **Instagram → "API con inicio de sesión de Instagram"** (*Instagram API with Instagram Login*: no necesita página de Facebook).
2. En la configuración de ese producto:
   - **Valid OAuth Redirect URIs**: `https://panel.midominio.com/instagram/conectado` (exacto).
   - Permiso: `instagram_business_basic` (solo leer el perfil y las publicaciones).
   - Copiar el **Instagram App ID** y el **Instagram App Secret** a `.env` (`INSTAGRAM_APP_ID`, `INSTAGRAM_APP_SECRET`) y generar
     `INSTAGRAM_CLAVE_CIFRADO` (`openssl rand -base64 32`).
3. **Mientras la app no pase la revisión de Meta** ("Standard Access") solo se pueden conectar las cuentas que vos agregues a la
   app como **evaluadoras/testers de Instagram** (y que acepten la invitación desde Instagram). Para que cualquier tienda pueda
   conectar su cuenta hace falta **"Advanced Access"**: pedir la **revisión de la app** (App Review) para
   `instagram_business_basic`, con un video mostrando el uso y la política de privacidad publicada (`/privacidad`). Meta puede
   pedir además la **verificación del negocio**.
4. Las cuentas que se conectan tienen que ser **profesionales** (empresa o creador). Una personal se pasa gratis desde la app de
   Instagram (Configuración → Tipo de cuenta).

La API trae lo nuevo cada 30 minutos (`Instagram__IntervaloMinutos`) y renueva sola el permiso de 60 días. Todo esto se probó
contra un Meta FALSO que imita las respuestas documentadas (`scripts/meta_falso.py`); **la primera conexión real hay que
mirarla con calma** (`docker compose logs -f api`).

## Más adelante

- **Fotos a Cloudflare R2** cuando haya muchas tiendas (ver `Almacenamiento:Tipo` en docs/GUIA.md): hoy viven en Postgres y entran
  en los respaldos.
- Más de un servidor: sacar `BloqueoDeLogin` (en memoria) a una caché compartida y migrar aparte (no al arrancar).
