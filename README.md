# Ruta Cashflow

Calculadora local de rutas de transferencia. Representa plataformas y cuentas como un grafo dirigido, aplica las comisiones y conversiones de cada tramo, y ordena las alternativas por el importe final que llega a destino.

> Las plantillas GrabrFi y Wallbit Pro incluyen las comisiones y cotizaciones informadas por el usuario el 15 de agosto de 2026. Todos los valores siguen siendo editables y se guardan localmente.

## Que incluye este MVP

- Aplicacion nativa para Windows, con icono y AppUserModelID propios.
- Pantalla de carga grafica de marca y arranque `WinExe` sin consola visible.
- Escenarios completos con origen GrabrFi y origen Wallbit Pro.
- Multiples escenarios guardados en forma local, con creación y eliminación desde la barra lateral.
- Alta, edicion, borrado y posicionamiento de plataformas/cuentas.
- Alta, edicion, borrado y activacion de transiciones dirigidas.
- Las transiciones que existen en ambos sentidos se dibujan en dos carriles separados, cada uno con su propia flecha.
- Comision porcentual con topes opcionales, comision fija, fee de trade, cargo sobre la salida, límites de monto y tipo de cambio por tramo.
- Redondeo por paso de cantidad, mínimo nocional y detalle del remanente que no entra en una orden.
- Cotizaciones Binance Spot USDT/ARS y USDC/USDT desde los libros oficiales, calculadas para el monto ingresado.
- Comparacion de todas las rutas simples y resaltado de la mejor.
- Detalle resumido por tarjeta y modal completo, cerrable y sin texto recortado al hacer clic en una ruta comparada.
- El monto inicial funciona como presupuesto total: ninguna ruta puede gastar más al sumar una comisión cobrada aparte.
- Indicadores naranjas en nodos, transiciones y campos cuya cotización debe actualizarse manualmente, con fecha de última revisión y un editor global que replica cada valor de proveedor en todos los escenarios.
- Anexo **Sesión musical** para calcular cuánto saldo hace falta para conseguir un objetivo editable en USD (USD 400 inicialmente).
- Grafo propio del anexo y dos rankings separados: sin pasar por pesos y con pesos bancarizados.
- Actualización periódica y fechada de dólar blue/oficial, con separación visual entre datos de internet y datos manuales.
- Estimador de jubilación con múltiples ingresos y reservas, gasto anual de vacaciones prorrateado, aportes secuenciales a acciones y bonos, calendario de objetivos y autonomía de emergencia.
- Cada reserva puede comenzar en un mes definido o después de alcanzar el objetivo de jubilación; la proyección continúa hasta completar ambos.
- Selector global persistente para recalcular toda Jubilación en dólares ajustados por inflación o en valores nominales sin inflación.
- Gráficos de Jubilación ajustados al tamaño disponible, con ampliación desde 100%, paneo solo al ampliar y lectura exacta al seleccionar puntos.
- Sin cuentas, servidores ni envio de datos.

La formula de una transicion es:

```text
presupuesto = saldo disponible al comenzar el tramo

si la comisión se cobra aparte:
    operable = máximo monto redondeado que cumple:
               operable + comisión(operable) <= presupuesto

si la comisión se descuenta:
    operable = redondear_hacia_abajo(presupuesto, paso_de_cantidad)

comision_porcentual = limitar(operable × porcentaje / 100, minimo, maximo)
comision = comision_porcentual + comision_fija
si la comisión se descuenta:
    debito = operable
    bruto_salida = (operable - comision) × tipo_de_cambio

si la comisión se cobra aparte:
    debito = operable + comision
    bruto_salida = operable × tipo_de_cambio

fee_trade = bruto_salida × porcentaje_trade
despues_trade = bruto_salida - fee_trade
cargo_salida = despues_trade × porcentaje_salida
monto_salida = despues_trade - cargo_salida
remanente = presupuesto - debito
```

