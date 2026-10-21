package com.kilo.notch

import android.content.ComponentName
import android.content.Context
import android.graphics.Bitmap
import android.media.MediaMetadata
import android.media.session.MediaController
import android.media.session.MediaSessionManager
import android.media.session.PlaybackState
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import kotlinx.coroutines.flow.MutableStateFlow

/** Whatever is playing, through the same media sessions Android shows in its media player. */
object Music {
    data class Now(
        val title: String, val artist: String, val art: Bitmap?, val colors: Pair<Int, Int>?,
        val playing: Boolean, val pos: Long, val at: Long, val speed: Float, val duration: Long,
    ) {
        /** Players report position only on changes, so it's extrapolated while playing. */
        fun position(now: Long = SystemClock.elapsedRealtime()): Long {
            val p = if (playing) pos + ((now - at) * speed).toLong() else pos
            return p.coerceIn(0, if (duration > 0) duration else Long.MAX_VALUE)
        }
    }

    val now = MutableStateFlow<Now?>(null)
    private val main = Handler(Looper.getMainLooper())
    private var msm: MediaSessionManager? = null
    private val controllers = mutableListOf<MediaController>()
    private var current: MediaController? = null
    private var lastArt: Bitmap? = null
    private var lastLook: Pair<Bitmap?, Pair<Int, Int>?> = null to null

    private val sessions = MediaSessionManager.OnActiveSessionsChangedListener { track(it.orEmpty()) }
    private val callback = object : MediaController.Callback() {
        override fun onPlaybackStateChanged(state: PlaybackState?) = update()
        override fun onMetadataChanged(metadata: MediaMetadata?) = update()
        override fun onSessionDestroyed() = update()
    }

    /** Needs notification access, which the listener service has once it's connected. */
    fun attach(c: Context, listener: ComponentName) {
        val m = c.getSystemService(MediaSessionManager::class.java)
        msm = m
        try {
            m.addOnActiveSessionsChangedListener(sessions, listener, main)
            track(m.getActiveSessions(listener))
        } catch (e: SecurityException) { /* access was just revoked */ }
    }

    fun detach() {
        msm?.removeOnActiveSessionsChangedListener(sessions)
        controllers.forEach { it.unregisterCallback(callback) }
        controllers.clear()
        current = null
        now.value = null
    }

    private fun track(list: List<MediaController>) {
        controllers.forEach { it.unregisterCallback(callback) }
        controllers.clear()
        list.forEach { it.registerCallback(callback, main); controllers += it }
        update()
    }

    // the one that's playing wins; otherwise the most recent session (Android lists them by priority)
    private fun update() {
        val c = controllers.firstOrNull { it.playbackState?.state == PlaybackState.STATE_PLAYING }
            ?: controllers.firstOrNull { it.metadata != null }
        current = c
        val md = c?.metadata
        val title = md?.getString(MediaMetadata.METADATA_KEY_TITLE) ?: md?.getString(MediaMetadata.METADATA_KEY_DISPLAY_TITLE)
        if (c == null || md == null || title.isNullOrBlank()) { now.value = null; return }
        val st = c.playbackState
        val art = md.getBitmap(MediaMetadata.METADATA_KEY_ALBUM_ART) ?: md.getBitmap(MediaMetadata.METADATA_KEY_ART)
            ?: md.getBitmap(MediaMetadata.METADATA_KEY_DISPLAY_ICON)
        if (art !== lastArt) { lastArt = art; lastLook = look(art) }
        now.value = Now(
            title = title,
            artist = md.getString(MediaMetadata.METADATA_KEY_ARTIST) ?: md.getString(MediaMetadata.METADATA_KEY_ALBUM_ARTIST).orEmpty(),
            art = lastLook.first,
            colors = lastLook.second,
            playing = st?.state == PlaybackState.STATE_PLAYING,
            pos = st?.position ?: 0,
            at = st?.lastPositionUpdateTime?.takeIf { it > 0 } ?: SystemClock.elapsedRealtime(),
            speed = st?.playbackSpeed?.takeIf { it > 0 } ?: 1f,
            duration = md.getLong(MediaMetadata.METADATA_KEY_DURATION),
        )
    }

    // a small copy of the cover (players can hand over huge bitmaps) and its two colors
    private fun look(art: Bitmap?): Pair<Bitmap?, Pair<Int, Int>?> {
        if (art == null || art.width <= 0 || art.height <= 0) return null to null
        return try {
            val k = 256f / maxOf(art.width, art.height)
            val small = if (k < 1) Bitmap.createScaledBitmap(art, (art.width * k).toInt().coerceAtLeast(1), (art.height * k).toInt().coerceAtLeast(1), true) else art
            val tiny = Bitmap.createScaledBitmap(small, 24, 24, true)
            val px = IntArray(24 * 24)
            tiny.getPixels(px, 0, 24, 0, 0, 24, 24)
            small to Kolors.palette(px)
        } catch (e: RuntimeException) { null to null } // recycled or odd bitmap
    }

    private val controls get() = current?.transportControls

    fun toggle() { if (now.value?.playing == true) controls?.pause() else controls?.play() }
    fun next() = controls?.skipToNext()
    fun prev() = controls?.skipToPrevious()
    fun seek(ms: Long) = controls?.seekTo(ms)
}
