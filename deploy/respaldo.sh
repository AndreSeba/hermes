#!/bin/sh
# Un respaldo por día de la base (formato custom de pg_dump: se restaura con pg_restore). Corre para siempre dentro del
# contenedor "respaldo". Restaurar: ver docs/DESPLIEGUE.md.
set -eu

while true; do
  archivo="/respaldos/hermes-$(date -u +%Y%m%d-%H%M).dump"
  if pg_dump --format=custom --compress=9 --file="$archivo.tmp"; then
    mv "$archivo.tmp" "$archivo"
    echo "Respaldo listo: $archivo ($(du -h "$archivo" | cut -f1))"
  else
    rm -f "$archivo.tmp"
    echo "FALLÓ el respaldo de $(date -u)" >&2
  fi
  # Se borran los de más de DIAS_RESPALDO días.
  find /respaldos -name 'hermes-*.dump' -mtime +"${DIAS_RESPALDO:-14}" -delete
  sleep 86400
done
