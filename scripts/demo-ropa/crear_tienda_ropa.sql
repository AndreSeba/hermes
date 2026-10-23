-- Tienda de DEMOSTRACIÓN "Urbano Wear" (ropa: poleras, camisas y pantalones) con 6 meses de movimiento simulado, solo para
-- la base de desarrollo. Es un script de Postgres puro (sin la API): por eso las fechas del pasado son reales.
--   Crea: dueña + equipo (encargada, vendedores y contador con sus roles), categorías, proveedores, 20 productos con
--   variantes (color x talla) y fotos, compras (recibidas, pagadas, por pagar, en camino, una anulada), stock como libro de
--   movimientos, ventas con distintos métodos de pago por vendedor (algunas anuladas), pedidos de la tienda pública en todos
--   sus estados con reserva de stock, y gastos del negocio. Deja casos límite a propósito: producto sin costo, con margen
--   bajo, que pierde plata, agotados, por agotarse, por encargo, archivado y oculto.
-- Se puede volver a correr: borra y rehace la tienda "urbano-wear".
--
-- Uso (primero las imágenes: powershell -File scripts/demo-ropa/generar_imagenes.ps1):
--   $env:PGPASSWORD = "<la de postgres>"
--   psql -h localhost -U postgres -d hermes_dev -v dir="C:/ruta/a/scripts/demo-ropa/img" -v hash="<hash bcrypt>" -f scripts/demo-ropa/crear_tienda_ropa.sql
-- `hash` es el hash BCrypt de la contraseña que van a tener las cuentas de demo (copiarlo de cualquier usuario de prueba
-- cuya contraseña conozcas: select password_hash from hermes.usuarios where email = '...').
\set ON_ERROR_STOP on
select set_config('demo.dir', :'dir', false) \gset
select set_config('demo.hash', :'hash', false) \gset

begin;

-- ───────────── Limpieza (para poder repetir) ─────────────
do $$
declare t uuid;
begin
  select id into t from hermes.tiendas where slug = 'urbano-wear';
  if t is not null then
    update hermes.tiendas set logo_imagen_id = null where id = t;
    delete from hermes.movimientos where tienda_id = t;
    delete from hermes.pedido_lineas where tienda_id = t;
    delete from hermes.pedidos where tienda_id = t;
    delete from hermes.venta_lineas where tienda_id = t;
    delete from hermes.ventas where tienda_id = t;
    delete from hermes.compra_lineas where tienda_id = t;
    delete from hermes.compras where tienda_id = t;
    delete from hermes.gastos where tienda_id = t;
    delete from hermes.producto_fotos where tienda_id = t;
    delete from hermes.variantes where tienda_id = t;
    delete from hermes.productos where tienda_id = t;
    delete from hermes.categorias where tienda_id = t;
    delete from hermes.proveedores where tienda_id = t;
    delete from hermes.rol_permisos where tienda_id = t;
    delete from hermes.miembros where tienda_id = t;
    delete from hermes.roles where tienda_id = t;
    delete from hermes.imagenes where tienda_id = t;
    delete from hermes.archivos where clave like t::text || '/%';
    delete from hermes.tiendas where id = t;
  end if;
  delete from hermes.usuarios where email in ('valeria.rojas@ejemplo.com', 'lucia.mamani@ejemplo.com', 'marco.vaca@ejemplo.com',
                                               'diego.suarez@ejemplo.com', 'rafael.mendez@ejemplo.com');
end $$;

-- ───────────── Ayudas ─────────────
-- UUID v7 con la hora indicada (los listados ordenan por Id: tiene que seguir el orden del tiempo).
create function pg_temp.u7(ts timestamptz) returns uuid language plpgsql as $$
declare b bytea;
begin
  b := substring(int8send((extract(epoch from ts) * 1000)::bigint) from 3 for 6)
       || decode(substr(md5(random()::text || clock_timestamp()::text), 1, 20), 'hex');
  b := set_byte(b, 6, (get_byte(b, 6) & 15) | 112);
  b := set_byte(b, 8, (get_byte(b, 8) & 63) | 128);
  return encode(b, 'hex')::uuid;
end $$;

create function pg_temp.hora(d date, minutos int) returns timestamptz language sql as $$
  select ((d::timestamp + (minutos * 60 + floor(random() * 60)::int) * interval '1 second') at time zone 'America/La_Paz')
$$;

create function pg_temp.cliente() returns text language sql as $$
  select (array['María','José','Carlos','Ana','Luis','Fernanda','Jorge','Daniela','Miguel','Patricia','Andrés','Carla','Diego',
                'Gabriela','Rodrigo','Verónica','Pablo','Sandra','Marcelo','Lorena','Álvaro','Paola','Ricardo','Mónica','Gonzalo',
                'Jimena','Hugo','Claudia','Iván','Natalia'])[1 + floor(random() * 30)::int]
         || ' ' ||
         (array['Rojas','Vaca','Mamani','Suárez','Flores','Justiniano','Chávez','Quispe','Pereira','Salvatierra','Roca','Aguilera',
                'Soliz','Peña','Montaño','Egüez','Barba','Cuéllar','Taborga','Antelo'])[1 + floor(random() * 20)::int]
$$;

create function pg_temp.mes(d date) returns text language sql as $$
  select (array['enero','febrero','marzo','abril','mayo','junio','julio','agosto','septiembre','octubre','noviembre','diciembre'])[extract(month from d)::int]
$$;

create function pg_temp.metodo() returns text language sql as $$
  select case when random() < 0.40 then 'Efectivo' when random() < 0.62 then 'Qr' when random() < 0.55 then 'Transferencia' else 'Tarjeta' end
$$;

-- ───────────── Estado de la simulación ─────────────
create temp table t_cfg (tienda uuid, dueno uuid, lucia uuid, marco uuid, diego uuid);
create temp table t_prod (
  idx int primary key, id uuid, nombre text, cat text, precio numeric, costo0 numeric, costo numeric, pop numeric, prov int,
  controla boolean, colores text[], tallas text[], nfotos int, descr text, visible boolean, archivado boolean, minimo int);
