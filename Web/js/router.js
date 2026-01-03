// ============================================
// ROUTER SPA - Sistema de navegación sin recargas
// ============================================

(function() {
    'use strict';

    const SPARouter = {
        currentPage: null,
        currentCleanup: null,
        
        /**
         * Inicializa el router SPA
         */
        init() {
            // Interceptar clicks en links con data-spa-link
            document.addEventListener('click', (e) => {
                console.log('🔍 DEBUG click evento - target:', e.target);
                const link = e.target.closest('[data-spa-link]');
                console.log('🔍 DEBUG click evento - link encontrado:', link);
                if (link && link.href) {
                    e.preventDefault();
                    const url = new URL(link.href);
                    const fullPath = url.pathname + url.search;
                    console.log('🔍 DEBUG click interceptado - href:', link.href);
                    console.log('🔍 DEBUG click interceptado - fullPath:', fullPath);
                    this.navigateTo(fullPath);
                }
            });

            // Manejar botón atrás/adelante del navegador
            window.addEventListener('popstate', (e) => {
                const path = e.state?.path || window.location.pathname;
                this.loadPage(path, false); // false = no pushState
            });

            // Verificar si hay un hash en la URL (redirect del servidor)
            if (window.location.hash) {
                const targetPath = window.location.hash.substring(1); // Remove #
                window.history.replaceState({ path: targetPath }, '', targetPath);
                this.loadPage(targetPath, false);
                return;
            }

            // Cargar página inicial según la URL actual
            const initialPath = window.location.pathname === '/' || window.location.pathname === '/app.html' 
                ? '/index.html' 
                : window.location.pathname + window.location.search;
            
            this.loadPage(initialPath, true);
        },

        /**
         * Navega a una nueva página
         */
        navigateTo(path) {
            this.loadPage(path, true);
        },

        /**
         * Carga el contenido de una página
         */
        async loadPage(path, pushState = true) {
            console.log('🔍 DEBUG router.loadPage - path recibido:', path);
            console.log('🔍 DEBUG router.loadPage - pushState:', pushState);
            
            const mainContainer = document.getElementById('app-main');
            if (!mainContainer) {
                console.error('Contenedor #app-main no encontrado');
                return;
            }

            // Evitar recargar la misma página
            if (this.currentPage === path && pushState) {
                return;
            }

            try {
                // Ejecutar cleanup de la página anterior
                if (this.currentCleanup) {
                    try {
                        this.currentCleanup();
                    } catch (err) {
                        console.warn('Error en cleanup:', err);
                    }
                    this.currentCleanup = null;
                }

                // Mostrar indicador de carga
                mainContainer.style.opacity = '0.5';

                // Hacer request AJAX para obtener el contenido
                const response = await fetch(path, {
                    headers: {
                        'X-Requested-With': 'XMLHttpRequest'
                    }
                });

                if (!response.ok) {
                    throw new Error(`HTTP ${response.status}`);
                }

                let html = await response.text();

                // Si el servidor devolvió la página completa, extraer solo el contenido
                html = this.extractMainContent(html);

                // Actualizar historial ANTES de actualizar contenido y ejecutar scripts
                // Esto asegura que window.location.search tenga los parámetros correctos
                if (pushState) {
                    window.history.pushState({ path }, '', path);
                }

                // Actualizar página actual
                this.currentPage = path;

                // Actualizar contenido
                mainContainer.innerHTML = html;
                mainContainer.style.opacity = '1';

                // Ejecutar scripts inline que puedan estar en el contenido
                this.executeScripts(mainContainer);

                // Actualizar título de la página
                this.updateTitle(path);

                // Scroll al inicio
                window.scrollTo(0, 0);

                // Ejecutar inicializador de la página si existe
                this.initializePage(path);

                // Re-attachear event listeners para links dinámicos
                this.attachDynamicListeners(mainContainer);

            } catch (error) {
                console.error('Error cargando página:', error);
                mainContainer.innerHTML = `
                    <div style="text-align: center; padding: 4rem 2rem;">
                        <h2>❌ Error</h2>
                        <p>No se pudo cargar la página</p>
                        <button onclick="SPARouter.navigateTo('/index.html')" class="btn">Volver al inicio</button>
                    </div>
                `;
                mainContainer.style.opacity = '1';
            }
        },

        /**
         * Extrae el contenido principal si el servidor devolvió HTML completo
         */
        extractMainContent(html) {
            // Si no tiene etiqueta <html>, asumimos que ya es contenido parcial
            if (!html.includes('<html')) {
                return html;
            }

            // Crear un parser temporal
            const parser = new DOMParser();
            const doc = parser.parseFromString(html, 'text/html');

            // Buscar el contenedor principal o el body
            const main = doc.querySelector('main.container') || 
                         doc.querySelector('.container') || 
                         doc.querySelector('main') ||
                         doc.body;

            if (!main) return html;

            // Extraer el contenido del main
            let content = main.innerHTML;

            // Extraer scripts inline (que no tienen src) del documento completo
            const scripts = Array.from(doc.querySelectorAll('script:not([src])'));
            const scriptContent = scripts.map(s => s.outerHTML).join('\n');

            return content + '\n' + scriptContent;
        },

        /**
         * Actualiza el título de la página
         */
        updateTitle(path) {
            const titles = {
                '/index.html': 'Inicio',
                '/buscar.html': 'Canciones',
                '/albumes.html': 'Álbumes',
                '/medios.html': 'Medios',
                '/medio.html': 'Detalle de Medio',
                '/interpretes.html': 'Intérpretes',
                '/interprete.html': 'Detalle de Intérprete',
                '/cancion.html': 'Detalle de Canción',
                '/perfil-cancion.html': 'Perfil de Canción',
                '/estadisticas.html': 'Estadísticas',
                '/diagnostico.html': 'Diagnóstico',
                '/duplicados.html': 'Duplicados'
            };

            const pageName = path.split('?')[0];
            const title = titles[pageName] || 'Catálogo de Música';
            document.title = `${title} - Catálogo de Música`;
        },

        /**
         * Ejecuta los scripts inline del contenido cargado
         */
        executeScripts(container) {
            const scripts = container.querySelectorAll('script');
            scripts.forEach((oldScript) => {
                // Solo ejecutar scripts inline (sin src)
                if (oldScript.src) {
                    return;
                }

                try {
                    // Crear nuevo elemento script que se ejecuta en scope global
                    // Esto permite que funciones onclick y event handlers funcionen
                    const newScript = document.createElement('script');
                    newScript.textContent = oldScript.textContent;
                    
                    // Insertarlo temporalmente en el head para ejecución global
                    document.head.appendChild(newScript);
                    
                    // Remover ambos scripts
                    setTimeout(() => newScript.remove(), 0);
                    oldScript.remove();
                } catch (err) {
                    console.error('Error ejecutando script:', err);
                }
            });
        },

        /**
         * Inicializa la página cargada
         */
        initializePage(path) {
            // Obtener nombre de la página sin query string
            const pageName = path.split('?')[0].replace('/', '').replace('.html', '');
            
            // Buscar inicializador en window.PageInitializers
            if (window.PageInitializers && window.PageInitializers[pageName]) {
                try {
                    const result = window.PageInitializers[pageName]();
                    // Guardar función de cleanup si existe
                    if (result && typeof result.cleanup === 'function') {
                        this.currentCleanup = result.cleanup;
                    }
                } catch (err) {
                    console.error(`Error inicializando página ${pageName}:`, err);
                }
            }

            // Cargar notificaciones (común a todas las páginas)
            if (typeof cargarNotificaciones === 'function') {
                cargarNotificaciones();
            }
        },

        /**
         * Attachea event listeners a links dinámicos dentro del contenedor
         */
        attachDynamicListeners(container) {
            // Buscar todos los links dentro del contenedor
            const links = container.querySelectorAll('a[href]');
            links.forEach(link => {
                const href = link.getAttribute('href');
                
                // Si es link interno (empieza con / o es relativo .html)
                if (href && (href.startsWith('/') || href.endsWith('.html'))) {
                    // No tiene target blank ni download
                    if (!link.hasAttribute('target') && !link.hasAttribute('download')) {
                        link.setAttribute('data-spa-link', '');
                    }
                }
            });
        }
    };

    // Exponer globalmente
    window.SPARouter = SPARouter;

})();
