# Dibuja las fotos de la tienda de ropa de demostración (poleras, camisas, pantalones) con GDI+: una por producto y color,
# en 3 tamaños (miniatura, catalogo, detalle), más el logo. Salen en scripts/demo-ropa/img (ignorado por git) y las lee
# crear_tienda_ropa.sql con pg_read_binary_file. Uso: powershell -File scripts/demo-ropa/generar_imagenes.ps1
Add-Type -AssemblyName System.Drawing
$salida = Join-Path $PSScriptRoot 'img'
New-Item -ItemType Directory -Force $salida | Out-Null
Get-ChildItem $salida -Filter *.png | Remove-Item -Force

$colores = @{
  'blanco' = '#F4F3EF'; 'negro' = '#26262A'; 'gris' = '#9EA0A6'; 'azul-marino' = '#223458'; 'celeste' = '#8DB6DE'
  'azul' = '#3C6AAE'; 'rojo' = '#B8303A'; 'verde-oliva' = '#5E6E3C'; 'beige' = '#CDB892'
}
# idx, tipo, colores (los mismos y en el mismo orden que el SQL)
$productos = @(
  @(1,'polera',@('blanco','negro','gris')), @(2,'oversize',@('negro','beige','verde-oliva')),
  @(3,'estampada',@('blanco','negro')), @(4,'polo',@('azul-marino','blanco','rojo')),
  @(5,'polera-ml',@('negro','gris','azul-marino')), @(6,'dry',@('negro','azul','rojo')),
  @(7,'estampada',@('rojo')), @(8,'camisa',@('celeste','blanco')), @(9,'lino',@('beige','blanco','verde-oliva')),
  @(10,'cuadros',@('rojo','azul-marino')), @(11,'camisa-jean',@('azul')), @(12,'camisa-mc',@('blanco','celeste','negro')),
  @(13,'camisa',@('blanco')), @(14,'jean',@('azul','negro')), @(15,'jean-slim',@('azul','negro','gris')),
  @(16,'vestir',@('negro','gris','azul-marino')), @(17,'cargo',@('verde-oliva','beige','negro')),
  @(18,'jogger',@('negro','gris','azul-marino')), @(19,'short',@('beige','azul','negro')), @(20,'chino',@('beige','azul-marino'))
)

function Color-De($hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }
function Mezclar($c, $f) { # f < 0 oscurece, f > 0 aclara
  $m = if ($f -lt 0) { 0 } else { 255 }; $k = [Math]::Abs($f)
  [System.Drawing.Color]::FromArgb(255, [int]($c.R + ($m - $c.R) * $k), [int]($c.G + ($m - $c.G) * $k), [int]($c.B + ($m - $c.B) * $k))
}
function Pts($lista) { $p = @(); for ($i = 0; $i -lt $lista.Count; $i += 2) { $p += New-Object System.Drawing.PointF([single]$lista[$i], [single]$lista[$i + 1]) }; ,$p }
function Espejo($lista) { # la mitad izquierda -> polígono completo (espejo en x=500)
  $izq = @(); for ($i = 0; $i -lt $lista.Count; $i += 2) { $izq += ,@($lista[$i], $lista[$i + 1]) }
  $der = @(); for ($i = $izq.Count - 1; $i -ge 0; $i--) { $der += ,@((1000 - $izq[$i][0]), $izq[$i][1]) }
  $todo = @(); foreach ($p in ($izq + $der)) { $todo += $p[0]; $todo += $p[1] }; $todo
}