create temp table t_var (id uuid primary key, prod int, color text, talla text, stock int, w numeric, objetivo int);
create temp table t_cart (var uuid primary key, qty int);
create temp table t_comp (id uuid, prov int, recibe date, ts timestamptz);
create temp table t_ped (id uuid, num int, fase text, confirma date, fin date, fate text);
create temp table t_anular (venta uuid);

-- Libro de stock: todo cambio pasa por acá (stock = suma de movimientos; `saldo` es la foto de ese momento).
create function pg_temp.mov(p_var uuid, p_tipo text, p_cant int, p_nota text, p_costo numeric, p_ts timestamptz, p_venta uuid, p_compra uuid)
returns void language plpgsql as $$
declare s int;
begin
  update t_var set stock = stock + p_cant where id = p_var returning stock into s;
  if s < 0 then raise exception 'Stock negativo en % (%): %', p_var, p_tipo, s; end if;
  insert into hermes.movimientos (id, tienda_id, variante_id, tipo, cantidad, saldo, nota, costo_unitario, creado_en, venta_id, compra_id)
  values (pg_temp.u7(p_ts), (select tienda from t_cfg), p_var, p_tipo, p_cant, s, p_nota, p_costo, p_ts, p_venta, p_compra);
end $$;

-- Arma un carrito con `n` productos que haya en stock (los más populares y los talles medios salen más).
create function pg_temp.armar_carrito(n int) returns void language plpgsql as $$
declare vid uuid; disp int; q int;
begin
  delete from t_cart;
  for i in 1..n loop
    select v.id into vid from t_var v join t_prod p on p.idx = v.prod
     where p.pop > 0 and not exists (select 1 from t_cart c where c.var = v.id)
       and (not p.controla or v.stock > 0)
     order by -ln(1.0 - random()) / (p.pop * v.w) limit 1;
    exit when vid is null;
    select case when p.controla then v.stock else 99 end into disp from t_var v join t_prod p on p.idx = v.prod where v.id = vid;
    q := least(case when random() < 0.9 then 1 else 2 end, disp);
    insert into t_cart values (vid, q);
  end loop;
end $$;

create function pg_temp.descr(p_var uuid) returns text language sql as $$
  select p.nombre || case when v.color = '' and v.talla = '' then '' else ' (' || lower(v.color) || ' · ' || v.talla || ')' end
  from t_var v join t_prod p on p.idx = v.prod where v.id = p_var
$$;

-- Registra una venta con lo que haya en t_cart.
create function pg_temp.registrar_venta(p_ts timestamptz, p_usuario uuid, p_cliente text, p_metodo text, p_clave uuid)
returns uuid language plpgsql as $$
declare vid uuid := pg_temp.u7(p_ts); r record; tienda uuid := (select t.tienda from t_cfg t);
begin
  insert into hermes.ventas (id, tienda_id, fecha, metodo_pago, cliente, clave_idempotencia, anulada, usuario_id)
  values (vid, tienda, p_ts, p_metodo, p_cliente, coalesce(p_clave, gen_random_uuid()), false, p_usuario);
  for r in select c.var, c.qty, p.idx, p.id as prod_id, p.precio, p.costo, p.controla from t_cart c join t_var v on v.id = c.var join t_prod p on p.idx = v.prod loop
    insert into hermes.venta_lineas (id, tienda_id, venta_id, producto_id, variante_id, descripcion, cantidad, precio_unitario, costo_unitario, desconto_stock)
    values (pg_temp.u7(p_ts), tienda, vid, r.prod_id, r.var, pg_temp.descr(r.var), r.qty, r.precio, r.costo, r.controla);
    if r.controla then perform pg_temp.mov(r.var, 'Venta', -r.qty, null, null, p_ts, vid, null); end if;
  end loop;
  return vid;
end $$;

-- Compra a un proveedor: de lo que está bajo (o todo, la primera vez). Devuelve el Id.
create function pg_temp.hacer_compra(p_prov int, p_fecha date, p_lead int, p_plazo int, p_inicial boolean, p_nota text)
returns uuid language plpgsql as $$
declare cid uuid; ts timestamptz := pg_temp.hora(p_fecha, 545); recibe date := p_fecha + p_lead; rts timestamptz;
        pago date := p_fecha + p_lead + p_plazo; r record; n int := 0; q int; costo numeric; tienda uuid := (select t.tienda from t_cfg t);
        hoy date := (now() at time zone 'America/La_Paz')::date; factor numeric; hay boolean := false;
begin
  select exists (select 1 from t_var v join t_prod p on p.idx = v.prod
                  where p.prov = p_prov and p.controla and p.pop > 0 and not p.archivado
                    and not (p.idx = 11 and p_fecha >= date '2026-08-25')   -- el proveedor dejó de fabricar la camisa jean
                    and (p_inicial or v.stock <= greatest(2, round(v.objetivo * 0.45)::int))) into hay;
  if not hay then return null; end if;
  rts := pg_temp.hora(recibe, 500);
  cid := pg_temp.u7(ts);
  insert into hermes.compras (id, tienda_id, proveedor_id, fecha, notas, clave_idempotencia, recibida, recibida_en, pagada, pagada_en, anulada)
  values (cid, tienda, (select id from hermes.proveedores where tienda_id = tienda and nombre = (array['Textiles Cruceña S.R.L.','Denim Andino Bolivia','Confecciones Illimani'])[p_prov]),
          ts, p_nota, gen_random_uuid(), recibe <= hoy, case when recibe <= hoy then rts end, pago <= hoy, case when pago <= hoy then pg_temp.hora(pago, 600) end, false);
  for r in select v.id as var, p.idx, p.id as prod_id, p.costo0, v.stock, v.objetivo from t_var v join t_prod p on p.idx = v.prod
            where p.prov = p_prov and p.controla and p.pop > 0 and not p.archivado
                    and not (p.idx = 11 and p_fecha >= date '2026-08-25')   -- el proveedor dejó de fabricar la camisa jean
              and (p_inicial or v.stock <= greatest(2, round(v.objetivo * 0.45)::int))
            order by p.idx, v.id loop
    q := case when p_inicial then greatest(2, round(r.objetivo * 0.7)::int) else greatest(2, round(r.objetivo * 1.15)::int - r.stock) end;
    factor := case when p_fecha >= date '2026-08-01' then (case when p_prov in (1, 2) then 1.06 else 1.04 end) else 1.0 end;
    if r.idx = 19 and p_fecha >= date '2026-08-01' then factor := 1.40; end if;
    costo := round(r.costo0 * factor);
    n := n + 1;
    insert into hermes.compra_lineas (id, tienda_id, compra_id, orden, producto_id, variante_id, descripcion, cantidad, costo_unitario, sumo_stock)
    values (pg_temp.u7(ts), tienda, cid, n, r.prod_id, r.var, pg_temp.descr(r.var), q, costo, recibe <= hoy);
  end loop;
  insert into t_comp values (cid, p_prov, recibe, rts);
  return cid;
