// ============================================
// ROUTER SPA - Sistema de navegación sin recargas
// ============================================

(function () {
    'use strict';

    const SPARouter = {
        currentPage: null,
        currentCleanup: null,
        mutationObserver: null,

        /**
         * Inicializa el router SPA
         */
        init() {
            // Interceptar clicks en links con data-spa-link
            document.addEventListener('click', (e) => {
                const link = e.target.closest('[data-spa-link]');
                if (link && link.href) {
                    e.preventDefault();
                    e.stopPropagation(); // Evitar que el evento se propague a padres con onclick
                    const url = new URL(link.href);
                    const fullPath = url.pathname + url.search;
                    this.navigateTo(fullPath);
                }
            });

            // Manejar botón atrás/adelante del navegador
            window.addEventListener('popstate', (e) => {
                const path = e.state?.path || window.location.pathname;
                this.loadPage(path, false); // false = no pushState
            });

            // Iniciar MutationObserver para detectar nuevos links dinámicamente
            this.initMutationObserver();

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
         * Espera a que termine una animación con fallback de timeout
         */
        waitAnimation(el, timeoutMs) {
            return new Promise((resolve) => {
                let done = false;
                const finish = () => {
                    if (done) return;
                    done = true;
                    el.removeEventListener('animationend', finish);
                    resolve();
                };
                el.addEventListener('animationend', finish, { once: true });
                setTimeout(finish, timeoutMs);
            });
        },

        isNavigating: false,

        /**
         * Navega a una nueva página con transición de zoom
         */
        async navigateTo(path) {
            if (this.isNavigating) return;

            const mainContainer = document.getElementById('app-main');
            if (!mainContainer) return;

            // Evitar navegar a la misma página
            if (this.currentPage === path) return;

            this.isNavigating = true;

            // Bloquear interacciones durante la transición
            mainContainer.style.pointerEvents = 'none';

            // Animación OUT (zoom out)
            mainContainer.classList.remove('page-in');
            mainContainer.classList.add('page-out');
            await this.waitAnimation(mainContainer, 220);

            // Cargar nueva página
            await this.loadPage(path, true);
        },

        /**
         * Carga el contenido de una página
         */
        async loadPage(path, pushState = true) {
            const mainContainer = document.getElementById('app-main');
            if (!mainContainer) {
                console.error('Contenedor #app-main no encontrado');
                return;
            }

            try {
                // Ejecutar cleanup de la página anterior
                if (this.currentCleanup && typeof this.currentCleanup === 'function') {
                    try {
                        this.currentCleanup();
                    } catch (err) {
                        console.warn('Error en cleanup:', err);
                    }
                    this.currentCleanup = null;
                }

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

                // Actualizar historial ANTES de actualizar contenido
                if (pushState) {
                    window.history.pushState({ path }, '', path);
                }

                // Actualizar página actual
                this.currentPage = path;

                // Limpiar scripts de página anterior
                this.cleanupPageScripts();

                // Swap del contenido
                mainContainer.innerHTML = html;

                // Animación IN (zoom in)
                mainContainer.classList.remove('page-out');
                mainContainer.classList.add('page-in');
                await this.waitAnimation(mainContainer, 280);

                // Limpiar clases de animación
                mainContainer.classList.remove('page-in');

                // Ejecutar scripts inline que puedan estar en el contenido
                this.executeScripts(mainContainer);

                // Actualizar título de la página
                this.updateTitle(path);

                // Ejecutar inicializador de la página si existe
                this.initializePage(path);

                // Re-attachear event listeners para links dinámicos
                this.attachDynamicListeners(mainContainer);

                // Re-crear iconos de Lucide si está disponible
                if (window.lucide) {
                    window.lucide.createIcons();
                }

                // Scroll al inicio
                window.scrollTo(0, 0);

            } catch (error) {
                console.error('Error cargando página:', error);
                // Fallback: navegación tradicional
                location.href = path;
                return;
            } finally {
                // SIEMPRE restaurar interactividad
                mainContainer.classList.remove('page-out', 'page-in');
                mainContainer.style.pointerEvents = '';
                this.isNavigating = false;
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
         * Limpia los scripts de la página anterior
         */
        cleanupPageScripts() {
            // Remover modales huérfanos que se movieron al body
            document.querySelectorAll('.modal-versiones-moved, .modal-moved').forEach(m => m.remove());

            // Remover scripts temporales de páginas previas
            const oldScripts = document.querySelectorAll('script[data-page-script]');
            oldScripts.forEach(script => script.remove());

            // Lista de funciones globales que las páginas pueden crear
            // Se limpian para evitar conflictos al recargar la misma página
            const pageFunctions = [
                // buscar.html
                'abrirModalVersiones', 'cerrarModalVersiones', 'paginaAnterior', 'paginaSiguiente', 'limpiarFiltros',
                // cancion.html
                'cargarCancion', 'eliminarAudio', 'subirAudio', 'reproducirAudio',
                // albumes.html
                'cerrarModal', 'toggleSelectorArtistaNuevo', 'usarNuevoArtistaNuevo', 'crearAlbum',
                'cerrarModalCanciones', 'asignarCancionesSeleccionadas', 'cerrarModalEditar',
                'toggleSelectorArtistaEditar', 'usarNuevoArtistaEditar', 'guardarEdicionAlbum',
                'abrirModal', 'cambiarFiltroTipo', 'subirPortada', 'abrirModalEditarAlbum',
                'eliminarAlbum', 'abrirModalAgregarCanciones', 'cargarAlbum',
                // medios.html
                'abrirModalNuevoMedio', 'seleccionarOpcion', 'toggleSelectorVisual', 'crearNuevoMedio',
                // medio.html  
                'cargarMedio', 'eliminarMedio',
                // interprete.html
                'cargarInterprete'
            ];

            // Limpiar funciones globales de la página anterior
            // Usar try-catch porque algunas propiedades pueden no ser configurables
            pageFunctions.forEach(funcName => {
                try {
                    if (window[funcName]) {
                        delete window[funcName];
                    }
                } catch (e) {
                    // Si delete falla, asignar undefined
                    window[funcName] = undefined;
                }
            });
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
                    const scriptContent = oldScript.textContent;

                    // Crear nuevo elemento script
                    const newScript = document.createElement('script');
                    newScript.setAttribute('data-page-script', 'true');

                    // Envolver el contenido en una IIFE para aislar el scope
                    // PERO: permitir que las funciones globales se expongan
                    // Reemplazar declaraciones let/const por var para evitar errores de redeclaración
                    let wrappedContent = scriptContent
                        // Reemplazar let al inicio de línea o después de ; { o espacio
                        .replace(/(\s|^|;|\{)let\s+/g, '$1var ')
                        // Reemplazar const al inicio de línea o después de ; { o espacio
                        .replace(/(\s|^|;|\{)const\s+/g, '$1var ');

                    newScript.textContent = wrappedContent;

                    // Insertarlo en el body (no head) para poder limpiarlo después
                    document.body.appendChild(newScript);

                    // Remover el script original del contenedor
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
            // Procesar todo el documento, no solo el contenedor
            // Esto asegura que modales y contenido fuera de #app-main también se procesen
            const targetContainer = container || document;

            // Buscar todos los links dentro del contenedor
            const links = targetContainer.querySelectorAll('a[href]');
            links.forEach(link => {
                const href = link.getAttribute('href');

                // Si es link interno (empieza con / o es relativo .html)
                if (href && (href.startsWith('/') || href.endsWith('.html'))) {
                    // No tiene target blank ni download
                    if (!link.hasAttribute('target') && !link.hasAttribute('download')) {
                        // No agregar si ya tiene el atributo
                        if (!link.hasAttribute('data-spa-link')) {
                            link.setAttribute('data-spa-link', '');
                        }
                    }
                }
            });
        },

        /**
         * Inicializa MutationObserver para detectar nuevos links añadidos al DOM
         */
        initMutationObserver() {
            // Desconectar observer previo si existe
            if (this.mutationObserver) {
                this.mutationObserver.disconnect();
            }

            // Crear nuevo observer
            this.mutationObserver = new MutationObserver((mutations) => {
                mutations.forEach((mutation) => {
                    // Solo procesar nodos añadidos
                    if (mutation.addedNodes.length > 0) {
                        mutation.addedNodes.forEach((node) => {
                            // Solo procesar elementos (no text nodes)
                            if (node.nodeType === Node.ELEMENT_NODE) {
                                // Si el nodo es un link, procesarlo
                                if (node.tagName === 'A') {
                                    this.processLink(node);
                                }
                                // Buscar links dentro del nodo añadido
                                const links = node.querySelectorAll && node.querySelectorAll('a[href]');
                                if (links) {
                                    links.forEach(link => this.processLink(link));
                                }
                            }
                        });
                    }
                });
            });

            // Observar todo el body para cambios en el árbol DOM
            this.mutationObserver.observe(document.body, {
                childList: true,
                subtree: true
            });
        },

        /**
         * Procesa un link individual para añadirle data-spa-link si corresponde
         */
        processLink(link) {
            const href = link.getAttribute('href');

            // Si es link interno (empieza con / o es relativo .html)
            if (href && (href.startsWith('/') || href.endsWith('.html'))) {
                // No tiene target blank ni download
                if (!link.hasAttribute('target') && !link.hasAttribute('download')) {
                    // No agregar si ya tiene el atributo
                    if (!link.hasAttribute('data-spa-link')) {
                        link.setAttribute('data-spa-link', '');
                    }
                }
            }
        }
    };

    // Exponer globalmente
    window.SPARouter = SPARouter;

})();
