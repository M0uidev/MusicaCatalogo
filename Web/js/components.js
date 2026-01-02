// ============================================
// CATÁLOGO DE MÚSICA - SISTEMA DE COMPONENTES
// Genera header, footer y notificaciones dinámicamente
// ============================================

/**
 * Configuración de navegación
 */
const NAV_ITEMS = [
    { href: 'index.html', label: 'Inicio', icon: '🏠' },
    { href: 'buscar.html', label: 'Canciones', icon: '🎵' },
    { href: 'albumes.html', label: 'Álbumes', icon: '💿' },
    { href: 'medios.html', label: 'Medios', icon: '📼' },
    { href: 'interpretes.html', label: 'Intérpretes', icon: '🎤' },
    { href: 'estadisticas.html', label: 'Estadísticas', icon: '📊' }
];

/**
 * Detecta la página actual basándose en la URL
 */
function getPaginaActual() {
    const path = window.location.pathname;
    const pagina = path.split('/').pop() || 'index.html';
    return pagina;
}

/**
 * Genera el HTML del header con navegación
 */
function generarHeader() {
    const paginaActual = getPaginaActual();
    
    const navLinks = NAV_ITEMS.map(item => {
        const isActivo = paginaActual === item.href || 
                        (paginaActual === '' && item.href === 'index.html');
        return `<a href="${item.href}" class="${isActivo ? 'activo' : ''}">${item.label}</a>`;
    }).join('\n            ');

    return `
    <header>
        <h1>Catálogo de Música</h1>
        <nav>
            ${navLinks}
            
            <div class="notif-container">
                <button class="notif-btn" onclick="toggleNotificaciones()" title="Notificaciones">
                    🔔
                    <span id="notifBadge" class="notif-badge"></span>
                </button>
                <div id="notifDropdown" class="notif-dropdown">
                    <div class="notif-header"><span>Problemas Pendientes</span></div>
                    <div id="notifList" class="notif-list"><div class="notif-empty">Cargando...</div></div>
                </div>
            </div>
        </nav>
    </header>`;
}

/**
 * Genera el HTML del footer
 */
function generarFooter() {
    const año = new Date().getFullYear();
    return `
    <footer>
        <p>Catálogo de Música Personal © ${año}</p>
    </footer>`;
}

/**
 * Genera el contenedor de notificaciones toast
 */
function generarNotificacionesContainer() {
    return `<div id="notificaciones"></div>`;
}

/**
 * Inicializa los componentes en la página
 * Debe llamarse al cargar el DOM
 */
function initComponents() {
    // Buscar marcadores de posición y reemplazarlos
    const headerPlaceholder = document.getElementById('header-component');
    const footerPlaceholder = document.getElementById('footer-component');
    const notifPlaceholder = document.getElementById('notificaciones-component');

    if (headerPlaceholder) {
        headerPlaceholder.outerHTML = generarHeader();
    }

    if (footerPlaceholder) {
        footerPlaceholder.outerHTML = generarFooter();
    }

    if (notifPlaceholder) {
        notifPlaceholder.outerHTML = generarNotificacionesContainer();
    }

    // Si no hay marcadores, insertar automáticamente
    if (!headerPlaceholder && !document.querySelector('header')) {
        document.body.insertAdjacentHTML('afterbegin', generarHeader());
    }

    if (!footerPlaceholder && !document.querySelector('footer')) {
        // Insertar antes del último script o al final del body
        const main = document.querySelector('main');
        if (main) {
            main.insertAdjacentHTML('afterend', generarFooter());
        } else {
            document.body.insertAdjacentHTML('beforeend', generarFooter());
        }
    }

    if (!notifPlaceholder && !document.getElementById('notificaciones')) {
        const footer = document.querySelector('footer');
        if (footer) {
            footer.insertAdjacentHTML('beforebegin', generarNotificacionesContainer());
        }
    }
}

/**
 * Genera breadcrumbs para páginas de detalle
 * @param {Array} items - Array de {label, href} o solo {label} para el último
 */
function generarBreadcrumbs(items) {
    if (!items || items.length === 0) return '';
    
    const crumbs = items.map((item, index) => {
        const isLast = index === items.length - 1;
        if (isLast || !item.href) {
            return `<span class="breadcrumb-actual">${escapeHtml(item.label)}</span>`;
        }
        return `<a href="${item.href}" class="breadcrumb-link">${escapeHtml(item.label)}</a>`;
    }).join('<span class="breadcrumb-sep">›</span>');

    return `<nav class="breadcrumbs">${crumbs}</nav>`;
}

/**
 * Inserta breadcrumbs al inicio del main
 * @param {Array} items - Array de {label, href}
 */
function insertarBreadcrumbs(items) {
    const main = document.querySelector('main');
    if (main) {
        const breadcrumbsHtml = generarBreadcrumbs(items);
        main.insertAdjacentHTML('afterbegin', breadcrumbsHtml);
    }
}

// ============================================
// ESTILOS DE COMPONENTES (inyectados dinámicamente)
// ============================================

const COMPONENT_STYLES = `
/* Breadcrumbs */
.breadcrumbs {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    margin-bottom: 1.5rem;
    font-size: 0.9rem;
    flex-wrap: wrap;
}

.breadcrumb-link {
    color: var(--color-texto-secundario);
    text-decoration: none;
    transition: color 0.2s;
}

.breadcrumb-link:hover {
    color: var(--color-primario);
}

.breadcrumb-sep {
    color: var(--color-texto-claro);
}

.breadcrumb-actual {
    color: var(--color-texto);
    font-weight: 500;
}
`;

/**
 * Inyecta estilos de componentes si no existen
 */
function injectComponentStyles() {
    if (document.getElementById('component-styles')) return;
    
    const style = document.createElement('style');
    style.id = 'component-styles';
    style.textContent = COMPONENT_STYLES;
    document.head.appendChild(style);
}

// ============================================
// AUTO-INICIALIZACIÓN
// ============================================

// Inicializar cuando el DOM esté listo
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
        injectComponentStyles();
        initComponents();
    });
} else {
    // DOM ya está listo
    injectComponentStyles();
    initComponents();
}

// Exportar funciones para uso manual
window.Components = {
    generarHeader,
    generarFooter,
    generarBreadcrumbs,
    insertarBreadcrumbs,
    initComponents,
    NAV_ITEMS
};
