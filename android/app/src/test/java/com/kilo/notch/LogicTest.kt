package com.kilo.notch

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.LocalDateTime
import java.time.ZoneId
import kotlin.math.abs

class LogicTest {
    private val zone = ZoneId.of("Asia/Jakarta")
    private fun at(d: Int, h: Int, m: Int = 0) = LocalDateTime.of(2026, 10, d, h, m).atZone(zone).toInstant().toEpochMilli()
    private fun next(now: Long, lastSip: Long, lastNag: Long, every: Int = 60, from: Int = 9, to: Int = 21, goalMet: Boolean = false) =
        Water.next(now, lastSip, lastNag, every, from, to, goalMet, zone)

    @Test fun reminderComesOneIntervalAfterTheLastGlass() =
        assertEquals(at(21, 11, 30), next(at(21, 10, 40), lastSip = at(21, 10, 30), lastNag = at(21, 9, 0)))

    @Test fun aNudgeAlsoResetsTheInterval() =
        assertEquals(at(21, 12, 15), next(at(21, 11, 20), lastSip = at(21, 9, 0), lastNag = at(21, 11, 15)))

    @Test fun beforeActiveHoursWaitsForTheStart() =
        assertEquals(at(22, 9), next(at(22, 6, 30), lastSip = at(21, 20, 0), lastNag = at(21, 20, 30)))

    @Test fun afterActiveHoursMovesToTomorrowMorning() =
        assertEquals(at(22, 9), next(at(21, 20, 30), lastSip = at(21, 20, 10), lastNag = 0))

    @Test fun goalReachedWaitsForTomorrow() =
        assertEquals(at(22, 9), next(at(21, 14, 0), lastSip = at(21, 13, 0), lastNag = 0, goalMet = true))

    @Test fun overdueAfterThePhoneWasOffNudgesRightAway() {
        val now = at(21, 15, 0)
        val t = next(now, lastSip = at(21, 10, 0), lastNag = at(21, 11, 0))
        assertTrue(t > now && t - now <= 10_000)
    }

    @Test fun untilMidnightWorks() =
        assertEquals(at(21, 23, 30), next(at(21, 22, 40), lastSip = at(21, 22, 30), lastNag = 0, to = 24))

    @Test fun neverInThePast() {
        val now = at(21, 12, 0)
        assertTrue(next(now, 0, 0) > now)
    }

    // ---------- palette ----------

    private fun hue(c: Int): Float {
        val r = (c shr 16 and 255) / 255f; val g = (c shr 8 and 255) / 255f; val b = (c and 255) / 255f
        val max = maxOf(r, g, b); val d = max - minOf(r, g, b)
        if (d == 0f) return 0f
        val h = when (max) { r -> 60 * (((g - b) / d) % 6); g -> 60 * ((b - r) / d + 2); else -> 60 * ((r - g) / d + 4) }
        return (h + 360) % 360
    }
    private fun dist(a: Float, b: Float) = abs(a - b).let { minOf(it, 360 - it) }
    private fun sat(c: Int): Float {
        val r = c shr 16 and 255; val g = c shr 8 and 255; val b = c and 255
        val max = maxOf(r, g, b); return if (max == 0) 0f else (max - minOf(r, g, b)) / max.toFloat()
    }

    @Test fun rainbowCoverKeepsTwoDistinctColors() {
        val (a, b) = Kolors.palette(IntArray(576) { Kolors.hsl(it % 24 * 15f, 0.9f, 0.5f) })
        assertTrue(sat(a) > 0.4f)
        assertTrue(dist(hue(a), hue(b)) >= 45)
    }

    @Test fun twoColorCoverGivesBoth() {
        val (a, b) = Kolors.palette(IntArray(576) { if (it % 24 < 16) 0xFF1E50DC.toInt() else 0xFFF08C1E.toInt() })
        assertTrue(dist(hue(a), 225f) < 20)
        assertTrue(dist(hue(b), 32f) < 20)
    }

    @Test fun greyCoverStaysGrey() {
        val (a, _) = Kolors.palette(IntArray(576) { val v = it % 24 * 10; (0xFF shl 24) or (v shl 16) or (v shl 8) or v })
        assertTrue(sat(a) < 0.05f)
    }

    // ---------- music position ----------

    @Test fun positionRunsOnWhilePlayingAndStopsAtTheEnd() {
        val m = Music.Now("t", "a", null, null, playing = true, pos = 10_000, at = 1_000, speed = 1f, duration = 12_000)
        assertEquals(11_000, m.position(now = 2_000))
        assertEquals(12_000, m.position(now = 9_000))
        assertEquals(10_000, m.copy(playing = false).position(now = 9_000))
    }

    @Test fun clockFormat() {
        assertEquals("25:00", clock(25 * 60_000L))
        assertEquals("00:01", clock(1))
    }
}