La comision fija y los topes se expresan en la moneda del nodo de origen. El tipo de cambio indica cuantas unidades de la moneda de destino entrega una unidad neta de la moneda de origen. El monto inicial predeterminado es USD 2.500 y representa el **gasto total máximo**, con comisiones incluidas. En cada tramo, la salida anterior es el presupuesto disponible del siguiente.

## Plantilla GrabrFi

La plantilla inicial y el botón **Plantilla GrabrFi** crean estas salidas editables:

- ACH: 0,3% del monto, mínimo USD 1 y máximo USD 5;
- USDC: 0,5% del monto más USD 1 de blockchain;
- USDT: 0,6% del monto más USD 1 de blockchain;
- retiro directo a pesos argentinos: USD 5 fijos cobrados aparte y cotización inicial de 1.537,87 ARS por USD.

Las comisiones de salida de GrabrFi se marcan como cargos aparte. Con un presupuesto total de USD 1.000 y una comisión de 1%, la aplicación calcula un envío aproximado de USD 990,10 y una comisión de USD 9,90: el débito total sigue siendo USD 1.000.

## Plantilla Wallbit Pro

La plantilla **Wallbit Pro** incorpora:

- depósitos sin comisión;
- retiro ACH: 0,5% con mínimo de USD 5, cobrado aparte;
- retiro local: cotización inicial de 1.562,15 ARS por USD, sin comisión separada;
- USDC por Polygon: 1%, cobrado aparte;
- USDT por TRON: 1,25%, cobrado aparte;
- conexión ACH Wallbit Pro → GrabrFi y caminos directos hacia Binance.

