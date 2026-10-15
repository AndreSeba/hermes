"""Un Meta (Instagram) FALSO para probar M12 de punta a punta sin una app de Meta de verdad. SOLO para desarrollo.

Imita las direcciones y respuestas que documenta Meta para "Instagram API with Instagram Login":
  GET  /oauth/authorize            → redirige a redirect_uri con ?code=…&state=… (como si la dueña aceptara)
  POST /oauth/access_token         → token corto   (lo que en Meta es api.instagram.com)
  GET  /access_token               → token largo   (graph.instagram.com)
  GET  /refresh_access_token       → token renovado
  GET  /v23.0/me                   → la cuenta
  GET  /v23.0/me/media             → las publicaciones (con fotos servidas por este mismo servidor)
  GET  /foto/N.jpg                 → una foto JPEG
Y rutas para manejar la prueba: POST /_prueba/publicar {id, caption, tipo, fotos}, POST /_prueba/revocar, GET /_prueba/pedidos.

La API tiene que arrancar con: Instagram__AppId=111222333, Instagram__AppSecret=secreto-falso,
Instagram__RedirectUri=http://localhost:5173/instagram/conectado, Instagram__ClaveCifrado=<32 bytes base64>,
Instagram__UrlAutorizacion=http://localhost:5099/oauth/authorize, Instagram__UrlApi=http://localhost:5099,
Instagram__UrlGraph=http://localhost:5099, Instagram__SoloHttps=false.
Uso: python -I scripts/meta_falso.py   (puerto 5099)
"""
import io, json, sys, threading, urllib.parse
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

APP_ID, APP_SECRET, PUERTO = "111222333", "secreto-falso", 5099
estado = {"medias": [], "tokens": set(), "pedidos": [], "revocado": False, "n": 0}
candado = threading.Lock()


def foto_jpeg(n):
    try:
        from PIL import Image
        colores = [(194, 65, 12), (11, 122, 87), (242, 163, 58), (60, 90, 200)]
        img = Image.new("RGB", (640, 640), colores[n % len(colores)])
        salida = io.BytesIO()
        img.save(salida, "JPEG", quality=85)
        return salida.getvalue()
    except ImportError:
        sys.exit("Hace falta Pillow (pip install pillow) para las fotos del Meta falso.")


