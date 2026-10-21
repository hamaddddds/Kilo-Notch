package com.kilo.notch

import android.content.Context
import android.os.PowerManager
import androidx.core.app.NotificationCompat
import kotlinx.coroutines.flow.MutableStateFlow
import java.time.Instant
import java.time.LocalTime
import java.time.ZoneId

/** Water log and reminders. Each reminder is an exact alarm, re-armed after every glass, nudge, boot and settings change. */
object Water {
    private const val DAY = 86_400_000L

    data class Sip(val at: Long, val ml: Int)

    val total = MutableStateFlow(0) // today's ml

    fun sips(): List<Sip> = Prefs.sips.split(';').mapNotNull { e ->
        val parts = e.split(':')
        if (parts.size != 2) return@mapNotNull null
        Sip(parts[0].toLongOrNull() ?: return@mapNotNull null, parts[1].toIntOrNull() ?: return@mapNotNull null)
    }

    fun today(now: Long = System.currentTimeMillis()): List<Sip> {
        val d = date(now)
        return sips().filter { date(it.at) == d }
    }

    private fun date(t: Long) = Instant.ofEpochMilli(t).atZone(ZoneId.systemDefault()).toLocalDate()

    fun refresh() { total.value = today().sumOf { it.ml } }

    fun add(c: Context) {
        val now = System.currentTimeMillis()
        save(sips().filter { now - it.at < 7 * DAY } + Sip(now, Prefs.waterCup))
        Prefs.thirsty = false
        Notes.cancel(c, Notes.ID_WATER)
        if (Hub.peek.value?.kind == Peek.Kind.Water) Hub.peek.value = null
        Hub.react(Mood.Happy)
        schedule(c)
    }

    fun undo(c: Context) {
        val last = today().maxByOrNull { it.at } ?: return
        save(sips() - last)
        schedule(c)
    }

    private fun save(list: List<Sip>) {
        Prefs.sips = list.joinToString(";") { "${it.at}:${it.ml}" }
        refresh()
    }

    fun snooze(c: Context) {
        Prefs.lastNag = System.currentTimeMillis() - (Prefs.waterEvery - 15) * 60_000L // due again in 15 minutes
        Notes.cancel(c, Notes.ID_WATER)
        if (Hub.peek.value?.kind == Peek.Kind.Water) Hub.peek.value = null
        schedule(c)
    }

    /**
     * When the next nudge is due: one interval after the last glass or nudge, moved into the active
     * hours, and not before tomorrow once today's goal is met. Never in the past.
     */
    fun next(now: Long, lastSip: Long, lastNag: Long, everyMin: Int, from: Int, to: Int, goalMet: Boolean, zone: ZoneId = ZoneId.systemDefault()): Long {
        var t = maxOf(lastSip, lastNag) + everyMin * 60_000L
        if (goalMet) t = maxOf(t, Instant.ofEpochMilli(now).atZone(zone).toLocalDate().plusDays(1).atStartOfDay(zone).plusHours(from.toLong()).toInstant().toEpochMilli())
        t = maxOf(t, now + 5_000) // overdue (phone was off): nudge right away
        val z = Instant.ofEpochMilli(t).atZone(zone)
        val start = z.toLocalDate().atStartOfDay(zone).plusHours(from.toLong())
        val end = z.toLocalDate().atStartOfDay(zone).plusHours(to.toLong()) // "until 24:00" is midnight
        return when {
            z.isBefore(start) -> start
            !z.isBefore(end) -> start.plusDays(1)
            else -> z
        }.toInstant().toEpochMilli()
    }

    fun schedule(c: Context) {
        if (!Prefs.waterOn) {
            Alarms.cancel(c, Alarms.WATER)
            Prefs.waterNext = 0
            return
        }
        val now = System.currentTimeMillis()
        if (Prefs.lastNag == 0L) Prefs.lastNag = now // first run: the first nudge comes one interval from now
        val t = next(now, sips().maxOfOrNull { it.at } ?: 0L, Prefs.lastNag, Prefs.waterEvery, Prefs.waterFrom, Prefs.waterTo,
            today(now).sumOf { it.ml } >= Prefs.waterGoal)
        Prefs.waterNext = t
        Alarms.at(c, t, Alarms.WATER)
    }

    fun fire(c: Context) {
        val now = System.currentTimeMillis()
        val hour = LocalTime.now().hour
        val last = maxOf(sips().maxOfOrNull { it.at } ?: 0L, Prefs.lastNag)
        val due = Prefs.waterOn && today(now).sumOf { it.ml } < Prefs.waterGoal && hour >= Prefs.waterFrom && hour < Prefs.waterTo &&
            now - last >= Prefs.waterEvery * 60_000L - 60_000L
        if (due) remind(c)
        schedule(c)
    }

    /** The nudge: the island opens with a Drink button, and a notification carries the same buttons. */
    fun remind(c: Context) {
        Prefs.lastNag = System.currentTimeMillis()
        Prefs.thirsty = true
        refresh()
        val goal = Prefs.waterGoal
        val text = "%.2f / %.2f L today".format(total.value / 1000f, goal / 1000f)
        Hub.peek(Peek(Peek.Kind.Water, "Time for some water", text, progress = total.value.toFloat() / goal, ms = 9000))
        // quiet only when the island is on screen to show it; screen off or island hidden: ring and pop up
        val seen = Hub.alive.value && Hub.showing.value && c.getSystemService(PowerManager::class.java).isInteractive
        val n = NotificationCompat.Builder(c, if (seen) Notes.WATER_QUIET else Notes.WATER)
            .setSmallIcon(R.drawable.ic_stat)
            .setContentTitle("Time for some water")
            .setContentText(text)
            .setColor(Kolors.accent().first)
            .setCategory(NotificationCompat.CATEGORY_REMINDER)
            .setAutoCancel(true)
            .setContentIntent(Notes.openApp(c))
            .addAction(0, "Drink ${Prefs.waterCup} ml", Alarms.intent(c, Alarms.DRINK))
            .addAction(0, "In 15 min", Alarms.intent(c, Alarms.SNOOZE))
            .build()
        Notes.post(c, Notes.ID_WATER, n)
    }
}
