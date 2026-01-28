// ============================================
// PLAYER APP MÓVIL - Versión Solo Lectura
// ============================================

import AudioMotionAnalyzer from './audioMotion-analyzer.min.js';

// Estado de la aplicación
const state = {
    currentTab: 'home',
    currentSong: null,
    isPlaying: false,
    playlist: [],
    currentIndex: -1,
    shuffle: false,
    repeat: 'off', // 'off', 'all', 'one'
    shuffledPlaylist: [],
    shuffledIndex: -1,
    audio: null,
    allSongs: [],
    allArtists: [],
    allAlbums: [],
    allMedios: [],
    libraryFilter: 'songs',
    searchQuery: '',
    stats: null,
    fullPlayerVisible: false,
    skipAttempts: 0,
    maxSkipAttempts: 10,
    // Configuración del usuario
    settings: {
        showSongsWithoutAudio: false, // Por defecto solo mostrar canciones con audio
        darkMode: true,
        enableCrossfade: true,
        crossfadeDuration: 10, // Segundos
        userName: ''
    },
    // Sistema de playlists
    userPlaylists: [],
    currentPlaylistId: null,
    // Sistema de favoritos (array de {id, tipo})
    favorites: [],
    // Lock para prevenir race conditions en reproducción
    isLoadingTrack: false,
    hasLoggedCurrentPlay: false,
    // Cola de "A continuación" persistente (para modo aleatorio)
    upNextQueue: [],
    // Crossfade
    bgAudio: null, // Segundo elemento de audio para transiciones
    isCrossfading: false,
    crossfadeInterval: null,
    // Visualizer
    visualizer: null
};

// ============================================
// SOURCE OF TRUTH: TRACK PLAYABILITY
// ============================================

/**
 * Source of truth para determinar si un track es reproducible.
 * Usar esta función en TODOS los lugares donde se necesite validar.
 * @param {Object} song - Objeto de canción
 * @returns {boolean}
 */
function isTrackPlayable(song) {
    if (!song) return false;
    const hasAudio = song.archivoAudio &&
        typeof song.archivoAudio === 'string' &&
        song.archivoAudio.trim() !== '';
    return hasAudio;
}

// ============================================
// STRUCTURED LOGGING SYSTEM
// ============================================

const PlayerLog = {
    _format(level, category, message, data = {}) {
        const timestamp = new Date().toISOString().substr(11, 12);
        const prefix = `[${timestamp}] [${level}] [${category}]`;
        return { prefix, message, data };
    },
    info(category, message, data = {}) {
        const { prefix } = this._format('INFO', category, message, data);
        console.log(`%c${prefix}%c ${message}`, 'color: #4CAF50; font-weight: bold', 'color: inherit', Object.keys(data).length ? data : '');
    },
    warn(category, message, data = {}) {
        const { prefix } = this._format('WARN', category, message, data);
        console.warn(`${prefix} ${message}`, Object.keys(data).length ? data : '');
    },
    error(category, message, data = {}) {
        const { prefix } = this._format('ERROR', category, message, data);
        console.error(`${prefix} ${message}`, Object.keys(data).length ? data : '');
    },
    debug(category, message, data = {}) {
        const { prefix } = this._format('DEBUG', category, message, data);
        console.debug(`${prefix} ${message}`, Object.keys(data).length ? data : '');
    },
    trackInfo(song) {
        if (!song) return { id: null, tipo: null, nombre: 'null' };
        return {
            id: song.id,
            tipo: song.tipo,
            nombre: song.tema || song.nombre || 'Sin título',
            hasAudio: isTrackPlayable(song)
        };
    }
};

// Iconos
const musicIcon = `<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 18V5l12-2v13"/><circle cx="6" cy="18" r="3"/><circle cx="18" cy="16" r="3"/></svg>`;
const userIcon = `<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>`;
const discIcon = `<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><circle cx="12" cy="12" r="3"/></svg>`;
const tapeIcon = `<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="2" y="6" width="20" height="12" rx="2"/><circle cx="8" cy="12" r="2"/><circle cx="16" cy="12" r="2"/><path d="M10 12h4"/></svg>`;
const searchIcon = `<svg xmlns="http://www.w3.org/2000/svg" width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/></svg>`;
const playIcon = `<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="currentColor"><polygon points="5 3 19 12 5 21 5 3"/></svg>`;
const pauseIcon = `<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="currentColor"><rect x="6" y="4" width="4" height="16"/><rect x="14" y="4" width="4" height="16"/></svg>`;
const listMusicIcon = `<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 15V6"/><path d="M18.5 18a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5Z"/><path d="M12 12H3"/><path d="M16 6H3"/><path d="M12 18H3"/></svg>`;

// Referencias DOM
let elements = {};

// ============================================
// INICIALIZACIÓN
// ============================================

function init() {
    PlayerLog.info('INIT', 'Iniciando Player App...');

    cacheElements();
    setupEventListeners();
    setupModals();
    initAudio();
    loadInitialData();
    restoreState();

    // Marcar tab inicial
    switchTab('home');

    // Recargar datos cuando el usuario vuelve al player (para detectar nuevos MP3s)
    setupVisibilityReload();

    PlayerLog.info('INIT', 'Player App inicializado correctamente');
}

/**
 * Configura recarga automática cuando la pestaña vuelve a estar visible.
 * Esto detecta nuevos MP3s subidos desde otras páginas como cancion.html
 */
function setupVisibilityReload() {
    let lastSongCount = state.allSongs.length;

    document.addEventListener('visibilitychange', async () => {
        if (document.visibilityState === 'visible') {
            PlayerLog.debug('VISIBILITY', 'Pestaña visible, verificando nuevos datos...');

            try {
                const res = await fetch('/api/canciones/todas');
                if (res.ok) {
                    const newSongs = await res.json();
                    const newPlayable = newSongs.filter(isTrackPlayable).length;
                    const oldPlayable = state.allSongs.filter(isTrackPlayable).length;

                    // Solo actualizar si hay cambios en canciones reproducibles
                    if (newPlayable !== oldPlayable || newSongs.length !== lastSongCount) {
                        PlayerLog.info('VISIBILITY', 'Cambios detectados, actualizando datos', {
                            antes: { total: lastSongCount, reproducibles: oldPlayable },
                            ahora: { total: newSongs.length, reproducibles: newPlayable }
                        });

                        state.allSongs = newSongs;
                        lastSongCount = newSongs.length;

                        // Actualizar UI si estamos en biblioteca
                        if (state.currentTab === 'library') {
                            renderLibrary();
                        }

                        showToast(`${newPlayable} canciones con audio`);
                    }
                }
            } catch (e) {
                PlayerLog.debug('VISIBILITY', 'Error verificando datos', { error: e.message });
            }
        }
    });
}




function cacheElements() {
    elements = {
        // Tabs
        tabBtns: document.querySelectorAll('.tab-btn'),
        views: document.querySelectorAll('.player-view'),

        // Home
        homeGreeting: document.getElementById('homeGreeting'),
        statsRow: document.getElementById('statsRow'),

        // Search
        searchInput: document.getElementById('searchInput'),
        searchResults: document.getElementById('searchResults'),

        // Library
        libraryFilters: document.querySelectorAll('.library-filter-btn'),
        libraryList: document.getElementById('libraryList'),

        // Queue
        queueNowPlaying: document.getElementById('queueNowPlaying'),
        queueUpNext: document.getElementById('queueUpNext'),

        // Mini Player
        miniPlayer: document.getElementById('miniPlayer'),
        miniCover: document.getElementById('miniCover'),
        miniTitle: document.getElementById('miniTitle'),
        miniArtist: document.getElementById('miniArtist'),
        miniPlayBtn: document.getElementById('miniPlayBtn'),
        miniPrevBtn: document.getElementById('miniPrevBtn'),
        miniNextBtn: document.getElementById('miniNextBtn'),
        miniShuffleBtn: document.getElementById('miniShuffleBtn'),
        miniRepeatBtn: document.getElementById('miniRepeatBtn'),
        miniProgressBar: document.getElementById('miniProgressBar'),
        miniProgressContainer: document.querySelector('.mini-player-progress'),

        // Full Player
        fullPlayer: document.getElementById('fullPlayer'),
        fullCover: document.getElementById('fullCover'),
        fullTitle: document.getElementById('fullTitle'),
        fullArtist: document.getElementById('fullArtist'),
        fullProgressFill: document.getElementById('fullProgressFill'),
        fullCurrentTime: document.getElementById('fullCurrentTime'),
        fullDuration: document.getElementById('fullDuration'),
        fullPlayBtn: document.getElementById('fullPlayBtn'),
        fullPrevBtn: document.getElementById('fullPrevBtn'),
        fullNextBtn: document.getElementById('fullNextBtn'),
        fullShuffleBtn: document.getElementById('fullShuffleBtn'),
        fullRepeatBtn: document.getElementById('fullRepeatBtn'),
        fullLikeBtn: document.getElementById('fullLikeBtn'),
        fullQueueBtn: document.getElementById('fullQueueBtn'),
        fullCloseBtn: document.getElementById('fullCloseBtn'),
        fullProgressBar: document.getElementById('fullProgressBar'),

        // Toast
        toast: document.getElementById('toast'),

        // Settings
        settingUserName: document.getElementById('settingUserName')
    };
}

function setupEventListeners() {
    // Tabs
    elements.tabBtns.forEach(btn => {
        btn.addEventListener('click', () => switchTab(btn.dataset.tab));
    });

    // Search
    elements.searchInput?.addEventListener('input', debounce(handleSearch, 300));

    // Configuración Visualizador
    const visualizerSelect = document.getElementById('settingVisualizerStyle');
    if (visualizerSelect) {
        visualizerSelect.value = localStorage.getItem('player_visualizer_style') || 'spotify';
        visualizerSelect.addEventListener('change', (e) => {
            const style = e.target.value;
            localStorage.setItem('player_visualizer_style', style);
            updateVisualizerStyle(style);
        });
    }

    // Library filters
    elements.libraryFilters.forEach(btn => {
        btn.addEventListener('click', () => switchLibraryFilter(btn.dataset.filter));
    });

    // Mini player
    elements.miniPlayer?.addEventListener('click', (e) => {
        if (!e.target.closest('.mini-player-btn')) {
            openFullPlayer();
        }
    });

    elements.miniPlayBtn?.addEventListener('click', (e) => {
        e.stopPropagation();
        togglePlayPause();
    });

    elements.miniPrevBtn?.addEventListener('click', (e) => {
        e.stopPropagation();
        playPrevious();
    });

    elements.miniNextBtn?.addEventListener('click', (e) => {
        e.stopPropagation();
        playNext();
    });

    elements.miniShuffleBtn?.addEventListener('click', (e) => {
        e.stopPropagation();
        toggleShuffle();
    });

    elements.miniRepeatBtn?.addEventListener('click', (e) => {
        e.stopPropagation();
        toggleRepeat();
    });

    // Full player
    elements.fullCloseBtn?.addEventListener('click', closeFullPlayer);
    elements.fullPlayBtn?.addEventListener('click', togglePlayPause);
    elements.fullPrevBtn?.addEventListener('click', playPrevious);
    elements.fullNextBtn?.addEventListener('click', playNext);
    elements.fullShuffleBtn?.addEventListener('click', toggleShuffle);
    elements.fullRepeatBtn?.addEventListener('click', toggleRepeat);
    elements.fullLikeBtn?.addEventListener('click', toggleLike);
    elements.fullQueueBtn?.addEventListener('click', () => {
        closeFullPlayer();
        switchTab('queue');
    });

    // Progress bar seek
    elements.fullProgressBar?.addEventListener('click', handleSeek);

    // Quick actions
    document.getElementById('btnShuffle')?.addEventListener('click', playShuffleAll);
    document.getElementById('btnFavorites')?.addEventListener('click', () => {
        // Cambiar a la biblioteca con filtro de favoritos
        state.libraryFilter = 'favorites';
        switchTab('library');
        renderLibrary();
        // Actualizar botones de filtro
        elements.libraryFilters.forEach(btn => {
            btn.classList.toggle('active', btn.dataset.filter === 'favorites');
        });
    });

    document.getElementById('btnShowAllAlbums')?.addEventListener('click', () => {
        switchTab('library');
        switchLibraryFilter('albums');
    });

    document.getElementById('btnShowAllArtists')?.addEventListener('click', () => {
        switchTab('library');
        switchLibraryFilter('artists');
    });

    // Settings inputs
    const settingShowWithoutAudio = document.getElementById('settingShowWithoutAudio');
    if (settingShowWithoutAudio) {
        settingShowWithoutAudio.addEventListener('change', (e) => {
            state.settings.showSongsWithoutAudio = e.target.checked;
            saveSettings();
            // Si estamos en biblioteca, recargar
            if (state.currentTab === 'library' && state.libraryFilter === 'songs') {
                renderLibrary();
            }
        });
    }

    const settingCrossfade = document.getElementById('settingCrossfade');
    if (settingCrossfade) {
        settingCrossfade.addEventListener('change', (e) => {
            state.settings.enableCrossfade = e.target.checked;
            saveSettings();
            showToast(e.target.checked ? 'Crossfade activado' : 'Crossfade desactivado');
        });
    }

    // Keyboard shortcuts (for testing)
    document.addEventListener('keydown', (e) => {
        if (e.code === 'Space' && !e.target.closest('input')) {
            e.preventDefault();
            togglePlayPause();
        }
    });

    // Progress bar draggable
    setupProgressDrag();
    setupMiniProgressDrag();

    // Swipe gestures en cover
    setupSwipeGestures();
    // Swipe gestures en mini player info
    setupMiniPlayerSwipe();
}

// ============================================
// PROGRESS BAR DRAGGABLE
// ============================================

function setupProgressDrag() {
    const progressBar = elements.fullProgressBar;
    if (!progressBar) return;

    let isDragging = false;

    const getProgress = (e) => {
        const rect = progressBar.getBoundingClientRect();
        const clientX = e.touches ? e.touches[0].clientX : e.clientX;
        const x = Math.max(0, Math.min(clientX - rect.left, rect.width));
        return x / rect.width;
    };

    const updateVisualProgress = (progress) => {
        if (elements.fullProgressFill) {
            elements.fullProgressFill.style.width = `${progress * 100}%`;
        }
        if (elements.fullCurrentTime && state.audio?.duration) {
            const time = progress * state.audio.duration;
            elements.fullCurrentTime.textContent = formatTime(time);
        }
    };

    const seek = (progress) => {
        if (state.audio && state.audio.duration) {
            state.audio.currentTime = progress * state.audio.duration;
        }
    };

    // Mouse events
    progressBar.addEventListener('mousedown', (e) => {
        isDragging = true;
        const progress = getProgress(e);
        updateVisualProgress(progress);
        progressBar.classList.add('dragging');
    });

    document.addEventListener('mousemove', (e) => {
        if (!isDragging) return;
        const progress = getProgress(e);
        updateVisualProgress(progress);
    });

    document.addEventListener('mouseup', (e) => {
        if (!isDragging) return;
        isDragging = false;
        const progress = getProgress(e);
        seek(progress);
        progressBar.classList.remove('dragging');
    });

    // Touch events
    progressBar.addEventListener('touchstart', (e) => {
        isDragging = true;
        const progress = getProgress(e);
        updateVisualProgress(progress);
        progressBar.classList.add('dragging');
    }, { passive: true });

    progressBar.addEventListener('touchmove', (e) => {
        if (!isDragging) return;
        const progress = getProgress(e);
        updateVisualProgress(progress);
    }, { passive: true });

    progressBar.addEventListener('touchend', (e) => {
        if (!isDragging) return;
        isDragging = false;
        const touch = e.changedTouches[0];
        const rect = progressBar.getBoundingClientRect();
        const x = Math.max(0, Math.min(touch.clientX - rect.left, rect.width));
        const progress = x / rect.width;
        seek(progress);
        progressBar.classList.remove('dragging');
    });
}

