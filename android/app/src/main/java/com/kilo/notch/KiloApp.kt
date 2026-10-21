package com.kilo.notch

import android.app.Application
import android.app.NotificationChannel
import android.app.NotificationManager

class KiloApp : Application() {
    override fun onCreate() {
        super.onCreate()
        Prefs.init(this)
        getSystemService(NotificationManager::class.java).createNotificationChannels(
            listOf(
                NotificationChannel(Notes.WATER, "Water reminders", NotificationManager.IMPORTANCE_HIGH)
                    .apply { description = "Pops up when it's time for a glass" },
                NotificationChannel(Notes.WATER_QUIET, "Water reminders (shown in the island)", NotificationManager.IMPORTANCE_LOW)
                    .apply { description = "Used while the island is on, so the reminder doesn't pop up twice" },
                NotificationChannel(Notes.FOCUS, "Focus timer finished", NotificationManager.IMPORTANCE_HIGH),
                NotificationChannel(Notes.FOCUS_RUN, "Focus timer running", NotificationManager.IMPORTANCE_LOW)
                    .apply { setShowBadge(false) },
            )
        )
        Water.refresh()
        Water.schedule(this) // alarms don't survive a force-stop; the next start re-arms them
        Focus.restore(this)
    }
}
