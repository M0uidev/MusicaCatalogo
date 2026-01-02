// ============================================
// CATÁLOGO DE MÚSICA - JAVASCRIPT CON ICONOS SVG
// ============================================

// ============================================
// ICONOS SVG PARA TIPOS DE CINTA Y FORMATOS
// ============================================
const ICONOS = {
    // Cassette Normal (Type I) - Óxido de hierro - Color gris
    cassetteNormal: `<svg viewBox="0 0 64 64" fill="none" xmlns="http://www.w3.org/2000/svg">
        <rect x="4" y="12" width="56" height="40" rx="4" fill="#e7e5e4" stroke="#78716c" stroke-width="2"/>
        <rect x="8" y="16" width="48" height="24" rx="2" fill="#f5f5f4" stroke="#a8a29e" stroke-width="1"/>
        <circle cx="20" cy="28" r="8" fill="#78716c"/>
        <circle cx="44" cy="28" r="8" fill="#78716c"/>
        <circle cx="20" cy="28" r="3" fill="#f5f5f4"/>
        <circle cx="44" cy="28" r="3" fill="#f5f5f4"/>
        <rect x="26" y="24" width="12" height="8" fill="#d6d3d1"/>
        <text x="32" y="48" text-anchor="middle" font-size="7" font-weight="bold" fill="#78716c">NORMAL</text>
    </svg>`,

    // Cassette Cromo (Type II) - Dióxido de cromo - Color cyan
    cassetteCromo: `<svg viewBox="0 0 64 64" fill="none" xmlns="http://www.w3.org/2000/svg">
        <rect x="4" y="12" width="56" height="40" rx="4" fill="#ecfeff" stroke="#0891b2" stroke-width="2"/>
        <rect x="8" y="16" width="48" height="24" rx="2" fill="#cffafe" stroke="#22d3ee" stroke-width="1"/>
        <circle cx="20" cy="28" r="8" fill="#0891b2"/>
        <circle cx="44" cy="28" r="8" fill="#0891b2"/>
        <circle cx="20" cy="28" r="3" fill="#ecfeff"/>
        <circle cx="44" cy="28" r="3" fill="#ecfeff"/>
        <rect x="26" y="24" width="12" height="8" fill="#67e8f9"/>
        <text x="32" y="48" text-anchor="middle" font-size="7" font-weight="bold" fill="#0891b2">CROMO</text>
    </svg>`,

    // Cassette FeCr (Type III) - Ferrocromo - Color violeta
    cassetteFecr: `<svg viewBox="0 0 64 64" fill="none" xmlns="http://www.w3.org/2000/svg">
        <rect x="4" y="12" width="56" height="40" rx="4" fill="#f5f3ff" stroke="#7c3aed" stroke-width="2"/>
        <rect x="8" y="16" width="48" height="24" rx="2" fill="#ede9fe" stroke="#a78bfa" stroke-width="1"/>
        <circle cx="20" cy="28" r="8" fill="#7c3aed"/>
        <circle cx="44" cy="28" r="8" fill="#7c3aed"/>
        <circle cx="20" cy="28" r="3" fill="#f5f3ff"/>
        <circle cx="44" cy="28" r="3" fill="#f5f3ff"/>
        <rect x="26" y="24" width="12" height="8" fill="#c4b5fd"/>
        <text x="32" y="48" text-anchor="middle" font-size="7" font-weight="bold" fill="#7c3aed">Fe-Cr</text>
    </svg>`,

    // Cassette Metal (Type IV) - Metal puro - Color dorado
    cassetteMetal: `<svg viewBox="0 0 64 64" fill="none" xmlns="http://www.w3.org/2000/svg">
        <rect x="4" y="12" width="56" height="40" rx="4" fill="#fefce8" stroke="#ca8a04" stroke-width="2"/>
        <rect x="8" y="16" width="48" height="24" rx="2" fill="#fef9c3" stroke="#facc15" stroke-width="1"/>
        <circle cx="20" cy="28" r="8" fill="#ca8a04"/>
        <circle cx="44" cy="28" r="8" fill="#ca8a04"/>
        <circle cx="20" cy="28" r="3" fill="#fef9c3"/>
        <circle cx="44" cy="28" r="3" fill="#fef9c3"/>
        <rect x="26" y="24" width="12" height="8" fill="#fde047"/>
        <text x="32" y="48" text-anchor="middle" font-size="7" font-weight="bold" fill="#ca8a04">METAL</text>
    </svg>`,

    // CD - Compact Disc - Color azul
    cd: `<svg viewBox="0 0 64 64" fill="none" xmlns="http://www.w3.org/2000/svg">
        <circle cx="32" cy="32" r="26" fill="url(#cdGradient)" stroke="#3b82f6" stroke-width="2"/>
        <circle cx="32" cy="32" r="8" fill="#f8fafc" stroke="#3b82f6" stroke-width="1"/>
        <circle cx="32" cy="32" r="4" fill="#3b82f6"/>
        <path d="M32 10 A22 22 0 0 1 54 32" stroke="#93c5fd" stroke-width="1" fill="none" opacity="0.5"/>
        <path d="M32 14 A18 18 0 0 1 50 32" stroke="#bfdbfe" stroke-width="1" fill="none" opacity="0.5"/>
        <defs>
            <linearGradient id="cdGradient" x1="6" y1="6" x2="58" y2="58">
                <stop offset="0%" stop-color="#dbeafe"/>
                <stop offset="50%" stop-color="#eff6ff"/>
                <stop offset="100%" stop-color="#dbeafe"/>
            </linearGradient>
        </defs>
    </svg>`,

    // Cassette genérico (sin tipo específico)
    cassette: `<svg viewBox="0 0 64 64" fill="none" xmlns="http://www.w3.org/2000/svg">
        <rect x="4" y="12" width="56" height="40" rx="4" fill="#f3f4f6" stroke="#6b7280" stroke-width="2"/>
        <rect x="8" y="16" width="48" height="24" rx="2" fill="#e5e7eb" stroke="#9ca3af" stroke-width="1"/>
        <circle cx="20" cy="28" r="8" fill="#6b7280"/>
        <circle cx="44" cy="28" r="8" fill="#6b7280"/>
        <circle cx="20" cy="28" r="3" fill="#f3f4f6"/>
        <circle cx="44" cy="28" r="3" fill="#f3f4f6"/>
        <rect x="26" y="24" width="12" height="8" fill="#d1d5db"/>
    </svg>`,

    // Iconos de fuente de grabación
    fuenteFM: `<svg viewBox="0 0 64 64" fill="none" xmlns="http://www.w3.org/2000/svg">
        <rect x="8" y="16" width="48" height="32" rx="4" fill="#fef3c7" stroke="#d97706" stroke-width="2"/>
        <rect x="14" y="22" width="20" height="12" rx="2" fill="#fcd34d"/>
        <circle cx="44" cy="28" r="6" fill="#f59e0b"/>
        <line x1="14" y1="42" x2="50" y2="42" stroke="#d97706" stroke-width="2"/>
        <text x="32" y="54" text-anchor="middle" font-size="8" font-weight="bold" fill="#d97706">FM</text>
    </svg>`,

    fuenteCD: `<svg viewBox="0 0 64 64" fill="none" xmlns="http://www.w3.org/2000/svg">
        <circle cx="32" cy="28" r="20" fill="#dbeafe" stroke="#3b82f6" stroke-width="2"/>
        <circle cx="32" cy="28" r="6" fill="white" stroke="#3b82f6"/>
        <circle cx="32" cy="28" r="2" fill="#3b82f6"/>
        <text x="32" y="56" text-anchor="middle" font-size="8" font-weight="bold" fill="#3b82f6">CD</text>
    </svg>`,

    fuenteLP: `<svg viewBox="0 0 64 64" fill="none" xmlns="http://www.w3.org/2000/svg">
        <circle cx="32" cy="28" r="22" fill="#1f2937" stroke="#374151" stroke-width="2"/>
        <circle cx="32" cy="28" r="8" fill="#dc2626" stroke="#991b1b"/>
        <circle cx="32" cy="28" r="2" fill="#1f2937"/>
        <circle cx="32" cy="28" r="18" fill="none" stroke="#374151" stroke-width="0.5"/>
        <circle cx="32" cy="28" r="14" fill="none" stroke="#374151" stroke-width="0.5"/>
        <text x="32" y="58" text-anchor="middle" font-size="8" font-weight="bold" fill="#1f2937">VINILO</text>
    </svg>`,

    fuenteMP3: `<svg viewBox="0 0 64 64" fill="none" xmlns="http://www.w3.org/2000/svg">
        <rect x="12" y="8" width="40" height="48" rx="6" fill="#e0e7ff" stroke="#6366f1" stroke-width="2"/>
        <rect x="18" y="14" width="28" height="20" rx="2" fill="#c7d2fe"/>
        <circle cx="32" cy="46" r="4" fill="#6366f1"/>
        <text x="32" y="28" text-anchor="middle" font-size="7" font-weight="bold" fill="#6366f1">MP3</text>
    </svg>`,

    fuenteInternet: `<svg viewBox="0 0 64 64" fill="none" xmlns="http://www.w3.org/2000/svg">
        <circle cx="32" cy="32" r="24" fill="#fef2f2" stroke="#ef4444" stroke-width="2"/>
        <path d="M32 8 v48 M8 32 h48" stroke="#ef4444" stroke-width="1"/>
        <ellipse cx="32" cy="32" rx="12" ry="24" fill="none" stroke="#ef4444" stroke-width="1"/>
        <ellipse cx="32" cy="32" rx="24" ry="12" fill="none" stroke="#ef4444" stroke-width="1"/>
        <polygon points="28,26 42,32 28,38" fill="#ef4444"/>
    </svg>`,

    // Iconos para resumen
    iconoCassette: `<svg viewBox="0 0 64 64" fill="none" xmlns="http://www.w3.org/2000/svg">
        <rect x="4" y="12" width="56" height="40" rx="4" fill="#fef3c7" stroke="#d97706" stroke-width="2"/>
        <rect x="8" y="16" width="48" height="24" rx="2" fill="#fef9c3" stroke="#fcd34d" stroke-width="1"/>
        <circle cx="20" cy="28" r="8" fill="#d97706"/>
        <circle cx="44" cy="28" r="8" fill="#d97706"/>
        <circle cx="20" cy="28" r="3" fill="#fef9c3"/>
        <circle cx="44" cy="28" r="3" fill="#fef9c3"/>
        <rect x="26" y="24" width="12" height="8" fill="#fde047"/>
    </svg>`,

    iconoCD: `<svg viewBox="0 0 64 64" fill="none" xmlns="http://www.w3.org/2000/svg">
        <circle cx="32" cy="32" r="26" fill="#dbeafe" stroke="#3b82f6" stroke-width="2"/>
        <circle cx="32" cy="32" r="8" fill="white" stroke="#3b82f6"/>
        <circle cx="32" cy="32" r="3" fill="#3b82f6"/>
        <path d="M32 10 A22 22 0 0 1 54 32" stroke="#93c5fd" stroke-width="2" fill="none"/>
    </svg>`,

    iconoMusica: `<svg viewBox="0 0 64 64" fill="none" xmlns="http://www.w3.org/2000/svg">
        <path d="M44 12 L44 44 M20 16 L20 48" stroke="#059669" stroke-width="4" stroke-linecap="round"/>
        <circle cx="14" cy="48" r="8" fill="#059669"/>
        <circle cx="38" cy="44" r="8" fill="#059669"/>
        <path d="M20 16 L44 12" stroke="#059669" stroke-width="4"/>
    </svg>`,

    iconoInterprete: `<svg viewBox="0 0 64 64" fill="none" xmlns="http://www.w3.org/2000/svg">
        <circle cx="32" cy="20" r="12" fill="#8b5cf6"/>
        <path d="M12 56 Q12 36 32 36 Q52 36 52 56" fill="#8b5cf6"/>
        <circle cx="48" cy="44" r="8" fill="#c4b5fd" stroke="#8b5cf6" stroke-width="2"/>
        <path d="M48 40 L48 48 M44 44 L52 44" stroke="#8b5cf6" stroke-width="2"/>
    </svg>`
};

