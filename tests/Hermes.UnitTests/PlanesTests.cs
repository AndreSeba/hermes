using System.IdentityModel.Tokens.Jwt;
using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Domain.Entities;
using Hermes.Infrastructure.Imagenes;
using Hermes.Infrastructure.Persistence;
using Hermes.Infrastructure.Persistence.Configurations;
using Hermes.Infrastructure.Planes;
using Hermes.Infrastructure.Security;
using Hermes.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hermes.UnitTests;

public class PlanesTests : IDisposable
{
    private readonly BaseDePrueba _bd = new();
    private readonly FakeTimeProvider _reloj = new(new DateTimeOffset(2026, 10, 14, 15, 0, 0, TimeSpan.Zero));
    private readonly Guid _admin = Guid.CreateVersion7();

    public void Dispose() => _bd.Dispose();

    private record Mundo(HermesDbContext Db, ProductoService Productos, ImagenService Imagenes, PlanService Planes, PlanesDeTienda Estado);

    private static IOptions<PlanesOptions> Opciones(string plan = "gratis") => Options.Create(new PlanesOptions { PlanSinSuscripcion = plan });

    // El lado de la dueña (con los límites de plan puestos).
    private Mundo Duena(Guid tienda, string planPorDefecto = "gratis")
    {
        var db = _bd.Contexto(tienda);
        var almacen = new AlmacenEnBaseDeDatos(db, _reloj);
        var sesion = new BaseDePrueba.TiendaFija(tienda);
        var estado = new PlanesDeTienda(db, _reloj, Opciones(planPorDefecto));
        var limites = new LimitesDePlan(estado, sesion);
        var imagenes = new ImagenService(db, almacen, sesion, _reloj, limites);
        return new Mundo(db, new ProductoService(db, imagenes, almacen, sesion, _reloj, limites), imagenes, new PlanService(db, estado, almacen, sesion, _reloj, Opciones(planPorDefecto)), estado);
    }

    // El lado del administrador de la plataforma: no tiene tienda.
    private AdminService Administrador(HermesDbContext? db = null)
    {
        db ??= _bd.Contexto(null);
        return new AdminService(db, new PlanesDeTienda(db, _reloj, Opciones()), new AlmacenEnBaseDeDatos(db, _reloj), _reloj);
    }

    private static Task<ProductoDto> Crear(Mundo m, string nombre) => m.Productos.CrearAsync(new GuardarProductoDto(nombre, null, 100, 0, null, true, null, null, null), default);

    private static async Task Crear(Mundo m, int cantidad)
    {
        for (var i = 1; i <= cantidad; i++) await Crear(m, $"Producto {i}");
    }

    private static readonly Guid Gratis = PlanConfiguration.IdGratis, Emprendedor = PlanConfiguration.IdEmprendedor, Negocio = PlanConfiguration.IdNegocio;

    private async Task<Guid> ComprobanteDe(Mundo m) => (await m.Imagenes.SubirAsync(FotoDePrueba.Jpeg(), default, esComprobante: true)).Id;

    private static SolicitarPlanDto Pide(Guid plan, Guid comprobante, int meses = 1, string metodo = "Qr") => new(plan, meses, metodo, comprobante);

    // ───────────── El plan que aplica ─────────────

    [Fact]
    public async Task Sin_suscripcion_rige_el_plan_gratis_con_sus_15_productos()
    {
        var m = Duena(_bd.TiendaA);
        await Crear(m, 15);

        var ex = await Assert.ThrowsAsync<LimiteDePlanException>(() => Crear(m, "El 16"));

        Assert.Equal(402, ex.Status);
        Assert.Contains("15 productos", ex.Message);
        Assert.Contains("Gratis", ex.Message);
        Assert.Equal(15, await m.Db.Productos.CountAsync());
    }

    [Fact]
    public async Task Los_archivados_no_cuentan_pero_desarchivar_vuelve_a_contar()
    {
        var m = Duena(_bd.TiendaA);
        await Crear(m, 15);
        var primero = (await m.Db.Productos.ToListAsync()).OrderBy(p => p.CreadoEn).First();
        await m.Productos.ArchivarAsync(primero.Id, true, default);

        await Crear(m, "El 16 entra porque uno está archivado");
        var ex = await Assert.ThrowsAsync<LimiteDePlanException>(() => m.Productos.ArchivarAsync(primero.Id, false, default));

        Assert.Contains("límite", ex.Message);
        Assert.True((await m.Db.Productos.AsNoTracking().FirstAsync(p => p.Id == primero.Id)).Archivado); // sigue archivado
    }

