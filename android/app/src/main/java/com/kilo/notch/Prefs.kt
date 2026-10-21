package com.kilo.notch

import android.content.Context
import android.content.SharedPreferences
import kotlinx.coroutines.flow.MutableStateFlow
import kotlin.properties.ReadWriteProperty
import kotlin.reflect.KProperty

/** Everything Kilo remembers, in one SharedPreferences file. [changed] ticks on every write so screens refresh. */
object Prefs {
    private lateinit var sp: SharedPreferences
    val changed = MutableStateFlow(0)

    fun init(c: Context) {
        if (!::sp.isInitialized) sp = c.applicationContext.getSharedPreferences("kilo", Context.MODE_PRIVATE)
    }

    // colors
    var hue by float(265f)
    var spread by float(50f)
    var sat by float(0.9f)
    var albumColors by bool(true)

    // island
    var showKilo by bool(true)
    var notifications by bool(true)
    var privateOnLock by bool(true)
    var music by bool(true)
    var hideLandscape by bool(true)
    var hideFullscreen by bool(true)
    var size by float(1f)     // 0.8..1.3
    var offsetX by float(0f)  // dp: nudges the island onto the camera
    var offsetY by float(0f)
    var muted by stringSet()
    var seenApps by stringSet()

    // focus timer
    var focusMin by int(25)
    var breakMin by int(5)
    var focusRunning by bool(false)
    var focusBreak by bool(false)
    var focusEnd by long(0L)
    var focusLeft by long(-1L) // ms left while paused; -1 = a fresh session

    // water
    var waterOn by bool(true)
    var waterGoal by int(1500)
    var waterCup by int(250)
    var waterEvery by int(60)
    var waterFrom by int(9)
    var waterTo by int(21)
    var sips by string("")      // "epochMs:ml;epochMs:ml", last 7 days
    var lastNag by long(0L)
    var waterNext by long(0L)   // when the next reminder is scheduled, for the settings screen
    var thirsty by bool(false)

    private fun float(d: Float) = pref({ getFloat(it, d) }) { k, v -> putFloat(k, v) }
    private fun int(d: Int) = pref({ getInt(it, d) }) { k, v -> putInt(k, v) }
    private fun long(d: Long) = pref({ getLong(it, d) }) { k, v -> putLong(k, v) }
    private fun bool(d: Boolean) = pref({ getBoolean(it, d) }) { k, v -> putBoolean(k, v) }
    private fun string(d: String) = pref({ getString(it, d) ?: d }) { k, v -> putString(k, v) }
    private fun stringSet() = pref({ getStringSet(it, emptySet())!!.toSet() }) { k, v -> putStringSet(k, v) }

    private fun <T> pref(get: SharedPreferences.(String) -> T, put: SharedPreferences.Editor.(String, T) -> Unit) =
        object : ReadWriteProperty<Any?, T> {
            override fun getValue(thisRef: Any?, property: KProperty<*>): T = sp.get(property.name)
            override fun setValue(thisRef: Any?, property: KProperty<*>, value: T) {
                sp.edit().apply { put(property.name, value) }.apply()
                changed.value++
            }
        }
}
