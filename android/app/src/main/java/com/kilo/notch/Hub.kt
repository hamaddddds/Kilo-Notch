package com.kilo.notch

import android.app.PendingIntent
import android.graphics.Bitmap
import kotlinx.coroutines.flow.MutableStateFlow

enum class Mood { Normal, Happy, Sad, Asleep, Wink }

/** A moment the island pops open for: a notification, a reminder, the charger, headphones. */
data class Peek(
    val kind: Kind,
    val title: String,
    val text: String,
    val icon: Bitmap? = null,    // app icon
    val avatar: Bitmap? = null,  // sender photo, contact, cover
    val tap: PendingIntent? = null,
    val progress: Float? = null,
    val ms: Long = 4500,
    val id: Long = System.nanoTime(),
) {
    enum class Kind { Notification, Water, Focus, Charging, Headphones }
}

/** Something ongoing with a running clock: a timer or stopwatch, a call, a screen recording. */
data class Live(
    val key: String,
    val title: String,
    val icon: Bitmap?,
    val base: Long,         // the notification's `when`
    val countDown: Boolean,
    val tap: PendingIntent?,
)

/** What the island shows, shared by the services and the settings screen (one process). */
object Hub {
    val alive = MutableStateFlow(false)           // the island service is running
    val showing = MutableStateFlow(false)         // ...and on screen (not hidden for landscape or full screen)
    val peek = MutableStateFlow<Peek?>(null)
    val live = MutableStateFlow<List<Live>>(emptyList())
    val expanded = MutableStateFlow(false)
    val reaction = MutableStateFlow(Mood.Normal to 0L) // a passing mood and when it ends
    val battery = MutableStateFlow(100 to false)       // percent, charging

    fun peek(p: Peek) { peek.value = p }

    fun react(m: Mood, ms: Long = 3500) { reaction.value = m to System.currentTimeMillis() + ms }
}