Estos valores coinciden con el [listado oficial de tarifas de Wallbit](https://help.wallbit.io/es/articles/9156314-listado-de-tarifas-y-comisiones) para el plan Pro y con su [documentación de retiros USDC/USDT](https://help.wallbit.io/es/articles/8129910-puedo-retirar-en-criptomonedas).

## Binance Spot, comisiones y remanentes

- La ruta USDC→USDT usa por defecto el fee taker vigente de 0,095% para pares USDC; USDT→ARS usa 0,1%. Ambos campos son editables porque la tarifa real puede cambiar por nivel VIP, descuento en BNB o promociones.
- El grafo mantiene únicamente el sentido **USDC→USDT**. No crea ni consulta una vuelta USDT→USDC.
- La ruta Binance Spot USDT→ARS primero calcula la venta bruta, descuenta el fee de trade de 0,1% y luego el cargo de salida de 1% indicado por el usuario.
- El botón **Actualizar mercados Binance** consulta los libros `USDTARS` y `USDCUSDT` mediante la [API oficial de Binance Spot](https://developers.binance.com/en/docs/catalog/core-trading-spot-trading/api/rest-api/market), sin credenciales, y pondera los niveles necesarios para cubrir el monto.
- Ese mismo botón actualiza desde `exchangeInfo` el paso de las órdenes límite y los mínimos nocionales. Al 15 de agosto de 2026 ambos pares usan paso 1 para órdenes límite; `USDCUSDT` exige al menos 5 USDT de valor y `USDTARS`, 2.000 ARS.
- `USDTARS` está publicado y en estado `TRADING`; `USDCARS` no existe en el catálogo Spot consultado. Por eso USDC sigue el camino USDC→USDT→ARS.

Binance exige que la cantidad de una orden límite sea múltiplo de `stepSize`. La aplicación redondea hacia abajo y muestra la diferencia como **remanente**: por ejemplo, 1.553,25 USDC con paso 1 permite ordenar 1.553 USDC y deja 0,25 USDC. Además, una orden límite puede quedar parcialmente ejecutada si no hay volumen suficiente al precio elegido; esa parte sigue abierta en una orden GTC y no es un costo. El importe exacto de esa ejecución parcial no se puede conocer de antemano, por lo que la calculadora usa una ejecución inmediata ponderada por el libro y rechaza la cotización si los 100 niveles consultados no alcanzan. Los filtros están documentados en [Binance Spot Filters](https://developers.binance.com/en/docs/products/spot/filters) y las reglas GTC/IOC/FOK en la [guía oficial de órdenes](https://academy.binance.com/en/articles/advanced-order-parameters-and-trade-safeguards-explained).

La [tabla oficial de tarifas](https://www.binance.com/en/fee/trading) publica para un usuario Regular 0,1% maker/taker estándar, 0,095% taker en pares USDC y descuentos opcionales al pagar con BNB. La tarifa exacta de la cuenta solo está disponible mediante un endpoint firmado con credenciales; por privacidad, esta aplicación no pide ni guarda claves de Binance y deja el porcentaje editable.

La aplicación no usa P2P para ninguna conversión de Binance. Si la consulta de mercado falla, conserva la última cotización Spot guardada y permite editarla manualmente.

## Anexo Sesión musical

El botón **♫ Anexo · sesión musical** abre una comparación inversa: en lugar de preguntar cuánto llega al destino, parte del objetivo (USD 400 por defecto) y calcula cuánto debe debitarse **siempre desde GrabrFi**. Los caminos pueden pasar por Wallbit Pro o Binance, pero nunca comienzan en esos saldos intermedios.

El anexo las representa también como un grafo desde cada saldo de origen hasta el resultado final. Para que una opción barata que obliga a bancarizar pesos no tape una alternativa útil, muestra dos ganadores independientes:

- **Sin pasar por pesos:** efectivo vía stablecoin/persona.
- **Pesos bancarizados:** agrupa tanto el pago directo en ARS como la conversión a ARS y recompra de USD al oficial; la interfaz advierte que estos caminos podrían requerir facturación.

Dentro de esos dos escenarios compara tres formas de pago:

- **Efectivo vía stablecoin:** transferencia directa desde GrabrFi o Wallbit Pro, o desde Binance cuando se completan sus costos manuales de envío. Si el saldo parte en USDC, también puede convertirlo a USDT antes del envío.
- **Pago directo en ARS:** objetivo USD multiplicado por el promedio entre compra y venta del dólar blue.
- **Recompra al oficial:** pesos necesarios para comprar el objetivo al valor oficial de venta, con un campo manual adicional para impuestos/recargos. Solo se incorpora al ranking cuando se marca que el banco confirmó la disponibilidad.

La recompra oficial no se presenta como un “rulo infinito”. La [Comunicación A 8336 del BCRA](https://www.bcra.gob.ar/archivos/Pdfs/comytexord/A8336.pdf), vigente desde el 26/09/2025, exige que quien accede al mercado oficial se comprometa a no concertar compras de títulos con liquidación en moneda extranjera durante los 90 días siguientes. La casilla de disponibilidad evita que la calculadora trate una operación sujeta a elegibilidad y controles bancarios como una ruta siempre repetible.

El anexo obtiene `blue` y `oficial` mediante los endpoints documentados de [DolarAPI](https://dolarapi.com/docs/argentina/), guarda tanto la fecha del dato como la hora de la consulta y vuelve a consultar cada 10 minutos mientras la aplicación está abierta (intervalo editable). El Banco Nación aclara que para comprar dólares se debe revisar la cotización mostrada en la operación y que el resumen puede incluir impuestos; por eso la cotización web se presenta como referencia y el recargo real queda manual. [Preguntas frecuentes BNA+](https://bna.com.ar/Personas/BNAMas/PreguntasFrecuentesBNAmas).

Datos automáticos: blue compra/venta, oficial compra/venta, libros Binance y filtros de orden. Datos manuales: USD efectivo entregado por USDC/USDT, comisión de la persona, costo de envío USDC/USDT desde Binance y recargo/impuestos de la compra oficial. Un costo Binance vacío deja ese camino explícitamente pendiente en lugar de asumir que es gratis. Los puntos naranjas del grafo indican caminos que usan alguno de estos datos manuales.

## Cómo actualizar una cotización manual

En el mapa principal, un borde o punto naranja sobre un nodo indica que al menos una de sus salidas usa una cotización manual. La transición correspondiente también se dibuja en naranja. La forma recomendada es pulsar **Valores manuales globales**, modificar una vez la cotización de GrabrFi o Wallbit Pro y pulsar **Guardar y replicar**. El valor y su fecha se aplican a todas las transiciones equivalentes de todos los escenarios, incluidos los que se creen más adelante.

También se puede actualizar desde una transición concreta:

1. Seleccionar la línea naranja del mapa.
2. Modificar **Cotización** en el inspector derecho.
3. Mantener marcada **Esta cotización se actualiza manualmente**.
4. Pulsar **Aplicar cambios**.

La aplicación guarda un catálogo global de valores, además del valor y la fecha de revisión en cada transición vinculada. En el anexo musical, los campos manuales están agrupados bajo **Datos que ponés vos** y coloreados en naranja.

## Cómo eliminar un escenario

Elegí el escenario en la lista lateral, pulsá **Eliminar escenario** y confirmá. La aplicación exige conservar al menos un escenario y no elimina automáticamente ninguno al actualizar de versión.

Las cotizaciones manuales y las últimas cotizaciones Spot obtenidas quedan guardadas junto con el escenario en `%LOCALAPPDATA%\RutaCashflow\scenarios.json` y vuelven a cargarse al abrir la aplicación. Una ruta entre monedas distintas no participa del cálculo hasta que tenga una cotización configurada.

La investigación del 15 de agosto de 2026 encontró que la [documentación oficial de retiros](https://help.grabrfi.com/es/content/como-retirar-a-moneda-local-argentina-bolivia-brasil-colombia-peru-mexico-nigeria-y-uruguay) confirma el costo fijo del retiro argentino y dice que la aplicación muestra una cotización en tiempo real que queda garantizada al confirmar, pero no ofrece una API pública documentada para consultar esa misma cotización. Para evitar comparar con una tasa externa que no coincida con la oferta real de GrabrFi, la aplicación usa el valor manual visible en la pantalla de confirmación. La [tabla pública de comisiones](https://help.grabrfi.com/en/content/full-schedule-of-fees) también puede diferir de los porcentajes informados por el usuario, por lo que esos valores siguen siendo editables.

## Ejecutar

Requiere el SDK de .NET indicado en `global.json`.

```powershell
dotnet run --project src/Cashflow.Windows/Cashflow.Windows.csproj
```

Los escenarios se guardan en `%LOCALAPPDATA%\RutaCashflow\scenarios.json`.

## Validar

```powershell
dotnet run --project tests/Cashflow.Core.Tests/Cashflow.Core.Tests.csproj
dotnet run --project tests/Cashflow.Windows.Tests/Cashflow.Windows.Tests.csproj
dotnet build CashflowCalculator.sln -c Release
```

La validación completa restaura la herramienta local, compila en Release, ejecuta ambas suites y genera un informe Cobertura combinado para `Cashflow.Core` y `Cashflow.Windows`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Test-Coverage.ps1
```

El script informa las dos métricas disponibles en el informe Cobertura de este harness: líneas y ramas. Falla por debajo de 34% de líneas o 28% de ramas; la línea base local de la versión 0.9 es 35,00% y 29,30%, respectivamente. GitHub Actions ejecuta esta validación rápida en cada push y pull request; no hay suites externas o costosas recurrentes.

Para generar un ejecutable liviano compatible con el runtime instalado:

```powershell
powershell -ExecutionPolicy Bypass -File tools\Publish-Windows.ps1
```

Con `-Portable` se genera un ejecutable autocontenido, más grande, que no depende de un runtime de .NET previamente instalado.

Para crear o actualizar el acceso directo personal dentro de `Codex\CODEX APPS`:

```powershell
powershell -ExecutionPolicy Bypass -File tools\Install-CodexAppsShortcut.ps1
```

## Estructura y siguiente etapa

`Cashflow.Core` no depende de WPF. La version Android puede consumir el mismo modelo y motor desde una interfaz movil (por ejemplo, .NET MAUI cuando el entorno tenga un SDK compatible), manteniendo los mismos archivos de escenarios o una importacion/exportacion equivalente.
