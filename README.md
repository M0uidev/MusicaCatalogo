# Catálogo de Música

Aplicación web para gestionar y consultar un catálogo de música grabada en cassettes y CDs. Importa datos desde archivos CSV exportados de una base de datos Access y los almacena en SQLite para consultas rápidas.

## 🎵 Características

- **Búsqueda global**: Busca por nombre de tema, intérprete o número de formato
- **Explorador de formatos**: Lista todos los cassettes y CDs con sus temas
- **Detalle de formatos**: Muestra metadatos (marca, grabador, fecha, etc.) y lista de temas ordenada
- **Explorador de intérpretes**: Lista todos los artistas con conteo de temas
- **Detalle de intérpretes**: Muestra todos los temas de un artista agrupados por formato
- **Estadísticas**: Top intérpretes, conteos por formato, marcas más usadas
- **Diagnóstico**: Estado de importación, conteo de registros, información de red
- **Acceso móvil**: UI responsive, accesible desde cualquier dispositivo en la red local

## 📋 Requisitos

- .NET 8 SDK
- Archivos CSV en la carpeta Documentos del usuario:
  - `Ecualizador.csv`
  - `Formato.csv`
  - `Formato_grabado.csv`
  - `formato_grabadocd.csv`
  - `Fuente.csv`
  - `Grabador.csv`
  - `Interpretes.csv`
  - `Marca.csv`
  - `Temas.csv`
  - `Temascd.csv`
  - `Bias.csv`
  - `Modo.csv`
  - `Supresor.csv`

## 🚀 Ejecución

### Modo desarrollo

```bash
cd MusicaCatalogo
dotnet run
```

### Compilar y ejecutar

```bash
dotnet build
dotnet run --configuration Release
```

## 📦 Publicar como ejecutable único

Para generar un ejecutable `.exe` independiente que no requiere .NET instalado:

```bash
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

El ejecutable se generará en: `bin/Release/net8.0/win-x64/publish/MusicaCatalogo.exe`

### Copiar archivos necesarios

Después de publicar, copia la carpeta `Web` al mismo directorio del ejecutable:

```
publish/
├── MusicaCatalogo.exe
├── Web/
│   ├── index.html
│   ├── formatos.html
│   ├── formato.html
│   ├── interpretes.html
│   ├── interprete.html
│   ├── estadisticas.html
│   ├── diagnostico.html
│   ├── css/
│   │   └── estilos.css
│   └── js/
│       └── app.js
```

## 🌐 Acceso desde el celular

1. Ejecuta la aplicación en tu PC
2. Asegúrate de que el celular esté conectado a la misma red WiFi
3. En la consola se mostrarán las URLs disponibles, por ejemplo:
   ```
   ╔══════════════════════════════════════════════════════════════╗
   ║           CATÁLOGO DE MÚSICA - SERVIDOR INICIADO             ║
   ╠══════════════════════════════════════════════════════════════╣
   ║  Acceso local:     http://localhost:5179                     ║
   ╠══════════════════════════════════════════════════════════════╣
   ║  Acceso desde otros dispositivos (misma red WiFi):          ║
   ║    → http://192.168.1.100:5179                               ║
   ╚══════════════════════════════════════════════════════════════╝
   ```
4. Abre la URL con la IP de tu PC en el navegador del celular

### ⚠️ Firewall de Windows

Si no puedes acceder desde el celular, necesitas crear una regla de entrada en el Firewall de Windows:

1. Abre "Firewall de Windows Defender con seguridad avanzada"
2. Ve a "Reglas de entrada" → "Nueva regla..."
3. Selecciona "Puerto" → "TCP" → Puerto específico: `5179`
4. Selecciona "Permitir la conexión"
5. Marca todas las redes (Dominio, Privado, Público)
6. Nombre: "Catálogo de Música"

O ejecuta en PowerShell como administrador:

```powershell
New-NetFirewallRule -DisplayName "Catálogo de Música" -Direction Inbound -Protocol TCP -LocalPort 5179 -Action Allow
```

## 📁 Estructura del proyecto

```
MusicaCatalogo/
├── MusicaCatalogo.csproj     # Proyecto .NET
├── Program.cs                 # Punto de entrada, configuración del servidor
├── Data/
│   ├── BaseDatos.cs          # Gestión de SQLite y esquema
│   └── Entidades/
│       └── Entidades.cs      # Modelos de datos y DTOs
├── Services/
│   ├── ImportadorCSV.cs      # Importación de CSVs
│   ├── RepositorioMusica.cs  # Consultas a la base de datos
│   └── ServicioRed.cs        # Utilidades de red
├── Endpoints/
│   └── ConfiguracionEndpoints.cs  # API REST
└── Web/
    ├── index.html            # Página principal (búsqueda)
    ├── formatos.html         # Lista de formatos
    ├── formato.html          # Detalle de formato
    ├── interpretes.html      # Lista de intérpretes
    ├── interprete.html       # Detalle de intérprete
    ├── estadisticas.html     # Estadísticas
    ├── diagnostico.html      # Diagnóstico del sistema
    ├── css/
    │   └── estilos.css       # Estilos CSS
    └── js/
        └── app.js            # JavaScript cliente
```

## 🔌 API REST

| Endpoint | Descripción |
|----------|-------------|
| `GET /api/buscar?q={texto}` | Búsqueda global |
| `GET /api/formatos` | Lista de formatos |
| `GET /api/formatos/{numFormato}` | Detalle de formato |
| `GET /api/formatos/{numFormato}/temas` | Temas de un formato |
| `GET /api/interpretes` | Lista de intérpretes |
| `GET /api/interpretes/{id}` | Detalle de intérprete |
| `GET /api/estadisticas` | Estadísticas generales |
| `GET /api/diagnostico` | Información del sistema |
| `POST /api/reimportar` | Forzar reimportación de CSVs |
| `GET /api/red` | Información de red |

## 🗄️ Base de datos

La base de datos SQLite (`musica_catalogo.db`) se crea automáticamente en el directorio del ejecutable. Contiene:

- **Tablas de referencia**: ecualizador, formato, fuente, grabador, marca, bias, modo, supresor
- **Tabla maestra**: interpretes
- **Tablas de grabaciones**: formato_grabado (cassettes), formato_grabado_cd (CDs)
- **Tablas de temas**: temas (cassettes), temas_cd (CDs)

La importación se ejecuta automáticamente si:
- La base de datos no existe
- Los archivos CSV han cambiado desde la última importación

## 📝 Notas técnicas

- Los archivos CSV deben estar codificados en UTF-8
- El servidor escucha en todas las interfaces de red (0.0.0.0:5179)
- Las fechas se almacenan tal como están en los CSVs (sin normalización)
- Se crean índices en campos clave para optimizar búsquedas

## 🐛 Solución de problemas

**Error "No se encontraron archivos CSV"**
- Verifica que los archivos estén en la carpeta Documentos del usuario
- Los nombres de archivo deben coincidir exactamente (sensible a mayúsculas/minúsculas)

**No puedo acceder desde el celular**
- Verifica que ambos dispositivos estén en la misma red WiFi
- Configura el Firewall de Windows (ver instrucciones arriba)
- Prueba deshabilitando temporalmente el antivirus

**La importación falla**
- Revisa la consola para ver mensajes de error específicos
- Verifica que los archivos CSV no estén corruptos
- Usa la página de Diagnóstico para ver el estado de cada archivo
