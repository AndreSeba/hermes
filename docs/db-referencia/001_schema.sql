-- Hermes · esquema PostgreSQL 14+
-- Multi-tienda: cada tienda (p. ej. "Luna Accesorios", hermes.bo/luna) ve solo sus datos.
-- Dinero en Bolivianos, numeric(12,2). Fechas en timestamptz.

create schema if not exists hermes;
set search_path = hermes, public;

create extension if not exists citext;

-- ───────────────────────── Tipos ─────────────────────────
create type metodo_pago        as enum ('efectivo', 'qr', 'transferencia', 'otro');
create type tipo_movimiento    as enum ('venta', 'ingreso', 'ajuste', 'devolucion');
create type estado_importacion as enum ('pendiente', 'importado', 'descartado');
create type origen_venta       as enum ('app', 'tienda_publica');

-- ───────────────────────── Cuentas y tiendas ─────────────────────────
create table usuarios (
    id             bigint generated always as identity primary key,
    nombre         text   not null,
    email          citext not null unique,
    telefono       text,
    password_hash  text   not null,
    creado_en      timestamptz not null default now()
);

create table tiendas (
    id                 bigint generated always as identity primary key,
    dueno_id           bigint not null references usuarios(id) on delete restrict,
    slug               citext not null unique
                       check (slug ~ '^[a-z0-9][a-z0-9-]{1,38}$'),      -- hermes.bo/<slug>
    nombre             text   not null,
    descripcion        text,
    whatsapp           text,                                           -- +591...
    logo_url           text,
    color              text   not null default '#7C5CFC'
                       check (color ~ '^#[0-9A-Fa-f]{6}$'),
    diseno             jsonb  not null default '{}'::jsonb,            -- "Personalizar diseño"
    mostrar_precios    boolean not null default true,
    mostrar_agotados   boolean not null default true,
    instagram_usuario  text,
    creado_en          timestamptz not null default now()
);
create index on tiendas (dueno_id);

-- ───────────────────────── Catálogo ─────────────────────────
create table categorias (
    id         bigint generated always as identity primary key,
    tienda_id  bigint not null references tiendas(id) on delete cascade,
    nombre     text   not null,
    unique (tienda_id, nombre)
);

create table proveedores (
    id         bigint generated always as identity primary key,
    tienda_id  bigint not null references tiendas(id) on delete cascade,
    nombre     text   not null,
    whatsapp   text,                                                   -- para "Pedirle a ..."
    notas      text,
    creado_en  timestamptz not null default now(),
    unique (tienda_id, nombre)
);

create table productos (
    id                   bigint generated always as identity primary key,
    tienda_id            bigint not null references tiendas(id) on delete cascade,
    categoria_id         bigint references categorias(id) on delete set null,
    proveedor_id         bigint references proveedores(id) on delete set null,   -- proveedor habitual
    nombre               text   not null,
    descripcion          text,
    precio               numeric(12,2) not null check (precio >= 0),
    costo                numeric(12,2) not null default 0 check (costo >= 0),    -- se actualiza al llegar mercadería
    margen_objetivo      numeric(5,2) check (margen_objetivo >= 0 and margen_objetivo < 100),
    stock_minimo         integer not null default 2 check (stock_minimo >= 0),
    visible_en_tienda    boolean not null default true,
    instagram_media_id   text,                                          -- si se publicó/importó de IG
    archivado            boolean not null default false,
    creado_en            timestamptz not null default now(),
    actualizado_en       timestamptz not null default now()
);
create index on productos (tienda_id) where not archivado;
create index on productos (categoria_id);
create unique index on productos (tienda_id, instagram_media_id) where instagram_media_id is not null;

create table producto_fotos (
    id           bigint generated always as identity primary key,
    producto_id  bigint not null references productos(id) on delete cascade,
    url          text   not null,
    orden        smallint not null default 0,
    es_portada   boolean  not null default false
);
create unique index on producto_fotos (producto_id) where es_portada;

-- Variantes (colores). Todo producto tiene al menos una ("Único").
create table variantes (
    id           bigint generated always as identity primary key,
    producto_id  bigint  not null references productos(id) on delete cascade,
    nombre       text    not null default 'Único',
    stock        integer not null default 0 check (stock >= 0),
    unique (producto_id, nombre)
);