end $$;

-- Recibe una compra: suma el stock y el costo de cada producto pasa a ser el de su línea.
create function pg_temp.recibir_compra(p_id uuid, p_ts timestamptz) returns void language plpgsql as $$
declare r record;
begin
  for r in select l.variante_id, l.cantidad, l.costo_unitario, p.idx from hermes.compra_lineas l join t_var v on v.id = l.variante_id join t_prod p on p.idx = v.prod
            where l.compra_id = p_id order by l.orden loop
    perform pg_temp.mov(r.variante_id, 'Ingreso', r.cantidad, null, r.costo_unitario, p_ts, null, p_id);
    update t_prod set costo = r.costo_unitario where idx = r.idx;
  end loop;
end $$;

-- ───────────── La tienda, el equipo y el catálogo ─────────────
do $$
declare
  dir text := current_setting('demo.dir'); hash text := current_setting('demo.hash');
  t0 timestamptz := timestamptz '2026-04-20 09:30-04';
  tienda uuid := pg_temp.u7(timestamptz '2026-04-20 09:30-04');
  dueno uuid := pg_temp.u7(timestamptz '2026-04-20 09:00-04');
  lucia uuid := pg_temp.u7(timestamptz '2026-04-21 10:00-04');
  marco uuid := pg_temp.u7(timestamptz '2026-04-21 10:05-04');
  diego uuid := pg_temp.u7(timestamptz '2026-08-14 10:00-04');
  rafael uuid := pg_temp.u7(timestamptz '2026-08-31 10:00-04');
  r_enc uuid := pg_temp.u7(timestamptz '2026-04-20 09:40-04');
  r_ven uuid := pg_temp.u7(timestamptz '2026-04-20 09:41-04');
  r_sol uuid := pg_temp.u7(timestamptz '2026-04-20 09:42-04');
  p record; v record; img uuid; k int; sl text; tam text; ord int; c text; t text; bytes int; arch bytea; n int; w numeric; cw numeric;
  cat_p uuid := pg_temp.u7(timestamptz '2026-04-20 10:00-04'); cat_c uuid := pg_temp.u7(timestamptz '2026-04-20 10:01-04'); cat_n uuid := pg_temp.u7(timestamptz '2026-04-20 10:02-04');
