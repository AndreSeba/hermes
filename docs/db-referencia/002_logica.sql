-- Hermes · reglas de negocio (triggers, funciones) y vistas de lectura
set search_path = hermes, public;

-- ───────────────────────── Triggers ─────────────────────────

create function tocar_actualizado_en() returns trigger language plpgsql as $$
begin
    new.actualizado_en := now();
    return new;
end $$;

create trigger productos_actualizado
    before update on productos
    for each row execute function tocar_actualizado_en();

-- Todo producto nace con su variante "Único"
create function crear_variante_unica() returns trigger language plpgsql as $$
begin
    insert into variantes (producto_id) values (new.id);
    return new;
end $$;

create trigger productos_variante_unica
    after insert on productos
    for each row execute function crear_variante_unica();

-- Cada movimiento ajusta el stock; si baja de 0 el CHECK de variantes.stock lo rechaza.
-- Un ingreso con costo actualiza el costo del producto ("el costo se actualiza solo
-- cuando anotás que llegó mercadería").
create function aplicar_movimiento_stock() returns trigger language plpgsql as $$
begin
    update variantes set stock = stock + new.cantidad where id = new.variante_id;

    if new.tipo = 'ingreso' and new.costo_unitario is not null then
        update productos p
           set costo = new.costo_unitario
          from variantes v
         where v.id = new.variante_id and p.id = v.producto_id;
    end if;
    return new;
end $$;

create trigger movimientos_aplican_stock
    after insert on movimientos_stock
    for each row execute function aplicar_movimiento_stock();

-- Las ventas descuentan solas
create function descontar_stock_venta() returns trigger language plpgsql as $$
begin
    insert into movimientos_stock (variante_id, tipo, cantidad, venta_item_id)
    values (new.variante_id, 'venta', -new.cantidad, new.id);
    return new;
end $$;

create trigger venta_items_descuentan
    after insert on venta_items
    for each row execute function descontar_stock_venta();

-- ───────────────────────── Funciones ─────────────────────────

-- "Ya llegó la mercadería": suma stock, actualiza costos y deja la compra como recibida.
create function recibir_compra(p_compra_id bigint) returns void language plpgsql as $$
declare
    v_prov bigint;
begin
    select proveedor_id into v_prov
      from compras where id = p_compra_id and not recibida for update;
    if not found then
        raise exception 'La compra % no existe o ya fue recibida', p_compra_id;
    end if;

    insert into movimientos_stock (variante_id, tipo, cantidad, costo_unitario, proveedor_id, compra_item_id)
    select variante_id, 'ingreso', cantidad, costo_unitario, v_prov, id
      from compra_items where compra_id = p_compra_id;

    update compras set recibida = true where id = p_compra_id;
end $$;

-- Anular una venta devuelve el stock
create function anular_venta(p_venta_id bigint) returns void language plpgsql as $$
begin
    perform 1 from ventas where id = p_venta_id and not anulada for update;
    if not found then
        raise exception 'La venta % no existe o ya está anulada', p_venta_id;
    end if;

    insert into movimientos_stock (variante_id, tipo, cantidad, venta_item_id, nota)
    select variante_id, 'devolucion', cantidad, id, 'Venta anulada'
      from venta_items where venta_id = p_venta_id;

    update ventas set anulada = true where id = p_venta_id;
end $$;

-- ───────────────────────── Vistas ─────────────────────────

-- Stock por producto y su estado (Stock → "Para reponer")
create view v_stock_producto as
select p.id as producto_id,
       p.tienda_id,
       p.nombre,
       p.costo,
       p.precio,
       coalesce(sum(v.stock), 0)::int as stock_total,
       case
           when coalesce(sum(v.stock), 0) = 0              then 'agotado'
           when coalesce(sum(v.stock), 0) <= p.stock_minimo then 'pocos'
           else 'ok'
       end as estado
  from productos p
  left join variantes v on v.producto_id = p.id
 where not p.archivado
 group by p.id;

-- Totales de cada venta
create view v_ventas_resumen as
select v.id as venta_id,
       v.tienda_id,
       v.fecha,
       v.cliente,
       v.metodo_pago,
       v.origen,
       v.anulada,
       sum(i.cantidad)::int                                        as unidades,
       sum(i.cantidad * i.precio_unitario)                         as total,
       sum(i.cantidad * (i.precio_unitario - i.costo_unitario))    as ganancia
  from ventas v
  join venta_items i on i.venta_id = v.id
 group by v.id;

-- Totales de cada compra
create view v_compras_resumen as
select c.id as compra_id,
       c.tienda_id,
       c.proveedor_id,
       c.fecha,
       c.recibida,
       c.pagada,
       sum(i.cantidad)::int                    as unidades,
       sum(i.cantidad * i.costo_unitario)      as total
  from compras c
  join compra_items i on i.compra_id = c.id
 group by c.id;

-- Último precio que cobró cada proveedor por cada producto (Proveedores → "Comparar precios")
create view v_ultimo_precio_proveedor as
select distinct on (va.producto_id, c.proveedor_id)
       c.tienda_id,
       va.producto_id,
       c.proveedor_id,
       i.costo_unitario as precio,
       c.fecha
  from compra_items i
  join compras c   on c.id = i.compra_id
  join variantes va on va.id = i.variante_id
 where c.proveedor_id is not null
 order by va.producto_id, c.proveedor_id, c.fecha desc, c.id desc;

-- Resumen por proveedor: cuánto se le compró este mes y cuánto se le debe
create view v_proveedor_resumen as
select pr.id as proveedor_id,
       pr.tienda_id,
       pr.nombre,
       coalesce(sum(cr.total) filter (
           where date_trunc('month', cr.fecha) = date_trunc('month', current_date)), 0) as comprado_mes,
       coalesce(sum(cr.total) filter (where not cr.pagada), 0)                          as debe,
       count(*) filter (where not cr.recibida)                                          as en_camino
  from proveedores pr
  left join v_compras_resumen cr on cr.proveedor_id = pr.id
 group by pr.id;

-- Inicio / Nueva venta: ganancia del mes vs. gastos del mes ("ya cubriste el X % de los gastos")
create view v_resumen_mensual as
with ganancias as (
    select tienda_id, date_trunc('month', fecha)::date as mes, sum(ganancia) as ganancia, sum(total) as ventas
      from v_ventas_resumen
     where not anulada
     group by 1, 2
), egresos as (
    select tienda_id, date_trunc('month', fecha)::date as mes, sum(monto) as gastos
      from gastos
     group by 1, 2
)
select coalesce(g.tienda_id, e.tienda_id) as tienda_id,
       coalesce(g.mes, e.mes)             as mes,
       coalesce(g.ventas, 0)              as ventas,
       coalesce(g.ganancia, 0)            as ganancia,
       coalesce(e.gastos, 0)              as gastos,
       case when coalesce(e.gastos, 0) = 0 then null
            else round(100 * coalesce(g.ganancia, 0) / e.gastos, 1) end as pct_gastos_cubierto
  from ganancias g
  full join egresos e on e.tienda_id = g.tienda_id and e.mes = g.mes;
