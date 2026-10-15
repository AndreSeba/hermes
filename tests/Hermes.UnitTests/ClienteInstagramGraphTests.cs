using System.Net;
using System.Text;
using Hermes.Application.Exceptions;
using Hermes.Infrastructure.Instagram;
using Microsoft.Extensions.Options;

namespace Hermes.UnitTests;

// El cliente real contra respuestas armadas a mano con la forma que documenta Meta: qué direcciones y parámetros pide y cómo
// lee las respuestas y los errores. (Contra Meta de verdad no se pudo probar: no hay app todavía.)
public class ClienteInstagramGraphTests
{
    private sealed class MetaFalsa(Func<HttpRequestMessage, (HttpStatusCode, string)> responder) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Pedido, string Cuerpo)> Pedidos { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Pedidos.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
            var (estado, cuerpo) = responder(request);
            return new HttpResponseMessage(estado) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };
        }
    }

    private static (ClienteInstagramGraph, MetaFalsa) Cliente(Func<HttpRequestMessage, (HttpStatusCode, string)> responder)
    {
        var meta = new MetaFalsa(responder);
        var opciones = Options.Create(new InstagramOptions { AppId = "111", AppSecret = "s3cr3t", RedirectUri = "https://panel.test/instagram/conectado", VersionGraph = "v23.0" });
        return (new ClienteInstagramGraph(new HttpClient(meta), opciones), meta);
    }

    [Fact]
    public async Task Canjear_el_codigo_hace_POST_con_los_campos_de_Meta_y_entiende_la_respuesta_envuelta()
    {
        var (cliente, meta) = Cliente(_ => (HttpStatusCode.OK, """{"data":[{"access_token":"IGQcorto","user_id":"1784","permissions":"instagram_business_basic"}]}"""));

        var t = await cliente.CanjearCodigoAsync("abc", default);

        Assert.Equal("IGQcorto", t.Token);
        var (pedido, cuerpo) = meta.Pedidos.Single();
        Assert.Equal((HttpMethod.Post, "https://api.instagram.com/oauth/access_token"), (pedido.Method, pedido.RequestUri!.ToString()));
        Assert.Equal("client_id=111&client_secret=s3cr3t&grant_type=authorization_code&redirect_uri=https%3A%2F%2Fpanel.test%2Finstagram%2Fconectado&code=abc", cuerpo);
    }

    [Fact]
    public async Task Token_largo_y_renovacion_van_a_las_direcciones_de_Meta()
    {
        var (cliente, meta) = Cliente(_ => (HttpStatusCode.OK, """{"access_token":"IGQlargo","token_type":"bearer","expires_in":5183944}"""));

        var largo = await cliente.TokenLargoAsync("IGQcorto", default);
        var renovado = await cliente.RenovarAsync("IGQlargo", default);

        Assert.Equal(("IGQlargo", 5183944), (largo.Token, largo.ExpiraEnSegundos!.Value));
        Assert.Equal("IGQlargo", renovado.Token);
        Assert.Equal("https://graph.instagram.com/access_token?grant_type=ig_exchange_token&client_secret=s3cr3t&access_token=IGQcorto", meta.Pedidos[0].Pedido.RequestUri!.ToString());
        Assert.Equal("https://graph.instagram.com/refresh_access_token?grant_type=ig_refresh_token&access_token=IGQlargo", meta.Pedidos[1].Pedido.RequestUri!.ToString());
    }

    [Fact]
    public async Task Lee_el_perfil_y_las_publicaciones_con_carrusel_y_fecha_de_Meta()
    {
        var (cliente, meta) = Cliente(r => r.RequestUri!.AbsolutePath.EndsWith("/me")
            ? (HttpStatusCode.OK, """{"user_id":"17841400000000001","username":"accesorios.mia","id":"9988"}""")
            : (HttpStatusCode.OK, """
              {"data":[
                {"id":"18000000000000001","caption":"Cartera\nBs 150","media_type":"CAROUSEL_ALBUM","permalink":"https://www.instagram.com/p/AbC/","timestamp":"2026-10-14T18:30:00+0000",
                 "children":{"data":[{"media_type":"IMAGE","media_url":"https://scontent.cdninstagram.com/1.jpg","id":"1"},{"media_type":"VIDEO","media_url":"https://x/v.mp4","thumbnail_url":"https://scontent.cdninstagram.com/v.jpg","id":"2"}]}},
                {"id":"18000000000000002","media_type":"IMAGE","media_url":"https://scontent.cdninstagram.com/2.jpg","timestamp":"2026-10-13T10:00:00+0000"}
              ],"paging":{}}
              """));

        var perfil = await cliente.PerfilAsync("TOK", default);
        var medias = await cliente.PublicacionesAsync("TOK", 25, default);

        Assert.Equal(("17841400000000001", "accesorios.mia"), (perfil.Id, perfil.Usuario));
        Assert.StartsWith("https://graph.instagram.com/v23.0/me?fields=user_id,username&access_token=TOK", meta.Pedidos[0].Pedido.RequestUri!.ToString());
        var url = meta.Pedidos[1].Pedido.RequestUri!.AbsoluteUri;
        Assert.StartsWith("https://graph.instagram.com/v23.0/me/media?fields=", url);
        Assert.Contains("children%7Bmedia_type%2Cmedia_url%2Cthumbnail_url%7D", url);
        Assert.Contains("&limit=25&access_token=TOK", url);
        Assert.Equal(2, medias.Count);
        Assert.Equal(("CAROUSEL_ALBUM", "Cartera\nBs 150", "https://www.instagram.com/p/AbC/"), (medias[0].Tipo, medias[0].Texto, medias[0].Enlace));
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 18, 30, 0, TimeSpan.Zero), medias[0].Fecha);
        Assert.Equal([("IMAGE", "https://scontent.cdninstagram.com/1.jpg", (string?)null), ("VIDEO", "https://x/v.mp4", "https://scontent.cdninstagram.com/v.jpg")],
            medias[0].Hijos.Select(h => (h.Tipo, h.UrlMedia, h.Miniatura)));
        Assert.Null(medias[1].Texto);
    }

    [Fact]
    public async Task El_error_190_de_Meta_pide_reconectar_y_otros_errores_se_explican()
    {
        var (vencido, _) = Cliente(_ => (HttpStatusCode.BadRequest, """{"error":{"message":"Error validating access token","type":"OAuthException","code":190}}"""));
        var (otro, _) = Cliente(_ => (HttpStatusCode.BadRequest, """{"error_type":"OAuthException","code":400,"error_message":"Invalid authorization code"}"""));

        var e1 = await Assert.ThrowsAsync<InstagramRechazoException>(() => vencido.PublicacionesAsync("TOK", 10, default));
        var e2 = await Assert.ThrowsAsync<InstagramRechazoException>(() => otro.CanjearCodigoAsync("x", default));

        Assert.True(e1.TokenInvalido);
        Assert.Contains("Volvé a conectar", e1.Message);
        Assert.False(e2.TokenInvalido);
        Assert.Contains("Invalid authorization code", e2.Message);
    }

    [Fact]
    public async Task Solo_baja_fotos_por_https()
    {
        var (cliente, meta) = Cliente(_ => (HttpStatusCode.OK, "x"));

        await Assert.ThrowsAsync<InstagramRechazoException>(() => cliente.DescargarAsync("http://scontent.cdninstagram.com/1.jpg", default));
        await Assert.ThrowsAsync<InstagramRechazoException>(() => cliente.DescargarAsync("file:///etc/passwd", default));
        Assert.Empty(meta.Pedidos);
    }
}