// ============================================
// FUNCIONES DE ICONOS
// ============================================

/**
 * Obtiene el icono SVG según el tipo de cinta (bias)
 */
function obtenerIconoCinta(bias) {
    if (!bias) return ICONOS.cassette;
    const biasLower = bias.toLowerCase();
    if (biasLower.includes('metal')) return ICONOS.cassetteMetal;
    if (biasLower.includes('cromo') || biasLower.includes('chrome')) return ICONOS.cassetteCromo;
    if (biasLower.includes('fecr') || biasLower.includes('fe cr') || biasLower.includes('ferro')) return ICONOS.cassetteFecr;
    return ICONOS.cassetteNormal;
}

/**
 * Obtiene el icono SVG según el tipo de formato (cassette o CD)
 * @param {string} numFormato - Número del formato
 * @param {string} bias - Tipo de cinta (para cassettes)
 * @param {boolean} esCassette - Si es cassette (opcional, se detecta automáticamente)
 */
function obtenerIconoFormato(numFormato, bias, esCassette) {
    if (!numFormato) return ICONOS.cassette;
    // Si no se pasa esCassette, detectar automáticamente
    if (esCassette === undefined) {
        const num = numFormato.toLowerCase();
        esCassette = /^[nc]\d/.test(num);
    }
    if (!esCassette) return ICONOS.cd;
    return obtenerIconoCinta(bias);
}