class Meta(BaseHTTPRequestHandler):
    def log_message(self, *_):
        pass

    def responder(self, codigo, cuerpo=None, tipo="application/json", cabeceras=None):
        datos = cuerpo if isinstance(cuerpo, bytes) else json.dumps(cuerpo or {}).encode()
        self.send_response(codigo)
        self.send_header("Content-Type", tipo)
        self.send_header("Content-Length", str(len(datos)))
        for k, v in (cabeceras or {}).items():
            self.send_header(k, v)
        self.end_headers()
        self.wfile.write(datos)

    def error_token(self):
        self.responder(400, {"error": {"message": "Error validating access token: Session has expired", "type": "OAuthException", "code": 190}})

    def token_valido(self, q):
        t = q.get("access_token", [""])[0]
        return t in estado["tokens"] and not estado["revocado"]

    def do_GET(self):
        url = urllib.parse.urlparse(self.path)
        q = urllib.parse.parse_qs(url.query)
        with candado:
            estado["pedidos"].append(f"GET {url.path}")
        if url.path == "/oauth/authorize":
            if q.get("client_id", [""])[0] != APP_ID or "instagram_business_basic" not in q.get("scope", [""])[0]:
                return self.responder(400, {"error_message": "Invalid client or scope"})
            destino = q["redirect_uri"][0] + "?" + urllib.parse.urlencode({"code": "codigo-falso", "state": q.get("state", [""])[0]}) + "#_"
            return self.responder(302, b"", "text/plain", {"Location": destino})
        if url.path == "/access_token":
            if q.get("grant_type", [""])[0] != "ig_exchange_token" or q.get("client_secret", [""])[0] != APP_SECRET or q.get("access_token", [""])[0] != "IGQcorto":
                return self.responder(400, {"error": {"message": "Invalid parameters", "type": "OAuthException", "code": 100}})
            with candado:
                estado["revocado"] = False
                estado["n"] += 1
                token = f"IGQlargo-{estado['n']}"
                estado["tokens"].add(token)
            return self.responder(200, {"access_token": token, "token_type": "bearer", "expires_in": 5183944})
        if url.path == "/refresh_access_token":
            if not self.token_valido(q):
                return self.error_token()
            with candado:
                estado["n"] += 1
                token = f"IGQrenovado-{estado['n']}"
                estado["tokens"].add(token)
            return self.responder(200, {"access_token": token, "token_type": "bearer", "expires_in": 5183944})
        if url.path == "/v23.0/me":
            if not self.token_valido(q):
                return self.error_token()
            return self.responder(200, {"user_id": "17841400000000099", "username": "tienda.falsa", "id": "998877"})
        if url.path == "/v23.0/me/media":
            if not self.token_valido(q):
                return self.error_token()
            limite = int(q.get("limit", ["25"])[0])
            with candado:
                datos = sorted(estado["medias"], key=lambda m: m["timestamp"], reverse=True)[:limite]
            return self.responder(200, {"data": datos, "paging": {}})
        if url.path.startswith("/foto/"):
            n = int(url.path.split("/")[-1].split(".")[0])
            return self.responder(200, foto_jpeg(n), "image/jpeg")
        if url.path == "/_prueba/pedidos":
            with candado:
                return self.responder(200, {"pedidos": estado["pedidos"]})
        self.responder(404, {"error": {"message": "No existe", "code": 803}})

    def do_POST(self):
        url = urllib.parse.urlparse(self.path)
        largo = int(self.headers.get("Content-Length") or 0)
        cuerpo = self.rfile.read(largo).decode() if largo else ""
        with candado:
            estado["pedidos"].append(f"POST {url.path}")
        if url.path == "/oauth/access_token":
            f = urllib.parse.parse_qs(cuerpo)
            ok = (f.get("client_id", [""])[0] == APP_ID and f.get("client_secret", [""])[0] == APP_SECRET
                  and f.get("grant_type", [""])[0] == "authorization_code" and f.get("code", [""])[0] == "codigo-falso")
            if not ok:
                return self.responder(400, {"error_type": "OAuthException", "code": 400, "error_message": "Invalid authorization code"})
            return self.responder(200, {"data": [{"access_token": "IGQcorto", "user_id": "17841400000000099", "permissions": "instagram_business_basic"}]})
        if url.path == "/_prueba/publicar":
            p = json.loads(cuerpo)
            fotos = p.get("fotos", 1)
            base = f"http://localhost:{PUERTO}/foto"
            media = {"id": p["id"], "caption": p.get("caption"), "media_type": p.get("tipo", "IMAGE"), "permalink": f"https://www.instagram.com/p/{p['id']}/",
                     "timestamp": (datetime.now(timezone.utc) - timedelta(minutes=p.get("minutos_atras", 5))).strftime("%Y-%m-%dT%H:%M:%S+0000")}
            if media["media_type"] == "CAROUSEL_ALBUM":
                media["children"] = {"data": [{"media_type": "IMAGE", "media_url": f"{base}/{i}.jpg", "id": f"{p['id']}-{i}"} for i in range(fotos)]}
            elif media["media_type"] == "VIDEO":
                media["media_url"], media["thumbnail_url"] = f"{base}/video.mp4", f"{base}/9.jpg"
            else:
                media["media_url"] = f"{base}/{len(estado['medias'])}.jpg"
            with candado:
                estado["medias"].append(media)
            return self.responder(201, media)
        if url.path == "/_prueba/revocar":
            with candado:
                estado["revocado"] = True
            return self.responder(200, {})
        self.responder(404, {})


def levantar():
    servidor = ThreadingHTTPServer(("localhost", PUERTO), Meta)
    threading.Thread(target=servidor.serve_forever, daemon=True).start()
    return servidor


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    print(f"Meta falso en http://localhost:{PUERTO} (Ctrl+C para cortar)")
    ThreadingHTTPServer(("localhost", PUERTO), Meta).serve_forever()
