# ProgVoraz C#

Migracion de ProgVoraz a C# con:

- `ProgVorazCs.Core`: logica de monedas, stock y algoritmo voraz.
- `ProgVorazCs.Console`: aplicacion de consola (modos `a`, `b`, `c`).
- `ProgVorazCs.Gui`: interfaz grafica WinForms para Windows.

## Requisitos

- .NET SDK 10.0+
- `monedas.txt` y `stock.txt` en la raiz del repositorio.

## Compilar

```bash
dotnet build ProgVorazCSharp.slnx
```

## Ejecutar consola

```bash
dotnet run --project ProgVorazCs.Console --configuration Release
```

## Ejecutar GUI (Windows)

```powershell
dotnet run --project ProgVorazCs.Gui --configuration Release
```

## CI y Release en GitHub Actions

- `CI CSharp` (`.github/workflows/ci.yml`): build y smoke tests de consola en Linux/macOS/Windows, mas smoke launch de GUI en Windows.
- `Release CSharp` (`.github/workflows/release.yml`): se dispara al hacer push de tags `v*`, publica artefactos `.zip` para:
	- Consola: `win-x64`, `linux-x64`, `osx-x64`
	- GUI: `win-x64`

Para disparar release:

```bash
git tag -a v0.5.0-cs -m "Release v0.5.0-cs"
git push origin main --follow-tags
```

## Tags iniciales

Este repositorio incluye tags equivalentes de migracion en C#:

- `v0.1.0-cs`
- `v0.2.0-cs`
- `v0.3.0-cs`
- `v0.3.1-cs`
- `v0.4.0-cs`
- `v0.5.0-cs-rc1`
- `v0.5.0-cs`

## Servidor HTTP local

Reimplementación administrada de las rutas originales del servidor C:

```bash
dotnet run --project ProgVorazCs.Console -- --server --port 8080
curl http://127.0.0.1:8080/health
curl http://127.0.0.1:8080/export_stock_json
curl -X POST --data-binary @tests/sample_batch.csv http://127.0.0.1:8080/batch
```

Se escucha **únicamente** en `127.0.0.1`, con límite de 1 MiB por petición batch. No hay procesos ni archivos temporales auxiliares. Este cambio no incorpora todavía conversión entre divisas con tasas remotas.


## Conversion de divisas (paridad del original C)