begin
  perform setseed(0.37);
  insert into t_cfg values (tienda, dueno, lucia, marco, diego);

  insert into hermes.usuarios (id, nombre, email, telefono, password_hash, activo, creado_en, ultimo_login_en, es_administrador, debe_cambiar_password) values
    (dueno,  'Valeria Rojas Peña',    'valeria.rojas@ejemplo.com', '+59170001001', hash, true, timestamptz '2026-04-20 09:00-04', now() - interval '3 hours', false, false),
    (lucia,  'Lucía Mamani Quispe',   'lucia.mamani@ejemplo.com',  '+59170001002', hash, true, timestamptz '2026-04-21 10:00-04', now() - interval '1 day', false, false),
    (marco,  'Marco Vaca Justiniano', 'marco.vaca@ejemplo.com',    '+59170001003', hash, true, timestamptz '2026-04-21 10:05-04', now() - interval '5 hours', false, false),
    (diego,  'Diego Suárez Roca',     'diego.suarez@ejemplo.com',  '+59170001004', hash, true, timestamptz '2026-08-14 10:00-04', now() - interval '2 days', false, false),
    (rafael, 'Rafael Méndez Barba',   'rafael.mendez@ejemplo.com', '+59170001005', hash, true, timestamptz '2026-08-31 10:00-04', now() - interval '6 days', false, false);

  insert into hermes.tiendas (id, dueno_id, slug, nombre, descripcion, whatsapp, color, activa, creada_en, fondo, mostrar_disponibilidad, mostrar_precios, pedidos_por_whatsapp)
  values (tienda, dueno, 'urbano-wear', 'Urbano Wear',
          'Ropa urbana para hombre y mujer: poleras, camisas y pantalones. Envíos a todo Santa Cruz.', '+59170001234', '#14532D', true, t0, 'Crema', true, true, true);

  -- Roles (los mismos de fábrica) y el equipo
  insert into hermes.roles (id, tienda_id, nombre, descripcion, activo) values
    (r_enc, tienda, 'Encargado', 'Maneja el día a día: productos, stock, ventas, compras y gastos. No cambia la tienda, el plan ni el equipo.', true),
    (r_ven, tienda, 'Vendedor', 'Registra ventas y mira productos y stock. No ve costos ni ganancias, y solo ve sus propias ventas.', true),
    (r_sol, tienda, 'Solo ver', 'Mira cómo va el negocio (incluidas las ganancias) sin poder cambiar nada.', true);
  insert into hermes.rol_permisos (id, tienda_id, rol_id, codigo)
  select pg_temp.u7(t0), tienda, r.rol, cod from (values
    (r_enc, array['inicio.ver','productos.ver','productos.crear','productos.editar','productos.archivar','costos.ver','costos.editar','categorias.ver','categorias.gestionar','stock.ver','stock.ajustar','ventas.ver','ventas.registrar','ventas.anular','compras.ver','compras.gestionar','proveedores.ver','proveedores.gestionar','gastos.ver','gastos.gestionar','pedidos.ver','pedidos.gestionar','instagram.gestionar']),
    (r_ven, array['productos.ver','categorias.ver','stock.ver','ventas.registrar','pedidos.ver']),
    (r_sol, array['inicio.ver','productos.ver','costos.ver','categorias.ver','stock.ver','ventas.ver','compras.ver','proveedores.ver','gastos.ver','pedidos.ver'])) r(rol, codigos),
    unnest(r.codigos) cod;
  insert into hermes.miembros (id, tienda_id, usuario_id, rol_id, activo, creado_en) values
    (pg_temp.u7(timestamptz '2026-04-21 10:00-04'), tienda, lucia,  r_enc, true, timestamptz '2026-04-21 10:00-04'),
    (pg_temp.u7(timestamptz '2026-04-21 10:05-04'), tienda, marco,  r_ven, true, timestamptz '2026-04-21 10:05-04'),
    (pg_temp.u7(timestamptz '2026-08-14 10:00-04'), tienda, diego,  r_ven, true, timestamptz '2026-08-14 10:00-04'),
    (pg_temp.u7(timestamptz '2026-08-31 10:00-04'), tienda, rafael, r_sol, true, timestamptz '2026-08-31 10:00-04');

  insert into hermes.categorias (id, tienda_id, nombre, activa) values
    (cat_p, tienda, 'Poleras', true), (cat_c, tienda, 'Camisas', true), (cat_n, tienda, 'Pantalones', true);

  insert into hermes.proveedores (id, tienda_id, nombre, telefono, notas, activo, correo) values
    (pg_temp.u7(t0 + interval '1 minute'), tienda, 'Textiles Cruceña S.R.L.', '+59171000101', 'Poleras y camisas. Entregan en 3 días. Pedido mínimo de 30 prendas.', true, 'ventas@textilescrucena.ejemplo.com'),
    (pg_temp.u7(t0 + interval '2 minute'), tienda, 'Denim Andino Bolivia', '+59171000102', 'Jeans y pantalones de tela. Entrega en 5 días. Se les paga a los 15 días.', true, 'pedidos@denimandino.ejemplo.com'),
    (pg_temp.u7(t0 + interval '3 minute'), tienda, 'Confecciones Illimani', '+59171000103', 'Ropa deportiva y shorts. Trabajan por docena.', true, null);

  -- Catálogo: idx, nombre, categoría, precio, costo, popularidad, proveedor (0 = lo estampa la dueña), controla stock, colores, tallas, fotos, descripción, visible, archivado
  insert into t_prod (idx, nombre, cat, precio, costo0, costo, pop, prov, controla, colores, tallas, nfotos, descr, visible, archivado, minimo) values
    (1,  'Polera básica de algodón', 'P', 75, 38, 38, 1.0, 1, true, array['Blanco','Negro','Gris'], array['S','M','L','XL'], 3, 'Algodón peinado de 160 g, cuello redondo. Se siente suave y no se deforma en el lavado.', true, false, 4),
    (2,  'Polera oversize', 'P', 95, 48, 48, 0.9, 1, true, array['Negro','Beige','Verde oliva'], array['S','M','L','XL'], 3, 'Corte holgado, caída recta y hombros caídos. Algodón grueso de 200 g.', true, false, 4),
    (3,  'Polera estampada Andes', 'P', 85, 0, 0, 0.7, 0, true, array['Blanco','Negro'], array['S','M','L','XL'], 2, 'Estampado propio de Urbano Wear: montañas y sol del altiplano. Estampado resistente al lavado.', true, false, 3),
    (4,  'Polera polo piqué', 'P', 120, 62, 62, 0.6, 3, true, array['Azul marino','Blanco','Rojo'], array['S','M','L','XL'], 3, 'Tela piqué, cuello polo y dos botones. Ideal para la oficina o el fin de semana.', true, false, 3),
    (5,  'Polera manga larga', 'P', 110, 96, 96, 0.35, 1, true, array['Negro','Gris','Azul marino'], array['S','M','L','XL'], 2, 'Manga larga de algodón con puño elástico. Para las noches frías de Santa Cruz.', true, false, 3),
    (6,  'Polera deportiva dry-fit', 'P', 90, 46, 46, 0.5, 3, true, array['Negro','Azul','Rojo'], array['S','M','L','XL'], 2, 'Tela que seca rápido y no retiene el sudor, con franjas laterales.', true, false, 3),
    (7,  'Polera Navidad 2025', 'P', 80, 42, 42, 0.05, 1, true, array['Rojo'], array['S','M','L'], 1, 'Edición de fin de año. Ya no se repone.', true, true, 2),
    (8,  'Camisa Oxford manga larga', 'C', 180, 92, 92, 0.55, 1, true, array['Celeste','Blanco'], array['S','M','L','XL'], 2, 'Tela Oxford con bolsillo en el pecho. Cuello clásico con botones.', true, false, 3),
    (9,  'Camisa de lino', 'C', 195, 100, 100, 0.4, 1, true, array['Beige','Blanco','Verde oliva'], array['S','M','L','XL'], 3, 'Lino liviano y fresco para el calor. Se arruga un poco: es parte de su encanto.', true, false, 3),
    (10, 'Camisa a cuadros', 'C', 170, 88, 88, 0.45, 1, true, array['Rojo','Azul marino'], array['S','M','L','XL'], 2, 'Franela suave a cuadros, manga larga. Se usa sola o sobre una polera.', true, false, 3),
    (11, 'Camisa jean', 'C', 210, 110, 110, 0.6, 2, true, array['Azul'], array['S','M','L','XL'], 1, 'Denim liviano con costuras reforzadas y dos bolsillos.', true, false, 3),
    (12, 'Camisa manga corta', 'C', 150, 76, 76, 0.5, 1, true, array['Blanco','Celeste','Negro'], array['S','M','L','XL'], 3, 'Manga corta, tela fresca. Un básico para el calor.', true, false, 3),
    (13, 'Camisa a medida (por encargo)', 'C', 320, 170, 170, 0.1, 0, false, array['Blanco'], array['A medida'], 1, 'Se toman las medidas en el local y se entrega en 10 días. Elegís tela y color.', true, false, 0),
    (14, 'Jean clásico', 'N', 260, 135, 135, 0.9, 2, true, array['Azul','Negro'], array['28','30','32','34','36'], 2, 'Corte recto, cinco bolsillos. Denim de 12 oz que dura años.', true, false, 4),
    (15, 'Jean slim', 'N', 280, 145, 145, 0.85, 2, true, array['Azul','Negro','Gris'], array['28','30','32','34','36'], 3, 'Corte ajustado con algo de elástico para moverte cómodo.', true, false, 4),
    (16, 'Pantalón de vestir', 'N', 300, 155, 155, 0.4, 2, true, array['Negro','Gris','Azul marino'], array['28','30','32','34','36'], 3, 'Tela con caída y raya marcada. Para la oficina y los eventos.', true, false, 3),
    (17, 'Pantalón cargo', 'N', 270, 140, 140, 0.5, 2, true, array['Verde oliva','Beige','Negro'], array['28','30','32','34','36'], 3, 'Seis bolsillos y tela resistente. Cómodo para todo el día.', true, false, 3),
    (18, 'Jogger deportivo', 'N', 160, 82, 82, 0.65, 3, true, array['Negro','Gris','Azul marino'], array['S','M','L','XL'], 3, 'Felpa liviana con cordón y puños elásticos.', true, false, 3),
    (19, 'Short bermuda', 'N', 140, 105, 105, 0.45, 3, true, array['Beige','Azul','Negro'], array['30','32','34','36'], 2, 'Short de gabardina hasta la rodilla, con bolsillos laterales.', true, false, 3),
    (20, 'Pantalón chino', 'N', 250, 130, 130, 0.35, 2, true, array['Beige','Azul marino'], array['28','30','32','34','36'], 2, 'Corte recto y tela suave. Combina con camisa o polera.', false, false, 3);

  update t_prod set id = pg_temp.u7(t0 + (idx * interval '2 minute'));

  for p in select * from t_prod order by idx loop
    -- el costo del catálogo arranca en lo que costó la primera vez (el de la compra inicial lo vuelve a fijar)
    insert into hermes.productos (id, tienda_id, nombre, descripcion, precio, costo, categoria_id, visible_en_tienda, archivado, creado_en, actualizado_en, controla_stock, stock_minimo)
    values (p.id, tienda, p.nombre, p.descr, p.precio, p.costo0, case p.cat when 'P' then cat_p when 'C' then cat_c else cat_n end,
            p.visible, false, t0 + (p.idx * interval '2 minute'), t0 + (p.idx * interval '2 minute'), p.controla, p.minimo);
    ord := 0; n := 0;
    foreach c in array p.colores loop
      n := n + 1;
      cw := case n when 1 then 1.0 when 2 then 0.8 else 0.6 end;
      foreach tam in array p.tallas loop
        ord := ord + 1;
        w := cw * case tam when 'S' then 0.7 when 'M' then 1.2 when 'L' then 1.1 when 'XL' then 0.8
                           when '28' then 0.5 when '30' then 1.0 when '32' then 1.3 when '34' then 1.0 when '36' then 0.6 else 1.0 end;
        img := pg_temp.u7(t0 + (p.idx * interval '2 minute') + (ord * interval '1 second'));
        insert into t_var values (img, p.idx, c, tam, 0, w, greatest(2, round(p.pop * 9 * w)::int));
        insert into hermes.variantes (id, tienda_id, producto_id, color, talla, activa, orden) values (img, tienda, p.id, c, tam, true, ord);
      end loop;
    end loop;

    -- Fotos: una por color (las primeras `nfotos`)
    for k in 1..p.nfotos loop
      sl := 'p' || lpad(p.idx::text, 2, '0') || '_' || replace(lower(p.colores[k]), ' ', '-');
      img := pg_temp.u7(t0 + (p.idx * interval '2 minute') + (k * interval '1 minute'));
      bytes := 0;
      foreach t in array array['miniatura', 'catalogo', 'detalle'] loop
        arch := pg_read_binary_file(dir || '/' || sl || '_' || t || '.png');
        bytes := bytes + length(arch);
        insert into hermes.archivos (clave, datos, content_type, bytes, creado_en)
        values (tienda || '/imagenes/' || img || '/' || t || '.webp', arch, 'image/png', length(arch), t0);
      end loop;
      insert into hermes.imagenes (id, tienda_id, ancho_original, alto_original, bytes_guardados, creada_en) values (img, tienda, 1000, 1000, bytes, t0);
      insert into hermes.producto_fotos (id, tienda_id, producto_id, imagen_id, orden) values (pg_temp.u7(t0), tienda, p.id, img, k);
    end loop;
  end loop;

  -- Logo
  img := pg_temp.u7(t0 + interval '5 minute'); bytes := 0;
  foreach t in array array['miniatura', 'catalogo', 'detalle'] loop
    arch := pg_read_binary_file(dir || '/logo_' || t || '.png');
    bytes := bytes + length(arch);
    insert into hermes.archivos (clave, datos, content_type, bytes, creado_en) values (tienda || '/imagenes/' || img || '/' || t || '.webp', arch, 'image/png', length(arch), t0);
  end loop;
  insert into hermes.imagenes (id, tienda_id, ancho_original, alto_original, bytes_guardados, creada_en) values (img, tienda, 1000, 1000, bytes, t0);
  update hermes.tiendas set logo_imagen_id = img where id = tienda;
