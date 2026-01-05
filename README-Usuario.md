# 📀 Catálogo de Música - Guía de Usuario

## 🎵 ¿Qué es esta aplicación?

**Catálogo de Música** es una aplicación web personal para organizar y gestionar tu colección de música grabada en **cassettes** y **CDs**. Te permite:

- 📝 Catalogar todas tus canciones con información detallada
- 💿 Organizar tus cassettes y CDs con metadatos completos
- 🎤 Administrar intérpretes y artistas
- 📀 Crear y gestionar álbumes con portadas personalizadas
- 🔍 Buscar canciones rápidamente con filtros avanzados
- 🎧 Reproducir música (si tienes archivos de audio disponibles)
- 📊 Ver estadísticas de tu colección
- 📱 Acceder desde cualquier dispositivo en tu red local

---

## 🚀 Cómo usar la aplicación

### Acceso Inicial

1. **Ejecutar el programa**: Haz doble clic en `MusicaCatalogo.exe`
2. **Abrir en el navegador**: 
   - La aplicación se abrirá automáticamente en `http://localhost:5179`
   - Si no se abre, haz Ctrl+Click en el enlace que aparece en la consola
3. **Acceso desde celular**: En la página principal encontrarás un código QR que puedes escanear con tu teléfono (asegúrate de estar en la misma red WiFi)

### Navegación Principal

La aplicación tiene una barra de navegación superior con las siguientes secciones:

- **Inicio**: Resumen de tu colección
- **Canciones**: Búsqueda y listado de todas las canciones
- **Álbumes**: Gestión de álbumes y singles
- **Medios**: Listado de cassettes y CDs
- **Intérpretes**: Directorio de artistas
- **Estadísticas**: Datos y gráficos de tu colección
- **🔔 (Campana)**: Notificaciones de problemas pendientes (ej: canciones sin álbum asignado)

---

## 📚 Guías Paso a Paso

### 1️⃣ Cómo Agregar una Canción

Hay dos formas de agregar canciones:

#### Opción A: Desde un Medio Existente (Recomendado)

1. Ve a **Medios** en el menú superior
2. Busca y haz clic en el cassette o CD donde está la canción
3. Haz clic en el botón **"+ Agregar Canción"**
4. Completa el formulario:
   - **Nombre de la canción**: Título de la canción
   - **Intérprete**: Busca el artista (se autocompleta) o escribe uno nuevo
   - **Año**: Año de la grabación (opcional)
   - **Lado**: A o B (solo para cassettes)
   - **Pista/Posición**: Número de pista o contador (opcional)
   - **Álbum**: Selecciona un álbum existente o créalo desde el botón "+"
   - **Es Original**: Marca si esta es la versión original (no un cover)
5. Haz clic en **"Guardar"**
6. La canción aparecerá en la lista del medio

> **💡 Tip**: Si la canción es un cover o tiene múltiples versiones, desmarca "Es Original" y el sistema la vinculará automáticamente con la versión original.

#### Opción B: Desde la Búsqueda (Edición Rápida)

1. Ve a **Canciones** y busca una canción existente
2. Haz clic en el botón de **editar** (lápiz) en la esquina superior derecha de la tarjeta
3. Modifica los datos que necesites
4. Guarda los cambios

---

### 2️⃣ Cómo Crear un Álbum

1. Ve a **Álbumes** en el menú superior
2. Haz clic en el botón **"+ Nuevo"**
3. Completa el formulario:
   - **Nombre del álbum**: Título completo
   - **Intérprete**: Selecciona el artista del álbum
   - **Año**: Año de lanzamiento (opcional)
   - **¿Es un single?**: Marca esta opción si es un single en lugar de un álbum completo
4. Haz clic en **"Guardar"**
5. El álbum aparecerá en la lista

> **✨ Nota**: Los singles se muestran con un badge especial "💿 Single" para diferenciarlos de los álbumes completos.

---

### 3️⃣ Cómo Asignar una Canción a un Álbum

#### Durante la Creación de una Canción

1. Al agregar una canción nueva (ver sección 1️⃣)
2. En el campo **"Álbum"**, haz clic en el desplegable
3. Selecciona un álbum existente
4. Si no existe, haz clic en el botón **"+"** al lado del select
5. Crea el álbum desde el modal que se abre
6. El álbum recién creado se seleccionará automáticamente

#### Para una Canción Existente

1. Ve a **Canciones** y busca la canción
2. Haz clic en el botón de **editar** (lápiz)
3. En el formulario de edición, cambia el álbum
4. Guarda los cambios

> **⚠️ Importante**: Las canciones que no tienen álbum asignado aparecerán como notificaciones pendientes (🔔). Es recomendable asignarles un álbum.

---

### 4️⃣ Cómo Subir/Asignar una Foto (Cover) a un Álbum