function setupMiniProgressDrag() {
    const progressBar = elements.miniProgressContainer;
    if (!progressBar) return;

    const getProgress = (e) => {
        const rect = progressBar.getBoundingClientRect();
        const clientX = e.touches ? e.touches[0].clientX : e.clientX;
        const x = Math.max(0, Math.min(clientX - rect.left, rect.width));
        return x / rect.width;
    };

    const seek = (progress) => {
        if (state.audio && state.audio.duration) {
            state.audio.currentTime = progress * state.audio.duration;
        }
    };

    // Click/Drag listener
    let isDragging = false;

    // Mouse
    progressBar.addEventListener('mousedown', (e) => {
        isDragging = true;
        seek(getProgress(e)); // Seek immediately on click
    });

    document.addEventListener('mousemove', (e) => {
        if (isDragging) {
            // Optional: visual update while dragging (handled by timeupdate mostly, but for smoothness could be added)
            // But simple seek on move works too if we want live scrubbing
        }
    });

    document.addEventListener('mouseup', (e) => {
        if (isDragging) {
            isDragging = false;
            // Final seek if needed
            const progress = getProgress(e);
            seek(progress);
        }
    });

    // Touch
    progressBar.addEventListener('touchstart', (e) => {
        isDragging = true;
        seek(getProgress(e));
    }, { passive: true });

    progressBar.addEventListener('touchmove', (e) => {
        if (isDragging) {
            seek(getProgress(e));
        }
    }, { passive: true });

    progressBar.addEventListener('touchend', () => {
        isDragging = false;
    });
}

function setupMiniPlayerSwipe() {
    const miniInfo = document.querySelector('.mini-player-info');
    if (!miniInfo) return;

    let startX = 0;
    let deltaX = 0;
    let isSwiping = false;
    const threshold = 60;

    miniInfo.addEventListener('touchstart', (e) => {
        startX = e.touches[0].clientX;
        deltaX = 0;
        isSwiping = false;
        miniInfo.style.transition = 'none';
    }, { passive: true });

    miniInfo.addEventListener('touchmove', (e) => {
        deltaX = e.touches[0].clientX - startX;
        isSwiping = true;

        // Simple resistance logic
        const maxDelta = 120;
        const resistance = 0.5;
        const limitedDelta = Math.sign(deltaX) * Math.min(Math.abs(deltaX) * resistance, maxDelta);

        miniInfo.style.transform = `translateX(${limitedDelta}px)`;
        miniInfo.style.opacity = Math.max(0.2, 1 - Math.abs(limitedDelta) / (maxDelta * 1.5));
    }, { passive: true });

    miniInfo.addEventListener('touchend', (e) => {
        miniInfo.style.transition = 'transform 0.3s ease, opacity 0.3s ease';

        if (Math.abs(deltaX) > threshold) {
            // Prevent click on parent if swiped
            if (e.cancelable) e.preventDefault();
            e.stopPropagation();

            const direction = deltaX > 0 ? 1 : -1;
            miniInfo.style.transform = `translateX(${direction * 200}px)`;
            miniInfo.style.opacity = '0';

            setTimeout(() => {
                if (direction < 0) {
                    playNext();
                } else {
                    playPrevious();
                }

                // Reset position invisibly
                miniInfo.style.transition = 'none';
                miniInfo.style.transform = `translateX(${-direction * 200}px)`;

                requestAnimationFrame(() => {
                    requestAnimationFrame(() => {
                        miniInfo.style.transition = 'transform 0.3s ease, opacity 0.3s ease';
                        miniInfo.style.transform = 'translateX(0)';
                        miniInfo.style.opacity = '1';
                    });
                });
            }, 200);
        } else {
            miniInfo.style.transform = 'translateX(0)';
            miniInfo.style.opacity = '1';

            // If it was a small drag, still prevent click
            if (isSwiping && Math.abs(deltaX) > 10) {
                if (e.cancelable) e.preventDefault();
                e.stopPropagation();
            }
        }
        isSwiping = false;
    }, { passive: false });
}

// ============================================
// SWIPE GESTURES
// ============================================

function setupSwipeGestures() {
    const coverContainer = document.querySelector('.full-player-cover-container');
    if (!coverContainer) return;

    let startX = 0;
    let startY = 0;
    let deltaX = 0;
    let isSwiping = false;
    const threshold = 80; // píxeles mínimos para activar swipe

    coverContainer.addEventListener('touchstart', (e) => {
        startX = e.touches[0].clientX;
        startY = e.touches[0].clientY;
        deltaX = 0;
        isSwiping = true;
        coverContainer.style.transition = 'none';
    }, { passive: true });

    coverContainer.addEventListener('touchmove', (e) => {
        if (!isSwiping) return;

        deltaX = e.touches[0].clientX - startX;
        const deltaY = e.touches[0].clientY - startY;

        // Solo swipe horizontal si el movimiento es más horizontal que vertical
        if (Math.abs(deltaX) > Math.abs(deltaY)) {
            // Limitar el desplazamiento y aplicar resistencia
            const maxDelta = 150;
            const resistance = 0.6;
            const limitedDelta = Math.sign(deltaX) * Math.min(Math.abs(deltaX) * resistance, maxDelta);

            coverContainer.style.transform = `translateX(${limitedDelta}px)`;
            coverContainer.style.opacity = 1 - Math.abs(limitedDelta) / (maxDelta * 2);
        }
    }, { passive: true });

    coverContainer.addEventListener('touchend', (e) => {
        if (!isSwiping) return;
        isSwiping = false;

        coverContainer.style.transition = 'transform 0.3s ease, opacity 0.3s ease';

        if (Math.abs(deltaX) > threshold) {
            // Completar animación de salida
            const direction = deltaX > 0 ? 1 : -1;
            coverContainer.style.transform = `translateX(${direction * 200}px)`;
            coverContainer.style.opacity = '0';

            setTimeout(() => {
                // Cambiar canción
                if (direction < 0) {
                    playNext();
                } else {
                    playPrevious();
                }

                // Animar entrada desde el lado opuesto
                coverContainer.style.transition = 'none';
                coverContainer.style.transform = `translateX(${-direction * 200}px)`;
                coverContainer.style.opacity = '0';

                requestAnimationFrame(() => {
                    coverContainer.style.transition = 'transform 0.3s ease, opacity 0.3s ease';
                    coverContainer.style.transform = 'translateX(0)';
                    coverContainer.style.opacity = '1';
                });
            }, 150);
        } else {
            // Regresar a posición original
            coverContainer.style.transform = 'translateX(0)';
            coverContainer.style.opacity = '1';
        }
    });
}

function initAudio() {
    state.audio = new Audio();
    state.bgAudio = new Audio(); // Audio de fondo para fade out

    // Configurar ambos elementos de audio
    [state.audio, state.bgAudio].forEach(audioEl => {
        setupAudioListeners(audioEl);
    });

    // Restaurar volumen
    const savedVolume = localStorage.getItem('playerVolume');
    const volume = savedVolume ? parseFloat(savedVolume) : 1.0;
    state.audio.volume = volume;
    state.bgAudio.volume = volume;

    // Guardar estado periódicamente mientras suena (cada 10 segundos)
    setInterval(() => {
        if (state.isPlaying && state.currentSong) {
            savePlayerState();
        }
    }, 10000);

    // Restaurar estado al iniciar
    restorePlayerState();

    // Inicializar visualizador
    initVisualizer();
}

const visualizerPresets = {
    spotify: {
        mode: 3, // 1/6 octave bands (~60 bands)
        barSpace: 0.6,
        roundBars: true,
        showPeaks: false,
        smoothing: 0.7,
        gradient: 'spotify', // Custom registered gradient
        overlay: true,
        bgAlpha: 0,
        fillAlpha: 0.8,
        reflexRatio: 0,
        lineWidth: 0,
        channelLayout: 'single',
        showScaleX: false,
        showScaleY: false
    },
    rainbow: {
        mode: 2, // 1/12th octave (~120 bands) for premium detail
        barSpace: 0.25,
        roundBars: true,
        colorMode: 'bar-level',
        gradient: 'prism',
        channelLayout: 'single',
        fftSize: 8192,
        smoothing: 0.65,
        minFreq: 30,
        maxFreq: 16000,
        weightingFilter: 'D',
        minDecibels: -85,
        maxDecibels: -25,
        overlay: true,
        bgAlpha: 0.7,
        showPeaks: true,
        reflexRatio: 0.4,
        reflexAlpha: 0.25,
        showBgColor: false,
        showScaleX: false,
        showScaleY: false
    }
};

function initVisualizer() {
    if (state.visualizer) return;

    try {
        const container = document.getElementById('visualizerContainer');
        if (!container) return;

        // Cargar preferencia guardada o default a 'spotify'
        const savedStyle = localStorage.getItem('player_visualizer_style') || 'spotify';

        if (savedStyle === 'none') {
            PlayerLog.info('VISUALIZER', 'Visualizador desactivado por configuración.');
            return;
        }

        PlayerLog.info('VISUALIZER', `Inicializando AudioMotion con estilo: ${savedStyle}...`);

        const config = visualizerPresets[savedStyle] || visualizerPresets.spotify;

        // Evitar pasar gradiente custom en el constructor porque aún no está registrado
        const initialConfig = { ...config };
        if (initialConfig.gradient === 'spotify') {
            delete initialConfig.gradient; // Usará default 'classic' temporalmente
        }

        state.visualizer = new AudioMotionAnalyzer(container, {
            source: state.audio,
            height: container.offsetHeight,
            width: container.offsetWidth,
            ...initialConfig
        });

        // Registrar gradiente Spotify si es necesario
        state.visualizer.registerGradient('spotify', {
            bgColor: '#0000',
            colorStops: [
                { pos: 0, color: '#1db954' },
                { pos: 1, color: '#1ed760' }
            ]
        });

        // Aplicar el gradiente correcto si era uno custom
        if (config.gradient === 'spotify') {
            state.visualizer.setOptions({ gradient: 'spotify' });
        }

        // Capar FPS en móvil
        if (/iPhone|iPad|iPod|Android/i.test(navigator.userAgent)) {
            state.visualizer.setOptions({ fpsLimit: 30 });
        }

        state.visualizer.toggleAnalyzer(false);

        window.addEventListener('resize', () => {
            if (state.visualizer && state.fullPlayerVisible) {
                state.visualizer.setCanvasSize(container.offsetWidth, container.offsetHeight);
            }
        });

    } catch (e) {
        PlayerLog.error('VISUALIZER', 'Error inicializando visualizador', { error: e.message });
    }
}

// Función auxiliar para cambiar el estilo dinámicamente
function updateVisualizerStyle(style) {
    if (style === 'none') {
        if (state.visualizer) {
            state.visualizer.disconnectInput(); // Liberar audio node
            // No destruimos la instancia completa para evitar errores de audio context, solo lo paramos
            state.visualizer.toggleAnalyzer(false);
            document.getElementById('visualizerContainer').innerHTML = ''; // Limpiar canvas
            state.visualizer = null;
        }
        return;
    }

    if (!state.visualizer) {
        initVisualizer(); // Si estaba apagado, encender
        // Si initVisualizer se salta por la config guardada (que aun no se actualiza en localStorage al llamar init), forzamos update
    }

    const config = visualizerPresets[style] || visualizerPresets.spotify;

    if (state.visualizer) {
        // Asegurar registro de gradiente
        state.visualizer.registerGradient('spotify', {
            bgColor: '#0000',
            colorStops: [
                { pos: 0, color: '#1db954' },
                { pos: 1, color: '#1ed760' }
            ]
        });

        state.visualizer.setOptions(config);

        if (style === 'spotify') {
            state.visualizer.setOptions({ gradient: 'spotify' });
        }

        if (state.fullPlayerVisible) {
            state.visualizer.toggleAnalyzer(true);
        }
    }
}




function setupAudioListeners(audioEl) {
    audioEl.addEventListener('timeupdate', (e) => {
        // Solo actualizar UI si es el audio principal
        if (e.target === state.audio) {
            updateProgress();

            const currentTime = state.audio.currentTime;

            // 1. Registrar el "Play" (conteo) a los 30s
            if (!state.hasLoggedCurrentPlay && currentTime >= 30) {
                logPlay(state.currentSong, Math.floor(currentTime));
            }

            // 2. Actualizar tiempo transcurrido (Heartbeat cada 10s)
            // Solo si ya tenemos un ID de log
            if (state.currentPlayLogId) {
                const now = Date.now();
                if (!state.lastPlayUpdate || now - state.lastPlayUpdate > 10000) {
                    updatePlayTime(state.currentPlayLogId, Math.floor(currentTime));
                    state.lastPlayUpdate = now;
                }
            }

        } else if (state.isCrossfading && e.target === state.audio) {
            updateProgress();
        }
    });

    audioEl.addEventListener('ended', (e) => {
        // Si termina el audio de fondo, solo limpiar
        if (e.target === state.bgAudio) {
            e.target.pause();
            e.target.currentTime = 0;
            return;
        }

        // Si es el audio principal, lógica normal de fin de canción
        if (e.target === state.audio) {
            // Log play if finished (and short song < 30s)
            if (!state.hasLoggedCurrentPlay && state.currentSong) {
                logPlay(state.currentSong, Math.floor(state.audio.currentTime));
            } else if (state.currentPlayLogId) {
                // Final update of time
                updatePlayTime(state.currentPlayLogId, Math.floor(state.audio.duration || state.audio.currentTime));
            }
            handleSongEnd();
        }
    });

    audioEl.addEventListener('play', (e) => {
        // Solo reaccionar si es el audio principal
        if (e.target === state.audio) {
            state.isPlaying = true;
            state.skipAttempts = 0;
            updatePlayButtons();
        }
    });

    audioEl.addEventListener('pause', (e) => {
        // Si pausamos el principal, actualizamos estado. 
        // Ignorar pausa del bgAudio (sucede al terminar crossfade)
        if (e.target === state.audio && !state.isCrossfading) {
            state.isPlaying = false;
            updatePlayButtons();
            savePlayerState();

            // Update stats time on pause
            if (state.currentPlayLogId) {
                updatePlayTime(state.currentPlayLogId, Math.floor(state.audio.currentTime));
            }
        }
    });

    audioEl.addEventListener('loadedmetadata', (e) => {
        if (e.target === state.audio) {
            updateDuration();
        }
    });

    audioEl.addEventListener('error', (e) => {
        // Solo manejar errores del principal por ahora
        if (e.target !== state.audio) return;

        const errorCode = state.audio.error?.code || 0;
        const errorMessages = {
            1: 'MEDIA_ERR_ABORTED - Carga abortada',
            2: 'MEDIA_ERR_NETWORK - Error de red',
            3: 'MEDIA_ERR_DECODE - Error de decodificación',
            4: 'MEDIA_ERR_SRC_NOT_SUPPORTED - Formato no soportado o archivo no encontrado'
        };

        PlayerLog.error('AUDIO', 'Error de audio element', {
            code: errorCode,
            message: errorMessages[errorCode] || 'Error desconocido',
            ...PlayerLog.trackInfo(state.currentSong)
        });

        state.skipAttempts++;
        if (state.skipAttempts < state.maxSkipAttempts) {
            showToast('Error de audio, buscando siguiente...');
            setTimeout(() => playNext(), 300);
        } else {
            showToast('No se encontraron canciones reproducibles');
            state.skipAttempts = 0;
        }
    });
}

