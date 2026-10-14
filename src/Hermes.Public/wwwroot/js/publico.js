// La tienda pública funciona sin JavaScript (el pedido sale con la primera variante y cantidad 1). Esto solo lo mejora:
// cambiar la foto grande, la cantidad y la variante, y rearmar el mensaje de WhatsApp con lo elegido.
(function () {
  'use strict';

  var foto = document.getElementById('foto-principal');
  document.querySelectorAll('.miniaturas button').forEach(function (boton) {
    boton.addEventListener('click', function () {
      if (foto) foto.src = boton.getAttribute('data-foto');
    });
  });

  var formulario = document.getElementById('pedido');
  var enlace = document.getElementById('pedir');
  if (!formulario || !enlace) return;

  var salida = document.getElementById('cantidad');
  var cantidad = 1;

  // "Bs 180" si es redondo, "Bs 58,50" si no (igual que el servidor: Formato.Bs).
  function bs(monto) {
    return 'Bs ' + (Number.isInteger(monto) ? String(monto) : monto.toFixed(2).replace('.', ','));
  }

  // Igual que Formato.MensajeDePedido en el servidor.
  function armar() {
    var d = formulario.dataset;
    var elegida = formulario.querySelector('input[name="variante"]:checked');
    var nombre = elegida ? elegida.getAttribute('data-nombre') : '';
    var detalle = nombre ? ' (' + nombre + ')' : '';
    var precio = d.precio ? parseFloat(d.precio) : null;
    var total = precio === null ? '' : ' — ' + bs(precio * cantidad);
    var mensaje = 'Hola ' + d.tienda + '! Quiero pedir: ' + cantidad + ' × ' + d.producto + detalle + total + '. Lo vi en ' + d.direccion;
    enlace.href = 'https://wa.me/' + d.wa.replace(/\D/g, '') + '?text=' + encodeURIComponent(mensaje);
    salida.textContent = String(cantidad);
  }

  document.getElementById('menos').addEventListener('click', function () {
    cantidad = Math.max(1, cantidad - 1);
    armar();
  });
  document.getElementById('mas').addEventListener('click', function () {
    cantidad = Math.min(99, cantidad + 1);
    armar();
  });
  formulario.addEventListener('change', armar);
  armar();
})();
