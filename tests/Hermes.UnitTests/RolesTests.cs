using System.IdentityModel.Tokens.Jwt;
using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Hermes.Domain.Entities;
using Hermes.Domain.Enums;
using Hermes.Domain.Seguridad;
using Hermes.Infrastructure.Imagenes;
using Hermes.Infrastructure.Persistence;
using Hermes.Infrastructure.Planes;
using Hermes.Infrastructure.Security;
using Hermes.Infrastructure.Services;
using Hermes.Infrastructure.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hermes.UnitTests;

// Roles, equipo y permisos. Cada módulo nuevo lleva su prueba de aislamiento entre tiendas.
public class RolesTests : IDisposable
{
    private readonly BaseDePrueba _bd = new();
    private readonly FakeTimeProvider _reloj = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly SesionesRevocadas _revocadas;
    private readonly IOptions<JwtOptions> _jwt = Options.Create(new JwtOptions
    {
        Issuer = "hermes-api", Audience = "hermes-web", SecretKey = "clave-de-prueba-de-al-menos-32-caracteres!!", ExpiracionMinutos = 15, RefreshDias = 30,
    });

    public RolesTests() => _revocadas = new SesionesRevocadas(_cache, _reloj);

    public void Dispose() => _bd.Dispose();

    // Una persona con permisos fijos y un id (lo que en la API sale del token).
    private sealed class Persona(Guid? id, params string[] permisos) : IUsuarioActual
    {
        public Guid? UsuarioId { get; } = id;
        public bool Tiene(string permiso) => permisos.Contains(permiso);
    }

    private static Persona Duena(Guid? id = null) => new(id ?? Guid.CreateVersion7(), Permisos.Todos.ToArray());

    private RolService Roles(HermesDbContext db, IUsuarioActual? quien = null) => new(db, quien, _revocadas);

    private EquipoService Equipo(HermesDbContext db, Guid tienda, IUsuarioActual? quien = null, string plan = "emprendedor")
    {
        var sesion = new BaseDePrueba.TiendaFija(tienda);
        var estado = new PlanesDeTienda(db, _reloj, Options.Create(new PlanesOptions { PlanSinSuscripcion = plan }));
        return new EquipoService(db, sesion, estado, _reloj, new LimitesDePlan(estado, sesion), quien, _revocadas);
    }

    private AuthService Auth(HermesDbContext db)
        => new(db, new JwtTokenService(_jwt, _reloj), new BloqueoDeLogin(_cache, _reloj), _reloj, _jwt);

    private async Task<RolDto> RolVendedor(Guid tienda)
    {
        await using var db = _bd.Contexto(tienda);
        var lista = await Roles(db).ListarAsync(false, default);
        return lista.Roles.Single(r => r.Nombre == "Vendedor");
    }

    private static CrearMiembroDto Alta(Guid rol, string email = "carlos@ejemplo.com") => new("Carlos Vendedor", email, "provisoria1", rol);

    // ───────────── Roles ─────────────