1. Ve a **Álbumes**
2. Haz clic en el álbum que quieres editar
3. En la página de detalles del álbum, verás la portada actual (o un placeholder)
4. Haz clic en el botón **"📷 Cambiar"** que aparece sobre la imagen
5. Selecciona una imagen desde tu computadora
6. La portada se actualizará automáticamente

> **📌 Formatos soportados**: JPG, PNG, WEBP

---

### 5️⃣ Cómo Subir/Asignar una Foto a una Canción

Las canciones heredan automáticamente la portada de su álbum. Si quieres asignar una portada personalizada:

1. Ve a **Canciones** y busca la canción
2. Haz clic en el nombre de la canción para abrir el modal de versiones
3. Si la canción tiene múltiples versiones (covers), verás la opción de gestionar las portadas por versión
4. Haz clic en **"📷 Cambiar"** junto a la versión que quieres editar
5. Selecciona la imagen

> **💡 Tip**: Si una canción pertenece a un álbum, su portada será la del álbum a menos que subas una personalizada.

---

### 6️⃣ Cómo Editar Elementos

#### Editar una Canción

1. Ve a **Canciones** y busca la canción
2. Haz clic en el botón de **editar** (lápiz) en la esquina de la tarjeta
3. Modifica los campos que necesites
4. Haz clic en **"Guardar"**

#### Editar un Álbum

1. Ve a **Álbumes**
2. Haz clic en el álbum para ver sus detalles
3. Haz clic en el botón **"✏️ Editar"**
4. Realiza los cambios
5. Guarda

#### Editar un Medio (Cassette/CD)

1. Ve a **Medios**
2. Haz clic en el cassette o CD
3. Haz clic en el botón **"✏️ Editar"** en la parte superior
4. Modifica los metadatos (marca, grabador, fecha, etc.)
5. Guarda

---

### 7️⃣ Cómo Borrar Elementos

#### Borrar una Canción

1. Ve a **Medios** y selecciona el medio que contiene la canción
2. En la lista de canciones, busca la que quieres eliminar
3. Haz clic en el botón de **"🗑️ Eliminar"**
4. Confirma la eliminación

> **⚠️ Advertencia**: Esta acción no se puede deshacer.

#### Borrar un Álbum

1. Ve a **Álbumes**
2. Haz clic en el álbum
3. Haz clic en el botón **"🗑️ Eliminar"**
4. Confirma

> **⚠️ Importante**: Eliminar un álbum **NO** elimina las canciones asociadas. Las canciones quedarán sin álbum asignado.

#### Borrar un Medio

1. Ve a **Medios**
2. Haz clic en el cassette o CD
3. Haz clic en **"🗑️ Eliminar Medio"**
4. Confirma

> **⚠️ Advertencia**: Esto eliminará el medio **Y TODAS SUS CANCIONES**. Esta acción es irreversible.

---

### 8️⃣ Cómo Usar el Buscador y Filtros

#### Búsqueda Simple

1. Ve a **Canciones**
2. Escribe en la barra de búsqueda el nombre de la canción, artista, o número de medio
3. Los resultados se filtrarán automáticamente mientras escribes

#### Filtros Avanzados

1. En la página de **Canciones**, verás botones de filtro:
   - **Todas/Con Álbum/Sin Álbum**: Filtra por asignación de álbum
   - **Intérprete**: Desplegable para filtrar por artista específico
   - **Año**: Desplegable para filtrar por año de grabación
   - **Ordenar**: Alfabético o por año

2. Haz clic en los botones para activar/desactivar filtros
3. Los filtros se combinan entre sí
4. Usa **"Limpiar Filtros"** para resetear todo

> **📌 Nota**: Los filtros se guardan en la URL, por lo que puedes compartir un enlace con filtros específicos.

---

### 9️⃣ Cómo Funciona el Reproductor de Audio

El reproductor está ubicado en la parte inferior de la pantalla y es **global** (se mantiene activo mientras navegas).

#### Reproducir una Canción

1. Desde cualquier listado de canciones, haz clic en el botón **▶️ Play**
2. El reproductor se activará en la parte inferior
3. Verás la carátula, nombre de la canción, artista, y controles

#### Controles del Reproductor

- **▶️ / ⏸️**: Play/Pausa
- **⏮️**: Canción anterior
- **⏭️**: Canción siguiente
- **🔀**: Modo aleatorio (shuffle)
- **🔁**: Modo de repetición (off / repetir playlist / repetir canción)
- **❤️**: Marcar como favorito
- **🎵**: Ver cola de reproducción
- **🔊**: Control de volumen

> **💡 Tip**: El reproductor recuerda tu volumen y preferencias incluso si cierras y vuelves a abrir la aplicación.

> **⚠️ Nota**: El reproductor solo funciona si tienes archivos de audio vinculados a las canciones. Esta funcionalidad depende de tu configuración personal.

