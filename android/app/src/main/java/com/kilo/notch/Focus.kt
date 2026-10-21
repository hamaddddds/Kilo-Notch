package com.kilo.notch

import android.content.Context
import androidx.core.app.NotificationCompat
import kotlinx.coroutines.flow.MutableStateFlow

/** Pomodoro: focus, then a break. The end is an alarm-clock alarm, so it rings even with the app closed. */
object Focus {
    data class State(val running: Boolean, val isBreak: Boolean, val end: Long, val left: Long, val total: Long)

    val state = MutableStateFlow(read())

    fun total() = (if (Prefs.focusBreak) Prefs.breakMin else Prefs.focusMin) * 60_000L

    fun left(now: Long = System.currentTimeMillis()) = when {
        Prefs.focusRunning -> (Prefs.focusEnd - now).coerceAtLeast(0)
        Prefs.focusLeft >= 0 -> Prefs.focusLeft
        else -> total()
    }

    private fun read() = State(Prefs.focusRunning, Prefs.focusBreak, Prefs.focusEnd, left(), total())

    fun toggle(c: Context) {
        val now = System.currentTimeMillis()
        if (Prefs.focusRunning) {
            Prefs.focusLeft = left(now)
            Prefs.focusRunning = false
            Alarms.cancel(c, Alarms.FOCUS)
        } else {
            Prefs.focusEnd = now + left(now)
            Prefs.focusRunning = true
            Alarms.at(c, Prefs.focusEnd, Alarms.FOCUS, clock = true)
        }
        publish(c)
    }

    fun reset(c: Context) {
        Prefs.focusRunning = false
        Prefs.focusLeft = -1
        Alarms.cancel(c, Alarms.FOCUS)
        publish(c)
    }

    fun mode(c: Context, isBreak: Boolean) {
        Prefs.focusBreak = isBreak
        reset(c)
    }

    /** After a reboot or app start: re-arm a running timer, or finish one that ran out meanwhile. */
    fun restore(c: Context) {
        if (!Prefs.focusRunning) { publish(c); return }
        if (Prefs.focusEnd <= System.currentTimeMillis()) finished(c)
        else {
            Alarms.at(c, Prefs.focusEnd, Alarms.FOCUS, clock = true)
            publish(c)
        }
    }

    fun finished(c: Context) {
        if (!Prefs.focusRunning) return
        val wasBreak = Prefs.focusBreak
        Prefs.focusBreak = !wasBreak
        Prefs.focusRunning = false
        Prefs.focusLeft = -1
        publish(c)
        val title = if (wasBreak) "Break's over" else "Focus session done"
        val text = if (wasBreak) "Ready for the next round?" else "Take a ${Prefs.breakMin} min break"
        Hub.peek(Peek(Peek.Kind.Focus, title, text, progress = 1f, ms = 8000))
        Hub.react(Mood.Happy, 5000)
        val n = NotificationCompat.Builder(c, Notes.FOCUS)
            .setSmallIcon(R.drawable.ic_stat)
            .setContentTitle(title)
            .setContentText(text)
            .setColor(Kolors.accent().first)
            .setCategory(NotificationCompat.CATEGORY_ALARM)
            .setAutoCancel(true)
            .setContentIntent(Notes.openApp(c))
            .addAction(0, if (wasBreak) "Start focus" else "Start break", Alarms.intent(c, Alarms.FOCUS_TOGGLE))
            .build()
        Notes.post(c, Notes.ID_FOCUS_DONE, n)
    }

    /** Pushes the state to the island and keeps a quiet ongoing notification with a live countdown. */
    fun publish(c: Context) {
        val s = read()
        state.value = s
        if (!s.running && s.left == s.total) {
            Notes.cancel(c, Notes.ID_FOCUS_RUN)
            return
        }
        Notes.cancel(c, Notes.ID_FOCUS_DONE)
        val b = NotificationCompat.Builder(c, Notes.FOCUS_RUN)
            .setSmallIcon(R.drawable.ic_stat)
            .setContentTitle(if (s.isBreak) "Break" else "Focus")
            .setColor(Kolors.accent().first)
            .setOnlyAlertOnce(true)
            .setSilent(true)
            .setOngoing(s.running)
            .setContentIntent(Notes.openApp(c))
            .addAction(0, if (s.running) "Pause" else "Resume", Alarms.intent(c, Alarms.FOCUS_TOGGLE))
            .addAction(0, "Reset", Alarms.intent(c, Alarms.FOCUS_RESET))
        if (s.running) b.setUsesChronometer(true).setChronometerCountDown(true).setShowWhen(true).setWhen(s.end).setContentText("Running")
        else b.setShowWhen(false).setContentText("Paused · ${clock(s.left)} left")
        Notes.post(c, Notes.ID_FOCUS_RUN, b.build())
    }
}

/** mm:ss, rounded up so a timer never shows 00:00 while it's still running. */
fun clock(ms: Long): String {
    val s = (ms + 999) / 1000
    return "%02d:%02d".format(s / 60, s % 60)
}
