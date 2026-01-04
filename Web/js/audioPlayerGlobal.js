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
        currentIndex: -1,
        shuffleMode: false,
        repeatMode: 'off', // 'off', 'playlist', 'song'
        shuffledPlaylist: [],
        shuffledIndex: -1,
        likedSongs: new Set(),
        queueVisible: false,
        randomQueue: [] // Cola de canciones aleatorias
    };

    /**
     * Inicializa el reproductor global al cargar la página
     */
    function initGlobalPlayer() {
        // Verificar si el reproductor ya existe
        const existingPlayer = document.getElementById('global-audio-player');
        if (existingPlayer) {
            // Si ya existe, no hacer nada (ya está funcionando)
            // Solo restaurar estado si no hay audio cargado
            const audio = document.getElementById('global-audio');
            if (audio && !audio.src) {
                restorePlayerState();
            }
            return;
        }

        // Crear HTML del reproductor (versión simplificada)
        const playerHTML = `
            <div id="global-audio-player" class="audio-player-global" style="display: none;">
                <audio id="global-audio" preload="metadata"></audio>
                
                <div class="player-main">
                    <div class="player-left">
                        <div class="player-cover-container">
                            <img id="player-cover" class="player-cover" alt="Portada" style="display: none;">
                            <div id="player-cover-placeholder" class="player-cover-placeholder"><i data-lucide="music"></i></div>
                        </div>
                        
                        <div class="player-info">
                            <div class="player-song" id="player-song">Sin canción</div>
                            <div class="player-artist" id="player-artist"></div>
                        </div>
                    </div>
                    
                    <div class="player-center-controls">
                        <div class="player-controls">
                            <button id="player-shuffle" class="player-btn" title="Reproducción aleatoria"><i data-lucide="shuffle"></i></button>
                            <button id="player-prev" class="player-btn" title="Anterior"><i data-lucide="skip-back"></i></button>
                            <button id="player-play-pause" class="player-btn-main" title="Reproducir"><i data-lucide="play"></i></button>
                            <button id="player-next" class="player-btn" title="Siguiente"><i data-lucide="skip-forward"></i></button>
                            <button id="player-repeat" class="player-btn" title="Repetir"><i data-lucide="repeat"></i></button>
                        </div>
                        
                        <div class="player-progress">
                            <span id="player-current-time">0:00</span>
                            <input type="range" id="player-progress-bar" value="0" min="0" max="100" step="0.1">
                            <span id="player-duration">0:00</span>
                        </div>
                    </div>
                    
                    <div class="player-right">
                        <div class="player-volume">
                            <button id="player-volume-icon" class="player-btn" title="Volumen"><i data-lucide="volume-2"></i></button>
                            <input type="range" id="player-volume-bar" value="100" min="0" max="100" step="1">
                        </div>
                        
                        <button id="player-like" class="player-btn" title="Me gusta"><i data-lucide="heart"></i></button>
                        <button id="player-queue" class="player-btn" title="Cola de reproducción"><i data-lucide="list-music"></i></button>
                        <button id="player-minimize" class="player-minimize" title="Minimizar"><i data-lucide="chevron-down"></i></button>
                    </div>
                </div>
            </div>
            
            <!-- Cola de reproducción -->
            <div id="player-queue-panel" class="player-queue-panel" style="display: none;">
                <div class="queue-header">
                    <h3>Cola de reproducción</h3>
                    <button id="queue-close" class="player-btn" title="Cerrar"><i data-lucide="chevron-down"></i></button>
                </div>
                <div class="queue-content">
                    <div class="queue-now-playing">
                        <div class="queue-section-title">Reproduciendo ahora</div>
                        <div id="queue-current-song"></div>
                    </div>
                    <div class="queue-next">
                        <div class="queue-section-title">Siguiente</div>
                        <div id="queue-next-songs"></div>
                    </div>
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
        const minimizeBtn = document.getElementById('player-minimize');
        const progressBar = document.getElementById('player-progress-bar');
        const currentTimeSpan = document.getElementById('player-current-time');
        const durationSpan = document.getElementById('player-duration');
        const coverImg = document.getElementById('player-cover');
        const coverPlaceholder = document.getElementById('player-cover-placeholder');
        const volumeBar = document.getElementById('player-volume-bar');
        const volumeIcon = document.getElementById('player-volume-icon');
        const shuffleBtn = document.getElementById('player-shuffle');
        const repeatBtn = document.getElementById('player-repeat');
        const likeBtn = document.getElementById('player-like');
        const queueBtn = document.getElementById('player-queue');
        const queuePanel = document.getElementById('player-queue-panel');
        const queueClose = document.getElementById('queue-close');

        // Load liked songs from localStorage
        loadLikedSongs();

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
        
        shuffleBtn.addEventListener('click', () => toggleShuffle());
        repeatBtn.addEventListener('click', () => toggleRepeat());
        likeBtn.addEventListener('click', () => toggleLike());
        queueBtn.addEventListener('click', () => toggleQueue());
        queueClose.addEventListener('click', () => toggleQueue());
        
        minimizeBtn.addEventListener('click', () => {
            player.classList.toggle('minimized');
            
            // Si se minimiza, cerrar la cola si está abierta
            if (player.classList.contains('minimized') && state.queueVisible) {
                toggleQueue();
            }

            if (player.classList.contains('minimized')) {
                minimizeBtn.innerHTML = '<i data-lucide="chevron-up"></i>';
                minimizeBtn.title = 'Expandir';
            } else {
                minimizeBtn.innerHTML = '<i data-lucide="chevron-down"></i>';
                minimizeBtn.title = 'Minimizar';
            }
            if (window.lucide) window.lucide.createIcons();
        });

        // Eventos del audio
        audio.addEventListener('play', () => {
            playPauseBtn.innerHTML = '<i data-lucide="pause"></i>';
            state.isPlaying = true;
            savePlayerState();
            if (window.lucide) window.lucide.createIcons();
        });

        audio.addEventListener('pause', () => {
            playPauseBtn.innerHTML = '<i data-lucide="play"></i>';
            state.isPlaying = false;
            savePlayerState();
            if (window.lucide) window.lucide.createIcons();
        });

        audio.addEventListener('timeupdate', () => {
            if (audio.duration) {
                const progress = (audio.currentTime / audio.duration) * 100;
                progressBar.value = progress;
                currentTimeSpan.textContent = formatTime(audio.currentTime);
                // Actualizar estilo visual de la barra de progreso
                progressBar.style.background = `linear-gradient(to right, white 0%, white ${progress}%, rgba(255,255,255,0.2) ${progress}%, rgba(255,255,255,0.2) 100%)`;
            }
        });

        audio.addEventListener('loadedmetadata', () => {
            durationSpan.textContent = formatTime(audio.duration);
            progressBar.max = 100;
        });

        audio.addEventListener('ended', () => {
            if (state.repeatMode === 'song') {
                audio.currentTime = 0;
                audio.play().catch(err => console.error('Error al reproducir:', err));
            } else {
                playNext();
            }
        });

        audio.addEventListener('error', (e) => {
            console.error('Error de audio:', e);
            playPauseBtn.innerHTML = '<i data-lucide="play"></i>';
            if (window.lucide) window.lucide.createIcons();
        });

        // Barra de progreso
        progressBar.addEventListener('input', () => {
            if (audio.duration) {
                const time = (progressBar.value / 100) * audio.duration;
                audio.currentTime = time;
            }
        });

        // Control de volumen
        volumeBar.addEventListener('input', () => {
            const volume = volumeBar.value / 100;
            audio.volume = volume;
            updateVolumeIcon(volume);
            // Actualizar estilo visual del slider
            updateVolumeBarStyle();
            // Guardar volumen
            saveVolume(volume);
        });

        volumeIcon.addEventListener('click', () => {
            if (audio.volume > 0) {
                audio.dataset.previousVolume = audio.volume;
                audio.volume = 0;
                volumeBar.value = 0;
                updateVolumeIcon(0);
                saveVolume(0);
            } else {
                const previousVolume = parseFloat(audio.dataset.previousVolume) || 1;
                audio.volume = previousVolume;
                volumeBar.value = previousVolume * 100;
                updateVolumeIcon(previousVolume);
                saveVolume(previousVolume);
            }
            updateVolumeBarStyle();
        });

        // Actualizar estilo de barras al inicio
        updateVolumeBarStyle();
        
        // Cargar preferencias del reproductor
        loadPlayerPreferences();

        // Función para actualizar ícono de volumen
        function updateVolumeIcon(volume) {
            if (volume === 0) {
                volumeIcon.innerHTML = '<i data-lucide="volume-x"></i>';
            } else if (volume < 0.5) {
                volumeIcon.innerHTML = '<i data-lucide="volume-1"></i>';
            } else {
                volumeIcon.innerHTML = '<i data-lucide="volume-2"></i>';
            }
            if (window.lucide) window.lucide.createIcons();
        }

        // Función para actualizar estilo visual del slider de volumen
        function updateVolumeBarStyle() {
            const value = volumeBar.value;
            volumeBar.style.background = `linear-gradient(to right, white 0%, white ${value}%, rgba(255,255,255,0.2) ${value}%, rgba(255,255,255,0.2) 100%)`;
        }

        // Función para guardar volumen
        function saveVolume(volume) {
            try {
                localStorage.setItem('audioPlayerVolume', volume.toString());
            } catch (e) {
                console.warn('Error guardando volumen:', e);
            }
        }

        // Restaurar volumen guardado
        restoreVolume();

        
        if (window.lucide) window.lucide.createIcons();
        // Restaurar estado si existe
        restorePlayerState();
    }

    /**
     * Reproduce una canción
     */
    async function playSong(idCancion, tipo, contextPlaylist = null) {
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
            
            // Actualizar estado de favorito desde la base de datos
            const songKey = `${idCancion}_${tipo}`;
            if (cancion.esFavorito) {
                state.likedSongs.add(songKey);
            } else {
                state.likedSongs.delete(songKey);
            }
            
            // Verificar que tenga archivo de audio
            if (!cancion.tieneArchivoAudio) {
                alert('Esta canción no tiene archivo de audio');
                return;
            }

            // Cargar playlist
            if (contextPlaylist && Array.isArray(contextPlaylist) && contextPlaylist.length > 0) {
                state.playlist = contextPlaylist;
            } else {
                // Cargar playlist (todas las canciones del medio)
                await loadPlaylist(cancion.numMedio, tipo);
            }

            // Encontrar índice de la canción actual
            state.currentIndex = state.playlist.findIndex(c => c.id === idCancion && c.tipo === tipo);
            if (state.currentIndex === -1) {
                // Si la canción no está en la playlist (caso raro si viene de contexto), 
                // la agregamos o forzamos carga de medio
                if (contextPlaylist) {
                     // Fallback: si se pasó contexto pero la canción no está, cargar medio
                     await loadPlaylist(cancion.numMedio, tipo);
                     state.currentIndex = state.playlist.findIndex(c => c.id === idCancion && c.tipo === tipo);
                }
                if (state.currentIndex === -1) state.currentIndex = 0;
            }

            state.currentSong = cancion;

            // Actualizar UI
            updatePlayerUI(cancion);
            
            // Actualizar botón de like
            updateLikeButton();
            
            // Actualizar cola si está visible
            if (state.queueVisible) {
                updateQueue();
            }
            
            // Si shuffle está activo, actualizar índice en playlist mezclada
            if (state.shuffleMode) {
                createShuffledPlaylist();
            }

            // Cargar audio
            audio.src = `/api/canciones/${idCancion}/audio?tipo=${tipo}`;
            audio.load();
            
            // Mostrar reproductor
            player.style.display = 'block';

            // Reproducir
            setTimeout(() => {
                audio.play().catch(err => {
                    // Silenciar error de autoplay (esperado en algunos navegadores)
                    if (err.name !== 'NotAllowedError') {
                        console.error('Error al reproducir:', err);
                    }
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
        
        const audio = document.getElementById('global-audio');
        
        // Si han pasado más de 3 segundos, reiniciar la canción actual
        if (audio && audio.currentTime > 3) {
            audio.currentTime = 0;
            return;
        }
        
        // Ir a la canción anterior
        if (state.shuffleMode) {
            // En modo aleatorio infinito, "anterior" suele ser solo reiniciar o ir a una anterior en historial
            // Por simplicidad, aquí reiniciamos o vamos a una aleatoria nueva si está al principio
            // O podríamos implementar un historial. Para esta versión simple, vamos a una aleatoria.
            playNext(); 
        } else {
            state.currentIndex--;
            if (state.currentIndex < 0) {
                state.currentIndex = state.playlist.length - 1;
            }
            const song = state.playlist[state.currentIndex];
            if (song) playSong(song.id, song.tipo);
        }
    }

    /**
     * Reproduce la siguiente canción
     */
    function playNext() {
        if (state.playlist.length === 0) return;
        
        if (state.shuffleMode) {
            // Modo aleatorio infinito
            if (state.randomQueue.length === 0) {
                generateRandomQueue();
            }
            
            // Tomar la siguiente de la cola aleatoria
            const nextSong = state.randomQueue.shift();
            
            // Añadir una nueva al final para mantener la cola llena
            addRandomSongToQueue();
            
            if (nextSong) {
                // Actualizar currentIndex para mantener consistencia si cambiamos a modo normal
                state.currentIndex = state.playlist.findIndex(s => s.id === nextSong.id && s.tipo === nextSong.tipo);
                playSong(nextSong.id, nextSong.tipo);
            }
        } else {
            state.currentIndex = (state.currentIndex + 1) % state.playlist.length;
            const song = state.playlist[state.currentIndex];
            if (song) playSong(song.id, song.tipo);
        }
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
     * Restaura el volumen guardado
     */
    function restoreVolume() {
        try {
            const savedVolume = localStorage.getItem('audioPlayerVolume');
            if (savedVolume !== null) {
                const audio = document.getElementById('global-audio');
                const volumeBar = document.getElementById('player-volume-bar');
                const volumeIcon = document.getElementById('player-volume-icon');
                
                if (audio && volumeBar) {
                    const volume = parseFloat(savedVolume);
                    audio.volume = volume;
                    volumeBar.value = volume * 100;
                    
                    // Actualizar ícono
                    if (volume === 0) {
                        volumeIcon.innerHTML = '<i data-lucide="volume-x"></i>';
                    } else if (volume < 0.5) {
                        volumeIcon.innerHTML = '<i data-lucide="volume-1"></i>';
                    } else {
                        volumeIcon.innerHTML = '<i data-lucide="volume-2"></i>';
                    }
                    if (window.lucide) window.lucide.createIcons();
                    
                    // Actualizar estilo de la barra
                    const value = volumeBar.value;
                    volumeBar.style.background = `linear-gradient(to right, white 0%, white ${value}%, rgba(255,255,255,0.2) ${value}%, rgba(255,255,255,0.2) 100%)`;
                }
            }
        } catch (e) {
            console.warn('Error restaurando volumen:', e);
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

    /**
     * Toggle shuffle mode
     */
    function toggleShuffle() {
        state.shuffleMode = !state.shuffleMode;
        
        const shuffleBtn = document.getElementById('player-shuffle');
        if (state.shuffleMode) {
            shuffleBtn.classList.add('active');
            shuffleBtn.style.color = '#1db954'; // Spotify green
            
            // Generar cola aleatoria inicial
            generateRandomQueue();
        } else {
            shuffleBtn.classList.remove('active');
            shuffleBtn.style.color = '';
            state.randomQueue = [];
        }
        
        updateQueue();
        savePlayerPreferences();
    }

    /**
     * Genera una cola de canciones aleatorias
     */
    function generateRandomQueue() {
        state.randomQueue = [];
        if (state.playlist.length === 0) return;

        // Generar 10 canciones aleatorias
        for (let i = 0; i < 10; i++) {
            addRandomSongToQueue();
        }
    }

    /**
     * Añade una canción aleatoria a la cola
     */
    function addRandomSongToQueue() {
        if (state.playlist.length === 0) return;
        
        let nextIndex;
        // Intentar no repetir la última canción añadida (o la actual si la cola está vacía)
        const lastSong = state.randomQueue.length > 0 ? state.randomQueue[state.randomQueue.length - 1] : state.currentSong;
        
        if (state.playlist.length > 1) {
            let attempts = 0;
            do {
                nextIndex = Math.floor(Math.random() * state.playlist.length);
                attempts++;
            } while (lastSong && state.playlist[nextIndex].id === lastSong.id && attempts < 5);
        } else {
            nextIndex = 0;
        }
        
        state.randomQueue.push(state.playlist[nextIndex]);
    }

    /**
     * Create shuffled playlist (DEPRECATED but kept for compatibility if needed)
     */
    function createShuffledPlaylist() {
        // Ya no se usa en el nuevo modo aleatorio "infinito"
        // Pero si se necesitara, aquí iría la lógica
    }

    /**
     * Toggle repeat mode
     */
    function toggleRepeat() {
        const repeatBtn = document.getElementById('player-repeat');
        
        if (state.repeatMode === 'off') {
            state.repeatMode = 'playlist';
            repeatBtn.innerHTML = '<i data-lucide="repeat"></i>';
            repeatBtn.classList.add('active');
            repeatBtn.style.color = '#1db954';
            repeatBtn.title = 'Repetir lista';
        } else if (state.repeatMode === 'playlist') {
            state.repeatMode = 'song';
            repeatBtn.innerHTML = '<i data-lucide="repeat-1"></i>';
            repeatBtn.classList.add('active');
            repeatBtn.style.color = '#1db954';
            repeatBtn.title = 'Repetir canción';
        } else {
            state.repeatMode = 'off';
            repeatBtn.innerHTML = '<i data-lucide="repeat"></i>';
            repeatBtn.classList.remove('active');
            repeatBtn.style.color = '';
            repeatBtn.title = 'Repetir';
        }
        
        savePlayerPreferences();
        if (window.lucide) window.lucide.createIcons();
    }

    /**
     * Toggle like for current song
     */
    async function toggleLike() {
        if (!state.currentSong) return;
        
        const songKey = `${state.currentSong.id}_${state.currentSong.tipo}`;
        const likeBtn = document.getElementById('player-like');
        const esFavorito = !state.likedSongs.has(songKey);
        
        try {
            // Actualizar en el servidor
            const response = await fetch(`/api/canciones/${state.currentSong.id}/favorito?tipo=${state.currentSong.tipo}`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ esFavorito })
            });
            
            if (response.ok) {
                if (esFavorito) {
                    state.likedSongs.add(songKey);
                    likeBtn.innerHTML = '<i data-lucide="heart" fill="currentColor"></i>';
                    likeBtn.classList.add('active');
                    likeBtn.style.color = '#1db954';
                } else {
                    state.likedSongs.delete(songKey);
                    likeBtn.innerHTML = '<i data-lucide="heart"></i>';
                    likeBtn.classList.remove('active');
                    likeBtn.style.color = '';
                }
                
                if (window.lucide) window.lucide.createIcons();
                saveLikedSongs();
            }
        } catch (error) {
            console.error('Error al guardar favorito:', error);
        }
    }

    /**
     * Update like button state
     */
    function updateLikeButton() {
        if (!state.currentSong) return;
        
        const songKey = `${state.currentSong.id}_${state.currentSong.tipo}`;
        const likeBtn = document.getElementById('player-like');
        
        if (state.likedSongs.has(songKey)) {
            likeBtn.innerHTML = '<i data-lucide="heart" fill="currentColor"></i>';
            likeBtn.classList.add('active');
            likeBtn.style.color = '#1db954';
        } else {
            likeBtn.innerHTML = '<i data-lucide="heart"></i>';
            likeBtn.classList.remove('active');
            likeBtn.style.color = '';
        }
        if (window.lucide) window.lucide.createIcons();
    }

    /**
     * Toggle queue panel
     */
    function toggleQueue() {
        const queuePanel = document.getElementById('player-queue-panel');
        const queueBtn = document.getElementById('player-queue');
        state.queueVisible = !state.queueVisible;
        
        if (state.queueVisible) {
            queuePanel.style.display = 'flex';
            // Forzar reflow para que la animación funcione
            queuePanel.offsetHeight;
            queuePanel.classList.add('visible');
            queueBtn.classList.add('active');
            queueBtn.style.color = '#1db954';
            updateQueue();
        } else {
            queuePanel.classList.remove('visible');
            queueBtn.classList.remove('active');
            queueBtn.style.color = '';
            // Esperar a que termine la animación antes de ocultar
            setTimeout(() => {
                if (!state.queueVisible) {
                    queuePanel.style.display = 'none';
                }
            }, 400);
        }
    }

    /**
     * Update queue display
     */
    function updateQueue() {
        const currentSongDiv = document.getElementById('queue-current-song');
        const nextSongsDiv = document.getElementById('queue-next-songs');
        
        if (!currentSongDiv || !nextSongsDiv) return;
        
        const getCoverUrl = (song) => {
            if (song.idAlbum) return `/api/albumes/${song.idAlbum}/portada`;
            return null; // Placeholder handled by onerror
        };

        const placeholderSvg = encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="#333" stroke="#555" stroke-width="2"><rect x="3" y="3" width="18" height="18" rx="2" ry="2"/><circle cx="12" cy="12" r="3"/></svg>');
        const placeholderUrl = `data:image/svg+xml;charset=utf-8,${placeholderSvg}`;

        // Current song
        if (state.currentSong) {
            const coverUrl = state.currentSong.tienePortada ? 
                `/api/canciones/${state.currentSong.id}/portada?tipo=${state.currentSong.tipo}` : 
                (state.currentSong.idAlbum ? `/api/albumes/${state.currentSong.idAlbum}/portada` : placeholderUrl);

            currentSongDiv.innerHTML = `
                <div class="queue-song current">
                    <img src="${coverUrl}" class="queue-song-cover" onerror="this.src='${placeholderUrl}'">
                    <div class="queue-song-info">
                        <span class="queue-song-title">${state.currentSong.tema}</span>
                        <span class="queue-song-artist">${state.currentSong.interprete || ''}</span>
                    </div>
                </div>
            `;
        } else {
            currentSongDiv.innerHTML = '<div class="queue-empty">No hay canción reproduciéndose</div>';
        }
        
        // Next songs
        let nextSongsHTML = '';
        
        if (state.playlist && state.playlist.length > 0) {
            const maxSongs = 10;
            
            if (state.shuffleMode) {
                // Mostrar la cola aleatoria generada
                // Rellenar si falta
                while(state.randomQueue.length < maxSongs) {
                    addRandomSongToQueue();
                }

                for (let i = 0; i < maxSongs; i++) {
                    const song = state.randomQueue[i];
                    if (!song) continue;
                    
                    const coverUrl = getCoverUrl(song) || placeholderUrl;

                    nextSongsHTML += `
                        <div class="queue-song" onclick="reproducirCancion(${song.id}, '${song.tipo}')">
                            <img src="${coverUrl}" class="queue-song-cover" onerror="this.src='${placeholderUrl}'">
                            <div class="queue-song-info">
                                <span class="queue-song-title">${song.tema}</span>
                                <span class="queue-song-artist">${song.interprete || ''}</span>
                            </div>
                        </div>
                    `;
                }
            } else {
                // Modo secuencial (loop)
                const currentIdx = state.currentIndex;
                let count = 0;
                
                for (let i = 1; count < maxSongs; i++) {
                    let nextIdx = (currentIdx + i) % state.playlist.length;
                    // Manejo seguro de índices negativos
                    if (nextIdx < 0) nextIdx += state.playlist.length;
                    
                    const song = state.playlist[nextIdx];
                    
                    if (!song) break;
                    
                    const coverUrl = getCoverUrl(song) || placeholderUrl;

                    nextSongsHTML += `
                        <div class="queue-song" onclick="reproducirCancion(${song.id}, '${song.tipo}')">
                            <img src="${coverUrl}" class="queue-song-cover" onerror="this.src='${placeholderUrl}'">
                            <div class="queue-song-info">
                                <span class="queue-song-title">${song.tema}</span>
                                <span class="queue-song-artist">${song.interprete || ''}</span>
                            </div>
                        </div>
                    `;
                    
                    count++;
                }
            }
        }
        
        if (nextSongsHTML === '') {
            nextSongsHTML = '<div class="queue-empty">No hay más canciones en la cola</div>';
        }
        
        nextSongsDiv.innerHTML = nextSongsHTML;
    }

    /**
     * Save liked songs to localStorage
     */
    function saveLikedSongs() {
        try {
            localStorage.setItem('likedSongs', JSON.stringify([...state.likedSongs]));
        } catch (e) {
            console.warn('Error guardando canciones favoritas:', e);
        }
    }

    /**
     * Load liked songs from localStorage
     */
    function loadLikedSongs() {
        try {
            const saved = localStorage.getItem('likedSongs');
            if (saved) {
                state.likedSongs = new Set(JSON.parse(saved));
            }
        } catch (e) {
            console.warn('Error cargando canciones favoritas:', e);
            state.likedSongs = new Set();
        }
    }

    /**
     * Save player preferences (shuffle, repeat)
     */
    function savePlayerPreferences() {
        try {
            localStorage.setItem('playerPreferences', JSON.stringify({
                shuffleMode: state.shuffleMode,
                repeatMode: state.repeatMode
            }));
        } catch (e) {
            console.warn('Error guardando preferencias:', e);
        }
    }

    /**
     * Load player preferences
     */
    function loadPlayerPreferences() {
        try {
            const saved = localStorage.getItem('playerPreferences');
            if (saved) {
                const prefs = JSON.parse(saved);
                state.shuffleMode = prefs.shuffleMode || false;
                state.repeatMode = prefs.repeatMode || 'off';
                
                // Update UI
                const shuffleBtn = document.getElementById('player-shuffle');
                const repeatBtn = document.getElementById('player-repeat');
                
                if (state.shuffleMode && shuffleBtn) {
                    shuffleBtn.classList.add('active');
                    shuffleBtn.style.color = '#1db954';
                }
                
                if (repeatBtn) {
                    if (state.repeatMode === 'playlist') {
                        repeatBtn.innerHTML = '<i data-lucide="repeat"></i>';
                        repeatBtn.classList.add('active');
                        repeatBtn.style.color = '#1db954';
                        repeatBtn.title = 'Repetir lista';
                    } else if (state.repeatMode === 'song') {
                        repeatBtn.innerHTML = '<i data-lucide="repeat-1"></i>';
                        repeatBtn.classList.add('active');
                        repeatBtn.style.color = '#1db954';
                        repeatBtn.title = 'Repetir canción';
                    }
                }
                if (window.lucide) window.lucide.createIcons();
            }
        } catch (e) {
            console.warn('Error cargando preferencias:', e);
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
