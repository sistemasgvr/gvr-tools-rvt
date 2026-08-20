# GVR Tools RVT

Complementos (add-ins) internos de GVR para Autodesk Revit.

## Exportador de PDF Masivo

Complemento para Revit que agrega una pestaña **GVR Tools** a la cinta de opciones con un botón
para plotear/exportar láminas (sheets) a PDF de forma masiva, similar a herramientas como
ProSheets o DiRoots PDF Export. Permite elegir qué láminas exportar, elegir una carpeta destino,
y genera automáticamente una subcarpeta con el nombre del proyecto que contiene un PDF por cada
lámina exportada.

### Características

- Botón en una pestaña propia de la cinta de Revit (**GVR Tools**), sin depender de macros ni de
  ventanas externas al programa.
- Lista de todas las láminas del proyecto activo, con casillas de selección, buscador por número o
  nombre, y filtro por **set de láminas** (los "Sheet Issue/Revision Sets" guardados en el
  proyecto).
- Selección de carpeta destino; el complemento crea dentro de ella una subcarpeta con el nombre del
  archivo/proyecto de Revit y exporta ahí un PDF por cada lámina seleccionada.
- Nombre de archivo configurable mediante tokens: `{SheetNumber}`, `{SheetName}`,
  `{RevisionNumber}`, `{RevisionDescription}` (por defecto: `{SheetNumber} - {SheetName}`).
- Cada lámina se exporta en el tamaño de papel que le corresponde según su rótulo (título), no en
  un tamaño fijo para todas.
- Barra de progreso con opción de cancelar a mitad de proceso, resumen final con errores por
  lámina (si los hubiera), y opción de abrir la carpeta de destino al finalizar.
- No depende del idioma de instalación de Revit ni de Windows: no asume nombres de categorías,
  parámetros ni impresoras en inglés.

### Requisitos