-- Componentes del costo para la calculadora de precio (cuero, hilo, mano de obra...)
create table producto_costos (
    id           bigint generated always as identity primary key,
    producto_id  bigint not null references productos(id) on delete cascade,
    concepto     text   not null,
    monto        numeric(12,2) not null check (monto >= 0)
);
create index on producto_costos (producto_id);

-- ───────────────────────── Compras a proveedores ─────────────────────────
create table compras (
    id            bigint generated always as identity primary key,
    tienda_id     bigint not null references tiendas(id) on delete cascade,
    proveedor_id  bigint references proveedores(id) on delete set null,
    fecha         date    not null default current_date,
    recibida      boolean not null default false,      -- "Ya llegó la mercadería"
    pagada        boolean not null default false,      -- "Ya la pagué"
    notas         text,
    creado_en     timestamptz not null default now()
);
create index on compras (tienda_id, fecha desc);
create index on compras (proveedor_id);

create table compra_items (
    id              bigint generated always as identity primary key,
    compra_id       bigint  not null references compras(id) on delete cascade,
    variante_id     bigint  not null references variantes(id) on delete restrict,
    cantidad        integer not null check (cantidad > 0),
    costo_unitario  numeric(12,2) not null check (costo_unitario >= 0)
);
create index on compra_items (compra_id);
create index on compra_items (variante_id);

-- ───────────────────────── Ventas ─────────────────────────
create table ventas (
    id              bigint generated always as identity primary key,
    tienda_id       bigint not null references tiendas(id) on delete cascade,
    cliente         text,
    metodo_pago     metodo_pago  not null default 'efectivo',
    origen          origen_venta not null default 'app',
    fecha           timestamptz  not null default now(),
    anulada         boolean not null default false
);
create index on ventas (tienda_id, fecha desc);

create table venta_items (
    id              bigint generated always as identity primary key,
    venta_id        bigint  not null references ventas(id) on delete cascade,
    variante_id     bigint  not null references variantes(id) on delete restrict,
    cantidad        integer not null check (cantidad > 0),
    precio_unitario numeric(12,2) not null check (precio_unitario >= 0),
    costo_unitario  numeric(12,2) not null check (costo_unitario >= 0)   -- foto del costo al vender
);
create index on venta_items (venta_id);
create index on venta_items (variante_id);

-- ───────────────────────── Historial de stock ─────────────────────────
-- Fuente de verdad de "Lo último que pasó". variantes.stock se mantiene con trigger.
create table movimientos_stock (
    id              bigint generated always as identity primary key,
    variante_id     bigint not null references variantes(id) on delete cascade,
    tipo            tipo_movimiento not null,
    cantidad        integer not null check (cantidad <> 0),            -- + entra, − sale
    costo_unitario  numeric(12,2) check (costo_unitario >= 0),
    proveedor_id    bigint references proveedores(id) on delete set null,
    venta_item_id   bigint references venta_items(id) on delete set null,
    compra_item_id  bigint references compra_items(id) on delete set null,
    nota            text,
    creado_en       timestamptz not null default now()
);
create index on movimientos_stock (variante_id, creado_en desc);

-- ───────────────────────── Gastos ─────────────────────────
create table gastos (
    id         bigint generated always as identity primary key,
    tienda_id  bigint not null references tiendas(id) on delete cascade,
    concepto   text   not null,
    monto      numeric(12,2) not null check (monto >= 0),
    fecha      date   not null default current_date
);
create index on gastos (tienda_id, fecha);

-- ───────────────────────── Instagram ─────────────────────────
create table instagram_cuentas (
    tienda_id       bigint primary key references tiendas(id) on delete cascade,
    usuario         text not null,
    ig_user_id      text,
    token_cifrado   bytea,                                           -- nunca en claro
    token_expira    timestamptz,
    conectada_en    timestamptz not null default now()
);

create table instagram_importaciones (
    id            bigint generated always as identity primary key,
    tienda_id     bigint not null references tiendas(id) on delete cascade,
    media_id      text   not null,
    permalink     text,
    imagen_url    text,
    caption       text,
    estado        estado_importacion not null default 'pendiente',
    producto_id   bigint references productos(id) on delete set null,
    detectado_en  timestamptz not null default now(),
    unique (tienda_id, media_id)
);
create index on instagram_importaciones (tienda_id) where estado = 'pendiente';
