# Hermes · base de datos (PostgreSQL 14+)

Esquema `hermes`, multi-tienda: toda tabla de negocio cuelga de `tiendas.id`.

| Archivo | Qué hace |
|---|---|
| `001_schema.sql` | Tablas, tipos y restricciones |
| `002_logica.sql` | Triggers (stock automático), `recibir_compra()`, `anular_venta()` y vistas |
| `003_seed_demo.sql` | Datos de la tienda demo "Luna Accesorios" (solo desarrollo) |

## Crear la base

```bash
createdb hermes
psql -d hermes -v ON_ERROR_STOP=1 -f 001_schema.sql -f 002_logica.sql -f 003_seed_demo.sql
```

## Instalación local en esta PC

PostgreSQL 17.5 portable (sin servicio de Windows ni permisos de administrador) en `C:\Users\Sebas\postgres`.
Puerto 5432, usuario `postgres`, base `hermes`. La contraseña está en `C:\Users\Sebas\postgres\password.txt`.

```powershell
# iniciar / detener
C:\Users\Sebas\postgres\17\bin\pg_ctl.exe -D C:\Users\Sebas\postgres\data -l C:\Users\Sebas\postgres\server.log start
C:\Users\Sebas\postgres\17\bin\pg_ctl.exe -D C:\Users\Sebas\postgres\data stop
```

No arranca solo con Windows: hay que iniciarlo después de reiniciar la PC.

## Reglas que ya resuelve la base

- Las ventas descuentan stock solas (`venta_items` → `movimientos_stock` → `variantes.stock`).
- El stock no puede quedar negativo (CHECK en `variantes.stock`).
- Un ingreso con costo actualiza `productos.costo` ("el costo se actualiza solo cuando llega mercadería").
- `recibir_compra(id)` suma el stock de una compra; `anular_venta(id)` lo devuelve.
- Vistas para las pantallas: `v_stock_producto`, `v_ventas_resumen`, `v_compras_resumen`,
  `v_ultimo_precio_proveedor` (comparar precios), `v_proveedor_resumen`, `v_resumen_mensual`
  (% de gastos cubiertos).

## Pendiente de decidir

- Autenticación: `usuarios.password_hash` espera argon2/bcrypt generado por el backend.
- Token de Instagram: `instagram_cuentas.token_cifrado` debe cifrarse en la app, no en la base.
- Fotos: solo se guarda la URL; falta definir dónde viven los archivos.