async function logPlay(song, seconds = 0) {
    if (!song || state.hasLoggedCurrentPlay) return;

    state.hasLoggedCurrentPlay = true;
    PlayerLog.info('STATS', 'Registrando reproducción', { song: song.tema, seconds });

    try {
        const res = await fetch('/api/stats/log', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ id: song.id, tipo: song.tipo, segundos: seconds })
        });
        if (res.ok) {
            const data = await res.json();
            state.currentPlayLogId = data.id; // Guardamos ID para updates futuros
            state.lastPlayUpdate = Date.now();
        }
    } catch (e) {
        console.warn('Error al registrar reproducción:', e);
    }
}

async function updatePlayTime(logId, seconds) {
    if (!logId) return;
    try {
        await fetch('/api/stats/update', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ id: logId, segundos: seconds })
        });
    } catch (e) {
        // Silent fail
    }
}

// Guardar estado del player en la BDD
async function savePlayerState() {
    if (!state.currentSong) return;

    try {
        await fetch('/api/player/estado', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                cancionId: state.currentSong.id,
                cancionTipo: state.currentSong.tipo,
                posicionSegundos: state.audio?.currentTime || 0,
                playlistJson: JSON.stringify(state.playlist.slice(0, 50).map(s => ({ id: s.id, tipo: s.tipo }))),
                shuffle: state.shuffle,
                repeatMode: state.repeat
            })
        });
    } catch (e) {
        console.warn('Error guardando estado:', e);
    }
}

// Restaurar estado del player desde la BDD
async function restorePlayerState() {
    try {
        const res = await fetch('/api/player/estado');
        if (!res.ok) return;

        const estado = await res.json();
        if (!estado || !estado.cancionId) return;

        // Buscar la canción en allSongs
        const song = state.allSongs.find(s =>
            s.id === estado.cancionId && s.tipo === estado.cancionTipo
        );

        if (song) {
            state.currentSong = song;
            state.shuffle = estado.shuffle || false;
            state.repeat = estado.repeatMode || 'off';

            // Restaurar playlist si existe
            if (estado.playlistJson) {
                try {
                    const playlistRefs = JSON.parse(estado.playlistJson);
                    state.playlist = playlistRefs
                        .map(ref => state.allSongs.find(s => s.id === ref.id && s.tipo === ref.tipo))
                        .filter(Boolean);
                    state.currentIndex = state.playlist.findIndex(s => s.id === song.id && s.tipo === song.tipo);
                } catch (e) { }
            }

            // Actualizar UI sin reproducir automáticamente
            updateMiniPlayer();
            updateFullPlayer();
            showMiniPlayer();

            // Preparar audio pero no reproducir
            if (song.archivoAudio) {
                state.audio.src = song.archivoAudio;
                state.audio.currentTime = estado.posicionSegundos || 0;
            }

            // Actualizar botones
            if (state.shuffle) {
                elements.fullShuffleBtn?.classList.add('active');
                elements.miniShuffleBtn?.classList.add('active');
            }
            updateRepeatButton();

            showToast(`Retomando: ${song.tema || song.nombre}`);
        }
    } catch (e) {
        console.warn('Error restaurando estado:', e);
    }
}

async function loadInitialData() {
    try {
        PlayerLog.info('DATA', 'Cargando datos iniciales...');

        // Cargar todas las canciones
        const songsRes = await fetch('/api/canciones/todas');
        if (songsRes.ok) {
            state.allSongs = await songsRes.json();
            const playable = state.allSongs.filter(isTrackPlayable).length;
            PlayerLog.info('DATA', 'Canciones cargadas', {
                total: state.allSongs.length,
                reproducibles: playable,
                sinAudio: state.allSongs.length - playable
            });
        }

        // Cargar artistas
        const artistsRes = await fetch('/api/interpretes?limite=10000');
        if (artistsRes.ok) {
            state.allArtists = await artistsRes.json();
        }

        // Cargar álbumes
        const albumsRes = await fetch('/api/albumes?limite=10000');
        if (albumsRes.ok) {
            state.allAlbums = await albumsRes.json();
        }

        // Cargar medios
        const mediosRes = await fetch('/api/medios?limite=10000');
        if (mediosRes.ok) {
            state.allMedios = await mediosRes.json();
        }

        PlayerLog.info('DATA', 'Datos cargados', {
            artistas: state.allArtists.length,
            albumes: state.allAlbums.length,
            medios: state.allMedios.length
        });

        // Renderizar estadísticas (usa datos locales)
        renderStats();

        // Renderizar vista inicial de biblioteca
        renderLibrary();


        // Actualizar saludo
        updateGreeting();

        // Renderizar secciones del inicio
        renderHomeAlbums();
        renderHomeArtists();
        renderHomePlaylists();

    } catch (error) {
        PlayerLog.error('DATA', 'Error cargando datos iniciales', { error: error.message });
        showToast('Error al cargar datos');
    }
}



function restoreState() {
    // Restaurar preferencias de reproducción
    const savedShuffle = localStorage.getItem('playerShuffle');

    const savedRepeat = localStorage.getItem('playerRepeat');

    if (savedShuffle === 'true') {
        state.shuffle = true;
        elements.fullShuffleBtn?.classList.add('active');
    }

    if (savedRepeat) {
        state.repeat = savedRepeat;
        updateRepeatButton();
    }

    // Restaurar configuración del usuario
    try {
        const savedSettings = localStorage.getItem('playerSettings');
        if (savedSettings) {
            state.settings = { ...state.settings, ...JSON.parse(savedSettings) };
        }
    } catch (e) {
        console.warn('Error cargando configuración:', e);
    }

    // Restaurar playlists del usuario
    try {
        const savedPlaylists = localStorage.getItem('userPlaylists');
        if (savedPlaylists) {
            state.userPlaylists = JSON.parse(savedPlaylists);
        }
    } catch (e) {
        console.warn('Error cargando playlists:', e);
    }

    // Restaurar favoritos del usuario
    try {
        const savedFavorites = localStorage.getItem('userFavorites');
        if (savedFavorites) {
            state.favorites = JSON.parse(savedFavorites);
        }
    } catch (e) {
        console.warn('Error cargando favoritos:', e);
    }
}

function saveSettings() {
    try {
        localStorage.setItem('playerSettings', JSON.stringify(state.settings));
    } catch (e) {
        console.warn('Error guardando configuración:', e);
    }
}

function savePlaylists() {
    try {
        localStorage.setItem('userPlaylists', JSON.stringify(state.userPlaylists));
    } catch (e) {
        console.warn('Error guardando playlists:', e);
    }
}

// Guardar favoritos en localStorage (backup) y sincronizar con API
function saveFavorites() {
    try {
        localStorage.setItem('userFavorites', JSON.stringify(state.favorites));
    } catch (e) {
        console.warn('Error guardando favoritos:', e);
    }
}

function isFavorite(songId, tipo) {
    return state.favorites.some(f => f.id === songId && f.tipo === tipo);
}