    [Fact]
    public async Task El_plan_por_defecto_se_configura_y_uno_que_no_existe_cae_al_gratis()
    {
        var conNegocio = Duena(_bd.TiendaA, "negocio");
        await Crear(conNegocio, 20); // Negocio no tiene límite de productos
        Assert.Equal("Negocio", (await conNegocio.Planes.MiPlanAsync(default)).Plan.Nombre);

        var inventado = Duena(_bd.TiendaB, "inventado");
        await Crear(inventado, 15);
        await Assert.ThrowsAsync<LimiteDePlanException>(() => Crear(inventado, "El 16"));
    }

    // ───────────── Asignar, vencer y extender ─────────────

    [Fact]
    public async Task Un_plan_asignado_levanta_el_limite_y_al_vencer_se_conserva_todo_pero_no_se_puede_agregar()
    {
        var m = Duena(_bd.TiendaA);
        await Administrador().AsignarPlanAsync(_bd.TiendaA, _admin, new(Emprendedor, 1), default);
        await Crear(m, 20);

        var mi = await m.Planes.MiPlanAsync(default);
        Assert.Equal(("Emprendedor", 20), (mi.Plan.Nombre, mi.Productos));
        Assert.Equal(_reloj.GetUtcNow().AddMonths(1), mi.Hasta);

        _reloj.Advance(TimeSpan.FromDays(40)); // venció
        var vencida = await Duena(_bd.TiendaA).Planes.MiPlanAsync(default);
        Assert.Equal(("Gratis", 20, null), (vencida.Plan.Nombre, vencida.Productos, vencida.Hasta));
        Assert.Equal(20, await m.Db.Productos.CountAsync()); // NADA se borra
        await Assert.ThrowsAsync<LimiteDePlanException>(() => Crear(Duena(_bd.TiendaA), "Uno más"));
    }

    [Fact]
    public async Task Sin_meses_la_asignacion_no_vence()
    {
        await Administrador().AsignarPlanAsync(_bd.TiendaA, _admin, new(Negocio), default);
        _reloj.Advance(TimeSpan.FromDays(3650));

        var mi = await Duena(_bd.TiendaA).Planes.MiPlanAsync(default);

        Assert.Equal(("Negocio", null), (mi.Plan.Nombre, mi.Hasta));
    }

    [Fact]
    public async Task Cambiar_de_plan_cierra_el_anterior_ahora_y_deja_el_rastro_de_quien_lo_hizo()
    {
        var admin = Administrador();
        await admin.AsignarPlanAsync(_bd.TiendaA, _admin, new(Emprendedor, 6), default);
        _reloj.Advance(TimeSpan.FromDays(10));

        await admin.AsignarPlanAsync(_bd.TiendaA, _admin, new(Negocio, 1), default);

        var todas = await _bd.Contexto(null).Suscripciones.Where(s => s.TiendaId == _bd.TiendaA).OrderBy(s => s.Id).ToListAsync();
        Assert.Equal(2, todas.Count);
        Assert.Equal(_reloj.GetUtcNow().UtcDateTime, todas[0].Hasta); // la anterior termina hoy, no se queda "superpuesta"
        Assert.All(todas, s => Assert.Equal((_admin, "Manual"), (s.AsignadaPorId, s.Origen)));
        Assert.Equal("Negocio", (await Duena(_bd.TiendaA).Planes.MiPlanAsync(default)).Plan.Nombre);
    }

    [Fact]
    public async Task Las_asignaciones_invalidas_se_rechazan()
    {
        var admin = Administrador();

        await Assert.ThrowsAsync<ValidacionException>(() => admin.AsignarPlanAsync(_bd.TiendaA, _admin, new(Emprendedor, 0), default));
        await Assert.ThrowsAsync<ValidacionException>(() => admin.AsignarPlanAsync(_bd.TiendaA, _admin, new(Emprendedor, 37), default));
        await Assert.ThrowsAsync<PlanNoEncontradoException>(() => admin.AsignarPlanAsync(_bd.TiendaA, _admin, new(Guid.NewGuid(), 1), default));
        await Assert.ThrowsAsync<TiendaNoEncontradaException>(() => admin.AsignarPlanAsync(Guid.NewGuid(), _admin, new(Emprendedor, 1), default));
        Assert.Equal(0, await _bd.Contexto(null).Suscripciones.CountAsync());
    }