---

### 🔟 Gestión de Versiones y Covers

Cuando una canción tiene múltiples versiones (por ejemplo, originales y covers):

1. Ve a **Canciones** y busca la canción
2. Si tiene múltiples versiones, verás un badge **"🎭 Múltiples artistas"**
3. Haz clic en la tarjeta de la canción
4. Se abrirá un modal mostrando todas las versiones
5. Verás:
   - La versión **original** con un badge verde "✨ ORIGINAL"
   - Los **covers** con badge rosa "🎭 COVER"
   - Las **versiones** del mismo artista con badge azul "🔄 VERSIÓN"
6. Desde aquí puedes:
   - Ver en qué medio físico está cada versión
   - Reproducir cada versión
   - Editar cada una individualmente

> **📌 Lógica automática**: Si marcas una canción como "Original", el sistema automáticamente convierte las otras versiones con el mismo nombre en "Covers" o "Versiones" según el artista.

---

##  ❓ Troubleshooting - Solución de Problemas

### Problema: No me aparece una canción que acabo de agregar

**Posibles causas y soluciones:**

1. **Recarga la página**: Presiona F5 para refrescar
2. **Verifica la búsqueda**: Asegúrate de no tener filtros activos que la oculten
3. **Revisa el medio correcto**: Ve a **Medios** y verifica que esté en el cassette/CD correcto

---

### Problema: No se guarda el álbum que asigno a una canción

**Soluciones:**

1. **Verifica que hayas dado clic en "Guardar"**: El modal debe cerrarse automáticamente
2. **Comprueba las notificaciones**: Si hay un error, aparecerá en la parte superior
3. **Edita la canción nuevamente**: Ve a editar y vuelve a asignar el álbum

---

### Problema: La portada del álbum no se muestra

**Posibles causas:**

1. **El archivo es muy grande**: Intenta con una imagen de menor tamaño (menor a 2MB)
2. **Formato no soportado**: Usa JPG, PNG o WEBP
3. **Recarga la página**: Presiona F5 después de subir la imagen

---

### Problema: No puedo acceder desde mi celular

**Soluciones:**

1. **Verifica la WiFi**: Ambos dispositivos deben estar en la **misma red**
2. **Configura el Firewall**: 
   - Abre PowerShell como administrador
   - Ejecuta: `New-NetFirewallRule -DisplayName "Catálogo de Música" -Direction Inbound -Protocol TCP -LocalPort 5179 -Action Allow`
3. **Usa la IP correcta**: En la consola del servidor, copia la URL que empieza con `192.168...`

---

### Problema: El reproductor no suena / no aparece

**Causas:**

1. **No hay archivos de audio**: El reproductor solo funciona si tienes archivos de audio vinculados
2. **Ruta incorrecta**: Los archivos de audio deben estar en la ubicación configurada
3. **Formato no soportado**: Los navegadores soportan MP3, WAV, OGG, FLAC

---

### Problema: Duplicados o canciones repetidas

**Solución:**

1. Ve a la sección **Canciones**
2. Busca la canción duplicada
3. Si es un cover o versión, márcala correctamente con el sistema de versiones:
   - Abre el modal de la canción
   - Marca cuál es la **original**
   - Las demás se convertirán automáticamente en covers/versiones

---

### Problema: La aplicación se ve mal o descuadrada

**Soluciones:**

1. **Actualiza el navegador**: Usa Chrome, Edge, Firefox o Safari actualizados
2. **Limpia caché**: Presiona Ctrl+Shift+Delete y elimina caché
3. **Prueba en modo incógnito**: Abre una ventana privada

---

### Problema: No puedo eliminar un medio/álbum/canción

**Soluciones:**

1. **Confirma la acción**: Asegúrate de hacer clic en "Confirmar" en el diálogo
2. **Verifica permisos**: Asegúrate de que la base de datos no esté en solo lectura
3. **Cierra otros programas**: Si tienes la base de datos abierta en otro programa, ciérralo

---

## 📞 Consejos y Buenas Prácticas

✅ **Asigna siempre un álbum a tus canciones**: Esto facilita la organización y navegación  
✅ **Usa nombres consistentes**: Escribe los nombres de artistas siempre igual para evitar duplicados  
✅ **Marca las versiones correctamente**: Diferencia entre originales, covers y versiones  
✅ **Sube portadas de buena calidad**: Las imágenes se ven mejor si son cuadradas y de al menos 500x500px  
✅ **Revisa las notificaciones**: La campana 🔔 te avisará de canciones sin álbum asignado  
✅ **Haz respaldos regulares**: Copia el archivo `catalogo.db` periódicamente como backup  

---

## 🎉 ¡Disfruta Organizando tu Música!

Si tienes dudas adicionales o encuentras algún problema, revisa el **README Técnico** para más detalles sobre la arquitectura interna.