/**
 * Obtiene el icono de fuente de grabación
 */
function obtenerIconoFuente(fuente) {
    if (!fuente) return '';
    const fuenteLower = fuente.toLowerCase();
    if (fuenteLower.includes('fm') || fuenteLower.includes('radio')) return ICONOS.fuenteFM;
    if (fuenteLower.includes('cd')) return ICONOS.fuenteCD;
    if (fuenteLower.includes('lp') || fuenteLower.includes('vinilo') || fuenteLower.includes('disco')) return ICONOS.fuenteLP;
    if (fuenteLower.includes('mp3')) return ICONOS.fuenteMP3;
    if (fuenteLower.includes('internet') || fuenteLower.includes('youtube')) return ICONOS.fuenteInternet;
    return '';
}

/**
 * Obtiene el nombre del tipo de cinta
 */
function obtenerNombreTipoCinta(bias) {
    if (!bias) return 'Normal';
    const biasLower = bias.toLowerCase();
    if (biasLower.includes('metal')) return 'Metal (Type IV)';
    if (biasLower.includes('cromo') || biasLower.includes('chrome')) return 'Cromo (Type II)';
    if (biasLower.includes('fecr') || biasLower.includes('fe cr')) return 'FeCr (Type III)';
    return 'Normal (Type I)';
}