    // ───────────── Espacio de fotos ─────────────

    [Fact]
    public async Task Sin_espacio_no_se_suben_fotos_pero_el_comprobante_siempre_puede()
    {
        var m = Duena(_bd.TiendaA);
        await Administrador().ActualizarPlanAsync(Gratis, new(0, 15, 1, true), default); // 1 MB
        m.Db.Imagenes.Add(new Imagen { BytesGuardados = 1_048_576 - 10, CreadaEn = _reloj.GetUtcNow() }); // ya casi lleno

        await m.Db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<LimiteDePlanException>(() => m.Imagenes.SubirAsync(FotoDePrueba.Jpeg(), default));
        var comprobante = await ComprobanteDe(m); // no cuenta: si no, una tienda llena no podría pagar para tener más

        Assert.Contains("espacio de fotos", ex.Message);
        Assert.NotEqual(Guid.Empty, comprobante);
    }

    // ───────────── Pedir un plan ─────────────

    [Fact]
    public async Task Pedir_un_plan_calcula_el_monto_y_queda_pendiente_con_el_comprobante()
    {
        var m = Duena(_bd.TiendaA);
        var comprobante = await ComprobanteDe(m);

        var p = await m.Planes.SolicitarAsync(Pide(Emprendedor, comprobante, meses: 3, metodo: "Transferencia"), default);

        Assert.Equal((EstadoPago.Pendiente, 3, 117m, "Transferencia", "Emprendedor"), (p.Estado, p.Meses, p.Monto, p.Metodo, p.Plan)); // 39 × 3
        Assert.NotNull(p.Comprobante);
        var mi = await m.Planes.MiPlanAsync(default);
        Assert.Equal(p.Id, mi.Pendiente!.Id);
        Assert.Equal("Gratis", mi.Plan.Nombre); // el plan no cambia hasta que se apruebe
    }