async function toggleFavorite(songId, tipo) {
    const idx = state.favorites.findIndex(f => f.id === songId && f.tipo === tipo);
    const willBeFavorite = idx < 0;

    try {
        // Llamar a la API para persistir en BDD
        const res = await fetch(`/api/canciones/${songId}/favorito?tipo=${tipo}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ esFavorito: willBeFavorite })
        });

        if (!res.ok) throw new Error('Error en API');

        // Actualizar estado local
        if (willBeFavorite) {
            state.favorites.push({ id: songId, tipo: tipo });
            showToast('Agregado a favoritos');
        } else {
            state.favorites.splice(idx, 1);
            showToast('Quitado de favoritos');
        }

        saveFavorites(); // Backup en localStorage

        // Actualizar UI si está en la vista de favoritos
        if (state.libraryFilter === 'favorites') {
            renderLibrary();
        }
    } catch (e) {
        console.error('Error toggle favorito:', e);
        showToast('Error al actualizar favorito');
    }
}

async function getFavoriteSongs() {
    try {
        const res = await fetch('/api/player/favoritos');
        if (res.ok) {
            const favoritos = await res.json();
            // Actualizar estado local de favoritos
            state.favorites = favoritos.map(f => ({ id: f.id, tipo: f.tipo }));
            saveFavorites();

            // Enriquecer con datos de allSongs para obtener idAlbum y portadas
            return favoritos.map(fav => {
                const fullSong = state.allSongs.find(s => s.id === fav.id && s.tipo === fav.tipo);
                if (fullSong) {
                    // Combinar datos: favoritos tiene archivoAudio actualizado, allSongs tiene idAlbum
                    return { ...fullSong, ...fav };
                }
                return fav;
            });
        }
    } catch (e) {
        console.warn('Error obteniendo favoritos de API:', e);
    }
    // Fallback a buscar en allSongs
    return state.favorites
        .map(f => state.allSongs.find(s => s.id === f.id && s.tipo === f.tipo))
        .filter(Boolean);
}



// ============================================
// NAVEGACIÓN
// ============================================

function switchTab(tabId) {
    state.currentTab = tabId;

    // Actualizar botones
    elements.tabBtns.forEach(btn => {
        btn.classList.toggle('active', btn.dataset.tab === tabId);
    });

    // Actualizar vistas
    elements.views.forEach(view => {
        view.classList.toggle('active', view.id === `view-${tabId}`);
    });

    // Acciones específicas por tab
    if (tabId === 'queue') {
        renderQueue();
    } else if (tabId === 'stats') {
        // Siempre recargar estadísticas al entrar, incluso si ya estamos ahí
        renderDashboard();
    }
}

function switchLibraryFilter(filter) {
    state.libraryFilter = filter;

    elements.libraryFilters.forEach(btn => {
        btn.classList.toggle('active', btn.dataset.filter === filter);
    });

    renderLibrary();
}

// ============================================
// RENDERIZADO
// ============================================

function updateGreeting() {
    const hour = new Date().getHours();
    let greeting = 'Buenas noches';

    if (hour >= 5 && hour < 12) {
        greeting = 'Buenos días';
    } else if (hour >= 12 && hour < 19) {
        greeting = 'Buenas tardes';
    }

    if (state.settings.userName) {
        greeting += `, ${state.settings.userName}`;
    }
    greeting += '.';

    if (elements.homeGreeting) {
        elements.homeGreeting.textContent = greeting;
    }
}

function renderStats() {
    if (!elements.statsRow) return;

    // Usar datos cargados localmente para estadísticas precisas
    const totalCanciones = state.allSongs?.length || 0;
    const totalArtistas = state.allArtists?.length || 0;
    const totalMedios = state.allMedios?.length || 0;
    const totalAlbumes = state.allAlbums?.length || 0;

    elements.statsRow.innerHTML = `
            <div class="stat-chip">
                <span class="stat-chip-value">${totalCanciones}</span>
                <span class="stat-chip-label">canciones</span>
            </div>
            <div class="stat-chip">
                <span class="stat-chip-value">${totalArtistas}</span>
                <span class="stat-chip-label">artistas</span>
            </div>
            <div class="stat-chip">
                <span class="stat-chip-value">${totalMedios}</span>
                <span class="stat-chip-label">medios</span>
            </div>
            <div class="stat-chip">
                <span class="stat-chip-value">${totalAlbumes}</span>
                <span class="stat-chip-label">álbumes</span>
            </div>
        `;
}

async function renderDashboard() {
    const totalTimeEl = document.getElementById('statsTotalTime');
    const topSongsEl = document.getElementById('statsTopSongs');
    const topArtistsEl = document.getElementById('statsTopArtists');

    if (!topSongsEl || !topArtistsEl) return;

    // Show loading state
    topSongsEl.innerHTML = '<div class="loading-spinner"></div>';
    topArtistsEl.innerHTML = '<div class="loading-spinner"></div>';

    try {
        const res = await fetch('/api/stats/dashboard');
        if (!res.ok) throw new Error('Error API');
        const data = await res.json();

        // 1. Total Time
        if (totalTimeEl) totalTimeEl.textContent = data.tiempoTotalEscuchado;

        // 2. Top Songs
        if (data.topCanciones && data.topCanciones.length > 0) {
            topSongsEl.innerHTML = data.topCanciones.map((item, index) => {
                const [tipo, idStr] = item.id.split('_');
                const id = parseInt(idStr);

                // Construir URL de imagen
                let coverUrl = '';
                if (item.tieneImagen) {
                    coverUrl = item.idExtra
                        ? `/api/albumes/${item.idExtra}/portada`
                        : `/api/canciones/${id}/portada?tipo=${tipo}`;
                }

                return `
                <div class="stats-item song-stat-item" data-id="${id}" data-tipo="${tipo}" style="cursor: pointer;">
                    <div class="stats-rank">${index + 1}</div>
                    ${coverUrl ?
                        `<img class="stats-img" src="${coverUrl}" alt="" loading="lazy">` :
                        `<div class="stats-img-placeholder">${musicIcon}</div>`
                    }
                    <div class="stats-item-info">
                        <div class="stats-item-title">${escapeHtml(item.nombre)}</div>
                        <div class="stats-item-sub">${escapeHtml(item.subtitulo)}</div>
                    </div>
                    <div class="stats-count">${item.conteo} plays</div>
                </div>`;
            }).join('');

            // Attach listeners
            topSongsEl.querySelectorAll('.song-stat-item').forEach(item => {
                item.addEventListener('click', () => {
                    playSongById(parseInt(item.dataset.id), item.dataset.tipo);
                });
            });
        } else {
            topSongsEl.innerHTML = '<div class="search-empty"><p>No hay datos aún</p></div>';
        }

        // 3. Top Artists
        if (data.topArtistas && data.topArtistas.length > 0) {
            topArtistsEl.innerHTML = data.topArtistas.map((item, index) => {
                const imgUrl = item.tieneImagen ? `/api/interpretes/${item.idReferencia}/foto` : null;

                return `
                <div class="stats-item artist-stat-item" data-id="${item.idReferencia}" style="cursor: pointer;">
                     <div class="stats-rank">${index + 1}</div>
                     ${imgUrl ?
                        `<img class="stats-img artist-img" src="${imgUrl}" alt="" loading="lazy">` :
                        `<div class="stats-img-placeholder artist-img">${userIcon}</div>`
                    }
                    <div class="stats-item-info">
                        <div class="stats-item-title">${escapeHtml(item.nombre)}</div>
                    </div>
                    <div class="stats-count">${item.conteo} plays</div>
                </div>`;
            }).join('');

            // Attach listeners for artists
            topArtistsEl.querySelectorAll('.artist-stat-item').forEach(item => {
                item.addEventListener('click', () => {
                    const artistId = parseInt(item.dataset.id);
                    const artist = state.allArtists.find(a => a.id === artistId);
                    if (artist) showArtistDetail(artist);
                });
            });
        } else {
            topArtistsEl.innerHTML = '<div class="search-empty"><p>No hay datos aún</p></div>';
        }

    } catch (e) {
        console.error('Error loading dashboard:', e);
        showToast('Error cargando estadísticas');
    }
}


async function renderLibrary() {
    if (!elements.libraryList) return;

    let html = '';

    switch (state.libraryFilter) {
        case 'songs':
            // Aplicar filtro de canciones con/sin audio según configuración
            let songsToShow = state.allSongs;
            if (!state.settings.showSongsWithoutAudio) {
                songsToShow = state.allSongs.filter(s => s.archivoAudio && s.archivoAudio.trim() !== '');
            }
            html = renderSongsList(songsToShow);
            break;
        case 'favorites':
            const favSongs = await getFavoriteSongs();
            if (!favSongs || favSongs.length === 0) {
                html = `<div class="search-empty">
                        <svg xmlns="http://www.w3.org/2000/svg" width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <path d="M19 14c1.49-1.46 3-3.21 3-5.5A5.5 5.5 0 0 0 16.5 3c-1.76 0-3 .5-4.5 2-1.5-1.5-2.74-2-4.5-2A5.5 5.5 0 0 0 2 8.5c0 2.3 1.5 4.05 3 5.5l7 7Z"/>
                        </svg>
                        <p>No tienes canciones favoritas aún</p>
                        <p style="font-size: 0.875rem; color: var(--player-text-subdued);">Toca el ❤️ en el reproductor para agregar</p>
                    </div>`;
            } else {
                html = renderSongsList(favSongs);
            }
            break;
        case 'artists':
            html = renderArtistsList(state.allArtists);
            break;
        case 'albums':
            html = renderAlbumsList(state.allAlbums);
            break;
        case 'medios':
            html = renderMediosList(state.allMedios);
            break;
    }

    elements.libraryList.innerHTML = html || '<div class="search-empty">No hay elementos</div>';

    // Event listeners para cards
    attachCardListeners();
}



function renderSongsList(songs) {
    if (!songs || songs.length === 0) return '';

    return songs.map(song => {
        // La API puede devolver 'tema' o 'nombre' dependiendo del endpoint
        const songName = song.tema || song.nombre || 'Sin título';
        const artistName = song.interprete || song.artista || 'Artista desconocido';
        const hasAudio = song.archivoAudio && song.archivoAudio.trim() !== '';
        const isPlaying = state.currentSong?.id === song.id && state.currentSong?.tipo === song.tipo;
        const hasCover = song.tienePortada || song.tienePortadaEmbebida || song.idAlbum;

        return `
            <div class="song-card ${hasAudio ? '' : 'no-audio'}" data-id="${song.id}" data-tipo="${song.tipo}" data-has-audio="${hasAudio}">
                ${hasCover ?
                `<img class="song-cover" src="${getCoverUrl(song)}" alt="" loading="lazy" onerror="this.style.display='none'">` :
                `<div class="song-cover-placeholder"><svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M9 18V5l12-2v13"/><circle cx="6" cy="18" r="3"/><circle cx="18" cy="16" r="3"/></svg></div>`
            }
                <div class="song-info">
                    <div class="song-title ${isPlaying ? 'playing' : ''}">${escapeHtml(songName)}</div>
                    <div class="song-artist">${escapeHtml(artistName)}${hasAudio ? '' : ' · Sin audio'}</div>
                </div>
            </div>`;
    }).join('');
}




function renderArtistsList(artists) {
    if (!artists || artists.length === 0) return '';

    return artists.map(artist => {
        const artistName = artist.interprete || artist.nombre || 'Artista desconocido';
        const songCount = artist.totalTemas || artist.totalCanciones || 0;
        return `
            <div class="artist-card" data-id="${artist.id}">
                ${artist.tieneFoto ?
                `<img class="artist-avatar" src="/api/interpretes/${artist.id}/foto" alt="" loading="lazy" onerror="this.style.display='none'">` :
                `<div class="artist-avatar-placeholder"><svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg></div>`
            }
                <div class="song-info">
                    <div class="artist-name">${escapeHtml(artistName)}</div>
                    <div class="artist-info-sub">${songCount} canciones</div>
                </div>
            </div>`;
    }).join('');
}

function renderAlbumsList(albums) {
    if (!albums || albums.length === 0) return '';

    return albums.map(album => `
            <div class="album-card" data-id="${album.id}">
                ${album.tienePortada ?
            `<img class="song-cover" src="/api/albumes/${album.id}/portada" alt="" loading="lazy" onerror="this.style.display='none'">` :
            `<div class="song-cover-placeholder"><svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><circle cx="12" cy="12" r="3"/></svg></div>`
        }
                <div class="song-info">
                    <div class="album-name">${escapeHtml(album.nombre)}</div>
                    <div class="album-info-sub">${escapeHtml(album.interprete || 'Varios artistas')} • ${album.anio || ''}</div>
                </div>
            </div>
        `).join('');
}



function renderMediosList(medios) {
    if (!medios || medios.length === 0) return '';

    return medios.map(medio => {
        // La API devuelve numMedio y tipoMedio
        const numero = medio.numMedio || medio.numero;
        const tipo = medio.tipoMedio || medio.tipo;

        const isCd = tipo?.toLowerCase() === 'cd';
        const icon = isCd ? discIcon : tapeIcon;
        const typeLabel = isCd ? 'CD' : 'Cassette';
        // Mostrar "CD 1" o "Cassette C01" en lugar de solo el número
        const displayName = `${typeLabel} ${numero}`;

        return `
            <div class="medio-card" data-num="${numero}" data-tipo="${tipo}">
                <div class="song-cover-placeholder">${icon}</div>
                <div class="song-info">
                    <div class="medio-name">${escapeHtml(displayName)}</div>
                    <div class="medio-info-sub">${typeLabel} • ${medio.totalTemas || 0} temas</div>
                </div>
            </div>
        `}).join('');
}

function renderQueue() {
    const nowPlayingEl = elements.queueNowPlaying;
    const upNextEl = elements.queueUpNext;

    if (!nowPlayingEl || !upNextEl) return;

    // Reproduciendo ahora
    if (state.currentSong) {
        const songName = state.currentSong.tema || state.currentSong.nombre || 'Sin título';
        const artistName = state.currentSong.interprete || state.currentSong.artista || '';
        nowPlayingEl.innerHTML = `
                <div class="song-card playing">
                    ${state.currentSong.tienePortada || state.currentSong.tienePortadaEmbebida || state.currentSong.idAlbum ?
                `<img class="song-cover" src="${getCoverUrl(state.currentSong)}" alt="">` :
                `<div class="song-cover-placeholder">${musicIcon}</div>`
            }
                    <div class="song-info">
                        <div class="song-title playing">${escapeHtml(songName)}</div>
                        <div class="song-artist">${escapeHtml(artistName)}</div>
                    </div>
                </div>
            `;
    } else {
        nowPlayingEl.innerHTML = '<div class="queue-empty">Nada reproduciéndose</div>';
    }

    // Próximas - con funcionalidad de edición
    const upNextData = getUpNextSongsWithIndices(20);
    if (upNextData.length > 0) {
        upNextEl.innerHTML = upNextData.map((item, displayIndex) => {
            const song = item.song;
            const songName = song.tema || song.nombre || 'Sin título';
            const artistName = song.interprete || song.artista || '';
            return `
                <div class="queue-item" 
                     data-id="${song.id}" 
                     data-tipo="${song.tipo}" 
                     data-playlist-index="${item.playlistIndex}"
                     data-display-index="${displayIndex}">
                    <button class="queue-remove-btn" data-playlist-index="${item.playlistIndex}" data-display-index="${displayIndex}" title="Quitar de la cola">
                        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
                        </svg>
                    </button>
                    ${song.tienePortada || song.tienePortadaEmbebida || song.idAlbum ?
                    `<img class="song-cover" src="${getCoverUrl(song)}" alt="" loading="lazy">` :
                    `<div class="song-cover-placeholder">${musicIcon}</div>`
                }
                    <div class="song-info">
                        <div class="song-title">${escapeHtml(songName)}</div>
                        <div class="song-artist">${escapeHtml(artistName)}</div>
                    </div>
                    <div class="queue-drag-handle" draggable="true">
                        <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3">
                            <line x1="3" y1="6" x2="21" y2="6"/><line x1="3" y1="12" x2="21" y2="12"/><line x1="3" y1="18" x2="21" y2="18"/>
                        </svg>
                    </div>
                </div>
            `}).join('');
        attachQueueListeners();
    } else {
        upNextEl.innerHTML = '<div class="queue-empty">No hay más canciones en la cola</div>';
    }
}

/**
 * Obtiene las próximas canciones con sus índices en la playlist
 * En modo shuffle: usa cola persistente, solo agrega nuevas aleatorias al final
 * Siempre devuelve 'count' canciones, repitiéndolas si es necesario
 */
/**
 * Rellena la cola persistente con canciones aleatorias
 */
function fillUpNextQueue(targetCount, currentSongKey, playableSongs) {
    if (state.upNextQueue.length >= targetCount) return;

    let attempts = 0;
    const maxAttempts = targetCount * 3;

    while (state.upNextQueue.length < targetCount && attempts < maxAttempts) {
        attempts++;

        // Keys ya usados en la cola
        const usedKeys = new Set(state.upNextQueue.map(item => `${item.song.id}_${item.song.tipo}`));
        if (currentSongKey) usedKeys.add(currentSongKey);

        // Canciones disponibles
        let available = playableSongs.filter(s => !usedKeys.has(`${s.id}_${s.tipo}`));

        // Permitir repetición si no hay disponibles
        if (available.length === 0) {
            available = playableSongs.filter(s => `${s.id}_${s.tipo}` !== currentSongKey);
        }

        if (available.length === 0) break;

        // Elegir aleatoriamente
        const randomIdx = Math.floor(Math.random() * available.length);
        const song = available[randomIdx];

        const playlistIndex = state.playlist.findIndex(s => s.id === song.id && s.tipo === song.tipo);
        state.upNextQueue.push({
            song,
            playlistIndex: playlistIndex >= 0 ? playlistIndex : state.upNextQueue.length
        });
    }
}

function getUpNextSongsWithIndices(count) {
    const playableSongs = state.allSongs.filter(isTrackPlayable);
    if (playableSongs.length === 0) return [];

    if (state.shuffle) {
        // Modo aleatorio: usar cola persistente
        const currentSongKey = state.currentSong ? `${state.currentSong.id}_${state.currentSong.tipo}` : null;

        // Rellenar si hace falta
        fillUpNextQueue(count, currentSongKey, playableSongs);

        return state.upNextQueue.slice(0, count);
    } else {
        // Modo secuencial: usar la playlist en orden (sin cola persistente)
        const songs = state.playlist.length > 0 ? state.playlist : playableSongs;
        const currentIdx = state.currentIndex >= 0 ? state.currentIndex : -1;
        const upNext = [];

        let loopCount = 0;
        let i = 1;

        while (upNext.length < count && loopCount < count * 2) {
            const idx = (currentIdx + i) % songs.length;
            if (idx < 0) continue;

            const song = songs[idx];
            if (song && isTrackPlayable(song)) {
                upNext.push({ song, playlistIndex: idx });
            }
            i++;
            loopCount++;

            // Si hemos dado la vuelta completa, permitir repetir
            if (i > songs.length) {
                i = 1;
            }
        }

        return upNext;
    }
}

/**
 * Obtiene una canción aleatoria que no esté ya en la cola
 */
function getRandomPlayableSong(excludeKey, currentQueue) {
    const playableSongs = state.allSongs.filter(isTrackPlayable);
    if (playableSongs.length === 0) return null;

    // Crear lista de keys ya usados en la cola
    const usedKeys = new Set(currentQueue.map(item => `${item.song.id}_${item.song.tipo}`));
    if (excludeKey) usedKeys.add(excludeKey);

    // Filtrar canciones disponibles
    let available = playableSongs.filter(s => !usedKeys.has(`${s.id}_${s.tipo}`));

    // Si no hay canciones disponibles, permitir repetición
    if (available.length === 0) {
        available = playableSongs.filter(s => `${s.id}_${s.tipo}` !== excludeKey);
    }

    if (available.length === 0) {
        available = playableSongs;
    }

    // Elegir aleatoriamente
    const randomIdx = Math.floor(Math.random() * available.length);
    return available[randomIdx];
}

/**
 * Consume canciones de la cola (cuando se reproduce una)
 */
function consumeFromUpNextQueue(consumeCount) {
    if (state.shuffle && consumeCount > 0) {
        state.upNextQueue.splice(0, consumeCount);
    }
}

/**
 * Attach listeners para la cola editable
 */
function attachQueueListeners() {
    const queueItems = document.querySelectorAll('.queue-item');

    queueItems.forEach((item, displayIndex) => {
        // Click para reproducir con animación
        item.addEventListener('click', (e) => {
            if (e.target.closest('.queue-remove-btn') || e.target.closest('.queue-drag-handle')) return;
            const id = parseInt(item.dataset.id);
            const tipo = item.dataset.tipo;

            // Animar transición de cola
            animateQueueTransition(displayIndex, () => {
                // Consumir las canciones anteriores de la cola (incluyendo la clickeada)
                consumeFromUpNextQueue(displayIndex + 1);
                playSongById(id, tipo);
            });
        });

        // Drop targets en el item
        item.addEventListener('dragover', handleQueueDragOver);
        item.addEventListener('dragenter', handleQueueDragEnter);
        item.addEventListener('dragleave', handleQueueDragLeave);
        item.addEventListener('drop', handleQueueDrop);

        // Drag start/end solo en el handle
        const handle = item.querySelector('.queue-drag-handle');
        if (handle) {
            handle.addEventListener('dragstart', (e) => {
                // Pasar el item padre como referencia
                handleQueueDragStart(e, item, displayIndex);
            });
            handle.addEventListener('dragend', (e) => {
                handleQueueDragEnd(e, item);
            });
        }
    });

    // Botones de eliminar
    document.querySelectorAll('.queue-remove-btn').forEach(btn => {
        btn.addEventListener('click', (e) => {
            e.stopPropagation();
            const playlistIndex = parseInt(btn.dataset.playlistIndex);
            const displayIndex = btn.dataset.displayIndex ? parseInt(btn.dataset.displayIndex) : undefined;
            removeFromQueue(playlistIndex, displayIndex);
        });
    });
}

/**
 * Anima la transición de la cola al cambiar de canción
 * @param {number} clickedIndex - Índice de la canción clickeada en la lista visual (0-indexed)
 * @param {Function} callback - Función a ejecutar después de la animación
 */
function animateQueueTransition(clickedIndex, callback) {
    const queueItems = document.querySelectorAll('.queue-item');
    const nowPlayingEl = elements.queueNowPlaying;

    if (queueItems.length === 0) {
        if (callback) callback();
        return;
    }

    // Calcular la altura de un item (incluyendo margin/gap)
    const itemHeight = queueItems[0]?.offsetHeight || 60;
    const gap = 8; // gap aproximado entre items
    const slotHeight = itemHeight + gap;

    // Número de slots que se van a "consumir" (canción actual + las saltadas)
    const slotsToConsume = clickedIndex + 1;
    const translateAmount = slotsToConsume * slotHeight;

    // Animar la canción actual ("Reproduciendo ahora") con fade out
    const currentPlaying = nowPlayingEl?.querySelector('.song-card');
    if (currentPlaying) {
        currentPlaying.style.transition = 'opacity 250ms ease-out';
        currentPlaying.style.opacity = '0';
    }

    // Animar TODOS los items de la cola
    queueItems.forEach((item, index) => {
        item.style.transition = 'transform 300ms ease-out, opacity 300ms ease-out';

        // Si clickIndex es 0 (caso normal de siguiente canción), translateAmount será 1 * slotHeight
        // El item 0 debe subir y desaparecer (o transformarse en current)
        // El item 1 debe subir a la posición 0

        if (index < clickedIndex) {
            // Casos donde saltamos varias canciones (no debería pasar con playNext)
            item.style.transform = `translateY(-${slotHeight}px)`;
            item.style.opacity = '0';
        } else if (index === clickedIndex) {
            // Este es el que se va a convertir en "Reproduciendo ahora"
            // Lo movemos hacia arriba, hacia la sección 'queueNowPlaying'
            // Como queueNowPlaying está fuera de este contenedor, visualmente lo sacamos hacia arriba
            item.style.transform = `translateY(-${slotHeight * 1.5}px)`; // Sube un poco más
            item.style.opacity = '0';
        } else {
            // Los demás suben para ocupar el espacio liberado por los anteriores
            item.style.transform = `translateY(-${translateAmount}px)`;
        }
    });

    // Después de la animación, ejecutar callback (el renderQueue reemplazará todo el HTML)
    setTimeout(() => {
        if (callback) callback();
    }, 320);
}

/**
 * Actualiza la cola con animación cuando se está viendo
 */
function updateQueueWithAnimation() {
    if (state.currentTab === 'queue') {
        // Pequeña animación de transición
        const upNextEl = elements.queueUpNext;
        if (upNextEl) {
            upNextEl.classList.add('queue-updating');
            setTimeout(() => {
                renderQueue();
                upNextEl.classList.remove('queue-updating');
            }, 100);
        }
    }
}

// Estado para drag & drop
let draggedQueueItem = null;
let draggedPlaylistIndex = null;
let draggedDisplayIndex = null;

function handleQueueDragStart(e, item, displayIndex) {
    draggedQueueItem = item;
    draggedPlaylistIndex = parseInt(item.dataset.playlistIndex);
    draggedDisplayIndex = displayIndex;
    draggedQueueItem.classList.add('dragging');
    e.dataTransfer.effectAllowed = 'move';
    e.dataTransfer.setData('text/plain', draggedPlaylistIndex);
}

function handleQueueDragOver(e) {
    e.preventDefault();
    e.dataTransfer.dropEffect = 'move';
}

function handleQueueDragEnter(e) {
    e.preventDefault();
    const target = e.currentTarget;
    if (target !== draggedQueueItem && target.classList.contains('queue-item')) {
        target.classList.add('drag-over');
    }
}

function handleQueueDragLeave(e) {
    e.currentTarget.classList.remove('drag-over');
}

function handleQueueDrop(e) {
    e.preventDefault();
    const target = e.currentTarget;
    target.classList.remove('drag-over');

    if (target === draggedQueueItem || !target.classList.contains('queue-item')) return;

    const fromDisplayIndex = draggedDisplayIndex;
    const toDisplayIndex = parseInt(target.dataset.displayIndex);

    if (fromDisplayIndex !== toDisplayIndex) {
        moveInQueueByDisplayIndex(fromDisplayIndex, toDisplayIndex);
    }
}

function handleQueueDragEnd(e, item) {
    if (item) {
        item.classList.remove('dragging');
    }
    document.querySelectorAll('.queue-item.drag-over').forEach(el => el.classList.remove('drag-over'));
    draggedQueueItem = null;
    draggedPlaylistIndex = null;
    draggedDisplayIndex = null;
}

/**
 * Mueve una canción en la cola usando índices visuales
 */
function moveInQueueByDisplayIndex(fromDisplayIndex, toDisplayIndex) {
    if (state.shuffle) {
        // En modo shuffle, movemos los elementos en state.upNextQueue
        const item = state.upNextQueue.splice(fromDisplayIndex, 1)[0];
        state.upNextQueue.splice(toDisplayIndex, 0, item);
        renderQueue();
    } else {
        // En modo secuencial, calculamos los índices reales de playlist
        const currentIdx = state.currentIndex;
        const fromRealIdx = (currentIdx + 1 + fromDisplayIndex) % state.playlist.length;

        // Calculamos a dónde ir
        // Si movemos hacia abajo (índice mayor), hay que ajustar porque el elemento se eliminó
        let toRealIdx = (currentIdx + 1 + toDisplayIndex) % state.playlist.length;

        // Usar la función existente moveInQueue pero adaptada
        // Como moveInQueue espera índices de playlist, mejor modificamos la playlist directamente aquí para ser consistentes con la vista

        const item = state.playlist.splice(fromRealIdx, 1)[0];

        // Recalcular toRealIdx después del splice si es necesario
        if (fromRealIdx < toRealIdx) {
            toRealIdx--;
        }

        // Ajustar toRealIdx para que sea relativo al inicio de la playlist si dimos la vuelta
        if (toRealIdx < 0) toRealIdx += state.playlist.length;

        state.playlist.splice(toRealIdx, 0, item);

        // Si el índice actual cambió, ajustarlo
        if (fromRealIdx < state.currentIndex && toRealIdx >= state.currentIndex) {
            state.currentIndex--;
        } else if (fromRealIdx > state.currentIndex && toRealIdx <= state.currentIndex) {
            state.currentIndex++;
        }

        renderQueue();
        saveState();
    }
}

/**
 * Elimina una canción de la cola
 */
function removeFromQueue(playlistIndex, displayIndex) {
    // En modo shuffle, usar displayIndex es más seguro porque puede haber duplicados de playlistIndex

    if (state.shuffle) {
        let queueIdx = -1;

        // Si tenemos displayIndex, usarlo directamente (es el índice en upNextQueue)
        if (displayIndex !== undefined && displayIndex !== null) {
            queueIdx = displayIndex;
        } else {
            // Fallback: buscar por playlistIndex
            queueIdx = state.upNextQueue.findIndex(item => item.playlistIndex === playlistIndex);
        }

        if (queueIdx >= 0 && queueIdx < state.upNextQueue.length) {
            const removed = state.upNextQueue.splice(queueIdx, 1)[0].song;
            // Rellenar la cola si hace falta
            fillUpNextQueue(20, state.currentSong ? `${state.currentSong.id}_${state.currentSong.tipo}` : null, state.allSongs.filter(isTrackPlayable));
            renderQueue();
            if (removed) {
                const songName = removed.tema || removed.nombre || 'Canción';
                showToast(`${songName} quitada de la cola`);
            }
        }
    } else {
        // Modo playlist
        if (playlistIndex === state.currentIndex) {
            showToast('No se puede eliminar la canción actual');
            return;
        }

        const removed = state.playlist.splice(playlistIndex, 1)[0];

        // Ajustar índice actual si borramos algo anterior
        if (playlistIndex < state.currentIndex) {
            state.currentIndex--;
        }

        renderQueue();
        saveState();
        if (removed) {
            const songName = removed.tema || removed.nombre || 'Canción';
            showToast(`${songName} quitada de la cola`);
        }
    }
}

/**
 * Mueve una canción en la cola
 */
function moveInQueue(fromIndex, toIndex) {
    const songs = state.shuffle ? state.shuffledPlaylist : state.playlist;
    const currentIdx = state.shuffle ? state.shuffledIndex : state.currentIndex;

    // No permitir mover antes o a la posición de la canción actual
    if (fromIndex <= currentIdx || toIndex <= currentIdx) {
        showToast('No se puede mover a esa posición');
        return;
    }

    const [movedSong] = songs.splice(fromIndex, 1);
    const adjustedToIndex = toIndex > fromIndex ? toIndex - 1 : toIndex;
    songs.splice(adjustedToIndex, 0, movedSong);

    showToast('Cola reordenada');
    renderQueue();
}

function attachCardListeners() {
    document.querySelectorAll('.song-card').forEach(card => {
        card.addEventListener('click', () => {
            const id = parseInt(card.dataset.id);
            const tipo = card.dataset.tipo;
            playSongById(id, tipo);
        });
    });

    document.querySelectorAll('.artist-card').forEach(card => {
        card.addEventListener('click', () => {
            const artistId = parseInt(card.dataset.id);
            const artist = state.allArtists.find(a => a.id === artistId);
            if (artist) {
                showArtistDetail(artist);
            }
        });
    });

    document.querySelectorAll('.album-card').forEach(card => {
        card.addEventListener('click', () => {
            const albumId = parseInt(card.dataset.id);
            const album = state.allAlbums.find(a => a.id === albumId);
            if (album) {
                showAlbumDetail(album);
            }
        });
    });

    document.querySelectorAll('.medio-card').forEach(card => {
        card.addEventListener('click', async () => {
            const num = card.dataset.num;
            await playMedio(num);
        });
    });
}

// ============================================
// BÚSQUEDA
// ============================================

function handleSearch() {
    const query = elements.searchInput?.value.trim().toLowerCase() || '';
    state.searchQuery = query;

    if (!query) {
        elements.searchResults.innerHTML = `
                <div class="search-empty">
                    ${searchIcon}
                    <p>Busca canciones, artistas o álbumes</p>
                </div>
            `;
        return;
    }

    // Buscar en canciones locales (la API devuelve 'tema' e 'interprete')
    let songsToSearch = state.allSongs;
    // Aplicar filtro de audio según configuración
    if (!state.settings.showSongsWithoutAudio) {
        songsToSearch = state.allSongs.filter(s => s.archivoAudio && s.archivoAudio.trim() !== '');
    }

    const filteredSongs = songsToSearch.filter(song => {
        const songName = (song.tema || song.nombre || '').toLowerCase();
        const artistName = (song.interprete || song.artista || '').toLowerCase();
        return songName.includes(query) || artistName.includes(query);
    }).slice(0, 20);



    if (filteredSongs.length > 0) {
        elements.searchResults.innerHTML = renderSongsList(filteredSongs);
        attachCardListeners();
    } else {
        elements.searchResults.innerHTML = `
                <div class="search-empty">
                    <p>No se encontraron resultados para "${escapeHtml(query)}"</p>
                </div>
            `;
    }
}

// ============================================
// REPRODUCCIÓN
// ============================================

function playSongById(id, tipo) {
    const song = state.allSongs.find(s => s.id === id && s.tipo === tipo);
    if (!song) {
        PlayerLog.warn('PLAYBACK', 'Canción no encontrada en allSongs', { id, tipo });
        showToast('Canción no encontrada');
        return;
    }

    // Establecer playlist con canciones reproducibles
    const playableSongs = state.allSongs.filter(isTrackPlayable);
    state.playlist = playableSongs;
    state.currentIndex = state.playlist.findIndex(s => s.id === id && s.tipo === tipo);

    // Si la canción seleccionada no está en la lista de reproducibles, ajustar índice
    if (state.currentIndex < 0) {
        state.currentIndex = 0;
    }

    playSong(song);
}

async function playMedio(numMedio) {
    try {
        PlayerLog.info('MEDIO', 'Cargando medio', { numMedio });
        const res = await fetch(`/api/medios/${numMedio}/temas`);
        if (!res.ok) throw new Error('Error al cargar medio');

        const temas = await res.json();

        // Filtrar solo las reproducibles
        const temasReproducibles = temas.filter(isTrackPlayable);

        PlayerLog.info('MEDIO', 'Medio cargado', {
            numMedio,
            totalTemas: temas.length,
            reproducibles: temasReproducibles.length
        });

        if (temasReproducibles.length === 0) {
            showToast('Este medio no tiene canciones reproducibles');
            return;
        }

        state.playlist = temasReproducibles;
        state.currentIndex = 0;
        playSong(temasReproducibles[0]);
        showToast(`Reproduciendo ${numMedio} (${temasReproducibles.length} tracks)`);
    } catch (error) {
        PlayerLog.error('MEDIO', 'Error al cargar medio', { numMedio, error: error.message });
        showToast('Error al reproducir medio');
    }
}


function playSong(song) {
    if (state.isLoadingTrack) {
        PlayerLog.debug('PLAYBACK', 'Ignorando llamada, ya cargando track');
        return;
    }

    if (!song || !isTrackPlayable(song)) {
        // Manejo de error existente... (simplificado para no duplicar lógica)
        handleUnplayableSong(song);
        return;
    }

    state.isLoadingTrack = true;

    // Verificar si debemos aplicar crossfade
    // Permitir crossfade si está activado y hay una canción previa cargada
    let shouldCrossfade = state.settings.enableCrossfade && state.currentSong;

    // Si ya hay un crossfade ocurriendo, lo interrumpimos para iniciar el nuevo
    if (shouldCrossfade && state.isCrossfading) {
        PlayerLog.info('CROSSFADE', 'Interrumpiendo transición anterior para iniciar nueva');
        stopCrossfade();
        // Aún así queremos hacer crossfade hacia la nueva (shouldCrossfade sigue true)
        // PERO debemos asegurarnos de que la "vieja" (state.audio actual) esté lista para ser bg
    }

    try {
        if (shouldCrossfade) {
            performCrossfade(song);
        } else {
            // Reproducción normal (corte directo)
            stopCrossfade(); // Cancelar cualquier crossfade activo
            state.audio.pause();
            state.audio.currentTime = 0;

            state.currentSong = song;
            state.skipAttempts = 0;
            state.hasLoggedCurrentPlay = false;
            state.currentPlayLogId = null;
            state.lastPlayUpdate = 0;

            const audioUrl = `/api/canciones/${song.id}/audio?tipo=${song.tipo}`;
            state.audio.src = audioUrl;

            // Restaurar volumen por si acaso quedó en 0 por un crossfade anterior
            const savedVolume = localStorage.getItem('playerVolume');
            state.audio.volume = savedVolume ? parseFloat(savedVolume) : 1.0;

            state.audio.play().catch(e => {
                PlayerLog.error('PLAYBACK', 'Error al iniciar reproducción', { error: e.message });
            });

            updateUIForNewSong(song);
        }
    } finally {
        state.isLoadingTrack = false;
    }
}

function performCrossfade(nextSong) {
    const songName = nextSong.tema || nextSong.nombre || 'Sin título';
    PlayerLog.info('CROSSFADE', `Iniciando transición a: ${songName}`);
    state.isCrossfading = true;

    // 1. El actual (Playing) pasa a ser background (Fading out)
    state.bgAudio = state.audio;

    // 2. Creamos NUEVO elemento para el principal (Incoming)
    // Esto evita reciclar elementos "sucios" y garantiza un estado limpio
    state.audio = new Audio();
    setupAudioListeners(state.audio);

    // Restaurar configuración del nuevo elemento
    const savedVolume = localStorage.getItem('playerVolume');
    state.audio.volume = 0; // Empieza en silencio para el fade in

    // 3. Preparar el NUEVO audio
    state.currentSong = nextSong;
    state.skipAttempts = 0;
    state.hasLoggedCurrentPlay = false;
    state.currentPlayLogId = null;
    state.lastPlayUpdate = 0;

    const audioUrl = `/api/canciones/${nextSong.id}/audio?tipo=${nextSong.tipo}`;
    state.audio.src = audioUrl;

    // Actualizar visualizador para escuchar la nueva fuente
    if (state.visualizer) {
        try {
            state.visualizer.disconnectInput();
            state.visualizer.connectInput(state.audio);
        } catch (e) {
            PlayerLog.debug('VISUALIZER', 'Error conectando input', e);
        }
    }

    // 4. Reproducir el nuevo
    state.audio.play().then(() => {
        // Una vez que empieza a reproducir, iniciamos el ramp
        updateUIForNewSong(nextSong);
        startCrossfadeRamp();
    }).catch(e => {
        PlayerLog.error('CROSSFADE', 'Error iniciando nueva canción', e);
        // Fallback: corte directo
        state.isCrossfading = false;
        // Intercambiar de nuevo si falló?? Mejor reiniciar estado normal
        state.audio.volume = 1;
        state.bgAudio.pause();
    });
}

function startCrossfadeRamp() {
    const duration = (state.settings.crossfadeDuration || 3) * 1000;
    const steps = 20;
    const intervalTime = duration / steps;

    // Volumen objetivo
    const savedVolume = localStorage.getItem('playerVolume');
    const targetVolume = savedVolume ? parseFloat(savedVolume) : 1.0;

    let step = 0;

    // Limpiar intervalo anterior si existiera
    if (state.crossfadeInterval) clearInterval(state.crossfadeInterval);

    state.crossfadeInterval = setInterval(() => {
        step++;
        const progress = step / steps; // 0 a 1

        // Linear fade
        // Entrante: sube
        state.audio.volume = Math.min(targetVolume, progress * targetVolume);

        // Saliente: baja
        state.bgAudio.volume = Math.max(0, targetVolume - (progress * targetVolume));

        if (step >= steps) {
            // Fin del crossfade
            finishCrossfade(targetVolume);
        }
    }, intervalTime);
}

function finishCrossfade(finalVolume) {
    clearInterval(state.crossfadeInterval);
    state.crossfadeInterval = null;
    state.isCrossfading = false;

    // Asegurar volúmenes finales
    state.audio.volume = finalVolume;

    // Detener y limpiar el saliente
    state.bgAudio.pause();
    state.bgAudio.removeAttribute('src'); // Forzar limpieza
    state.bgAudio.load();
    state.bgAudio.src = '';
    state.bgAudio.volume = finalVolume; // Restaurar volumen para futuro uso

    PlayerLog.debug('CROSSFADE', 'Transición completada');
}

function stopCrossfade() {
    if (state.crossfadeInterval) {
        clearInterval(state.crossfadeInterval);
        state.crossfadeInterval = null;
    }
    state.isCrossfading = false;

    // Si estamos interrumpiendo para empezar OTRA, no pausar el audio principal
    // simplemente limpiar el bgAudio viejo
    state.bgAudio.pause();
    state.bgAudio.src = '';
    state.bgAudio.load();

    // Asegurar que el principal tenga volumen correcto
    const savedVolume = localStorage.getItem('playerVolume');
    state.audio.volume = savedVolume ? parseFloat(savedVolume) : 1.0;
}

function handleUnplayableSong(song) {
    PlayerLog.warn('PLAYBACK', 'Track no reproducible, saltando', PlayerLog.trackInfo(song));
    state.skipAttempts++;
    if (state.skipAttempts < state.maxSkipAttempts) {
        showToast('Sin audio, buscando siguiente...');
        setTimeout(() => playNext(), 100);
    } else {
        showToast('No hay más canciones reproducibles');
        state.skipAttempts = 0;
    }
}

function updateUIForNewSong(song) {
    updateMiniPlayer();
    updateFullPlayer();
    showMiniPlayer();
    if (state.currentTab === 'queue') renderQueue();

    document.querySelectorAll('.song-card').forEach(card => {
        const isPlaying = parseInt(card.dataset.id) === song.id && card.dataset.tipo === song.tipo;
        card.classList.toggle('playing', isPlaying);
        card.querySelector('.song-title')?.classList.toggle('playing', isPlaying);
    });

    saveState();
}


function togglePlayPause() {
    if (!state.currentSong) {
        // Si no hay canción, reproducir aleatoriamente
        playShuffleAll();
        return;
    }

    if (state.isPlaying) {
        state.audio.pause();
        if (state.visualizer) {
            state.visualizer.toggleAnalyzer(false);
        }
        // Si hay crossfade, pausar todo
        if (state.isCrossfading) {
            stopCrossfade();
            state.bgAudio.pause();
            state.isPlaying = false;
            updatePlayButtons();
        }
    } else {
        state.audio.play().then(() => {
            if (state.visualizer && state.fullPlayerVisible) {
                state.visualizer.toggleAnalyzer(true);
            }
        }).catch(e => {
            PlayerLog.error('PLAYBACK', 'Error en play()', { error: e.message });
        });
    }
}

function playPrevious() {
    if (state.playlist.length === 0) {
        PlayerLog.debug('PLAYBACK', 'playPrevious: playlist vacía');
        return;
    }

    // Si ya pasaron más de 3 segundos, reiniciar canción
    if (state.audio.currentTime > 3) {
        PlayerLog.debug('PLAYBACK', 'Reiniciando canción actual');
        state.audio.currentTime = 0;
        return;
    }

    let nextSong;
    if (state.shuffle && state.shuffledPlaylist.length > 0) {
        state.shuffledIndex = (state.shuffledIndex - 1 + state.shuffledPlaylist.length) % state.shuffledPlaylist.length;
        nextSong = state.shuffledPlaylist[state.shuffledIndex];
    } else {
        state.currentIndex = (state.currentIndex - 1 + state.playlist.length) % state.playlist.length;
        nextSong = state.playlist[state.currentIndex];
    }

    PlayerLog.debug('PLAYBACK', 'playPrevious', PlayerLog.trackInfo(nextSong));
    playSong(nextSong);
}

function playNext() {
    if (state.playlist.length === 0) {
        PlayerLog.debug('PLAYBACK', 'playNext: playlist vacía');
        return;
    }

    let nextSong;
    if (state.shuffle) {
        // En modo shuffle:
        // 1. Usar la primera canción de la cola persistente (upNextQueue)
        // 2. Si no hay cola, generarla

        const playableSongs = state.allSongs.filter(isTrackPlayable);
        if (state.upNextQueue.length === 0) {
            const currentKey = state.currentSong ? `${state.currentSong.id}_${state.currentSong.tipo}` : null;
            fillUpNextQueue(20, currentKey, playableSongs);
        }

        // Sacar la siguiente de la cola "A continuación"
        if (state.upNextQueue.length > 0) {
            const nextItem = state.upNextQueue.shift(); // Consumir permanentemente
            nextSong = nextItem.song;

            // Actualizar playlist y shuffledIndex para consistencia
            state.shuffledIndex = state.shuffledPlaylist.findIndex(s => s.id === nextSong.id && s.tipo === nextSong.tipo);
            if (state.shuffledIndex === -1) state.shuffledIndex = 0; // Fallback

            // Rellenar para que nunca se acabe
            const currentKey = `${nextSong.id}_${nextSong.tipo}`;
            fillUpNextQueue(20, currentKey, playableSongs);

            // Si estamos en la vista de cola, actualizar para reflejar el cambio (shift)
            if (state.currentTab === 'queue') {
                renderQueue();
            }
        } else {
            // Fallback si falla todo
            state.shuffledIndex = (state.shuffledIndex + 1) % state.shuffledPlaylist.length;
            nextSong = state.shuffledPlaylist[state.shuffledIndex];
        }

    } else {
        state.currentIndex = (state.currentIndex + 1) % state.playlist.length;
        nextSong = state.playlist[state.currentIndex];
    }

    PlayerLog.debug('PLAYBACK', 'playNext', PlayerLog.trackInfo(nextSong));
    playSong(nextSong);
}

function handleSongEnd() {
    if (state.repeat === 'one') {
        state.audio.currentTime = 0;
        state.audio.play();
        return;
    }

    // Verificar si es la última canción
    // En shuffle infinito esto no aplica, pero mantenemos por consistencia
    const isLast = !state.shuffle && state.currentIndex >= state.playlist.length - 1;

    if (isLast && state.repeat === 'off') {
        state.isPlaying = false;
        updatePlayButtons();
        return;
    }

    // ANIMACIÓN:
    // Si estamos viendo la lista de cola (no importa si full player o no, el usuario quiere verla)
    // El contenedor es #queueUpNext dentro de #libraryList (si tab=queue)
    // Asumimos que si estamos en la tab Queue, queremos ver la animación.

    if (state.currentTab === 'queue' && state.fullPlayerVisible === false) {
        animateQueueTransition(0, () => {
            // En shuffle, playNext ya hace el shift y renderQueue
            // En normal, playNext avanza índice
            playNext();
        });
    } else {
        playNext();
    }
}

function playShuffleAll() {
    // Filtrar canciones que tienen archivo de audio usando source of truth
    const songsWithAudio = state.allSongs.filter(isTrackPlayable);

    PlayerLog.info('SHUFFLE', 'Iniciando reproducción aleatoria', {
        totalCanciones: state.allSongs.length,
        conAudio: songsWithAudio.length
    });

    if (songsWithAudio.length === 0) {
        PlayerLog.warn('SHUFFLE', 'No hay canciones reproducibles');
        showToast('No hay canciones con audio disponibles');
        return;
    }

    showToast(`${songsWithAudio.length} canciones disponibles`);
    state.playlist = [...songsWithAudio];

    state.shuffle = true;
    state.skipAttempts = 0;
    createShuffledPlaylist();

    state.shuffledIndex = 0;
    playSong(state.shuffledPlaylist[0]);

    elements.fullShuffleBtn?.classList.add('active');
    elements.miniShuffleBtn?.classList.add('active');
    localStorage.setItem('playerShuffle', 'true');
}


function createShuffledPlaylist() {
    state.shuffledPlaylist = [...state.playlist];
    // Fisher-Yates shuffle
    for (let i = state.shuffledPlaylist.length - 1; i > 0; i--) {
        const j = Math.floor(Math.random() * (i + 1));
        [state.shuffledPlaylist[i], state.shuffledPlaylist[j]] =
            [state.shuffledPlaylist[j], state.shuffledPlaylist[i]];
    }
}

function toggleShuffle() {
    state.shuffle = !state.shuffle;
    elements.fullShuffleBtn?.classList.toggle('active', state.shuffle);
    elements.miniShuffleBtn?.classList.toggle('active', state.shuffle);

    if (state.shuffle) {
        createShuffledPlaylist();
        // Mover canción actual al inicio
        if (state.currentSong) {
            const idx = state.shuffledPlaylist.findIndex(
                s => s.id === state.currentSong.id && s.tipo === state.currentSong.tipo
            );
            if (idx > 0) {
                [state.shuffledPlaylist[0], state.shuffledPlaylist[idx]] =
                    [state.shuffledPlaylist[idx], state.shuffledPlaylist[0]];
            }
            state.shuffledIndex = 0;
        }
    }

    localStorage.setItem('playerShuffle', state.shuffle.toString());
    showToast(state.shuffle ? 'Aleatorio activado' : 'Aleatorio desactivado');
}

function toggleRepeat() {
    const modes = ['off', 'all', 'one'];
    const currentIdx = modes.indexOf(state.repeat);
    state.repeat = modes[(currentIdx + 1) % modes.length];

    updateRepeatButton();
    localStorage.setItem('playerRepeat', state.repeat);

    const messages = {
        'off': 'Repetir desactivado',
        'all': 'Repetir todo',
        'one': 'Repetir una'
    };
    showToast(messages[state.repeat]);
}

function updateRepeatButton() {
    state.repeat = state.repeat || 'off'; // Asegurar valor

    // Update Full Player Button
    if (elements.fullRepeatBtn) {
        elements.fullRepeatBtn.classList.toggle('active', state.repeat !== 'off');
        const svg = elements.fullRepeatBtn.querySelector('svg');
        if (svg) {
            if (state.repeat === 'one') {
                svg.innerHTML = '<polyline points="17 1 21 5 17 9" /><path d="M3 11V9a4 4 0 0 1 4-4h14" /><polyline points="7 23 3 19 7 15" /><path d="M21 13v2a4 4 0 0 1-4 4H3" /><text x="10" y="16" font-size="8" font-weight="bold" fill="currentColor">1</text>';
            } else {
                svg.innerHTML = '<polyline points="17 1 21 5 17 9" /><path d="M3 11V9a4 4 0 0 1 4-4h14" /><polyline points="7 23 3 19 7 15" /><path d="M21 13v2a4 4 0 0 1-4 4H3" />';
            }
        }
    }

    // Update Mini Player Button
    if (elements.miniRepeatBtn) {
        elements.miniRepeatBtn.classList.toggle('active', state.repeat !== 'off');
        // Opcional: Actualizar icono si se quiere mostrar el "1" en el mini player
        const miniSvg = elements.miniRepeatBtn.querySelector('svg');
        if (miniSvg) {
            if (state.repeat === 'one') {
                miniSvg.innerHTML = '<polyline points="17 1 21 5 17 9" /><path d="M3 11V9a4 4 0 0 1 4-4h14" /><polyline points="7 23 3 19 7 15" /><path d="M21 13v2a4 4 0 0 1-4 4H3" /><text x="10" y="16" font-size="8" font-weight="bold" fill="currentColor">1</text>';
            } else {
                miniSvg.innerHTML = '<polyline points="17 1 21 5 17 9" /><path d="M3 11V9a4 4 0 0 1 4-4h14" /><polyline points="7 23 3 19 7 15" /><path d="M21 13v2a4 4 0 0 1-4 4H3" />';
            }
        }
    }

    const svg = elements.fullRepeatBtn.querySelector('svg use');
    if (svg) {
        // Cambiar icono según modo
        // Por simplicidad, solo cambiamos el estado activo
    }
}

async function toggleLike() {
    if (!state.currentSong) return;

    const songId = state.currentSong.id;
    const tipo = state.currentSong.tipo;

    // Optimistic UI update - mostrar inmediatamente
    const willBeFavorite = !isFavorite(songId, tipo);
    elements.fullLikeBtn?.classList.toggle('active', willBeFavorite);
    updateLikeButtonIcon(willBeFavorite);

    // Ejecutar toggle en backend
    await toggleFavorite(songId, tipo);

    // Sincronizar estado final (por si falló)
    const isNowFavorite = isFavorite(songId, tipo);
    elements.fullLikeBtn?.classList.toggle('active', isNowFavorite);
    updateLikeButtonIcon(isNowFavorite);
}

function updateLikeButtonIcon(isFav) {
    if (!elements.fullLikeBtn) return;
    const svg = elements.fullLikeBtn.querySelector('svg');
    if (svg) {
        svg.setAttribute('fill', isFav ? 'currentColor' : 'none');
    }
}


function getUpNextSongs(count) {
    if (state.playlist.length === 0) return [];

    const songs = state.shuffle ? state.shuffledPlaylist : state.playlist;
    const currentIdx = state.shuffle ? state.shuffledIndex : state.currentIndex;

    const upNext = [];
    let checked = 0;
    let i = 1;

    // Buscar hasta encontrar 'count' canciones reproducibles
    while (upNext.length < count && checked < songs.length) {
        const idx = (currentIdx + i) % songs.length;
        const song = songs[idx];
        if (isTrackPlayable(song)) {
            upNext.push(song);
        }
        i++;
        checked++;
    }

    return upNext;
}

// ============================================
// PROGRESO Y UI
// ============================================

function updateProgress() {
    if (!state.audio.duration) return;

    const progress = (state.audio.currentTime / state.audio.duration) * 100;

    if (elements.miniProgressBar) {
        elements.miniProgressBar.style.width = `${progress}%`;
    }

    if (elements.fullProgressFill) {
        elements.fullProgressFill.style.width = `${progress}%`;
    }

    if (elements.fullCurrentTime) {
        elements.fullCurrentTime.textContent = formatTime(state.audio.currentTime);
    }
}

function updateDuration() {
    if (elements.fullDuration && state.audio.duration) {
        elements.fullDuration.textContent = formatTime(state.audio.duration);
    }
}

function handleSeek(e) {
    if (!state.audio.duration) return;

    const rect = elements.fullProgressBar.getBoundingClientRect();
    const percent = (e.clientX - rect.left) / rect.width;
    state.audio.currentTime = percent * state.audio.duration;
}

function updatePlayButtons() {
    const icon = state.isPlaying ? pauseIcon : playIcon;

    if (elements.miniPlayBtn) {
        elements.miniPlayBtn.innerHTML = icon;
    }

    if (elements.fullPlayBtn) {
        elements.fullPlayBtn.innerHTML = icon;
    }
}

function updateMiniPlayer() {
    if (!state.currentSong) return;

    const songName = state.currentSong.tema || state.currentSong.nombre || 'Sin título';
    const artistName = state.currentSong.interprete || state.currentSong.artista || '';

    if (elements.miniTitle) {
        elements.miniTitle.textContent = songName;
    }

    if (elements.miniArtist) {
        elements.miniArtist.textContent = artistName;
    }

    if (elements.miniCover) {
        if (state.currentSong.tienePortada || state.currentSong.tienePortadaEmbebida || state.currentSong.idAlbum) {
            elements.miniCover.src = getCoverUrl(state.currentSong);
            elements.miniCover.style.display = 'block';
        } else {
            elements.miniCover.style.display = 'none';
        }
    }
}

function updateFullPlayer() {
    if (!state.currentSong) return;

    const songName = state.currentSong.tema || state.currentSong.nombre || 'Sin título';
    const artistName = state.currentSong.interprete || state.currentSong.artista || '';

    if (elements.fullTitle) {
        elements.fullTitle.textContent = songName;
    }

    if (elements.fullArtist) {
        elements.fullArtist.textContent = artistName;
    }

    if (elements.fullCover) {
        if (state.currentSong.tienePortada || state.currentSong.tienePortadaEmbebida || state.currentSong.idAlbum) {
            elements.fullCover.src = getCoverUrl(state.currentSong);
            elements.fullCover.style.display = 'block';
            document.querySelector('.full-player-cover-placeholder')?.classList.add('hidden');
        } else {
            elements.fullCover.style.display = 'none';
            document.querySelector('.full-player-cover-placeholder')?.classList.remove('hidden');
        }
    }

    // Actualizar duración si ya está disponible
    updateDuration();

    // Actualizar estado del botón Like
    const songIsFav = isFavorite(state.currentSong.id, state.currentSong.tipo);
    elements.fullLikeBtn?.classList.toggle('active', songIsFav);
    updateLikeButtonIcon(songIsFav);
}


function showMiniPlayer() {
    if (!state.fullPlayerVisible) {
        elements.miniPlayer?.classList.remove('hidden');
    }
}

function openFullPlayer() {
    if (elements.fullPlayer) {
        elements.fullPlayer.classList.add('visible');
        state.fullPlayerVisible = true;

        // Ocultar mini player cuando se abre el grande
        elements.miniPlayer?.classList.add('hidden');

        // Activar visualizador si existe
        if (state.visualizer) {
            // AJUSTE CRÍTICO: Recalcular tamaño ya que al estar oculto podía ser 0
            const container = document.getElementById('visualizerContainer');
            if (container) {
                state.visualizer.setCanvasSize(container.offsetWidth, container.offsetHeight);
            }

            // Activar si está reproduciendo
            if (state.isPlaying) {
                state.visualizer.toggleAnalyzer(true);
            }
        }
    }
    updateFullPlayer();
}

function closeFullPlayer() {
    if (elements.fullPlayer) {
        elements.fullPlayer.classList.remove('visible');
        state.fullPlayerVisible = false;

        // Mostrar mini player cuando se cierra el grande (si hay una canción cargada)
        if (state.currentSong) {
            elements.miniPlayer?.classList.remove('hidden');
        }

        // Pausar visualizador para ahorrar batería/CPU
        if (state.visualizer) {
            state.visualizer.toggleAnalyzer(false);
        }
    }
}

// ============================================
// UTILIDADES
// ============================================

function getCoverUrl(song) {
    if (song.tienePortada || song.tienePortadaEmbebida) {
        return `/api/canciones/${song.id}/portada?tipo=${song.tipo}`;
    }
    if (song.idAlbum) {
        return `/api/albumes/${song.idAlbum}/portada`;
    }
    return '';
}

function formatTime(seconds) {
    if (!seconds || !isFinite(seconds) || isNaN(seconds) || seconds < 0) return '0:00';
    const mins = Math.floor(seconds / 60);
    const secs = Math.floor(seconds % 60);
    return `${mins}:${secs.toString().padStart(2, '0')}`;
}

function escapeHtml(str) {
    if (!str) return '';
    return str.replace(/[&<>"']/g, char => ({
        '&': '&amp;',
        '<': '&lt;',
        '>': '&gt;',
        '"': '&quot;',
        "'": '&#39;'
    })[char]);
}

function debounce(func, wait) {
    let timeout;
    return function (...args) {
        clearTimeout(timeout);
        timeout = setTimeout(() => func.apply(this, args), wait);
    };
}

function showToast(message) {
    if (!elements.toast) return;

    elements.toast.textContent = message;
    elements.toast.classList.add('visible');

    setTimeout(() => {
        elements.toast.classList.remove('visible');
    }, 2000);
}

function saveState() {
    if (state.currentSong) {
        localStorage.setItem('playerCurrentSong', JSON.stringify(state.currentSong));
    }
}

// ============================================
// ICONOS SVG
// ============================================



// ============================================
// RENDERIZADO DEL INICIO
// ============================================

function renderHomeAlbums() {
    const container = document.getElementById('homeAlbums');
    if (!container || !state.allAlbums) return;

    // Obtener álbumes con portada (priorizamos los que tienen imagen)
    const albumsWithCover = state.allAlbums.filter(a => a.tienePortada).slice(0, 15);
    const albumsToShow = albumsWithCover.length >= 5 ? albumsWithCover : state.allAlbums.slice(0, 15);

    if (albumsToShow.length === 0) {
        container.innerHTML = '<p style="color: var(--player-text-subdued);">No hay álbumes</p>';
        return;
    }

    container.innerHTML = albumsToShow.map(album => `
            <div class="album-card-home" data-id="${album.id}">
                ${album.tienePortada ?
            `<img class="album-cover" src="/api/albumes/${album.id}/portada" alt="${escapeHtml(album.nombre)}" loading="lazy" onerror="this.style.display='none'; this.nextElementSibling.style.display='flex';">
                     <div class="album-cover-placeholder" style="display: none;">${musicIcon}</div>` :
            `<div class="album-cover-placeholder">${musicIcon}</div>`
        }
                <div class="album-name">${escapeHtml(album.nombre)}</div>
                <div class="album-artist">${escapeHtml(album.interprete || '')}</div>
            </div>
        `).join('');

    // Click listeners para álbumes - abrir página de detalle
    container.querySelectorAll('.album-card-home').forEach(card => {
        card.addEventListener('click', () => {
            const albumId = parseInt(card.dataset.id);
            const album = state.allAlbums.find(a => a.id === albumId);
            if (album) {
                showAlbumDetail(album);
            }
        });
    });
}


function renderHomeArtists() {
    const container = document.getElementById('homeArtists');
    if (!container || !state.allArtists) return;

    // Obtener artistas con foto (priorizamos los que tienen imagen)
    const artistsWithPhoto = state.allArtists.filter(a => a.tieneFoto).slice(0, 15);
    const artistsToShow = artistsWithPhoto.length >= 4 ? artistsWithPhoto : state.allArtists.slice(0, 15);

    if (artistsToShow.length === 0) {
        container.innerHTML = '<p style="color: var(--player-text-subdued);">No hay artistas</p>';
        return;
    }

    container.innerHTML = artistsToShow.map(artist => `
            <div class="artist-card-home" data-id="${artist.id}">
                ${artist.tieneFoto ?
            `<img class="artist-photo" src="/api/interpretes/${artist.id}/foto" alt="${escapeHtml(artist.nombre)}" loading="lazy" onerror="this.style.display='none'; this.nextElementSibling.style.display='flex';">
                     <div class="artist-photo-placeholder" style="display: none;">${userIcon}</div>` :
            `<div class="artist-photo-placeholder">${userIcon}</div>`
        }
                <div class="artist-name">${escapeHtml(artist.nombre)}</div>
            </div>
        `).join('');

    // Click listeners para artistas - abrir página de detalle
    container.querySelectorAll('.artist-card-home').forEach(card => {
        card.addEventListener('click', () => {
            const artistId = parseInt(card.dataset.id);
            const artist = state.allArtists.find(a => a.id === artistId);
            if (artist) {
                showArtistDetail(artist);
            }
        });
    });
}


function renderHomePlaylists() {
    const container = document.getElementById('homePlaylists');
    if (!container) return;

    let html = '';

    // Renderizar playlists existentes
    state.userPlaylists.forEach(playlist => {
        html += `
                <button class="playlist-card" data-playlist-id="${playlist.id}">
                    <div class="playlist-cover">${listMusicIcon}</div>
                    <div class="playlist-info">
                        <div class="playlist-name">${escapeHtml(playlist.name)}</div>
                        <div class="playlist-count">${playlist.songs.length} canciones</div>
                    </div>
                </button>
            `;
    });

    // Botón para crear nueva playlist
    html += `
            <button id="btnNewPlaylist" class="playlist-card new-playlist">
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <line x1="12" y1="5" x2="12" y2="19"/>
                    <line x1="5" y1="12" x2="19" y2="12"/>
                </svg>
                <span>Crear Playlist</span>
            </button>
        `;

    container.innerHTML = html;

    // Event listeners - abrir vista de detalle de playlist
    container.querySelectorAll('.playlist-card[data-playlist-id]').forEach(card => {
        card.addEventListener('click', () => {
            const playlistId = card.dataset.playlistId;
            const playlist = state.userPlaylists.find(p => p.id === playlistId);
            if (playlist) {
                showPlaylistDetail(playlist);
            }
        });
    });

    document.getElementById('btnNewPlaylist')?.addEventListener('click', openPlaylistModal);
}


// ============================================
// SISTEMA DE PLAYLISTS
// ============================================

function createPlaylist(name) {
    const newPlaylist = {
        id: Date.now().toString(),
        name: name.trim(),
        songs: [],
        createdAt: new Date().toISOString()
    };
    state.userPlaylists.push(newPlaylist);
    savePlaylists();
    renderHomePlaylists();
    return newPlaylist;
}

function addToPlaylist(playlistId, song) {
    const playlist = state.userPlaylists.find(p => p.id === playlistId);
    if (!playlist) return false;

    // Evitar duplicados
    const exists = playlist.songs.some(s => s.id === song.id && s.tipo === song.tipo);
    if (exists) {
        showToast('La canción ya está en esta playlist');
        return false;
    }

    playlist.songs.push({
        id: song.id,
        tipo: song.tipo,
        tema: song.tema || song.nombre,
        interprete: song.interprete || song.artista
    });
    savePlaylists();
    showToast(`Agregado a "${playlist.name}"`);
    return true;
}

async function playUserPlaylist(playlistId) {
    const playlist = state.userPlaylists.find(p => p.id === playlistId);
    if (!playlist || playlist.songs.length === 0) {
        showToast('Esta playlist está vacía');
        return;
    }

    // Resolver referencias a canciones completas desde allSongs
    const resolvedSongs = playlist.songs
        .map(ref => state.allSongs.find(s => s.id === ref.id && s.tipo === ref.tipo))
        .filter(Boolean)
        .filter(isTrackPlayable);  // Solo las reproducibles

    if (resolvedSongs.length === 0) {
        PlayerLog.warn('PLAYLIST', 'No hay canciones reproducibles en esta playlist', {
            playlistId,
            totalSongs: playlist.songs.length
        });
        showToast('No hay canciones reproducibles en esta playlist');
        return;
    }

    PlayerLog.info('PLAYLIST', 'Reproduciendo playlist', {
        name: playlist.name,
        totalTracks: resolvedSongs.length
    });

    state.currentPlaylistId = playlistId;
    state.playlist = resolvedSongs;
    state.currentIndex = 0;
    playSong(state.playlist[0]);
    showToast(`Reproduciendo "${playlist.name}"`);
}


// ============================================
// MODALS
// ============================================

function openSettingsModal() {
    const modal = document.getElementById('settingsModal');
    const toggleAudio = document.getElementById('settingShowWithoutAudio');
    const toggleCrossfade = document.getElementById('settingCrossfade');

    if (modal) {
        if (toggleAudio) toggleAudio.checked = state.settings.showSongsWithoutAudio;
        if (toggleCrossfade) toggleCrossfade.checked = state.settings.enableCrossfade;
        if (elements.settingUserName) elements.settingUserName.value = state.settings.userName || '';
        modal.style.display = 'flex';
    }

    // Listeners (si no se han agregado antes, pero es mejor agregarlos en init o setupEventListeners)
    // Para simplificar, nos aseguramos que los inputs tengan listeners, 
    // pero mejor hacerlo en un lugar centralizado si es posible. 
    // Dado el diseño actual, vamos a agregar listeners aquí si no existen o confiar en setupEventListeners.
    // REVISIÓN: La función setupEventListeners no cubría estos inputs nuevos. 
    // Vamos a agregar los listeners una sola vez en setupEventListeners mejor.
}

function closeSettingsModal() {
    const modal = document.getElementById('settingsModal');
    if (modal) modal.style.display = 'none';
}

function openPlaylistModal() {
    const modal = document.getElementById('playlistModal');
    const input = document.getElementById('playlistNameInput');
    if (modal && input) {
        input.value = '';
        modal.style.display = 'flex';
        setTimeout(() => input.focus(), 100);
    }
}

function closePlaylistModal() {
    const modal = document.getElementById('playlistModal');
    if (modal) modal.style.display = 'none';
}

function handleCreatePlaylist() {
    const input = document.getElementById('playlistNameInput');
    const name = input?.value.trim();
    if (name) {
        createPlaylist(name);
        closePlaylistModal();
        showToast(`Playlist "${name}" creada`);
    } else {
        showToast('Ingresa un nombre para la playlist');
    }
}

// ============================================
// VISTAS DE DETALLE
// ============================================

function showAlbumDetail(album) {
    state.detailView = { type: 'album', data: album };

    // Obtener canciones del álbum
    const albumSongs = state.allSongs.filter(s => s.idAlbum == album.id);
    const songsWithAudio = albumSongs.filter(s => s.archivoAudio && s.archivoAudio.trim() !== '');

    // Renderizar la vista de detalle
    const content = `
            <div class="detail-view">
                <button class="detail-back-btn" id="detailBackBtn">
                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <polyline points="15 18 9 12 15 6"/>
                    </svg>
                    Volver
                </button>
                
                <div class="detail-header">
                    ${album.tienePortada ?
            `<img class="detail-cover" src="/api/albumes/${album.id}/portada" alt="${escapeHtml(album.nombre)}">` :
            `<div class="detail-cover-placeholder">${musicIcon}</div>`
        }
                    <div class="detail-info">
                        <h1 class="detail-title">${escapeHtml(album.nombre)}</h1>
                        <p class="detail-subtitle">${escapeHtml(album.interprete || 'Varios artistas')}</p>
                        <p class="detail-stats">${albumSongs.length} canciones • ${songsWithAudio.length} con audio</p>
                    </div>
                </div>

                <div class="detail-actions">
                    <button class="detail-play-btn" id="playAllAlbum" ${songsWithAudio.length === 0 ? 'disabled' : ''}>
                        ${playIcon} Reproducir Todo
                    </button>
                    <button class="detail-shuffle-btn" id="shuffleAlbum" ${songsWithAudio.length === 0 ? 'disabled' : ''}>
                        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <polyline points="16 3 21 3 21 8"/>
                            <line x1="4" y1="20" x2="21" y2="3"/>
                            <polyline points="21 16 21 21 16 21"/>
                            <line x1="15" y1="15" x2="21" y2="21"/>
                            <line x1="4" y1="4" x2="9" y2="9"/>
                        </svg>
                    </button>
                </div>

                <div class="detail-songs-list">
                    ${albumSongs.map((song, idx) => {
            const hasAudio = song.archivoAudio && song.archivoAudio.trim() !== '';
            return `
                            <div class="detail-song-item ${hasAudio ? '' : 'no-audio'}" data-id="${song.id}" data-tipo="${song.tipo}">
                                <span class="detail-song-num">${idx + 1}</span>
                                <div class="detail-song-info">
                                    <div class="detail-song-title">${escapeHtml(song.tema || song.nombre)}</div>
                                    <div class="detail-song-artist">${escapeHtml(song.interprete || '')}${hasAudio ? '' : ' • Sin audio'}</div>
                                </div>
                            </div>
                        `;
        }).join('')}
                </div>
            </div>
        `;

    // Mostrar en biblioteca
    elements.libraryList.innerHTML = content;
    switchTab('library');

    // Event listeners
    document.getElementById('detailBackBtn')?.addEventListener('click', () => {
        state.detailView = null;
        renderLibrary();
    });

    document.getElementById('playAllAlbum')?.addEventListener('click', async () => {
        if (songsWithAudio.length > 0) {
            state.playlist = [...songsWithAudio];
            state.currentIndex = 0;
            await playSong(state.playlist[0]);
            showToast(`Reproduciendo ${album.nombre}`);
        }
    });

    document.getElementById('shuffleAlbum')?.addEventListener('click', async () => {
        if (songsWithAudio.length > 0) {
            state.playlist = [...songsWithAudio];
            state.shuffle = true;
            createShuffledPlaylist();
            state.shuffledIndex = 0;
            await playSong(state.shuffledPlaylist[0]);
            showToast(`Reproduciendo ${album.nombre} en aleatorio`);
        }
    });

    // Click en canciones
    document.querySelectorAll('.detail-song-item').forEach(item => {
        item.addEventListener('click', async () => {
            const songId = parseInt(item.dataset.id);
            const tipo = item.dataset.tipo;
            const song = albumSongs.find(s => s.id === songId && s.tipo === tipo);
            if (song) {
                state.playlist = songsWithAudio.length > 0 ? [...songsWithAudio] : [...albumSongs];
                state.currentIndex = state.playlist.findIndex(s => s.id === songId && s.tipo === tipo);
                await playSong(song);
            }
        });
    });
}

function showArtistDetail(artist) {
    state.detailView = { type: 'artist', data: artist };

    // Nombre del artista (API usa 'interprete' como nombre, o puede ser 'nombre' en algunos casos)
    const artistName = artist.interprete || artist.nombre || '';

    // Obtener canciones del artista (match por nombre)
    const artistSongs = state.allSongs.filter(s =>
        s.interprete && s.interprete.toLowerCase() === artistName.toLowerCase()
    );
    const songsWithAudio = artistSongs.filter(s => s.archivoAudio && s.archivoAudio.trim() !== '');

    // Obtener álbumes del artista
    const artistAlbums = state.allAlbums.filter(a =>
        a.idInterprete == artist.id ||
        (a.interprete && a.interprete.toLowerCase() === artistName.toLowerCase())
    );

    // Renderizar la vista de detalle
    const content = `
            <div class="detail-view">
                <button class="detail-back-btn" id="detailBackBtn">
                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <polyline points="15 18 9 12 15 6"/>
                    </svg>
                    Volver
                </button>
                
                <div class="detail-header artist-detail">
                    ${artist.tieneFoto ?
            `<img class="detail-cover artist-photo" src="/api/interpretes/${artist.id}/foto" alt="${escapeHtml(artistName)}">` :
            `<div class="detail-cover-placeholder artist-photo">${userIcon}</div>`
        }
                    <div class="detail-info">
                        <h1 class="detail-title">${escapeHtml(artistName)}</h1>
                        <p class="detail-stats">${artistSongs.length} canciones • ${songsWithAudio.length} con audio</p>
                        ${artistAlbums.length > 0 ? `<p class="detail-stats">${artistAlbums.length} álbumes</p>` : ''}
                    </div>
                </div>

                <div class="detail-actions">
                    <button class="detail-play-btn" id="playAllArtist" ${songsWithAudio.length === 0 ? 'disabled' : ''}>
                        ${playIcon} Reproducir Todo
                    </button>
                    <button class="detail-shuffle-btn" id="shuffleArtist" ${songsWithAudio.length === 0 ? 'disabled' : ''}>
                        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <polyline points="16 3 21 3 21 8"/>
                            <line x1="4" y1="20" x2="21" y2="3"/>
                            <polyline points="21 16 21 21 16 21"/>
                            <line x1="15" y1="15" x2="21" y2="21"/>
                            <line x1="4" y1="4" x2="9" y2="9"/>
                        </svg>
                    </button>
                </div>

                ${artistAlbums.length > 0 ? `
                <div class="artist-albums-section">
                    <h2 class="section-title">Álbumes</h2>
                    <div class="artist-albums-grid">
                        ${artistAlbums.map(album => `
                            <div class="album-card-mini" data-id="${album.id}">
                                ${album.tienePortada ?
                `<img class="album-mini-cover" src="/api/albumes/${album.id}/portada" alt="" loading="lazy" onerror="this.style.display='none'">` :
                `<div class="album-mini-placeholder">${musicIcon}</div>`
            }
                                <div class="album-mini-name">${escapeHtml(album.nombre)}</div>
                                <div class="album-mini-year">${album.anio || ''}</div>
                            </div>
                        `).join('')}
                    </div>
                </div>
                ` : ''}

                <div class="artist-songs-section">
                    <h2 class="section-title">Canciones</h2>
                    <div class="detail-songs-list">
                        ${artistSongs.map((song, idx) => {
                const hasAudio = song.archivoAudio && song.archivoAudio.trim() !== '';
                return `
                                <div class="detail-song-item ${hasAudio ? '' : 'no-audio'}" data-id="${song.id}" data-tipo="${song.tipo}">
                                    <span class="detail-song-num">${idx + 1}</span>
                                    <div class="detail-song-info">
                                        <div class="detail-song-title">${escapeHtml(song.tema || song.nombre)}</div>
                                        <div class="detail-song-artist">${escapeHtml(song.albumNombre || '')}${hasAudio ? '' : ' • Sin audio'}</div>
                                    </div>
                                </div>
                            `;
            }).join('')}
                    </div>
                </div>
            </div>
        `;

    // Mostrar en biblioteca
    elements.libraryList.innerHTML = content;
    switchTab('library');

    // Event listeners
    document.getElementById('detailBackBtn')?.addEventListener('click', () => {
        state.detailView = null;
        renderLibrary();
    });

    document.getElementById('playAllArtist')?.addEventListener('click', async () => {
        if (songsWithAudio.length > 0) {
            state.playlist = [...songsWithAudio];
            state.currentIndex = 0;
            await playSong(state.playlist[0]);
            showToast(`Reproduciendo ${artist.nombre}`);
        }
    });

    document.getElementById('shuffleArtist')?.addEventListener('click', async () => {
        if (songsWithAudio.length > 0) {
            state.playlist = [...songsWithAudio];
            state.shuffle = true;
            createShuffledPlaylist();
            state.shuffledIndex = 0;
            await playSong(state.shuffledPlaylist[0]);
            showToast(`Reproduciendo ${artist.nombre} en aleatorio`);
        }
    });

    // Click en canciones
    document.querySelectorAll('.detail-song-item').forEach(item => {
        item.addEventListener('click', async () => {
            const songId = parseInt(item.dataset.id);
            const tipo = item.dataset.tipo;
            const song = artistSongs.find(s => s.id === songId && s.tipo === tipo);
            if (song) {
                state.playlist = songsWithAudio.length > 0 ? [...songsWithAudio] : [...artistSongs];
                state.currentIndex = state.playlist.findIndex(s => s.id === songId && s.tipo === tipo);
                await playSong(song);
            }
        });
    });

    // Click en álbumes dentro del artista
    document.querySelectorAll('.album-card-mini').forEach(card => {
        card.addEventListener('click', () => {
            const albumId = parseInt(card.dataset.id);
            const album = state.allAlbums.find(a => a.id === albumId);
            if (album) {
                showAlbumDetail(album);
            }
        });
    });
}

function showPlaylistDetail(playlist) {
    state.detailView = { type: 'playlist', data: playlist };

    // Obtener canciones de la playlist
    const playlistSongs = playlist.songs
        .map(ref => state.allSongs.find(s => s.id === ref.id && s.tipo === ref.tipo))
        .filter(Boolean);

    const songsWithAudio = playlistSongs.filter(s => s.archivoAudio && s.archivoAudio.trim() !== '');

    // Canciones disponibles para agregar (con audio, no en playlist)
    const availableSongs = state.allSongs.filter(s =>
        s.archivoAudio && s.archivoAudio.trim() !== '' &&
        !playlist.songs.some(ps => ps.id === s.id && ps.tipo === s.tipo)
    );

    const content = `
            <div class="detail-view">
                <button class="detail-back-btn" id="detailBackBtn">
                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <polyline points="15 18 9 12 15 6"/>
                    </svg>
                    Volver
                </button>
                
                <div class="detail-header">
                    <div class="detail-cover-placeholder">
                        ${listMusicIcon}
                    </div>
                    <div class="detail-info">
                        <h1 class="detail-title">${escapeHtml(playlist.name)}</h1>
                        <p class="detail-stats">${playlistSongs.length} canciones</p>
                    </div>
                </div>

                <div class="detail-actions">
                    <button class="detail-play-btn" id="playPlaylist" ${songsWithAudio.length === 0 ? 'disabled' : ''}>
                        ${playIcon} Reproducir
                    </button>
                    <button class="detail-shuffle-btn" id="shufflePlaylist" ${songsWithAudio.length === 0 ? 'disabled' : ''}>
                        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <polyline points="16 3 21 3 21 8"/>
                            <line x1="4" y1="20" x2="21" y2="3"/>
                            <polyline points="21 16 21 21 16 21"/>
                            <line x1="15" y1="15" x2="21" y2="21"/>
                            <line x1="4" y1="4" x2="9" y2="9"/>
                        </svg>
                    </button>
                </div>

                <div class="playlist-add-section">
                    <h3 class="section-title">Agregar Canciones</h3>
                    <div class="search-input-wrapper">
                        <svg class="search-icon" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <circle cx="11" cy="11" r="8"/>
                            <path d="m21 21-4.3-4.3"/>
                        </svg>
                        <input type="text" id="playlistSearchInput" class="search-input" placeholder="Buscar canciones...">
                    </div>
                    <div id="playlistSearchResults" class="playlist-search-results"></div>
                </div>

                <h3 class="section-title" style="margin-top: 1.5rem;">Canciones</h3>
                <div class="detail-songs-list" id="playlistSongsList">
                    ${playlistSongs.length === 0 ?
            '<div class="search-empty"><p>Playlist vacía</p></div>' :
            playlistSongs.map((song, idx) => `
                            <div class="detail-song-item" data-id="${song.id}" data-tipo="${song.tipo}">
                                <span class="detail-song-num">${idx + 1}</span>
                                <div class="detail-song-info">
                                    <div class="detail-song-title">${escapeHtml(song.tema || song.nombre)}</div>
                                    <div class="detail-song-artist">${escapeHtml(song.interprete || '')}</div>
                                </div>
                                <button class="remove-song-btn" data-id="${song.id}" data-tipo="${song.tipo}">✕</button>
                            </div>
                        `).join('')
        }
                </div>
            </div>
        `;

    elements.libraryList.innerHTML = content;
    switchTab('library');

    // Event listeners
    document.getElementById('detailBackBtn')?.addEventListener('click', () => {
        state.detailView = null;
        renderLibrary();
        renderHomePlaylists();
    });

    document.getElementById('playPlaylist')?.addEventListener('click', async () => {
        if (songsWithAudio.length > 0) {
            state.playlist = [...songsWithAudio];
            state.currentIndex = 0;
            await playSong(state.playlist[0]);
        }
    });

    document.getElementById('shufflePlaylist')?.addEventListener('click', async () => {
        if (songsWithAudio.length > 0) {
            state.playlist = [...songsWithAudio];
            state.shuffle = true;
            createShuffledPlaylist();
            state.shuffledIndex = 0;
            await playSong(state.shuffledPlaylist[0]);
        }
    });

    // Buscador
    const searchInput = document.getElementById('playlistSearchInput');
    const searchResults = document.getElementById('playlistSearchResults');

    searchInput?.addEventListener('input', debounce(() => {
        const query = searchInput.value.trim().toLowerCase();
        if (!query) { searchResults.innerHTML = ''; return; }

        const matches = availableSongs.filter(s => {
            const name = (s.tema || s.nombre || '').toLowerCase();
            const artist = (s.interprete || '').toLowerCase();
            return name.includes(query) || artist.includes(query);
        }).slice(0, 10);

        searchResults.innerHTML = matches.length === 0 ? '<div class="search-empty-small">Sin resultados</div>' :
            matches.map(song => `
                    <div class="playlist-add-item" data-id="${song.id}" data-tipo="${song.tipo}">
                        <div class="detail-song-info">
                            <div class="detail-song-title">${escapeHtml(song.tema || song.nombre)}</div>
                            <div class="detail-song-artist">${escapeHtml(song.interprete || '')}</div>
                        </div>
                        <button class="add-song-btn">+ Agregar</button>
                    </div>
                `).join('');

        searchResults.querySelectorAll('.add-song-btn').forEach(btn => {
            btn.addEventListener('click', (e) => {
                const item = e.target.closest('.playlist-add-item');
                const songId = parseInt(item.dataset.id);
                const tipo = item.dataset.tipo;
                addToPlaylist(playlist.id, { id: songId, tipo: tipo });
                showPlaylistDetail(state.userPlaylists.find(p => p.id === playlist.id));
            });
        });
    }, 300));

    // Click en canciones
    document.querySelectorAll('#playlistSongsList .detail-song-item').forEach(item => {
        item.addEventListener('click', async (e) => {
            if (e.target.closest('.remove-song-btn')) return;
            const songId = parseInt(item.dataset.id);
            const tipo = item.dataset.tipo;
            const song = playlistSongs.find(s => s.id === songId && s.tipo === tipo);
            if (song && songsWithAudio.length > 0) {
                state.playlist = [...songsWithAudio];
                state.currentIndex = state.playlist.findIndex(s => s.id === songId && s.tipo === tipo);
                await playSong(song);
            }
        });
    });

    // Remover canciones
    document.querySelectorAll('.remove-song-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            const songId = parseInt(btn.dataset.id);
            const tipo = btn.dataset.tipo;
            removeFromPlaylist(playlist.id, songId, tipo);
            showPlaylistDetail(state.userPlaylists.find(p => p.id === playlist.id));
        });
    });
}

function removeFromPlaylist(playlistId, songId, tipo) {
    const playlist = state.userPlaylists.find(p => p.id === playlistId);
    if (!playlist) return;
    const idx = playlist.songs.findIndex(s => s.id === songId && s.tipo === tipo);
    if (idx >= 0) {
        playlist.songs.splice(idx, 1);
        savePlaylists();
        showToast('Canción eliminada');
    }
}

// ============================================
// SETUP MODALS EVENT LISTENERS
// ============================================


function setupModals() {
    // Settings modal
    document.getElementById('btnSettings')?.addEventListener('click', openSettingsModal);
    document.getElementById('closeSettingsBtn')?.addEventListener('click', closeSettingsModal);
    document.getElementById('settingsModal')?.addEventListener('click', (e) => {
        if (e.target.id === 'settingsModal') closeSettingsModal();
    });

    // Toggle de mostrar canciones sin audio
    document.getElementById('settingShowWithoutAudio')?.addEventListener('change', (e) => {
        state.settings.showSongsWithoutAudio = e.target.checked;
        saveSettings();
        renderLibrary(); // Actualizar biblioteca
        showToast(e.target.checked ? 'Mostrando todas las canciones' : 'Solo canciones con audio');
    });

    // Cambio de nombre de usuario
    elements.settingUserName?.addEventListener('input', (e) => {
        state.settings.userName = e.target.value;
        saveSettings();
        updateGreeting(); // Actualizar saludo en tiempo real
    });

    // Playlist modal
    document.getElementById('closePlaylistModalBtn')?.addEventListener('click', closePlaylistModal);
    document.getElementById('playlistModal')?.addEventListener('click', (e) => {
        if (e.target.id === 'playlistModal') closePlaylistModal();
    });
    document.getElementById('savePlaylistBtn')?.addEventListener('click', handleCreatePlaylist);
    document.getElementById('playlistNameInput')?.addEventListener('keypress', (e) => {
        if (e.key === 'Enter') handleCreatePlaylist();
    });
}

// ============================================
// INICIAR APP
// ============================================

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
} else {
    init();
}