function Dibujar($tipo, $hex, $archivoBase) {
  $bmp = New-Object System.Drawing.Bitmap 1000, 1000
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'
  $fondo = if (($tipo -in 'polera','oversize','estampada','polo','polera-ml','dry')) { '#EFEBE4' } elseif ($tipo -like 'camisa*' -or $tipo -in 'lino','cuadros') { '#E9EDF0' } else { '#ECE9E6' }
  $g.Clear((Color-De $fondo))
  $base = Color-De $hex
  $borde = Mezclar $base -0.28
  $sombra = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(40, 0, 0, 0))
  $g.FillEllipse($sombra, 220, 880, 560, 60)
  $relleno = New-Object System.Drawing.SolidBrush $base
  $lapiz = New-Object System.Drawing.Pen $borde, 6; $lapiz.LineJoin = 'Round'
  $claro = New-Object System.Drawing.Pen (Mezclar $base 0.22), 5
  $oscuro = New-Object System.Drawing.Pen (Mezclar $base -0.18), 4
  $esClaro = ($base.R + $base.G + $base.B) -gt 600
  $colTrazo = if ($esClaro) { Mezclar $base -0.35 } else { Mezclar $base 0.35 }
  $trazo = New-Object System.Drawing.Pen $colTrazo, 4
  $camino = New-Object System.Drawing.Drawing2D.GraphicsPath

  switch -Regex ($tipo) {
    '^(polera|oversize|estampada|dry|polera-ml|polo)$' {
      $ancho = if ($tipo -eq 'oversize') { 1.12 } else { 1.0 }
      $manga = if ($tipo -eq 'polera-ml') { @(205,250, 130,520, 70,820) } else { @(215,260, 130,400, 205,455) }
      $mitad = @(405,165) + $manga
      if ($tipo -eq 'polera-ml') { $mitad += @(150,830, 235,500) } else { $mitad += @(272,385) }
      $mitad += @(272,845)
      $tr = @(); for ($i = 0; $i -lt $mitad.Count; $i += 2) { $tr += (500 + ($mitad[$i] - 500) * $ancho); $tr += $mitad[$i + 1] }
      $pts = Pts (Espejo $tr)
      $camino.AddPolygon($pts)
      $g.FillPath($relleno, $camino); $g.DrawPath($lapiz, $camino)
      $cuello = New-Object System.Drawing.SolidBrush (Color-De $fondo)
      if ($tipo -eq 'polo') {
        $g.FillPolygon((New-Object System.Drawing.SolidBrush (Mezclar $base 0.18)), (Pts @(405,165, 500,265, 440,300, 360,200)))
        $g.FillPolygon((New-Object System.Drawing.SolidBrush (Mezclar $base 0.18)), (Pts @(595,165, 500,265, 560,300, 640,200)))
        $g.DrawLine($trazo, 500, 265, 500, 430)
        foreach ($y in 305, 365) { $g.FillEllipse((New-Object System.Drawing.SolidBrush (Mezclar $base -0.3)), 492, $y, 16, 16) }
      } else {
        $g.FillEllipse($cuello, 410, 120, 180, 120)
        $g.DrawArc((New-Object System.Drawing.Pen (Mezclar $base -0.2), 14), 410, 120, 180, 120, 0, 180)
      }
      if ($tipo -eq 'dry') {
        $g.FillPolygon((New-Object System.Drawing.SolidBrush (Mezclar $base 0.35)), (Pts @(272,420, 322,420, 322,845, 272,845)))
        $g.FillPolygon((New-Object System.Drawing.SolidBrush (Mezclar $base 0.35)), (Pts @(678,420, 728,420, 728,845, 678,845)))
      }
      if ($tipo -eq 'estampada') { # "Andes": montañas y sol
        $tinta = if ($esClaro) { Color-De '#1F3A5F' } else { Color-De '#F2A33A' }
        $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 242, 163, 58))), 560, 360, 60, 60)
        $g.FillPolygon((New-Object System.Drawing.SolidBrush $tinta), (Pts @(380,540, 450,420, 500,490, 545,430, 640,540)))
        $g.DrawString('ANDES', (New-Object System.Drawing.Font 'Arial', ([single]34), ([System.Drawing.FontStyle]::Bold)), (New-Object System.Drawing.SolidBrush $tinta), 410, 565)
      }
      if ($tipo -ne 'polo') { $g.DrawLine($oscuro, 272, 835, 728, 835) }
    }
    '^(camisa|lino|cuadros|camisa-jean|camisa-mc)$' {
      $corta = ($tipo -eq 'camisa-mc')
      $manga = if ($corta) { @(215,255, 130,420, 215,470) } else { @(215,255, 150,520, 105,800, 175,815, 245,520) }
      $mitad = @(400,160) + $manga
      if ($corta) { $mitad += @(272,400) }
      $mitad += @(272,850)
      $pts = Pts (Espejo $mitad)
      $camino.AddPolygon($pts)
      $g.FillPath($relleno, $camino)
      $estado = $g.Save()
      $g.SetClip($camino)
      if ($tipo -eq 'cuadros') {
        $p1 = New-Object System.Drawing.Pen (Mezclar $base -0.35), 14; $p2 = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(110, 255, 255, 255)), 10
        for ($x = 110; $x -lt 900; $x += 90) { $g.DrawLine($p1, $x, 100, $x, 900) }
        for ($y = 130; $y -lt 900; $y += 90) { $g.DrawLine($p2, 80, $y, 920, $y) }
      }
      if ($tipo -eq 'lino') { for ($y = 150; $y -lt 880; $y += 22) { $g.DrawLine((New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(45, 0, 0, 0)), 2), 90, $y, 910, $y + 4) } }
      $g.Restore($estado)
      $g.DrawPath($lapiz, $camino)
      $cl = New-Object System.Drawing.SolidBrush (Mezclar $base 0.2)
      $g.FillPolygon($cl, (Pts @(400,160, 500,235, 440,300, 345,195)))
      $g.FillPolygon($cl, (Pts @(600,160, 500,235, 560,300, 655,195)))
      $g.DrawPolygon($lapiz, (Pts @(400,160, 500,235, 440,300, 345,195)))
      $g.DrawPolygon($lapiz, (Pts @(600,160, 500,235, 560,300, 655,195)))
      $g.DrawLine($trazo, 500, 235, 500, 850)
      foreach ($y in 300, 400, 500, 600, 700, 790) { $g.FillEllipse((New-Object System.Drawing.SolidBrush (Mezclar $base -0.4)), 492, $y, 16, 16) }
      $g.DrawRectangle($trazo, 560, 420, 85, 95)
      if ($tipo -eq 'camisa-jean') { for ($y = 190; $y -lt 850; $y += 60) { $g.DrawLine((New-Object System.Drawing.Pen (Color-De '#C9D4E4'), 2), 272, $y, 272, $y + 20) } }
    }
    '^(jean|jean-slim|vestir|cargo|jogger|short|chino)$' {
      $largo = if ($tipo -eq 'short') { 600 } else { 900 }
      $pierna = switch ($tipo) { 'jean-slim' { 10 } 'jogger' { 40 } 'cargo' { -20 } default { 0 } }
      $mitad = @(330,150, 322,300, (300 + $pierna),$largo)
      if ($tipo -eq 'jogger') { $mitad = @(330,150, 322,300, 300,760, 322,860) }
      $mitad += @((480 + $pierna / 2), $largo, 488, 420)
      $tr = $mitad; $tr = @(); for ($i = 0; $i -lt $mitad.Count; $i += 2) { $tr += $mitad[$i]; $tr += $mitad[$i + 1] }
      $pts = Pts (Espejo $tr)
      $camino.AddPolygon($pts)
      $g.FillPath($relleno, $camino); $g.DrawPath($lapiz, $camino)
      $g.FillRectangle((New-Object System.Drawing.SolidBrush (Mezclar $base -0.12)), 328, 140, 344, 70)
      $g.DrawRectangle($lapiz, 328, 140, 344, 70)
      if ($tipo -notin 'jogger') { foreach ($x in 360, 440, 560, 640) { $g.FillRectangle((New-Object System.Drawing.SolidBrush (Mezclar $base -0.3)), $x, 130, 14, 90) } }
      $g.DrawLine($trazo, 500, 210, 500, 430)
      if ($tipo -eq 'jogger') {
        $g.DrawLine((New-Object System.Drawing.Pen (Mezclar $base 0.4), 5), 470, 210, 450, 330); $g.DrawLine((New-Object System.Drawing.Pen (Mezclar $base 0.4), 5), 530, 210, 550, 330)
        $g.FillRectangle((New-Object System.Drawing.SolidBrush (Mezclar $base -0.15)), 297, 820, 30, 40); $g.FillRectangle((New-Object System.Drawing.SolidBrush (Mezclar $base -0.15)), 673, 820, 30, 40)
      } else {
        $g.DrawArc($trazo, 335, 210, 150, 200, 90, 90); $g.DrawArc($trazo, 515, 210, 150, 200, 0, 90)
      }
      if ($tipo -eq 'cargo') {
        $g.DrawRectangle($trazo, 300, 520, 92, 120); $g.DrawRectangle($trazo, 608, 520, 92, 120)
      }
      if ($tipo -in 'jean','jean-slim') {
        $pp = New-Object System.Drawing.Pen (Color-De '#D9B45C'), 3; $pp.DashStyle = 'Dash'
        $g.DrawLine($pp, 345, 230, 330, 880); $g.DrawLine($pp, 655, 230, 670, 880); $g.DrawLine($pp, 500, 440, 500, 880)
      }
      if ($tipo -in 'vestir','chino') { $g.DrawLine($trazo, 400, 330, 392, $largo - 20); $g.DrawLine($trazo, 600, 330, 608, $largo - 20) }
    }
  }
  foreach ($t in @(@('miniatura', 200), @('catalogo', 600), @('detalle', 1000))) {
    $e = New-Object System.Drawing.Bitmap $t[1], $t[1]
    $ge = [System.Drawing.Graphics]::FromImage($e); $ge.InterpolationMode = 'HighQualityBicubic'; $ge.SmoothingMode = 'HighQuality'
    $ge.DrawImage($bmp, 0, 0, $t[1], $t[1]); $ge.Dispose()
    $e.Save((Join-Path $salida ("{0}_{1}.png" -f $archivoBase, $t[0])), [System.Drawing.Imaging.ImageFormat]::Png); $e.Dispose()
  }
  $g.Dispose(); $bmp.Dispose()
}

