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
                            <div class="resumen-icono">${ICONOS.iconoCassette}</div>
                            <div class="resumen-info">
                                <span class="resumen-numero">${this.formatearNumero(stats.totalCassettes)}</span>
                                <span class="resumen-label">Cassettes</span>
                            </div>
                        </div>
                        <div class="resumen-card">
                            <div class="resumen-icono">${ICONOS.iconoCD}</div>
                            <div class="resumen-info">
                                <span class="resumen-numero">${this.formatearNumero(stats.totalCds)}</span>
                                <span class="resumen-label">CDs</span>
                            </div>
                        </div>
                        <div class="resumen-card">
                            <div class="resumen-icono">${ICONOS.iconoMusica}</div>
                            <div class="resumen-info">
                                <span class="resumen-numero">${this.formatearNumero((stats.totalTemasCassette || 0) + (stats.totalTemasCd || 0))}</span>
                                <span class="resumen-label">Canciones</span>
                            </div>
                        </div>
                        <div class="resumen-card">
                            <div class="resumen-icono">${ICONOS.iconoInterprete}</div>
                            <div class="resumen-info">
                                <span class="resumen-numero">${this.formatearNumero(stats.totalInterpretes)}</span>
                                <span class="resumen-label">Intérpretes</span>
                            </div>
                        </div>
                    `;
                }
            } catch (err) {
                console.error('Error cargando estadísticas:', err);
                const container = document.getElementById('resumenColeccion');
                if (container) {
                    container.innerHTML = '<div class="error-mensaje">No se pudo cargar el resumen</div>';
                }
            }
        },

        formatearNumero(num) {
            return new Intl.NumberFormat('es-ES').format(num || 0);
        },

        /**
         * Genera código QR para acceso desde móvil
         */
        async generateQR() {
            const qrContainer = document.getElementById('codigoQR');
            if (qrContainer && typeof QRCode !== 'undefined') {
                qrContainer.innerHTML = '';
                
                try {
                    // Obtener IPs del servidor
                    const resp = await fetch('/api/red');
                    const data = await resp.json();
                    
                    // Usar la primera IP de la red local (no localhost)
                    let url = window.location.origin;
                    if (data.ips && data.ips.length > 0) {
                        // Preferir IP que empiece con 192.168
                        const ipLocal = data.ips.find(ip => ip.startsWith('192.168')) || data.ips[0];
                        url = `http://${ipLocal}:${data.puerto}`;
                    }
                    
                    new QRCode(qrContainer, {
                        text: url,
                        width: 180,
                        height: 180,
                        colorDark: '#1e293b',
                        colorLight: '#ffffff',
                        correctLevel: QRCode.CorrectLevel.M
                    });
                } catch (err) {
                    // Fallback a la URL actual
                    const url = window.location.origin;
                    new QRCode(qrContainer, {
                        text: url,
                        width: 180,
                        height: 180,
                        colorDark: '#1e293b',
                        colorLight: '#ffffff'
                    });
                }
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