    [Fact]
    public async Task Los_pedidos_invalidos_se_rechazan()
    {
        var m = Duena(_bd.TiendaA);
        var c = await ComprobanteDe(m);

        await Assert.ThrowsAsync<ValidacionException>(() => m.Planes.SolicitarAsync(Pide(Emprendedor, c, meses: 2), default));              // meses que no se ofrecen
        await Assert.ThrowsAsync<ValidacionException>(() => m.Planes.SolicitarAsync(Pide(Emprendedor, c, metodo: "Efectivo"), default));   // método que no es
        await Assert.ThrowsAsync<ValidacionException>(() => m.Planes.SolicitarAsync(Pide(Gratis, c), default));                          // el gratis no se paga
        await Assert.ThrowsAsync<PlanNoEncontradoException>(() => m.Planes.SolicitarAsync(Pide(Guid.NewGuid(), c), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Planes.SolicitarAsync(Pide(Emprendedor, Guid.NewGuid()), default));          // sin comprobante real

        await Administrador().ActualizarPlanAsync(Emprendedor, new(39, 150, 1000, false), default); // desactivado: ya no se ofrece
        await Assert.ThrowsAsync<ValidacionException>(() => m.Planes.SolicitarAsync(Pide(Emprendedor, c), default));
        Assert.Equal(0, await m.Db.PagosPlan.CountAsync());
    }

    [Fact]
    public async Task El_comprobante_de_otra_tienda_no_sirve()
    {
        var deB = Duena(_bd.TiendaB);
        var ajeno = await ComprobanteDe(deB);

        await Assert.ThrowsAsync<ValidacionException>(() => Duena(_bd.TiendaA).Planes.SolicitarAsync(Pide(Emprendedor, ajeno), default));
    }

    [Fact]
    public async Task Solo_hay_un_pedido_esperando_por_tienda_hasta_que_se_resuelve()
    {
        var m = Duena(_bd.TiendaA);
        var primero = await m.Planes.SolicitarAsync(Pide(Emprendedor, await ComprobanteDe(m)), default);

        await Assert.ThrowsAsync<PagoPendienteExistenteException>(async () => await Duena(_bd.TiendaA).Planes.SolicitarAsync(Pide(Negocio, await ComprobanteDe(m)), default));
        await Administrador().RechazarAsync(primero.Id, _admin, new("El comprobante no se ve"), default);

        var segundo = await Duena(_bd.TiendaA).Planes.SolicitarAsync(Pide(Negocio, await ComprobanteDe(m)), default); // ahora sí
        Assert.Equal(EstadoPago.Pendiente, segundo.Estado);
    }

    // ───────────── El administrador resuelve ─────────────

    [Fact]
    public async Task Aprobar_activa_el_plan_por_los_meses_pagados_y_deja_quien_lo_hizo()
    {
        var m = Duena(_bd.TiendaA);
        var pago = await m.Planes.SolicitarAsync(Pide(Emprendedor, await ComprobanteDe(m), meses: 6), default);
        _reloj.Advance(TimeSpan.FromHours(5));

        var r = await Administrador().AprobarAsync(pago.Id, _admin, default);

        Assert.Equal(EstadoPago.Aprobado, r.Pago.Estado);
        var mi = await Duena(_bd.TiendaA).Planes.MiPlanAsync(default);
        Assert.Equal(("Emprendedor", null), (mi.Plan.Nombre, mi.Pendiente));
        Assert.Equal(_reloj.GetUtcNow().AddMonths(6), mi.Hasta);
        var guardado = await _bd.Contexto(null).PagosPlan.SingleAsync();
        Assert.Equal((_admin, _reloj.GetUtcNow().UtcDateTime), (guardado.ResueltoPorId, guardado.ResueltoEn));
        var s = await _bd.Contexto(null).Suscripciones.SingleAsync();
        Assert.Equal(("Pago", pago.Id), (s.Origen, s.PagoId));
    }

    [Fact]
    public async Task Un_segundo_pago_del_mismo_plan_extiende_desde_donde_termina_no_desde_hoy()
    {
        var admin = Administrador();
        var m = Duena(_bd.TiendaA);
        var uno = await m.Planes.SolicitarAsync(Pide(Emprendedor, await ComprobanteDe(m), meses: 1), default);
        await admin.AprobarAsync(uno.Id, _admin, default);
        var vence = (await m.Planes.MiPlanAsync(default)).Hasta!.Value;
        _reloj.Advance(TimeSpan.FromDays(10)); // paga por adelantado, 20 días antes de que venza

        var dos = await Duena(_bd.TiendaA).Planes.SolicitarAsync(Pide(Emprendedor, await ComprobanteDe(m), meses: 3), default);
        await admin.AprobarAsync(dos.Id, _admin, default);

        var mi = await Duena(_bd.TiendaA).Planes.MiPlanAsync(default);
        Assert.Equal(vence.AddMonths(3), mi.Hasta); // no pierde los días que le quedaban
        Assert.Equal(1, await _bd.Contexto(null).Suscripciones.CountAsync()); // se alarga la misma
    }

    [Fact]
    public async Task Un_pago_no_acorta_un_plan_sin_vencimiento()
    {
        await Administrador().AsignarPlanAsync(_bd.TiendaA, _admin, new(Emprendedor), default); // cortesía permanente
        var m = Duena(_bd.TiendaA);
        var pago = await m.Planes.SolicitarAsync(Pide(Emprendedor, await ComprobanteDe(m), meses: 1), default);

        await Administrador().AprobarAsync(pago.Id, _admin, default);

        Assert.Null((await m.Planes.MiPlanAsync(default)).Hasta);
        Assert.Equal(1, await _bd.Contexto(null).Suscripciones.CountAsync());
    }

    [Fact]
    public async Task Rechazar_pide_un_motivo_y_la_duena_lo_ve()
    {
        var m = Duena(_bd.TiendaA);
        var pago = await m.Planes.SolicitarAsync(Pide(Emprendedor, await ComprobanteDe(m)), default);

        await Assert.ThrowsAsync<ValidacionException>(() => Administrador().RechazarAsync(pago.Id, _admin, new("  "), default));
        var r = await Administrador().RechazarAsync(pago.Id, _admin, new("El monto no coincide"), default);

        Assert.Equal(EstadoPago.Rechazado, r.Pago.Estado);
        var visto = (await Duena(_bd.TiendaA).Planes.MisPagosAsync(default)).Single();
        Assert.Equal((EstadoPago.Rechazado, "El monto no coincide"), (visto.Estado, visto.Motivo));
        Assert.Equal("Gratis", (await Duena(_bd.TiendaA).Planes.MiPlanAsync(default)).Plan.Nombre);
    }

    [Fact]
    public async Task Un_pago_resuelto_no_se_resuelve_otra_vez_y_uno_que_no_existe_da_404()
    {
        var m = Duena(_bd.TiendaA);
        var pago = await m.Planes.SolicitarAsync(Pide(Emprendedor, await ComprobanteDe(m)), default);
        await Administrador().AprobarAsync(pago.Id, _admin, default);

        await Assert.ThrowsAsync<PagoYaResueltoException>(() => Administrador().AprobarAsync(pago.Id, _admin, default));
        await Assert.ThrowsAsync<PagoYaResueltoException>(() => Administrador().RechazarAsync(pago.Id, _admin, new("Tarde"), default));
        await Assert.ThrowsAsync<PagoNoEncontradoException>(() => Administrador().AprobarAsync(Guid.NewGuid(), _admin, default));
        Assert.Equal(1, await _bd.Contexto(null).Suscripciones.CountAsync()); // el plan se activó UNA vez
    }

    [Fact]
    public async Task El_administrador_ve_los_pendientes_del_mas_viejo_al_mas_nuevo_con_tienda_email_y_comprobante()
    {
        var a = Duena(_bd.TiendaA);
        var b = Duena(_bd.TiendaB);
        var deA = await a.Planes.SolicitarAsync(Pide(Emprendedor, await ComprobanteDe(a)), default);
        _reloj.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(3);
        var deB = await b.Planes.SolicitarAsync(Pide(Negocio, await ComprobanteDe(b), meses: 3), default);

        var pagina = await Administrador().PagosAsync(new(), default);

        Assert.Equal([deA.Id, deB.Id], pagina.Items.Select(i => i.Pago.Id));
        var primero = pagina.Items[0];
        Assert.Equal(("Tienda A", "tienda-a", "duena@hermes.test", "Emprendedor"), (primero.Tienda, primero.Slug, primero.Email, primero.Pago.Plan));
        Assert.NotNull(primero.Pago.Comprobante); // el administrador ve la foto de OTRA tienda
        Assert.Contains(primero.TiendaId.ToString(), primero.Pago.Comprobante!.Detalle);
        await Administrador().AprobarAsync(deA.Id, _admin, default);
        Assert.Equal([deB.Id], (await Administrador().PagosAsync(new(), default)).Items.Select(i => i.Pago.Id));
        Assert.Equal([deA.Id], (await Administrador().PagosAsync(new(EstadoPago.Aprobado), default)).Items.Select(i => i.Pago.Id));
    }

    [Fact]
    public async Task El_panel_de_tiendas_muestra_el_plan_y_el_uso_de_cada_una_y_busca()
    {
        var a = Duena(_bd.TiendaA);
        await Crear(a, 3);
        await Administrador().AsignarPlanAsync(_bd.TiendaB, _admin, new(Negocio, 2), default);

        var todas = await Administrador().TiendasAsync(new(), default);
        var deA = todas.Items.Single(t => t.Id == _bd.TiendaA);
        var deB = todas.Items.Single(t => t.Id == _bd.TiendaB);

        Assert.Equal(("Gratis", 3, null), (deA.Plan, deA.Productos, deA.Hasta));
        Assert.Equal(("Negocio", _reloj.GetUtcNow().AddMonths(2)), (deB.Plan, deB.Hasta));
        Assert.Equal(["Tienda B"], (await Administrador().TiendasAsync(new(Buscar: "TIENDA-B"), default)).Items.Select(t => t.Nombre));
        Assert.Equal(2, (await Administrador().TiendasAsync(new(Buscar: "duena@hermes.test"), default)).Items.Count); // por el email de la dueña
    }

    // ───────────── Editar los planes ─────────────

    [Fact]
    public async Task Editar_un_plan_cambia_el_limite_al_instante_y_valida()
    {
        var admin = Administrador();
        var m = Duena(_bd.TiendaA);
        await Crear(m, 3);

        await admin.ActualizarPlanAsync(Gratis, new(0, 3, 100, true), default);
        await Assert.ThrowsAsync<LimiteDePlanException>(() => Crear(Duena(_bd.TiendaA), "El 4"));
        await admin.ActualizarPlanAsync(Gratis, new(0, null, null, true), default); // sin límites
        await Crear(Duena(_bd.TiendaA), "El 4 ahora entra");

        await Assert.ThrowsAsync<ValidacionException>(() => admin.ActualizarPlanAsync(Gratis, new(10, 15, 100, true), default));       // el Gratis es gratis
        await Assert.ThrowsAsync<ValidacionException>(() => admin.ActualizarPlanAsync(Gratis, new(0, 15, 100, false), default));       // y siempre activo
        await Assert.ThrowsAsync<ValidacionException>(() => admin.ActualizarPlanAsync(Emprendedor, new(0, 150, 1000, true), default));  // uno pago no puede ser gratis
        await Assert.ThrowsAsync<ValidacionException>(() => admin.ActualizarPlanAsync(Emprendedor, new(39.555m, 150, 1000, true), default));
        await Assert.ThrowsAsync<ValidacionException>(() => admin.ActualizarPlanAsync(Emprendedor, new(39, 0, 1000, true), default));
        await Assert.ThrowsAsync<ValidacionException>(() => admin.ActualizarPlanAsync(Emprendedor, new(39, 150, -5, true), default));
        await Assert.ThrowsAsync<PlanNoEncontradoException>(() => admin.ActualizarPlanAsync(Guid.NewGuid(), new(39, 150, 1000, true), default));
    }

    [Fact]
    public async Task El_precio_pedido_no_cambia_si_despues_se_cambia_el_precio_del_plan()
    {
        var m = Duena(_bd.TiendaA);
        var pago = await m.Planes.SolicitarAsync(Pide(Emprendedor, await ComprobanteDe(m), meses: 3), default);

        await Administrador().ActualizarPlanAsync(Emprendedor, new(60, 150, 1000, true), default);

        Assert.Equal(117m, (await m.Planes.MisPagosAsync(default)).Single(p => p.Id == pago.Id).Monto); // 39 × 3, el de cuando lo pidió
        Assert.Equal(["Gratis", "Emprendedor", "Negocio"], (await m.Planes.ListarPlanesAsync(default)).Planes.Select(p => p.Nombre));
    }

    // ───────────── Aislamiento y rol ─────────────

    [Fact]
    public async Task Una_tienda_no_ve_los_pagos_ni_el_plan_pendiente_de_otra()
    {
        var a = Duena(_bd.TiendaA);
        await a.Planes.SolicitarAsync(Pide(Emprendedor, await ComprobanteDe(a)), default);

        var b = Duena(_bd.TiendaB);

        Assert.Empty(await b.Planes.MisPagosAsync(default));
        Assert.Null((await b.Planes.MiPlanAsync(default)).Pendiente);
    }

    [Fact]
    public async Task Sin_tienda_en_sesion_no_se_ve_ni_se_pide_nada()
    {
        var db = _bd.Contexto(null);
        var sesion = new BaseDePrueba.TiendaFija(null);
        var estado = new PlanesDeTienda(db, _reloj, Opciones());
        var planes = new PlanService(db, estado, new AlmacenEnBaseDeDatos(db, _reloj), sesion, _reloj, Opciones());

        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => planes.MiPlanAsync(default));
        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => planes.ListarPlanesAsync(default));
        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => planes.SolicitarAsync(Pide(Emprendedor, Guid.NewGuid()), default));
        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => new LimitesDePlan(estado, sesion).ExigirPoderCrearProductoAsync(default));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void El_rol_de_administrador_solo_viaja_en_el_token_si_el_usuario_lo_es_en_la_base(bool esAdmin, bool esperaClaim)
    {
        var jwt = new JwtTokenService(Options.Create(new JwtOptions { SecretKey = new string('k', 40), Issuer = "i", Audience = "a", ExpiracionMinutos = 15 }), _reloj);
        var usuario = new Usuario { Nombre = "U", Email = "u@x.test", EsAdministrador = esAdmin };
        var tienda = new Tienda { Slug = "t", Nombre = "T" };

        var (token, _) = jwt.EmitirAccessToken(usuario, tienda, []);

        var claims = new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.ToList();
        Assert.Equal(esperaClaim, claims.Any(c => c.Type == JwtTokenService.ClaimRol && c.Value == JwtTokenService.RolAdministrador));
    }

    // ───────────── Concurrencia (solo con Postgres real) ─────────────

    private async Task ConPostgresAsync(Func<Func<Guid?, HermesDbContext>, Guid, Task> prueba)
    {
        var conexion = Environment.GetEnvironmentVariable("HERMES_TEST_PG");
        if (string.IsNullOrWhiteSpace(conexion)) return;

        var baseDeDatos = "hermes_test_" + Guid.NewGuid().ToString("N")[..10];
        await using (var admin = new Npgsql.NpgsqlConnection(conexion + ";Database=postgres"))
        {
            await admin.OpenAsync();
            await new Npgsql.NpgsqlCommand($"create database {baseDeDatos}", admin).ExecuteNonQueryAsync();
        }
        HermesDbContext Nuevo(Guid? tienda) => new(
            new DbContextOptionsBuilder<HermesDbContext>().UseNpgsql($"{conexion};Database={baseDeDatos}").UseSnakeCaseNamingConvention().Options,
            new BaseDePrueba.TiendaFija(tienda));
        try
        {
            Guid tienda;
            await using (var db = Nuevo(null))
            {
                await db.Database.EnsureCreatedAsync();
                var dueno = new Usuario { Nombre = "D", Email = "d@x.test", PasswordHash = "x" };
                var t = new Tienda { DuenoId = dueno.Id, Slug = "tienda-x", Nombre = "X" };
                db.AddRange(dueno, t);
                await db.SaveChangesAsync();
                tienda = t.Id;
            }
            await prueba(Nuevo, tienda);
        }
        finally
        {
            Npgsql.NpgsqlConnection.ClearAllPools();
            await using var admin = new Npgsql.NpgsqlConnection(conexion + ";Database=postgres");
            await admin.OpenAsync();
            await new Npgsql.NpgsqlCommand($"drop database if exists {baseDeDatos} with (force)", admin).ExecuteNonQueryAsync();
        }
    }

    private PlanService PlanDe(HermesDbContext db, Guid tienda)
        => new(db, new PlanesDeTienda(db, _reloj, Opciones()), new AlmacenEnBaseDeDatos(db, _reloj), new BaseDePrueba.TiendaFija(tienda), _reloj, Opciones());

    [Fact]
    public async Task Cinco_pedidos_simultaneos_de_plan_dejan_uno_solo_pendiente_con_Postgres_real()
    {
        await ConPostgresAsync(async (nuevo, tienda) =>
        {
            Guid comprobante;
            await using (var db = nuevo(tienda))
            {
                var almacen = new AlmacenEnBaseDeDatos(db, _reloj);
                comprobante = (await new ImagenService(db, almacen, new BaseDePrueba.TiendaFija(tienda), _reloj).SubirAsync(FotoDePrueba.Jpeg(), default, true)).Id;
            }

            var resultados = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
            {
                await using var db = nuevo(tienda);
                try { await PlanDe(db, tienda).SolicitarAsync(Pide(Emprendedor, comprobante), default); return true; }
                catch (PagoPendienteExistenteException) { return false; }
            }));

            Assert.Equal(1, resultados.Count(r => r));
            await using var lectura = nuevo(null);
            Assert.Equal(1, await lectura.PagosPlan.CountAsync(p => p.Estado == EstadoPago.Pendiente));
        });
    }

    [Fact]
    public async Task Cinco_aprobaciones_simultaneas_activan_el_plan_una_sola_vez_con_Postgres_real()
    {
        await ConPostgresAsync(async (nuevo, tienda) =>
        {
            Guid pago;
            await using (var db = nuevo(tienda))
            {
                var almacen = new AlmacenEnBaseDeDatos(db, _reloj);
                var comprobante = (await new ImagenService(db, almacen, new BaseDePrueba.TiendaFija(tienda), _reloj).SubirAsync(FotoDePrueba.Jpeg(), default, true)).Id;
                pago = (await PlanDe(db, tienda).SolicitarAsync(Pide(Emprendedor, comprobante, meses: 3), default)).Id;
            }

            var resultados = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
            {
                await using var db = nuevo(null);
                try { await Administrador(db).AprobarAsync(pago, _admin, default); return true; }
                catch (PagoYaResueltoException) { return false; }
            }));

            Assert.Equal(1, resultados.Count(r => r));
            await using var lectura = nuevo(null);
            Assert.Equal(1, await lectura.Suscripciones.CountAsync(s => s.TiendaId == tienda)); // y no 5 suscripciones que se extienden entre sí
        });
    }
}