Se ha incorporado un proveedor de tipos de cambio con JSON válido, códigos
ISO de las nueve monedas y aritmética \`BigInteger\` para evitar desbordamientos.
Se utiliza primero la API configurada en la versión C y, ante fallos, el
fichero \`rates_stub.json\` para los pares explícitamente definidos.
La variable \`PROGVORAZ_EXCHANGE_SOURCE=stub\` fuerza la reproducción
determinista sin red:

\`\`\`bash
PROGVORAZ_EXCHANGE_SOURCE=stub dotnet run --project ProgVorazCs.Console -- --convert EUR USD 100
\`\`\`

Una tasa de ejemplo del stub no representa una cotización real ni actual.
Si falta una tasa y falla la API, se retorna error, sin utilizar tasas
inventadas. Se redondea a céntimos con precisión decimal.


## Integridad del inventario (concurrencia)

El módulo \`StockLedger\` reimplementa la escritura de \`stock.txt\` con
fichero de preparación y reemplazo, bloqueo de archivo entre procesos .NET y
validación estricta de los límites de cada moneda. La consola utiliza operaciones
read-modify-write bajo bloqueo para descontar piezas y modificar stock de forma
atómica. Si otra instancia consume las piezas antes, se informa al usuario en
lugar de persistir un stock negativo. Las pruebas de regresión incluyen operaciones
concurrentes, saldo insuficiente y protección entre bloques. Los procesos ajenos
al módulo, como el programa original en C, **no comparten el bloqueo**.


## Pruebas diferenciales exhaustivas

La suite \`ProgVorazCs.Tests\` compara el algoritmo de precisión arbitraria
con un oráculo independiente que enumera combinaciones de monedas.
Hay 480 escenarios deterministas con distintos stocks, cantidades, límites
de piezas y cambio cercano, además de las pruebas de persistencia, HTTP,
concurrencia e integridad previamente existentes. No se usa el propio motor
para calcular los resultados esperados.

### Optimización del cambio cercano

Para importes compatibles con programación dinámica, se utiliza **una sola tabla DP** para buscar la mayor cantidad alcanzable. Se elimina la reconstrucción repetida de la tabla para cada céntimo inferior, evitando el peor caso cuadrático anterior. Las pruebas incluyen un importe elevado sin ninguna moneda alcanzable.

### Corrección del formulario WinForms

La interfaz gráfica comparte las operaciones transaccionales del motor en lugar de sobrescribir `stock.txt` a partir de un estado obsoleto. Los cambios de administrador y las devoluciones limitadas notifican los errores de persistencia y recargan el inventario. La lista del resultado ya no se borra automáticamente al actualizar el panel de stock.


## Cambio específico de caja (C# nativo)

Se ha reimplementado la operación \`caja / específico\` del C original.
\`--specific MONEDA RECIBIDO_CSV ENTREGADO_CSV\` valida que el valor
monetario recibido sea exactamente igual al devuelto; no modifica stock.
Con \`--specific-stock\`, cada tipo se actualiza en **una sola transacción**
\`stock_anterior + monedas_recibidas - monedas_entregadas\`, rechazando los
saldos negativos y preservando el archivo si falla la comprobación.
Cada lista debe tener exactamente tantas cantidades como denominaciones
de esa divisa, ordenadas según \`monedas.txt\`. Ejemplo para una divisa
con denominaciones 5,3,1:

\`\`\`bash
dotnet run --project ProgVorazCs.Console -- --specific euro 0,0,5 1,0,0 --specific-stock
\`\`\`

La operación con stock modifica \`stock.txt\`; use un inventario de prueba
para este ejemplo. Esta API no ejecuta ni enlaza código C.


## Historial y resumen de inventario

La consola incluye \`--history [--tail 100]\` (hasta 1000 entradas) y
\`--summary EUR\`. El historial usa \`historial.txt\`, el formato de marcas de
tiempo del original en C, y registra cada persistencia correcta de inventario
realizada por este backend C#. La ausencia de historial no se considera un error.
La escritura del historial es un registro informativo posterior al commit y no
constituye una transacción conjunta con \`stock.txt\`; si falla el disco para el
log, la operación de stock ya confirmada se mantiene.


## Restauración segura con inventario concurrente

\`Restore\` utiliza ahora el mismo fichero de bloqueo que las operaciones
de recepción, consumo y ajuste. Copia el snapshot a un archivo de preparación,
valida todas las divisas y efectúa la sustitución de \`stock.txt\` antes de liberar
el bloqueo. Un snapshot incompleto se rechaza preservando el inventario.
El bloqueo solo coordina aplicaciones C# que empleen \`StockLedger\`.


## Menú interactivo ampliado

El programa de consola ya incluye los comandos del menú principal del C
original: \`x\`/\`convertir\`, \`caja\`, \`historial\`, \`resumen\`,
\`snapshot\`, \`restaurar\`, \`reporte\` y \`json\`, con varios alias
en inglés. La conversión y el cambio específico verifican los datos
antes de mostrar resultados; un error de stock no confirma la operación.
Las exportaciones y la restauración trabajan sobre las rutas indicadas
por el usuario. Al recibir EOF, la consola finaliza en lugar de
repetir indefinidamente el menú.


## Lanzamiento de la GUI desde consola

\`dotnet run --project ProgVorazCs.Console -- --gui\` abre la aplicación
WinForms en Windows usando \`ProcessStartInfo\`, sin ejecutar shell ni
el binario original en C. Busca la GUI empaquetada junto a la consola o
compilada en el proyecto WinForms, con ruta absoluta y directorio de trabajo
explícito. En Linux y macOS devuelve un error explicativo: no se presenta
WinForms como una GUI multiplataforma.


## Resultados JSON interactivos

\`--json\` o la variable \`PROGVORAZ_JSON=1\` reimplementan el renderizado
del C original para las operaciones de cambio en consola. El objeto
\`resultado\` contiene \`denominacion\` y \`cantidad\` como cadenas
decimales (sin pérdida de precisión), además de \`stock\` cuando se
consume inventario. Este modo conserva los menús de texto; los clientes
que precisen exclusivamente JSON deben extraer la línea JSON o usar
la exportación JSON dedicada. Actions verifica el formato en
Linux, macOS y Windows.

## Cambio exacto para importes grandes no canónicos

El motor usa Dijkstra sobre los residuos del valor de la mayor denominación (hasta 50.000 céntimos). Minimiza de manera demostrable el número de piezas cuando la clase de residuo tiene una representación mínima que cabe en el importe. Se mantiene la ruta BigInteger para cantidades astronómicas. Los ejemplos de regresión incluyen denominaciones `4,3,1`, donde el algoritmo voraz resulta subóptimo.

## Solución exacta con dos denominaciones, incluso con stock

Para cantidades superiores a 200.000 céntimos y dos denominaciones diferentes, el motor aplica Euclides extendido y aritmética diofántica `a·x + b·y = importe`. Calcula la solución con menos monedas y respeta las disponibilidades de inventario, los mínimos y máximos de piezas sin reservar memoria proporcional al importe. Se comprueban cantidades BigInteger astronómicas y casos sin solución.
