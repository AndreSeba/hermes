// La tienda pública funciona sin JavaScript (el pedido sale por WhatsApp con la primera variante y cantidad 1). Esto la
// mejora: cambiar la foto grande, la cantidad y la variante, y el CARRITO (vive en este navegador, una lista por tienda).
// Todo lo que viene de la tienda (nombres) se escribe con textContent, nunca como HTML.
(function () {
  'use strict';

  var MAXIMO_UNIDADES = 99; // Pedido.MaximoCantidad
  var MAXIMO_LINEAS = 30;   // Pedido.MaximoLineas

  // "Bs 180" si es redondo, "Bs 58,50" si no (igual que el servidor: Formato.Bs).
  function bs(monto) {
    return 'Bs ' + (Number.isInteger(monto) ? String(monto) : monto.toFixed(2).replace('.', ','));
  }

  // ───────────── El carrito en el navegador ─────────────
  // [{ v: varianteId, p: productoId, n: nombre, o: opción ("café · M" o ""), u: unidades }]. localStorage puede no
  // existir o fallar (modo privado, almacenamiento lleno): entonces el carrito simplemente no se guarda.
  function clave(slug) { return 'carrito:' + slug; }
  function leer(slug) {
    try {
      var datos = JSON.parse(localStorage.getItem(clave(slug)) || '[]');
      return Array.isArray(datos) ? datos.filter(function (x) { return x && typeof x.v === 'string' && x.u > 0; }) : [];
    } catch (e) { return []; }
  }
  function guardar(slug, items) {
    try { localStorage.setItem(clave(slug), JSON.stringify(items)); } catch (e) { /* sin almacenamiento: no se guarda */ }
    contar();
  }
  function unidades(items) { return items.reduce(function (s, x) { return s + x.u; }, 0); }

  // El enlace "Carrito" de la cabecera: se muestra con cuántas unidades hay.
  function contar() {
    document.querySelectorAll('[data-carrito]').forEach(function (a) {
      var n = unidades(leer(a.getAttribute('data-carrito')));
      a.hidden = false;
      var c = a.querySelector('[data-contador]');
      if (c) c.textContent = n > 0 ? '(' + n + ')' : '';
    });
  }
  contar();

  // ───────────── Página del producto ─────────────
  var foto = document.getElementById('foto-principal');
  document.querySelectorAll('.miniaturas button').forEach(function (boton) {
    boton.addEventListener('click', function () {
      if (foto) foto.src = boton.getAttribute('data-foto');
    });
  });

  var formulario = document.getElementById('pedido');
  var enlace = document.getElementById('pedir');
  if (formulario && enlace) producto(formulario, enlace);

  function producto(formulario, enlace) {
    var salida = document.getElementById('cantidad');
    var agregar = document.getElementById('agregar');
    var agregado = document.getElementById('agregado');
    var d = formulario.dataset;
    var cantidad = 1;

    function elegida() {
      var radio = formulario.querySelector('input[name="variante"]:checked');
      return radio ? { id: radio.value, nombre: radio.getAttribute('data-nombre') || '' } : (d.varianteUnica ? { id: d.varianteUnica, nombre: '' } : null);
    }

    // Igual que Formato.MensajeDePedido en el servidor.
    function armar() {
      var e = elegida();
      var detalle = e && e.nombre ? ' (' + e.nombre + ')' : '';
      var precio = d.precio ? parseFloat(d.precio) : null;
      var total = precio === null ? '' : ' — ' + bs(precio * cantidad);
      var mensaje = 'Hola ' + d.tienda + '! Quiero pedir: ' + cantidad + ' × ' + d.producto + detalle + total + '. Lo vi en ' + d.direccion;
      enlace.href = 'https://wa.me/' + d.wa.replace(/\D/g, '') + '?text=' + encodeURIComponent(mensaje);
      salida.textContent = String(cantidad);
    }

    document.getElementById('menos').addEventListener('click', function () { cantidad = Math.max(1, cantidad - 1); armar(); });
    document.getElementById('mas').addEventListener('click', function () { cantidad = Math.min(MAXIMO_UNIDADES, cantidad + 1); armar(); });
    formulario.addEventListener('change', function () { armar(); if (agregado) agregado.hidden = true; });
    armar();

    // Con carrito: el botón principal es "Agregar al carrito" y WhatsApp directo queda como alternativa.
    if (!agregar || !d.slug) return;
    agregar.hidden = false;
    enlace.classList.add('boton-secundario');
    enlace.textContent = 'Pedir solo este por WhatsApp';
    var nota = document.getElementById('nota-pedir');
    if (nota) nota.hidden = true;

    agregar.addEventListener('click', function () {
      var e = elegida();
      if (!e) return;
      var items = leer(d.slug);
      var existente = items.find(function (x) { return x.v === e.id; });
      if (existente) existente.u = Math.min(MAXIMO_UNIDADES, existente.u + cantidad);
      else if (items.length >= MAXIMO_LINEAS) { mostrar('Tu carrito ya tiene ' + MAXIMO_LINEAS + ' productos distintos: mandá ese pedido primero.'); return; }
      else items.push({ v: e.id, p: d.productoId, n: d.producto, o: e.nombre, u: cantidad });
      guardar(d.slug, items);
      mostrar(null);
    });

    function mostrar(error) {
      if (!agregado) return;
      agregado.hidden = false;
      agregado.classList.toggle('agregado-error', !!error);
      agregado.textContent = '';
      if (error) { agregado.textContent = error; return; }
      agregado.appendChild(document.createTextNode('✓ Agregado al carrito. '));
      var a = document.createElement('a');
      a.href = '/' + d.slug + '/carrito';
      a.textContent = 'Ver carrito';
      agregado.appendChild(a);
    }
  }

  // ───────────── Página del carrito ─────────────
  var carrito = document.getElementById('carrito');
  if (carrito && carrito.dataset.recibe === 'si') paginaCarrito(carrito);

  function paginaCarrito(raiz) {
    var slug = raiz.dataset.slug;
    var conPrecios = raiz.dataset.precios === 'si';
    var lista = document.getElementById('carrito-lista');
    var vacio = document.getElementById('carrito-vacio');
    var cargando = document.getElementById('carrito-cargando');
    var aviso = document.getElementById('carrito-aviso');
    var totalEl = document.getElementById('carrito-total');
    var checkout = document.getElementById('checkout');
    var errorEl = document.getElementById('checkout-error');
    var datosEnvio = document.getElementById('datos-envio');
    var enviar = checkout.querySelector('button[type="submit"]');
    var estado = {}; // varianteId → cómo está hoy (precio, disponible, foto)
    var clavePedido = nuevaClave(); // la misma en los reintentos de este carrito: no duplica el pedido
    var enviando = false;

    function nuevaClave() {
      if (window.crypto && crypto.randomUUID) return crypto.randomUUID();
      return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
        var r = (crypto.getRandomValues(new Uint8Array(1))[0] & 15);
        return (c === 'x' ? r : (r & 3) | 8).toString(16);
      });
    }

    function postear(ruta, cuerpo) {
      return fetch('/' + slug + ruta, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(cuerpo),
        cache: 'no-store',
      }).then(function (r) {
        return r.json().catch(function () { return {}; }).then(function (datos) { return { ok: r.ok, estado: r.status, datos: datos }; });
      });
    }

    function revisar() {
      var items = leer(slug);
      if (items.length === 0) return pintar();
      postear('/carrito/revisar', { variantes: items.map(function (x) { return x.v; }) })
        .then(function (r) {
          if (!r.ok) throw new Error();
          estado = {};
          r.datos.articulos.forEach(function (a) { estado[a.varianteId] = a; });
          // Lo que ya no está a la venta se saca, avisando.
          var fuera = items.filter(function (x) { return !estado[x.v]; });
          if (fuera.length > 0) {
            guardar(slug, items.filter(function (x) { return estado[x.v]; }));
            avisar('Sacamos del carrito lo que ya no está a la venta: ' + fuera.map(function (x) { return x.n; }).join(', ') + '.');
          }
          pintar();
        })
        .catch(function () {
          cargando.hidden = true;
          avisar('No pudimos cargar tu carrito. Revisá tu conexión y recargá la página.');
        });
    }

    function avisar(texto) {
      aviso.hidden = !texto;
      aviso.textContent = texto || '';
    }

    function pintar() {
      var items = leer(slug);
      cargando.hidden = true;
      lista.textContent = '';
      vacio.hidden = items.length > 0;
      checkout.hidden = items.length === 0;
      totalEl.hidden = true;
      if (items.length === 0) return;

      var total = 0;
      var hayAgotado = false;
      items.forEach(function (x) {
        var hoy = estado[x.v] || {};
        var li = document.createElement('li');
        li.className = 'linea' + (hoy.disponible === false ? ' linea-agotada' : '');

        var img = document.createElement(hoy.foto ? 'img' : 'span');
        img.className = 'linea-foto';
        if (hoy.foto) { img.src = hoy.foto.miniatura; img.alt = ''; img.width = 64; img.height = 64; }
        li.appendChild(img);

        var datos = document.createElement('div');
        datos.className = 'linea-datos';
        var nombre = document.createElement('a');
        nombre.className = 'linea-nombre';
        nombre.href = '/' + slug + '/p/' + (hoy.productoId || x.p);
        nombre.textContent = hoy.nombre || x.n;
        datos.appendChild(nombre);
        var opcion = hoy.variante !== undefined ? hoy.variante : x.o;
        if (opcion) {
          var o = document.createElement('span');
          o.className = 'linea-opcion';
          o.textContent = opcion;
          datos.appendChild(o);
        }
        if (hoy.disponible === false) {
          hayAgotado = true;
          var ag = document.createElement('span');
          ag.className = 'linea-agotado';
          ag.textContent = 'Se agotó: sacalo para poder enviar el pedido';
          datos.appendChild(ag);
        }
        if (typeof hoy.precio === 'number') {
          total += hoy.precio * x.u;
          var pr = document.createElement('span');
          pr.className = 'linea-precio';
          pr.textContent = bs(hoy.precio * x.u) + (x.u > 1 ? ' (' + bs(hoy.precio) + ' c/u)' : '');
          datos.appendChild(pr);
        }

        var acciones = document.createElement('div');
        acciones.className = 'linea-acciones';
        var cant = document.createElement('div');
        cant.className = 'cantidad cantidad-chica';
        cant.setAttribute('role', 'group');
        cant.setAttribute('aria-label', 'Cantidad de ' + x.n);
        cant.appendChild(boton('−', 'Una menos', function () { cambiar(x.v, -1); }));
        var out = document.createElement('output');
        out.textContent = String(x.u);
        cant.appendChild(out);
        cant.appendChild(boton('+', 'Una más', function () { cambiar(x.v, 1); }));
        acciones.appendChild(cant);
        var quitar = boton('Quitar', 'Quitar ' + x.n + ' del carrito', function () { sacar(x.v); });
        quitar.className = 'quitar';
        acciones.appendChild(quitar);
        datos.appendChild(acciones);

        li.appendChild(datos);
        lista.appendChild(li);
      });

      if (conPrecios) {
        totalEl.hidden = false;
        totalEl.textContent = 'Total: ' + bs(Math.round(total * 100) / 100);
      }
      enviar.disabled = hayAgotado || enviando;
    }

    function boton(texto, etiqueta, accion) {
      var b = document.createElement('button');
      b.type = 'button';
      b.textContent = texto;
      b.setAttribute('aria-label', etiqueta);
      b.addEventListener('click', accion);
      return b;
    }

    function cambiar(id, delta) {
      var items = leer(slug);
      var x = items.find(function (i) { return i.v === id; });
      if (!x) return;
      x.u = Math.max(1, Math.min(MAXIMO_UNIDADES, x.u + delta));
      guardar(slug, items);
      pintar();
    }

    function sacar(id) {
      guardar(slug, leer(slug).filter(function (i) { return i.v !== id; }));
      pintar();
    }

    checkout.addEventListener('change', function () {
      var envio = checkout.querySelector('input[name="entrega"]:checked').value === 'Envio';
      datosEnvio.hidden = !envio;
    });

    function error(texto) {
      errorEl.hidden = !texto;
      errorEl.textContent = texto || '';
    }

    checkout.addEventListener('submit', function (ev) {
      ev.preventDefault();
      if (enviando) return;
      var f = checkout.elements;
      var entrega = checkout.querySelector('input[name="entrega"]:checked').value;
      var celular = f.celular.value.replace(/[^\d]/g, '').replace(/^591(?=\d{8}$)/, '');
      // Las mismas reglas que el servidor (que igual valida): para avisar antes de mandar.
      if (!f.nombre.value.trim()) { error('Escribí tu nombre.'); f.nombre.focus(); return; }
      if (!/^[67]\d{7}$/.test(celular)) { error('Escribí tu celular de Bolivia: 8 dígitos que empiecen con 6 o 7.'); f.celular.focus(); return; }
      if (entrega === 'Envio' && !f.direccion.value.trim()) { error('Escribí la dirección de entrega.'); f.direccion.focus(); return; }
      var items = leer(slug);
      if (items.length === 0) { pintar(); return; }

      error(null);
      enviando = true;
      enviar.disabled = true;
      enviar.textContent = 'Enviando…';
      postear('/pedido', {
        claveIdempotencia: clavePedido,
        articulos: items.map(function (x) { return { varianteId: x.v, unidades: x.u }; }),
        nombre: f.nombre.value.trim(),
        celular: f.celular.value,
        entrega: entrega,
        direccion: entrega === 'Envio' ? f.direccion.value.trim() : null,
        referencia: entrega === 'Envio' ? (f.referencia.value.trim() || null) : null,
        nota: f.nota.value.trim() || null,
        sitio: f.sitio.value,
      }).then(function (r) {
        if (r.ok) return listo(r.datos);
        error((r.datos && r.datos.detail) || (r.estado === 429 ? 'Mandaste muchos pedidos seguidos. Esperá unos minutos.' : 'No pudimos enviar tu pedido. Probá de nuevo.'));
        // Si algo cambió (se agotó, ya no está), se vuelve a revisar el carrito para mostrar qué.
        if (r.estado === 409) revisar();
      }).catch(function () {
        error('No pudimos enviar tu pedido. Revisá tu conexión y probá de nuevo: no se va a duplicar.');
      }).then(function () {
        enviando = false;
        enviar.textContent = 'Enviar pedido';
        enviar.disabled = false;
      });
    });

    function listo(datos) {
      guardar(slug, []);
      lista.textContent = '';
      totalEl.hidden = true;
      checkout.hidden = true;
      avisar(null);
      var enviado = document.getElementById('enviado');
      document.getElementById('enviado-titulo').textContent = '¡Pedido #' + datos.numero + ' enviado!';
      document.getElementById('enviado-wa').href = datos.whatsapp;
      enviado.hidden = false;
      enviado.scrollIntoView({ block: 'start' });
    }

    revisar();
  }
})();