foreach ($p in $productos) {
  foreach ($c in $p[2]) { Dibujar $p[1] $colores[$c] ("p{0:00}_{1}" -f $p[0], $c) }
}

# Logo de la tienda: círculo con las iniciales
$bmp = New-Object System.Drawing.Bitmap 1000, 1000; $g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAlias'; $g.Clear((Color-De '#F7F5F0'))
$g.FillEllipse((New-Object System.Drawing.SolidBrush (Color-De '#14532D')), 100, 100, 800, 800)
$g.DrawEllipse((New-Object System.Drawing.Pen (Color-De '#F2C94C'), 18), 140, 140, 720, 720)
$f = New-Object System.Drawing.Font 'Arial', ([single]250), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
$sf = New-Object System.Drawing.StringFormat; $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
$g.DrawString('UW', $f, (New-Object System.Drawing.SolidBrush (Color-De '#F7F5F0')), (New-Object System.Drawing.RectangleF 100, 100, 800, 800), $sf)
foreach ($t in @(@('miniatura', 200), @('catalogo', 600), @('detalle', 1000))) {
  $e = New-Object System.Drawing.Bitmap $t[1], $t[1]; $ge = [System.Drawing.Graphics]::FromImage($e); $ge.InterpolationMode = 'HighQualityBicubic'
  $ge.DrawImage($bmp, 0, 0, $t[1], $t[1]); $ge.Dispose(); $e.Save((Join-Path $salida ("logo_{0}.png" -f $t[0])), [System.Drawing.Imaging.ImageFormat]::Png); $e.Dispose()
}
$g.Dispose(); $bmp.Dispose()
"{0} imagenes en {1}" -f (Get-ChildItem $salida -Filter *.png).Count, $salida