- **Revit 2021** (cualquier idioma). El proyecto está pensado para poder adaptarse a versiones
  2022 en adelante más adelante — ver [Extender a otras versiones](#extender-a-otras-versiones-de-revit).
- Windows 10/11 de 64 bits.
- Una impresora PDF instalada. Revit 2021 no tiene un exportador de PDF propio en su API (esa
  función se agregó recién en Revit 2022), así que este complemento plotea usando
  `Document.PrintManager` a través de una impresora PDF real, por ejemplo **Microsoft Print to
  PDF**, que viene incluida en Windows 10/11 (Panel de control → Dispositivos e impresoras →
  Agregar impresora, si no aparece ya instalada). También funciona con otras impresoras PDF
  (Adobe PDF, Bullzip, CutePDF, etc.) si el nombre contiene "PDF".
- Para compilar: [.NET SDK](https://dotnet.microsoft.com/) (se probó con la SDK 10) y, opcionalmente,
  Visual Studio 2022 con la carga de trabajo ".NET desktop development".

### Instalación rápida (para probar)

```powershell
.\scripts\install-addin.ps1
```

Esto compila el proyecto en modo `Release` y registra el `.addin` en
`%APPDATA%\Autodesk\Revit\Addins\2021\`. Reinicia Revit 2021 y busca la pestaña **GVR Tools**.

Parámetros útiles:

```powershell
# Instalar para todos los usuarios del equipo (requiere PowerShell como administrador)
.\scripts\install-addin.ps1 -AllUsers

# Compilar en Debug y registrar para otra versión de Revit ya migrada
.\scripts\install-addin.ps1 -Configuration Debug -RevitVersion 2022
```

### Instalación manual

1. Compila el proyecto: `dotnet build src/MassPdfExport/MassPdfExport.csproj -c Release`.
2. Copia `deploy/GvrTools.MassPdfExport.addin` a
   `%APPDATA%\Autodesk\Revit\Addins\2021\` (o a
   `%PROGRAMDATA%\Autodesk\Revit\Addins\2021\` para instalarlo para todos los usuarios).
3. Edita el `<Assembly>` del `.addin` copiado para que apunte a la ruta completa de
   `src\MassPdfExport\bin\Release\GvrTools.MassPdfExport.dll`.
4. Reinicia Revit.

### Uso

1. Abre un proyecto en Revit 2021.
2. En la pestaña **GVR Tools**, presiona **Exportar PDF Masivo**.
3. Selecciona las láminas a exportar (con casillas, búsqueda o un set de láminas guardado).
4. Elige la carpeta destino con **Examinar...**. El cuadro inferior muestra la subcarpeta que se
   creará (con el nombre del proyecto).
5. Ajusta el patrón de nombre de archivo si lo necesitas.
6. Presiona **Exportar PDF**. Puedes cancelar en cualquier momento; al finalizar se muestra un
   resumen y, si la opción está marcada, se abre la carpeta de destino.

### Estructura del proyecto

```
src/MassPdfExport/
  App.cs                       Punto de entrada del add-in (IExternalApplication), crea la cinta
  Commands/
    MassPdfExportCommand.cs    Comando de Revit (IExternalCommand) que abre la ventana
  Core/
    SheetCollector.cs          Lee láminas y sets de láminas del documento
    SheetSizeReader.cs         Mide el tamaño real de una lámina a partir de su rótulo
    PdfPrinterLocator.cs       Busca una impresora PDF instalada, sin asumir idioma
    PaperSizeMatcher.cs        Empareja el tamaño de la lámina con un tamaño de papel del driver
    PdfExportService.cs        Orquesta la exportación lámina por lámina vía PrintManager
    FileNaming.cs              Arma y sanea el nombre de archivo a partir de un patrón
    ExportModels.cs            Modelos simples de progreso/resultado/resumen
    NaturalSortComparer.cs     Orden natural de números de lámina (A-2 antes que A-10)
  UI/
    MainWindow.xaml(.cs)       Ventana WPF
    MainViewModel.cs           Lógica de la ventana (MVVM)
    SheetRow.cs, RelayCommand.cs
  Resources/
    RibbonIconFactory.cs       Ícono del botón dibujado por código (sin archivos binarios)
deploy/
  GvrTools.MassPdfExport.addin Manifiesto de referencia para instalación manual
scripts/
  install-addin.ps1            Compila e instala el add-in localmente para pruebas
```

### Cómo funciona la exportación a PDF

La API pública de Revit para exportar PDF directamente (`PDFExportOptions` +
`Document.Export(...)`) **no existe en Revit 2021** — se agregó en Revit 2022. Por eso, para 2021
el complemento plotea usando `Document.PrintManager` contra una impresora PDF real instalada en
Windows, exactamente como lo haría un usuario desde el diálogo *Imprimir* de Revit, pero de forma
automática:

- Cada lámina se imprime por separado, como un set de una sola vista, con `CombinedFile = true` y
  `PrintToFileName` apuntando directamente al nombre final deseado — así Revit no necesita adivinar
  ningún nombre de archivo.
- El tamaño de papel de cada lámina se mide a partir del rótulo (bounding box del title block) y se
  compara contra los tamaños que realmente reporta la impresora seleccionada (usando
  `System.Drawing.Printing.PrinterSettings`, que consulta el mismo controlador de Windows que usa
  Revit). Si se encuentra una coincidencia razonable, se imprime a tamaño real (100%); si no, se usa
  "Ajustar a página" como respaldo para que la exportación nunca falle.

### Extender a otras versiones de Revit

El proyecto compila contra el paquete NuGet
[`Nice3point.Revit.Api.RevitAPI`](https://www.nuget.org/packages/Nice3point.Revit.Api.RevitAPI/)
(ensamblados de referencia, no requieren tener Revit instalado para compilar). Para dar soporte a
Revit 2022 en adelante, que sí tiene `PDFExportOptions`/`Document.Export` nativos para PDF:

1. Crea un `PropertyGroup` adicional (o multi-target el `.csproj`) para la versión nueva, con su
   propio `Nice3point.Revit.Api.RevitAPI`/`RevitAPIUI` (p. ej. `2022.*`). Revit 2021-2024 usan
   `net48`; Revit 2025 en adelante usa `net8.0-windows`.
   [Repositorio de plantillas de Nice3point](https://github.com/Nice3point/RevitTemplates)
- Como en Revit 2022+ sí existe `PDFExportOptions`, conviene implementar una segunda variante de
  `IPdfExportStrategy` (interfaz que puedes extraer de `PdfExportService`) que use
  `Document.Export(folder, viewIds, options)` en vez de `PrintManager`, seleccionada según la
  versión de Revit en tiempo de compilación (`#if REVIT2022_OR_GREATER`, etc.) — es más simple y no
  depende de una impresora instalada.
2. Genera un `.addin` por versión (o un único `.addin` con varias entradas `<AddIn>`, una por
   ensamblado) y ajusta `scripts/install-addin.ps1` para el año correspondiente.

### Rama de trabajo

El desarrollo de este complemento vive en la rama `dev_deyvy`.

### Limitaciones conocidas

- El emparejamiento automático de tamaño de papel depende de que la impresora PDF instalada
  reporte ese tamaño estándar (ANSI/ARCH/ISO/Carta, etc.); tamaños de rótulo muy personalizados
  caen de respaldo en "Ajustar a página".
- No exporta a un único PDF combinado: el pedido original es una carpeta con un PDF por lámina, que
  es lo que hace. Si en el futuro se necesita también un combinado, es una función adicional a
  agregar sobre `PdfExportService`.
- Los tokens de revisión (`{RevisionNumber}`, `{RevisionDescription}`) leen los parámetros
  estándar de Revit "Revisión actual" de la lámina; si el proyecto no usa revisiones, quedan vacíos.
