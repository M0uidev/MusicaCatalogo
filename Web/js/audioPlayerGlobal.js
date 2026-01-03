// ============================================
// REPRODUCTOR GLOBAL PERSISTENTE - VERSIÓN SIMPLE
// ============================================

(function() {
    'use strict';

    // Estado global del reproductor
    const state = {
        currentSong: null,
        isPlaying: false,
        playlist: [],
        currentIndex: -1
    };

    /**
     * Inicializa el reproductor global al cargar la página
     */
    function initGlobalPlayer() {
        // Verificar si el reproductor ya existe
        if (document.getElementById('global-audio-player')) {
            restorePlayerState();
            return;
        }

        // Crear HTML del reproductor (versión simplificada)
        const playerHTML = `
            <div id="global-audio-player" class="audio-player-global" style="display: none;">
                <audio id="global-audio" preload="metadata"></audio>
                
                <div class="player-main">
                    <div class="player-cover-container">
                        <img id="player-cover" class="player-cover" alt="Portada" style="display: none;">
                        <div id="player-cover-placeholder" class="player-cover-placeholder">🎵</div>
                    </div>
                    
                    <div class="player-info">
                        <div class="player-song" id="player-song">Sin canción</div>
                        <div class="player-artist" id="player-artist"></div>
                    </div>
                    
                    <div class="player-controls">
                        <button id="player-prev" class="player-btn" title="Anterior">⏮</button>
                        <button id="player-play-pause" class="player-btn-main" title="Reproducir">▶</button>
                        <button id="player-next" class="player-btn" title="Siguiente">⏭</button>
                    </div>
                    
                    <div class="player-progress">
                        <span id="player-current-time">0:00</span>
                        <input type="range" id="player-progress-bar" value="0" min="0" max="100" step="0.1">
                        <span id="player-duration">0:00</span>
                    </div>
                    
                    <button id="player-close" class="player-close" title="Cerrar">✕</button>
                </div>
            </div>
        `;

        // Insertar en el body
        document.body.insertAdjacentHTML('beforeend', playerHTML);

        // Obtener referencias
        const player = document.getElementById('global-audio-player');
        const audio = document.getElementById('global-audio');
        const playPauseBtn = document.getElementById('player-play-pause');
        const prevBtn = document.getElementById('player-prev');
        const nextBtn = document.getElementById('player-next');
        const closeBtn = document.getElementById('player-close');
        const progressBar = document.getElementById('player-progress-bar');
        const currentTimeSpan = document.getElementById('player-current-time');
        const durationSpan = document.getElementById('player-duration');
        const coverImg = document.getElementById('player-cover');
        const coverPlaceholder = document.getElementById('player-cover-placeholder');

        // Event listeners
        playPauseBtn.addEventListener('click', () => {
            if (audio.paused) {
                audio.play().catch(err => console.error('Error al reproducir:', err));
            } else {
                audio.pause();
            }
        });

        prevBtn.addEventListener('click', () => playPrevious());
        nextBtn.addEventListener('click', () => playNext());
        closeBtn.addEventListener('click', () => {
            audio.pause();
            player.style.display = 'none';
            clearPlayerState();
        });

        // Eventos del audio
        audio.addEventListener('play', () => {
            playPauseBtn.textContent = '⏸';
            state.isPlaying = true;
            savePlayerState();
        });

        audio.addEventListener('pause', () => {
            playPauseBtn.textContent = '▶';
            state.isPlaying = false;
            savePlayerState();
        });

        audio.addEventListener('timeupdate', () => {
            if (audio.duration) {
                const progress = (audio.currentTime / audio.duration) * 100;
                progressBar.value = progress;
                currentTimeSpan.textContent = formatTime(audio.currentTime);
            }
        });

        audio.addEventListener('loadedmetadata', () => {
            durationSpan.textContent = formatTime(audio.duration);
            progressBar.max = 100;
        });

        audio.addEventListener('ended', () => {
            playNext();
        });

        audio.addEventListener('error', (e) => {
            console.error('Error de audio:', e);
            playPauseBtn.textContent = '▶';
        });

        // Barra de progreso
        progressBar.addEventListener('input', () => {
            if (audio.duration) {
                const time = (progressBar.value / 100) * audio.duration;
                audio.currentTime = time;
            }
        });

        // Restaurar estado si existe
        restorePlayerState();
    }

    /**
     * Reproduce una canción
     */
    async function playSong(idCancion, tipo) {
        const player = document.getElementById('global-audio-player');
        const audio = document.getElementById('global-audio');
        
        if (!player || !audio) {
            console.error('Reproductor no inicializado');
            return;
        }

        try {
            // Cargar información de la canción
            const resp = await fetch(`/api/canciones/${idCancion}?tipo=${tipo}`);
            if (!resp.ok) {
                alert('Error al cargar la canción');
                return;
            }

            const cancion = await resp.json();
            
            // Verificar que tenga archivo de audio
            if (!cancion.tieneArchivoAudio) {
                alert('Esta canción no tiene archivo de audio');
                return;
            }

            // Cargar playlist (todas las canciones del medio)
            await loadPlaylist(cancion.numMedio, tipo);

            // Encontrar índice de la canción actual
            state.currentIndex = state.playlist.findIndex(c => c.id === idCancion && c.tipo === tipo);
            if (state.currentIndex === -1) {
                state.currentIndex = 0;
            }

            state.currentSong = cancion;

            // Actualizar UI
            updatePlayerUI(cancion);

            // Cargar audio
            audio.src = `/api/canciones/${idCancion}/audio?tipo=${tipo}`;
            audio.load();
            
            // Mostrar reproductor
            player.style.display = 'block';

            // Reproducir
            setTimeout(() => {
                audio.play().catch(err => {
                    console.error('Error al reproducir:', err);
                });
            }, 100);

            savePlayerState();

        } catch (error) {
            console.error('Error al cargar canción:', error);
            alert('Error al cargar la canción');
        }
    }

    /**
     * Carga la playlist de canciones del medio
     */
    async function loadPlaylist(numMedio, tipo) {
        try {
            // Buscar el medio
            const medioResp = await fetch(`/api/medios?tipo=${tipo}`);
            if (!medioResp.ok) return;
            
            const medios = await medioResp.json();
            const medio = medios.find(m => m.num === numMedio);
            
            if (!medio || !medio.canciones) return;

            // Filtrar solo canciones con audio
            state.playlist = medio.canciones
                .filter(c => c.tieneArchivoAudio)
                .map(c => ({
                    id: c.id,
                    tipo: tipo,
                    tema: c.tema,
                    interprete: c.interprete,
                    idAlbum: c.idAlbum
                }));

        } catch (error) {
            console.error('Error cargando playlist:', error);
            state.playlist = [];
        }
    }

    /**
     * Actualiza la UI del reproductor
     */
    function updatePlayerUI(cancion) {
        const songSpan = document.getElementById('player-song');
        const artistSpan = document.getElementById('player-artist');
        const coverImg = document.getElementById('player-cover');
        const coverPlaceholder = document.getElementById('player-cover-placeholder');

        songSpan.textContent = cancion.tema || 'Sin título';
        artistSpan.textContent = cancion.interprete || '';

        // Cargar portada
        if (cancion.tienePortada) {
            coverImg.src = `/api/canciones/${cancion.id}/portada?tipo=${cancion.tipo}`;
            coverImg.style.display = 'block';
            coverPlaceholder.style.display = 'none';
        } else if (cancion.idAlbum && cancion.tienePortadaAlbum) {
            coverImg.src = `/api/albumes/${cancion.idAlbum}/portada`;
            coverImg.style.display = 'block';
            coverPlaceholder.style.display = 'none';
        } else {
            coverImg.style.display = 'none';
            coverPlaceholder.style.display = 'flex';
        }

        coverImg.onerror = () => {
            coverImg.style.display = 'none';
            coverPlaceholder.style.display = 'flex';
        };
    }

    /**
     * Reproduce la canción anterior
     */
    function playPrevious() {
        if (state.playlist.length === 0) return;
        
        state.currentIndex--;
        if (state.currentIndex < 0) {
            state.currentIndex = state.playlist.length - 1;
        }
        
        const song = state.playlist[state.currentIndex];
        playSong(song.id, song.tipo);
    }

    /**
     * Reproduce la siguiente canción
     */
    function playNext() {
        if (state.playlist.length === 0) return;
        
        state.currentIndex++;
        if (state.currentIndex >= state.playlist.length) {
            state.currentIndex = 0;
        }
        
        const song = state.playlist[state.currentIndex];
        playSong(song.id, song.tipo);
    }

    /**
     * Formatea segundos a mm:ss
     */
    function formatTime(seconds) {
        if (!seconds || !isFinite(seconds)) return '0:00';
        const mins = Math.floor(seconds / 60);
        const secs = Math.floor(seconds % 60);
        return `${mins}:${secs.toString().padStart(2, '0')}`;
    }

    /**
     * Guarda el estado del reproductor en localStorage
     */
    function savePlayerState() {
        try {
            const audio = document.getElementById('global-audio');
            if (!audio || !state.currentSong) return;

            localStorage.setItem('audioPlayerState', JSON.stringify({
                songId: state.currentSong.id,
                songType: state.currentSong.tipo,
                currentTime: audio.currentTime,
                isPlaying: state.isPlaying,
                timestamp: Date.now()
            }));
        } catch (e) {
            console.warn('Error guardando estado:', e);
        }
    }

    /**
     * Restaura el estado del reproductor desde localStorage
     */
    function restorePlayerState() {
        try {
            const saved = localStorage.getItem('audioPlayerState');
            if (!saved) return;

            const savedState = JSON.parse(saved);
            
            // Expirar después de 24 horas
            if (Date.now() - savedState.timestamp > 24 * 60 * 60 * 1000) {
                clearPlayerState();
                return;
            }

            // Restaurar canción
            if (savedState.songId && savedState.songType) {
                playSong(savedState.songId, savedState.songType).then(() => {
                    const audio = document.getElementById('global-audio');
                    if (audio && savedState.currentTime) {
                        audio.currentTime = savedState.currentTime;
                        if (!savedState.isPlaying) {
                            audio.pause();
                        }
                    }
                });
            }
        } catch (e) {
            console.warn('Error restaurando estado:', e);
            clearPlayerState();
        }
    }

    /**
     * Limpia el estado guardado
     */
    function clearPlayerState() {
        try {
            localStorage.removeItem('audioPlayerState');
        } catch (e) {
            console.warn('Error limpiando estado:', e);
        }
    }

    // Exponer funciones globalmente
    window.initGlobalPlayer = initGlobalPlayer;
    window.reproducirCancion = playSong;

    // Auto-inicializar si el DOM ya está listo
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initGlobalPlayer);
    } else {
        initGlobalPlayer();
    }

})();
