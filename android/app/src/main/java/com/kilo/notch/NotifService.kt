package com.kilo.notch

import android.app.KeyguardManager
import android.app.Notification
import android.app.NotificationManager
import android.content.ComponentName
import android.content.Context
import android.graphics.Bitmap
import android.service.notification.NotificationListenerService
import android.service.notification.StatusBarNotification
import androidx.core.graphics.drawable.toBitmap

/** Every notification goes past here: new ones pop up in the island, ongoing timers and calls live in it. */
class NotifService : NotificationListenerService() {
    private val recent = LinkedHashMap<String, Long>() // keys already shown, so updates don't pop again

    override fun onListenerConnected() {
        Prefs.init(this)
        Music.attach(this, ComponentName(this, NotifService::class.java))
        try { activeNotifications?.forEach(::live) } catch (e: SecurityException) { }
    }

    override fun onListenerDisconnected() {
        Music.detach()
        Hub.live.value = emptyList()
    }

    override fun onNotificationPosted(sbn: StatusBarNotification, map: RankingMap?) {
        if (live(sbn)) return
        peek(sbn, map)
    }

    override fun onNotificationRemoved(sbn: StatusBarNotification) {
        Hub.live.value = Hub.live.value.filterNot { it.key == sbn.key }
    }

    /** Ongoing with a running clock (timers, stopwatches, calls, recordings): it lives in the island. */
    private fun live(sbn: StatusBarNotification): Boolean {
        val n = sbn.notification
        val e = n.extras
        val isLive = sbn.packageName != packageName && n.flags and Notification.FLAG_ONGOING_EVENT != 0 &&
            e.getBoolean(Notification.EXTRA_SHOW_CHRONOMETER) && n.`when` > 0
        val rest = Hub.live.value.filterNot { it.key == sbn.key }
        Hub.live.value = if (!isLive) rest else rest + Live(
            key = sbn.key,
            title = e.getCharSequence(Notification.EXTRA_TITLE)?.toString() ?: label(sbn.packageName),
            icon = icon(sbn.packageName),
            base = n.`when`,
            countDown = e.getBoolean(Notification.EXTRA_CHRONOMETER_COUNT_DOWN),
            tap = n.contentIntent,
        )
        return isLive
    }

    private fun peek(sbn: StatusBarNotification, map: RankingMap?) {
        val pkg = sbn.packageName
        val n = sbn.notification
        if (!Prefs.notifications || pkg == packageName || pkg in Prefs.muted) return
        if (n.flags and (Notification.FLAG_GROUP_SUMMARY or Notification.FLAG_ONGOING_EVENT or Notification.FLAG_FOREGROUND_SERVICE) != 0) return
        if (n.extras.containsKey(Notification.EXTRA_MEDIA_SESSION)) return // music has its own view
        val r = Ranking()
        if (map != null && map.getRanking(sbn.key, r)) {
            if (r.importance < NotificationManager.IMPORTANCE_DEFAULT) return // silent ones don't pop up on the phone either
            if (!r.matchesInterruptionFilter()) return                      // Do Not Disturb
        }
        if (n.flags and Notification.FLAG_ONLY_ALERT_ONCE != 0 && sbn.key in recent) return // an update (progress, edits)
        val e = n.extras
        val title = e.getCharSequence(Notification.EXTRA_TITLE)?.toString()?.trim().orEmpty()
        val text = (e.getCharSequence(Notification.EXTRA_TEXT) ?: e.getCharSequence(Notification.EXTRA_BIG_TEXT))?.toString()?.trim().orEmpty()
        if (title.isEmpty() && text.isEmpty()) return

        recent[sbn.key] = System.currentTimeMillis()
        if (recent.size > 60) recent.remove(recent.keys.first())
        if (pkg !in Prefs.seenApps) Prefs.seenApps = Prefs.seenApps + pkg

        val app = label(pkg)
        val hidden = Prefs.privateOnLock && getSystemService(KeyguardManager::class.java).isKeyguardLocked
        Hub.peek(
            Peek(
                kind = Peek.Kind.Notification,
                title = if (hidden) app else title.ifEmpty { app },
                text = if (hidden) "New notification" else text,
                icon = icon(pkg),
                avatar = if (hidden) null else avatar(n),
                tap = n.contentIntent,
                ms = if (text.length > 60) 5500 else 4500,
            )
        )
    }

    private fun avatar(n: Notification): Bitmap? = try {
        n.getLargeIcon()?.loadDrawable(this)?.toBitmap(112, 112)
    } catch (e: RuntimeException) { null }

    private fun label(pkg: String) = label(this, pkg)
    private fun icon(pkg: String) = icon(this, pkg)

    companion object {
        private val icons = HashMap<String, Bitmap?>()

        fun label(c: Context, pkg: String): String = try {
            c.packageManager.getApplicationLabel(c.packageManager.getApplicationInfo(pkg, 0)).toString()
        } catch (e: Exception) { pkg }

        fun icon(c: Context, pkg: String): Bitmap? = icons.getOrPut(pkg) {
            try { c.packageManager.getApplicationIcon(pkg).toBitmap(72, 72) } catch (e: Exception) { null }
        }
    }
}