/**
 * Obtiene la clase CSS para el badge de tipo
 */
function obtenerClaseTipo(numFormato, bias) {
    if (!numFormato) return 'normal';
    // Detectar si es CD: empieza con cd o mp
    const num = numFormato.toLowerCase();
    if (/^(cd|mp)/.test(num)) return 'cd';
    if (!bias) return 'normal';
    const biasLower = bias.toLowerCase();
    if (biasLower.includes('metal')) return 'metal';
    if (biasLower.includes('cromo') || biasLower.includes('chrome')) return 'cromo';
    if (biasLower.includes('fecr') || biasLower.includes('fe cr')) return 'fecr';
    return 'normal';
}

// ============================================
// UTILIDADES GLOBALES
// ============================================

function escapeHtml(text) {
    if (!text) return '';
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

function mostrarNotificacion(mensaje, tipo = 'info') {
    const container = document.getElementById('notificaciones');
    if (!container) return;
    
    const notif = document.createElement('div');
    notif.className = `notificacion ${tipo}`;
    notif.textContent = mensaje;
    container.appendChild(notif);

    setTimeout(() => {
        notif.classList.add('saliendo');
        setTimeout(() => notif.remove(), 300);
    }, 3000);
}

function formatearNumero(num) {
    return new Intl.NumberFormat('es-CL').format(num);
}

function obtenerParametro(nombre) {
    const params = new URLSearchParams(window.location.search);
    return params.get(nombre);
}

function htmlCargando() {
    return '<p class="cargando">Cargando...</p>';
}

function htmlError(mensaje) {
    return `<p class="error">${escapeHtml(mensaje)}</p>`;
}

// ============================================
// LEYENDA DE TIPOS DE CINTA (para mostrar en página)
// ============================================
function generarLeyendaTiposCinta() {
    return `
        <div class="leyenda-grid">
            <div class="leyenda-item">
                <div class="leyenda-icono">${ICONOS.cassetteNormal}</div>
                <div class="leyenda-texto">
                    <div class="leyenda-titulo">Normal (Type I)</div>
                    <div class="leyenda-desc">Óxido de hierro. Cinta estándar.</div>
                </div>
            </div>
            <div class="leyenda-item">
                <div class="leyenda-icono">${ICONOS.cassetteCromo}</div>
                <div class="leyenda-texto">
                    <div class="leyenda-titulo">Cromo (Type II)</div>
                    <div class="leyenda-desc">Dióxido de cromo. Alta fidelidad.</div>
                </div>
            </div>
            <div class="leyenda-item">
                <div class="leyenda-icono">${ICONOS.cassetteFecr}</div>
                <div class="leyenda-texto">
                    <div class="leyenda-titulo">FeCr (Type III)</div>
                    <div class="leyenda-desc">Ferrocromo. Combinación especial.</div>
                </div>
            </div>
            <div class="leyenda-item">
                <div class="leyenda-icono">${ICONOS.cassetteMetal}</div>
                <div class="leyenda-texto">
                    <div class="leyenda-titulo">Metal (Type IV)</div>
                    <div class="leyenda-desc">Metal puro. Máxima calidad.</div>
                </div>
            </div>
            <div class="leyenda-item">
                <div class="leyenda-icono">${ICONOS.cd}</div>
                <div class="leyenda-texto">
                    <div class="leyenda-titulo">CD</div>
                    <div class="leyenda-desc">Disco compacto digital.</div>
                </div>
            </div>
        </div>
    `;
}
