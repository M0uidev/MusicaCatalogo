// ============================================
// PAGE INITIALIZERS - Funciones de inicialización por página
// ============================================

(function() {
    'use strict';

    // Sistema de inicializadores de páginas
    window.PageInitializers = {
        
        /**
         * Inicializador para index.html
         */
        index() {
            console.log('Inicializando página: index');
            
            // Cargar estadísticas
            this.loadStats();
            
            // Generar código QR si existe la biblioteca
            this.generateQR();
            
            return {
                cleanup: () => {
                    // Cleanup si es necesario
                }
            };
        },

        /**
         * Carga estadísticas del catálogo
         */
        async loadStats() {
            try {
                const resp = await fetch('/api/estadisticas');
                if (!resp.ok) return;
                
                const stats = await resp.json();
                const container = document.getElementById('resumenColeccion');
                
                if (container) {
                    container.innerHTML = `
                        <div class="resumen-card">
                            <div class="resumen-numero">${stats.totalCanciones || 0}</div>
                            <div class="resumen-label">Canciones</div>
                        </div>
                        <div class="resumen-card">
                            <div class="resumen-numero">${stats.totalInterpretes || 0}</div>
                            <div class="resumen-label">Intérpretes</div>
                        </div>
                        <div class="resumen-card">
                            <div class="resumen-numero">${stats.totalCassettes || 0}</div>
                            <div class="resumen-label">Cassettes</div>
                        </div>
                        <div class="resumen-card">
                            <div class="resumen-numero">${stats.totalCds || 0}</div>
                            <div class="resumen-label">CDs</div>
                        </div>
                    `;
                }
            } catch (err) {
                console.error('Error cargando estadísticas:', err);
            }
        },

        /**
         * Genera código QR para acceso desde móvil
         */
        generateQR() {
            const qrContainer = document.getElementById('codigoQR');
            if (qrContainer && typeof QRCode !== 'undefined') {
                qrContainer.innerHTML = '';
                const localUrl = window.location.origin.replace('/app.html', '');
                new QRCode(qrContainer, {
                    text: localUrl,
                    width: 200,
                    height: 200
                });
            }
        },

        /**
         * Inicializador genérico para páginas que no necesitan init especial
         */
        buscar() {
            console.log('Inicializando página: buscar');
            // El script inline de buscar.html se ejecutará automáticamente
            return { cleanup: () => {} };
        },

        albumes() {
            console.log('Inicializando página: albumes');
            return { cleanup: () => {} };
        },

        medios() {
            console.log('Inicializando página: medios');
            return { cleanup: () => {} };
        },

        medio() {
            console.log('Inicializando página: medio');
            return { cleanup: () => {} };
        },

        interpretes() {
            console.log('Inicializando página: interpretes');
            return { cleanup: () => {} };
        },

        interprete() {
            console.log('Inicializando página: interprete');
            return { cleanup: () => {} };
        },

        cancion() {
            console.log('Inicializando página: cancion');
            return { cleanup: () => {} };
        },

        'perfil-cancion'() {
            console.log('Inicializando página: perfil-cancion');
            return { cleanup: () => {} };
        },

        estadisticas() {
            console.log('Inicializando página: estadisticas');
            return { cleanup: () => {} };
        },

        diagnostico() {
            console.log('Inicializando página: diagnostico');
            return { cleanup: () => {} };
        },

        duplicados() {
            console.log('Inicializando página: duplicados');
            return { cleanup: () => {} };
        }
    };

})();
