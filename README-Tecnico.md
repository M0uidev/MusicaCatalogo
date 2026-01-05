# 🛠️ Catálogo de Música - Documentación Técnica

## 📖 Tabla de Contenidos

1. [Resumen Ejecutivo](#resumen-ejecutivo)
2. [Arquitectura y Mapa del Proyecto](#arquitectura-y-mapa-del-proyecto)
3. [Estructura de Carpetas y Archivos](#estructura-de-carpetas-y-archivos)
4. [Componentes Clave y Responsabilidades](#componentes-clave-y-responsabilidades)
5. [Flujos Importantes](#flujos-importantes)
6. [Sistema de Navegación SPA](#sistema-de-navegación-spa)
7. [Estado y Persistencia](#estado-y-persistencia)
8. [Eventos y Actualizaciones de UI](#eventos-y-actualizaciones-de-ui)
9. [Setup y Ejecución](#setup-y-ejecución)
10. [Mejoras Sugeridas](#mejoras-sugeridas)

---

## Resumen Ejecutivo

**MusicaCatalogo** es una aplicación web full-stack para gestionar una colección personal de música en cassettes y CDs.

### Stack Tecnológico

- **Backend**: ASP.NET Core 8 (C#) con Minimal APIs
- **Base de Datos**: SQLite con Dapper ORM
- **Frontend**: HTML5 + JavaScript vanilla (ES6+) + CSS3
- **Arquitectura**: SPA (Single Page Application) con router personalizado
- **Persistencia**: SQLite local + localStorage para estado del cliente

### Características Principales

- CRUD completo para Canciones, Álbumes, Medios, e Intérpretes
- Sistema de gestión de versiones y covers
- Reproductor de audio global persistente
- Búsqueda avanzada con filtros
- Gestión de imágenes (portadas de álbumes)
- Sistema de notificaciones para problemas pendientes
- Acceso desde múltiples dispositivos en red local
- System tray icon con minimización automática

---

## Arquitectura y Mapa del Proyecto

```
┌─────────────────────────────────────────────────────────────┐
│                      CLIENTE (Browser)                       │
├─────────────────────────────────────────────────────────────┤
│  app.html (Shell SPA)                                        │
│    ├─ router.js         → Navegación sin recargas           │
│    ├─ audioPlayerGlobal.js → Reproductor persistente        │
│    ├─ components.js     → Componentes UI reutilizables      │
│    ├─ pageInitializers.js → Init específico por página      │
│    └─ app.js            → Funciones helpers y constantes    │
│                                                              │
│  Páginas HTML (cargadas dinámicamente)                       │
│    ├─ index.html        → Dashboard principal               │
│    ├─ buscar.html       → Búsqueda de canciones             │
│    ├─ cancion.html      → Modal de edición de canción       │
│    ├─ albumes.html      → Gestión de álbumes                │
│    ├─ medios.html       → Lista de cassettes/CDs            │
│    ├─ medio.html        → Detalle de cassette/CD            │
│    ├─ interpretes.html  → Lista de artistas                 │
│    ├─ interprete.html   → Detalle de artista                │
│    ├─ perfil-cancion.html → Versiones múltiples             │
│    ├─ duplicados.html   → Gestión de duplicados             │
│    └─ estadisticas.html → Dashboard de estadísticas         │
└─────────────────────────────────────────────────────────────┘
                              │
                              │ HTTP/REST API
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    SERVIDOR (ASP.NET Core)                   │
├─────────────────────────────────────────────────────────────┤
│  Program.cs                                                  │
│    ├─ Configuración de Kestrel (puerto 5179)                │
│    ├─ Middleware SPA (redirección automática)               │
│    ├─ Servicio de archivos estáticos                        │
│    ├─ System tray icon y minimización                       │
│    └─ Inicialización de base de datos                       │
│                                                              │
│  ConfiguracionEndpoints.cs                                   │
│    └─ Definición de ~40 endpoints REST                      │
│         ├─ /api/buscar                                       │
│         ├─ /api/canciones/*                                  │
│         ├─ /api/albumes/*                                    │
│         ├─ /api/medios/*                                     │
│         ├─ /api/interpretes/*                                │
│         ├─ /api/estadisticas                                 │
│         └─ /api/mantenimiento/*                              │
│                                                              │
│  RepositorioMusica.cs                                        │
│    └─ Lógica de negocio y consultas (60+ métodos)           │
│         ├─ CRUD de entidades                                 │
│         ├─ Búsquedas y autocompletado                        │
│         ├─ Sistema de versiones/covers                       │
│         └─ Estadísticas y análisis                           │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                  BASE DE DATOS (SQLite)                      │
├─────────────────────────────────────────────────────────────┤
│  BaseDatos.cs                                                │
│    ├─ Creación de esquema                                    │
│    ├─ Migraciones automáticas                                │
│    └─ Gestión de conexiones                                  │
│                                                              │
│  catalogo.db                                                 │
│    ├─ Tablas de referencia (ecualizador, marca, etc.)       │
│    ├─ interpretes                                            │
│    ├─ albumes                                                │
│    ├─ formato_grabado (cassettes)                            │
│    ├─ formato_grabado_cd (CDs)                               │
│    ├─ temas (canciones en cassettes)                         │
│    └─ temas_cd (canciones en CDs)                            │
└─────────────────────────────────────────────────────────────┘
```

---

## Estructura de Carpetas y Archivos

```
MusicaCatalogo/
│
├── Program.cs                    # Punto de entrada, configuración del servidor
├── MusicaCatalogo.csproj         # Proyecto .NET
├── MusicaCatalogo.sln            # Solución Visual Studio
│
├── Data/                         # Capa de acceso a datos
│   ├── BaseDatos.cs              # Gestión de SQLite: esquema, migraciones
│   ├── catalogo.db               # Base de datos SQLite (generada en runtime)
│   └── Entidades/
│       └── Entidades.cs          # Modelos y DTOs (95 clases)
│
├── Services/                     # Capa de lógica de negocio
│   ├── RepositorioMusica.cs      # Consultas y lógica principal (60+ métodos)
│   └── ServicioRed.cs            # Utilidades de red para acceso remoto
│
├── Endpoints/                    # Definición de API REST
│   └── ConfiguracionEndpoints.cs # ~40 endpoints mapeados
│
└── Web/                          # Frontend (HTML/CSS/JS)
    ├── app.html                  # Shell SPA (punto de entrada)
    ├── index.html                # Dashboard principal
    ├── buscar.html               # Búsqueda de canciones
    ├── cancion.html              # (Parcial) Modal de edición
    ├── albumes.html              # Gestión de álbumes
    ├── medios.html               # Lista de medios
    ├── medio.html                # Detalle de medio
    ├── interpretes.html          # Lista de intérpretes
    ├── interprete.html           # Detalle de intérprete
    ├── perfil-cancion.html       # Gestión de versiones
    ├── duplicados.html           # Gestión de duplicados
    ├── estadisticas.html         # Dashboard de estadísticas
    ├── diagnostico.html          # Info del sistema
    │
    ├── css/
    │   ├── estilos.css           # Estilos base y variables CSS
    │   └── componentes.css       # Estilos de componentes reutilizables
    │
    └── js/
        ├── router.js             # Sistema de navegación SPA
        ├── audioPlayerGlobal.js  # Reproductor global persistente
        ├── app.js                # Helpers, constantes, funciones globales
        ├── components.js         # Componentes UI (modales, HTML helpers)
        └── pageInitializers.js   # Inicializadores específicos por página
```

### Propósito de Cada Carpeta

| Carpeta/Archivo | Propósito |
|-----------------|-----------|
| **Program.cs** | Configuración del servidor Kestrel, middleware, system tray, inicialización |
| **Data/** | Modelos de datos, esquemas de base de datos, conexión a SQLite |
| **Services/** | Lógica de negocio, consultas complejas, validaciones |
| **Endpoints/** | Definición de rutas HTTP y mapeo de controllers |
| **Web/** | Interfaz de usuario: HTML, CSS, JavaScript |
| **Web/css/** | Sistema de diseño, variables CSS, responsive |
| **Web/js/** | Lógica de cliente, routing SPA, reproductor, helpers |

---

## Componentes Clave y Responsabilidades

### Backend

#### 1. `Program.cs`

**Responsabilidades:**
- Configurar Kestrel para escuchar en puerto 5179 (todas las interfaces)
- Inicializar base de datos SQLite
- Middleware para forzar navegación SPA (redirige `.html` a `app.html#/ruta`)
- Servir archivos estáticos desde carpeta `Web/`
- System tray icon con menú contextual (mostrar/ocultar, abrir navegador, cerrar servidor)
- Deshabilitar botón de cierre de consola (solo se cierra desde tray icon)

**Líneas clave:**
```csharp
// Redirigir peticiones HTML a SPA shell
app.Use(async (context, next) => {
    if (!isAjaxRequest && path.EndsWith(".html") && path != "/app.html") {
        context.Response.Redirect($"/app.html#{fullPath}");
        return;
    }
    await next();
});
```

---

#### 2. `BaseDatos.cs`

**Responsabilidades:**
- Crear esquema SQLite si no existe
- Ejecutar migraciones automáticas para nuevas columnas
- Proveer conexiones a la base de datos
- Crear índices para optimizar búsquedas

**Tablas principales:**
- `interpretes`: Artistas/bandas
- `albumes`: Álbumes y singles
- `formato_grabado` / `formato_grabado_cd`: Cassettes y CDs
- `temas` / `temas_cd`: Canciones

**Índices creados:**
```sql
CREATE INDEX IF NOT EXISTS idx_temas_interprete ON temas(id_interprete);
CREATE INDEX IF NOT EXISTS idx_temas_album ON temas(id_album);
CREATE INDEX IF NOT EXISTS idx_albumes_interprete ON albumes(id_interprete);
```

---

#### 3. `RepositorioMusica.cs` (3217 líneas)

**Responsabilidades:**
- Implementar toda la lógica de negocio
- CRUD completo para todas las entidades
- Búsquedas con normalización de texto (sin tildes)
- Sistema de autocompletado fuzzy
- Gestión automática de versiones/covers
- Sincronización de álbumes entre covers
- Estadísticas y análisis de colección

**Métodos destacados:**
- `BuscarAsync()`: Búsqueda global por nombre, intérprete, número de medio
- `AutocompletarTemasAsync()`: Búsqueda fuzzy sin tildes para autocompletado
- `MarcarArtistaOriginalAsync()`: Lógica automática de conversión cover/versión
- `SincronizarAlbumesCoversAsync()`: Propaga álbum de original a covers
- `ObtenerGrupoDuplicadoPorIdAsync()`: Gestión de versiones múltiples

**Normalización de texto:**
```csharp
private string NormalizarTexto(string texto)
{
    if (string.IsNullOrWhiteSpace(texto)) return "";
    return new string(texto.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
        .ToArray()).ToLowerInvariant();
}
```

---

#### 4. `ConfiguracionEndpoints.cs`

**Responsabilidades:**
- Mapear todos los endpoints REST
- Validación de parámetros
- Manejo de errores HTTP
- Subida de archivos (imágenes de portada)

**Principales endpoints:**

| Método | Ruta | Descripción |
|--------|------|-------------|
| GET | `/api/buscar` | Búsqueda global |
| GET | `/api/canciones/autocompletar` | Autocompletado de canciones |
| GET | `/api/canciones/{id}` | Detalle de canción |
| POST | `/api/canciones` | Crear canción |
| PUT | `/api/canciones/{id}` | Actualizar canción |
| DELETE | `/api/canciones/{id}` | Eliminar canción |
| POST | `/api/canciones/reordenar` | Reordenar canciones en medio |
| GET | `/api/albumes` | Listar álbumes |
| POST | `/api/albumes` | Crear álbum |
| PUT | `/api/albumes/{id}` | Actualizar álbum |
| DELETE | `/api/albumes/{id}` | Eliminar álbum |
| POST | `/api/albumes/{id}/cover` | Subir portada |
| DELETE | `/api/albumes/{id}/cover` | Eliminar portada |
| GET | `/api/medios` | Listar cassettes/CDs |
| GET | `/api/medios/{num}` | Detalle de medio |
| POST | `/api/medios` | Crear medio |
| PUT | `/api/medios/{num}` | Actualizar medio |
| DELETE | `/api/medios/{num}` | Eliminar medio |
| GET | `/api/interpretes` | Listar intérpretes |
| POST | `/api/mantenimiento/sincronizar` | Sincronizar álbumes de covers |
| GET | `/api/estadisticas` | Estadísticas generales |
| GET | `/api/diagnostico` | Info del sistema |

---

### Frontend

#### 5. `app.html` (Shell SPA)

**Responsabilidades:**
- Proveer estructura HTML base persistente (header, nav, footer, reproductor)
- Cargar scripts globales una sola vez
- Contenedor `#app-main` donde se inyecta contenido dinámico
- Inicializar router SPA al cargar

**Elementos persistentes:**
```html
<header> <!-- Navegación global --> </header>
<main id="app-main"> <!-- Contenido dinámico --> </main>
<footer> <!-- Pie de página --> </footer>
<div id="audio-player-global"> <!-- Reproductor --> </div>
```

---

#### 6. `router.js` (403 líneas)

**Responsabilidades:**
- Implementar navegación SPA sin recargas completas
- Interceptar clics en enlaces con `data-spa-link`
- Cargar contenido de páginas mediante AJAX
- Extraer solo el `<main>` del HTML cargado
- Ejecutar scripts inline de las páginas cargadas
- Limpiar scripts de la página anterior
- Actualizar título y URL (pushState)
- MutationObserver para detectar links dinámicos

**Flujo de navegación:**
```javascript
1. Usuario hace clic en link con data-spa-link
2. preventDefault() para evitar recarga
3. Fetch de la página HTML solicitada (con header X-Requested-With: XMLHttpRequest)
4. Extracción del contenido <main>
5. Limpieza de scripts de página anterior
6. Inyección del nuevo contenido en #app-main
7. Ejecución de scripts inline de la nueva página
8. Inicialización de página específica (pageInitializers.js)
9. Attach de event listeners a nuevos links
10. Actualización de history.pushState
```

**Código clave:**
```javascript
loadPage(path, pushState = true) {
    fetch(path, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
        .then(html => this.extractMainContent(html))
        .then(content => {
            this.cleanupPageScripts();
            mainContainer.innerHTML = content;
            this.executeScripts(mainContainer);
            this.initializePage(path);
            if (pushState) history.pushState({ path }, '', `#${path}`);
        });
}
```

---

#### 7. `audioPlayerGlobal.js` (1006 líneas)

**Responsabilidades:**
- Reproductor de audio global persistente
- Gestión de playlist y cola de reproducción
- Modos de reproducción (shuffle, repeat)
- Sistema de favoritos
- Persistencia en localStorage (volumen, estado, preferencias)
- UI actualizable (carátula, progreso, controles)

**Estado del reproductor:**
```javascript
const state = {
    audio: new Audio(),              // Elemento de audio
    currentSong: null,               // Canción actual
    currentMedio: null,              // Medio actual
    playlist: [],                    // Playlist completa
    currentIndex: -1,                // Índice en playlist
    isShuffleOn: false,              // Modo shuffle
    repeatMode: 'off',               // 'off', 'playlist', 'song'
    likedSongs: new Set(),           // Set de canciones favoritas
    queueVisible: false,             // Visibilidad de cola
    randomQueue: []                  // Cola aleatoria
};
```

**Funciones principales:**
- `playSong(idCancion, tipo, contextPlaylist)`: Reproduce canción
- `loadPlaylist(numMedio, tipo)`: Carga todas las canciones de un medio
- `playNext()` / `playPrevious()`: Navegación en playlist
- `toggleShuffle()`: Activa/desactiva modo aleatorio
- `toggleRepeat()`: Cicla entre off/playlist/song
- `toggleLike()`: Marca/desmarca favorito
- `savePlayerState()` / `restorePlayerState()`: Persistencia

**Persistencia:**
```javascript
localStorage.setItem('playerState', JSON.stringify({
    idCancion, tipo, currentMedio, currentIndex,
    playlist, isShuffleOn, repeatMode
}));
localStorage.setItem('playerVolume', volume);
localStorage.setItem('likedSongs', JSON.stringify([...likedSongs]));
```

---

#### 8. `app.js` (478 líneas)

**Responsabilidades:**
- Definir constantes globales (iconos SVG)
- Funciones helper para UI (formateo, validación)
- Sistema de notificaciones
- Generación de HTML dinámico

**Constantes importantes:**
```javascript
const ICONOS = {
    cassetteNormal: `<svg>...</svg>`,
    cassetteCromo: `<svg>...</svg>`,
    cassetteFecr: `<svg>...</svg>`,
    cassetteMetal: `<svg>...</svg>`,
    cd: `<svg>...</svg>`,
    // ... otros iconos
};
```

**Helpers:**
- `obtenerIconoCinta(bias)`: Retorna SVG según tipo de cinta
- `obtenerIconoMedio(tipo)`: Retorna SVG de cassette/CD
- `formatearNumero(num)`: Formato con separadores de miles
- `htmlError(mensaje)`: Genera HTML de error
- `htmlCargando()`: Genera HTML de loading

---

#### 9. `components.js` (206 líneas)

**Responsabilidades:**
- Componentes UI reutilizables
- Modales genéricos
- Helpers de HTML

**Componentes:**
- `abrirModal(titulo, contenido)`: Modal genérico
- `confirmarAccion(mensaje, callback)`: Modal de confirmación
- `mostrarNotificacion(mensaje, tipo)`: Toast notifications

---

#### 10. `pageInitializers.js` (196 líneas)

**Responsabilidades:**
- Inicializar funciones específicas de cada página después de la carga SPA
- Llamar a funciones de setup según la ruta

**Ejemplo:**
```javascript
window.PageInitializers = {
    '/index.html': () => {
        if (typeof cargarResumen === 'function') cargarResumen();
        if (typeof generarQR === 'function') generarQR();
    },
    '/buscar.html': () => {
        if (typeof inicializarBuscador === 'function') inicializarBuscador();
    },
    // ... otros inicializadores
};
```

---

## Flujos Importantes

### 1. CRUD de Canciones

#### Crear Canción

```
[UI] medio.html → Click "Agregar Canción"
  ↓
[Modal] cancion.html (inline en medio.html)
  ↓
[JS] Recoger datos del formulario
  ↓
[HTTP POST] /api/canciones
  ↓
[Backend] RepositorioMusica.CrearCancionAsync()
  ├─ Resolver ID de intérprete (crear si no existe)
  ├─ Normalizar nombre de canción
  ├─ Insertar en tabla temas o temas_cd
  └─ Retornar ID de canción creada
  ↓
[UI] Cerrar modal y recargar lista de canciones
```

#### Editar Canción

```
[UI] buscar.html → Click botón editar (lápiz)
  ↓
[HTTP GET] /api/canciones/{id}?tipo={cassette|cd}
  ↓
[Modal] Rellenar formulario con datos actuales
  ↓
[JS] Usuario modifica datos
  ↓
[HTTP PUT] /api/canciones/{id}
  ↓
[Backend] RepositorioMusica.ActualizarCancionAsync()
  ├─ Validar datos
  ├─ Actualizar registro
  └─ Lógica de versiones si cambia "es_original"
  ↓
[UI] Actualizar UI sin recargar página
```

#### Eliminar Canción

```
[UI] medio.html → Click botón eliminar (🗑️)
  ↓
[Modal] Confirmación
  ↓
[HTTP DELETE] /api/canciones/{id}?tipo={cassette|cd}
  ↓
[Backend] RepositorioMusica.EliminarCancionAsync()
  ↓
[UI] Recargar lista de canciones
```

---

### 2. CRUD de Álbumes

#### Crear Álbum

```
[UI] albumes.html → Click "Nuevo"
  ↓
[Modal] Formulario de creación
  ├─ Nombre del álbum
  ├─ Intérprete (select con búsqueda)
  ├─ Año
  └─ ¿Es single? (checkbox)
  ↓
[HTTP POST] /api/albumes
  ↓
[Backend] RepositorioMusica.CrearAlbumAsync()
  ├─ Validar campo valor único nombre + intérprete
  ├─ Insertar en tabla albumes
  └─ Retornar ID del álbum
  ↓
[UI] Cerrar modal y agregar álbum a la lista
```

#### Subir Portada de Álbum

```
[UI] Detalle de álbum → Click "📷 Cambiar"
  ↓
[Input file] Seleccionar imagen
  ↓
[JS] Construir FormData con archivo
  ↓
[HTTP POST] /api/albumes/{id}/cover
  ↓
[Backend] ConfiguracionEndpoints
  ├─ Validar tipo de archivo (jpg, png, webp)
  ├─ Guardar en /covers/{id}.{ext}
  ├─ Actualizar campo imagen_portada en DB
  └─ Retornar URL de la imagen
  ↓
[UI] Actualizar <img> con nueva URL + timestamp para evitar caché
```

---

### 3. CRUD de Medios (Cassettes/CDs)

#### Crear Medio

```
[UI] medios.html → Click "Nuevo Cassette" o "Nuevo CD"
  ↓
[Modal] Formulario extenso
  ├─ Número de medio (ej: c001, f014)
  ├─ Tipo (cassette o cd)
  ├─ Marca (select)
  ├─ Grabador (select)
  ├─ Fuente (select)
  ├─ Fecha de grabación
  ├─ Bias (solo cassettes)
  ├─ Ecualizador, modo, supresor
  └─ Observaciones
  ↓
[HTTP POST] /api/medios
  ↓
[Backend] RepositorioMusica.CrearFormatoAsync()
  ├─ Resolver IDs de referencias (marca, grabador, etc.)
  ├─ Crear nuevos registros de lookup si no existen
  ├─ Insertar en formato_grabado o formato_grabado_cd
  └─ Retornar número de medio
  ↓
[UI] Redirigir a medio.html?num={numero}
```

---

### 4. Sistema de Gestión de Versiones y Covers

```
[Escenario] Usuario marca una canción como "Original"
  ↓
[HTTP POST] /api/canciones/versiones/marcar-original
  Body: { idCancion, tipo, idCancionOriginal }
  ↓
[Backend] RepositorioMusica.MarcarArtistaOriginalAsync()
  ├─ Obtener nombre de la canción
  ├─ Buscar todas las canciones con el mismo nombre (normalizado)
  ├─ Para cada canción encontrada:
  │   ├─ Si es la marcada como original: es_original = 1
  │   ├─ Si tiene diferente intérprete: es_cover = 1
  │   └─ Si tiene mismo intérprete: es_version = 1
  ├─ Sincronizar álbum: copiar id_album de original a covers
  └─ Retornar éxito
  ↓
[UI] Actualizar badges y estados en buscar.html
```

**Lógica automática:**
- Solo puede haber **1 original** por grupo de canciones con el mismo nombre
- **Cover** = Mismo nombre, diferente artista
- **Versión** = Mismo nombre, mismo artista, diferente grabación

---

### 5. Flujo de Búsqueda y Filtrado

```
[UI] buscar.html
  ↓
[Input] Usuario escribe en barra de búsqueda
  ↓
[JS] Evento input con debounce (300ms)
  ↓
[HTTP GET] /api/buscar?q={texto}
  ↓
[Backend] RepositorioMusica.BuscarAsync()
  ├─ Normalizar texto de búsqueda
  ├─ Buscar en temas.nombre_normalizado
  ├─ Buscar en interpretes.nombre_normalizado
  ├─ Buscar en medios por número
  ├─ UNION de resultados
  └─ LIMIT 100
  ↓
[UI] Renderizar tarjetas de canciones
  ↓
[Filtros UI] Usuario aplica filtros
  ├─ Por intérprete (select)
  ├─ Por año (select)
  ├─ Con/sin álbum
  └─ Ordenación
  ↓
[JS] Filtrado en cliente (no nueva petición)
  ↓
[URL] Actualizar query params (?interprete=X&año=Y...)
  ↓
[localStorage] Guardar estado de filtros
```

---

### 6. Flujo de Reproducción de Audio

```
[UI] Click en botón ▶️ de una canción
  ↓
[JS] audioPlayerGlobal.playSong(idCancion, tipo, playlist)
  ↓
[HTTP GET] /api/canciones/{id}?tipo={tipo}
  ↓
[Backend] Retornar canción con datos completos
  ↓
[JS] Actualizar estado del reproductor
  ├─ audio.src = cancion.ruta_archivo
  ├─ currentSong = cancion
  ├─ currentIndex = posición en playlist
  └─ playlist = lista de canciones del medio
  ↓
[HTML Audio API] audio.play()
  ↓
[UI] Actualizar interfaz del reproductor
  ├─ Mostrar carátula
  ├─ Mostrar nombre de canción
  ├─ Mostrar intérprete
  ├─ Actualizar barra de progreso
  └─ Activar controles
  ↓
[localStorage] Guardar estado para persistencia
```

**Eventos del audio:**
- `timeupdate`: Actualizar barra de progreso
- `ended`: Reproducir siguiente canción (según repeatMode)
- `error`: Mostrar error y saltar a siguiente

---

## Sistema de Navegación SPA

### Conceptos Clave

El sistema implementa una **SPA híbrida**:
- **Shell persistente** (`app.html`): Header, nav, footer, reproductor
- **Contenido dinámico**: Se inyecta en `#app-main` sin recargar la página
- **Sin framework**: Router custom en vanilla JavaScript

### Evitar Refresh Completo

#### Middleware de Servidor (Program.cs)

```csharp
app.Use(async (context, next) =>
{
    var isAjaxRequest = context.Request.Headers["X-Requested-With"] == "XMLHttpRequest";
    var path = context.Request.Path.Value ?? "";
    
    // Redirigir peticiones HTML normales (no AJAX) al shell SPA
    if (!isAjaxRequest && path.EndsWith(".html") && path != "/app.html")
    {
        var fullPath = path + context.Request.QueryString;
        context.Response.Redirect($"/app.html#{fullPath}");
        return;
    }
    
    await next();
});
```

**Funcionamiento:**
- Si el usuario navega directamente a `http://localhost:5179/buscar.html`
- El servidor redirige a `http://localhost:5179/app.html#/buscar.html`
- El router JS detecta el hash y carga `buscar.html` vía AJAX

#### Router de Cliente (router.js)

```javascript
// Interceptar clics en links
document.addEventListener('click', (e) => {
    const link = e.target.closest('a[data-spa-link]');
    if (link) {
        e.preventDefault();
        const path = new URL(link.href).pathname;
        SPARouter.navigateTo(path);
    }
});

// Cargar contenido sin recargar
loadPage(path) {
    fetch(path, {
        headers: { 'X-Requested-With': 'XMLHttpRequest' }
    })
    .then(response => response.text())
    .then(html => {
        const content = this.extractMainContent(html);
        document.getElementById('app-main').innerHTML = content;
        this.executeScripts(document.getElementById('app-main'));
        history.pushState({ path }, '', `#${path}`);
    });
}
```

---

### Gestión de Scripts Inline

**Problema:** Al cargar contenido dinámicamente, los `<script>` no se ejecutan.

**Solución:**

```javascript
executeScripts(container) {
    const scripts = container.querySelectorAll('script');
    scripts.forEach(oldScript => {
        const newScript = document.createElement('script');
        if (oldScript.src) {
            newScript.src = oldScript.src;
        } else {
            newScript.textContent = oldScript.textContent;
        }
        oldScript.parentNode.replaceChild(newScript, oldScript);
    });
}
```

---

### Cargas Automáticas vs Manuales

#### Estado Actual

**Manual:**
- Al navegar a una página, el contenido se carga automáticamente
- Pero los datos internos (listas de canciones, álbumes, etc.) **NO** se refrescan automáticamente
- Después de crear/editar/eliminar, hay que **recargar la lista manualmente** con `cargarCanciones()`, etc.

#### Ejemplo: Después de Crear Canción

```javascript
// Estado actual (MANUAL)
async function guardarCancion() {
    await fetch('/api/canciones', { method: 'POST', body: JSON.stringify(datos) });
    cerrarModal();
    cargarCanciones(); // ❌ Llamada manual
}
```

#### Mejora Sugerida: Auto-refresh

```javascript
// Propuesta: Sistema de eventos
window.addEventListener('cancionCreada', () => cargarCanciones());

async function guardarCancion() {
    await fetch('/api/canciones', { method: 'POST', body: JSON.stringify(datos) });
    cerrarModal();
    window.dispatchEvent(new Event('cancionCreada')); // ✅ Auto-refresh
}
```

---

## Estado y Persistencia

### Dónde Vive el Estado

| Tipo de Estado | Ubicación | Persistencia |
|----------------|-----------|--------------|
| **Datos de colección** (canciones, álbumes, medios) | SQLite (`catalogo.db`) | Permanente |
| **Estado del reproductor** (canción actual, playlist) | localStorage (`playerState`) | Persistente entre sesiones |
| **Volumen del reproductor** | localStorage (`playerVolume`) | Persistente |
| **Canciones favoritas** | localStorage (`likedSongs`) | Persistente |
| **Filtros de búsqueda** | URL query params + localStorage | Persistente (vía URL) |
| **Estado de UI** (modales abiertos, scroll) | Memoria (JavaScript) | Volátil (se pierde al recargar) |

---

### Sincronización UI ↔ Datos

#### Flujo Típico

```
[UI] Usuario realiza acción (crear, editar, eliminar)
  ↓
[HTTP POST/PUT/DELETE] API REST
  ↓
[Backend] Actualiza SQLite
  ↓
[Response] 200 OK con datos actualizados
  ↓
[UI] Actualiza DOM manualmente:
  ├─ Recargar lista completa (cargarCanciones())
  ├─ Actualizar solo el elemento modificado
  └─ Cerrar modal
```

#### Problema: No Hay Estado Global Reactivo

- **No hay framework reactivo** (Vue, React, Angular)
- Cada página gestiona su propio estado en variables locales
- No hay sincronización automática entre páginas

#### Ejemplo: Editar Canción desde Buscar

```javascript
// buscar.html
let canciones = []; // Estado local

async function cargarCanciones() {
    canciones = await fetch('/api/buscar?q=').then(r => r.json());
    renderizarCanciones(canciones);
}

async function editarCancion(id) {
    // Abrir modal, editar, guardar...
    await fetch('/api/canciones/' + id, { method: 'PUT', body: ... });
    
    // Actualizar estado local
    cargarCanciones(); // Recarga todo ❌
    // O actualizar solo el item:
    Object.assign(canciones.find(c => c.id === id), datosNuevos); // ✅
    renderizarCanciones(canciones);
}
```

---

### Sistema de Notificaciones

Las notificaciones de "Problemas Pendientes" (campana 🔔) usan:

```javascript
// app.js
async function cargarNotificaciones() {
    const resp = await fetch('/api/canciones/sin-album');
    const cancionesSinAlbum = await resp.json();
    
    // Actualizar badge
    document.getElementById('notifBadge').textContent = cancionesSinAlbum.length || '';
    
    // Renderizar lista
    document.getElementById('notifList').innerHTML = cancionesSinAlbum.map(c => 
        `<a href="/cancion.html?id=${c.id}">🎵 ${c.nombre}</a>`
    ).join('');
}

// Llamar cada vez que se carga una página
cargarNotificaciones();
```

---

## Eventos y Actualizaciones de UI

### Qué Dispara Renders/Recargas

| Evento | Trigger | Elemento Actualizado |
|--------|---------|---------------------|
| **Navegación SPA** | Click en `data-spa-link` | `#app-main` (contenido completo) |
| **Búsqueda** | Input en barra de búsqueda (debounced) | Lista de canciones |
| **Aplicar filtros** | Click en botones de filtro | Lista de canciones (filtrado en cliente) |
| **Crear/Editar/Eliminar** | Submit de formulario | Lista correspondiente (manual refresh) |
| **Reproducir canción** | Click en botón play | Reproductor global |
| **Cambio de volumen** | Input en slider | Elemento `<audio>` + localStorage |
| **Subir portada** | Input file + submit | `<img>` de portada |

---

### Listeners Globales

#### Router

```javascript
// router.js
document.addEventListener('click', attachSpaLinkListener);
window.addEventListener('popstate', handleBackButton);
```

#### Reproductor

```javascript
// audioPlayerGlobal.js
audio.addEventListener('timeupdate', updateProgressBar);
audio.addEventListener('ended', handleSongEnd);
audio.addEventListener('error', handleAudioError);
```

#### Notificaciones

```javascript
// app.js (cargado en cada página via pageInitializers.js)
setInterval(cargarNotificaciones, 60000); // Cada minuto
```

---

### Puntos Donde Falta Auto-Refresh

#### Problema 1: Crear Canción desde Medio

**Escenario:** Usuario está en `medio.html`, crea una canción.  
**Actual:** Tiene que llamar manualmente `cargarCanciones()`.  
**Ideal:** Al cerrar el modal, la lista se actualiza automáticamente.

**Solución:**

```javascript
// En cancion.html (modal)
async function guardarCancion() {
    await fetch('/api/canciones', { method: 'POST', body: ... });
    cerrarModal();
    
    // Opción 1: Callback
    if (window.onCancionGuardada) window.onCancionGuardada();
    
    // Opción 2: Event
    window.dispatchEvent(new CustomEvent('cancionGuardada', { detail: cancion }));
}

// En medio.html
window.addEventListener('cancionGuardada', () => {
    cargarCanciones();
});
```

---

#### Problema 2: Editar Álbum desde Detalle

**Escenario:** Usuario edita el nombre de un álbum en la página de detalle.  
**Actual:** Al guardar, el título de la página no se actualiza hasta refrescar.  
**Ideal:** El título se actualiza inmediatamente.

**Solución:**

```javascript
async function guardarAlbum() {
    const respuesta = await fetch('/api/albumes/' + id, { method: 'PUT', body: ... });
    const albumActualizado = await respuesta.json();
    
    // Actualizar UI inmediatamente
    document.querySelector('h2').textContent = albumActualizado.nombre;
    document.querySelector('.album-año').textContent = albumActualizado.año;
}
```

---

### Mejora: Sistema de Eventos Global

**Propuesta:**

```javascript
// events.js (nuevo archivo)
const EventBus = {
    events: {},
    
    on(event, callback) {
        if (!this.events[event]) this.events[event] = [];
        this.events[event].push(callback);
    },
    
    emit(event, data) {
        if (this.events[event]) {
            this.events[event].forEach(cb => cb(data));
        }
    }
};

// Uso en cualquier parte:
EventBus.on('cancionCreada', () => cargarCanciones());
EventBus.on('albumActualizado', (album) => actualizarTituloAlbum(album));

// Al guardar:
EventBus.emit('cancionCreada', cancion);
```

---

## Setup y Ejecución

### Requisitos

- **Windows** (o Linux/Mac con ajustes menores)
- **.NET 8 SDK** ([Descargar](https://dotnet.microsoft.com/download/dotnet/8.0))
- **Navegador moderno** (Chrome, Edge, Firefox, Safari)

---

### Instalación

```bash
# Clonar repositorio
git clone <url-del-repo>
cd MusicaCatalogo

# Restaurar dependencias
dotnet restore
```

---

### Ejecución en Desarrollo

```bash
dotnet run
```

La aplicación estará disponible en:
- **Local**: `http://localhost:5179`
- **Red**: `http://<IP-local>:5179` (se muestra en consola)

---

### Compilación para Producción

```bash
dotnet build -c Release
```

---

### Publicar como Ejecutable Único

```bash
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

El ejecutable se genera en:
```
bin/Release/net8.0-windows/win-x64/publish/MusicaCatalogo.exe
```

**Importante:** Copia la carpeta `Web/` al mismo directorio del `.exe`.

---

### Variables de Entorno

No hay variables de entorno requeridas. Todo se configura automáticamente.

**Configuración por defecto:**
- **Puerto**: 5179 (definido en `Program.cs`)
- **Base de datos**: `catalogo.db` (se crea automáticamente en el directorio del ejecutable)
- **Carpeta Web**: `./Web` (relativa al ejecutable)
- **Carpeta de covers**: `./covers` (se crea automáticamente)

---

### Scripts Útiles

No hay scripts predefinidos, pero puedes crear:

#### `start.bat` (Windows)

```batch
@echo off
dotnet run
```

#### `publish.bat` (Windows)

```batch
@echo off
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
xcopy /E /I /Y Web bin\Release\net8.0-windows\win-x64\publish\Web
echo.
echo ✅ Publicado en: bin\Release\net8.0-windows\win-x64\publish\
pause
```

---

## Mejoras Sugeridas

### 🔴 Alta Prioridad (Bugs/Deuda Técnica)

#### 1. Auto-refresh de Listas Después de CRUD

**Problema:** Al crear/editar/eliminar, las listas no se actualizan automáticamente.  
**Solución:** Implementar sistema de eventos (EventBus) o callbacks.  
**Archivos a modificar:**
- Crear `Web/js/eventBus.js`
- Modificar todos los formularios de creación/edición para emitir eventos
- Modificar todas las listas para escuchar eventos

**Tiempo estimado:** 4-6 horas

---

#### 2. Validación de Formularios

**Problema:** No hay validación robusta en el frontend. Se puede enviar data inválida al backend.  
**Solución:** 
- Agregar atributos HTML5 (`required`, `pattern`, `min`, `max`)
- Implementar validación JavaScript antes de enviar
- Mostrar mensajes de error claros

**Ejemplo:**
```javascript
function validarFormularioCancion(datos) {
    if (!datos.nombre) {
        mostrarError('El nombre de la canción es obligatorio');
        return false;
    }
    if (!datos.idInterprete) {
        mostrarError('Debes seleccionar un intérprete');
        return false;
    }
    return true;
}
```

**Tiempo estimado:** 3-4 horas

---

#### 3. Manejo de Errores HTTP

**Problema:** Muchas peticiones fetch no manejan errores correctamente.  
**Solución:** Crear función helper para fetch con manejo de errores consistente.

```javascript
async function fetchAPI(url, options = {}) {
    try {
        const response = await fetch(url, options);
        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.mensaje || 'Error en la petición');
        }
        return await response.json();
    } catch (err) {
        mostrarNotificacion(err.message, 'error');
        throw err;
    }
}

// Uso:
const canciones = await fetchAPI('/api/canciones');
```

**Tiempo estimado:** 2-3 horas

---

### 🟡 Media Prioridad (UX/Features)

#### 4. Paginación de Listas

**Problema:** Listas largas (Canciones, Medios) pueden ser lentas.  
**Solución:** Implementar paginación en backend y frontend.

**Backend:**
```csharp
public async Task<(List<Cancion> items, int total)> ListarCancionesAsync(int pagina, int porPagina)
{
    var offset = (pagina - 1) * porPagina;
    var items = await _db.ObtenerConexion().QueryAsync<Cancion>(
        "SELECT * FROM temas LIMIT @Limit OFFSET @Offset",
        new { Limit = porPagina, Offset = offset }
    );
    var total = await _db.ObtenerConexion().QueryFirstAsync<int>("SELECT COUNT(*) FROM temas");
    return (items.ToList(), total);
}
```

**Tiempo estimado:** 6-8 horas

---

#### 5. Drag & Drop para Portadas

**Problema:** Subir portadas requiere click en botón → file input.  
**Solución:** Implementar zona de drag & drop.

```javascript
albumCover.addEventListener('dragover', (e) => {
    e.preventDefault();
    albumCover.classList.add('drag-over');
});

albumCover.addEventListener('drop', async (e) => {
    e.preventDefault();
    const file = e.dataTransfer.files[0];
    await subirPortada(albumId, file);
});
```

**Tiempo estimado:** 2-3 horas

---

#### 6. Búsqueda Avanzada con Operadores

**Problema:** La búsqueda actual es simple (solo texto libre).  
**Solución:** Permitir operadores como `intérprete:"Queen"`, `año:1975`, etc.

**Tiempo estimado:** 8-10 horas

---

### 🟢 Baja Prioridad (Optimizaciones/Nice-to-have)

#### 7. Caché de Imágenes

**Problema:** Las portadas se recargan cada vez (aunque el navegador las cachea, no hay control).  
**Solución:** Implementar Service Worker para caché offline.

**Tiempo estimado:** 4-6 horas

---

#### 8. Exportar/Importar Catálogo

**Problema:** No hay forma de exportar datos a CSV/Excel.  
**Solución:** Endpoint `/api/exportar` que genere CSV o JSON.

**Tiempo estimado:** 3-4 horas

---

#### 9. Estadísticas Avanzadas

**Problema:** Estadísticas básicas (solo conteos).  
**Solución:** Agregar gráficos interactivos con Chart.js o similar.

**Tiempo estimado:** 6-8 horas

---

#### 10. Tema Oscuro

**Problema:** Solo hay tema claro.  
**Solución:** Implementar toggle de tema con CSS variables.

```css
:root {
    --color-fondo: #ffffff;
    --color-texto: #1f2937;
}

[data-theme="dark"] {
    --color-fondo: #1f2937;
    --color-texto: #f3f4f6;
}
```

**Tiempo estimado:** 3-4 horas

---

### 🔵 Refactors Recomendados

#### 11. Separar Lógica de UI

**Problema:** Muchas funciones mezclan lógica de negocio con manipulación de DOM.  
**Solución:** Patrón MVC o MVVM.

```javascript
// Modelo
class CancionModel {
    static async obtener(id) {
        return await fetch(`/api/canciones/${id}`).then(r => r.json());
    }
    static async guardar(datos) {
        return await fetch('/api/canciones', { method: 'POST', body: JSON.stringify(datos) });
    }
}

// Vista
class CancionView {
    renderizar(cancion) {
        return `<div class="cancion-card">...</div>`;
    }
}

// Controlador
class CancionController {
    async cargar(id) {
        const cancion = await CancionModel.obtener(id);
        const html = new CancionView().renderizar(cancion);
        document.getElementById('container').innerHTML = html;
    }
}
```

**Tiempo estimado:** 15-20 horas (refactor grande)

---

#### 12. Migrar a Framework Frontend

**Problema:** Vanilla JS se vuelve difícil de mantener con más features.  
**Solución:** Migrar a Vue.js, React o Svelte.

**Pros:**
- Reactividad automática
- Componentes reutilizables
- Mejor gestión de estado
- DevTools

**Contras:**
- Requiere build step
- Curva de aprendizaje
- Más dependencias

**Tiempo estimado:** 40-60 horas (reescritura completa del frontend)

---

#### 13. Tests Automatizados

**Problema:** No hay tests.  
**Solución:** 
- Backend: xUnit + Moq
- Frontend: Jest + Testing Library

**Ejemplo (Backend):**
```csharp
[Fact]
public async Task CrearCancion_DebeRetornarId()
{
    var repo = new RepositorioMusica(mockDB);
    var id = await repo.CrearCancionAsync(new CancionRequest { ... });
    Assert.True(id > 0);
}
```

**Tiempo estimado:** 20-30 horas (cobertura básica)

---

### Priorización Sugerida

Si solo puedes hacer 5 mejoras, hazlas en este orden:

1. ✅ **Auto-refresh de listas** (2-3 días) → Mejora UX inmediatamente
2. ✅ **Validación de formularios** (1 día) → Previene bugs
3. ✅ **Manejo de errores HTTP** (medio día) → Mejora confiabilidad
4. ✅ **Paginación** (1-2 días) → Mejora performance con muchos datos
5. ✅ **Drag & drop para portadas** (medio día) → Mejora UX significativamente

---

## 🎯 Conclusión

Este proyecto está bien estructurado para una aplicación personal, con una arquitectura clara y código mantenible. Las principales áreas de mejora son:

1. **Reactividad automática** de la UI
2. **Validaciones** más robustas
3. **Paginación** para escalabilidad
4. **Tests** para confiabilidad a largo plazo

Con las mejoras sugeridas, la aplicación podría crecer sin problemas a miles de canciones y mantenerse fácil de extender.

---

## 📞 Soporte

Para modificar este proyecto:

1. **Entiende el flujo**: Sigue el diagrama de arquitectura
2. **Explora el código**: Usa `view_file_outline` para mapear rápidamente
3. **Prueba localmente**: Usa `dotnet watch run` para hot reload
4. **Documenta cambios**: Actualiza este README con nuevas features

**Happy coding!** 🚀