    [Fact]
    public async Task La_primera_vez_el_negocio_recibe_los_tres_roles_por_defecto_y_no_se_duplican()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);

        var primera = await Roles(db).ListarAsync(false, default);
        var segunda = await Roles(db).ListarAsync(false, default);

        Assert.Equal(["Encargado", "Solo ver", "Vendedor"], primera.Roles.Select(r => r.Nombre).Order());
        Assert.Equal(3, segunda.Roles.Count);
        Assert.Equal(Permisos.Catalogo.Count, primera.Permisos.Count);
        var vendedor = primera.Roles.Single(r => r.Nombre == "Vendedor");
        Assert.Contains(Permisos.VentasRegistrar, vendedor.Permisos);
        Assert.DoesNotContain(Permisos.CostosVer, vendedor.Permisos); // el vendedor no ve costos
        Assert.DoesNotContain(Permisos.VentasVer, vendedor.Permisos); // ni las ventas de los demás
    }

    [Fact]
    public async Task Los_roles_por_defecto_solo_usan_permisos_del_catalogo()
    {
        foreach (var d in RolesPorDefecto.Todos)
            foreach (var c in d.Permisos)
                Assert.True(Permisos.Existe(c), $"{d.Nombre}: «{c}» no está en el catálogo");
        Assert.Equal(Permisos.Todos.Count, Permisos.Todos.Distinct().Count());
        Assert.All(Permisos.Todos, c => Assert.DoesNotContain('|', c)); // «|» separa alternativas en una policy
    }

    [Fact]
    public async Task Una_tienda_no_ve_ni_edita_los_roles_ni_el_equipo_de_otra()
    {
        var vendedorA = await RolVendedor(_bd.TiendaA);
        await using (var dbA = _bd.Contexto(_bd.TiendaA))
            await Equipo(dbA, _bd.TiendaA).CrearAsync(Alta(vendedorA.Id), default);

        await using var dbB = _bd.Contexto(_bd.TiendaB);
        var deB = await Roles(dbB).ListarAsync(true, default);
        Assert.DoesNotContain(deB.Roles, r => r.Id == vendedorA.Id);
        await Assert.ThrowsAsync<RolNoEncontradoException>(() =>
            Roles(dbB).ActualizarAsync(vendedorA.Id, new("Robado", null, [Permisos.ProductosVer]), default));

        var equipoB = await Equipo(dbB, _bd.TiendaB).ListarAsync(default);
        Assert.Single(equipoB.Miembros); // solo el dueño: Carlos es de la tienda A
        var miembroA = await _bd.Contexto(_bd.TiendaA).Miembros.SingleAsync();
        await Assert.ThrowsAsync<MiembroNoEncontradoException>(() =>
            Equipo(dbB, _bd.TiendaB).ActualizarAsync(miembroA.Id, new(deB.Roles[0].Id, false), default));
        // Un rol de OTRA tienda no se puede asignar: para esta tienda "no existe".
        await Assert.ThrowsAsync<RolNoEncontradoException>(() => Equipo(dbB, _bd.TiendaB).CrearAsync(Alta(vendedorA.Id, "otro@ejemplo.com"), default));
    }

    [Fact]
    public async Task Crear_un_rol_valida_nombre_permisos_y_duplicados()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var servicio = Roles(db);

        await Assert.ThrowsAsync<ValidacionException>(() => servicio.CrearAsync(new("  ", null, [Permisos.ProductosVer]), default));
        await Assert.ThrowsAsync<ValidacionException>(() => servicio.CrearAsync(new("Cajero", null, []), default)); // sin permisos
        await Assert.ThrowsAsync<PermisoInvalidoException>(() => servicio.CrearAsync(new("Cajero", null, ["ventas.registrar", "cualquier.cosa"]), default));
        await Assert.ThrowsAsync<ValidacionException>(() => servicio.CrearAsync(new("Dueño", null, [Permisos.ProductosVer]), default)); // se confunde con el dueño

        var cajero = await servicio.CrearAsync(new(" Cajero ", "Cobra", [Permisos.VentasRegistrar, Permisos.VentasRegistrar, Permisos.ProductosVer]), default);
        Assert.Equal("Cajero", cajero.Nombre);
        Assert.Equal(2, cajero.Permisos.Count); // el repetido cuenta una sola vez
        await Assert.ThrowsAsync<NombreRolDuplicadoException>(() => servicio.CrearAsync(new("cajero", null, [Permisos.ProductosVer]), default));
    }

    [Fact]
    public async Task Nadie_le_da_a_un_rol_mas_permisos_de_los_que_tiene()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        // Una encargada con "roles.gestionar" pero SIN costos.editar ni equipo.gestionar.
        var encargada = new Persona(Guid.CreateVersion7(), Permisos.RolesGestionar, Permisos.VentasRegistrar, Permisos.ProductosVer);
        var servicio = Roles(db, encargada);

        var ex = await Assert.ThrowsAsync<SinPermisoException>(() =>
            servicio.CrearAsync(new("Superrol", null, [Permisos.VentasRegistrar, Permisos.EquipoGestionar]), default));
        Assert.Contains("no lo tenés", ex.Message);
        Assert.Empty(await db.Roles.ToListAsync()); // nada quedó guardado

        // Lo que SÍ tiene lo puede dar.
        var bien = await servicio.CrearAsync(new("Cajero", null, [Permisos.VentasRegistrar]), default);
        // Al editar solo se le exige lo que AGREGA: no puede sumar costos.ver que no tiene.
        await Assert.ThrowsAsync<SinPermisoException>(() =>
            servicio.ActualizarAsync(bien.Id, new("Cajero", null, [Permisos.VentasRegistrar, Permisos.CostosVer]), default));
    }

    [Fact]
    public async Task Cambiar_los_permisos_de_un_rol_cierra_las_sesiones_de_quienes_lo_tienen()
    {
        var vendedor = await RolVendedor(_bd.TiendaA);
        MiembroDto carlos;
        await using (var db = _bd.Contexto(_bd.TiendaA)) carlos = await Equipo(db, _bd.TiendaA).CrearAsync(Alta(vendedor.Id), default);

        await using (var db = _bd.Contexto(_bd.TiendaA))
        {
            var antes = _reloj.GetUtcNow().AddSeconds(-5);
            _reloj.Advance(TimeSpan.FromSeconds(10));
            var editado = await Roles(db, Duena()).ActualizarAsync(vendedor.Id,
                new("Vendedor", null, [Permisos.VentasRegistrar, Permisos.ProductosVer]), default);

            Assert.Equal(2, editado.Permisos.Count);
            Assert.Equal(1, editado.Personas);
            Assert.True(_revocadas.Invalida(carlos.UsuarioId, antes)); // el token emitido antes ya no sirve
            Assert.False(_revocadas.Invalida(carlos.UsuarioId, _reloj.GetUtcNow())); // uno nuevo sí
        }
        await using var consulta = _bd.Contexto(_bd.TiendaA);
        Assert.Equal(2, await consulta.RolPermisos.CountAsync(p => p.RolId == vendedor.Id)); // se reemplazaron, no se duplicaron
    }

    [Fact]
    public async Task No_se_desactiva_un_rol_con_personas_activas()
    {
        var vendedor = await RolVendedor(_bd.TiendaA);
        await using var db = _bd.Contexto(_bd.TiendaA);
        var equipo = Equipo(db, _bd.TiendaA);
        var carlos = await equipo.CrearAsync(Alta(vendedor.Id), default);

        var ex = await Assert.ThrowsAsync<RolConPersonasException>(() =>
            Roles(db).ActualizarAsync(vendedor.Id, new("Vendedor", null, vendedor.Permisos, Activo: false), default));
        Assert.Contains("1 persona", ex.Message);

        // Sin personas activas, sí.
        await equipo.ActualizarAsync(carlos.Id, new(vendedor.Id, false), default);
        var desactivado = await Roles(db).ActualizarAsync(vendedor.Id, new("Vendedor", null, vendedor.Permisos, Activo: false), default);
        Assert.False(desactivado.Activo);
        await Assert.ThrowsAsync<RolNoEncontradoException>(() => equipo.CrearAsync(Alta(vendedor.Id, "otra@ejemplo.com"), default)); // un rol inactivo no se asigna
    }

    // ───────────── Equipo ─────────────

    [Fact]
    public async Task El_dueno_crea_la_cuenta_con_contrasena_provisoria_y_figura_primero()
    {
        var vendedor = await RolVendedor(_bd.TiendaA);
        await using var db = _bd.Contexto(_bd.TiendaA);

        var carlos = await Equipo(db, _bd.TiendaA).CrearAsync(Alta(vendedor.Id, "  Carlos@Ejemplo.com "), default);

        Assert.Equal(("carlos@ejemplo.com", "Vendedor", true, false), (carlos.Email, carlos.Rol, carlos.Activo, carlos.EsDueno));
        var usuario = await db.Usuarios.SingleAsync(u => u.Email == "carlos@ejemplo.com");
        Assert.True(usuario.DebeCambiarPassword);
        Assert.NotEqual("provisoria1", usuario.PasswordHash); // se guarda el hash
        Assert.True(BCrypt.Net.BCrypt.Verify("provisoria1", usuario.PasswordHash));

        var equipo = await Equipo(db, _bd.TiendaA).ListarAsync(default);
        Assert.Equal(["Dueña", "Carlos Vendedor"], equipo.Miembros.Select(m => m.Nombre));
        Assert.True(equipo.Miembros[0].EsDueno);
        Assert.Equal(2, equipo.Usuarios);
        Assert.Equal(3, equipo.MaxUsuarios);
    }

    [Fact]
    public async Task Cada_correo_tiene_una_sola_cuenta_en_todo_el_sistema()
    {
        var vendedor = await RolVendedor(_bd.TiendaA);
        await using var db = _bd.Contexto(_bd.TiendaA);
        var equipo = Equipo(db, _bd.TiendaA);
        await equipo.CrearAsync(Alta(vendedor.Id), default);

        await Assert.ThrowsAsync<CorreoDeEquipoEnUsoException>(() => equipo.CrearAsync(Alta(vendedor.Id), default));
        await Assert.ThrowsAsync<CorreoDeEquipoEnUsoException>(() => equipo.CrearAsync(Alta(vendedor.Id, "duena@hermes.test"), default)); // el del dueño
        await Assert.ThrowsAsync<ValidacionException>(() => equipo.CrearAsync(Alta(vendedor.Id, "no-es-un-correo"), default));
        await Assert.ThrowsAsync<ValidacionException>(() => equipo.CrearAsync(Alta(vendedor.Id) with { Email = "otro@ejemplo.com", Password = "corta" }, default));
    }

    [Fact]
    public async Task El_plan_limita_cuantas_personas_entran_contando_al_dueno()
    {
        var vendedor = await RolVendedor(_bd.TiendaA);
        await using var db = _bd.Contexto(_bd.TiendaA);

        // Plan Gratis: 1 persona (el dueño). No entra nadie.
        var gratis = Equipo(db, _bd.TiendaA, plan: "gratis");
        var ex = await Assert.ThrowsAsync<LimiteDePlanException>(() => gratis.CrearAsync(Alta(vendedor.Id), default));
        Assert.Contains("1 persona", ex.Message);

        // Emprendedor: 3 (dueño + 2).
        var emprendedor = Equipo(db, _bd.TiendaA, plan: "emprendedor");
        var a = await emprendedor.CrearAsync(Alta(vendedor.Id, "a@ejemplo.com"), default);
        await emprendedor.CrearAsync(Alta(vendedor.Id, "b@ejemplo.com"), default);
        await Assert.ThrowsAsync<LimiteDePlanException>(() => emprendedor.CrearAsync(Alta(vendedor.Id, "c@ejemplo.com"), default));

        // Desactivar a una libera el lugar; reactivarla lo vuelve a ocupar.
        await emprendedor.ActualizarAsync(a.Id, new(vendedor.Id, false), default);
        var c = await emprendedor.CrearAsync(Alta(vendedor.Id, "c@ejemplo.com"), default);
        await Assert.ThrowsAsync<LimiteDePlanException>(() => emprendedor.ActualizarAsync(a.Id, new(vendedor.Id, true), default));
        Assert.True(c.Activo);
    }

    [Fact]
    public async Task Cambiar_de_rol_o_desactivar_cierra_sus_sesiones_y_nadie_se_toca_a_si_mismo()
    {
        var vendedor = await RolVendedor(_bd.TiendaA);
        await using var db = _bd.Contexto(_bd.TiendaA);
        var dueña = Duena();
        var equipo = Equipo(db, _bd.TiendaA, dueña);
        var carlos = await equipo.CrearAsync(Alta(vendedor.Id), default);
        var encargado = (await Roles(db).ListarAsync(false, default)).Roles.Single(r => r.Nombre == "Encargado");

        // Tiene una sesión larga abierta.
        db.RefreshTokens.Add(new RefreshToken { UsuarioId = carlos.UsuarioId, TiendaId = _bd.TiendaA, TokenHash = "abc", CreadoEn = _reloj.GetUtcNow(), ExpiraEn = _reloj.GetUtcNow().AddDays(30) });
        await db.SaveChangesAsync();
        var antes = _reloj.GetUtcNow().AddSeconds(-1);
        _reloj.Advance(TimeSpan.FromSeconds(5));

        var ascendido = await equipo.ActualizarAsync(carlos.Id, new(encargado.Id, true), default);
        Assert.Equal("Encargado", ascendido.Rol);
        Assert.True(_revocadas.Invalida(carlos.UsuarioId, antes));
        Assert.Null((await db.RefreshTokens.AsNoTracking().SingleAsync()).RevocadoEn); // sigue pudiendo renovar (con los permisos nuevos)

        var baja = await equipo.ActualizarAsync(carlos.Id, new(encargado.Id, false), default);
        Assert.False(baja.Activo);
        Assert.NotNull((await db.RefreshTokens.AsNoTracking().SingleAsync()).RevocadoEn); // sin acceso: la sesión larga se cerró

        // Nadie se desactiva ni se cambia el rol a sí mismo (se quedaría sin acceso o se ascendería).
        var propia = Equipo(db, _bd.TiendaA, new Persona(carlos.UsuarioId, Permisos.Todos.ToArray()));
        await Assert.ThrowsAsync<OperacionNoPermitidaException>(() => propia.ActualizarAsync(carlos.Id, new(encargado.Id, false), default));
        await Assert.ThrowsAsync<OperacionNoPermitidaException>(() => propia.ActualizarAsync(carlos.Id, new(vendedor.Id, true), default));
    }

    [Fact]
    public async Task Nadie_administra_a_alguien_con_mas_permisos_ni_le_da_un_rol_mas_grande()
    {
        var roles = await RolesDe(_bd.TiendaA);
        await using var db = _bd.Contexto(_bd.TiendaA);
        var encargado = roles.Single(r => r.Nombre == "Encargado");
        var vendedor = roles.Single(r => r.Nombre == "Vendedor");
        var ana = await Equipo(db, _bd.TiendaA).CrearAsync(Alta(encargado.Id, "ana@ejemplo.com"), default);
        var luis = await Equipo(db, _bd.TiendaA).CrearAsync(Alta(vendedor.Id, "luis@ejemplo.com"), default);

        // Un gerente con equipo.gestionar pero solo los permisos del vendedor + equipo.
        var gerente = new Persona(Guid.CreateVersion7(), [.. vendedor.Permisos, Permisos.EquipoGestionar]);
        var servicio = Equipo(db, _bd.TiendaA, gerente);

        await Assert.ThrowsAsync<SinPermisoException>(() => servicio.ActualizarAsync(ana.Id, new(vendedor.Id, true), default)); // ana tiene más que él
        await Assert.ThrowsAsync<SinPermisoException>(() => servicio.RestablecerPasswordAsync(ana.Id, new("nueva-clave-1"), default));
        await Assert.ThrowsAsync<SinPermisoException>(() => servicio.ActualizarAsync(luis.Id, new(encargado.Id, true), default)); // no puede darle un rol más grande
        var ok = await servicio.ActualizarAsync(luis.Id, new(vendedor.Id, false), default); // sí puede administrar a quien tiene lo mismo o menos
        Assert.False(ok.Activo);
    }

    [Fact]
    public async Task Restablecer_la_contrasena_pide_cambiarla_y_cierra_las_sesiones()
    {
        var vendedor = await RolVendedor(_bd.TiendaA);
        await using var db = _bd.Contexto(_bd.TiendaA);
        var equipo = Equipo(db, _bd.TiendaA, Duena());
        var carlos = await equipo.CrearAsync(Alta(vendedor.Id), default);
        await Auth(_bd.Contexto(null)).CambiarPasswordAsync(carlos.UsuarioId, null, new("provisoria1", "la-mia-propia-1"), default);
        db.ChangeTracker.Clear();
        Assert.False((await db.Usuarios.SingleAsync(u => u.Id == carlos.UsuarioId)).DebeCambiarPassword);

        db.RefreshTokens.Add(new RefreshToken { UsuarioId = carlos.UsuarioId, TiendaId = _bd.TiendaA, TokenHash = "xyz", CreadoEn = _reloj.GetUtcNow(), ExpiraEn = _reloj.GetUtcNow().AddDays(30) });
        await db.SaveChangesAsync();
        await equipo.RestablecerPasswordAsync(carlos.Id, new("otra-provisoria-2"), default);

        db.ChangeTracker.Clear();
        var usuario = await db.Usuarios.SingleAsync(u => u.Id == carlos.UsuarioId);
        Assert.True(usuario.DebeCambiarPassword);
        Assert.True(BCrypt.Net.BCrypt.Verify("otra-provisoria-2", usuario.PasswordHash));
        Assert.NotNull((await db.RefreshTokens.SingleAsync()).RevocadoEn);
        await Assert.ThrowsAsync<ValidacionException>(() => equipo.RestablecerPasswordAsync(carlos.Id, new("corta"), default));
    }

    private async Task<IReadOnlyList<RolDto>> RolesDe(Guid tienda)
    {
        await using var db = _bd.Contexto(tienda);
        return (await Roles(db).ListarAsync(false, default)).Roles;
    }

    // ───────────── Inicio de sesión con roles ─────────────

    private async Task<(MiembroDto Miembro, RolDto Rol)> EmpleadoAsync(Guid tienda, string rolNombre = "Vendedor")
    {
        var rol = (await RolesDe(tienda)).Single(r => r.Nombre == rolNombre);
        await using var db = _bd.Contexto(tienda);
        var miembro = await Equipo(db, tienda).CrearAsync(Alta(rol.Id), default);
        return (miembro, rol);
    }

    private static HashSet<string> PermisosDelToken(string token)
        => new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.Where(c => c.Type == JwtTokenService.ClaimPermiso).Select(c => c.Value).ToHashSet();

    [Fact]
    public async Task El_dueno_entra_con_todos_los_permisos_y_el_empleado_con_los_de_su_rol()
    {
        var (_, rol) = await EmpleadoAsync(_bd.TiendaA);

        await using var db = _bd.Contexto(null);
        var empleado = await Auth(db).LoginAsync(new("carlos@ejemplo.com", "provisoria1"), default);
        Assert.Equal(_bd.TiendaA, empleado.Sesion.Tienda.Id);
        Assert.Equal(("Vendedor", false), (empleado.Sesion.Acceso.Rol, empleado.Sesion.Acceso.EsDueno));
        Assert.True(empleado.Sesion.Usuario.DebeCambiarPassword);
        Assert.Equal(rol.Permisos.ToHashSet(), empleado.Sesion.Acceso.Permisos.ToHashSet());
        Assert.Equal(rol.Permisos.ToHashSet(), PermisosDelToken(empleado.Sesion.AccessToken)); // lo que decide la API es el token

        // La dueña (registrada en la tienda) lleva el catálogo completo.
        var registro = await Auth(_bd.Contexto(null)).RegistrarAsync(new("Luna", "luna@ejemplo.com", "secreta123", "Luna", "luna"), default);
        Assert.True(registro.Sesion.Acceso.EsDueno);
        Assert.Equal("Dueño", registro.Sesion.Acceso.Rol);
        Assert.Equal(Permisos.Todos.ToHashSet(), PermisosDelToken(registro.Sesion.AccessToken));
    }

    [Fact]
    public async Task Un_empleado_desactivado_o_con_el_rol_desactivado_no_entra_a_la_tienda()
    {
        var (carlos, rol) = await EmpleadoAsync(_bd.TiendaA);
        await using (var db = _bd.Contexto(_bd.TiendaA))
            await Equipo(db, _bd.TiendaA).ActualizarAsync(carlos.Id, new(rol.Id, false), default);

        await using var sinAcceso = _bd.Contexto(null);
        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => Auth(sinAcceso).LoginAsync(new("carlos@ejemplo.com", "provisoria1"), default));

        // Reactivado entra; si después desactivan SU ROL (vía base), vuelve a quedar afuera.
        await using (var db = _bd.Contexto(_bd.TiendaA))
            await Equipo(db, _bd.TiendaA).ActualizarAsync(carlos.Id, new(rol.Id, true), default);
        await using (var db = _bd.Contexto(null))
            Assert.NotNull((await Auth(db).LoginAsync(new("carlos@ejemplo.com", "provisoria1"), default)).Sesion.AccessToken);
        await using (var db = _bd.Contexto(_bd.TiendaA))
        {
            var r = await db.Roles.SingleAsync(x => x.Id == rol.Id);
            r.Activo = false;
            await db.SaveChangesAsync();
        }
        await using var final = _bd.Contexto(null);
        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => Auth(final).LoginAsync(new("carlos@ejemplo.com", "provisoria1"), default));
    }

    [Fact]
    public async Task Al_renovar_la_sesion_llegan_los_permisos_de_ahora_o_se_cierra()
    {
        var (carlos, vendedor) = await EmpleadoAsync(_bd.TiendaA);
        var encargado = (await RolesDe(_bd.TiendaA)).Single(r => r.Nombre == "Encargado");
        var sesion = await Auth(_bd.Contexto(null)).LoginAsync(new("carlos@ejemplo.com", "provisoria1"), default);
        Assert.DoesNotContain(Permisos.CostosVer, sesion.Sesion.Acceso.Permisos);

        await using (var db = _bd.Contexto(_bd.TiendaA))
            await Equipo(db, _bd.TiendaA).ActualizarAsync(carlos.Id, new(encargado.Id, true), default);
        var renovada = await Auth(_bd.Contexto(null)).RenovarAsync(sesion.RefreshToken, default);
        Assert.Equal("Encargado", renovada.Sesion.Acceso.Rol);
        Assert.Contains(Permisos.CostosVer, PermisosDelToken(renovada.Sesion.AccessToken));

        await using (var db = _bd.Contexto(_bd.TiendaA))
            await Equipo(db, _bd.TiendaA).ActualizarAsync(carlos.Id, new(encargado.Id, false), default);
        // La baja ya cerró la sesión larga: renovar falla.
        await Assert.ThrowsAsync<SesionInvalidaException>(() => Auth(_bd.Contexto(null)).RenovarAsync(renovada.RefreshToken, default));
        Assert.NotEqual(vendedor.Id, encargado.Id);
    }

    [Fact]
    public async Task Un_empleado_de_una_tienda_no_puede_entrar_con_un_refresh_de_otra()
    {
        await EmpleadoAsync(_bd.TiendaA);
        var sesion = await Auth(_bd.Contexto(null)).LoginAsync(new("carlos@ejemplo.com", "provisoria1"), default);

        // Alguien le cambia la tienda a su refresh token: el acceso se vuelve a comprobar contra ESA tienda y no existe.
        await using (var db = _bd.Contexto(null))
        {
            var token = await db.RefreshTokens.SingleAsync();
            token.TiendaId = _bd.TiendaB;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<SesionInvalidaException>(() => Auth(_bd.Contexto(null)).RenovarAsync(sesion.RefreshToken, default));
    }

    // ───────────── Cambiar la contraseña ─────────────

    [Fact]
    public async Task Cambiar_la_contrasena_pide_la_actual_cierra_las_otras_sesiones_y_apaga_el_aviso()
    {
        await EmpleadoAsync(_bd.TiendaA);
        var celular = await Auth(_bd.Contexto(null)).LoginAsync(new("carlos@ejemplo.com", "provisoria1"), default);
        var compu = await Auth(_bd.Contexto(null)).LoginAsync(new("carlos@ejemplo.com", "provisoria1"), default);
        var usuarioId = celular.Sesion.Usuario.Id;

        await Assert.ThrowsAsync<ValidacionException>(() =>
            Auth(_bd.Contexto(null)).CambiarPasswordAsync(usuarioId, celular.RefreshToken, new("incorrecta1", "la-mia-propia-1"), default));
        await Assert.ThrowsAsync<ValidacionException>(() =>
            Auth(_bd.Contexto(null)).CambiarPasswordAsync(usuarioId, celular.RefreshToken, new("provisoria1", "corta"), default));
        await Assert.ThrowsAsync<ValidacionException>(() =>
            Auth(_bd.Contexto(null)).CambiarPasswordAsync(usuarioId, celular.RefreshToken, new("provisoria1", "provisoria1"), default)); // la misma

        await Auth(_bd.Contexto(null)).CambiarPasswordAsync(usuarioId, celular.RefreshToken, new("provisoria1", "la-mia-propia-1"), default);

        await using var db = _bd.Contexto(null);
        Assert.False((await db.Usuarios.SingleAsync(u => u.Id == usuarioId)).DebeCambiarPassword);
        // La sesión desde donde la cambió sigue; la otra (quien conociera la clave anterior) se cerró.
        Assert.NotNull((await Auth(db).RenovarAsync(celular.RefreshToken, default)).Sesion.AccessToken);
        await Assert.ThrowsAsync<SesionInvalidaException>(() => Auth(_bd.Contexto(null)).RenovarAsync(compu.RefreshToken, default));
        await Assert.ThrowsAsync<CredencialesInvalidasException>(() => Auth(_bd.Contexto(null)).LoginAsync(new("carlos@ejemplo.com", "provisoria1"), default));
        Assert.NotNull((await Auth(_bd.Contexto(null)).LoginAsync(new("carlos@ejemplo.com", "la-mia-propia-1"), default)).Sesion.AccessToken);
    }

    // ───────────── Cierre inmediato de sesiones ─────────────

    [Fact]
    public void Un_token_emitido_antes_de_revocar_deja_de_servir_y_uno_posterior_si()
    {
        var persona = Guid.CreateVersion7();
        var emitido = _reloj.GetUtcNow();
        Assert.False(_revocadas.Invalida(persona, emitido)); // sin revocación no invalida nada

        _reloj.Advance(TimeSpan.FromSeconds(30));
        _revocadas.Revocar(persona);

        Assert.True(_revocadas.Invalida(persona, emitido));
        Assert.False(_revocadas.Invalida(persona, _reloj.GetUtcNow())); // el emitido ahora mismo, al renovar, sirve
        Assert.False(_revocadas.Invalida(Guid.CreateVersion7(), emitido)); // otra persona, no
    }

    // ───────────── Ventas: quién las registró ─────────────

    private (VentaService Ventas, StockService Stock, ProductoService Productos) Ventas(HermesDbContext db, Guid tienda, IUsuarioActual? quien)
    {
        var almacen = new AlmacenEnBaseDeDatos(db, _reloj);
        var sesion = new BaseDePrueba.TiendaFija(tienda);
        var operaciones = new OperacionesStock(db, _reloj);
        return (new VentaService(db, operaciones, almacen, sesion, _reloj, quien), new StockService(db, operaciones, almacen, sesion),
            new ProductoService(db, new ImagenService(db, almacen, sesion, _reloj), almacen, sesion, _reloj, null, quien));
    }

    [Fact]
    public async Task Cada_venta_guarda_quien_la_registro_y_quien_no_ve_todo_ve_solo_las_suyas()
    {
        var ana = Guid.CreateVersion7();
        var luis = Guid.CreateVersion7();
        await using (var alta = _bd.Contexto(null))
        {
            alta.Usuarios.AddRange(new Usuario { Id = ana, Nombre = "Ana", Email = "ana@x.test", PasswordHash = "x" }, new Usuario { Id = luis, Nombre = "Luis", Email = "luis@x.test", PasswordHash = "x" });
            await alta.SaveChangesAsync();
        }
        Guid variante;
        await using (var db = _bd.Contexto(_bd.TiendaA))
        {
            var (_, stock, productos) = Ventas(db, _bd.TiendaA, null);
            var p = await productos.CrearAsync(new("Cartera", null, 180, 110, null, true, null, null, null), default);
            await stock.IngresarAsync(new(p.Id, [new(p.Variantes[0].Id, 20)]), default);
            variante = p.Variantes[0].Id;
        }

        var vendedora = new Persona(ana, Permisos.VentasRegistrar);
        var vendedor = new Persona(luis, Permisos.VentasRegistrar);
        var dueña = new Persona(Guid.CreateVersion7(), Permisos.VentasVer, Permisos.InicioVer, Permisos.CostosVer);
        await using (var db = _bd.Contexto(_bd.TiendaA)) await Ventas(db, _bd.TiendaA, vendedora).Ventas.RegistrarAsync(new(Guid.NewGuid(), [new(variante, 2)], MetodoPago.Efectivo), default);
        await using (var db = _bd.Contexto(_bd.TiendaA)) await Ventas(db, _bd.TiendaA, vendedora).Ventas.RegistrarAsync(new(Guid.NewGuid(), [new(variante, 1)], MetodoPago.Qr), default);
        await using (var db = _bd.Contexto(_bd.TiendaA)) await Ventas(db, _bd.TiendaA, vendedor).Ventas.RegistrarAsync(new(Guid.NewGuid(), [new(variante, 3)], MetodoPago.Efectivo), default);

        await using var consulta = _bd.Contexto(_bd.TiendaA);
        // La dueña (ventas.ver) ve las tres, con el nombre de quien las registró.
        var todas = await Ventas(consulta, _bd.TiendaA, dueña).Ventas.ListarAsync(new(), default);
        Assert.Equal(3, todas.Items.Count);
        Assert.Equal(["Ana", "Ana", "Luis"], todas.Items.Select(v => v.Vendedor).Order());
        // Ana ve SOLO las suyas, en el listado, el resumen y el detalle.
        var deAna = Ventas(consulta, _bd.TiendaA, vendedora).Ventas;
        var suyas = await deAna.ListarAsync(new(), default);
        Assert.Equal(2, suyas.Items.Count);
        Assert.All(suyas.Items, v => Assert.Equal("Ana", v.Vendedor));
        Assert.Equal(3, (await deAna.ResumenAsync(new(), default)).Unidades);
        var deLuis = await Ventas(consulta, _bd.TiendaA, dueña).Ventas.ListarAsync(new(), default);
        var ventaDeLuis = deLuis.Items.Single(v => v.Vendedor == "Luis");
        await Assert.ThrowsAsync<VentaNoEncontradaException>(() => deAna.ObtenerAsync(ventaDeLuis.Id, default));
        await Assert.ThrowsAsync<VentaNoEncontradaException>(() => deAna.AnularAsync(ventaDeLuis.Id, default));

        // El detalle por vendedor: de más a menos.
        var porVendedor = await Ventas(consulta, _bd.TiendaA, dueña).Ventas.PorVendedorAsync(new(), default);
        Assert.Equal([("Ana", 2, 3, 540m), ("Luis", 1, 3, 540m)], porVendedor.Select(v => (v.Vendedor, v.Ventas, v.Unidades, v.Total)).ToList()); // mismo total: por nombre
        Assert.Single(await deAna.PorVendedorAsync(new(), default)); // quien no ve todo solo aparece él
    }

    [Fact]
    public async Task Quien_no_puede_editar_costos_no_los_cambia_al_guardar_un_producto()
    {
        var sinCostos = new Persona(Guid.CreateVersion7(), Permisos.ProductosCrear, Permisos.ProductosEditar);
        var conCostos = new Persona(Guid.CreateVersion7(), Permisos.ProductosCrear, Permisos.ProductosEditar, Permisos.CostosEditar);
        await using var db = _bd.Contexto(_bd.TiendaA);

        // Crear sin permiso: queda sin costo (el que mande el formulario se ignora).
        var nuevo = await Ventas(db, _bd.TiendaA, sinCostos).Productos.CrearAsync(new("Taza", null, 15, 8.75m, null, true, null, null, null), default);
        Assert.Equal(0m, nuevo.Costo);

        // Con permiso, sí lo carga; y quien no puede editarlos lo conserva al editar el producto.
        var cargado = await Ventas(db, _bd.TiendaA, conCostos).Productos.ActualizarAsync(nuevo.Id, new("Taza", null, 15, 8.75m, null, true, null, null, null), default);
        Assert.Equal(8.75m, cargado.Costo);
        var editado = await Ventas(db, _bd.TiendaA, sinCostos).Productos.ActualizarAsync(nuevo.Id, new("Taza grande", null, 20, 1m, null, true, null, null, null), default);
        Assert.Equal(("Taza grande", 20m, 8.75m), (editado.Nombre, editado.Precio, editado.Costo));
    }
}
