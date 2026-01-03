// Reproductor de audio global para todas las páginas
// Incluye controles básicos y carga de archivo local

export function renderAudioPlayerGlobal() {
    return `
    <div class="audio-player-spotify audio-player-global" style="max-width:340px;margin:0 auto 1.5rem auto;">
        <audio id="audio-player-global" preload="none"></audio>
        <div id="audio-controls-global">
            <button id="play-pause-btn-global">▶️</button>
            <input type="range" id="progress-bar-global" value="0" min="0" max="100" step="0.1">
            <span id="current-time-global">0:00</span> / <span id="duration-global">0:00</span>
        </div>
        <div style="width:100%;display:flex;align-items:center;justify-content:flex-start;">
            <input type="file" id="audio-file-input-global" accept="audio/*" style="display:none;">
            <button id="load-audio-btn-global">Cargar archivo MP3</button>
            <span id="audio-file-name-global"></span>
        </div>
    </div>
    <script>
    (function(){
        const audio = document.getElementById('audio-player-global');
        const playPauseBtn = document.getElementById('play-pause-btn-global');
        const progressBar = document.getElementById('progress-bar-global');
        const currentTimeSpan = document.getElementById('current-time-global');
        const durationSpan = document.getElementById('duration-global');
        const loadAudioBtn = document.getElementById('load-audio-btn-global');
        const audioFileInput = document.getElementById('audio-file-input-global');
        const audioFileName = document.getElementById('audio-file-name-global');
        let userLoadedFile = false;
        function formatTime(seconds) {
            const m = Math.floor(seconds / 60);
            const s = Math.floor(seconds % 60);
            return `${m}:${s.toString().padStart(2, '0')}`;
        }
        playPauseBtn.onclick = function() {
            if (audio.paused) {
                audio.play();
            } else {
                audio.pause();
            }
        };
        audio.addEventListener('play', () => { playPauseBtn.textContent = '⏸️'; });
        audio.addEventListener('pause', () => { playPauseBtn.textContent = '▶️'; });
        audio.addEventListener('loadedmetadata', () => {
            progressBar.max = audio.duration;
            durationSpan.textContent = formatTime(audio.duration);
        });
        audio.addEventListener('timeupdate', () => {
            progressBar.value = audio.currentTime;
            currentTimeSpan.textContent = formatTime(audio.currentTime);
        });
        progressBar.oninput = function() {
            audio.currentTime = progressBar.value;
        };
        loadAudioBtn.onclick = function() {
            audioFileInput.click();
        };
        audioFileInput.onchange = function(e) {
            const file = e.target.files[0];
            if (file) {
                const url = URL.createObjectURL(file);
                audio.src = url;
                audioFileName.textContent = file.name;
                userLoadedFile = true;
                audio.load();
                playPauseBtn.textContent = '▶️';
                currentTimeSpan.textContent = '0:00';
                durationSpan.textContent = '0:00';
            }
        };
    })();
    </script>
    `;
}
