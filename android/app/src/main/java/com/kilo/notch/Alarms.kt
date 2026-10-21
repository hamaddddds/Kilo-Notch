package com.kilo.notch

import android.Manifest
import android.app.AlarmManager
import android.app.Notification
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat

/** Exact alarms for reminders and the timer: they fire on time in Doze and with the app closed. */
object Alarms {
    const val WATER = "com.kilo.notch.WATER"
    const val FOCUS = "com.kilo.notch.FOCUS"
    const val DRINK = "com.kilo.notch.DRINK"
    const val SNOOZE = "com.kilo.notch.SNOOZE"
    const val FOCUS_TOGGLE = "com.kilo.notch.FOCUS_TOGGLE"
    const val FOCUS_RESET = "com.kilo.notch.FOCUS_RESET"

    fun intent(c: Context, action: String): PendingIntent = PendingIntent.getBroadcast(
        c, action.hashCode(), Intent(c, AlarmReceiver::class.java).setAction(action),
        PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
    )

    fun canExact(c: Context) = Build.VERSION.SDK_INT < 31 || c.getSystemService(AlarmManager::class.java).canScheduleExactAlarms()

    /** [clock] uses an alarm-clock alarm (the most reliable kind, for the timer's end). */
    fun at(c: Context, time: Long, action: String, clock: Boolean = false) {
        val am = c.getSystemService(AlarmManager::class.java)
        val pi = intent(c, action)
        try {
            when {
                !canExact(c) -> am.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, time, pi) // a few minutes late at worst
                clock -> am.setAlarmClock(AlarmManager.AlarmClockInfo(time, Notes.openApp(c)), pi)
                else -> am.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, time, pi)
            }
        } catch (e: SecurityException) {
            am.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, time, pi) // exact alarms revoked meanwhile
        }
    }

    fun cancel(c: Context, action: String) = c.getSystemService(AlarmManager::class.java).cancel(intent(c, action))
}

object Notes {
    const val WATER = "water"
    const val WATER_QUIET = "water_quiet"
    const val FOCUS = "focus"
    const val FOCUS_RUN = "focus_run"
    const val ID_WATER = 10
    const val ID_FOCUS_RUN = 20
    const val ID_FOCUS_DONE = 21

    fun openApp(c: Context): PendingIntent = PendingIntent.getActivity(
        c, 0, Intent(c, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
        PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
    )

    fun post(c: Context, id: Int, n: Notification) {
        val allowed = Build.VERSION.SDK_INT < 33 ||
            ContextCompat.checkSelfPermission(c, Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED
        if (allowed) NotificationManagerCompat.from(c).notify(id, n)
    }

    fun cancel(c: Context, id: Int) = NotificationManagerCompat.from(c).cancel(id)
}

class AlarmReceiver : BroadcastReceiver() {
    override fun onReceive(c: Context, i: Intent) {
        Prefs.init(c)
        when (i.action) {
            Alarms.WATER -> Water.fire(c)
            Alarms.DRINK -> Water.add(c)
            Alarms.SNOOZE -> Water.snooze(c)
            Alarms.FOCUS -> Focus.finished(c)
            Alarms.FOCUS_TOGGLE -> Focus.toggle(c)
            Alarms.FOCUS_RESET -> Focus.reset(c)
        }
    }
}

/** Alarms are wiped by a reboot, an update or a clock change: set them again. */
class BootReceiver : BroadcastReceiver() {
    override fun onReceive(c: Context, i: Intent) {
        Prefs.init(c)
        Water.schedule(c)
        Focus.restore(c)
    }
}