end $$;

-- ───────────── Seis meses de movimiento, día por día ─────────────
do $$
declare
  cfg t_cfg%rowtype; tienda uuid; dueno uuid; lucia uuid; marco uuid; diego uuid;
  s0 date := date '2026-04-23';              -- el día que abre el local
  hoy date := (now() at time zone 'America/La_Paz')::date;
  d date; dias int; trend numeric; base numeric; n int; i int; mins int[]; m int; tope int;
  ts timestamptz; vend uuid; vid uuid; r record; res record; pnum int := 0; pid uuid; items int; pago text; cli text;
  cid uuid; npe int; fate text; dir text; ent text; x numeric; q int;
begin
  select * into cfg from t_cfg; tienda := cfg.tienda; dueno := cfg.dueno; lucia := cfg.lucia; marco := cfg.marco; diego := cfg.diego;

  -- Compra inicial (el 20 de abril; llega el 22) para armar el local
  perform pg_temp.hacer_compra(1, date '2026-04-20', 2, 10, true, 'Compra inicial para abrir el local');
  perform pg_temp.hacer_compra(2, date '2026-04-20', 2, 15, true, 'Compra inicial de pantalones');
  perform pg_temp.hacer_compra(3, date '2026-04-21', 2, 7, true, 'Primer pedido de ropa deportiva');

  d := date '2026-04-20';
  while d <= hoy loop
    tope := case when d = hoy then 600 else 1215 end;          -- hoy solo hasta las 10:00

    -- Cambios de precio de agosto (suben los costos y se actualiza el precio de algunos pantalones)
    if d = date '2026-08-01' then
      update t_prod set precio = precio + 20 where idx in (14, 15, 16);
    end if;
    if d = date '2026-07-01' then update t_prod set pop = 0 where idx = 7; end if;

    -- 08:15 llegan las compras
    for r in select * from t_comp where recibe = d order by ts loop
      perform pg_temp.recibir_compra(r.id, r.ts);
      insert into hermes.gastos (id, tienda_id, concepto, monto, fecha, clave_idempotencia, anulado)
      values (pg_temp.u7(r.ts), tienda, 'Flete de mercadería (' || (array['Textiles Cruceña','Denim Andino','Confecciones Illimani'])[r.prov] || ')',
              (array[90, 130, 80])[r.prov] + 10 * floor(random() * 4), r.ts + interval '20 minutes', gen_random_uuid(), false);
    end loop;
    -- la polera estampada la hace la dueña: entra a stock sin costo cada 25 días
    if d >= date '2026-04-22' and mod((d - date '2026-04-22'), 25) = 0 then
      for r in select v.id, v.objetivo, v.stock from t_var v where v.prod = 3 loop
        if r.objetivo > r.stock then
          perform pg_temp.mov(r.id, 'Ingreso', greatest(2, round((r.objetivo - r.stock) * 0.9)::int), 'Estampado propio', null, pg_temp.hora(d, 500), null, null);
        end if;
      end loop;
    end if;

    -- 08:30 pedidos de la tienda que toca confirmar, y 08:45 los que toca cerrar
    for r in select pe.* from t_ped pe where pe.fase = 'N' and pe.confirma = d order by pe.num loop
      ts := pg_temp.hora(d, 510);
      if r.fate = 'cancel_antes' then
        update hermes.pedidos set estado = 'Cancelado', resuelto_en = ts,
               motivo_cancelacion = (array['El cliente ya no lo quiere','No respondió el WhatsApp','Pidió una talla que no hay'])[1 + floor(random() * 3)::int] where id = r.id;
        update t_ped set fase = 'F' where id = r.id;
      elsif exists (select 1 from hermes.pedido_lineas l join t_var v on v.id = l.variante_id join t_prod p on p.idx = v.prod
                     where l.pedido_id = r.id and p.controla and v.stock < l.cantidad) then
        update hermes.pedidos set estado = 'Cancelado', resuelto_en = ts, motivo_cancelacion = 'Sin stock al confirmar: se avisó al cliente' where id = r.id;
        update t_ped set fase = 'F' where id = r.id;
      else
        for res in select l.variante_id, l.cantidad from hermes.pedido_lineas l join t_var v on v.id = l.variante_id join t_prod p on p.idx = v.prod where l.pedido_id = r.id and p.controla loop
          perform pg_temp.mov(res.variante_id, 'Reserva', -res.cantidad, 'Pedido #' || r.num || ' confirmado', null, ts, null, null);
        end loop;
        update hermes.pedidos set estado = 'Confirmado', stock_reservado = true where id = r.id;
        update t_ped set fase = 'C' where id = r.id;
      end if;
    end loop;
    for r in select pe.* from t_ped pe where pe.fase = 'C' and pe.fin = d order by pe.num loop
      ts := pg_temp.hora(d, 525);
      for res in select l.variante_id, l.cantidad from hermes.pedido_lineas l join t_var v on v.id = l.variante_id join t_prod p on p.idx = v.prod where l.pedido_id = r.id and p.controla loop
        perform pg_temp.mov(res.variante_id, 'LiberaReserva', res.cantidad, case when r.fate = 'entrega' then 'Pedido #' || r.num || ' entregado' else 'Pedido #' || r.num || ' cancelado' end, null, ts, null, null);
      end loop;
      if r.fate = 'entrega' then
        delete from t_cart;
        insert into t_cart select l.variante_id, l.cantidad from hermes.pedido_lineas l where l.pedido_id = r.id;
        vend := case when random() < 0.5 then lucia else dueno end;
        vid := pg_temp.registrar_venta(ts + interval '2 minutes', vend, (select cliente_nombre from hermes.pedidos where id = r.id), (array['Efectivo','Qr','Qr','Transferencia'])[1 + floor(random() * 4)::int], r.id);
        update hermes.pedidos set estado = 'Entregado', resuelto_en = ts + interval '2 minutes', venta_id = vid, stock_reservado = false where id = r.id;
      else
        update hermes.pedidos set estado = 'Cancelado', resuelto_en = ts, stock_reservado = false,
               motivo_cancelacion = (array['No fue a recoger el pedido','El cliente pidió cancelar','No se pudo coordinar la entrega'])[1 + floor(random() * 3)::int] where id = r.id;
      end if;
      update t_ped set fase = 'F' where id = r.id;
    end loop;

    -- 09:00 reposición a proveedores (cada uno con su ritmo)
    if d > date '2026-04-22' then
      if mod((d - date '2026-04-20'), 14) = 0 then perform pg_temp.hacer_compra(1, d, 3, 10, false, null); end if;
      if mod((d - date '2026-04-20'), 21) = 0 then perform pg_temp.hacer_compra(2, d, 5, 15, false, null); end if;
      if mod((d - date '2026-04-21'), 18) = 0 then perform pg_temp.hacer_compra(3, d, 3, 7, false, null); end if;
      if d = date '2026-10-20' then perform pg_temp.hacer_compra(2, d, 5, 15, false, 'Reposición urgente de jeans'); end if;
      if d = date '2026-07-20' then
        -- un pedido que se canceló antes de que llegara
        cid := pg_temp.u7(pg_temp.hora(d, 560));
        insert into hermes.compras (id, tienda_id, proveedor_id, fecha, notas, clave_idempotencia, recibida, pagada, anulada, anulada_en)
        values (cid, tienda, (select id from hermes.proveedores where tienda_id = tienda and nombre = 'Textiles Cruceña S.R.L.'), pg_temp.hora(d, 560),
                'Pedido de camisas de lino: el proveedor no tenía la tela', gen_random_uuid(), false, false, true, pg_temp.hora(d + 1, 600));
        insert into hermes.compra_lineas (id, tienda_id, compra_id, orden, producto_id, variante_id, descripcion, cantidad, costo_unitario, sumo_stock)
        select pg_temp.u7(pg_temp.hora(d, 560)), tienda, cid, row_number() over (order by v.id), p.id, v.id, pg_temp.descr(v.id), 6, 100, false
          from t_var v join t_prod p on p.idx = v.prod where p.idx = 9 and v.talla in ('M', 'L') and v.color = 'Beige';
      end if;
    end if;

    -- Pedidos que hacen los clientes desde la tienda en internet
    if d >= s0 then
      trend := 0.75 + 0.45 * (d - s0) / 183.0;
      npe := round(0.5 * trend * (0.2 + 1.6 * random()) * case when extract(isodow from d) >= 5 then 1.3 else 1.0 end)::int;
      if d >= hoy - 2 then npe := greatest(npe, 1); end if;   -- que siempre haya pedidos recién llegados y por entregar
      for i in 1..npe loop
        ts := pg_temp.hora(d, 560 + floor(random() * (greatest(tope, 600) - 560))::int);
        perform pg_temp.armar_carrito(1 + floor(random() * random() * 3)::int);
        continue when not exists (select 1 from t_cart);
        pnum := pnum + 1; pid := pg_temp.u7(ts);
        ent := case when random() < 0.6 then 'Envio' else 'Retiro' end;
        x := random();
        fate := case when x < 0.84 or d >= hoy - 1 then 'entrega' when x < 0.92 then 'cancel_antes' else 'cancel_despues' end;
        cli := pg_temp.cliente();
        insert into hermes.pedidos (id, tienda_id, numero, clave_idempotencia, fecha, cliente_nombre, cliente_celular, entrega, direccion, referencia, nota, estado, stock_reservado)
        values (pid, tienda, pnum, gen_random_uuid(), ts, cli, '7000' || lpad(floor(random() * 10000)::int::text, 4, '0'), ent,
                case when ent = 'Envio' then (array['Av. Banzer, 4to anillo, casa 412','Calle Libertad 280, zona Centro','Av. Alemana, 3er anillo, edificio Torres','Barrio Equipetrol, calle 7 Este','Av. Cañoto 530','Urbanización Las Palmas, calle 3','Av. Santos Dumont, 2do anillo','Plan 3000, barrio Mercado'])[1 + floor(random() * 8)::int] end,
                case when ent = 'Envio' then (array['Portón negro, frente a la farmacia','Casa de rejas verdes','Al lado del minimarket','Edificio con portero, piso 3','Llamar al llegar'])[1 + floor(random() * 5)::int] end,
                case when random() < 0.25 then (array['Lo necesito para el sábado','Si no hay la talla M, avísenme','Puedo pagar con QR al recibir','Es un regalo, ¿lo pueden envolver?'])[1 + floor(random() * 4)::int] end,
                'Nuevo', false);
        insert into hermes.pedido_lineas (id, tienda_id, pedido_id, producto_id, variante_id, descripcion, cantidad, precio_unitario, orden)
        select pg_temp.u7(ts), tienda, pid, p.id, c.var, pg_temp.descr(c.var), c.qty, p.precio, row_number() over (order by c.var)
          from t_cart c join t_var v on v.id = c.var join t_prod p on p.idx = v.prod;
        insert into t_ped values (pid, pnum, 'N', d + 1, d + 2 + floor(random() * 3)::int, fate);
      end loop;
      delete from t_cart;

      -- Ventas del mostrador
      base := case extract(isodow from d) when 1 then 4 when 2 then 4 when 3 then 4.5 when 4 then 5 when 5 then 7 when 6 then 9 else 3 end;
      n := round(base * trend
                 * case when extract(day from d) in (1,2,3,4,5,15,16,28,29,30,31) then 1.25 else 1.0 end
                 * case when d between date '2026-05-20' and date '2026-05-27' then 1.6 when d between date '2026-08-01' and date '2026-08-07' then 1.35 else 1.0 end
                 * (0.65 + 0.7 * random()))::int;
      mins := array(select (570 + floor(random() * (tope - 570)))::int from generate_series(1, n) order by 1);
      for i in 1..coalesce(array_length(mins, 1), 0) loop
        ts := pg_temp.hora(d, mins[i]);
        x := random();
        items := case when x < 0.55 then 1 when x < 0.83 then 2 when x < 0.95 then 3 else 4 end;
        perform pg_temp.armar_carrito(items);
        continue when not exists (select 1 from t_cart);
        vend := case when random() < 0.12 or extract(isodow from d) = 7 then dueno
                     when d >= date '2026-08-15' and mins[i] < 840 and random() < 0.5 then diego
                     when mins[i] < 840 or random() < 0.3 then marco
                     else lucia end;
        cli := case when random() < 0.28 then pg_temp.cliente() end;
        pago := pg_temp.metodo();
        vid := pg_temp.registrar_venta(ts, vend, cli, pago, null);
        if random() < 0.015 then insert into t_anular values (vid); end if;
      end loop;
    end if;

    -- 20:30 anulaciones del día (devuelven el stock)
    for r in select venta from t_anular loop
      ts := pg_temp.hora(d, 1230);
      update hermes.ventas set anulada = true, anulada_en = ts where id = r.venta;
      for res in select l.variante_id, l.cantidad from hermes.venta_lineas l where l.venta_id = r.venta and l.desconto_stock loop
        perform pg_temp.mov(res.variante_id, 'Devolucion', res.cantidad, 'Venta anulada', null, ts, r.venta, null);
      end loop;
    end loop;
    delete from t_anular;

    -- Conteo de fin de mes: faltan 2 o 3 prendas (merma)
    if extract(day from d) = 28 and d >= date '2026-05-28' then
      ts := pg_temp.hora(d, 1245);
      for r in select v.id from t_var v join t_prod p on p.idx = v.prod where p.controla and v.stock >= 2 order by random() limit 2 + floor(random() * 2)::int loop
        perform pg_temp.mov(r.id, 'Ajuste', -1, 'Conteo de fin de mes: faltó 1 unidad', null, ts, null, null);
      end loop;
    end if;

    -- Gastos del negocio
    ts := pg_temp.hora(d, 720);
    if d = date '2026-04-21' then
      insert into hermes.gastos (id, tienda_id, concepto, monto, fecha, clave_idempotencia, anulado) values
        (pg_temp.u7(ts), tienda, 'Percheros y exhibidores', 1850, ts, gen_random_uuid(), false),
        (pg_temp.u7(ts), tienda, 'Garantía y primer mes de alquiler', 7000, ts, gen_random_uuid(), false);
    end if;
    if d = date '2026-04-22' then insert into hermes.gastos values (pg_temp.u7(ts), tienda, 'Letrero del local', 1200, ts, gen_random_uuid(), false, null); end if;
    if d = date '2026-05-20' then insert into hermes.gastos values (pg_temp.u7(ts), tienda, 'Dos maniquíes para la vitrina', 780, ts, gen_random_uuid(), false, null); end if;
    if d = date '2026-08-17' then insert into hermes.gastos values (pg_temp.u7(ts), tienda, 'Reparación del aire acondicionado', 420, ts, gen_random_uuid(), false, null); end if;
    if d = date '2026-07-10' then
      insert into hermes.gastos values (pg_temp.u7(ts), tienda, 'Publicidad en Facebook (cargada dos veces)', 200, ts, gen_random_uuid(), true, ts + interval '1 day');
    end if;
    if extract(day from d) = 5 and d >= date '2026-05-05' then insert into hermes.gastos values (pg_temp.u7(ts), tienda, 'Alquiler del local', 3500, ts, gen_random_uuid(), false, null); end if;
    if extract(day from d) = 3 and d >= date '2026-05-03' then insert into hermes.gastos values (pg_temp.u7(ts), tienda, 'Bolsas y empaques', 150 + 10 * floor(random() * 7), ts, gen_random_uuid(), false, null); end if;
    if extract(day from d) = 8 and d >= date '2026-05-08' then insert into hermes.gastos values (pg_temp.u7(ts), tienda, 'Internet y telefonía', 260, ts, gen_random_uuid(), false, null); end if;
    if extract(day from d) = 12 and d >= date '2026-05-12' then
      insert into hermes.gastos values (pg_temp.u7(ts), tienda, 'Luz', 330 + 12 * (extract(month from d) - 4) + floor(random() * 40), ts, gen_random_uuid(), false, null);
      insert into hermes.gastos values (pg_temp.u7(ts), tienda, 'Agua', 80 + floor(random() * 15), ts, gen_random_uuid(), false, null);
    end if;
    if extract(isodow from d) = 5 and mod((d - date '2026-04-24'), 14) = 0 then
      insert into hermes.gastos values (pg_temp.u7(ts), tienda, 'Publicidad en Instagram y Facebook', 120 + 20 * floor(random() * 8), ts, gen_random_uuid(), false, null);
    end if;
    if extract(day from d) = 28 and d >= date '2026-04-28' then
      ts := pg_temp.hora(d, 1000);
      if d = date '2026-04-28' then
        insert into hermes.gastos values (pg_temp.u7(ts), tienda, 'Sueldo de abril (proporcional): Lucía Mamani', 1100, ts, gen_random_uuid(), false, null);
        insert into hermes.gastos values (pg_temp.u7(ts), tienda, 'Sueldo de abril (proporcional): Marco Vaca', 900, ts, gen_random_uuid(), false, null);
      else
        insert into hermes.gastos values (pg_temp.u7(ts), tienda, 'Sueldo de ' || pg_temp.mes(d) || ': Lucía Mamani (encargada)', 3300, ts, gen_random_uuid(), false, null);
        insert into hermes.gastos values (pg_temp.u7(ts), tienda, 'Sueldo de ' || pg_temp.mes(d) || ': Marco Vaca', 2800, ts, gen_random_uuid(), false, null);
        if d >= date '2026-08-28' then insert into hermes.gastos values (pg_temp.u7(ts), tienda, 'Sueldo de ' || pg_temp.mes(d) || ': Diego Suárez', 2800, ts, gen_random_uuid(), false, null); end if;
      end if;
    end if;
    if extract(day from d) = 30 and d >= date '2026-09-30' then
      insert into hermes.gastos values (pg_temp.u7(pg_temp.hora(d, 700)), tienda, 'Honorarios del contador', 450, pg_temp.hora(d, 700), gen_random_uuid(), false, null);
    end if;

    d := d + 1;
  end loop;

  -- Lo que quedó en el catálogo al final
  update hermes.productos pr set precio = t.precio, costo = t.costo, archivado = (t.idx = 7), actualizado_en = now() from t_prod t where pr.id = t.id;
  update hermes.productos set costo = 0 where id = (select id from t_prod where idx = 3);

  raise notice 'Listo: % ventas, % pedidos, % compras, % gastos, % movimientos de stock.',
    (select count(*) from hermes.ventas where tienda_id = tienda), (select count(*) from hermes.pedidos where tienda_id = tienda),
    (select count(*) from hermes.compras where tienda_id = tienda), (select count(*) from hermes.gastos where tienda_id = tienda),
    (select count(*) from hermes.movimientos where tienda_id = tienda);
end $$;

commit;

