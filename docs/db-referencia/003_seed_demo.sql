-- Hermes · datos de demostración (la tienda "Luna Accesorios" del prototipo)
-- Solo para desarrollo. Ejecutar después de 001 y 002.
set search_path = hermes, public;

insert into usuarios (nombre, email, telefono, password_hash)
values ('Andrea', 'andrea@example.com', '+59170000000', 'CAMBIAR-hash-argon2-o-bcrypt');

insert into tiendas (dueno_id, slug, nombre, descripcion, whatsapp, color, instagram_usuario)
select id, 'luna', 'Luna Accesorios', 'Carteras, joyería y accesorios hechos con cariño.', '+59170000000', '#7C5CFC', 'luna.accesorios'
  from usuarios where email = 'andrea@example.com';

insert into categorias (tienda_id, nombre)
select t.id, c from tiendas t, unnest(array['Carteras', 'Joyería', 'Accesorios']) c where t.slug = 'luna';

insert into proveedores (tienda_id, nombre, whatsapp, notas)
select t.id, v.nombre, v.wa, v.notas
  from tiendas t,
       (values ('Cueros Andinos', '+59171111111', 'Entrega en 3 días'),
               ('Bisutería Mayorista', '+59172222222', null)) as v(nombre, wa, notas)
 where t.slug = 'luna';

insert into productos (tienda_id, categoria_id, proveedor_id, nombre, descripcion, precio, costo, margen_objetivo, stock_minimo)
select t.id,
       (select id from categorias where tienda_id = t.id and nombre = x.cat),
       (select id from proveedores where tienda_id = t.id and nombre = x.prov),
       x.nombre, x.descr, x.precio, x.costo, x.margen, x.minimo
  from tiendas t,
       (values ('Carteras',   'Cueros Andinos',      'Cartera de cuero Mía', 'Cuero genuino, forro interior.', 180, 95, 45, 2),
               ('Joyería',    'Bisutería Mayorista', 'Aros luna',            'Acero hipoalergénico.',           35, 12, 60, 3),
               ('Accesorios', null,                  'Pañuelo de seda',      null,                              60, 28, 50, 2)
       ) as x(cat, prov, nombre, descr, precio, costo, margen, minimo)
 where t.slug = 'luna';

-- Cartera Mía en dos colores
update variantes set nombre = 'Negro'
 where producto_id = (select id from productos where nombre = 'Cartera de cuero Mía');
insert into variantes (producto_id, nombre)
select id, 'Café' from productos where nombre = 'Cartera de cuero Mía';

-- Componentes del costo de la Mía (calculadora de precio)
insert into producto_costos (producto_id, concepto, monto)
select id, c.concepto, c.monto
  from productos, (values ('Cuero', 60), ('Forro y herrajes', 20), ('Mano de obra', 15)) as c(concepto, monto)
 where nombre = 'Cartera de cuero Mía';

-- Stock inicial (ajuste)
insert into movimientos_stock (variante_id, tipo, cantidad, nota)
select v.id, 'ajuste', s.cantidad, 'Stock inicial'
  from variantes v
  join productos p on p.id = v.producto_id
  join (values ('Cartera de cuero Mía', 'Negro', 4), ('Cartera de cuero Mía', 'Café', 1),
               ('Aros luna', 'Único', 10), ('Pañuelo de seda', 'Único', 0)) as s(prod, var, cantidad)
    on s.prod = p.nombre and s.var = v.nombre
 where s.cantidad > 0;

-- Una compra recibida (actualiza stock y costo)
with c as (
    insert into compras (tienda_id, proveedor_id, pagada, notas)
    select t.id, pr.id, true, 'Reposición de aros'
      from tiendas t join proveedores pr on pr.tienda_id = t.id
     where t.slug = 'luna' and pr.nombre = 'Bisutería Mayorista'
    returning id
)
insert into compra_items (compra_id, variante_id, cantidad, costo_unitario)
select c.id, v.id, 20, 11
  from c, variantes v join productos p on p.id = v.producto_id
 where p.nombre = 'Aros luna';

select recibir_compra((select max(id) from compras));

-- Una venta
with v as (
    insert into ventas (tienda_id, cliente, metodo_pago)
    select id, 'Carolina', 'qr' from tiendas where slug = 'luna'
    returning id
)
insert into venta_items (venta_id, variante_id, cantidad, precio_unitario, costo_unitario)
select v.id, va.id, 1, p.precio, p.costo
  from v, variantes va join productos p on p.id = va.producto_id
 where p.nombre = 'Cartera de cuero Mía' and va.nombre = 'Negro';

-- Gastos del mes
insert into gastos (tienda_id, concepto, monto)
select t.id, g.concepto, g.monto
  from tiendas t, (values ('Alquiler del puesto', 400), ('Publicidad', 150)) as g(concepto, monto)
 where t.slug = 'luna';
