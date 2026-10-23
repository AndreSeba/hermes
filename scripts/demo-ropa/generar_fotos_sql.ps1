# Arma fotos.sql con las fotos reales (scripts/demo-ropa/img-reales/pNN_K_<tamaño>.jpg) para correr crear_tienda_ropa.sql con
# `-v reales=1 -v fotos=<ruta>`. Es solo una tabla temporal t_foto con los bytes en base64: funciona contra cualquier Postgres.
param([string]$Entrada = (Join-Path $PSScriptRoot 'img-reales'), [string]$Salida = (Join-Path $PSScriptRoot 'fotos.sql'))
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('create temp table t_foto (nombre text primary key, datos bytea);')
foreach ($f in Get-ChildItem $Entrada -Filter 'p??_?_*.jpg') {
  [void]$sb.AppendLine(("insert into t_foto values ('{0}', decode('{1}', 'base64'));" -f $f.Name, [Convert]::ToBase64String([IO.File]::ReadAllBytes($f.FullName))))
}
[IO.File]::WriteAllText($Salida, $sb.ToString(), (New-Object System.Text.UTF8Encoding $false))
"{0} fotos, {1:n1} MB -> {2}" -f (Get-ChildItem $Entrada -Filter 'p??_?_*.jpg').Count, ((Get-Item $Salida).Length / 1MB), $Salida
